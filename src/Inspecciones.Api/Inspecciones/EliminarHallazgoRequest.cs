namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>DELETE /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}</c>.
/// Spec slice 1e §9.
/// </summary>
public sealed record EliminarHallazgoRequest(string Motivo);