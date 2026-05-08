namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para registrar la calificación cualitativa de un ítem de la rutina
/// de monitoreo durante la ejecución de la inspección (slice 1i'). Shape canónico
/// definido en spec §2. <see cref="HallazgoId"/> ignorado si Calificacion != Malo.
/// </summary>
public sealed record RegistrarEvaluacionCualitativa(
    Guid     InspeccionId,
    Guid     HallazgoId,           // Generado client-side (UUIDv7 preferido). Ignorado si Calificacion != Malo.
    int      ItemId,
    CalificacionCualitativa Calificacion,
    string?  Observacion,
    string   EmitidoPor,
    IReadOnlyCollection<string> Capabilities);
