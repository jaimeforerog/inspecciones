namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada para <c>POST /api/v1/inspecciones/{id}/rechazar-generar-ot</c>.
/// Spec slice 1l §2 y §9. El payload es mínimo — sólo el motivo del rechazo.
/// </summary>
/// <param name="Motivo">Motivo del rechazo. Texto libre, mínimo 10 chars (trimmed). D-1 máximo 500 chars.</param>
public sealed record RechazarGenerarOTRequest(
    string Motivo);
