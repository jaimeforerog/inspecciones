using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.GenerarOTFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.SolicitarOT"/>.
/// Cobertura de §6.1–§6.13 del spec del slice 1k (GenerarOT).
/// Todos los tests fallan con <see cref="NotImplementedException"/> hasta que
/// <c>green</c> implemente <see cref="Inspeccion.SolicitarOT"/>.
///
/// Nota: §6.3 (PRE-1 capability) y §6.9 (idempotencia Wolverine) y §6.10 (PRE-2
/// InspeccionId inexistente) se saltan en tests de dominio puro — viven en capa HTTP
/// o Application.Tests con Marten. Se marcan como Skip.
/// </summary>
public sealed class GenerarOTTests
{
    // ── §6.1 — Happy path: NoPuedeOperar + RequiereIntervencion ─────────────

    [Fact]
    public void GenerarOT_inspeccion_firmada_con_dictamen_NoPuedeOperar_y_hallazgo_RequiereIntervencion_emite_OTSolicitada_v1()
    {
        // Given: stream con firma completa, dictamen NoPuedeOperar, hallazgo RequiereIntervencion
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoGenerarOTUrgente();

        // When
        var resultado = CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then: exactamente un evento OTSolicitada_v1
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<OTSolicitada_v1>();
    }

    [Fact]
    public void GenerarOT_payload_OTSolicitada_v1_contiene_todos_los_campos_del_comando_seccion_6_1()
    {
        // Given
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoGenerarOTUrgente(
            solicitadaPor: "jefe.campo.01",
            responsable: ResponsableCosto.Proyecto,
            prioridad: PrioridadOT.Urgente,
            observaciones: "Equipo fuera de operación — prioridad máxima",
            comentarioJefe: null);

        // When
        var resultado = CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then: payload completo del evento
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<OTSolicitada_v1>().Subject;

        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.SolicitadaPor.Should().Be("jefe.campo.01");
        evt.Responsable.Should().Be(ResponsableCosto.Proyecto);
        evt.Prioridad.Should().Be(PrioridadOT.Urgente);
        evt.Observaciones.Should().Be("Equipo fuera de operación — prioridad máxima");
        evt.ComentarioJefe.Should().BeNull();
        evt.SolicitadaEn.Should().Be(AhoraOT);
    }

    [Fact]
    public void GenerarOT_estado_aggregate_OTSolicitada_es_true_tras_emision()
    {
        // Given
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoGenerarOTUrgente();

        // When
        var emitidos = CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then: reproyectar para verificar estado
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        aggregate.OTSolicitada.Should().BeTrue("Apply(OTSolicitada_v1) debe marcar OTSolicitada=true");
        aggregate.Estado.Should().Be(EstadoInspeccion.Firmada,
            "el estado no cambia a Cerrada al emitir OTSolicitada — cambia cuando la saga confirma M-1");
    }

    // ── §6.2 — Happy path: ConRestriccion + ComentarioJefe ──────────────────

    [Fact]
    public void GenerarOT_con_dictamen_ConRestriccion_y_ComentarioJefe_emite_OTSolicitada_v1_con_payload_correcto()
    {
        // Given: stream con firma completa, dictamen ConRestriccion
        var dados = StreamFirmadoConRestriccion();
        var cmd = ComandoGenerarOTConComentarioJefe(
            solicitadaPor: "supervisor.01",
            comentarioJefe: "Coordinar con David antes de iniciar");

        // When
        var resultado = CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<OTSolicitada_v1>().Subject;

        evt.SolicitadaPor.Should().Be("supervisor.01");
        evt.Responsable.Should().Be(ResponsableCosto.DepartamentoEquipos);
        evt.Prioridad.Should().Be(PrioridadOT.Alta);
        evt.Observaciones.Should().BeNull();
        evt.ComentarioJefe.Should().Be("Coordinar con David antes de iniciar");
    }

    // ── §6.3 — PRE-1: capability "generar-ot" ausente (test de middleware — Skip) ─

