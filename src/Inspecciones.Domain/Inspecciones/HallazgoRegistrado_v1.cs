using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un técnico registra un hallazgo durante la inspección.
/// Payload según §3 del spec slice 1c. Versionado <c>_v1</c>.
/// Campo <c>MedicionOrigenId</c>: PK ERP del ítem numérico origen. Obligatorio cuando
/// <c>Origen=Monitoreo</c> (numérico); null en orígenes Manual/PreOperacional (backward compat — slice 1i).
/// Campo <c>EvaluacionOrigenId</c>: PK ERP del ítem cualitativo origen. Obligatorio cuando
/// <c>Origen=Monitoreo</c> (cualitativo) y la fuente es <c>RegistrarEvaluacionCualitativa</c>;
/// null en todos los demás casos. Backward compat — slice 1i'. Los dos campos son excluyentes:
/// uno de ellos es null siempre.
/// </summary>
public sealed record HallazgoRegistrado_v1(
    Guid   InspeccionId,
    Guid   HallazgoId,
    OrigenHallazgo Origen,
    int?   NovedadPreopOrigenId,
    int?   MedicionOrigenId,
    int?   EvaluacionOrigenId,     // NUEVO (slice 1i') — null para orígenes no cualitativos (backward compat).
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
