namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/medicion</c>.
/// La capa API mapea esto al record <c>RegistrarMedicion</c> del dominio.
/// Spec slice 1i §2 + §9.
/// </summary>
public sealed record RegistrarMedicionRequest(
    Guid    HallazgoId,
    decimal ValorMedido,
    string? Observacion);
