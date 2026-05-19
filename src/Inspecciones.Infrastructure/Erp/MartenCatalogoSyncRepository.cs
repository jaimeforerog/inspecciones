using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Auth;
using Marten;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Adapter de producción de <see cref="ICatalogoSyncRepository"/> sobre Marten.
///
/// <para>
/// Recibe <see cref="ITenantedDocumentSessionFactory"/> (scoped) — refactor mt-2.
/// Cada operación que requiere commit abre una sesión propia via el factory; cada
/// sesión queda discriminada por <c>tenant_id</c> derivado de
/// <see cref="ISessionService.IdEmpresa"/> (Marten Conjoined — ADR-009).
/// Sesiones separadas por catálogo evitan la race condition de
/// <see cref="IDocumentSession"/> compartida en <c>Task.WhenAll</c>
/// (hallazgo #1 review erp-4); el aislamiento cross-tenant es garantía adicional de mt-2.
/// </para>
///
/// <para>
/// Modelo transaccional: cada <c>PersistirSync*Async</c> abre una sesión tenanted,
/// acumula el wipe-and-replace (si aplica) y el upsert del state, y cierra con un
/// único <c>SaveChangesAsync</c>. Atomicidad wipe+replace+state garantizada dentro
/// de cada catálogo y tenant; aislamiento entre catálogos garantizado por sesiones
/// independientes; aislamiento cross-tenant garantizado por Marten Conjoined.
/// </para>
///
/// MT2-INV-1: Prohibido <c>store.LightweightSession()</c> directo — usar siempre el factory.
/// MT2-INV-3: D5 — todos los catálogos son por-empresa, sin excepciones single-tenant.
/// </summary>
public sealed class MartenCatalogoSyncRepository : ICatalogoSyncRepository
{
    private readonly ITenantedDocumentSessionFactory _sessions;

    public MartenCatalogoSyncRepository(ITenantedDocumentSessionFactory sessions)
    {
        _sessions = sessions;
    }

    // ── Lectura de estado de sync ───────────────────────────────────────────

    public async Task<CatalogoSyncState?> LeerSyncStateAsync(string catalogoId, CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        return await session.LoadAsync<CatalogoSyncState>(catalogoId, ct).ConfigureAwait(false);
    }

    // ── Contadores ─────────────────────────────────────────────────────────

    public async Task<int> ContarCausasFallaAsync(CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        return await session.Query<CausaFallaCatalogo>().CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ContarTiposFallaAsync(CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        return await session.Query<TipoFallaCatalogo>().CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ContarProductosAsync(CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        return await session.Query<RepuestoLocal>().CountAsync(ct).ConfigureAwait(false);
    }

    // ── Operaciones atómicas por catálogo ───────────────────────────────────

    public async Task PersistirSyncCausasFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<CausaFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        if (wipeAndReplace is not null)
        {
            session.DeleteWhere<CausaFallaCatalogo>(_ => true);
            session.Store(wipeAndReplace.ToArray());
        }
        session.Store(state);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task PersistirSyncTiposFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<TipoFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        if (wipeAndReplace is not null)
        {
            session.DeleteWhere<TipoFallaCatalogo>(_ => true);
            session.Store(wipeAndReplace.ToArray());
        }
        session.Store(state);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task PersistirSyncProductosAsync(
        CatalogoSyncState state,
        IReadOnlyList<RepuestoLocal>? wipeAndReplace,
        CancellationToken ct = default)
    {
        await using var session = _sessions.OpenSession();
        if (wipeAndReplace is not null)
        {
            session.DeleteWhere<RepuestoLocal>(_ => true);
            session.Store(wipeAndReplace.ToArray());
        }
        session.Store(state);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
