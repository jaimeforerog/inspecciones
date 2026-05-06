using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/{id}/hallazgos</c>.
/// Spec slice 1c §9.
/// </summary>
public sealed record RegistrarHallazgoRequest(
    Guid   HallazgoId,
    OrigenHallazgo Origen,
    int    ParteEquipoId,
    int?   NovedadPreopOrigenId,
    int?   ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int?   TipoFallaId,
    int?   CausaFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion);
