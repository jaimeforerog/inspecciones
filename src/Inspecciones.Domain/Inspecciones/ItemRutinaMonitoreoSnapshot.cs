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
    EvaluacionEsperada Evaluacion);