    [Fact(Skip = "PRE-1 capability 'generar-ot' se verifica en middleware HTTP (capa Inspecciones.Api.Tests). " +
                 "No aplica al aggregate puro — el dominio no conoce JWT ni HTTP context.")]
    public void GenerarOT_sin_capability_generar_ot_lanza_excepcion_403_PRE_1()
    {
        // Este escenario se cubre en Inspecciones.Api.Tests/GenerarOTEndpointTests.cs
    }

    // ── §6.4 — PRE-3 / I-F4.a: inspección en EnEjecucion (no firmada) ───────

    [Fact]
    public void GenerarOT_en_inspeccion_no_firmada_EnEjecucion_lanza_InspeccionNoFirmadaException_I_F4_a()
    {
        // Given: inspección en estado EnEjecucion, sin firmar
        var dados = StreamEnEjecucion();
        var cmd = ComandoGenerarOTNormal();

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then
        act.Should().Throw<InspeccionNoFirmadaException>()
            .WithMessage("*EnEjecucion*");
    }

    // ── §6.5 — PRE-4 / I-F4.b: sin hallazgos con RequiereIntervencion ────────

    [Fact]
    public void GenerarOT_sin_hallazgos_con_RequiereIntervencion_lanza_SinHallazgosConIntervencionException_I_F4_b()
    {
        // Given: inspección firmada pero sin hallazgos con RequiereIntervencion
        var dados = StreamFirmadoConSoloHallazgoSeguimiento();
        var cmd = ComandoGenerarOTNormal();

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then
        act.Should().Throw<SinHallazgosConIntervencionException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ── §6.6 — PRE-5 / I-F4.c: OT ya solicitada previamente ─────────────────

    [Fact]
    public void GenerarOT_OT_ya_solicitada_previamente_lanza_OTYaSolicitadaException_I_F4_c()
    {
        // Given: stream con OTSolicitada_v1 previo (aggregate.OTSolicitada == true)
        var dados = StreamFirmadoConOTYaSolicitada();
        var cmd = ComandoGenerarOTNormal(solicitadaPor: "jefe.campo.02");

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then
        act.Should().Throw<OTYaSolicitadaException>()
            .WithMessage("*solicitada*");
    }

    // ── §6.7 — PRE-6 / I-F4.d: OT rechazada previamente ─────────────────────

    [Fact]
    public void GenerarOT_OT_rechazada_previamente_lanza_OTRechazadaException_I_F4_d()
    {
        // Given: stream con GeneracionOTRechazada_v1 previo (aggregate.OTRechazada == true)
        var dados = StreamFirmadoConOTRechazada();
        var cmd = ComandoGenerarOTNormal();

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then
        act.Should().Throw<OTRechazadaException>()
            .WithMessage("*rechazada*");
    }

    // ── §6.8 — PRE-7 / I-F4.e: dictamen PuedeOperar ─────────────────────────

    [Fact]
    public void GenerarOT_dictamen_PuedeOperar_lanza_DictamenNoPermiteOTException_I_F4_e()
    {
        // Given: inspección firmada con dictamen PuedeOperar (defensa de segunda línea)
        var dados = StreamFirmadoDictamenPuedeOperar();
        var cmd = ComandoGenerarOTNormal();

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then
        act.Should().Throw<DictamenNoPermiteOTException>()
            .WithMessage("*PuedeOperar*");
    }

    // ── §6.9 — Idempotencia Wolverine (Skip — infra) ──────────────────────────

    [Fact(Skip = "Idempotencia por MessageId/X-Client-Command-Id es responsabilidad de Wolverine envelope dedup. " +
                 "Requiere infra Wolverine+Marten. Cubre Inspecciones.Application.Tests/GenerarOTHandlerTests.cs.")]
    public void GenerarOT_replay_mismo_clientCommandId_no_duplica_evento_ni_re_ejecuta_handler()
    {
        // Escenario §6.9 — responsabilidad de la capa Application.Tests con Wolverine.
    }

    // ── §6.10 — PRE-2: InspeccionId no existe (Skip — handler/Marten) ────────

    [Fact(Skip = "PRE-2 vive en el handler (IDocumentSession.Events.AggregateStreamAsync). " +
                 "Requiere Marten/Testcontainers. Cubre Inspecciones.Application.Tests/GenerarOTHandlerTests.cs.")]
    public void GenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2()
    {
        // Escenario §6.10 — responsabilidad de la capa Application.Tests.
    }

    // ── §6.11 — Caso borde PRE-4: hallazgo RequiereIntervencion eliminado no cuenta ─

    [Fact]
    public void GenerarOT_hallazgo_RequiereIntervencion_eliminado_no_cuenta_para_PRE_4_lanza_SinHallazgosConIntervencionException()
    {
        // Given: inspección firmada donde el único hallazgo con RequiereIntervencion fue eliminado
        var dados = StreamFirmadoConHallazgoIntervencionEliminado();
        var cmd = ComandoGenerarOTNormal();

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then: el hallazgo eliminado no cuenta — misma excepción que PRE-4
        act.Should().Throw<SinHallazgosConIntervencionException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ── §6.12 — PRE-3 variante: inspección CerradaSinOT (estado terminal) ────

    [Fact]
    public void GenerarOT_en_inspeccion_CerradaSinOT_lanza_InspeccionNoFirmadaException_I_F4_a()
    {
        // Given: inspección en estado CerradaSinOT (estado terminal post-firma)
        var dados = StreamCerradaSinOT();
        var cmd = ComandoGenerarOTNormal();

        // When
        var act = () => CasoDeUso.SolicitarOT(dados, cmd, AhoraOT);

        // Then: CerradaSinOT no es Firmada — misma excepción que PRE-3
        act.Should().Throw<InspeccionNoFirmadaException>()
            .WithMessage("*CerradaSinOT*");
    }

    // ── §6.13 — Rebuild desde stream (obligatorio por CLAUDE.md) ─────────────

    [Fact]
    public void GenerarOT_rebuild_desde_stream_7_eventos_reproduce_estado_correcto()
    {
        // Given: reproyectar los 7 eventos del happy path §6.1 en orden causal
        var h1 = HallazgoFixtures.HallazgoG1;

        var stream = new object[]
        {
            // 1. InspeccionIniciada_v1
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 42,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "carlos.ruiz",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            // 2. HallazgoRegistrado_v1
            HallazgoFixtures.HallazgoRegistradoEjemplo(
                hallazgoId: h1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "carlos.ruiz"),
            // 3. AdjuntoSubido_v1
            new AdjuntoSubido_v1(
                InspeccionId: InspeccionIdNueva,
                AdjuntoId: AdjuntoAdj1,
                HallazgoId: h1,
                BlobUri: "https://blobs/adj-fixture.jpg",
                SubidoPor: "carlos.ruiz",
                SubidoEn: Ahora),
            // 4. DiagnosticoEmitido_v1
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Falla estructural",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            // 5. DictamenEstablecido_v1
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.NoPuedeOperar,
                Justificacion: "Brazo hidráulico no operativo",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            // 6. InspeccionFirmada_v1
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
            // 7. OTSolicitada_v1
            new OTSolicitada_v1(
                InspeccionId: InspeccionIdNueva,
                SolicitadaPor: "jefe.campo.01",
                Responsable: ResponsableCosto.Proyecto,
                Prioridad: PrioridadOT.Urgente,
                Observaciones: "Equipo fuera de operación — prioridad máxima",
                ComentarioJefe: null,
                SolicitadaEn: AhoraOT),
        };

        // When: reproyectar los 7 eventos sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: no lanza, estado coherente
        var aggregate = act.Should().NotThrow().Subject;

        aggregate.Estado.Should().Be(EstadoInspeccion.Firmada,
            "el estado no cambia al emitir OTSolicitada — cambia cuando la saga confirma M-1");
        aggregate.Dictamen.Should().Be(DictamenOperacion.NoPuedeOperar);
        aggregate.OTSolicitada.Should().BeTrue();
        aggregate.OTRechazada.Should().BeFalse();
        aggregate.Hallazgos.Should().HaveCount(1, "h1 activo, RequiereIntervencion");
        aggregate.SolicitadaEn.Should().Be(AhoraOT);
    }
}
