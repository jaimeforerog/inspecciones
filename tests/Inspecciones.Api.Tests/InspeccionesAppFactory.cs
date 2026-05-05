using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Test fixture que arranca un Postgres en Testcontainers y monta el host de la
/// API contra él. Se reusa entre tests dentro del mismo collection para evitar
/// el costo de 5-10s del container por cada test.
/// </summary>
public sealed class InspeccionesAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("inspecciones")
        .WithUsername("inspecciones")
        .WithPassword("inspecciones-test-only")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString()
            });
        });
    }
}

[CollectionDefinition(nameof(InspeccionesAppCollection))]
public sealed class InspeccionesAppCollection : ICollectionFixture<InspeccionesAppFactory>;
