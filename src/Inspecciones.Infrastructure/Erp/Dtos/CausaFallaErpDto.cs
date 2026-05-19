namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>CausaFallaDto</c> de Maquinaria_V4
/// (<c>GET /api/v4/Maquinaria/api/causas-falla</c>, slice 5).
/// Mapeo ERP → DTO confirmado: <c>Codigo</c> ← <c>EQV4.FallaCausa.CausaFId</c>,
/// <c>Descripcion</c> ← <c>EQV4.FallaCausa.CausaFDescripcion</c>.
/// </summary>
public sealed record CausaFallaErpDto
{
    public int Codigo { get; init; }
    public string Descripcion { get; init; } = string.Empty;
}

/// <summary>Wrapper de respuesta del endpoint causas-falla. Soporta <c>If-None-Match</c>/<c>ETag</c>.</summary>
public sealed record ListarCausasFallaResponseDto
{
    public IReadOnlyList<CausaFallaErpDto> Causas { get; init; } = Array.Empty<CausaFallaErpDto>();
    public int Total { get; init; }
}
