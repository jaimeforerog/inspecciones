namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Snapshot inmutable de los claims del técnico al momento del comando.
/// El handler los recibe del contexto inyectado por el host PWA Sinco MYE
/// (mecanismo concreto pendiente — ADR-002 tentativo) y los pasa al método
/// de decisión del agregado. El dominio nunca lee del contexto HTTP.
/// </summary>
public sealed record ClaimsTecnico(
    string TecnicoIniciador,
    IReadOnlySet<int> ProyectosAsignados,
    bool TieneCapabilityEjecutarInspeccion);
