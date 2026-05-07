namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un adjunto es marcado como eliminado (soft delete).
/// Stub mínimo para soportar el test §6.13 del slice 1g (FirmarInspeccion):
/// la firma exige ≥1 adjunto NO eliminado por hallazgo RequiereIntervencion.
/// La lógica completa llega con el slice de EliminarAdjunto.
/// </summary>
public sealed record AdjuntoEliminado_v1(
    Guid           InspeccionId,
    Guid           AdjuntoId,
    Guid           HallazgoId,
    string         EliminadoPor,
    DateTimeOffset EliminadoEn);
