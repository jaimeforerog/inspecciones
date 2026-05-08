using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.MedicionFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.RegistrarMedicion"/>.
/// Cobertura de los escenarios §6.1 al §6.15 del spec del slice 1i.
/// Los escenarios §6.11 (PRE-2 InspeccionId no existe), §6.12 (idempotencia
/// Wolverine) y §6.14 (atomicidad Testcontainers) viven en
/// <c>Inspecciones.Application.Tests</c> o se marcan como Skip.
/// </summary>
public class RegistrarMedicionTests
{
    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — medición dentro del rango (un evento)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_dentro_del_rango_emite_un_solo_MedicionRegistrada_v1()
    {
        // Given: stream con inspección de monitoreo en EnEjecucion, ItemId=1 numérico [12.3, 12.5]
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m);

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: un solo evento, sin hallazgo
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<MedicionRegistrada_v1>()
            .Which.FueraDeRango.Should().BeFalse();
    }

    [Fact]
    public void RegistrarMedicion_dentro_del_rango_payload_correcto_y_no_emite_HallazgoRegistrado_v1()
    {
        // Given
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m, observacion: null, emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<MedicionRegistrada_v1>().Subject
            .Should().BeEquivalentTo(new
            {
                InspeccionId = InspeccionIdMonitoreo,
                ItemId = 1,
                ValorMedido = 12.4m,
                Observacion = (string?)null,
                FueraDeRango = false,
                EmitidoPor = "ana.gomez",
                RegistradaEn = AhoraRegistro,
            });

        resultado.Should().NotContain(e => e is HallazgoRegistrado_v1);
    }

    [Fact]
    public void RegistrarMedicion_dentro_del_rango_agrega_item_a_itemsMedidos()
    {
        // Given
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m);

        // When
        var emitidos = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then: el item queda marcado como medido
        agregado.ItemsMedidos.Should().Contain(1);
        agregado.Hallazgos.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 Happy path — medición fuera de rango por debajo (dos eventos)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_fuera_de_rango_por_debajo_emite_MedicionRegistrada_v1_y_HallazgoRegistrado_v1()
    {
        // Given
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(
            hallazgoId: HallazgoM1,
            itemId: 1,
            valorMedido: 10.2m,
            observacion: "multímetro con pila baja",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: exactamente 2 eventos en orden causal
        resultado.Should().HaveCount(2);
        resultado[0].Should().BeOfType<MedicionRegistrada_v1>()
            .Which.FueraDeRango.Should().BeTrue();
        resultado[1].Should().BeOfType<HallazgoRegistrado_v1>();
    }

    [Fact]
    public void RegistrarMedicion_fuera_de_rango_por_debajo_MedicionRegistrada_v1_con_timestamp_correcto()
    {
        // Given
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(
            hallazgoId: HallazgoM1,
            itemId: 1,
            valorMedido: 10.2m,
            observacion: "multímetro con pila baja",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: primer evento con payload completo
        var medicion = (MedicionRegistrada_v1)resultado[0];
        medicion.InspeccionId.Should().Be(InspeccionIdMonitoreo);
        medicion.ItemId.Should().Be(1);
        medicion.ValorMedido.Should().Be(10.2m);
        medicion.FueraDeRango.Should().BeTrue();
        medicion.Observacion.Should().Be("multímetro con pila baja");
        medicion.EmitidoPor.Should().Be("ana.gomez");
        medicion.RegistradaEn.Should().Be(AhoraRegistro);
    }

    [Fact]
    public void RegistrarMedicion_fuera_de_rango_por_debajo_HallazgoRegistrado_v1_con_payload_correcto()
    {
        // Given: spec §6.2 — ItemId=1, Parte="Batería", ParteEquipoId proviene del snapshot (P-1)
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(
            hallazgoId: HallazgoM1,
            itemId: 1,
            valorMedido: 10.2m,
            observacion: "multímetro con pila baja",
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: segundo evento — hallazgo automático
        var hallazgo = (HallazgoRegistrado_v1)resultado[1];
        hallazgo.HallazgoId.Should().Be(HallazgoM1);
        hallazgo.Origen.Should().Be(OrigenHallazgo.Monitoreo);
        hallazgo.MedicionOrigenId.Should().Be(1, "MedicionOrigenId = ItemId cuando Origen=Monitoreo");
        hallazgo.AccionRequerida.Should().Be(AccionRequerida.RequiereSeguimiento,
            "Origen=Monitoreo siempre emite con RequiereSeguimiento (§12.11.5 punto 6)");
        hallazgo.NovedadTecnica.Should().Be("Voltaje 10.2V fuera de rango esperado [12.3, 12.5]");
        hallazgo.NovedadPreopOrigenId.Should().BeNull("Monitoreo no tiene novedad preop");
        hallazgo.TipoFallaId.Should().BeNull("I-H5 — RequiereSeguimiento no exige TipoFallaId");
        hallazgo.CausaFallaId.Should().BeNull("I-H5 — RequiereSeguimiento no exige CausaFallaId");
        hallazgo.AccionCorrectiva.Should().BeNull("null cuando Origen=Monitoreo");
        hallazgo.Ubicacion.Should().BeNull("GPS no requerido en mediciones (spec §9)");
        hallazgo.ObservacionCampo.Should().Be("multímetro con pila baja");
        hallazgo.EmitidoPor.Should().Be("ana.gomez");
        hallazgo.RegistradoEn.Should().Be(AhoraRegistro);
    }

    [Fact]
    public void RegistrarMedicion_fuera_de_rango_por_debajo_aggregate_tiene_hallazgo_monitoreo_activo()
    {
        // Given
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(hallazgoId: HallazgoM1, itemId: 1, valorMedido: 10.2m);

        // When
        var emitidos = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then
        agregado.Hallazgos.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                HallazgoId = HallazgoM1,
                Origen = OrigenHallazgo.Monitoreo,
                MedicionOrigenId = (int?)1,
                AccionRequerida = AccionRequerida.RequiereSeguimiento,
                Eliminado = false,
            });
        agregado.ItemsMedidos.Should().Contain(1);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 Happy path — medición fuera de rango por encima
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_fuera_de_rango_por_encima_emite_dos_eventos_con_FueraDeRango_true()
    {
        // Given
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(hallazgoId: HallazgoM1, itemId: 1, valorMedido: 15.0m);

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        resultado.Should().HaveCount(2);
        ((MedicionRegistrada_v1)resultado[0]).FueraDeRango.Should().BeTrue();
        ((MedicionRegistrada_v1)resultado[0]).ValorMedido.Should().Be(15.0m);

        var hallazgo = (HallazgoRegistrado_v1)resultado[1];
        hallazgo.NovedadTecnica.Should().Be("Voltaje 15.0V fuera de rango esperado [12.3, 12.5]");
        hallazgo.AccionRequerida.Should().Be(AccionRequerida.RequiereSeguimiento);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 Happy path — borde inclusivo del rango (rango cerrado P-2)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_en_borde_inferior_ValorMin_emite_FueraDeRango_false()
    {
        // Given: ValorMedido=12.3 = ValorMin — rango cerrado [12.3, 12.5] → FueraDeRango=false
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.3m);

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: exactamente un evento, FueraDeRango=false (P-2: borde inclusivo)
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<MedicionRegistrada_v1>()
            .Which.FueraDeRango.Should().BeFalse("rango cerrado [ValorMin, ValorMax] — borde es dentro del rango");
    }

    [Fact]
    public void RegistrarMedicion_en_borde_superior_ValorMax_emite_FueraDeRango_false()
    {
        // Given: ValorMedido=12.5 = ValorMax — rango cerrado → FueraDeRango=false
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.5m);

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<MedicionRegistrada_v1>()
            .Which.FueraDeRango.Should().BeFalse("borde superior inclusivo");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 Violación PRE-3 / I-M1 — inspección técnica rechaza medición
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1()
    {
        // Given: stream con InspeccionIniciada_v1 Tipo=Tecnica
        var dados = StreamTecnicaEnEjecucion();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m);

        // When
        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        act.Should().Throw<InspeccionNoEsMonitoreoException>()
            .WithMessage("*Tecnica*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 Violación PRE-4 / I-M2 — inspección Firmada rechaza medición
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_en_inspeccion_monitoreo_firmada_lanza_InspeccionNoEnEjecucionException_I_M2()
    {
        // Given: stream Monitoreo + Firmada → Estado=Firmada
        var dados = StreamMonitoreoFirmado();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m);

        // When
        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: misma excepción reutilizada (spec §4 PRE-4 — reutiliza InspeccionNoEnEjecucionException)
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 Violación PRE-5 / I-M3 — ítem inexistente en snapshot
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_con_ItemId_inexistente_en_snapshot_lanza_ItemNoEncontradoEnSnapshotException_I_M3()
    {
        // Given: snapshot solo contiene ItemId=1 e ItemId=2; comando usa ItemId=999
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 999, valorMedido: 5.0m);

        // When
        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        act.Should().Throw<ItemNoEncontradoEnSnapshotException>()
            .WithMessage("*999*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 Violación PRE-6 / I-M4 — ítem previamente omitido
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_en_item_omitido_lanza_ItemOmitidoNoPuedeMedirseException_I_M4()
    {
        // Given: stream con InspeccionIniciada_v1 + ItemMonitoreoOmitido_v1(ItemId=1)
        var dados = StreamMonitoreoConItemOmitido(itemIdOmitido: 1);
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m);

        // When
        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        act.Should().Throw<ItemOmitidoNoPuedeMedirseException>()
            .WithMessage("*1*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 Violación PRE-7 / I-M5 — ítem cualitativo rechaza medición numérica
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_en_item_cualitativo_lanza_ItemNoEsNumericoException_I_M5()
    {
        // Given: ItemId=2 tiene EvaluacionCualitativaEsperada() en el snapshot
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 2, valorMedido: 1.5m);

        // When
        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        act.Should().Throw<ItemNoEsNumericoException>()
            .WithMessage("*2*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 Violación PRE-8 / I-M6 — doble medición del mismo ítem (409)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_segunda_vez_el_mismo_item_lanza_ItemYaMedidoException_I_M6()
    {
        // Given: stream con MedicionRegistrada_v1(ItemId=1) ya aplicado → _itemsMedidos={1}
        var dados = StreamMonitoreoConItemYaMedido(itemId: 1, valorMedido: 12.4m);
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.1m);

        // When
        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then
        act.Should().Throw<ItemYaMedidoException>()
            .WithMessage("*1*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.13 Múltiples ítems — cada ítem fuera de rango genera su propio hallazgo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_segundo_item_fuera_de_rango_coexiste_con_hallazgo_previo()
    {
        // Given: stream con ItemId=1 ya medido (fuera de rango, HallazgoM1 generado)
        //        + ItemId=3 numérico [0.9, 1.1] disponible
        var dados = StreamMonitoreoConDosItemsYUnoMedidoFueraDeRango();
        var cmd = ComandoRegistrarMedicion(
            hallazgoId: HallazgoM2,
            itemId: 3,
            valorMedido: 0.5m,
            emitidoPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: emite 2 eventos para ItemId=3
        resultado.Should().HaveCount(2);
        resultado[0].Should().BeOfType<MedicionRegistrada_v1>()
            .Which.ItemId.Should().Be(3);
        ((HallazgoRegistrado_v1)resultado[1]).HallazgoId.Should().Be(HallazgoM2);
        ((HallazgoRegistrado_v1)resultado[1]).MedicionOrigenId.Should().Be(3);
    }

    [Fact]
    public void RegistrarMedicion_segundo_item_fuera_de_rango_aggregate_tiene_dos_hallazgos_activos()
    {
        // Given
        var dados = StreamMonitoreoConDosItemsYUnoMedidoFueraDeRango();
        var cmd = ComandoRegistrarMedicion(hallazgoId: HallazgoM2, itemId: 3, valorMedido: 0.5m);

        // When
        var emitidos = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then: 2 hallazgos activos (I-H6 — multiplicidad permitida)
        agregado.Hallazgos.Where(h => !h.Eliminado).Should().HaveCount(2,
            "I-H6: múltiples hallazgos derivados de diferentes ítems del mismo equipo están permitidos");
        agregado.ItemsMedidos.Should().BeEquivalentTo(new[] { 1, 3 });
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Atomicidad — test de integración con Testcontainers (Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Testcontainers Postgres. Ver RegistrarMedicionHandlerTests en Inspecciones.Application.Tests. red-notes §6.14.")]
    public void RegistrarMedicion_SaveChangesAsync_falla_no_persiste_ningun_evento()
    {
        // Este test de atomicidad vive en Inspecciones.Application.Tests.
        // La atomicidad la garantiza Marten con un único SaveChangesAsync.
        // Ver spec §6.14 y red-notes.md §6.14.
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.15 Rebuild desde stream — Apply puro y orden causal (obligatorio)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones()
    {
        // Given: historial previo + ejecutar comando happy path fuera de rango (§6.2)
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(
            hallazgoId: HallazgoM1,
            itemId: 1,
            valorMedido: 10.2m,
            observacion: "multímetro con pila baja",
            emitidoPor: "ana.gomez");

        var emitidos = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // When: reproyectar el stream completo (previos + emitidos) sobre un agregado vacío
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: el rebuild no lanza y el estado resultante es coherente con §6.2
        var agregado = act.Should().NotThrow().Subject;

        agregado.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        agregado.ItemsSnapshot.Should().NotBeNull().And.HaveCount(2);
        agregado.ItemsMedidos.Should().Contain(1);
        agregado.ItemsOmitidos.Should().BeEmpty();
        agregado.Hallazgos.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                HallazgoId = HallazgoM1,
                Origen = OrigenHallazgo.Monitoreo,
                MedicionOrigenId = (int?)1,
                AccionRequerida = AccionRequerida.RequiereSeguimiento,
                Eliminado = false,
            });
        agregado.Contribuyentes.Should().Contain("ana.gomez");
    }

    [Fact]
    public void RegistrarMedicion_rebuild_dentro_del_rango_no_tiene_hallazgos()
    {
        // Given: happy path §6.1 — medición dentro del rango
        var dados = StreamMonitoreoConItems();
        var cmd = ComandoRegistrarMedicion(itemId: 1, valorMedido: 12.4m, emitidoPor: "ana.gomez");
        var emitidos = CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // When: reproyectar
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then
        var agregado = act.Should().NotThrow().Subject;
        agregado.ItemsMedidos.Should().Contain(1);
        agregado.Hallazgos.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Iteración correctiva 2026-05-08 — guard I-H1: ParteEquipoIdAusente
    // Cubre rama Inspeccion.cs:889-893 detectada por el reviewer como sin test.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarMedicion_fuera_de_rango_con_ParteEquipoId_nulo_en_snapshot_lanza_ParteEquipoIdAusenteEnSnapshotException()
    {
        // Given: stream con Tipo=Monitoreo, Estado=EnEjecucion; ItemId=1 tiene
        // MedicionEsperada(voltaje, V, 12.3, 12.5) y ParteEquipoId=null
        // (simula stream del slice 1h creado antes de la extensión P-1).
        var snapshotSinParteEquipoId = new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m),
                ParteEquipoId: null),
        };

        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: MonitoreoFixtures.UbicacionColombia(),
            IniciadaEn: MonitoreoFixtures.Ahora,
            FechaReportada: DateOnly.FromDateTime(MonitoreoFixtures.Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: snapshotSinParteEquipoId);

        var dados = new object[] { inicioEvt };

        // When: medición fuera de rango (10.2 < 12.3) — activa la rama del guard
        var cmd = ComandoRegistrarMedicion(
            hallazgoId: HallazgoM1,
            itemId: 1,
            valorMedido: 10.2m,
            emitidoPor: "ana.gomez");

        var act = () => CasoDeUso.RegistrarMedicion(dados, cmd, AhoraRegistro);

        // Then: lanza con mensaje descriptivo, sin emitir ningún evento
        act.Should().Throw<ParteEquipoIdAusenteEnSnapshotException>()
            .WithMessage("*ParteEquipoId*");
    }
}
