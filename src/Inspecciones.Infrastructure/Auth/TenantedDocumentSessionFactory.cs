using System.Globalization;
using Marten;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Implementación de producción de <see cref="ITenantedDocumentSessionFactory"/>
/// que delega a <see cref="IDocumentStore"/> propagando el <c>tenant_id</c> del
/// <see cref="ISessionService.IdEmpresa"/> en cada sesión.
///
/// <para>
/// El <c>tenantId</c> serializa el <c>int IdEmpresa</c> a <c>string</c> con
/// <see cref="CultureInfo.InvariantCulture"/> (ver D-MT2-4 del spec mt-2). Cada
/// sesión Marten queda discriminada con <c>WHERE tenant_id = '7'</c> implícito
/// en lecturas y <c>INSERT INTO ... (tenant_id, ...) VALUES ('7', ...)</c> en
/// escrituras.
/// </para>
/// </summary>
public sealed class TenantedDocumentSessionFactory : ITenantedDocumentSessionFactory
{
    private readonly IDocumentStore _store;
    private readonly ISessionService _session;

    public TenantedDocumentSessionFactory(IDocumentStore store, ISessionService session)
    {
        _store = store;
        _session = session;
    }

    public IDocumentSession OpenSession()
    {
        var tenantId = _session.IdEmpresa.ToString(CultureInfo.InvariantCulture);
        return _store.LightweightSession(tenantId);
    }

    public IQuerySession OpenQuerySession()
    {
        var tenantId = _session.IdEmpresa.ToString(CultureInfo.InvariantCulture);
        return _store.QuerySession(tenantId);
    }

    public IDocumentSession OpenSessionForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        return _store.LightweightSession(tenantId);
    }
}
