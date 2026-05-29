using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Catalogos;
using Marten;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>GET /api/v1/equipos/{equipoId}/partes</c> — partes
/// para el rol técnico (reemplaza el workaround admin). Fuente: <c>EquipoLocal</c>
/// en Marten.
///
/// Cubre:
/// Happy path — 200 OK + { equipoId, partes: [{ parteEquipoId, parteCodigo, parteNombre }] }.
/// 404        — equipo no sincronizado → codigoError NO_SINCRONIZADO.
/// PRE-1      — capability "ejecutar-inspeccion" ausente → 403.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class ListarPartesEquipoEndpointTests(InspeccionesAppFactory factory)
{
    private async Task SembrarEquipoConPartes(int equipoId)
    {
        await using var session = factory.OpenSeedingSessionForDefaultTenant();

        session.Store(new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
            RutinaTecnicaId: 18,
            Partes: new List<ParteEquipoLocal>
            {
                new(77, "MANGUERA-HID", "Manguera hidráulica"),
                new(88, "SELLO-HID", "Sello hidráulico")
            }));

        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task GET_partes_happy_path_responde_200_con_partes()
    {
        const int equipoId = 90301;
        await SembrarEquipoConPartes(equipoId);

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/equipos/{equipoId}/partes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RespuestaPartes>();
        body.Should().NotBeNull();
        body!.EquipoId.Should().Be(equipoId);
        body.Partes.Should().HaveCount(2);
        body.Partes.Should().Contain(p => p.ParteEquipoId == 77 && p.ParteCodigo == "MANGUERA-HID");
        body.Partes.Should().Contain(p => p.ParteEquipoId == 88 && p.ParteNombre == "Sello hidráulico");
    }

    [Fact]
    public async Task GET_partes_equipo_no_sincronizado_responde_404_NO_SINCRONIZADO()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/equipos/90399/partes");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("NO_SINCRONIZADO");
    }

    [Fact]
    public async Task GET_partes_sin_capability_responde_403()
    {
        const int equipoId = 90302;
        await SembrarEquipoConPartes(equipoId);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/equipos/{equipoId}/partes");
        request.Headers.Add("X-Sin-Capability-Ejecutar", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record RespuestaPartes(int EquipoId, IReadOnlyList<ParteLeida> Partes);

    private sealed record ParteLeida(int ParteEquipoId, string ParteCodigo, string ParteNombre);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
