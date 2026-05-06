using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>PATCH /api/v1/inspecciones/{id}/hallazgos/{hid}</c>.
/// Spec slice 2 §9. Los campos de origen son inmutables (I-H8) y no forman parte del payload.
/// </summary>
public sealed record ActualizarHallazgoRequest(
    int?   ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int?   TipoFallaId,
    int?   CausaFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion);
