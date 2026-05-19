using Inspecciones.Domain.Catalogos;
using Marten;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Adapter de producción de <see cref="ICatalogoSyncRepository"/> sobre Marten.
///
/// <para>
/// Recibe <see cref="IDocumentStore"/> (singleton) en lugar de <see cref="IDocumentSession"/>
/// (scoped). Cada operación que requiere commit abre una <c>LightweightSession</c> propia,
/// garantizando que los tres catálogos que corren concurrentemente en el
/// <c>Task.WhenAll</c> del handler no compartan sesión. <see cref="IDocumentSession"/> de
/// Marten no es thread-safe — una sesión compartida entre tareas paralelas produce race
/// conditions y puede commitear wipes sin su replace correspondiente (bug erp-4 hallazgo #1).
/// </para>
///
/// <para>
/// Modelo transaccional: cada <c>PersistirSync*Async</c> abre una sesión, acumula el
/// wipe-and-replace (si aplica) y el upsert del state, y cierra con un único
/// <c>SaveChangesAsync</c>. Atomicidad wipe+replace+state garantizada dentro de cada
/// catálogo; aislamiento entre catálogos garantizado por sesiones independientes.
/// </para>
/// </summary>
public sealed class MartenCatalogoSyncRepository : ICatalogoSyncRepository
{
    private readonly IDocumentStore _store;

    public MartenCatalogoSyncRepository(IDocumentStore store)
    {
        _store = store;
    }

    // ── Lectura de estado de sync ───────────────────────────────────────────

    public async Task<CatalogoSyncState?> LeerSyncStateAsync(string catalogoId, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        return await session.LoadAsync<CatalogoSyncState>(catalogoId, ct).ConfigureAwait(false);
    }

    // ── Contadores ─────────────────────────────────────────────────────────

    public async Task<int> ContarCausasFallaAsync(CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        return await session.Query<CausaFallaCatalogo>().CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ContarTiposFallaAsync(CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        return await session.Query<TipoFallaCatalogo>().CountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ContarProductosAsync(CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        return await session.Query<RepuestoLocal>().CountAsync(ct).ConfigureAwait(false);
    }

    // ── Operaciones atómicas por catálogo ───────────────────────────────────

    public async Task PersistirSyncCausasFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<CausaFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
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
        await using var session = _store.LightweightSession();
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
        await using var session = _store.LightweightSession();
        if (wipeAndReplace is not null)
        {
            session.DeleteWhere<RepuestoLocal>(_ => true);
            session.Store(wipeAndReplace.ToArray());
        }
        session.Store(state);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
