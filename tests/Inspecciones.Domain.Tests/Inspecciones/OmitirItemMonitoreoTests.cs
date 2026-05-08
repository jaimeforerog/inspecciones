using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.OmitirItemMonitoreoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.OmitirItem"/>.
/// Cobertura de los escenarios §6.1 al §6.14 del spec del slice 1j.
/// Los escenarios §6.11 (PRE-2 InspeccionId no existe) y §6.12 (idempotencia Wolverine)
/// son de integración y se marcan Skip o viven en Inspecciones.Application.Tests.
/// </summary>
public class OmitirItemMonitoreoTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — ítem válido, motivo válido (un evento)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_en_inspeccion_monitoreo_enEjecucion_emite_ItemMonitoreoOmitido_v1()
    {
        // Given: stream con inspección Monitoreo en EnEjecucion, ItemId=3 disponible
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(itemId: 3, motivo: MotivoValido);

        // When
        var resultado = CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: exactamente un evento del tipo correcto
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<ItemMonitoreoOmitido_v1>();
    }

    [Fact]
    public void OmitirItemMonitoreo_happy_path_payload_correcto_en_evento_emitido()
    {
        // Given
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(
            inspeccionId: InspeccionIdMonitoreo,
            itemId: 3,
            motivo: MotivoValido,
            emitidoPor: "carlos.ruiz");

        // When
        var resultado = CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: payload completo
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<ItemMonitoreoOmitido_v1>().Subject
            .Should().BeEquivalentTo(new
            {
                InspeccionId = InspeccionIdMonitoreo,
                ItemId = 3,
                Motivo = MotivoValido,
                EmitidoPor = "carlos.ruiz",
                OmitidoEn = AhoraOmision,
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.2 Motivo con exactamente 10 caracteres (límite inferior válido)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_motivo_con_exactamente_10_chars_emite_ItemMonitoreoOmitido_v1()
    {
        // Given: mismo aggregate que 6.1
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(itemId: 3, motivo: MotivoExactamente10Chars); // "Sin acceso" = 10 chars

        // When
        var resultado = CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: el motivo de 10 chars exactos es válido — se emite el evento
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<ItemMonitoreoOmitido_v1>()
            .Which.Motivo.Should().Be(MotivoExactamente10Chars);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.3 Violación PRE-9 / I-M9 — ítem ya omitido previamente (409)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_item_ya_omitido_lanza_ItemYaOmitidoException_I_M9()
    {
        // Given: ItemId=3 ya fue omitido
        var dados = StreamMonitoreoConItemId3YaOmitido();
        var cmd = ComandoOmitir(itemId: 3, motivo: "Mismo problema persiste aún");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: I-M9 — doble omisión rechazada
        act.Should().Throw<ItemYaOmitidoException>()
            .WithMessage("*3*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.4 Violación PRE-8 / I-M8 — ítem ya medido no puede omitirse (422)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_item_ya_medido_lanza_ItemYaProcesadoException_I_M8()
    {
        // Given: ItemId=3 ya tiene medición
        var dados = StreamMonitoreoConItemId3YaMedido();
        var cmd = ComandoOmitir(itemId: 3, motivo: "Multímetro descargado ahora mismo");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: I-M8 — ítem procesado no puede omitirse
        act.Should().Throw<ItemYaProcesadoException>()
            .WithMessage("*3*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.5 Violación PRE-8 / I-M8 — ítem ya evaluado no puede omitirse (422)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_item_ya_evaluado_cualitativamente_lanza_ItemYaProcesadoException_I_M8()
    {
        // Given: ItemId=4 ya tiene evaluación cualitativa
        var dados = StreamMonitoreoConItemId4YaEvaluado();
        var cmd = ComandoOmitir(itemId: 4, motivo: "No pude acceder al componente");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: I-M8 — ítem con evaluación no puede omitirse
        act.Should().Throw<ItemYaProcesadoException>()
            .WithMessage("*4*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.6 Violación PRE-3 — motivo vacío (400)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_motivo_vacio_lanza_MotivoOmisionInvalidoException()
    {
        // Given: inspección válida, motivo vacío
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(itemId: 3, motivo: "");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: motivo vacío → MotivoOmisionInvalidoException
        act.Should().Throw<MotivoOmisionInvalidoException>();
    }

    [Fact]
    public void OmitirItemMonitoreo_motivo_solo_whitespace_lanza_MotivoOmisionInvalidoException()
    {
        // Given: inspección válida, motivo solo whitespace
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(itemId: 3, motivo: "   ");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then
        act.Should().Throw<MotivoOmisionInvalidoException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.7 Violación PRE-4 — motivo con menos de 10 caracteres (400)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_motivo_menor_a_10_chars_lanza_MotivoOmisionInvalidoException()
    {
        // Given: inspección válida, motivo "corto" (5 chars)
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(itemId: 3, motivo: "corto");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: longitud insuficiente → MotivoOmisionInvalidoException
        act.Should().Throw<MotivoOmisionInvalidoException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.8 Violación PRE-5 / I-M1 — inspección Tecnica rechaza omisión (422)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1()
    {
        // Given: inspección de Tipo=Tecnica
        var dados = StreamTecnicaEnEjecucion();
        var cmd = ComandoOmitir(itemId: 3, motivo: "No pude acceder al componente");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: I-M1 — solo Monitoreo soporta OmitirItemMonitoreo
        act.Should().Throw<InspeccionNoEsMonitoreoException>()
            .WithMessage("*Tecnica*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.9 Violación PRE-7 / I-M3 — ítem inexistente en snapshot (404)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_con_ItemId_inexistente_en_snapshot_lanza_ItemNoEncontradoEnSnapshotException_I_M3()
    {
        // Given: snapshot solo contiene ItemId=3; se omite ItemId=999
        var dados = StreamMonitoreoConSoloItemId3();
        var cmd = ComandoOmitir(itemId: 999, motivo: "Sensor no encontrado en la máquina");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: I-M3 — ItemId no pertenece al snapshot
        act.Should().Throw<ItemNoEncontradoEnSnapshotException>()
            .WithMessage("*999*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.10 Violación PRE-6 / I-M2 — inspección Firmada rechaza omisión (422)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I_M2()
    {
        // Given: inspección Monitoreo en Estado=Firmada
        var dados = StreamMonitoreoFirmado();
        var cmd = ComandoOmitir(itemId: 3, motivo: "Sensor inaccesible por barro");

        // When
        var act = () => CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: I-M2 — solo estado EnEjecucion acepta la omisión
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.11 PRE-2 — InspeccionId no existe (test de integración — Skip)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-2 vive en el handler — requiere Marten/Testcontainers (Application.Tests).")]
    public void OmitirItemMonitoreo_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException()
    {
        // Este test es de integración. El handler hace AggregateStreamAsync y devuelve null.
        // Cubierto en Inspecciones.Application.Tests / OmitirItemMonitoreoHandlerTests.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.12 Idempotencia — replay con mismo clientCommandId (Skip)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Idempotencia Wolverine envelope dedup — requiere infra (Application.Tests).")]
    public void OmitirItemMonitoreo_replay_mismo_clientCommandId_no_re_ejecuta_handler()
    {
        // Este test requiere Wolverine envelope storage. Cubierto en Application.Tests.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.13 Coexistencia — múltiples ítems omitidos (no interfieren entre sí)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_segundo_item_coexiste_con_omision_previa_emite_ItemMonitoreoOmitido_v1()
    {
        // Given: ItemId=3 ya omitido, ItemId=5 libre
        var dados = StreamMonitoreoConItemId3OmitidoItemId5Libre();
        var cmd = ComandoOmitir(
            itemId: 5,
            motivo: "Mangera hidráulica con aceite, no toqué el sensor");

        // When
        var resultado = CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // Then: se emite el evento para ItemId=5
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<ItemMonitoreoOmitido_v1>()
            .Which.ItemId.Should().Be(5);
    }

    [Fact]
    public void OmitirItemMonitoreo_segundo_item_agrega_ItemId5_a_itemsOmitidos_sin_perder_ItemId3()
    {
        // Given: ItemId=3 ya omitido, ItemId=5 libre
        var dados = StreamMonitoreoConItemId3OmitidoItemId5Libre();
        var cmd = ComandoOmitir(
            itemId: 5,
            motivo: "Mangera hidráulica con aceite, no toqué el sensor");

        // When: emitir y reproyectar
        var emitidos = CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);
        var stream = dados.Concat(emitidos).ToArray();
        var agregado = Inspeccion.Reconstruir(stream);

        // Then: ambos ítems en _itemsOmitidos
        agregado.ItemsOmitidos.Should().Contain(3);
        agregado.ItemsOmitidos.Should().Contain(5);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.14 Rebuild desde stream — Apply puro y orden causal (obligatorio)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OmitirItemMonitoreo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones()
    {
        // Given: stream previo + comando happy path
        var dados = StreamMonitoreoBase();
        var cmd = ComandoOmitir(
            inspeccionId: InspeccionIdMonitoreo,
            itemId: 3,
            motivo: MotivoValido,
            emitidoPor: "carlos.ruiz");
        var emitidos = CasoDeUso.OmitirItem(dados, cmd, AhoraOmision);

        // When: reproyectar el stream completo sobre aggregate vacío
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: el rebuild no lanza y el estado es coherente
        var agregado = act.Should().NotThrow().Subject;
        agregado.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        agregado.ItemsOmitidos.Should().Contain(3);
        agregado.ItemsMedidos.Should().BeEmpty();
        agregado.ItemsEvaluados.Should().BeEmpty();
        agregado.Hallazgos.Should().BeEmpty();
        agregado.Contribuyentes.Should().Contain("carlos.ruiz");
    }

    [Fact]
    public void OmitirItemMonitoreo_Apply_puro_no_lanza_al_reproyectar_solo_eventos_del_slice()
    {
        // Given: los dos eventos del rebuild acotado del §6.14 de la spec
        var omitidoEvt = new ItemMonitoreoOmitido_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 3,
            Motivo: MotivoValido,
            EmitidoPor: "carlos.ruiz",
            OmitidoEn: AhoraOmision);

        var stream = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdMonitoreo,
                Tipo: TipoInspeccion.Monitoreo,
                EquipoId: 4521,
                RutinaId: 42,
                RutinaCodigo: "Sistema hidráulico",
                TecnicoIniciador: "carlos.ruiz",
                ProyectoId: 3,
                Ubicacion: UbicacionColombia(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null,
                RutinaMonitoreoSeleccionadaId: 42,
                ItemsSnapshot: new List<ItemRutinaMonitoreoSnapshot>
                {
                    new(ItemId: 3,
                        Parte: "Sensor de presión",
                        Actividad: "Medir presión hidráulica",
                        Evaluacion: new MedicionEsperada("presión", "bar", 120m, 150m),
                        ParteEquipoId: 77),
                    new(ItemId: 4,
                        Parte: "Conectores",
                        Actividad: "Estado visual",
                        Evaluacion: new EvaluacionCualitativaEsperada(),
                        ParteEquipoId: 99),
                }),
            omitidoEvt,
        };

        // When: reproyectar
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: estado coherente — Apply no valida, solo muta
        var agregado = act.Should().NotThrow().Subject;
        agregado.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        agregado.ItemsSnapshot.Should().HaveCount(2);
        agregado.ItemsMedidos.Should().BeEmpty();
        agregado.ItemsEvaluados.Should().BeEmpty();
        agregado.ItemsOmitidos.Should().ContainSingle().Which.Should().Be(3);
        agregado.Hallazgos.Should().BeEmpty();
        agregado.Contribuyentes.Should().Contain("carlos.ruiz");
    }
}
