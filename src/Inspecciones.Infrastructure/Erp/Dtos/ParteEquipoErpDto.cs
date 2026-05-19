namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>ParteEquipoDto</c> de Maquinaria_V4
/// (<c>GET /api/v4/Maquinaria/api/partes-equipos</c>).
/// </summary>
public sealed record ParteEquipoErpDto
{
    public int ParteId { get; init; }
    public string ParteDescripcion { get; init; } = string.Empty;
    public string RutinaMantenimientoDescripcion { get; init; } = string.Empty;
    public int EquipoId { get; init; }
}
