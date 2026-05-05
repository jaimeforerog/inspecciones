namespace Inspecciones.Domain.Comun;

/// <summary>
/// Coordenadas GPS capturadas en un momento dado. Regla CLAUDE.md: prohibido
/// pasar lat/long como <c>double</c> pelado en el dominio.
/// </summary>
public sealed record UbicacionGps(
    decimal Latitud,
    decimal Longitud,
    decimal PrecisionMetros,
    DateTimeOffset CapturadoEn);
