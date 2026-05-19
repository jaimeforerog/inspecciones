namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Carrier estático del header <c>Authorization</c> entrante del request HTTP.
/// Lo setea el ASP.NET middleware <see cref="CaptureBearerForOutboxMiddleware"/>
/// al inicio de cada request y lo lee <see cref="ForwardAuthEnvelopeRule"/> al
/// hacer outbox publish — para escribir <c>X-Forwarded-Authorization</c> en el
/// envelope Wolverine.
///
/// Por qué AsyncLocal estático y no DI scoped:
///   - El outbox publish ocurre dentro del mismo async-flow del handler HTTP.
///   - Wolverine no expone el HttpContext a los <see cref="Wolverine.IEnvelopeRule"/>
///     (son framework-agnostic).
///   - AsyncLocal aísla por contexto async (mismo idea que <c>Activity.Current</c>
///     o <c>CultureInfo.CurrentCulture</c>).
///
/// Patrón equivalente al de <see cref="AmbientBearerTokenAccessor"/> (mt-3) pero
/// con scope distinto: aquí el carrier es para el bearer entrante (HTTP scope);
/// `AmbientBearerTokenAccessor` es para el bearer del envelope (listener scope).
/// La distinción semántica justifica dos componentes — D-MT4-1 spec.
///
/// MT4-INV-2 / FU-60.
/// </summary>
public static class IncomingBearerCarrier
{
    // AsyncLocal estático: el carrier es semánticamente UN solo storage por
    // proceso. AsyncLocal lo aísla por contexto async automáticamente.
    private static readonly AsyncLocal<string?> Current = new();

    /// <summary>
    /// Retorna el header <c>Authorization</c> capturado por el middleware en el
    /// scope async actual. <c>null</c> si no hay scope HTTP o el middleware no
    /// vio un Bearer válido en el request.
    /// </summary>
    public static string? GetForwardedAuth()
    {
        var value = Current.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Setea el header en el scope async actual. Devuelve <see cref="IDisposable"/>
    /// que restaura el valor anterior al dispose — soporta anidamiento.
    /// </summary>
    public static IDisposable SetForCurrentScope(string? authHeader)
    {
        var anterior = Current.Value;
        Current.Value = authHeader;
        return new ScopeReverter(anterior);
    }

    private sealed class ScopeReverter : IDisposable
    {
        private readonly string? _anterior;
        private bool _disposed;

        public ScopeReverter(string? anterior)
        {
            _anterior = anterior;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Current.Value = _anterior;
            _disposed = true;
        }
    }
}
