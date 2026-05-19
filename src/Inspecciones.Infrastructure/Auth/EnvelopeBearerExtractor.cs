using Wolverine;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Utilidad estática para extraer el Bearer raw del header
/// <c>X-Forwarded-Authorization</c> de un envelope Wolverine.
///
/// Lo consumen los listeners ERP (<c>DescartarNovedadPreopErpListener</c>,
/// <c>SincronizarDictamenVigenteListener</c>) para setear el ambient antes
/// de invocar al adapter HTTP (mt-3 D-MT3-6).
///
/// Comportamiento:
/// - <c>null</c> envelope.Headers → null.
/// - header ausente o vacío → null.
/// - scheme distinto a "Bearer " (case-insensitive) → null.
/// - "Bearer "&lt;token&gt; → token trimmeado, o null si vacío post-trim.
/// </summary>
public static class EnvelopeBearerExtractor
{
    private const string ForwardedAuthHeader = "X-Forwarded-Authorization";
    private const string BearerPrefix = "Bearer ";

    public static string? ExtraerBearerForwarded(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Headers is null)
        {
            return null;
        }
        if (!envelope.Headers.TryGetValue(ForwardedAuthHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (!raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var token = raw.Substring(BearerPrefix.Length).Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
