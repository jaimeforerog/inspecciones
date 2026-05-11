using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.Cancelar"/>.
/// Cobertura de §6.1–§6.16 del spec del slice 1m (CancelarInspeccion).
/// Todos los tests que invocan Cancelar fallan con <see cref="NotImplementedException"/>
/// hasta que <c>green</c> implemente <see cref="Inspeccion.Cancelar"/>.
/// Los tests de rebuild pasan desde el estado rojo si Apply es puro (ya lo es).
///
/// Decisión P-1 firmada: solo contribuyentes pueden cancelar (D-5).
/// </summary>
public sealed class CancelarInspeccionTests
{
    /// <summary>Timestamp de cancelación alineado con el spec §6.1 (2026-05-11T10:00:00Z).</summary>
    private static readonly DateTimeOffset AhoraCancelacion =
        new(2026, 5, 11, 10, 0, 0, TimeSpan.Zero);

    // ── Helpers de fixtures ──────────────────────────────────────────────────

    /// <summary>
    /// Stream base mínimo: solo <see cref="InspeccionIniciada_v1"/>.
    /// Estado EnEjecucion, TecnicoIniciador="carlos.ruiz", TecnicosContribuyentes={"carlos.ruiz"}.
    /// </summary>
    private static object[] StreamEnEjecucion(
        int equipoId = 42,
        string tecnicoId = "carlos.ruiz",
        TipoInspeccion tipo = TipoInspeccion.Tecnica) =>
    [
        new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdNueva,
            Tipo: tipo,
            EquipoId: equipoId,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: tecnicoId,
            ProyectoId: 3,
            Ubicacion: UbicacionTipo(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null),
    ];

    /// <summary>
    /// Stream en ejecución con dos hallazgos: h1=RequiereIntervencion, h2=RequiereSeguimiento.
    /// Usado para verificar que los hallazgos no se eliminan al cancelar (§6.2, §6.16).
    /// </summary>
    private static object[] StreamEnEjecucionConDosHallazgos()
    {
        var h1 = HallazgoG1;
        var h2 = HallazgoG2;
        return
        [
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
            HallazgoRegistradoEjemplo(
                hallazgoId: h1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "carlos.ruiz"),
            HallazgoRegistradoEjemplo(
                hallazgoId: h2,
                accionRequerida: AccionRequerida.RequiereSeguimiento,
                emitidoPor: "carlos.ruiz"),
        ];
    }

