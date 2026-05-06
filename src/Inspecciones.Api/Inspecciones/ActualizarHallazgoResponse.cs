namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>PATCH /api/v1/inspecciones/{id}/hallazgos/{hid}</c>.
/// Spec slice 2 §9 — respuesta happy path <c>200 OK</c>.
/// </summary>
public sealed record ActualizarHallazgoResponse(
    Guid   HallazgoId,
    DateTimeOffset ActualizadoEn);
