namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/evaluacion</c>.
/// La capa API mapea esto al record <c>RegistrarEvaluacionCualitativa</c> del dominio.
/// Spec slice 1i' §2 + §9.
/// </summary>
public sealed record RegistrarEvaluacionCualitativaRequest(
    Guid    HallazgoId,
    string  Calificacion,   // "Bueno" | "Regular" | "Malo" — deserializado a CalificacionCualitativa en el endpoint
    string? Observacion);
