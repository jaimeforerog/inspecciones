using Microsoft.AspNetCore.Http;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Implementación de <see cref="IBearerTokenAccessor"/> que lee el header
/// <c>Authorization</c> del <see cref="HttpContext"/> actual y extrae el Bearer
/// token. Si no hay HttpContext (caller fuera de request HTTP — p. ej.
/// listener Wolverine, seed manual) o el header no es Bearer, retorna <c>null</c>.
///
/// D-MT3-1 / MT3-INV-1.
/// </summary>
public sealed class HttpContextBearerTokenAccessor : IBearerTokenAccessor
{
    private const string BearerPrefix = "Bearer ";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextBearerTokenAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? ObtenerBearerToken()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            return null;
        }

        var auth = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth))
        {
            return null;
        }

        // Solo aceptamos esquema Bearer — Basic/Negotiate/etc. no se propagan al ERP.
        if (!auth.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = auth.Substring(BearerPrefix.Length).Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
