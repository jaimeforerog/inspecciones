namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Determina qué entidad asume el costo de la OT correctiva generada.
/// Slice 1k — GenerarOT. Ref §17 ADR-007 del modelo.
/// </summary>
public enum ResponsableCosto
{
    /// <summary>El proyecto donde está el equipo asume el costo.</summary>
    Proyecto,

    /// <summary>El área que administra los equipos como activo asume el costo.</summary>
    DepartamentoEquipos
}
