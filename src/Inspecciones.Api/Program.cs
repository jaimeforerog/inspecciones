using Inspecciones.Api.Inspecciones;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Marten;
using Oakton;
using Scalar.AspNetCore;
using Wolverine;
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

        // JSON serializer — usa el default de Marten (Newtonsoft.Json). El detalle de
        // configuración del serializer (System.Text.Json + casing + enum como string) se
        // cierra en un slice posterior cuando emerja necesidad concreta.

        // PKs de catálogos ERP usan nombre de campo semántico (no "Id") — registrar identidad explícita.
        options.Schema.For<EquipoLocal>().Identity(x => x.EquipoId);
        options.Schema.For<RutinaTecnicaLocal>().Identity(x => x.RutinaId);
        options.Schema.For<RepuestoLocal>().Identity(x => x.SkuId);
        options.Schema.For<RutinaMonitoreoLocal>().Identity(x => x.RutinaMonitoreoId);

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

    // Persistir mensajes pendientes (outbox) para resiliencia ante caídas del proceso.
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();
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

// Endpoint informativo en root — útil para diagnóstico rápido.
app.MapGet("/", () => Results.Ok(new
{
    nombre = "Inspecciones API",
    estado = "ok",
    descripcion = "Módulo de inspecciones técnicas Sinco MYE"
}));

await app.RunOaktonCommands(args);

// Marker class para que los tests E2E puedan usar WebApplicationFactory&lt;Program&gt;.


public partial class Program;
