using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.GenerarOTFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.RechazarGenerarOTFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.RechazarOT"/>.
/// Cobertura de §6.1–§6.14 del spec del slice 1l (RechazarGenerarOT).
/// Todos los tests que invocan RechazarOT fallan con <see cref="NotImplementedException"/>
/// hasta que <c>green</c> implemente <see cref="Inspeccion.RechazarOT"/>.
///
/// Nota: §6.3 (PRE-1 capability) y §6.12 (PRE-2 InspeccionId inexistente) y §6.13
/// (idempotencia Wolverine) se saltan en tests de dominio puro — viven en capa HTTP
/// o Application.Tests con Marten. Se marcan como Skip.
/// </summary>
public sealed class RechazarGenerarOTTests
{
    // ── §6.1 — Happy path: NoPuedeOperar + RequiereIntervencion ─────────────

    [Fact]
    public void RechazarGenerarOT_inspeccion_firmada_con_hallazgo_intervencion_emite_dos_eventos_en_orden_causal()
    {
        // Given: stream con firma completa, dictamen NoPuedeOperar, hallazgo RequiereIntervencion
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoRechazarOTUrgente();

        // When
        var resultado = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: exactamente dos eventos en orden causal
        resultado.Should().HaveCount(2, "RechazarGenerarOT emite siempre GeneracionOTRechazada_v1 + InspeccionCerradaSinOT_v1");
        resultado[0].Should().BeOfType<GeneracionOTRechazada_v1>("el rechazo ocurre primero — orden causal");
        resultado[1].Should().BeOfType<InspeccionCerradaSinOT_v1>("el cierre ocurre después — estado terminal");
    }

    [Fact]
    public void RechazarGenerarOT_GeneracionOTRechazada_v1_emitido_antes_de_InspeccionCerradaSinOT_v1()
    {
        // Given
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoRechazarOTUrgente();

        // When
        var resultado = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: los tipos en orden exacto
        resultado.Should().HaveCount(2);
        resultado[0].Should().BeOfType<GeneracionOTRechazada_v1>();
        resultado[1].Should().BeOfType<InspeccionCerradaSinOT_v1>();
    }

    [Fact]
    public void RechazarGenerarOT_payload_GeneracionOTRechazada_v1_contiene_todos_los_campos_correctos_seccion_6_1()
    {
        // Given
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoRechazarOTUrgente(
            rechazadoPor: "jefe.campo.01",
            motivo: MotivoHappyPath);

        // When
        var resultado = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: payload completo del primer evento
        var evt = resultado[0].Should().BeOfType<GeneracionOTRechazada_v1>().Subject;
        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.Motivo.Should().Be(MotivoHappyPath);
        evt.RechazadoPor.Should().Be("jefe.campo.01");
        evt.RechazadaEn.Should().Be(AhoraRechazo);
    }

    [Fact]
    public void RechazarGenerarOT_payload_InspeccionCerradaSinOT_v1_tiene_MotivoCierre_RechazadaPorAprobador()
    {
        // Given
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoRechazarOTUrgente();

        // When
        var resultado = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: payload del segundo evento
        var evt = resultado[1].Should().BeOfType<InspeccionCerradaSinOT_v1>().Subject;
        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.MotivoCierre.Should().Be(MotivoCierreSinOT.RechazadaPorAprobador, "D-5: discriminador para rechazo explícito");
        evt.CerradaEn.Should().Be(AhoraRechazo);
    }

    // ── §6.2 — Happy path: ConRestriccion + RequiereIntervencion ────────────

