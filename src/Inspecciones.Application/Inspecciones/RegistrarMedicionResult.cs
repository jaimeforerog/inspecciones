namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler <see cref="RegistrarMedicionHandler"/>.
/// Stub mínimo fase red. Shape canónico según spec slice 1i §2.
/// </summary>
public sealed record RegistrarMedicionResult(
    Guid           InspeccionId,
    int            ItemId,
    decimal        ValorMedido,
    bool           FueraDeRango,
    Guid?          HallazgoGeneradoId,
    DateTimeOffset RegistradaEn);
