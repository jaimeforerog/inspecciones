using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para registrar un hallazgo técnico sobre una inspección en estado
/// <see cref="EstadoInspeccion.EnEjecucion"/>. Spec slice 1c §2.
/// </summary>
public sealed record RegistrarHallazgo(
    Guid   InspeccionId,
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
    string EmitidoPor);
