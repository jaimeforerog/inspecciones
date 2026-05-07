using Inspecciones.Domain.Comun;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/monitoreo</c>.
/// La capa API mapea esto al record <c>IniciarInspeccionMonitoreo</c> del dominio.
/// Spec slice 1h §2 + §9.
/// </summary>
public sealed record IniciarInspeccionMonitoreoRequest(
    Guid InspeccionId,
    int EquipoId,
    int ProyectoId,
    int RutinaMonitoreoId,
    UbicacionGps Ubicacion,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario);