    /// <summary>
    /// Stream de monitoreo en ejecución con un ítem omitido.
    /// Usado para §6.3 (cancelar inspección de monitoreo).
    /// </summary>
    private static object[] StreamMonitoreoEnEjecucionConItemOmitido()
    {
        return
        [
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Monitoreo,
                EquipoId: 42,
                RutinaId: 99,
                RutinaCodigo: "MON-BULL-MOTOR",
                TecnicoIniciador: "juan.perez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            new ItemMonitoreoOmitido_v1(
                InspeccionId: InspeccionIdNueva,
                ItemId: 5,
                Motivo: "Componente inaccesible por mantenimiento",
                EmitidoPor: "juan.perez",
                OmitidoEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream con segundo contribuyente: juan.perez registró un hallazgo en la inspección
    /// iniciada por carlos.ruiz, lo que lo convierte en contribuyente (I-I2b del modelo).
    /// Usado para §6.13 (segundo contribuyente puede cancelar).
    /// </summary>
    private static object[] StreamConDosContribuyentes()
    {
        var h1 = HallazgoG1;
        return
        [
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
            HallazgoRegistradoEjemplo(
                hallazgoId: h1,
                accionRequerida: AccionRequerida.NoRequiereIntervencion,
                emitidoPor: "juan.perez"),
        ];
    }

    // ── §6.1 — Happy path: cancelar inspección técnica en ejecución (sin hallazgos) ──

    [Fact]
    public void CancelarInspeccion_en_ejecucion_emite_InspeccionCancelada_v1()
    {
        // Given: inspección técnica en EnEjecucion sin hallazgos
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When: comando de cancelación con motivo válido (≥10 chars)
        var resultado = CasoDeUso.Cancelar(
            dados,
            motivo: "Equipo trasladado a otra obra sin previo aviso",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: exactamente un evento
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionCancelada_v1>();
    }

    [Fact]
    public void CancelarInspeccion_en_ejecucion_emite_payload_completo_y_correcto()
    {
        // Given
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When
        var resultado = CasoDeUso.Cancelar(
            dados,
            motivo: "Equipo trasladado a otra obra sin previo aviso",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: payload del evento completo
        var evt = resultado.Should().ContainSingle().Which.Should()
            .BeOfType<InspeccionCancelada_v1>().Subject;
        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.Motivo.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        evt.CanceladaPor.Should().Be("carlos.ruiz");
        evt.CanceladaEn.Should().Be(AhoraCancelacion);
    }

    // ── §6.2 — Happy path: cancelar con hallazgos registrados (hallazgos permanecen) ──

    [Fact]
    public void CancelarInspeccion_con_hallazgos_emite_InspeccionCancelada_v1_hallazgos_permanecen()
    {
        // Given: inspección con 2 hallazgos (RequiereIntervencion + RequiereSeguimiento)
        var dados = StreamEnEjecucionConDosHallazgos();

        // When: cancelar la inspección
        var resultado = CasoDeUso.Cancelar(
            dados,
            motivo: "Error de selección de equipo, se reinspeccionará mañana",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: un solo InspeccionCancelada_v1 — los hallazgos no se tocan
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionCancelada_v1>();

        // Verificar que el aggregate no pierde hallazgos al reproyectar
        var stream = dados.Concat(resultado).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);
        aggregate.Hallazgos.Should().HaveCount(2,
            "los hallazgos permanecen en el stream como histórico auditado tras la cancelación");
    }

    // ── §6.3 — Happy path: cancelar inspección de monitoreo ──

    [Fact]
    public void CancelarInspeccion_inspeccion_tipo_monitoreo_emite_InspeccionCancelada_v1()
    {
        // Given: inspección de monitoreo en EnEjecucion con ítem omitido
        var dados = StreamMonitoreoEnEjecucionConItemOmitido();

        // When
        var resultado = CasoDeUso.Cancelar(
            dados,
            motivo: "Equipo fuera de operación por falla eléctrica",
            canceladaPor: "juan.perez",
            canceladaEn: AhoraCancelacion);

        // Then: un solo evento — la cancelación aplica a cualquier tipo
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionCancelada_v1>();
    }

    // ── §6.4 PRE-1 — capability ausente (Skip — dominio puro no conoce JWT) ──

    [Fact(Skip = "PRE-1 capability 'ejecutar-inspeccion' se verifica en middleware HTTP. " +
                 "No aplica al aggregate puro — el dominio no conoce JWT ni HTTP context. " +
                 "Cubierto por CancelarInspeccionEndpointTests.")]
    public void CancelarInspeccion_sin_capability_ejecutar_inspeccion_lanza_403_PRE_1()
    {
        // Escenario §6.4 — responsabilidad de la capa Inspecciones.Api.Tests.
    }

    // ── §6.5 PRE-2 — inspección inexistente (Skip — requiere Marten) ──

    [Fact(Skip = "PRE-2 vive en el handler (IDocumentSession.Events.AggregateStreamAsync). " +
                 "Requiere Marten/Testcontainers. Cubierto por CancelarInspeccionHandlerTests.")]
    public void CancelarInspeccion_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2()
    {
        // Escenario §6.5 — responsabilidad de la capa Application.Tests.
    }

    // ── §6.6 PRE-3 — técnico no contribuyente ──

    [Fact]
    public void CancelarInspeccion_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException()
    {
        // Given: inspección con TecnicosContribuyentes={"carlos.ruiz"}
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When: intento de cancelar con técnico externo no contribuyente
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "Motivo de cancelación suficientemente largo",
            canceladaPor: "tecnico.externo.99",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-3 — solo contribuyentes pueden cancelar
        act.Should().Throw<TecnicoNoContribuyenteException>()
            .WithMessage("*tecnico.externo.99*");
    }

    // ── §6.7 PRE-4 — motivo vacío ──

    [Fact]
    public void CancelarInspeccion_motivo_vacio_lanza_MotivoCancelacionInvalidoException()
    {
        // Given: inspección en EnEjecucion
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When: motivo vacío
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-4 (I6-MOTIVO)
        act.Should().Throw<MotivoCancelacionInvalidoException>();
    }

    // ── §6.8 PRE-4 — motivo solo espacios ──

    [Fact]
    public void CancelarInspeccion_motivo_solo_espacios_lanza_MotivoCancelacionInvalidoException()
    {
        // Given: inspección en EnEjecucion
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When: motivo solo espacios — trim da longitud 0 < 10
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "   ",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-4
        act.Should().Throw<MotivoCancelacionInvalidoException>();
    }

    // ── §6.9 PRE-4 — motivo con menos de 10 chars ──

    [Fact]
    public void CancelarInspeccion_motivo_menor_10_chars_lanza_MotivoCancelacionInvalidoException()
    {
        // Given: inspección en EnEjecucion
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When: motivo "Corto" — 5 chars < 10
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "Corto",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: 5 < 10 → inválido
        act.Should().Throw<MotivoCancelacionInvalidoException>()
            .WithMessage("*10*", because: "el mensaje debe indicar el mínimo de 10 caracteres");
    }

    [Fact]
    public void CancelarInspeccion_motivo_exactamente_10_chars_es_valido_y_emite_evento()
    {
        // Given: inspección en EnEjecucion
        var dados = StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        // When: motivo de exactamente 10 chars (borde inferior válido)
        var resultado = CasoDeUso.Cancelar(
            dados,
            motivo: "1234567890",   // 10 chars exactos
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: borde mínimo válido — se emite el evento
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionCancelada_v1>();
    }

    // ── §6.10 PRE-5 / I6 — inspección ya firmada (I-F1) ──

    [Fact]
    public void CancelarInspeccion_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I6()
    {
        // Given: stream con inspección firmada (Estado=Firmada)
        var dados = StreamConInspeccionFirmada();

        // When: intento de cancelar una inspección firmada — I-F1 prohíbe esto
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "Intento de cancelar inspección ya firmada",
            canceladaPor: "rmartinez",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-5 — I6: solo EnEjecucion; I-F1: no se puede cancelar post-firma
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ── §6.11 PRE-5 / I6 — inspección ya cancelada (idempotencia natural) ──

    [Fact]
    public void CancelarInspeccion_inspeccion_ya_cancelada_lanza_InspeccionNoEnEjecucionException_I6()
    {
        // Given: stream con InspeccionIniciada + InspeccionCancelada (Estado=Cancelada)
        var dados = StreamConInspeccionCancelada();

        // When: segundo intento de cancelación
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "Segundo intento de cancelar",
            canceladaPor: "rmartinez",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-5 — Estado=Cancelada != EnEjecucion
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Cancelada*");
    }

    // ── §6.12 PRE-5 / I6 — inspección cerrada (CerradaSinOT) ──

    [Fact]
    public void CancelarInspeccion_inspeccion_cerrada_sin_OT_lanza_InspeccionNoEnEjecucionException_I6()
    {
        // Given: stream con Estado=CerradaSinOT
        var dados = GenerarOTFixtures.StreamCerradaSinOT();

        // When: intento de cancelar inspección ya cerrada
        var act = () => CasoDeUso.Cancelar(
            dados,
            motivo: "Intentando cancelar una inspección cerrada",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-5 — CerradaSinOT != EnEjecucion
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*CerradaSinOT*");
    }

    // ── §6.13 — Segundo contribuyente puede cancelar ──

    [Fact]
    public void CancelarInspeccion_segundo_contribuyente_puede_cancelar()
    {
        // Given: stream donde "carlos.ruiz" inició y "juan.perez" registró un hallazgo
        // → ambos son contribuyentes (I-I2b)
        var dados = StreamConDosContribuyentes();

        // Verificar que el aggregate tiene los dos contribuyentes
        var aggregate = Inspeccion.Reconstruir(dados);
        aggregate.Contribuyentes.Should().Contain("carlos.ruiz");
        aggregate.Contribuyentes.Should().Contain("juan.perez");

        // When: juan.perez (segundo contribuyente) cancela
        var resultado = CasoDeUso.Cancelar(
            dados,
            motivo: "El técnico iniciador no puede continuar por emergencia",
            canceladaPor: "juan.perez",
            canceladaEn: AhoraCancelacion);

        // Then: PRE-3 se cumple porque juan.perez es contribuyente — no lanza
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionCancelada_v1>()
            .Which.CanceladaPor.Should().Be("juan.perez");
    }

    // ── §6.14 — Idempotencia Wolverine (Skip — infra) ──

    [Fact(Skip = "Idempotencia por MessageId/X-Client-Command-Id es responsabilidad de Wolverine envelope dedup. " +
                 "Requiere infra Wolverine+Marten. Cubierto por CancelarInspeccionEndpointTests (ADR-008).")]
    public void CancelarInspeccion_replay_mismo_clientCommandId_no_duplica_eventos_ni_re_ejecuta_handler()
    {
        // Escenario §6.14 — responsabilidad de la capa E2E.
    }

    // ── §6.15 — Rebuild desde stream — Apply puro (obligatorio) ──

    [Fact]
    public void CancelarInspeccion_rebuild_desde_stream_2_eventos_estado_correcto()
    {
        // Given: stream de 2 eventos en orden causal (§6.15 — orden obligatorio)
        var stream = new object[]
        {
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
            new InspeccionCancelada_v1(
                InspeccionId: InspeccionIdNueva,
                Motivo: "Equipo trasladado a otra obra sin previo aviso",
                CanceladaPor: "carlos.ruiz",
                CanceladaEn: AhoraCancelacion),
        };

        // When: reproyectar los 2 eventos sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: no lanza, estado coherente post-cancelación
        var aggregate = act.Should().NotThrow().Subject;

        aggregate.Estado.Should().Be(EstadoInspeccion.Cancelada,
            "InspeccionCancelada_v1 transiciona al estado terminal Cancelada");
        aggregate.MotivoCancelacion.Should().Be("Equipo trasladado a otra obra sin previo aviso",
            "Apply(InspeccionCancelada_v1) persiste el motivo textual");
        aggregate.Contribuyentes.Should().Contain("carlos.ruiz",
            "Apply(InspeccionCancelada_v1) registra al cancelador como contribuyente");
        aggregate.Hallazgos.Should().BeEmpty("no se registraron hallazgos en este stream");
    }

    // ── §6.16 — Rebuild con hallazgos — hallazgos persisten en stream ──

    [Fact]
    public void CancelarInspeccion_rebuild_con_hallazgos_hallazgos_persisten_estado_cancelada()
    {
        // Given: stream de 4 eventos en orden causal (§6.16)
        var h1 = HallazgoG1;
        var h2 = HallazgoG2;

        var stream = new object[]
        {
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
            HallazgoRegistradoEjemplo(
                hallazgoId: h1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "carlos.ruiz"),
            HallazgoRegistradoEjemplo(
                hallazgoId: h2,
                accionRequerida: AccionRequerida.RequiereSeguimiento,
                emitidoPor: "carlos.ruiz"),
            new InspeccionCancelada_v1(
                InspeccionId: InspeccionIdNueva,
                Motivo: "Error de selección de equipo, se reinspeccionará mañana",
                CanceladaPor: "carlos.ruiz",
                CanceladaEn: AhoraCancelacion),
        };

        // When: reproyectar los 4 eventos
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: estado correcto y hallazgos preservados
        var aggregate = act.Should().NotThrow().Subject;

        aggregate.Estado.Should().Be(EstadoInspeccion.Cancelada);
        aggregate.Hallazgos.Should().HaveCount(2,
            "los hallazgos h1 y h2 permanecen en el stream como histórico auditado");
        aggregate.MotivoCancelacion.Should().Be("Error de selección de equipo, se reinspeccionará mañana");
    }

    // ── Rebuild completo desde happy path + emitidos (estado post-comando) ──

    [Fact]
    public void CancelarInspeccion_rebuild_desde_stream_reproduce_estado_post_comando()
    {
        // Given: stream previo (solo inicio) + emitir el evento de cancelación
        var dados = (IReadOnlyList<object>)StreamEnEjecucion(tecnicoId: "carlos.ruiz");

        var emitidos = CasoDeUso.Cancelar(
            dados,
            motivo: "Equipo trasladado a otra obra sin previo aviso",
            canceladaPor: "carlos.ruiz",
            canceladaEn: AhoraCancelacion);

        // When: reproyectar el stream completo (previos + emitidos)
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: el rebuild no lanza y el estado resultante es coherente con §6.1
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Estado.Should().Be(EstadoInspeccion.Cancelada);
        aggregate.MotivoCancelacion.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        aggregate.Contribuyentes.Should().Contain("carlos.ruiz");
    }
}
