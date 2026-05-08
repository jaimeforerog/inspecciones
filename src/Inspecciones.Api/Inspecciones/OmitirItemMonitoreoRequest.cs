namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/omitir</c>.
/// La capa API mapea esto al record <c>OmitirItemMonitoreo</c> del dominio.
/// Spec slice 1j §2 + §9.
/// </summary>
public sealed record OmitirItemMonitoreoRequest(
    string Motivo);
