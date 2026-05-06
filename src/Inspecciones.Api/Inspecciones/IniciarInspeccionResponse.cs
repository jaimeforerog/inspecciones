namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>POST /api/v1/inspecciones</c>. Forma idéntica
/// al record <c>IniciarInspeccionResult</c> del handler — la capa API solo
/// serializa. Spec §9.
/// </summary>
public sealed record IniciarInspeccionResponse(
    Guid InspeccionId,
    bool RedirigeAExistente,
    int Version,
    string? Mensaje);
