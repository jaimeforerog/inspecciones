namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando para actualizar los campos patcheables de un repuesto estimado
/// (<c>Cantidad</c> y/o <c>Justificacion</c>). Spec slice 1o §2.
/// <para>Semántica PATCH: <c>null</c> = "no tocar el campo". Al menos uno de los dos
/// campos patcheables debe ser no-<c>null</c> (PRE-8).</para>
/// </summary>
public sealed record ActualizarRepuesto(
    Guid     InspeccionId,
    Guid     HallazgoId,
    Guid     RepuestoId,
    decimal? CantidadNueva,
    string?  ObservacionNueva,
    string   ActualizadoPor);
