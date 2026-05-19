using Inspecciones.Domain.Catalogos;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Puerto de lectura/escritura de documentos de catálogo y estado de sync.
/// Abstrae el store de persistencia para que <see cref="SincronizarCatalogosHandler"/>
/// sea testeable sin Marten real (igual que <see cref="IInspeccionReader"/> en erp-3).
///
/// <para>
/// Cada <c>PersistirSync*Async</c> es una operación atómica que:
/// (a) hace wipe-and-replace de los documentos del catálogo si <paramref name="items"/> no es null, y
/// (b) persiste el <see cref="CatalogoSyncState"/> final.
/// Ambas acciones ocurren en una única transacción por catálogo, aislada de los otros
/// catálogos que corran concurrentemente en el mismo <c>Task.WhenAll</c>.
/// </para>
///
/// <para>
/// Implementación de producción: <c>MartenCatalogoSyncRepository</c>.
/// Implementación de test: <c>FakeCatalogoSyncRepository</c>.
/// </para>
/// </summary>
public interface ICatalogoSyncRepository
{
    // ── Lectura de estado de sync ───────────────────────────────────────────

    Task<CatalogoSyncState?> LeerSyncStateAsync(string catalogoId, CancellationToken ct = default);

    // ── Contadores (usados por tests de integración) ────────────────────────

    Task<int> ContarCausasFallaAsync(CancellationToken ct = default);
    Task<int> ContarTiposFallaAsync(CancellationToken ct = default);
    Task<int> ContarProductosAsync(CancellationToken ct = default);

    // ── Operaciones atómicas por catálogo ───────────────────────────────────

    /// <summary>
    /// Persiste atomicamente el estado de sync de causas-falla.
    /// Si <paramref name="wipeAndReplace"/> no es null, hace wipe-and-replace antes de
    /// guardar el state, todo en la misma transacción.
    /// </summary>
    Task PersistirSyncCausasFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<CausaFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default);

    /// <summary>
    /// Persiste atomicamente el estado de sync de tipos-falla.
    /// Si <paramref name="wipeAndReplace"/> no es null, hace wipe-and-replace antes de
    /// guardar el state, todo en la misma transacción.
    /// </summary>
    Task PersistirSyncTiposFallaAsync(
        CatalogoSyncState state,
        IReadOnlyList<TipoFallaCatalogo>? wipeAndReplace,
        CancellationToken ct = default);

    /// <summary>
    /// Persiste atomicamente el estado de sync de productos.
    /// Si <paramref name="wipeAndReplace"/> no es null, hace wipe-and-replace antes de
    /// guardar el state, todo en la misma transacción.
    /// </summary>
    Task PersistirSyncProductosAsync(
        CatalogoSyncState state,
        IReadOnlyList<RepuestoLocal>? wipeAndReplace,
        CancellationToken ct = default);
}
