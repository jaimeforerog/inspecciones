using Inspecciones.Api.Tests.Auth;
using Inspecciones.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Test fixture que arranca un Postgres y monta el host de la API contra él. Se reusa
/// entre tests dentro del mismo collection para evitar el costo de 5-10s del container
/// por cada test.
///
/// Modos de arranque:
///
/// 1. <b>Local Postgres</b> (preferido en dev / offline / sin Docker): define la variable
///    de entorno <c>POSTGRES_TEST_CONNSTRING</c> apuntando a una DB dedicada — la fixture
///    no usa Testcontainers, conecta directo. Ejemplo:
///    <c>Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test</c>.
///    La DB se crea si no existe (CREATE DATABASE) usando la connstring admin contra
///    la DB <c>postgres</c>. El schema <c>inspecciones</c> se dropea entre corridas para
///    aislar y resolver <c>DocumentAlreadyExistsException</c> por residuo de seeds.
///
/// 2. <b>Testcontainers</b> (fallback, CI / cuando Docker está disponible): si la variable
///    de entorno no existe, arranca un container Postgres efímero. Cada corrida es limpia.
/// </summary>
public sealed class InspeccionesAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string EnvVarConnString = "POSTGRES_TEST_CONNSTRING";

    private readonly PostgreSqlContainer? _postgres;
    private string? _connectionString;

    public InspeccionesAppFactory()
    {
        var envConnString = Environment.GetEnvironmentVariable(EnvVarConnString);
        if (string.IsNullOrWhiteSpace(envConnString))
        {
            // Modo Testcontainers (CI o dev con Docker)
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("inspecciones")
                .WithUsername("inspecciones")
                .WithPassword("inspecciones-test-only")
                .Build();
        }
        else
        {
            // Modo local — connection string explícita
            _connectionString = envConnString;
        }
    }

    public async Task InitializeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.StartAsync();
            _connectionString = _postgres.GetConnectionString();
            return;
        }

        // Modo local: asegurar que la DB existe + dropear schemas Marten (inspecciones +
        // schemas auxiliares de Wolverine).
        await EnsureDatabaseExistsAsync(_connectionString!);
        await DropMartenSchemasAsync(_connectionString!);
    }

    public new async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    /// <summary>
    /// Override del <see cref="ISessionService"/> registrado en DI antes de
    /// crear el cliente HTTP — usado por <c>SessionServicePipelineTests</c>
    /// (spec slice mt-1 §6 + §12.B) para inyectar instancias específicas que
    /// fuerzan capabilities vacías, lanzan <c>ClaimRequeridaException</c>, etc.
    ///
    /// Devuelve un <see cref="WebApplicationFactory{Program}"/> nuevo que
    /// comparte la misma fixture pero con el servicio sustituido — siguiendo
    /// el patrón estándar de <c>WebApplicationFactory.WithWebHostBuilder</c>.
    /// </summary>
    public WebApplicationFactory<Program> WithSessionService(ISessionService session)
        => WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionService>();
                services.AddSingleton(session);
            });
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Env Test: SincoMiddlewareSessionService NO se registra (Program.cs lo gatea
        // por env); el middleware corporativo MiddlewareAuthorizationToken tampoco
        // se monta. La fixture inyecta TestHeaderAwareSessionService por default.
        // Decisión spec mt-1 §12.B firmada.
        builder.UseEnvironment("Test");

        // UseSetting tiene prioridad sobre appsettings.Development.json (que puede tener
        // una connection string apuntando a la DB de desarrollo `inspecciones`, distinta
        // de la DB de tests).
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                // BaseUrl placeholder — los tests no realizan llamadas reales al ERP
                // (el listener vive en Wolverine local queue + outbox; los tests que
                // realmente disparan el adapter HTTP usan WireMock independiente).
                // Sin valor, IMaquinariaErpClient explota al construir HttpClient.
                ["Maquinaria:BaseUrl"] = "http://wiremock-placeholder.test/api/v4/Maquinaria"
            });
        });

        // Wolverine intenta loguear al Windows EventLog durante DisposeAsync cuando drena
        // colas durables, lo cual falla con "SeSecurityPrivilege" si el usuario de test no
        // tiene permisos al EventLog. El error se contabiliza como test failure por VSTest.
        // Eliminamos el EventLogLoggerProvider de la colección de servicios — más robusto
        // que ConfigureLogging porque también captura el provider agregado por el host
        // genérico en Windows.
        builder.ConfigureServices(services =>
        {
            var eventLogDescriptors = services
                .Where(d => d.ServiceType == typeof(ILoggerProvider) &&
                            (d.ImplementationType?.FullName?.Contains("EventLog", StringComparison.Ordinal) ?? false))
                .ToList();
            foreach (var d in eventLogDescriptors)
            {
                services.Remove(d);
            }

            // Reemplazar TimeProvider.System por FakeTimeProvider con timestamp fijo, para que
            // los tests E2E que asertan BeCloseTo(CapturadoEn, ...) sean deterministas
            // independientemente del wall-clock real al momento de correr.
            //
            // CapturadoEn canónico de los tests: 2026-05-08T15:00:00Z.
            //
            // Cierra FU-37. Causa raíz original mal descrita: el bug NO es DateTime.UtcNow en
            // handlers (auditado — los handlers usan TimeProvider correctamente). El bug es
            // la ausencia del swap en esta fixture.
            var timeDescriptors = services
                .Where(d => d.ServiceType == typeof(TimeProvider))
                .ToList();
            foreach (var d in timeDescriptors)
            {
                services.Remove(d);
            }
            var fakeTime = new FakeTimeProvider(
                new DateTimeOffset(2026, 5, 8, 15, 0, 0, TimeSpan.Zero));
            services.AddSingleton<TimeProvider>(fakeTime);

            // ISessionService default en env Test: header-aware backward-compat con
            // tests legacy (spec mt-1 — TestHeaderAwareSessionService).
            // Los tests del slice mt-1 lo overridean vía WithSessionService(fake).
            services.RemoveAll<ISessionService>();
            services.AddScoped<ISessionService, TestHeaderAwareSessionService>();
        });
    }

    /// <summary>
    /// Si la DB objetivo no existe, la crea conectándose a la DB <c>postgres</c>
    /// (la admin DB por defecto). Idempotente — si ya existe, ignora el error.
    /// </summary>
    private static async Task EnsureDatabaseExistsAsync(string targetConnString)
    {
        var targetBuilder = new NpgsqlConnectionStringBuilder(targetConnString);
        var targetDatabase = targetBuilder.Database
            ?? throw new InvalidOperationException(
                $"La variable {EnvVarConnString} debe incluir 'Database='.");

        // Conectar a 'postgres' (admin DB) para poder ejecutar CREATE DATABASE.
        var adminBuilder = new NpgsqlConnectionStringBuilder(targetConnString)
        {
            Database = "postgres"
        };

        await using var conn = new NpgsqlConnection(adminBuilder.ConnectionString);
        await conn.OpenAsync();

        // Check existence first — CREATE DATABASE no soporta IF NOT EXISTS en Postgres.
        await using (var checkCmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", conn))
        {
            checkCmd.Parameters.AddWithValue("name", targetDatabase);
            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists is not null)
            {
                return;
            }
        }

        await using var createCmd = new NpgsqlCommand(
            $"CREATE DATABASE \"{targetDatabase}\"", conn);
        await createCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Dropea los schemas creados por Marten y Wolverine para aislar la corrida.
    /// Marten / Wolverine los recrearán al arrancar el host (AutoCreateSchemaObjects en Development).
    ///
    /// Schemas a dropear:
    /// - <c>inspecciones</c>: read models + event store (configurado en Program.cs).
    /// - <c>public</c>: Wolverine outbox / control queues por default.
    /// Para no romper Postgres, dropeamos solo objetos de Wolverine en <c>public</c> (los suyos
    /// arrancan con prefijos conocidos), pero como solución simple y robusta dropeamos el
    /// schema inspecciones completo (Marten lo recrea) y trunca tablas wolverine en public.
    /// </summary>
    private static async Task DropMartenSchemasAsync(string connString)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // 1. Dropear schema inspecciones completo (read models + event store).
        await using (var dropSchema = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS inspecciones CASCADE;", conn))
        {
            await dropSchema.ExecuteNonQueryAsync();
        }

        // 2. Dropear tablas de Wolverine en schema public (si existen).
        //    Wolverine crea tablas con prefijo wolverine_* en el schema configurado (public por default).
        await using (var dropWolverine = new NpgsqlCommand(@"
            DO $$
            DECLARE r record;
            BEGIN
                FOR r IN (
                    SELECT tablename FROM pg_tables
                    WHERE schemaname = 'public' AND tablename LIKE 'wolverine_%'
                ) LOOP
                    EXECUTE 'DROP TABLE IF EXISTS public.' || quote_ident(r.tablename) || ' CASCADE;';
                END LOOP;
            END $$;", conn))
        {
            await dropWolverine.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(nameof(InspeccionesAppCollection))]
public sealed class InspeccionesAppCollection : ICollectionFixture<InspeccionesAppFactory>;
