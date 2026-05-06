namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler de <c>IniciarInspeccion</c>.
/// </summary>
/// <param name="InspeccionId">Id del stream creado, o el de la inspección activa preexistente si <see cref="RedirigeAExistente"/>.</param>
/// <param name="RedirigeAExistente">True si I-I1 corto-circuitó: el equipo ya tenía inspección activa y se devuelve la existente.</param>
/// <param name="Version">Versión actual del stream tras el comando (1 si nuevo, N si redirige).</param>
/// <param name="Mensaje">Null en happy path; "Ya hay inspección activa..." si redirige (spec §2).</param>
public sealed record IniciarInspeccionResult(
    Guid InspeccionId,
    bool RedirigeAExistente,
    int Version,
    string? Mensaje);
