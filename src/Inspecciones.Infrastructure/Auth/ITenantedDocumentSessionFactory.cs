using Marten;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Factory para abrir sesiones Marten ya discriminadas por <c>tenant_id</c>
/// (Marten Conjoined — ADR-009). Reemplaza el acceso directo a
/// <see cref="IDocumentStore"/> en código de producción.
///
/// MT2-INV-1 (regla CLAUDE.md mt-2):
/// <list type="bullet">
///   <item>Toda apertura de sesión Marten en código de producción pasa por este puerto.</item>
///   <item>Prohibido <c>store.LightweightSession()</c> directo sin tenant en <c>src/</c>.</item>
///   <item>Aplicaciones legales del bypass <see cref="OpenSessionForTenant"/>: listeners
///   Wolverine que ya leyeron el tenant del envelope; tests E2E cross-tenant; bootstrap/admin
///   sin contexto HTTP.</item>
/// </list>
///
/// Spec slice mt-2 §2 + §5 D-MT2-1.
/// </summary>
public interface ITenantedDocumentSessionFactory
{
    /// <summary>
    /// Abre una <see cref="IDocumentSession"/> lightweight con el tenant resuelto del
    /// <see cref="ISessionService"/> actual (HTTP scope). Lanza
    /// <see cref="ClaimRequeridaException"/> si <c>IdEmpresa</c> no está disponible.
    /// </summary>
    IDocumentSession OpenSession();

    /// <summary>
    /// Abre una <see cref="IQuerySession"/> de solo lectura con el tenant del
    /// <see cref="ISessionService"/> actual.
    /// </summary>
    IQuerySession OpenQuerySession();

    /// <summary>
    /// Abre una sesión Marten con un <paramref name="tenantId"/> arbitrario.
    /// Bypass legal para: listeners Wolverine (que leen el tenant del
    /// <c>Envelope.TenantId</c>), tests E2E cross-tenant, y operaciones de bootstrap.
    /// </summary>
    IDocumentSession OpenSessionForTenant(string tenantId);
}
