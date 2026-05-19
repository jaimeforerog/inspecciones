using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Erp.Dtos;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Handler de aplicación que sincroniza los catálogos globales (causas-falla,
/// tipos-falla, productos) desde Maquinaria_V4 usando ETag/If-None-Match.
///
/// <para>
/// Estrategia: wipe-and-replace atómico cuando Maquinaria responde 200 con body
/// no vacío; no-change si 304; error si excepción; vaciado-sospechoso si body vacío
/// (D3, D4, D5 del spec erp-4).
/// </para>
/// </summary>
public sealed class SincronizarCatalogosHandler
{
    private readonly IMaquinariaErpClient _erp;
    private readonly ICatalogoSyncRepository _repo;
    private readonly TimeProvider _time;

    public SincronizarCatalogosHandler(
        IMaquinariaErpClient erp,
        ICatalogoSyncRepository repo,
        TimeProvider time)
    {
        _erp = erp;
        _repo = repo;
        _time = time;
    }

    /// <summary>
    /// Ejecuta el sync de los 3 catálogos en paralelo y devuelve el resultado
    /// por catálogo. Siempre completa sin lanzar aunque un catálogo falle (D5).
    /// Cada catálogo usa su propia sesión de persistencia — thread-safe.
    /// </summary>
    public async Task<SincronizarCatalogosResult> EjecutarAsync(CancellationToken ct = default)
    {
        var tareas = new[]
        {
            SincronizarCatalogoAsync(
                nombre: "causas-falla",
                fetchErp: (etag, token) => _erp.ListarCausasFallaAsync("-1", etag, token),
                obtenerItems: body => body.Causas,
                mapearItem: c => new CausaFallaCatalogo(c.Codigo, c.Descripcion),
                persistir: (state, items, token) => _repo.PersistirSyncCausasFallaAsync(state, items, token),
                ct),

            SincronizarCatalogoAsync(
                nombre: "tipos-falla",
                fetchErp: (etag, token) => _erp.ListarTiposFallaAsync("-1", etag, token),
                obtenerItems: body => body.TiposFalla,
                mapearItem: t => new TipoFallaCatalogo(t.Codigo, t.Descripcion, t.Prioridad),
                persistir: (state, items, token) => _repo.PersistirSyncTiposFallaAsync(state, items, token),
                ct),

            SincronizarCatalogoAsync(
                nombre: "productos",
                fetchErp: (etag, token) => _erp.ListarProductosAsync("-1", etag, token),
                obtenerItems: body => body.Productos,
                // CodigoSinco no existe en el DTO de productos — se usa Codigo.ToString() como aproximación MVP.
                // ParteIdsCompatibles tampoco está disponible en este endpoint global.
                mapearItem: p => new RepuestoLocal(p.Codigo, p.Codigo.ToString(), p.Descripcion, p.UnidadContable, Array.Empty<int>()),
                persistir: (state, items, token) => _repo.PersistirSyncProductosAsync(state, items, token),
                ct),
        };

        var resultados = await Task.WhenAll(tareas).ConfigureAwait(false);

        return new SincronizarCatalogosResult(
            resultados,
            _time.GetUtcNow());
    }

    // ── Helper genérico del flujo ETag ──────────────────────────────────────

    private async Task<ResultadoCatalogo> SincronizarCatalogoAsync<TDto, TItem, TLocal>(
        string nombre,
        Func<string?, CancellationToken, Task<EtagResult<TDto>>> fetchErp,
        Func<TDto, IReadOnlyList<TItem>> obtenerItems,
        Func<TItem, TLocal> mapearItem,
        Func<CatalogoSyncState, IReadOnlyList<TLocal>?, CancellationToken, Task> persistir,
        CancellationToken ct)
        where TDto : class
        where TLocal : class
    {
        try
        {
            var state = await _repo.LeerSyncStateAsync(nombre, ct).ConfigureAwait(false);
            var resultado = await fetchErp(state?.EtagActual, ct).ConfigureAwait(false);

            if (resultado.NotModified)
            {
                await persistir(BuildStateNoChange(state, nombre), null, ct).ConfigureAwait(false);
                return new ResultadoCatalogo(nombre, "no-change", null, null);
            }

            var items = obtenerItems(resultado.Body!);
            if (items.Count == 0)
            {
                // D4: body vacío es anomalía — no borrar el cache local ni actualizar el ETag.
                await persistir(BuildStateVaciadoSospechoso(state, nombre), null, ct).ConfigureAwait(false);
                return new ResultadoCatalogo(nombre, "vaciado-sospechoso", null,
                    "Maquinaria devolvió catálogo vacío — cache local preservado");
            }

            var locales = items.Select(mapearItem).ToList();
            var ahora = _time.GetUtcNow();
            var stateActualizado = new CatalogoSyncState
            {
                Id = nombre,
                EtagActual = resultado.ETag,
                UltimoEstado = "actualizado",
                UltimaSyncExitosa = ahora,
                UltimaSyncIntento = ahora,
            };
            await persistir(stateActualizado, locales, ct).ConfigureAwait(false);
            return new ResultadoCatalogo(nombre, "actualizado", ahora, null);
        }
        catch (Exception ex)
        {
            return await GuardarErrorYRetornarAsync(nombre, ex, ct).ConfigureAwait(false);
        }
    }

    // ── Builders de CatalogoSyncState ───────────────────────────────────────

    private CatalogoSyncState BuildStateNoChange(CatalogoSyncState? previo, string nombre)
    {
        var ahora = _time.GetUtcNow();
        return new CatalogoSyncState
        {
            Id = nombre,
            EtagActual = previo?.EtagActual,
            UltimoEstado = "no-change",
            UltimaSyncExitosa = ahora,
            UltimaSyncIntento = ahora,
        };
    }

    private CatalogoSyncState BuildStateVaciadoSospechoso(CatalogoSyncState? previo, string nombre)
    {
        var ahora = _time.GetUtcNow();
        return new CatalogoSyncState
        {
            Id = nombre,
            EtagActual = previo?.EtagActual, // no se actualiza el etag (D4)
            UltimoEstado = "vaciado-sospechoso",
            UltimaSyncExitosa = previo?.UltimaSyncExitosa,
            UltimaSyncIntento = ahora,
        };
    }

    private async Task<ResultadoCatalogo> GuardarErrorYRetornarAsync(string nombre, Exception ex, CancellationToken ct)
    {
        var ahora = _time.GetUtcNow();
        // Segunda lectura del state para preservar EtagActual y UltimaSyncExitosa previos en el registro de error.
        var previo = await _repo.LeerSyncStateAsync(nombre, ct).ConfigureAwait(false);
        var errorState = new CatalogoSyncState
        {
            Id = nombre,
            EtagActual = previo?.EtagActual,
            UltimoEstado = "error",
            UltimaSyncExitosa = previo?.UltimaSyncExitosa,
            UltimaSyncIntento = ahora,
            UltimoErrorMensaje = ex.Message,
        };
        await PersistirErrorStateAsync(nombre, errorState, ct).ConfigureAwait(false);
        return new ResultadoCatalogo(nombre, "error", null, ex.Message);
    }

    // Despacha la persistencia del error-state al método correcto del repo según el nombre del catálogo.
    // El wipe-and-replace es null porque en el camino de error no tocamos los documentos del catálogo.
    private Task PersistirErrorStateAsync(string nombre, CatalogoSyncState errorState, CancellationToken ct) =>
        nombre switch
        {
            "causas-falla" => _repo.PersistirSyncCausasFallaAsync(errorState, null, ct),
            "tipos-falla"  => _repo.PersistirSyncTiposFallaAsync(errorState, null, ct),
            "productos"    => _repo.PersistirSyncProductosAsync(errorState, null, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(nombre), nombre, "Catálogo desconocido"),
        };
}

/// <summary>Resultado global del sync-all.</summary>
public sealed record SincronizarCatalogosResult(
    IReadOnlyList<ResultadoCatalogo> Catalogos,
    DateTimeOffset SincronizadoEn);

/// <summary>Resultado de sincronizar un catálogo individual.</summary>
public sealed record ResultadoCatalogo(
    string Nombre,
    string Status,
    DateTimeOffset? ActualizadosEn,
    string? Error);
