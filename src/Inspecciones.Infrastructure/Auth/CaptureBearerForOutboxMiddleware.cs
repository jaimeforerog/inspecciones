using Microsoft.AspNetCore.Http;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// ASP.NET middleware que captura el header <c>Authorization: Bearer ...</c>
/// del request HTTP entrante y lo escribe en <see cref="IncomingBearerCarrier"/>
/// para el scope de la request. <see cref="ForwardAuthEnvelopeRule"/> lo lee al
/// hacer outbox publish y lo persiste como <c>X-Forwarded-Authorization</c> en
/// el envelope.
///
/// Comportamiento:
/// - Header ausente o vacío → no captura (carrier permanece en null).
/// - Esquema distinto a "Bearer " (case-insensitive) → no captura.
/// - Bearer válido → setea el header tal cual (con prefijo "Bearer ") en el
///   carrier durante todo el scope del <c>next</c>; restaura al disponer.
///
/// MT4-INV-2 / FU-60. Spec slice mt-4 §2 + D-MT4-1.
/// </summary>
public sealed class CaptureBearerForOutboxMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private readonly RequestDelegate _next;

    public CaptureBearerForOutboxMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext ctx)
    {
        var auth = ctx.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(auth) ||
            !auth.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Sin Bearer válido → no capturamos. El carrier permanece en su valor
            // anterior (típicamente null en HTTP scope fresco). Si fuera test que
            // contamina entre tests vía AsyncLocal estático: aceptable porque cada
            // test corre en su propio task / contexto async (xUnit aísla).
            await _next(ctx).ConfigureAwait(false);
            return;
        }

        using var _ = IncomingBearerCarrier.SetForCurrentScope(auth);
        await _next(ctx).ConfigureAwait(false);
    }
}
