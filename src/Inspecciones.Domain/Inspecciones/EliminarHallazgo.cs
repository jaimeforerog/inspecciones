namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para eliminar (soft delete) un hallazgo ya registrado en una inspección
/// en estado <see cref="EstadoInspeccion.EnEjecucion"/>. Spec slice 1e §2.
/// El <c>Motivo</c> es obligatorio — la eliminación silenciosa viola la trazabilidad
/// requerida por el consultor mecánico (§15.2).
/// </summary>
public sealed record EliminarHallazgo(
    Guid   InspeccionId,
    Guid   HallazgoId,
    string Motivo,
    string TecnicoId);