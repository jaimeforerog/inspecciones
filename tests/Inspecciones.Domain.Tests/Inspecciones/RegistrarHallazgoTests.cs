using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.RegistrarHallazgo"/>.
/// Cobertura de §6.1–§6.5, §6.7–§6.13, §6.15 del spec del slice 1c.
/// Los escenarios §6.6 (INV-PartePerteneceAlEquipo), §6.14 (PRE-2) y §6.16
/// (idempotencia Wolverine) viven en los tests de handler e integración.
/// </summary>
public class RegistrarHallazgoTests
{
    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — Origen=Manual, AccionRequerida=NoRequiereIntervencion
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_origen_manual_sin_intervencion_emite_HallazgoRegistrado_v1()
    {
        // Given: inspección en EnEjecucion
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualSinIntervencion();

        // When
        var resultado = CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoRegistrado_v1>();
    }

    [Fact]
    public void RegistrarHallazgo_origen_manual_sin_intervencion_emite_evento_con_payload_completo()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualSinIntervencion(
            hallazgoId: HallazgoG1,
            parteEquipoId: 77,
            novedadTecnica: "Manguera con desgaste leve superficial",
            actividadDescripcion: "Revisión visual de manguera",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        // Then
        var evento = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoRegistrado_v1>().Subject;

        evento.HallazgoId.Should().Be(HallazgoG1);
        evento.Origen.Should().Be(OrigenHallazgo.Manual);
        evento.ParteEquipoId.Should().Be(77);
        evento.NovedadPreopOrigenId.Should().BeNull("Origen=Manual → no tiene novedad preop");
        evento.ActividadId.Should().BeNull();
        evento.ActividadDescripcion.Should().Be("Revisión visual de manguera");
        evento.NovedadTecnica.Should().Be("Manguera con desgaste leve superficial");
        evento.AccionRequerida.Should().Be(AccionRequerida.NoRequiereIntervencion);
        evento.AccionCorrectiva.Should().BeNull("I-H5 — opcional para NoRequiereIntervencion");
        evento.TipoFallaId.Should().BeNull("I-H5 — opcional para NoRequiereIntervencion");
        evento.CausaFallaId.Should().BeNull("I-H5 — opcional para NoRequiereIntervencion");
        evento.EmitidoPor.Should().Be("ana.gomez");
        evento.RegistradoEn.Should().Be(Ahora);
    }

    [Fact]
    public void RegistrarHallazgo_origen_manual_sin_intervencion_aggregate_tiene_un_hallazgo_activo()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualSinIntervencion(hallazgoId: HallazgoG1, parteEquipoId: 77, emitidoPor: "ana.gomez");

        // When: aplicar el evento al aggregate
        var emitidos = CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: I2b y estado del aggregate
        aggregate.Hallazgos.Should().HaveCount(1);
        aggregate.Hallazgos[0].ParteEquipoId.Should().Be(77);
        aggregate.Hallazgos[0].Eliminado.Should().BeFalse();
        aggregate.Contribuyentes.Should().Contain("ana.gomez", "I2b — EmitidoPor se agrega a _contribuyentes");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 Happy path — Origen=PreOperacional, AccionRequerida=RequiereIntervencion
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_origen_preoperacional_con_intervencion_emite_HallazgoRegistrado_v1()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoPreopConIntervencion(
            hallazgoId: HallazgoG2,
            parteEquipoId: 88,
            novedadPreopOrigenId: 1042,
            actividadId: 55,
            novedadTecnica: "Fuga confirmada en sello hidráulico",
            accionCorrectiva: "Reemplazar sello hidráulico y rellenar aceite",
            tipoFallaId: 3,
            causaFallaId: 12,
            ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Ahora),
            observacionCampo: "Fuga visualmente confirmada con luz UV",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        // Then
        var evento = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoRegistrado_v1>().Subject;

