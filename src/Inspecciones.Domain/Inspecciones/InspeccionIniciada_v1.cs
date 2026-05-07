using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento de creación del stream <see cref="Inspeccion"/>. Payload vigente
/// según §12.11.3 + §15.4 del modelo. Versionado <c>_v1</c>; futuras versiones
/// se sufijan <c>_v2</c> sin tocar este record.
/// Extendido en slice 1h con campos de monitoreo (nullable — backward compat
/// con streams Tipo=Tecnica del slice 1b). Ver decisión D6 del spec 1h §12.
/// </summary>
public sealed record InspeccionIniciada_v1(
    Guid InspeccionId,
    TipoInspeccion Tipo,
    int EquipoId,
    int RutinaId,
    string RutinaCodigo,
    string TecnicoIniciador,
    int ProyectoId,
    UbicacionGps Ubicacion,
    DateTimeOffset IniciadaEn,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario,
    // Campos slice 1h — null cuando Tipo=Tecnica (backward compat).
    int? RutinaMonitoreoSeleccionadaId = null,
    IReadOnlyList<ItemRutinaMonitoreoSnapshot>? ItemsSnapshot = null);
