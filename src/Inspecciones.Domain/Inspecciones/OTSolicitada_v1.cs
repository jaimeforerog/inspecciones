namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando el aprobador (capability "generar-ot") solicita la generación
/// de una Orden de Trabajo correctiva para una inspección firmada. Slice 1k — GenerarOT.
/// Ref §3.1 del spec y §17 ADR-007 del modelo de dominio.
/// La saga <c>EjecutarOTSaga</c> (slice 3.24b) reacciona a este evento para invocar M-1.
/// </summary>
public sealed record OTSolicitada_v1(
    Guid             InspeccionId,
    string           SolicitadaPor,       // userId opaco del aprobador
    ResponsableCosto Responsable,
    PrioridadOT      Prioridad,           // decisión P-1: campo explícito del aprobador
    string?          Observaciones,       // texto libre opcional — para la saga EjecutarOTSaga
    string?          ComentarioJefe,      // texto libre opcional — para la saga EjecutarOTSaga
    DateTimeOffset   SolicitadaEn);       // TimeProvider.GetUtcNow() en el handler
