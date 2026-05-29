using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>GET /api/v1/inspecciones/activa?equipoId={int}</c>.
/// Lee la proyección inline <c>InspeccionAbiertaPorEquipoView</c> (misma lectura
/// que la idempotencia I-I1) — permite reentrar al flujo de un equipo con
/// inspección abierta.
///
/// Cubre:
/// Happy path — 200 OK + { equipoId, inspeccionId, tecnicoIniciador, iniciadaEn,
///              proyectoId, tipo }.
/// 404        — equipo sin inspección activa → codigoError SIN_INSPECCION_ACTIVA.
/// PRE-1      — capability "ejecutar-inspeccion" ausente → 403.
/// Ruteo      — "activa" no choca con {id:guid}.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class InspeccionActivaPorEquipoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SembrarInspeccionActiva(int equipoId, int proyectoId = 3)
    {
        var inspeccionId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();

        // La proyección inline InspeccionAbiertaPorEquipoView se materializa en la
        // misma transacción al appendear InspeccionIniciada_v1.
        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "1",
                ProyectoId: proyectoId,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                IniciadaEn: CapturadoEn,
                FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    [Fact]
    public async Task GET_activa_happy_path_responde_200_con_inspeccion_activa()
    {
        const int equipoId = 90201;
        var inspeccionId = await SembrarInspeccionActiva(equipoId);

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/inspecciones/activa?equipoId={equipoId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RespuestaActiva>();
        body.Should().NotBeNull();
        body!.EquipoId.Should().Be(equipoId);
        body.InspeccionId.Should().Be(inspeccionId);
        body.TecnicoIniciador.Should().Be("1");
        body.ProyectoId.Should().Be(3);
        body.Tipo.Should().Be("Tecnica");
        body.IniciadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GET_activa_equipo_sin_inspeccion_responde_404_SIN_INSPECCION_ACTIVA()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/inspecciones/activa?equipoId=90299");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("SIN_INSPECCION_ACTIVA");
    }

    [Fact]
    public async Task GET_activa_sin_capability_responde_403()
    {
        const int equipoId = 90202;
        await SembrarInspeccionActiva(equipoId);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/inspecciones/activa?equipoId={equipoId}");
        request.Headers.Add("X-Sin-Capability-Ejecutar", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record RespuestaActiva(
        int EquipoId,
        Guid InspeccionId,
        string TecnicoIniciador,
        DateTimeOffset IniciadaEn,
        int ProyectoId,
        string Tipo);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
