namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Discriminador del tipo de inspección. MVP soporta ambos tipos:
/// <see cref="Tecnica"/> (flujo libre) y <see cref="Monitoreo"/> (checklist con mediciones —
/// promovido al MVP el 2026-05-05, ver §12.11.5 del modelo + roadmap §3.B').
/// </summary>
public enum TipoInspeccion
{
    Tecnica,
    Monitoreo
}
