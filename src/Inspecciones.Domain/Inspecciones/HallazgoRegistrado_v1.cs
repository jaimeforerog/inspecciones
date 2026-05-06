using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico registra un hallazgo durante la inspección.
/// Payload según §3 del spec slice 1c. Versionado <c>_v1</c>.
/// </summary>
public sealed record HallazgoRegistrado_v1(
    Guid   InspeccionId,
    Guid   HallazgoId,
    OrigenHallazgo Origen,
    int?   NovedadPreopOrigenId,
    int    ParteEquipoId,
    int?   ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int?   TipoFallaId,
    int?   CausaFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTimeOffset RegistradoEn);
