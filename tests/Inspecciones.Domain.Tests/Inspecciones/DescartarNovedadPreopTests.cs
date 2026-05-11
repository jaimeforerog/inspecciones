using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.DescartarNovedadPreopFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.Descartar"/>.
/// Cobertura de §6.1–§6.10 del spec del slice 1n (DescartarNovedadPreop).
///
/// Tests que invocan <c>Descartar</c> fallan con <see cref="NotImplementedException"/>
/// hasta que <c>green</c> implemente <see cref="Inspeccion.Descartar"/>.
///
/// El test de rebuild (§6.9) pasa desde el estado rojo porque
/// <see cref="Inspeccion.Apply(NovedadPreopDescartada_v1)"/> ya es puro.
///
/// Decisiones firmadas: D-2 (no se trackea _novedadesImportadas → PRE-7 skip/comentario);
/// D-4 (plantilla de motivo autogenerado); D-5 (estado post-descarte = EnEjecucion).
/// </summary>
public sealed class DescartarNovedadPreopTests
{
    /// <summary>Timestamp de descarte alineado con el spec §6.1 (2026-05-11T14:30:00Z).</summary>
    private static readonly DateTimeOffset AhoraDescarte =
        new(2026, 5, 11, 14, 30, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────────
    // §6.1 — Happy path: descartar novedad válida en inspección en ejecución
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_en_inspeccion_en_ejecucion_emite_NovedadPreopDescartada_v1()
    {
        // Given: inspección técnica en EnEjecucion
        var dados = StreamEnEjecucionBase(tecnicoId: "ana.gomez");
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // Then: exactamente un evento del tipo esperado
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<NovedadPreopDescartada_v1>();
    }

    [Fact]
    public void DescartarNovedadPreop_happy_path_payload_del_evento_es_correcto()
    {
        // Given
        var dados = StreamEnEjecucionBase(tecnicoId: "ana.gomez");
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        // When
        var resultado = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // Then: todos los campos del evento son correctos (spec §6.1)
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<NovedadPreopDescartada_v1>().Subject;

        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.NovedadId.Should().Be(NovedadId9001);
        evt.DescartadaPor.Should().Be("ana.gomez");
        evt.DescartadaEn.Should().Be(AhoraDescarte);
        // MotivoDescarte verificado en §6.10 dedicado
    }

    [Fact]
    public void DescartarNovedadPreop_happy_path_estado_permanece_EnEjecucion_D5()
    {
        // Given
        var dados = StreamEnEjecucionBase(tecnicoId: "ana.gomez");
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        // When: emitir evento + reproyectar
        var emitidos = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: D-5 — el descarte no transiciona el estado (sigue EnEjecucion)
        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion,
            "el descarte es un evento de captura, no de lifecycle (D-5 spec §13)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.2 — PRE-2: descarte sobre inspección firmada
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_PRE2()
    {
        // Given: stream con Estado=Firmada
        var dados = StreamConInspeccionFirmada();
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "rmartinez");

        // When
        var act = () => CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // Then: PRE-2 — inspección en estado terminal no puede recibir descartes
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.3 — PRE-2: descarte sobre inspección cancelada
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_en_inspeccion_cancelada_lanza_InspeccionNoEnEjecucionException_PRE2()
    {
        // Given: stream con Estado=Cancelada
        var dados = StreamConInspeccionCancelada();
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "rmartinez");

        // When
        var act = () => CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // Then: PRE-2 — estado terminal
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Cancelada*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.4 — PRE-5: novedad ya descartada previamente (idempotencia de error)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_novedad_ya_descartada_lanza_NovedadYaDescartadaException_PRE5()
    {
        // Given: stream con NovedadPreopDescartada_v1(NovedadId=9001) previa
        var dados = StreamConNovedadYaDescartada(novedadId: NovedadId9001);
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        // When: segundo intento de descarte sobre la misma novedad
        var act = () => CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // Then: PRE-5 / I4 / INV-ND1 — "la novedad 9001 ya fue descartada"
        act.Should().Throw<NovedadYaDescartadaException>()
            .WithMessage("*9001*");
    }

    [Fact]
    public void DescartarNovedadPreop_novedad_distinta_no_falla_por_PRE5()
    {
        // Given: stream con NovedadId=9001 ya descartada
        var dados = StreamConNovedadYaDescartada(novedadId: NovedadId9001);
        // When: descartar una novedad diferente (9002)
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9002, descartadaPor: "ana.gomez");

        // Then: PRE-5 no aplica (novedadId distinto) — emite el evento
        var resultado = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<NovedadPreopDescartada_v1>()
            .Which.NovedadId.Should().Be(NovedadId9002);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.5 — PRE-6: novedad ya convertida en hallazgo (INV-ND1)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_novedad_ya_importada_como_hallazgo_lanza_NovedadYaConvertidaEnHallazgoException_PRE6()
    {
        // Given: stream con HallazgoRegistrado_v1(Origen=PreOperacional, NovedadPreopOrigenId=9001)
        var dados = StreamConHallazgoPreopConNovedadImportada(novedadPreopOrigenId: NovedadId9001);
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        // When: intento de descartar una novedad que ya fue importada como hallazgo
        var act = () => CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // Then: PRE-6 / INV-ND1 — exclusividad mutua descarte/hallazgo
        act.Should().Throw<NovedadYaConvertidaEnHallazgoException>()
            .WithMessage("*9001*");
    }

    [Fact]
    public void DescartarNovedadPreop_hallazgo_preopcional_con_otra_novedad_no_falla_por_PRE6()
    {
        // Given: stream con HallazgoRegistrado_v1(NovedadPreopOrigenId=9001)
        var dados = StreamConHallazgoPreopConNovedadImportada(novedadPreopOrigenId: NovedadId9001);
        // When: descartar la novedad 9002 (diferente al origen del hallazgo)
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9002, descartadaPor: "ana.gomez");

        // Then: PRE-6 no aplica — NovedadPreopOrigenId=9001 != cmd.NovedadId=9002
        var resultado = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<NovedadPreopDescartada_v1>()
            .Which.NovedadId.Should().Be(NovedadId9002);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.6 — PRE-7 (D-2): novedad no pertenece a la inspección — no se trackea
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-7 no implementada (D-2 opción A del spec §12 P-1): el aggregate no trackea " +
                 "_novedadesImportadas. La validación de pertenencia de la novedad es UX-only " +
                 "en MVP. Un NovedadId arbitrario es aceptado por el aggregate. Ver spec §6.6.")]
    public void DescartarNovedadPreop_novedad_no_pertenece_a_la_inspeccion_lanza_DomainException_PRE7()
    {
        // Escenario §6.6 — D-2 asunción: no se implementa _novedadesImportadas.
        // Si se implementa en el futuro, este test se desactiva el skip y se activa.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.7 — PRE-4: técnico sin capability (solo HTTP — Skip en dominio puro)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-4 capability 'ejecutar-inspeccion' se verifica en middleware HTTP. " +
                 "No aplica al aggregate puro — el dominio no conoce JWT ni HTTP context. " +
                 "Cubierto por DescartarNovedadPreopEndpointTests.")]
    public void DescartarNovedadPreop_sin_capability_ejecutar_inspeccion_lanza_403_PRE4()
    {
        // Escenario §6.7 — responsabilidad de la capa Inspecciones.Api.Tests.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.8 — PRE-1: inspección no encontrada (solo Handler — Skip en dominio puro)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-1 vive en el handler (IDocumentSession.Events.AggregateStreamAsync). " +
                 "Requiere Marten/Testcontainers. Cubierto por DescartarNovedadPreopHandlerTests.")]
    public void DescartarNovedadPreop_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE1()
    {
        // Escenario §6.8 — responsabilidad de la capa Application.Tests.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.9 — Rebuild desde stream (obligatorio — el comando emite ≥1 evento)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_rebuild_desde_stream_reproduce_estado()
    {
        // Given: stream de 2 eventos en orden causal (InspeccionIniciada + NovedadPreopDescartada)
        var stream = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 42,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "ana.gomez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            new NovedadPreopDescartada_v1(
                InspeccionId: InspeccionIdNueva,
                NovedadId: NovedadId9001,
                MotivoDescarte: "Cerrado por ana.gomez el 2026-05-11 14:30 UTC desde Inspecciones",
                DescartadaPor: "ana.gomez",
                DescartadaEn: AhoraDescarte),
        };

        // When: reproyectar los 2 eventos sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: no lanza, estado coherente post-descarte (spec §6.9)
        var aggregate = act.Should().NotThrow().Subject;

        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion,
            "el descarte no transiciona el estado (D-5)");
        aggregate.NovedadesDescartadas.Should().Contain(NovedadId9001,
            "Apply(NovedadPreopDescartada_v1) agrega el NovedadId al set");
        aggregate.Contribuyentes.Should().Contain("ana.gomez",
            "Apply(NovedadPreopDescartada_v1) registra al técnico como contribuyente");
    }

    [Fact]
    public void DescartarNovedadPreop_rebuild_desde_stream_completo_previos_mas_emitidos()
    {
        // Given: previos + emitir el happy path
        var dados = (IReadOnlyList<object>)StreamEnEjecucionBase(tecnicoId: "ana.gomez");
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        var emitidos = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);

        // When: reproyectar el stream completo (previos + emitidos)
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: rebuild no lanza y el estado es coherente con el happy path §6.1
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        aggregate.NovedadesDescartadas.Should().Contain(NovedadId9001);
        aggregate.Contribuyentes.Should().Contain("ana.gomez");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.10 — Motivo autogenerado: verificar plantilla exacta (D-4)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_motivo_autogenerado_sigue_plantilla_D4_exacta()
    {
        // Given: inspección en EnEjecucion
        // TimeProvider devuelve 2026-11-03T09:05:07Z (spec §6.10)
        var ahoraEspecifico = new DateTimeOffset(2026, 11, 3, 9, 5, 7, TimeSpan.Zero);
        var dados = StreamEnEjecucionBase(tecnicoId: "r.martinez");
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9002, descartadaPor: "r.martinez");

        // When
        var resultado = CasoDeUso.Descartar(dados, cmd, ahoraEspecifico);

        // Then: plantilla D-4 exacta — "Cerrado por {usuario} el {fecha:yyyy-MM-dd HH:mm} UTC desde Inspecciones"
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<NovedadPreopDescartada_v1>().Subject;

        evt.MotivoDescarte.Should().Be(
            "Cerrado por r.martinez el 2026-11-03 09:05 UTC desde Inspecciones",
            because: "la plantilla D-4 del spec §13 D-4 define el formato exacto del motivo autogenerado");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.1 extra — TecnicosContribuyentes se actualiza vía Apply (I2b)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescartarNovedadPreop_agregado_al_set_de_contribuyentes_I2b()
    {
        // Given: inspección iniciada por "ana.gomez"
        var dados = StreamEnEjecucionBase(tecnicoId: "ana.gomez");
        var cmd = ComandoDescartarNovedad(novedadId: NovedadId9001, descartadaPor: "ana.gomez");

        // When
        var emitidos = CasoDeUso.Descartar(dados, cmd, AhoraDescarte);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: I2b — el técnico descartador queda registrado como contribuyente
        aggregate.Contribuyentes.Should().Contain("ana.gomez",
            "Apply(NovedadPreopDescartada_v1) agrega DescartadaPor a _contribuyentes (I2b §2.1)");
    }
}
