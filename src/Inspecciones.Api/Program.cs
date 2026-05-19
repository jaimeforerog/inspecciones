using System.Net;
using System.Text.Json.Serialization;
using Inspecciones.Api.Catalogos;
using Inspecciones.Api.Inspecciones;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using Marten;
using Oakton;
using Scalar.AspNetCore;
using SincoSoft.MYE.Common.Middleware;
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

        // ── Multi-tenancy Conjoined (ADR-009, slice mt-2) ──────────────────────
        //
        // Cada documento y cada evento del event store gana una columna `tenant_id`
        // con índice. Las queries y los appends filtran/persisten implícitamente
        // por el tenant de la sesión (ver TenantedDocumentSessionFactory + Marten
        // session.LightweightSession(tenantId)).
        //
        // D5 firmada: TODOS los documentos son por-empresa (sin excepciones single-tenant).
        // D2: tenancy style = Conjoined (no schema-per-tenant ni DB-per-tenant).
        options.Policies.AllDocumentsAreMultiTenanted();
        options.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;

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

    // ── mt-4 FU-60: propagación del JWT entrante al envelope del outbox ────────
    //
    // ForwardAuthEnvelopeRule.Modify lee IncomingBearerCarrier (poblado por
    // CaptureBearerForOutboxMiddleware desde el header Authorization del
    // request HTTP entrante) y escribe X-Forwarded-Authorization en cada
    // envelope saliente. Los listeners tenant-aware (mt-3) consumen el header
    // del envelope y propagan el JWT al ERP via AmbientBearerTokenAccessor.
    //
    // Wolverine 3.13 expone ISubscriberConfiguration.CustomizeOutgoing(Action<Envelope>);
    // delegamos directamente al método Modify del rule (puro, sin estado).
    // AllSenders cubre senders no-locales; para el outbox local (Marten
    // transaccional), AllLocalQueues no expone CustomizeOutgoing en
    // IListenerConfiguration — verificable en green con el test §6.10.
    var forwardAuthRule = new ForwardAuthEnvelopeRule();
    opts.Policies.AllSenders(cfg => cfg.CustomizeOutgoing(forwardAuthRule.Modify));
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

// mt-3: el Bearer del request se propaga vía BearerTokenPropagationHandler
// (DelegatingHandler que consulta IBearerTokenAccessor en cada SendAsync).
// El JwtToken global (MaquinariaErpOptions.JwtToken) queda como service-account
// fallback — su rol cambió, no se elimina (ver MaquinariaErpOptions.cs y D-MT3-3).
builder.Services.AddTransient<BearerTokenPropagationHandler>();
builder.Services.AddScoped<HttpContextBearerTokenAccessor>();
builder.Services.AddScoped<AmbientBearerTokenAccessor>();
builder.Services.AddScoped<ServiceAccountBearerTokenAccessor>();
builder.Services.AddScoped<IBearerTokenAccessor, ChainedBearerTokenAccessor>();

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

    // mt-3 MT3-INV-4: NO setear DefaultRequestHeaders.Authorization aquí.
    // El BearerTokenPropagationHandler (registrado abajo) lo hace por request,
    // consultando IBearerTokenAccessor (HTTP → Ambient → ServiceAccount).
})
.AddHttpMessageHandler<BearerTokenPropagationHandler>();

builder.Services.AddScoped<SincronizarEquipoDesdeErpHandler>();
builder.Services.AddScoped<SeedManualCatalogoHandler>();

// Sync de catálogos globales on-app-open (ADR-004, erp-4) + per-empresa (mt-2 D5).
// MartenCatalogoSyncRepository recibe ITenantedDocumentSessionFactory (scoped) y abre
// sesiones independientes por catálogo via el factory — evita la race condition de
// IDocumentSession compartida en Task.WhenAll (hallazgo #1 review erp-4) y garantiza
// que cada sync queda discriminado por tenant_id (MT2-INV-3).
// Registro scoped porque depende del factory (que es scoped).
builder.Services.AddScoped<ICatalogoSyncRepository, MartenCatalogoSyncRepository>();
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

