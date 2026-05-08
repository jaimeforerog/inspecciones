namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para solicitar la generación de una Orden de Trabajo correctiva
/// para una inspección firmada con al menos un hallazgo RequiereIntervencion.
/// Slice 1k — GenerarOT. Ref §17 ADR-007 del modelo.
/// PRE-1 (capability "generar-ot") se valida en la capa HTTP antes de llegar al handler.
/// PRE-2 (inspección existe) se valida en el handler.
/// PRE-3..PRE-7 (I-F4) se validan en el método de decisión <see cref="Inspeccion.SolicitarOT"/>.
/// </summary>
public sealed record GenerarOT(
    Guid                          InspeccionId,
    string                        SolicitadaPor,         // userId del aprobador del host PWA — opaco para el dominio
    ResponsableCosto              Responsable,
    string?                       Observaciones,         // texto libre opcional
    string?                       ComentarioJefe,        // comentario adicional del aprobador, opcional
    IReadOnlyCollection<string>   Capabilities,          // debe contener "generar-ot" — verificado en la capa HTTP
    PrioridadOT                   Prioridad              // decisión P-1: campo explícito del aprobador
);
