using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>DELETE /api/v1/inspecciones/{id}/hallazgos/{hallazgoId}</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1e §9.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class EliminarHallazgoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 6, 10, 0, 0, TimeSpan.FromHours(-5));

    private async Task<(Guid InspeccionId, Guid HallazgoId)> SembrarInspeccionConHallazgo(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
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
                MedicionOrigenId: null,  // Slice 1i: null para Manual (backward compat)
                ParteEquipoId: 77,
                ActividadId: null,
                ActividadDescripcion: "Revisión visual de manguera",
                NovedadTecnica: "Manguera con desgaste leve superficial",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "rmartinez",
                RegistradoEn: CapturadoEn));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId);
    }

    // ── Happy path E2E — §6.1 via DELETE ────────────────────────────────────

    [Fact]
    public async Task DELETE_hallazgo_happy_path_responde_204_NoContent()
    {
        // Given: inspección en EnEjecucion con hallazgo activo
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 15001);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}")
        {
            Content = JsonContent.Create(new { motivo = "Registrado por error — parte incorrecta" })
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 204 No Content — spec slice 1e §9
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── PRE-B2: segundo intento devuelve 422 PRE-B2-ELIMINADO ───────────────

    [Fact]
    public async Task DELETE_hallazgo_segundo_intento_responde_422_PRE_B2_ELIMINADO()
    {
        // Given: hallazgo ya eliminado en un primer DELETE exitoso
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 15002);
        var client = factory.CreateClient();

        var primerRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}")
        {
            Content = JsonContent.Create(new { motivo = "Primer intento" })
        };
        primerRequest.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        (await client.SendAsync(primerRequest)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // When: segundo intento con X-Client-Command-Id distinto
        var segundoRequest = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}")
        {
            Content = JsonContent.Create(new { motivo = "Segundo intento" })
        };
        segundoRequest.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var respuesta = await client.SendAsync(segundoRequest);

        // Then: 422 PRE-B2-ELIMINADO — el cliente lo trata como éxito silencioso (spec §7 y §9)
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-B2-ELIMINADO");
    }

    // ── PRE-F: InspeccionId inexistente devuelve 404 ─────────────────────────

    [Fact]
    public async Task DELETE_hallazgo_inspeccion_inexistente_responde_404_PRE_F()
    {
        // Given: InspeccionId que no existe en Marten
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/inspecciones/{Guid.NewGuid()}/hallazgos/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { motivo = "Prueba" })
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 404 con codigoError PRE-F
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-F");
    }

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
