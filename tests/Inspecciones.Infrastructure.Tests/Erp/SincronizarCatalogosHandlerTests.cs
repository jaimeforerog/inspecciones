using System.Net;
using System.Net.Http.Headers;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Dtos;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Inspecciones.Infrastructure.Tests.Erp;

/// <summary>
/// Tests Given/When/Then del handler <see cref="SincronizarCatalogosHandler"/>.
/// Cada test cubre un escenario de §6 del spec erp-4.
///
/// Stack:
///   - WireMock.Net para emular Maquinaria_V4 (GETs de causas-falla, tipos-falla, productos).
///   - <see cref="FakeCatalogoSyncRepository"/> (Opción B) en lugar de Marten real, ya que
///     el proyecto Infrastructure.Tests no tiene Testcontainers/Postgres. El repositorio
///     fake implementa el puerto <see cref="ICatalogoSyncRepository"/> con diccionarios
///     en-memoria — suficiente para verificar el comportamiento observable del handler
///     sin depender de infraestructura. Documentado en red-notes §1.
///   - TimeProvider.Fixed para controlar timestamps.
/// </summary>
public sealed class SincronizarCatalogosHandlerTests : IDisposable
{
    private readonly WireMockServer _wiremock;
    private readonly HttpClient _httpClient;
    private readonly MaquinariaErpClient _erpClient;
    private readonly FakeCatalogoSyncRepository _repo;
    private readonly TimeProvider _time;

    private const string PathCausasFalla = "/api/causas-falla";
    private const string PathTiposFalla = "/api/tipos-falla";
    private const string PathProductos = "/api/productos";

