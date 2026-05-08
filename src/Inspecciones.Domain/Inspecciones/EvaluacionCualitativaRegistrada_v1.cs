namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico registra la calificación cualitativa de un
/// ítem de la rutina de monitoreo. Payload según spec slice 1i' §3.1.
/// Versionado <c>_v1</c>. Campo <c>RegistradaEn</c> usa <see cref="DateTimeOffset"/>
/// (coherencia con el módulo — mismo ajuste que MedicionRegistrada_v1). Campo
/// <c>EmitidoPor</c> aprobado en firma del spec (2026-05-08) para coherencia con el
/// resto de eventos de acción del módulo.
/// </summary>
public sealed record EvaluacionCualitativaRegistrada_v1(
    Guid                  InspeccionId,
    int                   ItemId,
    CalificacionCualitativa Calificacion,
    string?               Observacion,
    string                EmitidoPor,
    DateTimeOffset        RegistradaEn);
