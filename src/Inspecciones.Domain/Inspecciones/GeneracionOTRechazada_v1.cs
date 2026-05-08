namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento stub para representar el rechazo de una solicitud de OT por parte
/// del aprobador. Slice 1k — GenerarOT (stub para PRE-6 / I-F4.d).
/// La implementación completa pertenece a un slice futuro (RechazarGenerarOT).
/// </summary>
public sealed record GeneracionOTRechazada_v1(
    Guid           InspeccionId,
    string         RechazadoPor,
    string         MotivoRechazo,
    DateTimeOffset RechazadaEn);
