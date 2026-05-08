namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler <see cref="GenerarOTHandler"/>.
/// Shape canónico según spec slice 1k §2. Devuelve los campos necesarios para
/// que el endpoint construya el body de la respuesta 202 Accepted.
/// </summary>
public sealed record GenerarOTResult(
    Guid           InspeccionId,
    DateTimeOffset SolicitadaEn,
    string         SolicitadaPor,
    string         Responsable,
    string         Prioridad);
