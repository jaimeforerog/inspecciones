namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Dictamen de operatividad del equipo al momento de firmar la inspección.
/// §15.5 del modelo. Slice 1g — FirmarInspeccion.
/// </summary>
public enum DictamenOperacion
{
    PuedeOperar,
    ConRestriccion,
    NoPuedeOperar
}