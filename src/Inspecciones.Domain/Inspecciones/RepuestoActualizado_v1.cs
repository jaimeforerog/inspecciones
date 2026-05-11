namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando se actualizan los campos patcheables de un repuesto estimado
/// (<c>Cantidad</c> y/o <c>Justificacion</c>). Spec slice 1o §3.
/// <para>
/// Semántica de delta: <c>null</c> en <c>Cantidad</c> o <c>Justificacion</c> significa
/// que ese campo no fue modificado en esta actualización. La proyección aplica el delta
/// sobre el estado previo del repuesto.
/// </para>
/// </summary>
public sealed record RepuestoActualizado_v1(
    Guid           InspeccionId,
    Guid           HallazgoId,
    Guid           RepuestoId,
    decimal?       Cantidad,       // null = no cambió en esta actualización
    string?        Justificacion,  // null = no cambió en esta actualización; ver P-2 sobre limpiar
    string         ActualizadoPor,
    DateTimeOffset ActualizadoEn);
