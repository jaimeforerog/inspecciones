using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.EvaluacionCualitativaFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.RegistrarEvaluacionCualitativa"/>.
/// Cobertura de los escenarios §6.1 al §6.16 del spec del slice 1i'.
/// Los escenarios §6.10 (PRE-2 InspeccionId no existe), §6.12 (idempotencia Wolverine)
/// y §6.15 (atomicidad Testcontainers) viven en
/// <c>Inspecciones.Application.Tests</c> o se marcan como Skip.
/// </summary>
public class RegistrarEvaluacionCualitativaTests
{
    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — calificación Bueno (un evento)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_Bueno_emite_exactamente_un_EvaluacionCualitativaRegistrada_v1()
    {
        // Given: stream con inspección de monitoreo en EnEjecucion, ItemId=2 cualitativo
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno);

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: un solo evento, sin hallazgo
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<EvaluacionCualitativaRegistrada_v1>();
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Bueno_no_emite_HallazgoRegistrado_v1()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno);

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: NO se emite hallazgo (P-2 — solo Malo dispara)
        resultado.Should().NotContain(e => e is HallazgoRegistrado_v1);
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Bueno_payload_correcto_en_evento_emitido()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            itemId: 2,
            calificacion: CalificacionCualitativa.Bueno,
            observacion: null,
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<EvaluacionCualitativaRegistrada_v1>().Subject
            .Should().BeEquivalentTo(new
            {
                InspeccionId = InspeccionIdMonitoreo,
                ItemId = 2,
                Calificacion = CalificacionCualitativa.Bueno,
                Observacion = (string?)null,
                EmitidoPor = "ana.gomez",
                RegistradaEn = AhoraEvaluacion,
            });
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Bueno_agrega_item_a_itemsEvaluados()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno);

        // When
        var emitidos = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then: el item queda marcado como evaluado, sin hallazgos
        agregado.ItemsEvaluados.Should().Contain(2);
        agregado.Hallazgos.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 Happy path — calificación Regular (un evento; no dispara hallazgo)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_Regular_emite_exactamente_un_EvaluacionCualitativaRegistrada_v1()
    {
        // Given: mismo aggregate que §6.1
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            itemId: 2,
            calificacion: CalificacionCualitativa.Regular,
            observacion: "desgaste visible, revisar próximo mantenimiento");

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: exactamente un evento (P-2 — Regular no dispara hallazgo)
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<EvaluacionCualitativaRegistrada_v1>()
            .Which.Calificacion.Should().Be(CalificacionCualitativa.Regular);
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Regular_no_emite_HallazgoRegistrado_v1()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Regular);

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: P-2 — Regular no dispara hallazgo automático (§12.11.5 punto 6)
        resultado.Should().NotContain(e => e is HallazgoRegistrado_v1);
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Regular_propagada_observacion_en_evento()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var observacion = "desgaste visible, revisar próximo mantenimiento";
        var cmd = ComandoRegistrarEvaluacion(
            itemId: 2,
            calificacion: CalificacionCualitativa.Regular,
            observacion: observacion);

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<EvaluacionCualitativaRegistrada_v1>()
            .Which.Observacion.Should().Be(observacion);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 Happy path — calificación Malo (dos eventos atómicos)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_emite_dos_eventos_en_orden_causal()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE1,
            itemId: 2,
            calificacion: CalificacionCualitativa.Malo,
            observacion: "corrosión severa en terminales",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: exactamente 2 eventos en orden causal
        resultado.Should().HaveCount(2);
        resultado[0].Should().BeOfType<EvaluacionCualitativaRegistrada_v1>(
            "orden causal: EvaluacionCualitativaRegistrada_v1 antes que HallazgoRegistrado_v1");
        resultado[1].Should().BeOfType<HallazgoRegistrado_v1>(
            "orden causal: HallazgoRegistrado_v1 después del evento de evaluación");
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_EvaluacionCualitativaRegistrada_v1_con_payload_correcto()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE1,
            itemId: 2,
            calificacion: CalificacionCualitativa.Malo,
            observacion: "corrosión severa en terminales",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: primer evento con payload completo
        var evaluacion = (EvaluacionCualitativaRegistrada_v1)resultado[0];
        evaluacion.InspeccionId.Should().Be(InspeccionIdMonitoreo);
        evaluacion.ItemId.Should().Be(2);
        evaluacion.Calificacion.Should().Be(CalificacionCualitativa.Malo);
        evaluacion.Observacion.Should().Be("corrosión severa en terminales");
        evaluacion.EmitidoPor.Should().Be("ana.gomez");
        evaluacion.RegistradaEn.Should().Be(AhoraEvaluacion);
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_HallazgoRegistrado_v1_con_payload_correcto()
    {
        // Given: spec §6.3 — ItemId=2, Parte="Conectores batería", ParteEquipoId=55
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE1,
            itemId: 2,
            calificacion: CalificacionCualitativa.Malo,
            observacion: "corrosión severa en terminales",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: segundo evento — hallazgo automático con EvaluacionOrigenId y MedicionOrigenId=null
        var hallazgo = (HallazgoRegistrado_v1)resultado[1];
        hallazgo.HallazgoId.Should().Be(HallazgoE1);
        hallazgo.Origen.Should().Be(OrigenHallazgo.Monitoreo);
        hallazgo.EvaluacionOrigenId.Should().Be(2, "EvaluacionOrigenId = ItemId cuando Origen=Monitoreo cualitativo");
        hallazgo.MedicionOrigenId.Should().BeNull("null para hallazgos cualitativos — excluyente de EvaluacionOrigenId");
        hallazgo.ParteEquipoId.Should().Be(55, "ParteEquipoId proviene del snapshot");
        hallazgo.AccionRequerida.Should().Be(AccionRequerida.RequiereSeguimiento,
            "Origen=Monitoreo siempre emite con RequiereSeguimiento (§12.11.5 punto 6)");
        hallazgo.NovedadTecnica.Should().Be("Estado calificado Malo en Conectores batería",
            "P-6: formato '$\"Estado calificado Malo en {snapshot.Parte}\"'");
        hallazgo.NovedadPreopOrigenId.Should().BeNull("Monitoreo no tiene novedad preop");
        hallazgo.TipoFallaId.Should().BeNull("I-H5 — RequiereSeguimiento no exige TipoFallaId");
        hallazgo.CausaFallaId.Should().BeNull("I-H5 — RequiereSeguimiento no exige CausaFallaId");
        hallazgo.AccionCorrectiva.Should().BeNull("null cuando Origen=Monitoreo");
        hallazgo.Ubicacion.Should().BeNull("GPS no requerido en evaluación cualitativa (spec §9)");
        hallazgo.ObservacionCampo.Should().Be("corrosión severa en terminales",
            "ObservacionCampo propagado desde cmd.Observacion");
        hallazgo.EmitidoPor.Should().Be("ana.gomez");
        hallazgo.RegistradoEn.Should().Be(AhoraEvaluacion);
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_aggregate_tiene_hallazgo_monitoreo_activo()
    {
        // Given
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE1,
            itemId: 2,
            calificacion: CalificacionCualitativa.Malo);

        // When
        var emitidos = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then
        agregado.Hallazgos.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                HallazgoId = HallazgoE1,
                Origen = OrigenHallazgo.Monitoreo,
                EvaluacionOrigenId = (int?)2,
                MedicionOrigenId = (int?)null,
                AccionRequerida = AccionRequerida.RequiereSeguimiento,
                Eliminado = false,
            });
        agregado.ItemsEvaluados.Should().Contain(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 Violación PRE-3 / I-M1 — inspección técnica rechaza evaluación cualitativa
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1()
    {
        // Given: stream con InspeccionIniciada_v1 Tipo=Tecnica
        var dados = StreamTecnicaEnEjecucion();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno);

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then
        act.Should().Throw<InspeccionNoEsMonitoreoException>()
            .WithMessage("*Tecnica*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 Violación PRE-4 / I-M2 — inspección Firmada rechaza evaluación
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_en_inspeccion_monitoreo_firmada_lanza_InspeccionNoEnEjecucionException_I_M2()
    {
        // Given: stream Monitoreo + Firmada → Estado=Firmada
        var dados = StreamMonitoreoFirmado();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno);

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: misma excepción reutilizada (spec §4 PRE-4 — reutiliza InspeccionNoEnEjecucionException)
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 Violación PRE-5 / I-M3 — ítem inexistente en snapshot
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_con_ItemId_inexistente_en_snapshot_lanza_ItemNoEncontradoEnSnapshotException_I_M3()
    {
        // Given: snapshot solo contiene ItemId=1 e ItemId=2; comando usa ItemId=999
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 999, calificacion: CalificacionCualitativa.Bueno);

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then
        act.Should().Throw<ItemNoEncontradoEnSnapshotException>()
            .WithMessage("*999*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 Violación PRE-6 / I-M4 — ítem previamente omitido
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_en_item_omitido_lanza_ItemOmitidoNoPuedeMedirseException_I_M4()
    {
        // Given: stream con InspeccionIniciada_v1 + ItemMonitoreoOmitido_v1(ItemId=2)
        var dados = StreamMonitoreoConItemOmitido(itemIdOmitido: 2);
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno);

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then
        act.Should().Throw<ItemOmitidoNoPuedeMedirseException>()
            .WithMessage("*2*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 Violación PRE-7 / I-M5b — ítem numérico rechaza evaluación cualitativa
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_en_item_numerico_lanza_ItemNoEsCualitativoException_I_M5b()
    {
        // Given: ItemId=1 tiene MedicionEsperada en el snapshot
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 1, calificacion: CalificacionCualitativa.Bueno);

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: nueva excepción I-M5b simétrica a ItemNoEsNumericoException de slice 1i
        act.Should().Throw<ItemNoEsCualitativoException>()
            .WithMessage("*1*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 Violación PRE-8 / I-M7 — doble evaluación del mismo ítem rechazada (409)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_segunda_vez_el_mismo_item_lanza_ItemYaEvaluadoException_I_M7()
    {
        // Given: stream con EvaluacionCualitativaRegistrada_v1(ItemId=2) ya aplicado → _itemsEvaluados={2}
        var dados = StreamMonitoreoConItemYaEvaluado(itemId: 2, calificacion: CalificacionCualitativa.Bueno);
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Malo);

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: nueva excepción I-M7 — 409 Conflict
        act.Should().Throw<ItemYaEvaluadoException>()
            .WithMessage("*2*");
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_segunda_vez_no_cambia_itemsEvaluados()
    {
        // Given: stream con EvaluacionCualitativaRegistrada_v1(ItemId=2) ya aplicado
        var dados = StreamMonitoreoConItemYaEvaluado(itemId: 2);
        var agregadoPrevio = Inspeccion.Reconstruir(dados);

        // Verificar precondición del test: el set tiene el item
        agregadoPrevio.ItemsEvaluados.Should().Contain(2);

        // When: intento de segunda evaluación lanza
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Malo);
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: sin eventos emitidos (la excepción previene cualquier mutación)
        act.Should().Throw<ItemYaEvaluadoException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 PRE-2 — InspeccionId no existe (test de integración — Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-2 vive en el handler. Requiere Marten + Testcontainers Postgres. Ver RegistrarEvaluacionCualitativaHandlerTests en Inspecciones.Application.Tests. red-notes §6.10.")]
    public void RegistrarEvaluacionCualitativa_InspeccionId_no_existe_lanza_InspeccionNoEncontradaException()
    {
        // El handler valida PRE-2 (aggregate null → 404). No aplica en test de dominio puro.
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 Guard I-H1 — ParteEquipoId ausente en snapshot cuando Calificacion=Malo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_con_ParteEquipoId_nulo_en_snapshot_lanza_ParteEquipoIdAusenteEnSnapshotException()
    {
        // Given: stream con Tipo=Monitoreo, Estado=EnEjecucion;
        // ItemId=2 tiene EvaluacionCualitativaEsperada() y ParteEquipoId=null
        // (simula stream del slice 1h creado antes de la extensión P-1 de followup #22).
        var dados = StreamMonitoreoConSnapshotSinParteEquipoId();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE1,
            itemId: 2,
            calificacion: CalificacionCualitativa.Malo,
            emitidoPor: "ana.gomez");

        // When
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: guard I-H1 — mismo guard que RegistrarMedicion (§4 spec)
        act.Should().Throw<ParteEquipoIdAusenteEnSnapshotException>()
            .WithMessage("*ParteEquipoId*");
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Bueno_con_ParteEquipoId_nulo_en_snapshot_no_lanza()
    {
        // Given: mismo snapshot sin ParteEquipoId — pero calificación Bueno no genera hallazgo
        var dados = StreamMonitoreoConSnapshotSinParteEquipoId();
        var cmd = ComandoRegistrarEvaluacion(
            itemId: 2,
            calificacion: CalificacionCualitativa.Bueno);

        // When: Bueno no emite hallazgo → guard I-H1 no aplica
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: no lanza (guard solo aplica cuando Calificacion=Malo)
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 Idempotencia — replay con mismo clientCommandId (integración — Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Idempotencia end-to-end via Wolverine envelope dedup. Requiere infra. Ver red-notes §6.12.")]
    public void RegistrarEvaluacionCualitativa_mismo_clientCommandId_retorna_respuesta_original_sin_reejecutar()
    {
        // Wolverine envelope dedup detecta replay por MessageId y devuelve respuesta original.
        // Test de integración con Testcontainers.
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.13 Múltiples ítems cualitativos — cada Malo genera su propio hallazgo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_segundo_item_Malo_coexiste_con_hallazgo_previo()
    {
        // Given: stream con ItemId=2 ya evaluado como Malo (HallazgoE1 generado),
        //        + ItemId=4 cualitativo disponible
        var dados = StreamMonitoreoConDosItemsCualitativosYUnoEvaluadoMalo();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE2,
            itemId: 4,
            calificacion: CalificacionCualitativa.Malo,
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // Then: emite 2 eventos para ItemId=4
        resultado.Should().HaveCount(2);
        resultado[0].Should().BeOfType<EvaluacionCualitativaRegistrada_v1>()
            .Which.ItemId.Should().Be(4);
        ((HallazgoRegistrado_v1)resultado[1]).HallazgoId.Should().Be(HallazgoE2);
        ((HallazgoRegistrado_v1)resultado[1]).EvaluacionOrigenId.Should().Be(4);
        ((HallazgoRegistrado_v1)resultado[1]).ParteEquipoId.Should().Be(60);
        ((HallazgoRegistrado_v1)resultado[1]).NovedadTecnica.Should().Be("Estado calificado Malo en Mangueras hidráulicas");
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_segundo_item_Malo_aggregate_tiene_dos_hallazgos_activos()
    {
        // Given
        var dados = StreamMonitoreoConDosItemsCualitativosYUnoEvaluadoMalo();
        var cmd = ComandoRegistrarEvaluacion(hallazgoId: HallazgoE2, itemId: 4, calificacion: CalificacionCualitativa.Malo);

        // When
        var emitidos = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then: 2 hallazgos activos (I-H6 — multiplicidad permitida)
        agregado.Hallazgos.Where(h => !h.Eliminado).Should().HaveCount(2,
            "I-H6: múltiples hallazgos derivados de diferentes ítems del mismo equipo están permitidos");
        agregado.ItemsEvaluados.Should().BeEquivalentTo(new[] { 2, 4 });
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Coexistencia — sets _itemsMedidos y _itemsEvaluados son independientes
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_en_item_numerico_lanza_I_M5b_aunque_itemsMedidos_tenga_ese_ItemId()
    {
        // Given: aggregate con _itemsMedidos={1} y snapshot donde ItemId=1 es numérico
        // (scenario teórico — el set _itemsMedidos no interfiere con el guard I-M5b)
        var dados = StreamMonitoreoConItemsCualitativos();
        // Insertamos una medición previa sobre ItemId=1 (numérico) para poblar _itemsMedidos
        var medicionEvt = new MedicionRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 1,
            ValorMedido: 12.4m,
            Observacion: null,
            FueraDeRango: false,
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraEvaluacion.AddMinutes(-5));
        var dadosConMedicion = dados.Append(medicionEvt).ToArray();

        // Verificar precondición: ItemId=1 está en _itemsMedidos
        var agregadoPrevio = Inspeccion.Reconstruir(dadosConMedicion);
        agregadoPrevio.ItemsMedidos.Should().Contain(1);

        // When: intentamos evaluar cualitativamente ItemId=1 (que es numérico en el snapshot)
        var cmd = ComandoRegistrarEvaluacion(itemId: 1, calificacion: CalificacionCualitativa.Bueno);
        var act = () => CasoDeUso.RegistrarEvaluacionCualitativa(dadosConMedicion, cmd, AhoraEvaluacion);

        // Then: I-M5b se dispara — el set _itemsMedidos no interfiere con el guard de tipo
        act.Should().Throw<ItemNoEsCualitativoException>(
            "I-M5b: el tipo del ítem (numérico/cualitativo) se determina por el snapshot, no por los sets");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.15 Atomicidad — dos eventos o ninguno (integración — Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Atomicidad garantizada por Marten con único SaveChangesAsync. Ver RegistrarEvaluacionCualitativaHandlerTests en Inspecciones.Application.Tests con Testcontainers Postgres. red-notes §6.15.")]
    public void RegistrarEvaluacionCualitativa_SaveChangesAsync_falla_no_persiste_ningun_evento()
    {
        // Test de atomicidad vive en Inspecciones.Application.Tests.
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.16 Rebuild desde stream — Apply puro y orden causal (OBLIGATORIO)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones()
    {
        // Given: historial previo + ejecutar comando happy path Malo (§6.3)
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(
            hallazgoId: HallazgoE1,
            itemId: 2,
            calificacion: CalificacionCualitativa.Malo,
            observacion: "corrosión severa en terminales",
            emitidoPor: "ana.gomez");

        var emitidos = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // When: reproyectar el stream completo (previos + emitidos) sobre un agregado vacío
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: el rebuild no lanza y el estado resultante es coherente con §6.3
        var agregado = act.Should().NotThrow().Subject;

        agregado.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        agregado.ItemsSnapshot.Should().NotBeNull().And.HaveCount(2);
        agregado.ItemsEvaluados.Should().Contain(2);
        agregado.ItemsMedidos.Should().BeEmpty();
        agregado.ItemsOmitidos.Should().BeEmpty();
        agregado.Hallazgos.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                HallazgoId = HallazgoE1,
                Origen = OrigenHallazgo.Monitoreo,
                EvaluacionOrigenId = (int?)2,
                MedicionOrigenId = (int?)null,
                AccionRequerida = AccionRequerida.RequiereSeguimiento,
                Eliminado = false,
            });
        agregado.Contribuyentes.Should().Contain("ana.gomez");
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Bueno_rebuild_desde_stream_no_tiene_hallazgos()
    {
        // Given: happy path §6.1 — calificación Bueno, sin hallazgo
        var dados = StreamMonitoreoConItemsCualitativos();
        var cmd = ComandoRegistrarEvaluacion(itemId: 2, calificacion: CalificacionCualitativa.Bueno, emitidoPor: "ana.gomez");
        var emitidos = CasoDeUso.RegistrarEvaluacionCualitativa(dados, cmd, AhoraEvaluacion);

        // When: reproyectar
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then
        var agregado = act.Should().NotThrow().Subject;
        agregado.ItemsEvaluados.Should().Contain(2);
        agregado.Hallazgos.Should().BeEmpty();
    }

    [Fact]
    public void RegistrarEvaluacionCualitativa_Malo_Apply_puro_EvaluacionCualitativaRegistrada_antes_de_HallazgoRegistrado()
    {
        // Given: reproyectar stream con eventos en orden causal correcto
        var evaluacionEvt = new EvaluacionCualitativaRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 2,
            Calificacion: CalificacionCualitativa.Malo,
            Observacion: "corrosión severa",
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraEvaluacion);

        var hallazgoEvt = new HallazgoRegistrado_v1(
            InspeccionId: InspeccionIdMonitoreo,
            HallazgoId: HallazgoE1,
            Origen: OrigenHallazgo.Monitoreo,
            NovedadPreopOrigenId: null,
            MedicionOrigenId: null,
            EvaluacionOrigenId: 2,
            ParteEquipoId: 55,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: "Estado calificado Malo en Conectores batería",
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: "corrosión severa",
            Ubicacion: null,
            EmitidoPor: "ana.gomez",
            RegistradoEn: AhoraEvaluacion);

        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotCualitativo());

        // When: reproyectar en orden causal correcto
        var streamOrdenCausal = new object[] { inicioEvt, evaluacionEvt, hallazgoEvt };
        var act = () => Inspeccion.Reconstruir(streamOrdenCausal);

        // Then: rebuild sin excepción — Apply no valida, solo muta estado
        var agregado = act.Should().NotThrow().Subject;
        agregado.ItemsEvaluados.Should().Contain(2,
            "Apply(EvaluacionCualitativaRegistrada_v1) debe añadir el ítem a _itemsEvaluados antes de Apply(HallazgoRegistrado_v1)");
        agregado.Hallazgos.Should().ContainSingle()
            .Which.EvaluacionOrigenId.Should().Be(2);
    }
}
