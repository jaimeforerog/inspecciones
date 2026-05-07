namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>POST /api/v1/inspecciones/monitoreo</c>. Forma
/// idéntica al record <c>IniciarInspeccionMonitoreoResult</c> del handler — la
/// capa API solo serializa. Spec slice 1h §9.
/// Followup #24: evaluar unificación con <c>IniciarInspeccionResponse</c>.
/// </summary>
public sealed record IniciarInspeccionMonitoreoResponse(
    Guid InspeccionId,
    bool RedirigeAExistente,
    int Version,
    string? Mensaje);
