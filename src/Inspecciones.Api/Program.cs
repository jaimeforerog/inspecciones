using Marten;
using Oakton;
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

        // Solo crear/migrar el schema en Development. En prod los DDL se aplican via pipeline.
        if (isDevelopment)
        {
            options.AutoCreateSchemaObjects = Weasel.Core.AutoCreate.CreateOrUpdate;
        }

        // Proyecciones inline corren sincrónicamente por default y registran read models
        // en la misma transacción del Append. Cada slice agrega las suyas al definirlas.
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

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
