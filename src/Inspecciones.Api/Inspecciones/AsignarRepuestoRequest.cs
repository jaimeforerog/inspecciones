namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos</c>.
/// Spec slice 1f §9.
/// </summary>
public sealed record AsignarRepuestoRequest(
    Guid RepuestoId,
    int SkuId,
    decimal Cantidad,
    string? Justificacion);