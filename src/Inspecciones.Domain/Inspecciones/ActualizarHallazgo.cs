using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para actualizar los campos mutables de un hallazgo ya registrado en
/// una inspección en estado <see cref="EstadoInspeccion.EnEjecucion"/>.
/// Spec slice 1d §2. Los campos inmutables (Origen, ParteEquipoId,
/// NovedadPreopOrigenId) se ignoran — viven en <see cref="HallazgoRegistrado_v1"/>.
/// </summary>
public sealed record ActualizarHallazgo(
    Guid             InspeccionId,
    Guid             HallazgoId,
    string           NovedadTecnica,
    AccionRequerida  AccionRequerida,
    string?          AccionCorrectiva,
    int?             TipoFallaId,
    int?             CausaFallaId,
    string?          ObservacionCampo,
    UbicacionGps?    UbicacionGps,
    string           TecnicoId);
