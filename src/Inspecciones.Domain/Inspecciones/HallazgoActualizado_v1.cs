using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico actualiza un hallazgo existente durante la inspección.
/// Payload según §3 del spec slice 2. Versionado <c>_v1</c>.
/// Los campos de origen (<c>Origen</c>, <c>NovedadPreopOrigenId</c>, <c>SeguimientoOrigenId</c>,
/// <c>ParteEquipoId</c>) son inmutables (I-H8) — no forman parte de este evento.
/// </summary>
public sealed record HallazgoActualizado_v1(
    Guid   InspeccionId,
    Guid   HallazgoId,
    int?   ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int?   TipoFallaId,
    int?   CausaFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,
    string ActualizadoPor,
    DateTimeOffset ActualizadoEn);
