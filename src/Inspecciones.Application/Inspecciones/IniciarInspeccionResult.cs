namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler de <c>IniciarInspeccion</c>.
/// </summary>
/// <param name="InspeccionId">Id del stream creado, o el de la inspección activa preexistente si <see cref="RedirigeAExistente"/>.</param>
/// <param name="RedirigeAExistente">True si I-I1 corto-circuitó: el equipo ya tenía inspección activa y se devuelve la existente.</param>
/// <param name="Version">Versión actual del stream tras el comando.</param>
public sealed record IniciarInspeccionResult(
    Guid InspeccionId,
    bool RedirigeAExistente,
    int Version);
