namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Se lanza desde <see cref="Erp.BearerTokenPropagationHandler"/> cuando ningún
/// accessor de la cadena (<see cref="ChainedBearerTokenAccessor"/>: HTTP → Ambient
/// → ServiceAccount) provee un Bearer token para una llamada al ERP.
///
/// MT3-INV-3 (fail-closed): nunca request anónimo al ERP. Es un error de
/// configuración (service-account vacío + sin HttpContext + sin envelope).
///
/// Hereda de <see cref="InvalidOperationException"/> para consistencia con
/// <c>TenantRequeridoEnEnvelopeException</c>. En contexto de endpoint HTTP se
/// mapea a 500 por el handler global de excepciones. En contexto de listener
/// Wolverine, el outbox lo trata como excepción permanente (no en la lista
/// de policies de reintento) → dead-letter eventual.
/// </summary>
public sealed class BearerTokenAusenteException : InvalidOperationException
{
    /// <summary>Código de error estandarizado para logs estructurados.</summary>
    public string CodigoError { get; } = "BEARER-TOKEN-AUSENTE";

    public BearerTokenAusenteException(string message) : base(message) { }
}
