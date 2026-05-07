using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.Firmar"/>.
/// Cobertura de §6.1–§6.18 del spec del slice 1g.
/// Todos los tests de happy path fallan con <see cref="NotImplementedException"/>
/// hasta que <c>green</c> implemente <see cref="Inspeccion.Firmar"/>.
/// Los tests de invariante fallan porque la excepción esperada no se lanza
/// (el método lanza <see cref="NotImplementedException"/> antes de llegar a la guardia).
///
/// Decisión P-1: V-F2 no se testa al nivel del aggregate (solo UX enforcement).
/// Decisión P-2: PRE-9 solo permite contribuyentes — sin capability de supervisor en este slice.
/// </summary>
public sealed class FirmarInspeccionTests
{
    private static readonly Guid AdjuntoA1 = new("0194a000-0000-7000-0000-000000000001");
    private static readonly Guid AdjuntoA2 = new("0194a000-0000-7000-0000-000000000002");

    // ── Helpers de fixtures ──────────────────────────────────────────────────

    /// <summary>GPS estándar para la firma (distinto al de inicio para reflejar re-captura).</summary>
    private static UbicacionGps UbicacionFirmaEjemplo() =>
        new(Latitud: 4.7m, Longitud: -74.1m, PrecisionMetros: 5.0m, CapturadoEn: Ahora);

    /// <summary>Comando happy path mínimo: hallazgo NoRequiereIntervencion, PuedeOperar.</summary>
    private static FirmarInspeccion ComandoFirmarBasico(
        string tecnicoId = "rmartinez",
        DictamenOperacion dictamen = DictamenOperacion.PuedeOperar,
        string justificacion = "Equipo en buen estado",
        string firmaUri = "https://blobs/firma-01.png",
        UbicacionGps? ubicacionFirma = null) =>
        new(InspeccionId: InspeccionIdNueva,
            Diagnostico: "Inspección sin hallazgos críticos",
            Dictamen: dictamen,
            JustificacionDictamen: justificacion,
            FirmaUri: firmaUri,
            UbicacionFirma: ubicacionFirma ?? UbicacionFirmaEjemplo(),
            TecnicoId: tecnicoId);

