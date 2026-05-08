namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico registra el valor medido de un ítem numérico
/// de la rutina de monitoreo. Payload según spec slice 1i §3.1.
/// Versionado <c>_v1</c>. Campo <c>RegistradaEn</c> usa <see cref="DateTimeOffset"/>
/// (decisión P-3 — coherencia con resto del módulo).
/// </summary>
public sealed record MedicionRegistrada_v1(
    Guid           InspeccionId,
    int            ItemId,
    decimal        ValorMedido,
    string?        Observacion,
    bool           FueraDeRango,
    string         EmitidoPor,
    DateTimeOffset RegistradaEn);
