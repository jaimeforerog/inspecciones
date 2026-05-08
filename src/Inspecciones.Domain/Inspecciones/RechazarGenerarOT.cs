namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para rechazar explícitamente la generación de una Orden de Trabajo
/// para una inspección firmada con al menos un hallazgo RequiereIntervencion.
/// Slice 1l — RechazarGenerarOT. Ref §17 ADR-007 del modelo.
/// PRE-1 (capability "generar-ot") se valida en la capa HTTP antes de llegar al handler.
/// PRE-2 (inspección existe) se valida en el handler.
/// PRE-3 (Motivo ≥ 10 chars) se valida en el handler o al inicio del método de decisión.
/// PRE-4..PRE-7 (I-F6) se validan en el método de decisión <see cref="Inspeccion.RechazarOT"/>.
/// </summary>
public sealed record RechazarGenerarOT(
    Guid                        InspeccionId,
    string                      Motivo,            // texto libre; min 10 chars (trimmed); obligatorio — D-1: max 500 chars
    string                      RechazadoPor,      // userId del aprobador del host PWA — opaco para el dominio
    IReadOnlyCollection<string> Capabilities       // debe contener "generar-ot" — verificado en la capa HTTP (PRE-1)
);