    /// <summary>
    /// Stream base válido para firmar: InspeccionIniciada + un hallazgo NoRequiereIntervencion.
    /// El iniciador es "rmartinez" → es contribuyente → puede firmar (PRE-9).
    /// </summary>
    private static object[] StreamParaFirmarSinIntervencion(
        Guid? hallazgoId = null,
        string tecnicoIniciador = "rmartinez") =>
        [
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: tecnicoIniciador,
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: hallazgoId ?? HallazgoG1,
                accionRequerida: AccionRequerida.NoRequiereIntervencion,
                emitidoPor: tecnicoIniciador)
        ];

    /// <summary>
    /// Stream válido para firmar con NoPuedeOperar:
    /// hallazgo RequiereIntervencion + TipoFallaId + CausaFallaId + adjunto.
    /// </summary>
    private static object[] StreamParaFirmarConIntervencion(
        Guid? hallazgoId = null,
        bool incluirAdjunto = true,
        bool adjuntoEliminado = false)
    {
        var hid = hallazgoId ?? HallazgoG1;
        var eventos = new List<object>
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: hid,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "rmartinez")
        };

        if (incluirAdjunto)
        {
            eventos.Add(new AdjuntoSubido_v1(InspeccionIdNueva, AdjuntoA1, hid, "https://blobs/adj1.jpg", "rmartinez", Ahora));
            if (adjuntoEliminado)
            {
                eventos.Add(new AdjuntoEliminado_v1(InspeccionIdNueva, AdjuntoA1, hid, "rmartinez", Ahora));
            }
        }

        return eventos.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — firma con hallazgo NoRequiereIntervencion (PuedeOperar)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_happy_path_NoRequiereIntervencion_PuedeOperar_emite_tres_eventos_en_orden()
    {
        // Given: aggregate con InspeccionIniciada + hallazgo NoRequiereIntervencion
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(tecnicoId: "rmartinez", dictamen: DictamenOperacion.PuedeOperar);

        // When
        var resultado = CasoDeUso.Firmar(dados, cmd, Ahora);

        // Then: exactamente 3 eventos en orden causal
        resultado.Should().HaveCount(3, "FirmarInspeccion emite 3 eventos en un acto atómico");
        resultado[0].Should().BeOfType<DiagnosticoEmitido_v1>();
        resultado[1].Should().BeOfType<DictamenEstablecido_v1>();
        resultado[2].Should().BeOfType<InspeccionFirmada_v1>();
    }

    [Fact]
    public void FirmarInspeccion_happy_path_payload_DiagnosticoEmitido_v1_correcto()
    {
        // Given
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(tecnicoId: "rmartinez");

        // When
        var resultado = CasoDeUso.Firmar(dados, cmd, Ahora);

        // Then
        var evtDiag = resultado[0].Should().BeOfType<DiagnosticoEmitido_v1>().Subject;
        evtDiag.InspeccionId.Should().Be(InspeccionIdNueva);
        evtDiag.DiagnosticoFinal.Should().Be("Inspección sin hallazgos críticos");
        evtDiag.EmitidoPor.Should().Be("rmartinez");
        evtDiag.EmitidoEn.Should().Be(Ahora);
    }

    [Fact]
    public void FirmarInspeccion_happy_path_payload_DictamenEstablecido_v1_correcto()
    {
        // Given
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(tecnicoId: "rmartinez", dictamen: DictamenOperacion.PuedeOperar, justificacion: "Equipo en buen estado");

        // When
        var resultado = CasoDeUso.Firmar(dados, cmd, Ahora);

        // Then
        var evtDict = resultado[1].Should().BeOfType<DictamenEstablecido_v1>().Subject;
        evtDict.InspeccionId.Should().Be(InspeccionIdNueva);
        evtDict.Dictamen.Should().Be(DictamenOperacion.PuedeOperar);
        evtDict.Justificacion.Should().Be("Equipo en buen estado");
        evtDict.EmitidoPor.Should().Be("rmartinez");
        evtDict.EstablecidoEn.Should().Be(Ahora);
    }

    [Fact]
    public void FirmarInspeccion_happy_path_payload_InspeccionFirmada_v1_correcto()
    {
        // Given
        var ubicacionFirma = UbicacionFirmaEjemplo();
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(
            tecnicoId: "rmartinez",
            firmaUri: "https://blobs/firma-01.png",
            ubicacionFirma: ubicacionFirma);

        // When
        var resultado = CasoDeUso.Firmar(dados, cmd, Ahora);

        // Then
        var evtFirma = resultado[2].Should().BeOfType<InspeccionFirmada_v1>().Subject;
        evtFirma.InspeccionId.Should().Be(InspeccionIdNueva);
        evtFirma.FirmadoPor.Should().Be("rmartinez");
        evtFirma.FirmaUri.Should().Be("https://blobs/firma-01.png");
        evtFirma.UbicacionFirma.Latitud.Should().Be(4.7m);
        evtFirma.FirmadaEn.Should().Be(Ahora);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.2 Happy path — firma con hallazgo RequiereIntervencion (NoPuedeOperar)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_happy_path_RequiereIntervencion_NoPuedeOperar_emite_tres_eventos()
    {
        // Given: aggregate con hallazgo RequiereIntervencion + TipoFallaId + CausaFallaId + adjunto
        var dados = StreamParaFirmarConIntervencion();
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Falla estructural confirmada",
            Dictamen: DictamenOperacion.NoPuedeOperar,
            JustificacionDictamen: "Falla estructural",
            FirmaUri: "https://blobs/firma-02.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When
        var resultado = CasoDeUso.Firmar(dados, cmd, Ahora);

        // Then
        resultado.Should().HaveCount(3);
        resultado[1].Should().BeOfType<DictamenEstablecido_v1>()
            .Which.Dictamen.Should().Be(DictamenOperacion.NoPuedeOperar);
        resultado[2].Should().BeOfType<InspeccionFirmada_v1>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.3 Happy path — firma con hallazgo RequiereSeguimiento (ConRestriccion) — V-F8 válido
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_happy_path_RequiereSeguimiento_ConRestriccion_no_lanza()
    {
        // Given: aggregate con hallazgo RequiereSeguimiento
        var dados = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                accionRequerida: AccionRequerida.RequiereSeguimiento,
                emitidoPor: "rmartinez")
        };
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Monitoreo requerido",
            Dictamen: DictamenOperacion.ConRestriccion,
            JustificacionDictamen: "Monitoreo requerido",
            FirmaUri: "https://blobs/firma-03.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When / Then: no lanza — V-F8 es válido con ConRestriccion cuando hay RequiereSeguimiento
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);
        act.Should().NotThrow("V-F8 permite ConRestriccion cuando existen hallazgos con RequiereSeguimiento");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.4 Violación PRE-2 — inspección ya firmada (V-F7)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_en_inspeccion_ya_firmada_lanza_InspeccionNoEnEjecucionException_PRE_2()
    {
        // Given: aggregate con InspeccionFirmada_v1 ya aplicado (Estado=Firmada)
        var dados = StreamConInspeccionFirmada();
        var cmd = ComandoFirmarBasico();

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.5 Violación PRE-3 — sin hallazgos (V-F1)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_sin_hallazgos_registrados_lanza_SinHallazgosException_PRE_3_V_F1()
    {
        // Given: aggregate con InspeccionIniciada y sin ningún hallazgo
        var dados = StreamConInspeccionIniciada();
        var cmd = ComandoFirmarBasico();

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<SinHallazgosException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.6 Violación PRE-3 — todos los hallazgos eliminados (V-F1)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_con_todos_hallazgos_eliminados_lanza_SinHallazgosException_PRE_3_V_F1()
    {
        // Given: aggregate con un hallazgo eliminado (ningún hallazgo vigente)
        var dados = StreamConHallazgoEliminado();
        var cmd = ComandoFirmarBasico();

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<SinHallazgosException>(
            "todos los hallazgos del stream están eliminados — ninguno vigente para firmar");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.7 Violación PRE-5 — dictamen PuedeOperar con hallazgo RequiereSeguimiento (V-F8)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_dictamen_PuedeOperar_con_hallazgo_RequiereSeguimiento_lanza_DictamenIncoherenteException_PRE_5_V_F8()
    {
        // Given: aggregate con hallazgo RequiereSeguimiento
        var dados = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                accionRequerida: AccionRequerida.RequiereSeguimiento,
                emitidoPor: "rmartinez")
        };
        // Dictamen PuedeOperar es incoherente con RequiereSeguimiento
        var cmd = ComandoFirmarBasico(dictamen: DictamenOperacion.PuedeOperar);

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<DictamenIncoherenteException>()
            .WithMessage("*seguimiento*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.8 Violación PRE-5 — dictamen PuedeOperar con hallazgo RequiereIntervencion (V-F8)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_dictamen_PuedeOperar_con_hallazgo_RequiereIntervencion_lanza_DictamenIncoherenteException_PRE_5_V_F8()
    {
        // Given: aggregate con hallazgo RequiereIntervencion completo (con adjunto)
        var dados = StreamParaFirmarConIntervencion();
        // Dictamen PuedeOperar es incoherente con RequiereIntervencion
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Falla confirmada",
            Dictamen: DictamenOperacion.PuedeOperar,
            JustificacionDictamen: "justificación inválida",
            FirmaUri: "https://blobs/firma-04.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<DictamenIncoherenteException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.9 Caso borde V-F8 — dictamen PuedeOperar permitido cuando solo hay NoRequiereIntervencion
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_dictamen_PuedeOperar_con_solo_NoRequiereIntervencion_no_lanza_V_F8()
    {
        // Given: aggregate con hallazgo NoRequiereIntervencion
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(dictamen: DictamenOperacion.PuedeOperar);

        // When / Then: PuedeOperar es válido — no hay hallazgos que requieran seguimiento/intervención
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);
        act.Should().NotThrow("PuedeOperar es coherente cuando todos los hallazgos son NoRequiereIntervencion");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.10 Violación PRE-6 — hallazgo RequiereIntervencion sin TipoFallaId (V-F3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_hallazgo_RequiereIntervencion_sin_TipoFallaId_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3()
    {
        // Given: hallazgo RequiereIntervencion con TipoFallaId=null (incompleto)
        var dados = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: null,      // TipoFallaId ausente — PRE-6 debe bloquearlo
                causaFallaId: 2,
                emitidoPor: "rmartinez"),
            new AdjuntoSubido_v1(InspeccionIdNueva, AdjuntoA1, HallazgoG1, "https://blobs/adj1.jpg", "rmartinez", Ahora)
        };
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Falla confirmada",
            Dictamen: DictamenOperacion.NoPuedeOperar,
            JustificacionDictamen: "Falla estructural",
            FirmaUri: "https://blobs/firma-05.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<HallazgoIntervencionIncompletoException>()
            .WithMessage("*TipoFallaId*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.11 Violación PRE-6 — hallazgo RequiereIntervencion sin CausaFallaId (V-F3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_hallazgo_RequiereIntervencion_sin_CausaFallaId_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3()
    {
        // Given: hallazgo RequiereIntervencion con CausaFallaId=null
        var dados = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: null,     // CausaFallaId ausente — PRE-6 debe bloquearlo
                emitidoPor: "rmartinez"),
            new AdjuntoSubido_v1(InspeccionIdNueva, AdjuntoA1, HallazgoG1, "https://blobs/adj1.jpg", "rmartinez", Ahora)
        };
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Falla confirmada",
            Dictamen: DictamenOperacion.NoPuedeOperar,
            JustificacionDictamen: "Falla estructural",
            FirmaUri: "https://blobs/firma-06.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<HallazgoIntervencionIncompletoException>()
            .WithMessage("*CausaFallaId*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.12 Violación PRE-6 — hallazgo RequiereIntervencion sin adjuntos (V-F3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_hallazgo_RequiereIntervencion_sin_adjuntos_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3()
    {
        // Given: hallazgo RequiereIntervencion con TipoFallaId + CausaFallaId pero sin adjuntos
        var dados = StreamParaFirmarConIntervencion(incluirAdjunto: false);
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Falla confirmada",
            Dictamen: DictamenOperacion.NoPuedeOperar,
            JustificacionDictamen: "Falla estructural",
            FirmaUri: "https://blobs/firma-07.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<HallazgoIntervencionIncompletoException>()
            .WithMessage("*adjunto*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.13 Violación PRE-6 — hallazgo RequiereIntervencion con todos adjuntos eliminados (V-F3)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_hallazgo_RequiereIntervencion_con_adjunto_eliminado_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3()
    {
        // Given: hallazgo RequiereIntervencion con adjunto subido y luego eliminado
        var dados = StreamParaFirmarConIntervencion(incluirAdjunto: true, adjuntoEliminado: true);
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Falla confirmada",
            Dictamen: DictamenOperacion.NoPuedeOperar,
            JustificacionDictamen: "Falla estructural",
            FirmaUri: "https://blobs/firma-08.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<HallazgoIntervencionIncompletoException>(
            "ningún adjunto activo en el hallazgo — el único fue eliminado");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.14 Violación PRE-7 — FirmaUri vacío (V-F5)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_con_FirmaUri_vacio_lanza_FirmaRequeridaException_PRE_7_V_F5()
    {
        // Given: aggregate en estado válido para firmar
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(firmaUri: "");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<FirmaRequeridaException>();
    }

    [Fact]
    public void FirmarInspeccion_con_FirmaUri_solo_espacios_lanza_FirmaRequeridaException_PRE_7_V_F5()
    {
        // Given: aggregate en estado válido para firmar
        var dados = StreamParaFirmarSinIntervencion();
        var cmd = ComandoFirmarBasico(firmaUri: "   ");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<FirmaRequeridaException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.15 Violación PRE-8 — UbicacionFirma nula (V-F6)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_con_UbicacionFirma_nula_lanza_GpsRequeridoException_PRE_8_V_F6()
    {
        // Given: aggregate en estado válido para firmar
        var dados = StreamParaFirmarSinIntervencion();

        // Nota: no se usa ComandoFirmarBasico porque su parámetro ubicacionFirma usa el patrón
        // `ubicacionFirma ?? UbicacionFirmaEjemplo()` — pasar null invocaría el fallback y la
        // invariante V-F6 nunca se dispararía. Se construye el record directamente para garantizar
        // que UbicacionFirma sea literalmente null en el comando enviado al dominio.
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Inspección sin hallazgos críticos",
            Dictamen: DictamenOperacion.PuedeOperar,
            JustificacionDictamen: "Equipo en buen estado",
            FirmaUri: "https://blobs/firma-01.png",
            UbicacionFirma: null!,
            TecnicoId: "rmartinez");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<GpsRequeridoException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.16 Violación PRE-9 — técnico no contribuyente intenta firmar
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException_PRE_9()
    {
        // Given: aggregate con TecnicosContribuyentes={"rmartinez"} (el iniciador)
        //        + hallazgo registrado por "rmartinez" (único contribuyente)
        var dados = StreamParaFirmarSinIntervencion(tecnicoIniciador: "rmartinez");

        // Técnico "tecnico-99" no es contribuyente — no tiene derecho a firmar (P-2 confirmado)
        var cmd = ComandoFirmarBasico(tecnicoId: "tecnico-99");

        // When / Then
        var act = () => CasoDeUso.Firmar(dados, cmd, Ahora);

        act.Should().Throw<TecnicoNoContribuyenteException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.17 Rebuild desde stream (obligatorio — 3 eventos en orden causal)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirmarInspeccion_rebuild_desde_stream_reproduce_estado()
    {
        // Given: stream previo (happy path §6.1) + emitir los 3 eventos de firma
        var dados = (IReadOnlyList<object>)StreamParaFirmarSinIntervencion();
        var cmd = new FirmarInspeccion(
            InspeccionId: InspeccionIdNueva,
            Diagnostico: "Inspección sin hallazgos críticos",
            Dictamen: DictamenOperacion.PuedeOperar,
            JustificacionDictamen: "Equipo en buen estado",
            FirmaUri: "https://blobs/firma-01.png",
            UbicacionFirma: UbicacionFirmaEjemplo(),
            TecnicoId: "rmartinez");

        var emitidos = CasoDeUso.Firmar(dados, cmd, Ahora);

        // When: reproyectar el stream completo (previos + emitidos) sobre un aggregate vacío
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: el rebuild no lanza y el estado resultante es coherente
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Estado.Should().Be(EstadoInspeccion.Firmada);
        aggregate.DiagnosticoFinal.Should().Be("Inspección sin hallazgos críticos");
        aggregate.Dictamen.Should().Be(DictamenOperacion.PuedeOperar);
        aggregate.FirmaUri.Should().Be("https://blobs/firma-01.png");
        aggregate.UbicacionFirma!.Latitud.Should().Be(4.7m);
        aggregate.FirmadaEn.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.18 Inmutabilidad post-firma — no se puede agregar hallazgo tras firmar (I-F1)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RegistrarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I_F1()
    {
        // Given: aggregate reproyectado con InspeccionFirmada_v1 (Estado=Firmada)
        var dados = StreamConInspeccionFirmada();
        var aggregate = Inspeccion.Reconstruir(dados);

        // Verificar que el estado es efectivamente Firmada
        aggregate.Estado.Should().Be(EstadoInspeccion.Firmada, "el stream incluye InspeccionFirmada_v1");

        // When: intento de registrar hallazgo sobre inspección ya firmada
        var cmdHallazgo = ComandoManualSinIntervencion();
        var act = () => aggregate.RegistrarHallazgo(cmdHallazgo, Ahora);

        // Then: la invariante I-F1 garantizada por PRE-3 de RegistrarHallazgo
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }
}
