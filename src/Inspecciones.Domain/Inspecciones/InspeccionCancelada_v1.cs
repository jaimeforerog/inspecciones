namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento que marca la inspección como cancelada.
/// Stub mínimo para que el slice 1c pueda reconstruir el estado
/// <see cref="EstadoInspeccion.Cancelada"/> en los tests de PRE-3.
/// La lógica completa se implementa en el slice de CancelarInspeccion.
/// </summary>
public sealed record InspeccionCancelada_v1(
    Guid InspeccionId,
    DateTimeOffset CanceladaEn,
    string CanceladoPor,
    string? Motivo);
