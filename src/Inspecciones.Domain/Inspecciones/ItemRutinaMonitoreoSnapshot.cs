namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Snapshot inmutable de un item activo de la rutina de monitoreo, capturado al
/// momento de iniciar la inspección. Preserva la <see cref="EvaluacionEsperada"/>
/// para que <c>FueraDeRango</c> se calcule correctamente en slices futuros aunque
/// el catálogo cambie. Slice 1h — stub mínimo fase red.
/// </summary>
public sealed record ItemRutinaMonitoreoSnapshot(
    int ItemId,
    string Parte,
    string Actividad,
    EvaluacionEsperada Evaluacion,
    // Slice 1i — P-1 (opción A): ParteEquipoId int? (nullable backward-compat con snapshots
    // del slice 1h donde el campo aún no se capturaba). Followup #22: confirmar con David
    // que M-16 expone ParteEquipoId por ítem.
    int? ParteEquipoId = null);
