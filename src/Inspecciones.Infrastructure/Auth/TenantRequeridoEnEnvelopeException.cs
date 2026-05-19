namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Se lanza desde un listener Wolverine cuando el envelope del mensaje entrante
/// no incluye <c>TenantId</c> (o es vacío). MT2-PRE-2 del spec mt-2 §4:
/// la integración Marten Conjoined + Wolverine outbox debe propagar siempre el
/// <c>tenant_id</c> de la transacción de origen. Si falta, indica un bug en el
/// publisher o un mensaje legacy publicado antes de mt-2 — caso patológico.
///
/// Se mapea a dead-letter inmediato por la política ADR-006 §16 (errores
/// permanentes no se reintentan). Hereda de <see cref="InvalidOperationException"/>
/// para distinguirse claramente de <c>MaquinariaErpException</c> (5xx retryable)
/// y de <see cref="ArgumentException"/> (eventos malformados).
/// </summary>
public sealed class TenantRequeridoEnEnvelopeException : InvalidOperationException
{
    /// <summary>Nombre del listener que detectó la ausencia (para diagnóstico/logs).</summary>
    public string NombreListener { get; }

    /// <summary>Id del envelope Wolverine, útil para correlacionar con el outbox.</summary>
    public Guid MessageId { get; }

    /// <summary>
    /// Código de error estandarizado (para logs estructurados + dashboards).
    /// Propiedad de instancia (no const) para consistencia con
    /// <see cref="ClaimRequeridaException.CodigoError"/> — paridad del patrón.
    /// </summary>
    public string CodigoError { get; } = "TENANT-ENVELOPE-AUSENTE";

    public TenantRequeridoEnEnvelopeException(string nombreListener, Guid messageId)
        : base($"Listener '{nombreListener}' recibió envelope sin TenantId (mensaje {messageId}). " +
               "Dead-letter inmediato (MT2-PRE-2). Verificar wiring Marten Conjoined + Wolverine outbox.")
    {
        NombreListener = nombreListener;
        MessageId = messageId;
    }
}
