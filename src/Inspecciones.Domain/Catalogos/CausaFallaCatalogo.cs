namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Documento Marten del catálogo local de causas de falla, sincronizado desde
/// Maquinaria_V4 (M-10). Id = <c>CausaFallaId</c> (int ERP, campo <c>Codigo</c>
/// del DTO). Estrategia de actualización: wipe-and-replace (D3 del spec erp-4).
/// </summary>
public sealed record CausaFallaCatalogo(
    int Id,
    string Descripcion);
