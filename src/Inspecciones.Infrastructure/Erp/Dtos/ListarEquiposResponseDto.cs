namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>Wrapper de la respuesta paginada de Maquinaria_V4 para equipos.</summary>
public sealed record ListarEquiposResponseDto
{
    public IReadOnlyList<EquipoErpDto> Equipos { get; init; } = Array.Empty<EquipoErpDto>();
    public int Total { get; init; }
}

/// <summary>Wrapper de la respuesta de partes.</summary>
public sealed record ListarPartesEquiposResponseDto
{
    public IReadOnlyList<ParteEquipoErpDto> Partes { get; init; } = Array.Empty<ParteEquipoErpDto>();
    public int Total { get; init; }
}