        evento.Origen.Should().Be(OrigenHallazgo.PreOperacional);
        evento.NovedadPreopOrigenId.Should().Be(1042);
        evento.ActividadId.Should().Be(55);
        evento.ActividadDescripcion.Should().BeNull("PreOperacional — descripción viene del catálogo preop, no del campo libre");
        evento.TipoFallaId.Should().Be(3);
        evento.CausaFallaId.Should().Be(12);
        evento.AccionCorrectiva.Should().NotBeNullOrWhiteSpace();
        evento.Ubicacion.Should().NotBeNull();
    }

    [Fact]
    public void RegistrarHallazgo_origen_preoperacional_con_intervencion_aggregate_tiene_un_hallazgo()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoPreopConIntervencion(hallazgoId: HallazgoG2, parteEquipoId: 88, novedadPreopOrigenId: 1042);

        // When
        var emitidos = CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then
        aggregate.Hallazgos.Should().HaveCount(1);
        aggregate.Hallazgos[0].HallazgoId.Should().Be(HallazgoG2);
        aggregate.Hallazgos[0].Eliminado.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 Happy path — Origen=Manual, AccionRequerida=RequiereSeguimiento (I-H5)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_con_RequiereSeguimiento_sin_tipo_causa_falla_no_lanza_I_H5()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualConSeguimiento(parteEquipoId: 77);

        // When / Then — no lanza; I-H5 permite tipo/causa nulos para este AccionRequerida
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().NotThrow("I-H5 — TipoFallaId y CausaFallaId son opcionales para RequiereSeguimiento");
    }

    [Fact]
    public void RegistrarHallazgo_con_RequiereSeguimiento_emite_evento_con_tipo_causa_nulos_I_H5()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualConSeguimiento(parteEquipoId: 77);

        // When
        var resultado = CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        // Then
        var evento = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoRegistrado_v1>().Subject;

        evento.AccionRequerida.Should().Be(AccionRequerida.RequiereSeguimiento);
        evento.TipoFallaId.Should().BeNull("I-H5 — opcionales para RequiereSeguimiento");
        evento.CausaFallaId.Should().BeNull("I-H5 — opcionales para RequiereSeguimiento");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 Múltiples hallazgos sobre la misma parte (I-H6 — permitido)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_dos_hallazgos_sobre_misma_parte_I_H6_ambos_permitidos()
    {
        // Given: aggregate con un hallazgo ya registrado en parte 77
        var primerEvento = HallazgoRegistradoEjemplo(hallazgoId: HallazgoG1, parteEquipoId: 77);
        var dados = StreamConInspeccionIniciada().Append(primerEvento).ToArray();

        // Segundo comando — misma parte 77, HallazgoId distinto G2
        var cmd = ComandoManualSinIntervencion(hallazgoId: HallazgoG2, parteEquipoId: 77);

        // When / Then — no lanza; I-H6 multiplicidad permitida
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);
        act.Should().NotThrow("I-H6 — múltiples hallazgos sobre la misma parte son permitidos");
    }

    [Fact]
    public void RegistrarHallazgo_dos_hallazgos_sobre_misma_parte_aggregate_tiene_dos_activos_I_H6()
    {
        // Given
        var primerEvento = HallazgoRegistradoEjemplo(hallazgoId: HallazgoG1, parteEquipoId: 77);
        var dadosPrevios = StreamConInspeccionIniciada().Append(primerEvento).ToArray();
        var cmd = ComandoManualSinIntervencion(hallazgoId: HallazgoG2, parteEquipoId: 77);

        // When
        var emitidos = CasoDeUso.RegistrarHallazgo(dadosPrevios, cmd, Ahora);
        var stream = dadosPrevios.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then
        aggregate.Hallazgos.Should().HaveCount(2, "I-H6 — ambos hallazgos sobre parte 77 deben persistir");
        aggregate.Hallazgos.Should().AllSatisfy(h => h.ParteEquipoId.Should().Be(77));
        aggregate.Hallazgos.Should().AllSatisfy(h => h.Eliminado.Should().BeFalse());
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 Violación PRE-3 — inspección no está en estado EnEjecucion
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucion_PRE_3()
    {
        // Given: aggregate con Estado=Firmada (simula stream con evento de firma)
        var dados = StreamConInspeccionFirmada();
        var cmd = ComandoManualSinIntervencion();

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    [Fact]
    public void RegistrarHallazgo_en_inspeccion_cancelada_lanza_InspeccionNoEnEjecucion_PRE_3()
    {
        // Given: aggregate con Estado=Cancelada
        var dados = StreamConInspeccionCancelada();
        var cmd = ComandoManualSinIntervencion();

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<InspeccionNoEnEjecucionException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 Violación I-H2 — Origen=PreOperacional sin NovedadPreopOrigenId
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_origen_preoperacional_sin_NovedadPreopId_lanza_I_H2()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoPreopConIntervencion(novedadPreopOrigenId: null);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadPreopOrigenIdRequeridoException>()
            .WithMessage("*obligatorio*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 Violación I-H3 — Origen=Manual con NovedadPreopOrigenId presente
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_origen_manual_con_NovedadPreopId_lanza_I_H3()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualSinIntervencion(novedadPreopOrigenId: 999);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadPreopOrigenIdNoPermitidoException>()
            .WithMessage("*null*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 Violación I-H4 — RequiereIntervencion sin TipoFallaId
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_con_RequiereIntervencion_sin_TipoFallaId_lanza_I_H4()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualConIntervencion(tipoFallaId: null, causaFallaId: 5);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<TipoYCausaFallaRequeridosException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 Violación I-H4 — RequiereIntervencion sin CausaFallaId
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_con_RequiereIntervencion_sin_CausaFallaId_lanza_I_H4()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualConIntervencion(tipoFallaId: 3, causaFallaId: null);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<TipoYCausaFallaRequeridosException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 Violación PRE-8 — RequiereIntervencion sin AccionCorrectiva
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_con_RequiereIntervencion_sin_AccionCorrectiva_lanza_PRE_8()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualConIntervencion(tipoFallaId: 3, causaFallaId: 12, accionCorrectiva: null);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<AccionCorrectivaRequeridaException>()
            .WithMessage("*AccionCorrectiva*");
    }

    [Fact]
    public void RegistrarHallazgo_con_RequiereIntervencion_con_AccionCorrectiva_vacia_lanza_PRE_8()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualConIntervencion(tipoFallaId: 3, causaFallaId: 12, accionCorrectiva: "   ");

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<AccionCorrectivaRequeridaException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 Violación PRE-9 — NovedadTecnica vacía
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_con_NovedadTecnica_vacia_lanza_PRE_9()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualSinIntervencion(novedadTecnica: "");

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadTecnicaVaciaException>();
    }

    [Fact]
    public void RegistrarHallazgo_con_NovedadTecnica_solo_espacios_lanza_PRE_9()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoManualSinIntervencion(novedadTecnica: "   ");

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadTecnicaVaciaException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.13 Violación PRE-10 — Origen=Seguimiento (no soportado en este slice)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_con_origen_Seguimiento_lanza_OrigenNoSoportado_PRE_10()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoConOrigen(OrigenHallazgo.Seguimiento);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<OrigenNoSoportadoException>()
            .WithMessage("*Seguimiento*");
    }

    [Fact]
    public void RegistrarHallazgo_con_origen_Monitoreo_lanza_OrigenNoSoportado_PRE_10()
    {
        // Given
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoConOrigen(OrigenHallazgo.Monitoreo);

        // When / Then
        var act = () => CasoDeUso.RegistrarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<OrigenNoSoportadoException>()
            .WithMessage("*Monitoreo*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.15 Rebuild desde stream — Apply puro y orden causal (obligatorio)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_rebuild_desde_stream_reproduce_estado()
    {
        // Given: stream previo + dos hallazgos emitidos
        var inspeccionIniciada = EventoInspeccionIniciada();
        var hallazgoG1 = HallazgoRegistradoEjemplo(
            hallazgoId: HallazgoG1,
            parteEquipoId: 77,
            origen: OrigenHallazgo.Manual,
            accionRequerida: AccionRequerida.NoRequiereIntervencion,
            emitidoPor: "ana.gomez");
        var hallazgoG2 = HallazgoRegistradoEjemplo(
            hallazgoId: HallazgoG2,
            parteEquipoId: 88,
            origen: OrigenHallazgo.PreOperacional,
            novedadPreopOrigenId: 1042,
            accionRequerida: AccionRequerida.RequiereIntervencion,
            tipoFallaId: 3,
            causaFallaId: 12,
            emitidoPor: "pedro.ruiz");

        var stream = new object[] { inspeccionIniciada, hallazgoG1, hallazgoG2 };

        // When: reproyectar el stream completo sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: no lanza y el estado es coherente
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        aggregate.Hallazgos.Should().HaveCount(2);
        aggregate.Hallazgos[0].HallazgoId.Should().Be(HallazgoG1);
        aggregate.Hallazgos[0].Origen.Should().Be(OrigenHallazgo.Manual);
        aggregate.Hallazgos[0].ParteEquipoId.Should().Be(77);
        aggregate.Hallazgos[0].AccionRequerida.Should().Be(AccionRequerida.NoRequiereIntervencion);
        aggregate.Hallazgos[0].Eliminado.Should().BeFalse();
        aggregate.Hallazgos[1].HallazgoId.Should().Be(HallazgoG2);
        aggregate.Hallazgos[1].Origen.Should().Be(OrigenHallazgo.PreOperacional);
        aggregate.Hallazgos[1].NovedadPreopOrigenId.Should().Be(1042);
        aggregate.Hallazgos[1].TipoFallaId.Should().Be(3);
        aggregate.Hallazgos[1].CausaFallaId.Should().Be(12);
        aggregate.Hallazgos[1].Eliminado.Should().BeFalse();
        aggregate.Contribuyentes.Should().Contain("ana.gomez");
        aggregate.Contribuyentes.Should().Contain("pedro.ruiz");
    }

    [Fact]
    public void RegistrarHallazgo_rebuild_estado_identico_al_de_decision_in_process()
    {
        // Given: aplicar comandos secuenciales y comparar con rebuild desde stream
        var dadosBase = StreamConInspeccionIniciada();

        // Emitir primer hallazgo
        var cmdG1 = ComandoManualSinIntervencion(hallazgoId: HallazgoG1, parteEquipoId: 77, emitidoPor: "ana.gomez");
        var emitidosG1 = CasoDeUso.RegistrarHallazgo(dadosBase, cmdG1, Ahora);

        // Emitir segundo hallazgo sobre aggregate actualizado
        var dadosConG1 = dadosBase.Concat(emitidosG1).ToArray();
        var cmdG2 = ComandoManualSinIntervencion(hallazgoId: HallazgoG2, parteEquipoId: 77, emitidoPor: "pedro.ruiz");
        var emitidosG2 = CasoDeUso.RegistrarHallazgo(dadosConG1, cmdG2, Ahora);

        // When: rebuild total
        var streamCompleto = dadosBase.Concat(emitidosG1).Concat(emitidosG2).ToArray();
        var aggregateRebuild = Inspeccion.Reconstruir(streamCompleto);

        // Then: el estado del rebuild debe ser idéntico al de decisión in-process
        aggregateRebuild.Hallazgos.Should().HaveCount(2);
        aggregateRebuild.Contribuyentes.Should().Contain("ana.gomez");
        aggregateRebuild.Contribuyentes.Should().Contain("pedro.ruiz");
    }

    /// <summary>
    /// Sub-escenario del §6.15: resuelve FOLLOWUPS.md #12.
    /// El segundo <c>case</c> en <c>AplicarEvento</c> ya existe (HallazgoRegistrado_v1).
    /// Verificar que un tipo de evento desconocido lanza <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12()
    {
        // Given: un stream con un tipo de evento anónimo / desconocido
        var inspeccionIniciada = EventoInspeccionIniciada();
        var eventoDesconocido = new { Tipo = "EventoQueNoExiste", Payload = "algo" };
        var stream = new object[] { inspeccionIniciada, eventoDesconocido };

        // When / Then: AplicarEvento lanza InvalidOperationException para tipos no soportados
        var act = () => Inspeccion.Reconstruir(stream);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no soportado*");
    }
}
