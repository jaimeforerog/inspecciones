using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para iniciar una inspección de monitoreo sobre un equipo. El técnico
/// elige la rutina de monitoreo aplicable al grupo del equipo. Ver spec
/// <c>slices/1h-iniciar-inspeccion-monitoreo/spec.md</c> §2.
/// Stub mínimo — slice 1h fase red.
/// </summary>
public sealed record IniciarInspeccionMonitoreo(
    Guid InspeccionId,
    int EquipoId,
    int ProyectoId,
    int RutinaMonitoreoId,
    string IniciadaPor,
    UbicacionGps Ubicacion,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario,
    IReadOnlyCollection<string> Capabilities);
