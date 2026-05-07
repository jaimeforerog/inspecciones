namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Value object de estado interno del aggregate <see cref="Inspeccion"/>.
/// Materializado a partir de <see cref="RepuestoEstimado_v1"/> en el Apply
/// correspondiente. Solo para fold — no es evento ni comando. Spec slice 1f §3.
/// </summary>
public sealed record Repuesto(
    Guid     RepuestoId,
    Guid     HallazgoId,
    int      SkuId,
    string   SkuCodigo,
    decimal  Cantidad,
    string?  Justificacion,
    string   Unidad);