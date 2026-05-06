namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Read model local del equipo, sincronizado desde Sinco on-prem vía M-3b
/// (ADR-004 + refinamientos 2026-05-05). Extendido en slice 1c con
/// <see cref="Partes"/> para la validación INV-PartePerteneceAlEquipo.
/// </summary>
public sealed record EquipoLocal(
    int EquipoId,
    string EquipoCodigo,
    int ProyectoId,
    int? RutinaTecnicaId,
    IReadOnlyList<ParteEquipoLocal>? Partes = null);
