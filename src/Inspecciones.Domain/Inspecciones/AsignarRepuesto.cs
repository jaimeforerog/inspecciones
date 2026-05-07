namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para registrar un repuesto estimado en un hallazgo que requiere
/// intervención. Spec slice 1f §2.
/// <para>
/// <c>Unidad</c> y <c>SkuCodigo</c> no viajan en el comando — el handler los
/// deriva del catálogo local (<c>RepuestoLocal</c>) y los pasa como parámetros
/// adicionales al método de decisión del aggregate.
/// </para>
/// </summary>
public sealed record AsignarRepuesto(
    Guid    InspeccionId,
    Guid    HallazgoId,
    Guid    RepuestoId,
    int     SkuId,
    decimal Cantidad,
    string? Justificacion,
    string  TecnicoId);