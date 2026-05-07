namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler de <c>IniciarInspeccionMonitoreo</c>. Mismo shape que
/// <see cref="IniciarInspeccionResult"/> — reutilizable según spec 1h §2.
/// </summary>
public sealed record IniciarInspeccionMonitoreoResult(
    Guid InspeccionId,
    bool RedirigeAExistente,
    int Version,
    string? Mensaje);
