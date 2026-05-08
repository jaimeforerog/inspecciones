using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico registra un hallazgo durante la inspección.
/// Payload según §3 del spec slice 1c. Versionado <c>_v1</c>.
/// Campo <c>MedicionOrigenId</c>: PK ERP del ítem de monitoreo origen. Obligatorio cuando
/// <c>Origen=Monitoreo</c>; null en orígenes Manual/PreOperacional (backward compat — slice 1i).
/// </summary>
public sealed record HallazgoRegistrado_v1(
    Guid   InspeccionId,
    Guid   HallazgoId,
    OrigenHallazgo Origen,
    int?   NovedadPreopOrigenId,
    int?   MedicionOrigenId,
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
