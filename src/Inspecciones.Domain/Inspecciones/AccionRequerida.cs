namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Acción requerida por un hallazgo. Determina las invariantes aplicables
/// (I-H4, PRE-8) y el ciclo posterior del hallazgo (OT, SeguimientoHallazgo).
/// </summary>
public enum AccionRequerida
{
    NoRequiereIntervencion,
    RequiereIntervencion,
    RequiereSeguimiento
}
