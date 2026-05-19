using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Erp;

namespace Inspecciones.Infrastructure.Tests.Erp;

/// <summary>
/// Fake en-memoria de <see cref="ICatalogoSyncRepository"/> para tests del
/// <see cref="SincronizarCatalogosHandler"/>. No mockea dominio — sólo persiste
/// documentos Marten en diccionarios/listas locales.
///
/// <para>
/// Cada <c>PersistirSync*Async</c> es atómico por construcción (in-memory, single-threaded).
/// La semántica de <paramref name="wipeAndReplace"/> == null (no tocar los docs) vs
/// != null (wipe-and-replace) replica el contrato del puerto sin necesitar Postgres.
/// </para>
/// </summary>
internal sealed class FakeCatalogoSyncRepository : ICatalogoSyncRepository
{
    private readonly Dictionary<string, CatalogoSyncState> _states = new();
    private readonly List<CausaFallaCatalogo> _causasFalla = new();
    private readonly List<TipoFallaCatalogo> _tiposFalla = new();
    private readonly List<RepuestoLocal> _productos = new();

    // ── Lectura de estado de sync ───────────────────────────────────────────

    public Task<CatalogoSyncState?> LeerSyncStateAsync(string catalogoId, CancellationToken ct = default)
    {
        _states.TryGetValue(catalogoId, out var state);
        return Task.FromResult(state);
    }

    // ── Contadores ─────────────────────────────────────────────────────────

    public Task<int> ContarCausasFallaAsync(CancellationToken ct = default) =>
        Task.FromResult(_causasFalla.Count);

    public Task<int> ContarTiposFallaAsync(CancellationToken ct = default) =>
        Task.FromResult(_tiposFalla.Count);

    public Task<int> ContarProductosAsync(CancellationToken ct = default) =>
        Task.FromResult(_productos.Count);

    // ── Operaciones atómicas por catálogo ───────────────────────────────────

    public Task PersistirSyncCausasFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<CausaFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default)
    {
        if (wipeAndReplace is not null)
        {
            _causasFalla.Clear();
            _causasFalla.AddRange(wipeAndReplace);
        }
        _states[state.Id] = state;
        return Task.CompletedTask;
    }

    public Task PersistirSyncTiposFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<TipoFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default)
    {
        if (wipeAndReplace is not null)
        {
            _tiposFalla.Clear();
            _tiposFalla.AddRange(wipeAndReplace);
        }
        _states[state.Id] = state;
        return Task.CompletedTask;
    }

    public Task PersistirSyncProductosAsync(
        CatalogoSyncState state,
        IReadOnlyList<RepuestoLocal>? wipeAndReplace,
        CancellationToken ct = default)
    {
        if (wipeAndReplace is not null)
        {
            _productos.Clear();
            _productos.AddRange(wipeAndReplace);
        }
        _states[state.Id] = state;
        return Task.CompletedTask;
    }

    // ── Helpers de inspección (usados sólo desde tests) ────────────────────

    public IReadOnlyList<CausaFallaCatalogo> CausasFalla => _causasFalla;
    public IReadOnlyList<TipoFallaCatalogo> TiposFalla => _tiposFalla;
    public IReadOnlyList<RepuestoLocal> Productos => _productos;

    public CatalogoSyncState? ObtenerState(string catalogoId) =>
        _states.GetValueOrDefault(catalogoId);

    public void SeedState(CatalogoSyncState state) => _states[state.Id] = state;

    public void SeedCausasFalla(IEnumerable<CausaFallaCatalogo> causas)
    {
        _causasFalla.Clear();
        _causasFalla.AddRange(causas);
    }

    public void SeedTiposFalla(IEnumerable<TipoFallaCatalogo> tipos)
    {
        _tiposFalla.Clear();
        _tiposFalla.AddRange(tipos);
    }

    public void SeedProductos(IEnumerable<RepuestoLocal> productos)
    {
        _productos.Clear();
        _productos.AddRange(productos);
    }
}
