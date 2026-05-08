namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para omitir un ítem de la rutina de monitoreo durante la ejecución.
/// Slice 1j — OmitirItemMonitoreo.
/// </summary>
public sealed record OmitirItemMonitoreo(
    Guid                          InspeccionId,
    int                           ItemId,
    string                        Motivo,
    string                        EmitidoPor,
    IReadOnlyCollection<string>   Capabilities);