// ─────────────────────────────────────────────────────────────────────────────
// Identidad del host PWA (ADR-002 cerrado / ADR-009, spec slice mt-1).
//
// Env Test: registramos un fake que combina header-aware (backward-compat con
// tests legacy pre-mt-1) — los tests del slice mt-1 lo overridean vía
// InspeccionesAppFactory.WithSessionService(fake) para inyectar instancias
// específicas.
//
// Env Development/Production: SincoMiddlewareSessionService real que lee de
// MiddlewareAuthorizationToken.SessionVariables() del paquete corporativo.
// El middleware ASP.NET se registra abajo (UseMiddleware<MiddlewareAuthorizationToken>())
// para validar el JWT antes de que el endpoint acceda al puerto.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

if (!builder.Environment.IsEnvironment("Test"))
{
    builder.Services.AddScoped<ISessionService, SincoMiddlewareSessionService>();
}
// En env Test la fixture (InspeccionesAppFactory) registra la implementación
// (TestHeaderAwareSessionService por default, FakeSessionService override por test).

// ─────────────────────────────────────────────────────────────────────────────
// Tenanted Marten sessions (slice mt-2 — ADR-009).
//
// El puerto ITenantedDocumentSessionFactory abre IDocumentSession/IQuerySession
// con el tenant_id derivado de ISessionService.IdEmpresa. El IDocumentSession
// scoped que Marten registró via AddMarten() se sobrescribe con un factory
// delegate para que TODOS los handlers existentes (que reciben IDocumentSession
// por DI) reciban una sesión tenant-aware sin tocar sus constructores.
//
// MT2-INV-1: prohibido store.LightweightSession() directo en producción —
// usar siempre el puerto. Bypass legal: OpenSessionForTenant(tenantId) para
// listeners Wolverine que leen el envelope.TenantId.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ITenantedDocumentSessionFactory, TenantedDocumentSessionFactory>();
builder.Services.AddScoped<IDocumentSession>(
    sp => sp.GetRequiredService<ITenantedDocumentSessionFactory>().OpenSession());
builder.Services.AddScoped<IQuerySession>(
    sp => sp.GetRequiredService<ITenantedDocumentSessionFactory>().OpenQuerySession());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// ─────────────────────────────────────────────────────────────────────────────
// Pipeline de autenticación / identidad (ADR-002 cerrado, spec mt-1 §4 PRE-AUTH-*).
//
// Env Test: no se registra el middleware corporativo (el fake suple las claims
// sin necesidad de validar JWT — paridad con el proyecto Attachment).
//
// Env Development/Production: se monta MiddlewareAuthorizationToken antes de
// que cualquier endpoint acceda a ISessionService. El middleware valida el
// JWT (firma/issuer/exp) y popula SessionVariables() que el puerto lee.
//
// El handler global mapea ClaimRequeridaException → 401 con codigoError
// específico (PRE-AUTH-3, PRE-AUTH-4).
// ─────────────────────────────────────────────────────────────────────────────
if (!app.Environment.IsEnvironment("Test"))
{
    app.UseMiddleware<MiddlewareAuthorizationToken>();
}

// mt-4 FU-60: capturar Authorization del request HTTP entrante en
// IncomingBearerCarrier (AsyncLocal scope HTTP) para que ForwardAuthEnvelopeRule
// lo propague al envelope del outbox. Se monta DESPUÉS del middleware corporativo
// (que valida el JWT primero) pero ANTES de los endpoints y del handler global
// de ClaimRequeridaException — el carrier vive durante todo el scope del request.
app.UseMiddleware<CaptureBearerForOutboxMiddleware>();

// Handler global de ClaimRequeridaException — mapea a 401 con codigoError de la claim.
// Pattern: middleware inline que intercepta la excepción antes de que llegue al
// handler de errores genérico.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ClaimRequeridaException ex)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            codigoError = ex.CodigoError,
            mensaje = ex.Message
        });
    }
});

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// Endpoints de slices — registrados por feature folder.
// mt-4 MT4-INV-3: SessionLoggingScopeFilter abre un scope con IdEmpresa/IdUsuario
// alrededor de cada invocación de endpoint — observabilidad por tenant sin
// modificar los 15 endpoints individualmente.
var endpointsGroup = app.MapGroup(string.Empty)
    .AddEndpointFilter<SessionLoggingScopeFilter>();
endpointsGroup.MapInspeccionesEndpoints();
endpointsGroup.MapCatalogosEndpoints();

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
