using System.Net;
using System.Net.Http.Json;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Regresión del bug "500 al cancelar/firmar inspecciones anteriores al registro de
/// InspeccionResumenProjection". Reproduce un stream cuyo InspeccionIniciada_v1 está
/// committed pero NO tiene fila en InspeccionResumenView (se simula borrando la fila),
/// y verifica que un evento de ciclo de vida posterior se autocura (reconstruye desde
/// el stream) en lugar de tumbar la transacción con CreateDefault.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class InspeccionResumenAutocuradoTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    private async Task<Guid> SembrarEnEjecucionSinFilaResumen(int equipoId)
    {
        var inspeccionId = Guid.NewGuid();

        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
            session.Events.StartStream<Inspeccion>(inspeccionId,
                new InspeccionIniciada_v1(
                    InspeccionId: inspeccionId, Tipo: TipoInspeccion.Tecnica, EquipoId: equipoId,
                    RutinaId: 18, RutinaCodigo: "INSP. BULL.MOTOR", TecnicoIniciador: "1", ProyectoId: 3,
                    Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn), IniciadaEn: CapturadoEn,
                    FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
                    LecturaMedidorPrimario: null, LecturaMedidorSecundario: null));
            await session.SaveChangesAsync();
        }

        // Simula "stream anterior al registro de la proyección": la fila no existe.
        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
            session.Delete<InspeccionResumenView>(inspeccionId);
            await session.SaveChangesAsync();
        }

        return inspeccionId;
    }

    [Fact]
    public async Task POST_cancelar_inspeccion_sin_fila_resumen_responde_200_y_autocura()
    {
        const int equipoId = 90601;
        var inspeccionId = await SembrarEnEjecucionSinFilaResumen(equipoId);

        var client = factory.CreateClient();

        // La inspección no es visible antes (la fila fue borrada).
        var antes = await client.GetFromJsonAsync<RespuestaLista>($"/api/v1/inspecciones?equipoId={equipoId}");
        antes!.Total.Should().Be(0);

        // Cancelar: antes del fix esto daba 500 (CreateDefault). Ahora debe autocurar → 200.
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(new { motivo = "Equipo trasladado a otra obra sin previo aviso" })
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "el evento de ciclo de vida sobre un stream sin fila debe reconstruir, no lanzar 500");

        // La fila se autocuró con el estado correcto y queda visible/operada.
        var despues = await client.GetFromJsonAsync<RespuestaLista>($"/api/v1/inspecciones?equipoId={equipoId}");
        despues!.EquipoId.Should().Be(equipoId, "la inspección reaparece en el equipo consultado");
        despues.Total.Should().Be(1);
        var fila = despues.Inspecciones.Single();
        fila.InspeccionId.Should().Be(inspeccionId);
        fila.Estado.Should().Be("Cancelada");
        fila.MotivoCancelacion.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        // Campo base recuperado desde el stream (no desde el evento de cancelación):
        // prueba que la reconstrucción derivó la fila base del agregado, no de un default.
        fila.TecnicoIniciador.Should().Be("1");
    }

    private sealed record RespuestaLista(int EquipoId, int Total, IReadOnlyList<Fila> Inspecciones);

    private sealed record Fila(
        Guid InspeccionId,
        string Estado,
        string TecnicoIniciador,
        string? MotivoCancelacion);
}
