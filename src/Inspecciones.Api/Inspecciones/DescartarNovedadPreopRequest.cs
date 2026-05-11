namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de request para <c>POST /api/v1/inspecciones/{inspeccionId}/novedades-preop/{novedadId}/descartar</c>.
/// Slice 1n — DescartarNovedadPreop. Spec §2 y §9.
/// El motivo es server-generated (D-4); el cliente no lo envía.
/// </summary>
public sealed record DescartarNovedadPreopRequest(
    string? DescartadaPor);   // userId del técnico; el motivo es server-generated
