using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Read model local de una rutina de monitoreo, sincronizada desde el ERP vía
/// M-16 (ADR-004 on-app-open). El handler <c>IniciarInspeccionMonitoreoHandler</c>
/// la consulta vía <c>IDocumentSession</c> para validar PRE-4, PRE-5 y PRE-6.
/// Slice 1h — stub mínimo fase red.
/// </summary>
public sealed record RutinaMonitoreoLocal(
    int RutinaMonitoreoId,
    string Nombre,
    int GrupoMantenimientoId,
    string GrupoMantenimiento,
    IReadOnlyList<ItemRutinaMonitoreoLocal> Items,
    DateTimeOffset SincronizadoEn);

/// <summary>
/// Item individual de una rutina de monitoreo en el catálogo local.
/// <see cref="Activo"/> permite deprecar items sin romper inspecciones en curso
/// (decisión D4 del spec 1h §12). El handler filtra por <c>Activo=true</c> antes
/// de construir el snapshot (PRE-6 / I-I-Mon-1).
/// </summary>
public sealed record ItemRutinaMonitoreoLocal(
    int ItemId,
    string Parte,
    string Actividad,
    int Orden,
    bool Activo,
    EvaluacionEsperada Evaluacion);
