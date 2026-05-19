using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Inspecciones.Api.Catalogos;
using Inspecciones.Api.Inspecciones;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using Marten;
using Oakton;
using Scalar.AspNetCore;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// Marten — event store + read models + projections (sobre PostgreSQL).
// ─────────────────────────────────────────────────────────────────────────────
var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Falta la connection string 'Postgres'. Define ConnectionStrings:Postgres en appsettings " +
        "(Development) o como variable de entorno (Container Apps).");

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddMarten((StoreOptions options) =>
    {
        options.Connection(connectionString);
        options.DatabaseSchemaName = "inspecciones";

        // PKs de catálogos ERP usan nombre de campo semántico (no "Id") — registrar identidad explícita.
        options.Schema.For<EquipoLocal>().Identity(x => x.EquipoId);
        options.Schema.For<RutinaTecnicaLocal>().Identity(x => x.RutinaId);
        options.Schema.For<RepuestoLocal>().Identity(x => x.SkuId);
        options.Schema.For<RutinaMonitoreoLocal>().Identity(x => x.RutinaMonitoreoId);

        // Identidad del aggregate Inspeccion — requerida por Marten 7.40 para AggregateStreamAsync<Inspeccion>.
        // InspeccionId sigue la convención {ClassName}Id pero se registra explícitamente para garantizar
        // que el aggregate projection se compile correctamente.
        options.Schema.For<Inspecciones.Domain.Inspecciones.Inspeccion>().Identity(x => x.InspeccionId);

        // Solo crear/migrar el schema en Development. En prod los DDL se aplican via pipeline.
        if (isDevelopment)
        {
            options.AutoCreateSchemaObjects = Weasel.Core.AutoCreate.CreateOrUpdate;
        }



        // Proyecciones inline corren sincrónicamente por default y registran read models
        // en la misma transacción del Append. Cada slice agrega las suyas al definirlas.

        // FU-13 — InspeccionAbiertaPorEquipoView: migrada de session.Insert directo (slice 1b)
        // a EventProjection inline (slice 1g). Maneja InspeccionIniciada_v1 (upsert),
        // InspeccionFirmada_v1 (delete) e InspeccionCancelada_v1 (delete).
        options.Projections.Add<InspeccionAbiertaPorEquipoProjection>(Marten.Events.Projections.ProjectionLifecycle.Inline);
    })
    // Wolverine outbox transaccional integrado con Marten — persistencia atómica
    // de eventos + mensajes a despachar (regla CLAUDE.md, ADR-006).
    .IntegrateWithWolverine();

// ─────────────────────────────────────────────────────────────────────────────
// Wolverine — handlers, sagas, outbox.
// ─────────────────────────────────────────────────────────────────────────────
builder.Host.UseWolverine(opts =>
{
    // Auto-discovery de handlers en todos los proyectos referenciados.
    opts.Discovery.IncludeAssembly(typeof(Inspecciones.Application.AssemblyMarker).Assembly);
    // Listeners de integración ERP (p. ej. DescartarNovedadPreopErpListener) viven en Infrastructure.
    opts.Discovery.IncludeAssembly(typeof(DescartarNovedadPreopErpListener).Assembly);

    // Persistir mensajes pendientes (outbox) para resiliencia ante caídas del proceso.
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();

    // ── Política de resiliencia ADR-006 §16 para llamadas a Maquinaria_V4 ──────
    //
    // 5xx y timeout (transitorio): reintento con backoff progresivo.
    //   4 reintentos (5s → 30s → 2m → 10m); si agota → dead-letter.
    opts.Policies
        .OnException<MaquinariaErpException>(
            ex => ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500,
            "5xx retryable — ERP no disponible temporalmente")
        .ScheduleRetry(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10))
        .Then.MoveToErrorQueue();

    // 4xx (incluyendo 409 código desconocido): error permanente, no reintentar (INV-L3).
    //   El caso 409 YA_CERRADO se captura en el listener antes de llegar aquí.
    opts.Policies
        .OnException<MaquinariaErpException>(
            ex => ex.StatusCode.HasValue
                  && (int)ex.StatusCode.Value >= 400
                  && (int)ex.StatusCode.Value < 500,
            "4xx permanente — dead-letter inmediato (INV-L3)")
        .MoveToErrorQueue();

    // Evento malformado (PRE-L1): dead-letter inmediato sin reintentos.
    opts.Policies
        .OnException<ArgumentException>()
        .MoveToErrorQueue();
});

