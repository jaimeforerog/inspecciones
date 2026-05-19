using Wolverine;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// <see cref="IEnvelopeRule"/> global que escribe el header
/// <c>X-Forwarded-Authorization</c> en cada envelope saliente del outbox, leyendo
/// el bearer capturado por <see cref="CaptureBearerForOutboxMiddleware"/> desde
/// <see cref="IncomingBearerCarrier"/>.
///
/// Comportamiento:
/// - Carrier vacío → no añade el header (publish desde listener-to-listener,
///   seed manual, cron — fallback service-account es esperado).
/// - Carrier seteado pero envelope ya trae <c>X-Forwarded-Authorization</c> →
///   no sobrescribe (publisher explícito tiene precedencia).
/// - Carrier seteado y envelope sin el header → setea con el valor del carrier.
///
/// Registrado en <c>Program.cs</c> vía <c>opts.Policies.AllSenders(cfg =>
/// cfg.AddOutgoingRule(new ForwardAuthEnvelopeRule()))</c>.
///
/// MT4-INV-2 / FU-60. Spec slice mt-4 §2 + D-MT4-1.
/// </summary>
public sealed class ForwardAuthEnvelopeRule : IEnvelopeRule
{
    private const string HeaderName = "X-Forwarded-Authorization";

    public void Modify(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var auth = IncomingBearerCarrier.GetForwardedAuth();
        if (string.IsNullOrWhiteSpace(auth))
        {
            return;
        }

        // Envelope.Headers en Wolverine 3 es no-null por construcción (Dictionary).
        // Si emerge una versión donde puede ser null, la asignación defensiva queda
        // documentada pero hoy es no-op.
        if (envelope.Headers.ContainsKey(HeaderName))
        {
            // D-MT4-1: respetar header explícito del publisher.
            return;
        }

        envelope.Headers[HeaderName] = auth;
    }
}
