namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Read model local del repuesto/insumo, sincronizado desde Sinco on-prem vía M-6
/// (catálogo de inventario — ADR-004). Usado por el handler de <c>AsignarRepuesto</c>
/// (slice 1f) para validar PRE-H1 (SKU existe) y PRE-H2 (compatibilidad SKU↔Parte),
/// y para derivar <c>Unidad</c> y <c>SkuCodigo</c> antes de invocar el aggregate.
/// </summary>
public sealed record RepuestoLocal(
    int SkuId,
    string CodigoSinco,
    string Descripcion,
    string UnidadMedida,
    IReadOnlyList<int> ParteIdsCompatibles,
    bool AplicaMYE = true);