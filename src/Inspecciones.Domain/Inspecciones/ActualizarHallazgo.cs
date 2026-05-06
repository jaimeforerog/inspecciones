using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para actualizar los campos editables de un hallazgo existente.
/// Spec slice 2 §2. Los campos de origen son inmutables (I-H8) y no forman
/// parte del payload.
/// </summary>
public sealed record ActualizarHallazgo(
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
    string ActualizadoPor);
