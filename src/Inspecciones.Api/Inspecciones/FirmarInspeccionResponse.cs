using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>POST /api/v1/inspecciones/{id}/firmar</c>.
/// Devuelve el estado resultante y el dictamen establecido. Spec §9.
/// </summary>
public sealed record FirmarInspeccionResponse(
    Guid              InspeccionId,
    string            Estado,
    DateTimeOffset    FirmadaEn,
    DictamenOperacion Dictamen);
