namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando se elimina (soft delete) un hallazgo de la inspección.
/// El hallazgo permanece en el stream para auditoría — <c>Eliminado=true</c>
/// en el aggregate en memoria. Spec slice 1e §3.
/// </summary>
public sealed record HallazgoEliminado_v1(
    Guid           InspeccionId,
    Guid           HallazgoId,
    string         Motivo,
    string         EliminadoPor,
    DateTimeOffset EliminadoEn);