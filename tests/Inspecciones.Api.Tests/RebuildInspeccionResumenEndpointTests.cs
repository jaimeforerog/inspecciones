using System.Net;
using System.Net.Http.Json;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Test E2E del endpoint <c>POST /api/v1/admin/proyecciones/inspeccion-resumen/rebuild</c>.
/// Reconstruye <c>InspeccionResumenView</c> desde el event store — el caso de uso es
/// poblar la data preexistente al despliegue de la proyección.
///
/// Estrategia: se siembra una inspección (la proyección inline crea la fila), se borra
/// la fila para simular "evento histórico sin proyección materializada", se verifica que
/// el listado queda vacío, se dispara el rebuild y se verifica que la fila reaparece.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class RebuildInspeccionResumenEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task POST_rebuild_repuebla_la_vista_resumen_desde_el_event_store()
    {
        const int equipoId = 90501;
        var inspeccionId = Guid.NewGuid();

        // Given: una inspección sembrada (la proyección inline materializa la fila).
        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
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
                    LecturaMedidorSecundario: null));
            await session.SaveChangesAsync();
        }

        // And: se borra la fila de la vista para simular data histórica sin proyección.
        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
            session.Delete<InspeccionResumenView>(inspeccionId);
            await session.SaveChangesAsync();
        }

        var client = factory.CreateClient();

        // Sanity: el listado queda vacío tras el borrado.
        var antes = await client.GetFromJsonAsync<RespuestaLista>(
            $"/api/v1/inspecciones?equipoId={equipoId}");
        antes!.Total.Should().Be(0, "se borró la fila de la proyección antes del rebuild");

        // When: rebuild.
        var rebuild = await client.PostAsync(
            "/api/v1/admin/proyecciones/inspeccion-resumen/rebuild", content: null);
        rebuild.StatusCode.Should().Be(HttpStatusCode.OK);

        // Then: la fila reaparece y el listado la incluye.
        var despues = await client.GetFromJsonAsync<RespuestaLista>(
            $"/api/v1/inspecciones?equipoId={equipoId}");
        despues!.Total.Should().Be(1, "el rebuild reconstruye la fila desde el event store");
        despues.Inspecciones.Should().ContainSingle()
            .Which.InspeccionId.Should().Be(inspeccionId);
    }

    private sealed record RespuestaLista(
        int EquipoId,
        int Total,
        IReadOnlyList<InspeccionResumen> Inspecciones);

    private sealed record InspeccionResumen(Guid InspeccionId, string Estado);
}
