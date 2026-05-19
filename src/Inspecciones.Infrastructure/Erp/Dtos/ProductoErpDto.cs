namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>ProductoDto</c> de Maquinaria_V4
/// (<c>GET /api/v4/Maquinaria/api/productos</c>, slice 4).
/// Inspecciones lo usa para hidratar <c>RepuestoLocal</c> en el catálogo local
/// — es el "insumo" del modelo (§15.6).
/// </summary>
public sealed record ProductoErpDto
{
    /// <summary>SkuId — mapeado de <c>EQV4.Productos.ProCod</c>.</summary>
    public int Codigo { get; init; }
    public string Descripcion { get; init; } = string.Empty;

    /// <summary>UnidadMedida del modelo Inspecciones — mapeado de <c>EQV4.Productos.ProUnidadCont</c>.</summary>
    public string UnidadContable { get; init; } = string.Empty;
}

/// <summary>Wrapper de respuesta del endpoint productos. Soporta <c>If-None-Match</c>/<c>ETag</c>.</summary>
public sealed record ListarProductosResponseDto
{
    public IReadOnlyList<ProductoErpDto> Productos { get; init; } = Array.Empty<ProductoErpDto>();
    public int Total { get; init; }
}
