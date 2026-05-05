namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Read model local del equipo, sincronizado desde Sinco on-prem vía M-3b
/// (ADR-004 + refinamientos 2026-05-05). Shape mínimo para este slice — se
/// extiende en slices posteriores con los demás campos del detalle del equipo.
/// </summary>
public sealed record EquipoLocal(
    int EquipoId,
    string EquipoCodigo,
    int ProyectoId,
    int? RutinaTecnicaId);
