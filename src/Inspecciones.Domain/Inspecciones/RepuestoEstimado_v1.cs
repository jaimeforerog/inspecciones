namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando un repuesto es estimado para un hallazgo con
/// <c>AccionRequerida=RequiereIntervencion</c>. Spec slice 1f §3.
/// <para>
/// <c>SkuCodigo</c> se desnormaliza desde el catálogo local para legibilidad
/// en el stream sin necesidad de joins posteriores. <c>Unidad</c> se deriva
/// de <c>RepuestoLocal.UnidadMedida</c> en el handler.
/// </para>
/// </summary>
public sealed record RepuestoEstimado_v1(
    Guid           InspeccionId,
    Guid           HallazgoId,
    Guid           RepuestoId,
    int            SkuId,
    string         SkuCodigo,
    decimal        Cantidad,
    string?        Justificacion,
    string         Unidad,
    string         AsignadoPor,
    DateTimeOffset AsignadoEn);