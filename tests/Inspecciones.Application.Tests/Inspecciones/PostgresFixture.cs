using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Fixture compartido entre tests del handler que requieren un Marten real.
/// Levanta Postgres en Testcontainers una sola vez por colección (5-10s) y
/// expone <see cref="IDocumentStore"/>. Cada test crea su propio
/// <c>IDocumentSession</c> aislado.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("inspecciones")
        .WithUsername("inspecciones")
        .WithPassword("inspecciones-test-only")
        .Build();

    private ServiceProvider _services = null!;

    public IDocumentStore Store => _services.GetRequiredService<IDocumentStore>();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var collection = new ServiceCollection();
        collection.AddMarten((StoreOptions opts) =>
        {
            opts.Connection(_postgres.GetConnectionString());
            opts.DatabaseSchemaName = "inspecciones";
            opts.AutoCreateSchemaObjects = Weasel.Core.AutoCreate.CreateOrUpdate;
        });
        _services = collection.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(nameof(PostgresFixtureCollection))]
public sealed class PostgresFixtureCollection : ICollectionFixture<PostgresFixture>;