    private static readonly DateTimeOffset AhoraFijo =
        new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);

    public SincronizarCatalogosHandlerTests()
    {
        _wiremock = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_wiremock.Urls[0] + "/"),
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-jwt");
        _erpClient = new MaquinariaErpClient(_httpClient);
        _repo = new FakeCatalogoSyncRepository();
        _time = TimeProvider.System; // reemplazado por Fixed cuando importa el timestamp exacto
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _wiremock.Stop();
        _wiremock.Dispose();
    }

    private SincronizarCatalogosHandler CrearHandler(TimeProvider? time = null) =>
        new(_erpClient, _repo, time ?? _time);

    // ── helpers de fixture WireMock ─────────────────────────────────────────

    private void ConfigurarCausasFalla200(int cantidad, string etag, string? etiquetaPrev = null)
    {
        var causas = Enumerable.Range(1, cantidad)
            .Select(i => new { Codigo = i, Descripcion = $"Causa {i}" })
            .ToArray();

        var builder = Request.Create().WithPath(PathCausasFalla).WithParam("texto", "-1").UsingGet();
        if (etiquetaPrev is not null)
        {
            builder = builder.WithHeader("If-None-Match", etiquetaPrev);
        }

        _wiremock
            .Given(builder)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("ETag", etag)
                .WithBodyAsJson(new { Causas = causas, Total = cantidad }));
    }

    private void ConfigurarCausasFalla304(string etag)
    {
        _wiremock
            .Given(Request.Create().WithPath(PathCausasFalla).WithParam("texto", "-1").UsingGet()
                .WithHeader("If-None-Match", etag))
            .RespondWith(Response.Create()
                .WithStatusCode(304)
                .WithHeader("ETag", etag));
    }

    private void ConfigurarTiposFalla304(string etag)
    {
        _wiremock
            .Given(Request.Create().WithPath(PathTiposFalla).WithParam("texto", "-1").UsingGet()
                .WithHeader("If-None-Match", etag))
            .RespondWith(Response.Create()
                .WithStatusCode(304)
                .WithHeader("ETag", etag));
    }

    private void ConfigurarTiposFalla200(int cantidad, string etag, string? etiquetaPrev = null)
    {
        var tipos = Enumerable.Range(1, cantidad)
            .Select(i => new { Codigo = i, Descripcion = $"Tipo {i}", Prioridad = "ALTA" })
            .ToArray();

        var builder = Request.Create().WithPath(PathTiposFalla).WithParam("texto", "-1").UsingGet();
        if (etiquetaPrev is not null)
        {
            builder = builder.WithHeader("If-None-Match", etiquetaPrev);
        }

        _wiremock
            .Given(builder)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("ETag", etag)
                .WithBodyAsJson(new { TiposFalla = tipos, Total = cantidad }));
    }

    private void ConfigurarProductos304(string etag)
    {
        _wiremock
            .Given(Request.Create().WithPath(PathProductos).WithParam("texto", "-1").UsingGet()
                .WithHeader("If-None-Match", etag))
            .RespondWith(Response.Create()
                .WithStatusCode(304)
                .WithHeader("ETag", etag));
    }

    private void ConfigurarProductos200(int cantidad, string etag)
    {
        var productos = Enumerable.Range(1, cantidad)
            .Select(i => new { Codigo = i, Descripcion = $"Producto {i}", UnidadContable = "UND" })
            .ToArray();

        _wiremock
            .Given(Request.Create().WithPath(PathProductos).WithParam("texto", "-1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("ETag", etag)
                .WithBodyAsJson(new { Productos = productos, Total = cantidad }));
    }

    private void ConfigurarCausasFallaVacias(string etag)
    {
        _wiremock
            .Given(Request.Create().WithPath(PathCausasFalla).WithParam("texto", "-1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("ETag", etag)
                .WithBodyAsJson(new { Causas = Array.Empty<object>(), Total = 0 }));
    }

    private void ConfigurarCausasFalla5xx()
    {
        _wiremock
            .Given(Request.Create().WithPath(PathCausasFalla).WithParam("texto", "-1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBodyAsJson(new { Codigo = "INTERNAL_ERROR", Mensaje = "Error interno Maquinaria" }));
    }

    private void ConfigurarProductosHttpException()
    {
        // WireMock simula timeout/error de red cerrando la conexión bruscamente
        _wiremock
            .Given(Request.Create().WithPath(PathProductos).WithParam("texto", "-1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(503)
                .WithBody("Service Unavailable"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.1 — Sync inicial sin ETag previo, Maquinaria devuelve 200
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.1 — Sync inicial sin ETag: se persisten 3 CausaFallaCatalogo en el repo local.</summary>
    [Fact]
    public async Task SincronizarCatalogos_sync_inicial_sin_etag_persiste_causas_falla_en_repo()
    {
        // Given: no existe CatalogoSyncState para causas-falla (primer sync)
        // Y Maquinaria devuelve 200 con 3 causas
        ConfigurarCausasFalla200(3, "\"v1\"");
        ConfigurarTiposFalla200(2, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When: se ejecuta el sync-all
        var resultado = await handler.EjecutarAsync();

        // Then: existen 3 CausaFallaCatalogo en el repo
        _repo.CausasFalla.Should().HaveCount(3);
    }

    /// <summary>§6.1 — Sync inicial: CatalogoSyncState para "causas-falla" queda con estado "actualizado" y ETag guardado.</summary>
    [Fact]
    public async Task SincronizarCatalogos_sync_inicial_sin_etag_guarda_state_causas_falla_actualizado()
    {
        // Given: no existe CatalogoSyncState
        ConfigurarCausasFalla200(3, "\"v1\"");
        ConfigurarTiposFalla200(2, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: CatalogoSyncState para "causas-falla" existe con UltimoEstado = "actualizado"
        var state = _repo.ObtenerState("causas-falla");
        state.Should().NotBeNull();
        state!.UltimoEstado.Should().Be("actualizado");
        state.EtagActual.Should().Be("\"v1\"");
        state.UltimaSyncExitosa.Should().NotBeNull();
    }

    /// <summary>§6.1 — Sync inicial: la respuesta del endpoint contiene nombre "causas-falla" con status "actualizado".</summary>
    [Fact]
    public async Task SincronizarCatalogos_sync_inicial_respuesta_incluye_causas_falla_como_actualizado()
    {
        // Given
        ConfigurarCausasFalla200(3, "\"v1\"");
        ConfigurarTiposFalla200(2, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        var resultado = await handler.EjecutarAsync();

        // Then: el resultado incluye catalogo "causas-falla" con status "actualizado"
        var catalogoCausas = resultado.Catalogos
            .Should().Contain(c => c.Nombre == "causas-falla")
            .Which;
        catalogoCausas.Status.Should().Be("actualizado");
        catalogoCausas.ActualizadosEn.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.2 — Sync con ETag previo, Maquinaria devuelve 304
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.2 — ETag previo y 304: no se borran ni crean documentos TipoFallaCatalogo.</summary>
    [Fact]
    public async Task SincronizarCatalogos_con_etag_previo_y_304_no_toca_cache_tipos_falla()
    {
        // Given: CatalogoSyncState { Id="tipos-falla", EtagActual="\"v5\"" }
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "tipos-falla",
            EtagActual = "\"v5\"",
            UltimoEstado = "actualizado",
        });
        // Y existe 1 TipoFallaCatalogo previo
        _repo.SeedTiposFalla(new[] { new TipoFallaCatalogo(10, "Eléctrica", "ALTA") });

        // Y Maquinaria responde 304 para tipos-falla
        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: el TipoFallaCatalogo previo sigue intacto (sin borrar ni reemplazar)
        _repo.TiposFalla.Should().HaveCount(1);
        _repo.TiposFalla[0].Id.Should().Be(10);
    }

    /// <summary>§6.2 — ETag previo y 304: CatalogoSyncState.UltimoEstado queda "no-change".</summary>
    [Fact]
    public async Task SincronizarCatalogos_con_etag_previo_y_304_guarda_state_no_change()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "tipos-falla",
            EtagActual = "\"v5\"",
            UltimoEstado = "actualizado",
        });
        _repo.SeedTiposFalla(new[] { new TipoFallaCatalogo(10, "Eléctrica", "ALTA") });

        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: UltimoEstado = "no-change" y UltimaSyncExitosa actualizado
        var state = _repo.ObtenerState("tipos-falla");
        state.Should().NotBeNull();
        state!.UltimoEstado.Should().Be("no-change");
        state.UltimaSyncExitosa.Should().NotBeNull();
    }

    /// <summary>§6.2 — ETag previo y 304: la respuesta contiene tipos-falla con status "no-change".</summary>
    [Fact]
    public async Task SincronizarCatalogos_con_etag_y_304_respuesta_tipos_falla_es_no_change()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "tipos-falla",
            EtagActual = "\"v5\"",
            UltimoEstado = "actualizado",
        });
        _repo.SeedTiposFalla(new[] { new TipoFallaCatalogo(10, "Eléctrica", "ALTA") });

        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        var resultado = await handler.EjecutarAsync();

        // Then
        var cat = resultado.Catalogos.Should().Contain(c => c.Nombre == "tipos-falla").Which;
        cat.Status.Should().Be("no-change");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.3 — Sync con ETag previo, Maquinaria devuelve 200 (cambio detectado)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.3 — ETag previo y 200 nuevo: los 3 docs previos son reemplazados por los 4 nuevos.</summary>
    [Fact]
    public async Task SincronizarCatalogos_etag_previo_y_200_nuevo_reemplaza_causas_falla_wipe_and_replace()
    {
        // Given: 3 causas previas y CatalogoSyncState con ETag "v10"
        _repo.SeedCausasFalla(Enumerable.Range(1, 3).Select(i =>
            new CausaFallaCatalogo(i, $"Causa antigua {i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v10\"",
            UltimoEstado = "actualizado",
        });

        // Maquinaria responde 200 con 4 nuevas causas y ETag "v11"
        ConfigurarCausasFalla200(4, "\"v11\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: exactamente 4 causas (wipe-and-replace de las 3 antiguas)
        _repo.CausasFalla.Should().HaveCount(4);
    }

    /// <summary>§6.3 — ETag previo y 200 nuevo: CatalogoSyncState.EtagActual queda con el nuevo etag.</summary>
    [Fact]
    public async Task SincronizarCatalogos_etag_previo_y_200_nuevo_actualiza_etag_en_state()
    {
        // Given
        _repo.SeedCausasFalla(Enumerable.Range(1, 3).Select(i => new CausaFallaCatalogo(i, $"C{i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v10\"",
            UltimoEstado = "actualizado",
        });

        ConfigurarCausasFalla200(4, "\"v11\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: EtagActual = "\"v11\""
        var state = _repo.ObtenerState("causas-falla");
        state!.EtagActual.Should().Be("\"v11\"");
        state.UltimoEstado.Should().Be("actualizado");
    }

    /// <summary>§6.3 — ETag previo y 200 nuevo: respuesta contiene causas-falla como "actualizado".</summary>
    [Fact]
    public async Task SincronizarCatalogos_etag_previo_y_200_nuevo_respuesta_causas_falla_es_actualizado()
    {
        // Given
        _repo.SeedCausasFalla(Enumerable.Range(1, 3).Select(i => new CausaFallaCatalogo(i, $"C{i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v10\"",
            UltimoEstado = "actualizado",
        });

        ConfigurarCausasFalla200(4, "\"v11\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        var resultado = await handler.EjecutarAsync();

        // Then
        resultado.Catalogos.Should().Contain(c => c.Nombre == "causas-falla" && c.Status == "actualizado");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.4 — Partial failure: MaquinariaErpException en un catálogo
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.4 — causas-falla 5xx: el cache local de causas-falla no se toca.</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_5xx_cache_local_intacto()
    {
        // Given: CatalogoSyncState previos para todos los catálogos
        _repo.SeedState(new CatalogoSyncState { Id = "causas-falla", EtagActual = "\"v1\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "tipos-falla", EtagActual = "\"v5\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v3\"", UltimoEstado = "actualizado" });

        // Y existen 2 CausaFallaCatalogo previas
        _repo.SeedCausasFalla(new[]
        {
            new CausaFallaCatalogo(1, "Sobrecarga"),
            new CausaFallaCatalogo(2, "Fatiga"),
        });

        // Y causas-falla lanza MaquinariaErpException (5xx)
        ConfigurarCausasFalla5xx();
        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarProductos304("\"v3\"");

        var handler = CrearHandler();

        // When: el sync completa (no lanza — D5 partial-failure)
        var resultado = await handler.EjecutarAsync();

        // Then: el cache de causas-falla NO se tocó (sigue con 2 docs)
        _repo.CausasFalla.Should().HaveCount(2);
    }

    /// <summary>§6.4 — causas-falla 5xx: CatalogoSyncState.UltimoEstado queda "error" con mensaje.</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_5xx_guarda_state_error_con_mensaje()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState { Id = "causas-falla", EtagActual = "\"v1\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "tipos-falla", EtagActual = "\"v5\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v3\"", UltimoEstado = "actualizado" });
        _repo.SeedCausasFalla(new[] { new CausaFallaCatalogo(1, "Sobrecarga") });

        ConfigurarCausasFalla5xx();
        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarProductos304("\"v3\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: CatalogoSyncState.UltimoEstado = "error" y UltimoErrorMensaje poblado
        var state = _repo.ObtenerState("causas-falla");
        state!.UltimoEstado.Should().Be("error");
        state.UltimoErrorMensaje.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>§6.4 — causas-falla 5xx, tipos-falla 304: tipos-falla se procesa con "no-change".</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_5xx_tipos_falla_304_procesado_correctamente()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState { Id = "causas-falla", EtagActual = "\"v1\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "tipos-falla", EtagActual = "\"v5\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v3\"", UltimoEstado = "actualizado" });
        _repo.SeedCausasFalla(new[] { new CausaFallaCatalogo(1, "Sobrecarga") });

        ConfigurarCausasFalla5xx();
        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarProductos304("\"v3\"");

        var handler = CrearHandler();

        // When
        var resultado = await handler.EjecutarAsync();

        // Then: tipos-falla queda "no-change"; causas-falla queda "error"
        resultado.Catalogos.Should().Contain(c => c.Nombre == "tipos-falla" && c.Status == "no-change");
        resultado.Catalogos.Should().Contain(c => c.Nombre == "causas-falla" && c.Status == "error");
    }

    /// <summary>§6.4 — El endpoint no falla aunque un catálogo falle (el handler siempre completa — D5).</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_5xx_handler_no_lanza_excepcion()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState { Id = "causas-falla", EtagActual = "\"v1\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "tipos-falla", EtagActual = "\"v5\"", UltimoEstado = "actualizado" });
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v3\"", UltimoEstado = "actualizado" });

        ConfigurarCausasFalla5xx();
        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarProductos304("\"v3\"");

        var handler = CrearHandler();

        // When
        var act = () => handler.EjecutarAsync();

        // Then: el handler completa sin lanzar excepción (D5 — partial failure silenciado)
        await act.Should().NotThrowAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.5 — Partial failure: HttpRequestException en un catálogo
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.5 — causas-falla 200 ok, productos 503 HttpRequestException: ambos estados en la respuesta.</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_ok_productos_error_ambos_estados_en_respuesta()
    {
        // Given: sin state previo para causas-falla; productos falla con 503
        ConfigurarCausasFalla200(3, "\"v2\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductosHttpException();

        var handler = CrearHandler();

        // When
        var resultado = await handler.EjecutarAsync();

        // Then: causas-falla "actualizado", productos "error"
        resultado.Catalogos.Should().Contain(c => c.Nombre == "causas-falla" && c.Status == "actualizado");
        resultado.Catalogos.Should().Contain(c => c.Nombre == "productos" && c.Status == "error");
    }

    /// <summary>§6.5 — productos HttpRequestException: el cache de RepuestoLocal queda intacto.</summary>
    [Fact]
    public async Task SincronizarCatalogos_productos_error_cache_repuesto_local_intacto()
    {
        // Given: existen 2 RepuestoLocal previos
        _repo.SeedProductos(new[]
        {
            new RepuestoLocal(100, "SKU-100", "Filtro aceite", "UND", new[] { 1, 2 }),
            new RepuestoLocal(101, "SKU-101", "Correa distribución", "UND", new[] { 3 }),
        });
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v1\"", UltimoEstado = "actualizado" });

        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductosHttpException();

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: los 2 RepuestoLocal previos siguen intactos
        _repo.Productos.Should().HaveCount(2);
    }

    /// <summary>§6.5 — productos error: CatalogoSyncState para "productos" queda con UltimoEstado="error".</summary>
    [Fact]
    public async Task SincronizarCatalogos_productos_error_guarda_state_error()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v1\"", UltimoEstado = "actualizado" });

        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductosHttpException();

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then
        var state = _repo.ObtenerState("productos");
        state!.UltimoEstado.Should().Be("error");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.6 — Body vacío con 200: política conservadora (D4)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.6 — 200 con array vacío: los 5 CausaFallaCatalogo previos permanecen intactos (D4).</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_200_vacio_cache_no_se_borra_D4()
    {
        // Given: 5 causas previas y CatalogoSyncState con ETag "v3"
        _repo.SeedCausasFalla(Enumerable.Range(1, 5).Select(i => new CausaFallaCatalogo(i, $"C{i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v3\"",
            UltimoEstado = "actualizado",
        });

        // Maquinaria devuelve 200 con array vacío y ETag nuevo "v4"
        ConfigurarCausasFallaVacias("\"v4\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: los 5 documentos previos NO se borran
        _repo.CausasFalla.Should().HaveCount(5);
    }

    /// <summary>§6.6 — 200 con array vacío: EtagActual NO se actualiza (D4).</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_200_vacio_no_actualiza_etag_D4()
    {
        // Given
        _repo.SeedCausasFalla(Enumerable.Range(1, 5).Select(i => new CausaFallaCatalogo(i, $"C{i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v3\"",
            UltimoEstado = "actualizado",
        });

        ConfigurarCausasFallaVacias("\"v4\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: EtagActual sigue siendo "v3" (no se acepta el nuevo "v4")
        var state = _repo.ObtenerState("causas-falla");
        state!.EtagActual.Should().Be("\"v3\"");
    }

    /// <summary>§6.6 — 200 con array vacío: CatalogoSyncState.UltimoEstado = "vaciado-sospechoso" (D4).</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_200_vacio_guarda_state_vaciado_sospechoso_D4()
    {
        // Given
        _repo.SeedCausasFalla(Enumerable.Range(1, 5).Select(i => new CausaFallaCatalogo(i, $"C{i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v3\"",
            UltimoEstado = "actualizado",
        });

        ConfigurarCausasFallaVacias("\"v4\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: UltimoEstado = "vaciado-sospechoso"
        var state = _repo.ObtenerState("causas-falla");
        state!.UltimoEstado.Should().Be("vaciado-sospechoso");
    }

    /// <summary>§6.6 — 200 con array vacío: la respuesta indica status "error" con mensaje de catálogo vacío.</summary>
    [Fact]
    public async Task SincronizarCatalogos_causas_falla_200_vacio_respuesta_indica_error_con_mensaje_D4()
    {
        // Given
        _repo.SeedCausasFalla(Enumerable.Range(1, 5).Select(i => new CausaFallaCatalogo(i, $"C{i}")));
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "causas-falla",
            EtagActual = "\"v3\"",
            UltimoEstado = "actualizado",
        });

        ConfigurarCausasFallaVacias("\"v4\"");
        ConfigurarTiposFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        var resultado = await handler.EjecutarAsync();

        // Then: el catalogo "causas-falla" reporta status "error" (o "vaciado-sospechoso")
        // con mensaje que menciona catálogo vacío
        var cat = resultado.Catalogos.Should().Contain(c => c.Nombre == "causas-falla").Which;
        cat.Status.Should().BeOneOf("error", "vaciado-sospechoso");
        cat.Error.Should().NotBeNullOrWhiteSpace();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.7 — Sync concurrente con mismo ETag (last-write-wins, D6)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.7 — Dos handlers ejecutados secuencialmente con mismo ETag y 304: ambos completan, estado "no-change".</summary>
    [Fact]
    public async Task SincronizarCatalogos_dos_ejecuciones_con_mismo_etag_y_304_idempotente_D6()
    {
        // Given: CatalogoSyncState con ETag "v10"
        _repo.SeedState(new CatalogoSyncState { Id = "causas-falla", EtagActual = "\"v10\"", UltimoEstado = "no-change" });
        _repo.SeedState(new CatalogoSyncState { Id = "tipos-falla", EtagActual = "\"v5\"", UltimoEstado = "no-change" });
        _repo.SeedState(new CatalogoSyncState { Id = "productos", EtagActual = "\"v3\"", UltimoEstado = "no-change" });

        // WireMock siempre responde 304 (ningún cambio)
        ConfigurarCausasFalla304("\"v10\"");
        ConfigurarTiposFalla304("\"v5\"");
        ConfigurarProductos304("\"v3\"");

        // When: dos ejecuciones secuenciales (simula concurrencia — last-write-wins D6)
        var handler1 = CrearHandler();
        var handler2 = CrearHandler();

        await handler1.EjecutarAsync();
        await handler2.EjecutarAsync();

        // Then: UltimoEstado = "no-change" (ambas escrituras producen el mismo valor — idempotente)
        _repo.ObtenerState("causas-falla")!.UltimoEstado.Should().Be("no-change");

        // Y los documentos no se tocaron (no hubo wipe-and-replace)
        _repo.CausasFalla.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // §6.8 — Verificación de estado final del documento (análogo a rebuild)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>§6.8 — 200 con nuevo cuerpo: Marten contiene exactamente 3 TipoFallaCatalogo tras reemplazar los 2 anteriores.</summary>
    [Fact]
    public async Task SincronizarCatalogos_tipos_falla_200_nuevo_cuerpo_repo_contiene_exactamente_3_tipos()
    {
        // Given: CatalogoSyncState { Id="tipos-falla", EtagActual="\"v2\"", UltimoEstado="no-change" }
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "tipos-falla",
            EtagActual = "\"v2\"",
            UltimoEstado = "no-change",
        });
        // Y 2 TipoFallaCatalogo previos
        _repo.SeedTiposFalla(new[]
        {
            new TipoFallaCatalogo(1, "Mecánica", "MEDIA"),
            new TipoFallaCatalogo(2, "Eléctrica", "ALTA"),
        });

        // Maquinaria devuelve 3 tipos con ETag "v3"
        ConfigurarTiposFalla200(3, "\"v3\"");
        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: exactamente 3 TipoFallaCatalogo (los 2 anteriores reemplazados)
        _repo.TiposFalla.Should().HaveCount(3);
    }

    /// <summary>§6.8 — Estado del documento CatalogoSyncState tras sync es idéntico al escrito por el handler (análogo al rebuild check).</summary>
    [Fact]
    public async Task SincronizarCatalogos_tipos_falla_200_nuevo_cuerpo_state_coherente_con_repo()
    {
        // Given
        _repo.SeedState(new CatalogoSyncState
        {
            Id = "tipos-falla",
            EtagActual = "\"v2\"",
            UltimoEstado = "no-change",
        });
        _repo.SeedTiposFalla(new[]
        {
            new TipoFallaCatalogo(1, "Mecánica", "MEDIA"),
            new TipoFallaCatalogo(2, "Eléctrica", "ALTA"),
        });

        ConfigurarTiposFalla200(3, "\"v3\"");
        ConfigurarCausasFalla200(1, "\"v1\"");
        ConfigurarProductos200(1, "\"v1\"");

        var handler = CrearHandler();

        // When
        await handler.EjecutarAsync();

        // Then: si se recarga CatalogoSyncState desde el repo (equivale al rebuild check),
        // los campos son idénticos a los escritos por el handler
        var state = _repo.ObtenerState("tipos-falla");
        state.Should().NotBeNull();
        state!.EtagActual.Should().Be("\"v3\"");
        state.UltimoEstado.Should().Be("actualizado");
        state.UltimaSyncExitosa.Should().NotBeNull();
    }
}
