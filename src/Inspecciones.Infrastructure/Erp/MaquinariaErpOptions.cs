namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Configuración del cliente HTTP que consume Maquinaria_V4.
/// Se enlaza desde la sección <c>Maquinaria</c> de <c>appsettings.json</c>.
/// </summary>
public sealed class MaquinariaErpOptions
{
    public const string SectionName = "Maquinaria";

    /// <summary>
    /// Base URL del servicio Maquinaria_V4, incluyendo <c>/api/v4/Maquinaria</c>.
    /// Ejemplo: <c>http://localhost:5289/api/v4/Maquinaria</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Token de servicio (service-account) usado como <b>fallback</b> por
    /// <see cref="Auth.ServiceAccountBearerTokenAccessor"/> cuando no hay
    /// HttpContext (caller fuera de scope HTTP) ni envelope Wolverine con
    /// <c>X-Forwarded-Authorization</c> (mensaje legacy o listener-to-listener).
    ///
    /// Casos de uso legales: bootstrap/seed manual, retries con JWT del envelope
    /// expirado, mensajes publicados antes de mt-3. Si está vacío Y no hay
    /// HTTP/envelope, el <see cref="BearerTokenPropagationHandler"/> falla
    /// fail-closed con <see cref="Auth.BearerTokenAusenteException"/> (MT3-INV-3).
    ///
    /// FU-14 (ADR-002) cerrado en mt-1; FU-44 (propagación del JWT entrante)
    /// cerrado en mt-3. El token global cambió de rol — ya no es el único, ahora
    /// es el último recurso de la chain HTTP → Ambient → ServiceAccount (D-MT3-2 / D-MT3-3).
    /// </summary>
    public string JwtToken { get; set; } = string.Empty;

    /// <summary>
    /// Timeout HTTP por request al adapter. 30s por defecto (alineado con ADR-006
    /// — el outbox de Wolverine reintenta a partir de ese punto).
    /// </summary>
    public int TimeoutSegundos { get; set; } = 30;
}
