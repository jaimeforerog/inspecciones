namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento que marca un hallazgo como eliminado (soft delete).
/// Stub mínimo para que el slice 2 pueda reconstruir el estado
/// <c>Eliminado=true</c> en el test de PRE-4 (escenario 6.5).
/// La lógica completa (comando <c>EliminarHallazgo</c>) se implementa en slice 3.
/// </summary>
public sealed record HallazgoEliminado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    DateTimeOffset EliminadoEn,
    string EliminadoPor);
