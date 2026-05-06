using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>PUT /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}</c>.
/// Spec slice 1d §7 — 200 OK happy path.
/// </summary>
public sealed record ActualizarHallazgoResponse(
    Guid HallazgoId,
    Guid InspeccionId,
    AccionRequerida AccionRequerida,
    DateTimeOffset ActualizadoEn);
