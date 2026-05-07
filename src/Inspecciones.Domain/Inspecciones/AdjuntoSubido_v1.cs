namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un adjunto (imagen/video de evidencia) es subido al blob
/// y referenciado desde la inspección. Stub mínimo para soportar los tests de
/// PRE-6 del slice 1g (FirmarInspeccion): la firma exige ≥1 adjunto activo por
/// hallazgo con AccionRequerida=RequiereIntervencion. La lógica completa llega
/// con el slice de SubirAdjunto.
/// </summary>
public sealed record AdjuntoSubido_v1(
    Guid           InspeccionId,
    Guid           AdjuntoId,
    Guid           HallazgoId,
    string         BlobUri,
    string         SubidoPor,
    DateTimeOffset SubidoEn);
