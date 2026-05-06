using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.ActualizarHallazgo"/>.
/// Cobertura de §6.1–§6.9 del spec del slice 2.
/// </summary>
public class ActualizarHallazgoTests
{
    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — actualizar hallazgo Manual → RequiereIntervencion
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_manual_a_RequiereIntervencion_emite_HallazgoActualizado_v1()
    {
        // Given: inspección en EnEjecucion con un hallazgo Manual/NoRequiereIntervencion
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarConIntervencion(hallazgoId: HallazgoG1);

        // When
        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>();
    }

    [Fact]
    public void ActualizarHallazgo_manual_a_RequiereIntervencion_payload_correcto()
    {
        // Given
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarConIntervencion(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Fisura en bloque motor",
            accionCorrectiva: "Reemplazar bloque",
            tipoFallaId: 10,
            causaFallaId: 5,
            actualizadoPor: "tecnico-01");

        // When
        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then
        var evento = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        evento.HallazgoId.Should().Be(HallazgoG1);
        evento.NovedadTecnica.Should().Be("Fisura en bloque motor");
        evento.AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        evento.AccionCorrectiva.Should().Be("Reemplazar bloque");
        evento.TipoFallaId.Should().Be(10);
        evento.CausaFallaId.Should().Be(5);
        evento.ActualizadoPor.Should().Be("tecnico-01");
        evento.ActualizadoEn.Should().Be(Ahora);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 Happy path — actualizar hallazgo PreOperacional → RequiereSeguimiento
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_preop_a_RequiereSeguimiento_emite_HallazgoActualizado_v1()
    {
        // Given: inspección en EnEjecucion con un hallazgo PreOperacional/RequiereIntervencion
        var dados = StreamConUnHallazgoPreop(hallazgoId: HallazgoG2);
        var cmd = ComandoActualizarConSeguimiento(
            hallazgoId: HallazgoG2,
            novedadTecnica: "Vibración leve en eje",
            actualizadoPor: "tecnico-02");

        // When
        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then
        var evento = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        evento.HallazgoId.Should().Be(HallazgoG2);
        evento.AccionRequerida.Should().Be(AccionRequerida.RequiereSeguimiento);
        evento.TipoFallaId.Should().BeNull("I-H5 — tipo/causa opcionales para RequiereSeguimiento");
        evento.CausaFallaId.Should().BeNull("I-H5 — tipo/causa opcionales para RequiereSeguimiento");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 Violación PRE-2 — inspección no está en EnEjecucion (I-H7)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_IH7()
    {
        // Given: inspección en estado Firmada con un hallazgo
        var dados = StreamFirmadaConUnHallazgo(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(hallazgoId: HallazgoG1);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 Violación PRE-3 — HallazgoId no existe
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_con_HallazgoId_desconocido_lanza_HallazgoNoEncontradoException()
    {
        // Given: inspección en EnEjecucion sin hallazgos (el HallazgoId no existe)
        var dados = StreamConInspeccionIniciada();
        var hallazgoDesconocido = Guid.Parse("9999b4f7-1234-7abc-8def-000000000099");
        var cmd = ComandoActualizarMinimo(hallazgoId: hallazgoDesconocido);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<HallazgoNoEncontradoException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 Violación PRE-4 — hallazgo eliminado
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_sobre_hallazgo_eliminado_lanza_HallazgoEliminadoException()
    {
        // Given: inspección con hallazgo H1 que fue eliminado
        var dados = StreamConHallazgoEliminado(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(hallazgoId: HallazgoG1);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<HallazgoEliminadoException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 Violación I-H4 — RequiereIntervencion sin TipoFallaId/CausaFallaId (PRE-5)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_RequiereIntervencion_sin_TipoFallaId_lanza_TipoYCausaFallaRequeridosException()
    {
        // Given
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Falla crítica",
            accionRequerida: AccionRequerida.RequiereIntervencion,
            accionCorrectiva: "Reparar",
            tipoFallaId: null,
            causaFallaId: null);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<TipoYCausaFallaRequeridosException>()
            .WithMessage("*RequiereIntervencion*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 Violación PRE-6 — RequiereIntervencion sin AccionCorrectiva
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_RequiereIntervencion_sin_AccionCorrectiva_lanza_AccionCorrectivaRequeridaException()
    {
        // Given
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Falla detectada",
            accionRequerida: AccionRequerida.RequiereIntervencion,
            accionCorrectiva: null,
            tipoFallaId: 5,
            causaFallaId: 3);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<AccionCorrectivaRequeridaException>();
    }

    [Fact]
    public void ActualizarHallazgo_RequiereIntervencion_con_AccionCorrectiva_vacia_lanza_AccionCorrectivaRequeridaException()
    {
        // Given
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Falla detectada",
            accionRequerida: AccionRequerida.RequiereIntervencion,
            accionCorrectiva: "   ",
            tipoFallaId: 5,
            causaFallaId: 3);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<AccionCorrectivaRequeridaException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 Violación PRE-7 — NovedadTecnica vacía
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_con_NovedadTecnica_solo_espacios_lanza_NovedadTecnicaVaciaException()
    {
        // Given
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(
            hallazgoId: HallazgoG1,
            novedadTecnica: "   ",
            accionRequerida: AccionRequerida.NoRequiereIntervencion);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadTecnicaVaciaException>();
    }

    [Fact]
    public void ActualizarHallazgo_con_NovedadTecnica_vacia_lanza_NovedadTecnicaVaciaException()
    {
        // Given
        var dados = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarMinimo(
            hallazgoId: HallazgoG1,
            novedadTecnica: "",
            accionRequerida: AccionRequerida.NoRequiereIntervencion);

        // When / Then
        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        act.Should().Throw<NovedadTecnicaVaciaException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 Rebuild desde stream — Apply puro y orden causal (obligatorio)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_rebuild_desde_stream_reproduce_estado()
    {
        // Given: stream con hallazgo H1 registrado como NoRequiereIntervencion
        var inspeccionIniciada = EventoInspeccionIniciada();
        var hallazgoRegistrado = HallazgoRegistradoEjemplo(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.NoRequiereIntervencion);
        var hallazgoActualizado = new HallazgoActualizado_v1(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: "Fisura en bloque motor",
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: "Reemplazar bloque",
            TipoFallaId: 10,
            CausaFallaId: 5,
            ObservacionCampo: null,
            Ubicacion: null,
            ActualizadoPor: "tecnico-01",
            ActualizadoEn: Ahora);

        var stream = new object[] { inspeccionIniciada, hallazgoRegistrado, hallazgoActualizado };

        // When: reproyectar el stream sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: no lanza y el estado refleja la actualización
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        aggregate.Hallazgos.Should().HaveCount(1);
        aggregate.Hallazgos[0].HallazgoId.Should().Be(HallazgoG1);
        aggregate.Hallazgos[0].AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        aggregate.Hallazgos[0].TipoFallaId.Should().Be(10);
        aggregate.Hallazgos[0].CausaFallaId.Should().Be(5);
        aggregate.Hallazgos[0].NovedadTecnica.Should().Be("Fisura en bloque motor");
    }

    [Fact]
    public void ActualizarHallazgo_rebuild_estado_identico_al_de_decision_in_process()
    {
        // Given: stream previo con hallazgo
        var dadosBase = StreamConUnHallazgoManual(hallazgoId: HallazgoG1);
        var cmd = ComandoActualizarConIntervencion(hallazgoId: HallazgoG1);

        // When: ejecutar decisión y luego rebuildar
        var emitidos = CasoDeUso.ActualizarHallazgo(dadosBase, cmd, Ahora);
        var streamCompleto = dadosBase.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(streamCompleto);

        // Then: rebuild coherente — Apply no valida
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Hallazgos.Should().HaveCount(1);
        aggregate.Hallazgos[0].AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        aggregate.Hallazgos[0].TipoFallaId.Should().Be(10);
        aggregate.Hallazgos[0].CausaFallaId.Should().Be(5);
        aggregate.Contribuyentes.Should().Contain("tecnico-01");
    }
}
