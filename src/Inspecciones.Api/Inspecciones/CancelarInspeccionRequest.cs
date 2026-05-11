namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de request para <c>POST /api/v1/inspecciones/{id}/cancelar</c>.
/// Slice 1m — CancelarInspeccion. Spec §2.
/// </summary>
public sealed record CancelarInspeccionRequest(string? Motivo);

/// <summary>
/// DTO de response para <c>POST /api/v1/inspecciones/{id}/cancelar</c>.
/// Slice 1m — CancelarInspeccion. Spec §2.
/// </summary>
public sealed record CancelarInspeccionResponse(
    Guid           InspeccionId,
    string         Estado,
    DateTimeOffset CanceladaEn,
    string         CanceladaPor,
    string         Motivo);
