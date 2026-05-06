namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento que marca la inspección como firmada por el técnico.
/// Stub mínimo para que el slice 1c pueda reconstruir el estado
/// <see cref="EstadoInspeccion.Firmada"/> en los tests de PRE-3.
/// La lógica completa se implementa en el slice de FirmarInspeccion.
/// </summary>
public sealed record InspeccionFirmada_v1(
    Guid InspeccionId,
    DateTimeOffset FirmadaEn,
    string FirmadoPor);
