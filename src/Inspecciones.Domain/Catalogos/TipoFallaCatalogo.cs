namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Documento Marten del catálogo local de tipos de falla, sincronizado desde
/// Maquinaria_V4 (M-11). Id = <c>TipoFallaId</c> (int ERP, campo <c>Codigo</c>
/// del DTO). Estrategia de actualización: wipe-and-replace (D3 del spec erp-4).
/// </summary>
public sealed record TipoFallaCatalogo(
    int Id,
    string Descripcion,
    string Prioridad);