    [Fact]
    public void RechazarGenerarOT_inspeccion_con_dictamen_ConRestriccion_y_hallazgo_intervencion_emite_dos_eventos()
    {
        // Given: stream con firma completa, dictamen ConRestriccion, hallazgo RequiereIntervencion
        var dados = StreamFirmadoConRestriccion();
        var cmd = ComandoRechazarOTSupervisor();

        // When
        var resultado = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: exactamente dos eventos
        resultado.Should().HaveCount(2);
        resultado[0].Should().BeOfType<GeneracionOTRechazada_v1>().Which.RechazadoPor.Should().Be("supervisor.01");
        resultado[1].Should().BeOfType<InspeccionCerradaSinOT_v1>().Which.MotivoCierre.Should().Be(MotivoCierreSinOT.RechazadaPorAprobador);
    }

    // ── §6.3 — PRE-1: capability "generar-ot" ausente (test de middleware — Skip) ─

    [Fact(Skip = "PRE-1 capability 'generar-ot' se verifica en middleware HTTP (capa Inspecciones.Api.Tests). " +
                 "No aplica al aggregate puro — el dominio no conoce JWT ni HTTP context.")]
    public void RechazarGenerarOT_sin_capability_generar_ot_lanza_excepcion_403_PRE_1()
    {
        // Este escenario se cubre en Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs
    }

    // ── §6.4 — PRE-3: motivo demasiado corto (422) ───────────────────────────

    [Fact]
    public void RechazarGenerarOT_motivo_menor_10_chars_lanza_MotivoRechazoInvalidoException_I_F6()
    {
        // Given: inspección firmada válida, pero motivo "Corto" (5 chars)
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoMotivoCorto();

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then
        act.Should().Throw<MotivoRechazoInvalidoException>()
            .WithMessage("*10*", because: "el mensaje debe indicar el mínimo de 10 caracteres");
    }

    // ── §6.5 — PRE-3: motivo vacío o solo espacios (422) ────────────────────

    [Fact]
    public void RechazarGenerarOT_motivo_vacio_o_solo_espacios_lanza_MotivoRechazoInvalidoException_I_F6()
    {
        // Given: inspección firmada válida, pero motivo solo espacios
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoMotivoSoloEspacios();

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: trim de "   " da longitud 0 < 10
        act.Should().Throw<MotivoRechazoInvalidoException>();
    }

    [Fact]
    public void RechazarGenerarOT_motivo_9_chars_lanza_MotivoRechazoInvalidoException_borde_inferior()
    {
        // Given: motivo de 9 chars (1 debajo del mínimo)
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoMotivoNueveChars();

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: 9 < 10 → inválido
        act.Should().Throw<MotivoRechazoInvalidoException>();
    }

    [Fact]
    public void RechazarGenerarOT_motivo_exactamente_10_chars_es_valido_y_emite_eventos()
    {
        // Given: motivo de exactamente 10 chars (borde inferior válido)
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoMotivoBordeMinimo();   // "1234567890" — 10 chars exactos

        // When
        var resultado = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: borde mínimo válido — se emiten dos eventos
        resultado.Should().HaveCount(2, "10 chars es el mínimo permitido — debe ser válido");
    }

    // ── §6.6 — PRE-4 / I-F6.a: inspección no firmada (EnEjecucion) ──────────

    [Fact]
    public void RechazarGenerarOT_inspeccion_no_firmada_EnEjecucion_lanza_InspeccionNoFirmadaException_I_F6()
    {
        // Given: inspección en estado EnEjecucion, sin firmar
        var dados = GenerarOTFixtures.StreamEnEjecucion();
        var cmd = ComandoRechazarOTUrgente(motivo: "No aplica OT por razones operativas");

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then
        act.Should().Throw<InspeccionNoFirmadaException>()
            .WithMessage("*EnEjecucion*");
    }

    // ── §6.7 — PRE-4 variante: inspección ya CerradaSinOT (estado terminal) ─

    [Fact]
    public void RechazarGenerarOT_inspeccion_cerrada_sin_OT_lanza_InspeccionNoFirmadaException()
    {
        // Given: inspección en estado CerradaSinOT (cerrada previamente por saga automática)
        var dados = GenerarOTFixtures.StreamCerradaSinOT();
        var cmd = ComandoRechazarOTUrgente(motivo: "No aplica OT por razones operativas");

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: CerradaSinOT != Firmada → misma excepción que PRE-4
        act.Should().Throw<InspeccionNoFirmadaException>()
            .WithMessage("*CerradaSinOT*");
    }

