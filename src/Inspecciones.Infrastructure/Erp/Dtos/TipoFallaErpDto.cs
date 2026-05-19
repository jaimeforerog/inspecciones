namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>TipoFallaDto</c> de Maquinaria_V4
/// (<c>GET /api/v4/Maquinaria/api/tipos-falla</c>, slice 6).
/// </summary>
public sealed record TipoFallaErpDto
{
    public int Codigo { get; init; }
    public string Descripcion { get; init; } = string.Empty;

    /// <summary>
    /// Prioridad operativa expuesta por el ERP como <c>varchar(5)</c>. Inspecciones
    /// la conserva como string opaco; si emerge necesidad de ordenamiento numérico
    /// se hace cliente-side.
    /// </summary>
    public string Prioridad { get; init; } = string.Empty;
}

/// <summary>Wrapper de respuesta del endpoint tipos-falla. Soporta <c>If-None-Match</c>/<c>ETag</c>.</summary>
public sealed record ListarTiposFallaResponseDto
{
    public IReadOnlyList<TipoFallaErpDto> TiposFalla { get; init; } = Array.Empty<TipoFallaErpDto>();
    public int Total { get; init; }
}
