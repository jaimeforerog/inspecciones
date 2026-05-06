using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>POST /api/v1/inspecciones/{id}/hallazgos</c>.
/// Spec slice 1c §9 — 201 Created happy path.
/// </summary>
public sealed record RegistrarHallazgoResponse(
    Guid HallazgoId,
    Guid InspeccionId,
    AccionRequerida AccionRequerida,
    DateTimeOffset RegistradoEn);
