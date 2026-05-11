namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del comando <see cref="Inspecciones.Domain.Inspecciones.DescartarNovedadPreop"/>.
/// Slice 1n — DescartarNovedadPreop. Spec §2.
/// </summary>
public sealed record DescartarNovedadPreopResult(
    Guid           InspeccionId,
    int            NovedadId,
    string         MotivoDescarte,    // devuelto para confirmación UX (spec §2)
    string         DescartadaPor,
    DateTimeOffset DescartadaEn);
