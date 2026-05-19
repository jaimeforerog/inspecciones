namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Espejo del DTO <c>RutinaMonitoreoDto</c> de Maquinaria_V4
/// (<c>GET /api/v4/Maquinaria/api/rutinas-monitoreo?equipoId=N</c>, slice 10).
/// DTO minimal — solo Codigo + Descripcion (confirmado bilateral 2026-05-13).
/// </summary>
public sealed record RutinaMonitoreoErpDto
{
    /// <summary>RutinaMonitoreoId — mapeado de <c>EQV4.RutinaInspeccionMonitoreo.RiId</c>.</summary>
    public int Codigo { get; init; }

    /// <summary>Descripción de la rutina — mapeado de <c>RiDescripcion</c>.</summary>
    public string Descripcion { get; init; } = string.Empty;
}

/// <summary>Wrapper de respuesta del endpoint rutinas-monitoreo por equipo. Soporta <c>ETag</c>.</summary>
public sealed record ListarRutinasMonitoreoPorEquipoResponseDto
{
    public IReadOnlyList<RutinaMonitoreoErpDto> Rutinas { get; init; } = Array.Empty<RutinaMonitoreoErpDto>();
    public int Total { get; init; }
}
