using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Extensión de <see cref="ILogger"/> que abre un scope estructurado con las
/// propiedades <c>IdEmpresa</c> e <c>IdUsuario</c> del <see cref="ISessionService"/>.
/// Permite que App Insights / Azure Monitor filtren logs y métricas por tenant.
///
/// MT4-INV-3 / D-MT4-2 — observabilidad por <c>IdEmpresa</c>.
///
/// Comportamiento defensivo: si el <see cref="ISessionService"/> lanza
/// <see cref="ClaimRequeridaException"/> al acceder a IdEmpresa o IdUsuario,
/// el helper retorna null sin propagar la excepción. El endpoint
/// inmediatamente después devolverá 401 (middleware global ya cablado en
/// mt-1) — no enriquecemos el scope con datos inválidos.
///
/// También enriquece <see cref="Activity.Current"/> con el tag
/// <c>id_empresa</c> si hay activity activo (App Insights propagará el tag).
/// </summary>
public static class SessionLoggingScope
{
    /// <summary>
    /// Abre un scope con <c>IdEmpresa</c> e <c>IdUsuario</c> como propiedades.
    /// Devuelve <c>null</c> si la session no expone IdEmpresa (pre-auth fail-fast).
    /// Uso esperado: <c>using var _ = logger.BeginEmpresaScope(session);</c>
    /// </summary>
    public static IDisposable? BeginEmpresaScope(this ILogger logger, ISessionService session)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            var idEmpresa = session.IdEmpresa;
            var idUsuario = session.IdUsuario;

            // Distributed tracing: si hay Activity activo (ASP.NET genera uno por
            // request), añadir el tag id_empresa. AppInsights y OpenTelemetry lo
            // propagarán como dimension. Es no-op si Activity.Current es null.
            Activity.Current?.AddTag(
                "id_empresa",
                idEmpresa.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return logger.BeginScope(new Dictionary<string, object?>
            {
                ["IdEmpresa"] = idEmpresa,
                ["IdUsuario"] = idUsuario,
            });
        }
        catch (ClaimRequeridaException)
        {
            // Pre-auth: IdEmpresa o IdUsuario no disponible. No enriquecemos el
            // scope — el endpoint va a fallar 401 inmediatamente (middleware mt-1).
            return null;
        }
    }
}
