using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Read model de resumen — <b>una fila por inspección</b> (la PK <see cref="Id"/>
/// es el stream-id = <c>InspeccionId</c>). A diferencia de
/// <see cref="InspeccionAbiertaPorEquipoView"/> (que solo conoce la inspección
/// abierta y borra la fila al cerrar), esta vista <b>conserva la historia</b>:
/// materializa todas las inspecciones de cualquier estado para listarlas por equipo.
///
/// Se alimenta de <see cref="InspeccionResumenProjection"/> (SingleStreamProjection):
/// el <see cref="EquipoId"/> se captura del evento inicial
/// <c>InspeccionIniciada_v1</c> y persiste en la fila, de modo que los eventos
/// terminales (<c>InspeccionFirmada_v1</c> / <c>InspeccionCancelada_v1</c>) no
/// necesitan transportar <c>EquipoId</c> — esto sortea el bloqueante de FU-13 sin
/// versionar eventos.
/// </summary>
/// <param name="Id">PK Marten (convención "Id") — igual a <c>InspeccionId</c> (stream-id Guid).</param>
/// <param name="EquipoId">Equipo de la inspección (capturado en <c>InspeccionIniciada_v1</c>).</param>
/// <param name="Tipo">Tecnica o Monitoreo.</param>
/// <param name="Estado">Estado actual derivado de los eventos de ciclo de vida.</param>
/// <param name="RutinaId">Rutina aplicada.</param>
/// <param name="RutinaCodigo">Código/nombre de la rutina (denormalizado).</param>
/// <param name="TecnicoIniciador">Login opaco del técnico que inició.</param>
/// <param name="ProyectoId">Proyecto de la inspección.</param>
/// <param name="FechaReportada">Fecha de reporte declarada por el técnico.</param>
/// <param name="IniciadaEn">Timestamp de inicio — clave de ordenamiento "más recientes primero".</param>
/// <param name="FirmadaEn">Timestamp de firma (null si no firmada).</param>
/// <param name="CanceladaEn">Timestamp de cancelación (null si no cancelada).</param>
/// <param name="CerradaEn">Timestamp de cierre sin OT (null si no cerrada sin OT).</param>
/// <param name="Dictamen">Dictamen de operatividad (null hasta firmar).</param>
/// <param name="OTSolicitada">True si se solicitó OT correctiva.</param>
/// <param name="OTRechazada">True si se rechazó la generación de OT.</param>
/// <param name="MotivoCancelacion">Motivo de la cancelación (null si no cancelada). Gap #3.</param>
/// <param name="HallazgosActivos">
/// Mapa <c>HallazgoId → AccionRequerida</c> de los hallazgos vigentes (no eliminados),
/// mantenido por la proyección al aplicar Registrar/Actualizar/Eliminar. Es el estado
/// base del que se derivan los conteos del resumen (gap #2). No se expone crudo en el
/// listado HTTP — solo los conteos calculados.
/// </param>
public sealed record InspeccionResumenView(
    Guid Id,
    int EquipoId,
    TipoInspeccion Tipo,
    EstadoInspeccion Estado,
    int RutinaId,
    string RutinaCodigo,
    string TecnicoIniciador,
    int ProyectoId,
    DateOnly FechaReportada,
    DateTimeOffset IniciadaEn,
    DateTimeOffset? FirmadaEn,
    DateTimeOffset? CanceladaEn,
    DateTimeOffset? CerradaEn,
    DictamenOperacion? Dictamen,
    bool OTSolicitada,
    bool OTRechazada,
    string? MotivoCancelacion,
    IReadOnlyDictionary<Guid, AccionRequerida> HallazgosActivos)
{
    /// <summary>Alias semántico de <see cref="Id"/> — el doc-id es el stream-id (InspeccionId).</summary>
    public Guid InspeccionId => Id;

    /// <summary>Total de hallazgos vigentes (no eliminados). Gap #2.</summary>
    public int TotalHallazgos => HallazgosActivos?.Count ?? 0;

    /// <summary>Hallazgos vigentes que requieren intervención (candidatos a OT).</summary>
    public int HallazgosRequierenIntervencion =>
        HallazgosActivos?.Values.Count(a => a == AccionRequerida.RequiereIntervencion) ?? 0;

    /// <summary>Hallazgos vigentes que requieren seguimiento.</summary>
    public int HallazgosRequierenSeguimiento =>
        HallazgosActivos?.Values.Count(a => a == AccionRequerida.RequiereSeguimiento) ?? 0;

    /// <summary>Hallazgos vigentes que no requieren intervención.</summary>
    public int HallazgosSinIntervencion =>
        HallazgosActivos?.Values.Count(a => a == AccionRequerida.NoRequiereIntervencion) ?? 0;
}