// ─────────────────────────────────────────────────────────────────────────────
// SignalR — push real-time hacia clientes PWA (ADR-005).
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─────────────────────────────────────────────────────────────────────────────
// Health checks — /health/live y /health/ready.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ─────────────────────────────────────────────────────────────────────────────
// OpenAPI — solo en Development.
// ─────────────────────────────────────────────────────────────────────────────
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

// ─────────────────────────────────────────────────────────────────────────────
// TimeProvider — inyectado por la regla del CLAUDE.md (prohibido DateTime.UtcNow
// en dominio).
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);

// ─────────────────────────────────────────────────────────────────────────────
// Handlers de comandos — registrados como Scoped para recibir IDocumentSession.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IniciarInspeccionHandler>();
builder.Services.AddScoped<RegistrarHallazgoHandler>();
builder.Services.AddScoped<ActualizarHallazgoHandler>();
builder.Services.AddScoped<EliminarHallazgoHandler>();
builder.Services.AddScoped<AsignarRepuestoHandler>();
builder.Services.AddScoped<FirmarInspeccionHandler>();
builder.Services.AddScoped<IniciarInspeccionMonitoreoHandler>();
builder.Services.AddScoped<RegistrarMedicionHandler>();
builder.Services.AddScoped<RegistrarEvaluacionCualitativaHandler>();
builder.Services.AddScoped<OmitirItemMonitoreoHandler>();
builder.Services.AddScoped<GenerarOTHandler>();
builder.Services.AddScoped<RechazarGenerarOTHandler>();
builder.Services.AddScoped<CancelarInspeccionHandler>();
builder.Services.AddScoped<DescartarNovedadPreopHandler>();
builder.Services.AddScoped<ActualizarRepuestoHandler>();

// ─────────────────────────────────────────────────────────────────────────────
// Maquinaria_V4 — adapter HTTP de lectura para sembrar el catálogo local
// (EquipoLocal, RutinaTecnicaLocal, ParteEquipoLocal) desde el ERP SincoMyE.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.Configure<MaquinariaErpOptions>(
    builder.Configuration.GetSection(MaquinariaErpOptions.SectionName));

builder.Services.AddHttpClient<IMaquinariaErpClient, MaquinariaErpClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaquinariaErpOptions>>().Value;
    if (string.IsNullOrWhiteSpace(opts.BaseUrl))
    {
        throw new InvalidOperationException(
            "Falta configurar 'Maquinaria:BaseUrl' (apuntando a http://host:puerto/api/v4/Maquinaria).");
    }

    var baseUrl = opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/";
    http.BaseAddress = new Uri(baseUrl);

    // Timeout consistente con ADR-006: hasta 30s antes de fallar al outbox.
    http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos > 0 ? opts.TimeoutSegundos : 30);

    if (!string.IsNullOrWhiteSpace(opts.JwtToken))
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.JwtToken);
    }
});

builder.Services.AddScoped<SincronizarEquipoDesdeErpHandler>();
builder.Services.AddScoped<SeedManualCatalogoHandler>();

// Sync de catálogos globales on-app-open (ADR-004, erp-4).
// MartenCatalogoSyncRepository recibe IDocumentStore (singleton) para abrir sesiones
// independientes por catálogo — evita la race condition de IDocumentSession compartida
// en Task.WhenAll (hallazgo #1 review erp-4).
builder.Services.AddSingleton<ICatalogoSyncRepository, MartenCatalogoSyncRepository>();
builder.Services.AddScoped<SincronizarCatalogosHandler>();

// Puerto de lectura del aggregate Inspeccion — usado por SincronizarDictamenVigenteListener (erp-3).
builder.Services.AddScoped<IInspeccionReader, MartenInspeccionReader>();

// ─────────────────────────────────────────────────────────────────────────────
// JSON serializer — Minimal APIs: enums como string en request y response bodies.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Endpoints de slices — registrados por feature folder.
app.MapInspeccionesEndpoints();
app.MapCatalogosEndpoints();

// Endpoint informativo en root — útil para diagnóstico rápido.
app.MapGet("/", () => Results.Ok(new
{
    nombre = "Inspecciones API",
    estado = "ok",
    descripcion = "Módulo de inspecciones técnicas Sinco MYE"
}));

// Oakton CLI: solo activar cuando el primer argumento es un subcomando Oakton
// (palabra clave sin prefijo "--"). Cuando WebApplicationFactory arranca el host
// puede pasar flags como "--environment=Development" que no son subcomandos Oakton.
var oaktonCommand = args.Length > 0 && !args[0].StartsWith("--");
if (oaktonCommand)
{
    await app.RunOaktonCommands(args);
}
else
{
    await app.RunAsync();
}

// Marker class para que los tests E2E puedan usar WebApplicationFactory<Program>.

public partial class Program;
