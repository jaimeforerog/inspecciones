namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Origen del hallazgo registrado durante la inspección. Slice 1c cubre únicamente
/// <see cref="Manual"/> y <see cref="PreOperacional"/>. Los demás valores están en el
/// enum pero PRE-10 los bloquea con <see cref="OrigenNoSoportadoException"/> hasta que
/// sus slices respectivos los implementen.
/// </summary>
public enum OrigenHallazgo
{
    Manual,
    PreOperacional,
    Seguimiento,
    Monitoreo
}