    // ── §6.8 — PRE-5 / I-F6.b: sin hallazgos con RequiereIntervencion ────────

    [Fact]
    public void RechazarGenerarOT_sin_hallazgos_con_intervencion_lanza_SinHallazgosConIntervencionException_I_F6()
    {
        // Given: inspección firmada con solo hallazgo RequiereSeguimiento
        var dados = GenerarOTFixtures.StreamFirmadoConSoloHallazgoSeguimiento();
        var cmd = ComandoRechazarOTUrgente(motivo: "Rechazo por razones operativas evidentes");

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then
        act.Should().Throw<SinHallazgosConIntervencionException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ── §6.9 — PRE-5 variante: hallazgo RequiereIntervencion eliminado no cuenta ─

    [Fact]
    public void RechazarGenerarOT_hallazgo_intervencion_eliminado_no_cuenta_lanza_SinHallazgosConIntervencionException_I_F6()
    {
        // Given: inspección firmada donde el único hallazgo RequiereIntervencion fue eliminado
        var dados = GenerarOTFixtures.StreamFirmadoConHallazgoIntervencionEliminado();
        var cmd = ComandoRechazarOTUrgente(motivo: "Rechazo por razones operativas evidentes");

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: hallazgo eliminado no cuenta — misma excepción que PRE-5
        act.Should().Throw<SinHallazgosConIntervencionException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ── §6.10 — PRE-6 / I-F6.c: OT ya solicitada (409) ─────────────────────

    [Fact]
    public void RechazarGenerarOT_OT_ya_solicitada_lanza_OTYaSolicitadaException_I_F6()
    {
        // Given: stream con OTSolicitada_v1 previo (aggregate.OTSolicitada == true)
        var dados = GenerarOTFixtures.StreamFirmadoConOTYaSolicitada();
        var cmd = ComandoRechazarOTUrgente(motivo: "Rechazo tardío, no debería proceder");

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then
        act.Should().Throw<OTYaSolicitadaException>()
            .WithMessage("*solicitada*");
    }

    // ── §6.11 — PRE-7 / I-F6.d: OT ya rechazada (409 — defensa aislada) ────

    [Fact]
    public void RechazarGenerarOT_OT_ya_rechazada_estado_firmada_lanza_OTYaRechazadaException_I_F6()
    {
        // Given: stream hipotéticamente inconsistente — OTRechazada=true pero Estado=Firmada
        // (sin InspeccionCerradaSinOT_v1 subsiguiente). Activa PRE-7 directamente.
        var dados = StreamFirmadoConOTRechazadaSinCierre();
        var cmd = ComandoSegundoRechazo();

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: PRE-7 (OTYaRechazadaException) — defensa de segunda línea
        act.Should().Throw<OTYaRechazadaException>()
            .WithMessage("*rechazada*");
    }

    [Fact]
    public void RechazarGenerarOT_doble_rechazo_completo_PRE4_intercepta_antes_que_PRE7()
    {
        // Given: stream con rechazo completo (GeneracionOTRechazada_v1 + InspeccionCerradaSinOT_v1)
        // → aggregate.Estado == CerradaSinOT — PRE-4 dispara antes que PRE-7 (spec §6.11 nota)
        var dados = StreamRechazadoCompleto();
        var cmd = ComandoSegundoRechazo();

        // When
        var act = () => CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);

        // Then: PRE-4 intercepta (Estado=CerradaSinOT != Firmada) — no llega a PRE-7
        act.Should().Throw<InspeccionNoFirmadaException>()
            .WithMessage("*CerradaSinOT*", because: "PRE-4 tiene precedencia — spec §6.11");
    }

    // ── §6.12 — PRE-2: InspeccionId no existe (Skip — handler/Marten) ────────

    [Fact(Skip = "PRE-2 vive en el handler (IDocumentSession.Events.AggregateStreamAsync). " +
                 "Requiere Marten/Testcontainers. Cubre Inspecciones.Application.Tests/RechazarGenerarOTHandlerTests.cs.")]
    public void RechazarGenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2()
    {
        // Escenario §6.12 — responsabilidad de la capa Application.Tests.
    }

    // ── §6.13 — Idempotencia Wolverine (Skip — infra) ────────────────────────

    [Fact(Skip = "Idempotencia por MessageId/X-Client-Command-Id es responsabilidad de Wolverine envelope dedup. " +
                 "Requiere infra Wolverine+Marten. Cubre Inspecciones.Application.Tests/RechazarGenerarOTHandlerTests.cs.")]
    public void RechazarGenerarOT_replay_mismo_clientCommandId_no_duplica_eventos_ni_re_ejecuta_handler()
    {
        // Escenario §6.13 — responsabilidad de la capa Application.Tests con Wolverine.
    }

    // ── §6.14 — Rebuild desde stream — Apply puro y orden causal (obligatorio) ─

    [Fact]
    public void RechazarGenerarOT_rebuild_desde_stream_7_eventos_estado_correcto()
    {
        // Given: los 7 eventos del happy path §6.1 en orden causal
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
            // 3. DiagnosticoEmitido_v1
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Falla estructural en brazo hidráulico",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            // 4. DictamenEstablecido_v1
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.NoPuedeOperar,
                Justificacion: "Brazo hidráulico no operativo",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            // 5. InspeccionFirmada_v1
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
            // 6. GeneracionOTRechazada_v1
            new GeneracionOTRechazada_v1(
                InspeccionId: InspeccionIdNueva,
                Motivo: MotivoHappyPath,
                RechazadoPor: "jefe.campo.01",
                RechazadaEn: AhoraRechazo),
            // 7. InspeccionCerradaSinOT_v1
            new InspeccionCerradaSinOT_v1(
                InspeccionId: InspeccionIdNueva,
                MotivoCierre: MotivoCierreSinOT.RechazadaPorAprobador,
                CerradaEn: AhoraRechazo),
        };

        // When: reproyectar los 7 eventos sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: no lanza, estado coherente post-rechazo
        var aggregate = act.Should().NotThrow().Subject;

        aggregate.Estado.Should().Be(EstadoInspeccion.CerradaSinOT, "InspeccionCerradaSinOT_v1 transiciona al estado terminal");
        aggregate.OTRechazada.Should().BeTrue("Apply(GeneracionOTRechazada_v1) pone OTRechazada=true");
        aggregate.MotivoRechazoOT.Should().Be(MotivoHappyPath, "Apply(GeneracionOTRechazada_v1) persiste el motivo textual");
        aggregate.OTSolicitada.Should().BeFalse("no hay OTSolicitada_v1 en el stream");
        aggregate.Dictamen.Should().Be(DictamenOperacion.NoPuedeOperar);
        aggregate.Hallazgos.Should().HaveCount(1, "h1 activo, RequiereIntervencion");
    }

    // ── Estado post-comando (complementa §6.1) ──────────────────────────────

    [Fact]
    public void RechazarGenerarOT_estado_post_comando_OTRechazada_true_MotivoRechazoOT_seteado()
    {
        // Given
        var dados = StreamFirmadoNoPuedeOperar();
        var cmd = ComandoRechazarOTUrgente();

        // When: emitir eventos y reproyectar
        var emitidos = CasoDeUso.RechazarOT(dados, cmd, AhoraRechazo);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: estado materializado completo
        aggregate.OTRechazada.Should().BeTrue();
        aggregate.MotivoRechazoOT.Should().Be(MotivoHappyPath);
        aggregate.Estado.Should().Be(EstadoInspeccion.CerradaSinOT);
        aggregate.OTSolicitada.Should().BeFalse();
    }
}
