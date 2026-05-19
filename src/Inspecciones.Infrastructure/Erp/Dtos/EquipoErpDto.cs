namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>EquipoDto</c> que expone Maquinaria_V4 en
/// <c>GET /api/v4/Maquinaria/api/equipos</c>. Campos coinciden 1:1
/// con la vista <c>EQV4.Equipos</c> del ERP.
/// </summary>
public sealed record EquipoErpDto
{
    public int EquipoId { get; init; }
    public string Placa { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? UbicacionId { get; init; }
    public int? GrupoMantenimientoId { get; init; }
    public string? GrupoMantenimientoDescripcion { get; init; }
    public int? ObraEquipo { get; init; }
    public short? SucursalEquipo { get; init; }
    public short? SucursalProyecto { get; init; }
    public int? ObraProyecto { get; init; }
    public string? UbicacionDescripcion { get; init; }
    public string UM1 { get; init; } = string.Empty;
    public decimal M1Actual { get; init; }
    public string? UM2 { get; init; }
    public decimal? M2Actual { get; init; }
    public int? RutinaMantenimientoId { get; init; }
    public string? RutinaMantenimientoDescripcion { get; init; }
}
