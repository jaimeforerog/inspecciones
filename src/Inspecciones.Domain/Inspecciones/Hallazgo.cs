using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Value object que representa un hallazgo registrado en la inspección.
/// Materializado en el agregado a partir de <see cref="HallazgoRegistrado_v1"/>.
/// Shape según §15.2 del modelo. Extendido en slice 2 con los campos editables
/// que <see cref="HallazgoActualizado_v1"/> puede mutar.
/// Los campos de origen (<see cref="Origen"/>, <see cref="NovedadPreopOrigenId"/>,
/// <see cref="ParteEquipoId"/>) son inmutables (I-H8).
/// </summary>
public sealed record Hallazgo(
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
    UbicacionGps? Ubicacion,
    bool   Eliminado);
