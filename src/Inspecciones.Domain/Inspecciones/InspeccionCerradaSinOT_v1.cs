namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento canónico del cierre de una inspección sin generar OT (estado terminal CerradaSinOT).
/// Slice 1l — RechazarGenerarOT. Eleva el stub creado en slice 1k.
/// D-3: se elimina <c>CerradoPor</c> (semánticamente incorrecto para el caso automático)
/// y se añade <c>MotivoCierre</c> como discriminador (D-5: un único event record).
/// D-3: el aprobador queda auditado en <see cref="GeneracionOTRechazada_v1.RechazadoPor"/>
/// cuando el motivo es <see cref="MotivoCierreSinOT.RechazadaPorAprobador"/>.
/// </summary>
public sealed record InspeccionCerradaSinOT_v1(
    Guid                InspeccionId,
    MotivoCierreSinOT   MotivoCierre,    // discriminador del motivo del cierre sin OT (D-3, D-5)
    DateTimeOffset      CerradaEn);      // DateTimeOffset — TimeProvider.GetUtcNow()
