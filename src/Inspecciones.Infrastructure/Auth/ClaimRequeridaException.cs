namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Se lanza desde una implementación de <see cref="ISessionService"/> cuando una
/// claim requerida del JWT del host está ausente o no parsea. Se mapea a HTTP
/// <c>401 Unauthorized</c> con body
/// <c>{ codigoError: "CLAIM-{NOMBRE}-AUSENTE", mensaje: ... }</c> por un handler
/// global en <c>Program.cs</c>.
///
/// PRE-AUTH-3 / PRE-AUTH-4 del spec slice mt-1 §4. La razón por la que esto es
/// 401 y no 403: el problema es la integridad del token, no el authorization
/// scope del usuario autenticado.
/// </summary>
public sealed class ClaimRequeridaException : Exception
{
    /// <summary>Nombre de la claim ausente (case-sensitive, paridad con el JWT del host).</summary>
    public string NombreClaim { get; }

    /// <summary>Código de error HTTP — derivado del nombre de la claim (mayúsculas).</summary>
    public string CodigoError => $"CLAIM-{NombreClaim.ToUpperInvariant()}-AUSENTE";

    public ClaimRequeridaException(string nombreClaim)
        : base($"La claim '{nombreClaim}' es requerida en el JWT del host.")
    {
        NombreClaim = nombreClaim;
    }
}
