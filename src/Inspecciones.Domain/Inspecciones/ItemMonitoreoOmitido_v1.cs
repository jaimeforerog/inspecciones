namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico omite un ítem de la rutina de monitoreo
/// (p. ej. por inaccesibilidad). Slice 1j — OmitirItemMonitoreo.
/// </summary>
public sealed record ItemMonitoreoOmitido_v1(
    Guid           InspeccionId,
    int            ItemId,
    string         Motivo,
    string         EmitidoPor,
    DateTimeOffset OmitidoEn);
