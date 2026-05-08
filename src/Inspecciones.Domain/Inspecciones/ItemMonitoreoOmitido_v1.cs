namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico omite un ítem de la rutina de monitoreo
/// (p. ej. por inaccesibilidad). Slice futura <c>OmitirItemMonitoreo</c>.
/// Stub mínimo creado en slice 1i para que los tests de PRE-6 / I-M4 compilen.
/// </summary>
public sealed record ItemMonitoreoOmitido_v1(
    Guid           InspeccionId,
    int            ItemId,
    string         MotivoOmision,
    string         OmitidoPor,
    DateTimeOffset OmitidoEn);
