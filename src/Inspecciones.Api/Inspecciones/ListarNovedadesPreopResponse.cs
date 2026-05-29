using System.Globalization;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Erp.Dtos;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>GET /api/v1/inspecciones/{id}/novedades-preop</c>
/// (slice 1q). Una novedad de preoperacional importable, con los campos que el ERP
/// Maquinaria_V4 expone hoy (<see cref="PreoperacionalFallaErpDto"/>) más el
/// <see cref="Estado"/> derivado server-side desde el aggregate.
/// </summary>
/// <remarks>
/// Campos NO disponibles sin cambio en Maquinaria_V4 (ver
/// <c>09-solicitud-cambio-maquinaria-preop-fallas.md</c>) y por tanto ausentes:
/// <list type="bullet">
///   <item><c>parteEquipoId</c> — el front lo resuelve con el selector de parte manual (Paso 1).</item>
///   <item><c>responsableId</c> / <c>responsableNombre</c> — sin fuente; se omiten.</item>
/// </list>
/// <c>codigoPreoperacional</c> se sintetiza desde <c>registroPreoperacionalId</c>
/// (no hay folio textual upstream).
/// </remarks>
public sealed record NovedadPreopImportableDto(
    int                      NovedadPreopOrigenId,    // = ERP id (PODId) — va al POST de hallazgo
    int                      RegistroPreoperacionalId,
    string                   CodigoPreoperacional,    // sintetizado: "PREOP-{RegistroPreoperacionalId}"
    int                      EquipoId,
    int                      ActividadId,
    string                   ActividadDescripcion,
    string                   ArbolDescripcion,
    string                   NovedadTecnica,          // = observacion del ERP (pre-llena el form)
    DateTimeOffset           FechaRegistro,           // = fecha del ERP
    EstadoNovedadImportacion Estado)                  // derivado del aggregate (Disponible|Importada|Descartada)
{
    /// <summary>Proyecta un DTO del ERP + el estado derivado al DTO de salida.</summary>
    public static NovedadPreopImportableDto Desde(PreoperacionalFallaErpDto falla, EstadoNovedadImportacion estado) => new(
        NovedadPreopOrigenId: falla.Id,
        RegistroPreoperacionalId: falla.RegistroPreoperacionalId,
        CodigoPreoperacional: $"PREOP-{falla.RegistroPreoperacionalId.ToString(CultureInfo.InvariantCulture)}",
        EquipoId: falla.EquipoId,
        ActividadId: falla.ActividadId,
        ActividadDescripcion: falla.ActividadDescripcion,
        ArbolDescripcion: falla.ArbolDescripcion,
        NovedadTecnica: falla.Observacion,
        FechaRegistro: falla.Fecha,
        Estado: estado);
}

/// <summary>Respuesta del listado de novedades preop importables de una inspección.</summary>
public sealed record ListarNovedadesPreopResponse(
    Guid                                       InspeccionId,
    int                                        EquipoId,
    IReadOnlyList<NovedadPreopImportableDto>   Novedades,
    int                                        Total);
