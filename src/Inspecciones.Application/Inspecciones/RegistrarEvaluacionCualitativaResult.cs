namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler <see cref="RegistrarEvaluacionCualitativaHandler"/>.
/// Shape canónico según spec slice 1i' §2. <see cref="HallazgoGeneradoId"/> poblado
/// solo si <c>Calificacion=Malo</c>; null en Bueno/Regular.
/// </summary>
public sealed record RegistrarEvaluacionCualitativaResult(
    Guid           InspeccionId,
    int            ItemId,
    string         Calificacion,
    Guid?          HallazgoGeneradoId,   // Poblado solo si Calificacion=Malo; null en Bueno/Regular.
    DateTimeOffset RegistradaEn);
