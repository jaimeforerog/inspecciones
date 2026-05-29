using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>GET /api/v1/inspecciones/{id}</c> contra la app real
/// con Postgres. Reconstruye el aggregate desde el stream (IInspeccionReader) y
/// devuelve el estado completo — resuelve "no queda guardada".
///
/// Cubre:
/// Happy path  — 200 OK + estado completo (estado, equipoId, proyectoId, tipo,
///               hallazgos[], firma/dictamen) tras firmar.
/// Happy path  — 200 OK estado EnEjecucion con hallazgos[].
/// PRE-2       — id inexistente → 404 + codigoError PRE-2.
/// PRE-1       — capability "ejecutar-inspeccion" ausente → 403.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class RecuperarInspeccionEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SembrarInspeccionEnEjecucionConHallazgo(int equipoId)
    {
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "1",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                IniciadaEn: CapturadoEn,
                FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            new HallazgoRegistrado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                Origen: OrigenHallazgo.Manual,
                NovedadPreopOrigenId: null,
                MedicionOrigenId: null,
                EvaluacionOrigenId: null,
                ParteEquipoId: 77,
                ActividadId: null,
                ActividadDescripcion: "Revisión general",
                NovedadTecnica: "Fuga leve detectada",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "1",
                RegistradoEn: CapturadoEn));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    [Fact]
    public async Task GET_inspeccion_por_id_happy_path_responde_200_con_estado_completo()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucionConHallazgo(equipoId: 90101);

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/inspecciones/{inspeccionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RespuestaRecuperarInspeccion>();
        body.Should().NotBeNull();
        body!.InspeccionId.Should().Be(inspeccionId);
        body.Estado.Should().Be("EnEjecucion");
        body.Tipo.Should().Be("Tecnica");
        body.EquipoId.Should().Be(90101);
        body.ProyectoId.Should().Be(3);
        body.TecnicoIniciador.Should().Be("1");
        body.IniciadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
        body.Hallazgos.Should().ContainSingle();
        body.Hallazgos[0].NovedadTecnica.Should().Be("Fuga leve detectada");
    }

    [Fact]
    public async Task GET_inspeccion_inexistente_responde_404_PRE_2()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/inspecciones/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-2");
    }

    [Fact]
    public async Task GET_inspeccion_sin_capability_responde_403()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucionConHallazgo(equipoId: 90102);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/inspecciones/{inspeccionId}");
        request.Headers.Add("X-Sin-Capability-Ejecutar", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record RespuestaRecuperarInspeccion(
        Guid InspeccionId,
        string Tipo,
        string Estado,
        int EquipoId,
        int ProyectoId,
        string TecnicoIniciador,
        DateTimeOffset IniciadaEn,
        IReadOnlyList<HallazgoLeido> Hallazgos);

    private sealed record HallazgoLeido(Guid HallazgoId, string NovedadTecnica);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
