using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de lectura <see cref="Inspeccion.EstadoNovedadPreop"/> (slice 1q):
/// deriva Disponible | Importada | Descartada desde el stream del aggregate. Es la
/// fuente de verdad del campo <c>estado</c> del endpoint GET …/novedades-preop.
/// </summary>
public class EstadoNovedadPreopTests
{
    private const int Novedad1042 = 1042;

    private static NovedadPreopDescartada_v1 Descartada(int novedadId) =>
        new(InspeccionIdNueva, novedadId, "Cerrado desde Inspecciones", "ana.gomez", Ahora);

    private static object[] StreamConHallazgoPreop(int novedadId) => new object[]
    {
        EventoInspeccionIniciada(),
        HallazgoRegistradoEjemplo(
            hallazgoId: HallazgoG2,
            origen: OrigenHallazgo.PreOperacional,
            novedadPreopOrigenId: novedadId,
            accionRequerida: AccionRequerida.RequiereIntervencion,
            tipoFallaId: 3,
            causaFallaId: 12)
    };

    [Fact]
    public void Novedad_con_hallazgo_activo_preop_es_Importada()
    {
        var agg = Inspeccion.Reconstruir(StreamConHallazgoPreop(Novedad1042));

        agg.EstadoNovedadPreop(Novedad1042).Should().Be(EstadoNovedadImportacion.Importada);
    }

    [Fact]
    public void Novedad_descartada_es_Descartada()
    {
        var agg = Inspeccion.Reconstruir(new object[] { EventoInspeccionIniciada(), Descartada(Novedad1042) });

        agg.EstadoNovedadPreop(Novedad1042).Should().Be(EstadoNovedadImportacion.Descartada);
    }

    [Fact]
    public void Novedad_sin_hallazgo_ni_descarte_es_Disponible()
    {
        var agg = Inspeccion.Reconstruir(new object[] { EventoInspeccionIniciada() });

        agg.EstadoNovedadPreop(Novedad1042).Should().Be(EstadoNovedadImportacion.Disponible);
    }

    [Fact]
    public void Novedad_cuyo_hallazgo_fue_eliminado_vuelve_a_Disponible()
    {
        var stream = StreamConHallazgoPreop(Novedad1042)
            .Append(HallazgoEliminadoEjemplo(hallazgoId: HallazgoG2))
            .ToArray();
        var agg = Inspeccion.Reconstruir(stream);

        agg.EstadoNovedadPreop(Novedad1042).Should().Be(EstadoNovedadImportacion.Disponible,
            "un hallazgo eliminado no cuenta como importación activa (coherente con I-H13 / D-1 del slice 1p)");
    }

    [Fact]
    public void EstadoNovedadPreop_solo_aplica_a_la_novedad_consultada()
    {
        // 1042 descartada; consultar 9999 (no tocada) debe dar Disponible.
        var agg = Inspeccion.Reconstruir(new object[] { EventoInspeccionIniciada(), Descartada(Novedad1042) });

        agg.EstadoNovedadPreop(9999).Should().Be(EstadoNovedadImportacion.Disponible);
    }
}
