namespace Inspecciones.Application.Inspecciones;

/// <summary>Resultado de ejecutar <see cref="Inspecciones.Domain.Inspecciones.ActualizarRepuesto"/>. Spec slice 1o §2.</summary>
public sealed record ActualizarRepuestoResult(
    Guid           InspeccionId,
    Guid           HallazgoId,
    Guid           RepuestoId,
    decimal        Cantidad,
    string?        Justificacion,
    DateTimeOffset ActualizadoEn);
