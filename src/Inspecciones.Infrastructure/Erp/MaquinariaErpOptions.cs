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
    /// JWT que el host SincoMyE emite. Maquinaria_V4 valida firma e issuer
    /// vía <c>MiddlewareAuthorizationToken</c>. Vacío == no enviar header
    /// <c>Authorization</c> (las requests fallarán con 401, pero el módulo
    /// arranca igual y el endpoint de seed manual sigue siendo usable).
    /// </summary>
    /// <remarks>
    /// FU-14 sigue abierto: cuando ADR-002 (mecanismo de identidad del host PWA)
    /// se resuelva, este token deberá venir del <c>HttpContext</c> de la request
    /// entrante, no de configuración global. Por ahora se usa un token fijo
    /// (apto para QA y arranque local).
    /// </remarks>
    public string JwtToken { get; set; } = string.Empty;

    /// <summary>
    /// Timeout HTTP por request al adapter. 30s por defecto (alineado con ADR-006
    /// — el outbox de Wolverine reintenta a partir de ese punto).
    /// </summary>
    public int TimeoutSegundos { get; set; } = 30;
}
