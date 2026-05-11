namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del comando <see cref="Inspecciones.Domain.Inspecciones.CancelarInspeccion"/>.
/// Slice 1m — CancelarInspeccion. Spec §2.
/// </summary>
public sealed record CancelarInspeccionResult(
    Guid           InspeccionId,
    string         Estado,
    DateTimeOffset CanceladaEn,
    string         CanceladaPor,
    string         Motivo);
