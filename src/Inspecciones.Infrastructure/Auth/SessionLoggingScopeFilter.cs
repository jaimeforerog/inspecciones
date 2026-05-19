using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Endpoint filter Minimal API que abre un <see cref="SessionLoggingScope"/>
/// alrededor de cada invocación del endpoint. Aplica MT4-INV-3 a TODOS los
/// endpoints de manera no-invasiva — sin modificar los handlers/endpoints
/// individualmente.
///
/// Comportamiento:
/// - Si <see cref="ISessionService"/> es resoluble desde el scope y expone
///   IdEmpresa válido → abre scope con propiedades IdEmpresa/IdUsuario
///   durante toda la ejecución del endpoint.
/// - Si lanza ClaimRequeridaException → el filtro NO abre scope; deja que el
///   endpoint corra y emita la excepción que el middleware global mapea a 401.
/// - Endpoints sin ISessionService disponible (p. ej. /health/live) → no-op.
///
/// Registro: <c>app.MapGroup("...").AddEndpointFilter&lt;SessionLoggingScopeFilter&gt;()</c>
/// o globalmente en el routing.
///
/// MT4-INV-3 / D-MT4-2.
/// </summary>
public sealed class SessionLoggingScopeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var sp = context.HttpContext.RequestServices;
        var session = sp.GetService(typeof(ISessionService)) as ISessionService;
        if (session is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        // Usamos un logger de categoría neutra para que el scope no esté ligado
        // a un tipo específico. El BeginEmpresaScope tolera ClaimRequeridaException
        // y retorna null cuando la claim no está — en ese caso el endpoint corre
        // sin scope y emite 401 al acceder a IdEmpresa.
        var loggerFactory = sp.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        if (loggerFactory is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var logger = loggerFactory.CreateLogger("Inspecciones.Endpoint");
        using var _ = logger.BeginEmpresaScope(session);
        return await next(context).ConfigureAwait(false);
    }
}
