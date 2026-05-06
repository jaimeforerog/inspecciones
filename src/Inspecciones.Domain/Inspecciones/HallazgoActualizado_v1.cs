using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando se actualizan los campos mutables de un hallazgo.
/// Los campos inmutables (Origen, ParteEquipoId, NovedadPreopOrigenId,
/// SeguimientoOrigenId) no forman parte del payload — invariante I-H8.
/// Spec slice 1d §3.
/// </summary>
public sealed record HallazgoActualizado_v1(
    Guid             InspeccionId,
    Guid             HallazgoId,
    string           NovedadTecnica,
    AccionRequerida  AccionRequerida,
    string?          AccionCorrectiva,
    int?             TipoFallaId,
    int?             CausaFallaId,
    string?          ObservacionCampo,
    UbicacionGps?    UbicacionGps,
    DateTimeOffset   ActualizadoEn,
    string           EmitidoPor);
