namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Composición de <see cref="IBearerTokenAccessor"/> que intenta tres fuentes
/// en orden: HttpContext (request HTTP entrante) → Ambient (envelope Wolverine
/// seteado por el listener) → ServiceAccount (token de servicio de config).
/// Devuelve el primero no-vacío.
///
/// Orden fijo, no configurable. Razón: la prioridad refleja "más específico
/// gana" — el caller HTTP es la fuente más fresca; el envelope puede traer un
/// JWT que ya expiró durante retries; el service-account es el último recurso.
///
/// D-MT3-2 / MT3-INV-1 / MT3-INV-2.
/// </summary>
public sealed class ChainedBearerTokenAccessor : IBearerTokenAccessor
{
    private readonly HttpContextBearerTokenAccessor _http;
    private readonly AmbientBearerTokenAccessor _ambient;
    private readonly ServiceAccountBearerTokenAccessor _service;

    public ChainedBearerTokenAccessor(
        HttpContextBearerTokenAccessor http,
        AmbientBearerTokenAccessor ambient,
        ServiceAccountBearerTokenAccessor service)
    {
        _http = http;
        _ambient = ambient;
        _service = service;
    }

    public string? ObtenerBearerToken()
    {
        return _http.ObtenerBearerToken()
            ?? _ambient.ObtenerBearerToken()
            ?? _service.ObtenerBearerToken();
    }
}
