namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de request para el endpoint
/// <c>PATCH /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos/{repuestoId}</c>.
/// Spec slice 1o §2 + §9.
/// <para>
/// Semántica PATCH: ambos campos son opcionales. Al menos uno debe ser no-<c>null</c> (PRE-8).
/// <c>InspeccionId</c>, <c>HallazgoId</c> y <c>RepuestoId</c> viajan en el path.
/// </para>
/// </summary>
public sealed record ActualizarRepuestoRequest(
    decimal? CantidadNueva,
    string?  ObservacionNueva);
