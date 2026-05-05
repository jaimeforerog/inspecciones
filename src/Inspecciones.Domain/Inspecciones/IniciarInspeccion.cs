using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para crear un stream <see cref="Inspeccion"/> nuevo en estado
/// <see cref="EstadoInspeccion.EnEjecucion"/>. Ver
/// <c>slices/1-iniciar-inspeccion/spec.md</c> §2.
/// </summary>
public sealed record IniciarInspeccion(
    Guid InspeccionId,
    int EquipoId,
    int ProyectoId,
    UbicacionGps UbicacionInicio,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario);
