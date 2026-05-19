namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Puerto de acceso al raw Bearer token a propagar al ERP Maquinaria_V4.
/// Distinto a <see cref="ISessionService"/> (claims parseadas del JWT): este puerto
/// expone el token raw, sin parsing.
///
/// Cadena de resolución en producción (<see cref="ChainedBearerTokenAccessor"/>):
/// HTTP → Ambient → ServiceAccount. El primero no-vacío gana.
///
/// El consumer es <see cref="Erp.BearerTokenPropagationHandler"/> (DelegatingHandler
/// del <see cref="Erp.MaquinariaErpClient"/>). Si todos los accessors retornan null,
/// el handler lanza <see cref="BearerTokenAusenteException"/> (MT3-INV-3 fail-closed).
///
/// Spec slice mt-3 §2 + D-MT3-1.
/// </summary>
public interface IBearerTokenAccessor
{
    /// <summary>
    /// Devuelve el raw Bearer token (sin el prefijo "Bearer "), o <c>null</c> si
    /// no hay ningún token disponible. String vacío equivale a <c>null</c>.
    /// </summary>
    string? ObtenerBearerToken();
}
