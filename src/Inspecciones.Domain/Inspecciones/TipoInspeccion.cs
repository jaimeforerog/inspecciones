namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Discriminador del tipo de inspección. En MVP solo aplica <see cref="Tecnica"/>.
/// <see cref="Monitoreo"/> queda preparado para Fase 2 (§12.11.5 modelo).
/// </summary>
public enum TipoInspeccion
{
    Tecnica,
    Monitoreo
}
