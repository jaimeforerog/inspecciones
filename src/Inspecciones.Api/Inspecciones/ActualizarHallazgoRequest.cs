using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>PUT /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}</c>.
/// Spec slice 1d §7.
/// </summary>
public sealed record ActualizarHallazgoRequest(
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? TipoFallaId,
    int? CausaFallaId,
    string? ObservacionCampo,
    UbicacionGps? UbicacionGps);
