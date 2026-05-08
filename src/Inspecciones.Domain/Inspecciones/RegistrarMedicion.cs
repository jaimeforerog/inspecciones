namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para registrar el valor medido de un ítem numérico de la rutina de
/// monitoreo durante la ejecución de la inspección. Slice 1i — stub mínimo fase red.
/// Versión final definida en spec §2.
/// </summary>
public sealed record RegistrarMedicion(
    Guid   InspeccionId,
    Guid   HallazgoId,
    int    ItemId,
    decimal ValorMedido,
    string? Observacion,
    string EmitidoPor,
    IReadOnlyCollection<string> Capabilities);
