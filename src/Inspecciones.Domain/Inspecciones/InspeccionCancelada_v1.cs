namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento que marca la inspección como cancelada.
/// Shape canónico del slice 1m — CancelarInspeccion (spec §3.1).
/// El orden de parámetros sigue la convención del modelo §2.1 corregida:
/// Motivo y CanceladaPor primero, CanceladaEn como DateTimeOffset (D-3).
/// </summary>
public sealed record InspeccionCancelada_v1(
    Guid           InspeccionId,
    string         Motivo,
    string         CanceladaPor,
    DateTimeOffset CanceladaEn);
