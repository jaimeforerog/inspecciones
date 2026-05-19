namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Body del request a <c>POST /api/v4/Maquinaria/api/preoperacional-fallas/cerrar</c>
/// (slice 9 de Maquinaria_V4). Inspecciones lo emite desde el adapter del comando
/// <c>DescartarNovedadPreop</c> (§15.9) y desde la saga <c>CerrarInspeccionSaga</c>.
/// </summary>
/// <remarks>
/// Idempotencia natural: cierre único y definitivo de cada POD (slice 9). No requiere
/// <c>Idempotency-Key</c>. Reabrir el caso unitario invocando con <c>PodIds=[N]</c>
/// (reconciliación bilateral 2026-05-13, P-5 descartado).
/// </remarks>
public sealed record CerrarPreoperacionalFallasRequestDto
{
    public IReadOnlyList<int> PodIds { get; init; } = Array.Empty<int>();
    public string Observaciones { get; init; } = string.Empty;
}

/// <summary>Respuesta 200 OK del endpoint.</summary>
public sealed record CerrarPreoperacionalFallasResponseDto
{
    public int CerradasAhora { get; init; }
    public int YaCerradas { get; init; }
    public int Total { get; init; }
    public IReadOnlyList<int> PodIdsCerradosAhora { get; init; } = Array.Empty<int>();
}
