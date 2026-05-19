namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>PreoperacionalFallaDto</c> de Maquinaria_V4
/// (<c>GET /api/v4/Maquinaria/api/preoperacional-fallas</c>, slice 7).
/// Inspecciones lo consume desde el adapter Preop para listar novedades
/// importables al wizard de hallazgo (Origen=PreOperacional, §15.9 del modelo).
/// </summary>
public sealed record PreoperacionalFallaErpDto
{
    /// <summary>NovedadPreopId del modelo — mapeado de <c>EQV4.PreoperacionalFallas.PODId</c>.</summary>
    public int Id { get; init; }
    public int RegistroPreoperacionalId { get; init; }
    public int EquipoId { get; init; }
    public int ActividadId { get; init; }
    public string ArbolDescripcion { get; init; } = string.Empty;
    public string ActividadDescripcion { get; init; } = string.Empty;
    public string Observacion { get; init; } = string.Empty;
    public DateTimeOffset Fecha { get; init; }
}

/// <summary>Wrapper de respuesta del endpoint preoperacional-fallas (slice 7 NO emite ETag).</summary>
public sealed record ListarPreoperacionalFallasResponseDto
{
    public IReadOnlyList<PreoperacionalFallaErpDto> Fallas { get; init; } = Array.Empty<PreoperacionalFallaErpDto>();
    public int Total { get; init; }
}
