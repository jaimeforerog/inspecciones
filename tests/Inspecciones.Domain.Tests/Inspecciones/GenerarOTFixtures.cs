using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1k — GenerarOT.
/// InspeccionId reutiliza <see cref="Fixtures.InspeccionIdNueva"/> (inspección técnica).
/// Timestamp <see cref="AhoraOT"/> posterior al inicio para representar el momento de aprobación.
/// </summary>
internal static class GenerarOTFixtures
{
    /// <summary>Timestamp del handler de GenerarOT (2026-05-08T14:00:00Z — fijado en spec §6.1).</summary>
    public static readonly DateTimeOffset AhoraOT =
        new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);

    /// <summary>AdjuntoId de ejemplo para el hallazgo con RequiereIntervencion.</summary>
    public static readonly Guid AdjuntoAdj1 = new("0194b000-0000-7000-0000-000000000001");

    // ── Constructores de comandos ─────────────────────────────────────────────

    /// <summary>
    /// Comando happy path §6.1: aprobador con capability "generar-ot",
    /// responsable=Proyecto, prioridad=Urgente.
    /// </summary>
    public static GenerarOT ComandoGenerarOTUrgente(
        Guid? inspeccionId = null,
        string solicitadaPor = "jefe.campo.01",
        ResponsableCosto responsable = ResponsableCosto.Proyecto,
        PrioridadOT prioridad = PrioridadOT.Urgente,
        string? observaciones = "Equipo fuera de operación — prioridad máxima",
        string? comentarioJefe = null) =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            SolicitadaPor: solicitadaPor,
            Responsable: responsable,
            Observaciones: observaciones,
            ComentarioJefe: comentarioJefe,
            Capabilities: new[] { "generar-ot" },
            Prioridad: prioridad);

    /// <summary>
    /// Comando happy path §6.2: aprobador con capability "generar-ot",
    /// responsable=DepartamentoEquipos, prioridad=Alta, con ComentarioJefe.
    /// </summary>
    public static GenerarOT ComandoGenerarOTConComentarioJefe(
        Guid? inspeccionId = null,
        string solicitadaPor = "supervisor.01",
        string comentarioJefe = "Coordinar con David antes de iniciar") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            SolicitadaPor: solicitadaPor,
            Responsable: ResponsableCosto.DepartamentoEquipos,
            Observaciones: null,
            ComentarioJefe: comentarioJefe,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Alta);

    /// <summary>Comando con capability incorrecta (§6.3 PRE-1).</summary>
    public static GenerarOT ComandoSinCapabilityGenerarOT(
        Guid? inspeccionId = null) =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            SolicitadaPor: "carlos.ruiz",
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: null,
            ComentarioJefe: null,
            Capabilities: new[] { "ejecutar-inspeccion" },
            Prioridad: PrioridadOT.Normal);

    /// <summary>Comando básico con capability correcta y prioridad Normal (para escenarios de error).</summary>
    public static GenerarOT ComandoGenerarOTNormal(
        Guid? inspeccionId = null,
        string solicitadaPor = "jefe.campo.01") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            SolicitadaPor: solicitadaPor,
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: null,
            ComentarioJefe: null,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Normal);

    // ── Streams de Given ──────────────────────────────────────────────────────

    /// <summary>
    /// Stream happy path §6.1: inspección técnica firmada con dictamen NoPuedeOperar
    /// y un hallazgo RequiereIntervencion con adjunto. El stream tiene los 6 eventos
    /// previos al comando.
    /// </summary>
    public static object[] StreamFirmadoNoPuedeOperar()
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
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "carlos.ruiz"),
            new AdjuntoSubido_v1(
                InspeccionId: InspeccionIdNueva,
                AdjuntoId: AdjuntoAdj1,
                HallazgoId: h1,
                BlobUri: "https://blobs/adj-fixture.jpg",
                SubidoPor: "carlos.ruiz",
                SubidoEn: Ahora),
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Falla estructural en brazo hidráulico",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.NoPuedeOperar,
                Justificacion: "Brazo hidráulico no operativo",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream happy path §6.2: inspección técnica firmada con dictamen ConRestriccion
    /// y un hallazgo RequiereIntervencion con adjunto.
    /// </summary>
    public static object[] StreamFirmadoConRestriccion()
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
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "carlos.ruiz"),
            new AdjuntoSubido_v1(
                InspeccionId: InspeccionIdNueva,
                AdjuntoId: AdjuntoAdj1,
                HallazgoId: h1,
                BlobUri: "https://blobs/adj-fixture.jpg",
                SubidoPor: "carlos.ruiz",
                SubidoEn: Ahora),
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Restricción operativa por desgaste",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.ConRestriccion,
                Justificacion: "Operable con límite de carga",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream para §6.3 PRE-1: inspección firmada válida con hallazgo RequiereIntervencion.
    /// El PRE-1 se verifica en tests de capa HTTP (Skip aquí).
    /// </summary>
    public static object[] StreamFirmadoConHallazgoIntervencion() =>
        StreamFirmadoNoPuedeOperar();

    /// <summary>
    /// Stream para §6.4 PRE-3: inspección en estado EnEjecucion (sin firma).
    /// </summary>
    public static object[] StreamEnEjecucion() =>
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
    ];

    /// <summary>
    /// Stream para §6.5 PRE-4: inspección firmada con ConRestriccion, pero el único
    /// hallazgo tiene AccionRequerida=RequiereSeguimiento (ninguno con RequiereIntervencion).
    /// </summary>
    public static object[] StreamFirmadoConSoloHallazgoSeguimiento()
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
                accionRequerida: AccionRequerida.RequiereSeguimiento,
                emitidoPor: "carlos.ruiz"),
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Desgaste leve, requiere seguimiento",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.ConRestriccion,
                Justificacion: "Operable con seguimiento",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream para §6.6 PRE-5: inspección firmada con hallazgo RequiereIntervencion
    /// y un OTSolicitada_v1 previo (aggregate.OTSolicitada == true).
    /// </summary>
    public static object[] StreamFirmadoConOTYaSolicitada()
    {
        var baseStream = StreamFirmadoNoPuedeOperar();
        var otSolicitada = new OTSolicitada_v1(
            InspeccionId: InspeccionIdNueva,
            SolicitadaPor: "jefe.campo.01",
            Responsable: ResponsableCosto.Proyecto,
            Prioridad: PrioridadOT.Urgente,
            Observaciones: "Primera solicitud",
            ComentarioJefe: null,
            SolicitadaEn: AhoraOT.AddMinutes(-30));
        return [.. baseStream, otSolicitada];
    }

    /// <summary>
    /// Stream para §6.7 PRE-6: inspección firmada con GeneracionOTRechazada_v1 previo
    /// (aggregate.OTRechazada == true).
    /// </summary>
    public static object[] StreamFirmadoConOTRechazada()
    {
        var baseStream = StreamFirmadoNoPuedeOperar();
        var otRechazada = new GeneracionOTRechazada_v1(
            InspeccionId: InspeccionIdNueva,
            Motivo: "Presupuesto insuficiente para el período",          // D-2: renombrado MotivoRechazo → Motivo
            RechazadoPor: "gerente.01",
            RechazadaEn: AhoraOT.AddMinutes(-60));
        return [.. baseStream, otRechazada];
    }

    /// <summary>
    /// Stream para §6.8 PRE-7: inspección firmada con dictamen PuedeOperar
    /// y hallazgo NoRequiereIntervencion. Caso de defensa de segunda línea (I-F4.e).
    /// </summary>
    public static object[] StreamFirmadoDictamenPuedeOperar()
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
                emitidoPor: "carlos.ruiz"),
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Equipo en buen estado",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin defectos observados",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream para §6.11: inspección firmada donde el único hallazgo RequiereIntervencion
    /// fue eliminado. HallazgoG1 eliminado, no quedan activos con RequiereIntervencion.
    /// </summary>
    public static object[] StreamFirmadoConHallazgoIntervencionEliminado()
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
            // Segundo hallazgo NoRequiereIntervencion para poder firmar (V-F1 — al menos uno activo)
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                accionRequerida: AccionRequerida.NoRequiereIntervencion,
                emitidoPor: "carlos.ruiz"),
            // Hallazgo con RequiereIntervencion que luego se elimina
            HallazgoRegistradoEjemplo(
                hallazgoId: h1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 1,
                causaFallaId: 2,
                emitidoPor: "carlos.ruiz"),
            new AdjuntoSubido_v1(
                InspeccionId: InspeccionIdNueva,
                AdjuntoId: AdjuntoAdj1,
                HallazgoId: h1,
                BlobUri: "https://blobs/adj-fixture.jpg",
                SubidoPor: "carlos.ruiz",
                SubidoEn: Ahora),
            // Eliminación del hallazgo con RequiereIntervencion (sin repuestos — puede eliminarse)
            HallazgoEliminadoEjemplo(
                hallazgoId: h1,
                eliminadoPor: "carlos.ruiz"),
            new DiagnosticoEmitido_v1(
                InspeccionId: InspeccionIdNueva,
                DiagnosticoFinal: "Solo requiere seguimiento",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: InspeccionIdNueva,
                Dictamen: DictamenOperacion.ConRestriccion,
                Justificacion: "Requiere seguimiento periódico",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream para §6.12 PRE-3: inspección en estado CerradaSinOT (estado terminal).
    /// </summary>
    public static object[] StreamCerradaSinOT()
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
                emitidoPor: "carlos.ruiz"),
            new InspeccionCerradaSinOT_v1(
                InspeccionId: InspeccionIdNueva,
                MotivoCierre: MotivoCierreSinOT.AutomaticoSinIntervencion,  // D-3: eliminado CerradoPor
                CerradaEn: Ahora),
        ];
    }
}
