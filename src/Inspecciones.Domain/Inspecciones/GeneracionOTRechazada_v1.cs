namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento canónico del rechazo de generación de OT por parte del aprobador.
/// Slice 1l — RechazarGenerarOT. Eleva el stub creado en slice 1k.
/// D-2: el campo se llama <c>Motivo</c> (alineado con el command record §17),
/// no <c>MotivoRechazo</c> como tenía el stub de 1k.
/// </summary>
public sealed record GeneracionOTRechazada_v1(
    Guid           InspeccionId,
    string         Motivo,          // texto auditado del rechazo (D-2)
    string         RechazadoPor,    // userId opaco del aprobador
    DateTimeOffset RechazadaEn);    // DateTimeOffset — TimeProvider.GetUtcNow()
