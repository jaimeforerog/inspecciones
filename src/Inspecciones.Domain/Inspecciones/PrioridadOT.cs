namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Nivel de prioridad de la Orden de Trabajo correctiva solicitada.
/// Slice 1k — GenerarOT. Decisión P-1: campo explícito del aprobador (opción B del spec).
/// </summary>
public enum PrioridadOT
{
    Baja,
    Normal,
    Alta,
    Urgente
}
