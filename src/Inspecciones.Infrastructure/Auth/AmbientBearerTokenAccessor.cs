namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Implementación de <see cref="IBearerTokenAccessor"/> que mantiene un token
/// en <see cref="AsyncLocal{T}"/>. La usan los listeners Wolverine para setear
/// el JWT que leyeron del envelope (header <c>X-Forwarded-Authorization</c>)
/// antes de invocar al adapter HTTP. Cuando el ambient está seteado, el
/// <see cref="Erp.BearerTokenPropagationHandler"/> lo recoge via la chain.
///
/// El patrón <c>SetForCurrentScope(jwt)</c> retorna <see cref="IDisposable"/>
/// que limpia el ambient al dispose — uso esperado <c>using var _ = ambient.SetForCurrentScope(jwt)</c>.
///
/// Es scoped singleton: cero estado mutable entre requests, AsyncLocal aísla
/// por contexto async (mismo idea que <c>Activity.Current</c>).
///
/// D-MT3-1 / D-MT3-6 / MT3-INV-2.
/// </summary>
public sealed class AmbientBearerTokenAccessor : IBearerTokenAccessor
{
    // AsyncLocal estático: el ambient es semánticamente UN solo storage por
    // proceso (como Activity.Current o CultureInfo.CurrentCulture). Distintas
    // instancias del accessor leen/escriben el mismo valor, lo que permite
    // que el listener Wolverine setee el token y que el DelegatingHandler del
    // adapter (resuelto por otro scope DI) lo lea — sin pasar la instancia
    // explícitamente.
    private static readonly AsyncLocal<string?> Current = new();

    public string? ObtenerBearerToken()
    {
        var token = Current.Value;
        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>
    /// Setea el bearer token en el contexto async actual. Devuelve un
    /// <see cref="IDisposable"/> que restaura el valor anterior al dispose
    /// (soporta anidamiento). Uso esperado: <c>using var _ = ambient.SetForCurrentScope(jwt)</c>.
    /// Es método de instancia (no static) por convención — el caller no necesita
    /// saber que el storage es estático; consumirlo como instancia permite mock/fake
    /// en tests sin acoplar al type name estático.
    /// </summary>
#pragma warning disable CA1822 // intencional: API de instancia aunque el storage sea AsyncLocal estático.
    public IDisposable SetForCurrentScope(string? token)
#pragma warning restore CA1822
    {
        var anterior = Current.Value;
        Current.Value = token;
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
