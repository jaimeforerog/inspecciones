using System.Net;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Test E2E de bootstrap de la API. Verifica que la app levanta correctamente
/// con Marten + Wolverine + SignalR + health checks contra un Postgres real
/// en Testcontainers. Si esto falla, el plumbing está roto y ningún slice
/// funciona.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
public class HealthChecksTests(InspeccionesAppFactory factory)
{
    [Fact]
    public async Task GET_health_live_responde_200()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_health_ready_responde_200()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_root_responde_200_con_metadata()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Inspecciones API");
    }
}
