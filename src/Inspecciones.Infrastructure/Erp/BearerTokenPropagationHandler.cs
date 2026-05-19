using System.Net.Http.Headers;
using Inspecciones.Infrastructure.Auth;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// <see cref="DelegatingHandler"/> que setea el header <c>Authorization</c>
/// de cada request al ERP Maquinaria_V4 con el Bearer token resuelto por
/// <see cref="IBearerTokenAccessor"/>. Reemplaza la asignación estática que
/// vivía en <c>Program.cs</c> (<c>http.DefaultRequestHeaders.Authorization</c>
/// con <see cref="MaquinariaErpOptions.JwtToken"/> fijo).
///
/// Comportamiento:
/// - Si el accessor retorna un token → setea <c>Authorization: Bearer {token}</c>
///   (sobrescribe cualquier valor anterior — MT3-INV-4).
/// - Si retorna null → lanza <see cref="BearerTokenAusenteException"/> ANTES de
///   enviar la request (MT3-INV-3 fail-closed).
///
/// Spec slice mt-3 §2 + D-MT3-5.
/// </summary>
public sealed class BearerTokenPropagationHandler : DelegatingHandler
{
    private readonly IBearerTokenAccessor _accessor;

    public BearerTokenPropagationHandler(IBearerTokenAccessor accessor)
    {
        _accessor = accessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = _accessor.ObtenerBearerToken();
        if (string.IsNullOrEmpty(token))
        {
            throw new BearerTokenAusenteException(
                $"No hay Bearer token disponible para la llamada a {request.RequestUri}. " +
                "Configure MaquinariaErpOptions.JwtToken como fallback o asegure HttpContext/Envelope.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
