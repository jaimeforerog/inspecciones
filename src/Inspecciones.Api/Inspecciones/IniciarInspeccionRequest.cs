using Inspecciones.Domain.Comun;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones</c>. La capa API
/// mapea esto al record <c>IniciarInspeccion</c> del dominio. Spec §9.
/// </summary>
public sealed record IniciarInspeccionRequest(
    Guid InspeccionId,
    int EquipoId,
    int ProyectoId,
    UbicacionGps UbicacionInicio,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario);
