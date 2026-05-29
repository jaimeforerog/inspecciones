using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del slice 1p — guardas de unicidad de novedad preop en
/// <see cref="Inspeccion.RegistrarHallazgo"/>:
/// <list type="bullet">
///   <item>PRE-11 / INV-ND1 — no importar una novedad ya descartada (cierra FU-40).</item>
///   <item>PRE-12 / I-H13 — no importar dos veces la misma novedad (Gap 6b del contrato front).</item>
/// </list>
/// Cobertura de §6.1–§6.7 del spec del slice 1p.
/// </summary>
public class RegistrarHallazgoUnicidadNovedadPreopTests
{
    private const int Novedad1042 = 1042;

    private static NovedadPreopDescartada_v1 NovedadDescartadaEjemplo(int novedadId) =>
        new(InspeccionId: InspeccionIdNueva,
            NovedadId: novedadId,
            MotivoDescarte: $"Cerrado por ana.gomez desde Inspecciones",
            DescartadaPor: "ana.gomez",
            DescartadaEn: Ahora);

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 PRE-11 / INV-ND1 — importar una novedad ya descartada → rechazo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_de_novedad_ya_descartada_lanza_NovedadDescartadaNoImportable_INV_ND1()
    {
        // Given: la novedad 1042 fue descartada en esta inspección
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            NovedadDescartadaEjemplo(Novedad1042)
        };
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG3, novedadPreopOrigenId: Novedad1042);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadDescartadaNoImportableException>()
            .WithMessage("*1042*descartada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 PRE-12 / I-H13 — importar una novedad ya importada (activa) → rechazo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_de_novedad_ya_importada_activa_lanza_NovedadPreopYaImportada_I_H13()
    {
        // Given: la novedad 1042 ya tiene un hallazgo preop activo (G2)
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                origen: OrigenHallazgo.PreOperacional,
                novedadPreopOrigenId: Novedad1042,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12)
        };
        // Segundo import de la MISMA novedad con distinto HallazgoId (G3)
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG3, novedadPreopOrigenId: Novedad1042);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadPreopYaImportadaException>()
            .WithMessage("*1042*importada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 D-1 — re-importar tras eliminar el hallazgo previo → permitido
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_de_novedad_cuyo_hallazgo_fue_eliminado_no_lanza_D1()
    {
        // Given: la novedad 1042 fue importada (G2) y luego eliminada (soft delete)
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                origen: OrigenHallazgo.PreOperacional,
                novedadPreopOrigenId: Novedad1042,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            HallazgoEliminadoEjemplo(hallazgoId: HallazgoG2)
        };
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG3, novedadPreopOrigenId: Novedad1042);

        // When
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        // Then: re-importar es legítimo — el hallazgo previo fue deshecho
        var emitidos = act.Should().NotThrow("D-1 — re-importar tras eliminar es legítimo").Subject;
        emitidos.Should().ContainSingle().Which.Should().BeOfType<HallazgoRegistrado_v1>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 alcance — importar OTRA novedad cuando una está descartada/importada
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_de_novedad_distinta_no_lanza_aunque_otras_esten_descartadas_o_importadas()
    {
        // Given: 1042 descartada, 2000 importada
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            NovedadDescartadaEjemplo(Novedad1042),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                origen: OrigenHallazgo.PreOperacional,
                novedadPreopOrigenId: 2000,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12)
        };
        // Import de una tercera novedad, 3000 — distinta de ambas
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG3, novedadPreopOrigenId: 3000);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().NotThrow("las guardas son por-novedad: 3000 no está ni descartada ni importada");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 no-regresión I-H6 — dos novedades distintas sobre la misma parte
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_dos_novedades_distintas_misma_parte_no_lanza_I_H6_intacto()
    {
        // Given: novedad 1042 importada sobre la parte 88
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                parteEquipoId: 88,
                origen: OrigenHallazgo.PreOperacional,
                novedadPreopOrigenId: Novedad1042,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12)
        };
        // Otra novedad (2042) sobre la MISMA parte 88
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG3, novedadPreopOrigenId: 2042, parteEquipoId: 88);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().NotThrow("I-H6 — la cota I-H13 es por novedad, no por parte");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 obligatorio — la guarda lee estado reconstruido desde stream
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_guarda_INV_ND1_lee_estado_reconstruido_desde_stream()
    {
        // Given: aggregate reconstruido (no in-process) con la novedad descartada
        var aggregate = Inspeccion.Reconstruir(new object[]
        {
            EventoInspeccionIniciada(),
            NovedadDescartadaEjemplo(Novedad1042)
        });
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG3, novedadPreopOrigenId: Novedad1042);

        // When / Then: la guarda dispara sobre el _novedadesDescartadas rehidratado
        var act = () => aggregate.RegistrarHallazgo(cmd, Ahora);

        act.Should().Throw<NovedadDescartadaNoImportableException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 no-regresión — Origen=Manual no se ve afectado por las guardas nuevas
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_origen_manual_no_se_afecta_por_guardas_de_novedad_preop()
    {
        // Given: hay una novedad descartada en el stream
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            NovedadDescartadaEjemplo(Novedad1042)
        };
        // Comando manual (sin novedadPreopOrigenId)
        var cmd = ComandoManualSinIntervencion(hallazgoId: HallazgoG3);

        // When
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        // Then: el flujo manual no se afecta (las guardas solo aplican a PreOperacional)
        var emitidos = act.Should().NotThrow().Subject;
        emitidos.Should().ContainSingle().Which.Should().BeOfType<HallazgoRegistrado_v1>();
    }
}
