namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler <see cref="RechazarGenerarOTHandler"/>.
/// Shape canónico según spec slice 1l §2. Devuelve los campos necesarios para
/// que el endpoint construya el body de la respuesta 200 OK (D-4: cierre síncrono).
/// </summary>
public sealed record RechazarGenerarOTResult(
    Guid           InspeccionId,
    string         Estado,          // "CerradaSinOT" — estado terminal post-rechazo
    DateTimeOffset RechazadaEn,
    string         RechazadoPor,
    string         Motivo);
