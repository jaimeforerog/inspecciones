using Inspecciones.Infrastructure.Erp;
using Microsoft.Extensions.Options;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Implementación de <see cref="IBearerTokenAccessor"/> que devuelve el token
/// de servicio configurado en <see cref="MaquinariaErpOptions.JwtToken"/>.
/// Es el último fallback de la cadena (D-MT3-2 / D-MT3-3): se usa cuando no
/// hay HttpContext ni envelope con <c>X-Forwarded-Authorization</c>.
///
/// Casos de uso legales:
/// - Seed manual / bootstrap (sin HTTP scope).
/// - Listener-to-listener publish (sin HttpContext).
/// - Mensaje legacy del outbox publicado antes de mt-3.
///
/// String vacío en <see cref="MaquinariaErpOptions.JwtToken"/> equivale a
/// "sin service-account" → retorna null y la chain falla con
/// <see cref="BearerTokenAusenteException"/> (fail-closed).
/// </summary>
public sealed class ServiceAccountBearerTokenAccessor : IBearerTokenAccessor
{
    private readonly IOptions<MaquinariaErpOptions> _options;

    public ServiceAccountBearerTokenAccessor(IOptions<MaquinariaErpOptions> options)
    {
        _options = options;
    }

    public string? ObtenerBearerToken()
    {
        var token = _options.Value.JwtToken;
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
