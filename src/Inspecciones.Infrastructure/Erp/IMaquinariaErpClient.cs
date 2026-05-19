using Inspecciones.Infrastructure.Erp.Dtos;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Adapter HTTP de Inspecciones contra Maquinaria_V4 (módulo SincoMyE del ERP).
/// Expone los endpoints que el módulo necesita para:
///   1. Hidratar el catálogo local en Marten (sync on-app-open, ADR-004 canonical).
///   2. Emitir comandos de escritura post-firma (M-W-1) y de cierre preop (P-6).
/// </summary>
/// <remarks>
/// <para>
/// Convenciones del adapter:
/// </para>
/// <list type="bullet">
///   <item><b>BaseAddress</b>: <c>{Maquinaria:BaseUrl}/</c> (incluye <c>/api/v4/Maquinaria</c>).</item>
///   <item><b>Header Authorization</b>: <c>Bearer {jwt}</c> propagado desde la PWA host.</item>
///   <item><b>If-None-Match</b>: aceptado por los endpoints de catálogos (causas-falla,
///   tipos-falla, productos, equipos, partes-equipos, rutinas-monitoreo). El adapter
///   devuelve <see cref="EtagResult{T}"/> con discriminador NotModified.</item>
///   <item><b>Error envelope</b>: Maquinaria devuelve <c>{Codigo, Mensaje}</c>. El
///   adapter lo deserializa y lo mete en <see cref="MaquinariaErpException"/>.</item>
/// </list>
/// </remarks>
public interface IMaquinariaErpClient
{
    // ── Equipos (slice 2 Maquinaria_V4) ─────────────────────────────────────
    Task<EtagResult<ListarEquiposResponseDto>> ListarEquiposAsync(
        string? filtro,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    // ── Partes-equipos (slice 3 Maquinaria_V4) ──────────────────────────────
    /// <summary><c>idEquipo</c>: <c>-1</c> trae todas las partes visibles; entero positivo filtra por equipo.</summary>
    Task<EtagResult<ListarPartesEquiposResponseDto>> ListarPartesEquipoAsync(
        int idEquipo,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    // ── Causas-falla (slice 5 Maquinaria_V4) ────────────────────────────────
    /// <summary><c>texto</c>: <c>-1</c> trae todo el catálogo; texto no vacío filtra por descripción.</summary>
    Task<EtagResult<ListarCausasFallaResponseDto>> ListarCausasFallaAsync(
        string texto,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    // ── Tipos-falla (slice 6 Maquinaria_V4) ─────────────────────────────────
    Task<EtagResult<ListarTiposFallaResponseDto>> ListarTiposFallaAsync(
        string texto,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    // ── Productos / repuestos (slice 4 Maquinaria_V4) ───────────────────────
    Task<EtagResult<ListarProductosResponseDto>> ListarProductosAsync(
        string texto,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    // ── Preoperacional-fallas listar (slice 7 Maquinaria_V4) ────────────────
    /// <summary>
    /// Lista novedades preop visibles. <c>equipoId=-1</c> = sin filtro de equipo;
    /// <c>texto=-1</c> = sin filtro de texto. Slice 7 NO emite ETag.
    /// </summary>
    Task<ListarPreoperacionalFallasResponseDto> ListarPreoperacionalFallasAsync(
        DateOnly desde,
        DateOnly hasta,
        int equipoId,
        string texto,
        CancellationToken ct = default);

    // ── Preoperacional-fallas cerrar (slice 9 Maquinaria_V4) ────────────────
    /// <summary>
    /// Cierra (bulk-first, 1..N) novedades preop. Consumido por
    /// <c>DescartarNovedadPreop</c> y por <c>CerrarInspeccionSaga</c> (ADR-006 outbox).
    /// </summary>
    Task<CerrarPreoperacionalFallasResponseDto> CerrarPreoperacionalFallasAsync(
        CerrarPreoperacionalFallasRequestDto request,
        CancellationToken ct = default);

    // ── Rutinas-monitoreo por equipo (slice 10 Maquinaria_V4) ───────────────
    Task<EtagResult<ListarRutinasMonitoreoPorEquipoResponseDto>> ListarRutinasMonitoreoPorEquipoAsync(
        int equipoId,
        string? ifNoneMatch = null,
        CancellationToken ct = default);

    // ── Actualizar dictamen equipo (slice 11 Maquinaria_V4) ─────────────────
    /// <summary>
    /// M-W-1 — actualiza dictamen vigente post-firma. Consumido por
    /// <c>SincronizarDictamenVigenteSaga</c> (outbox ADR-006).
    /// </summary>
    Task<ActualizarDictamenEquipoResponseDto> ActualizarDictamenEquipoAsync(
        int equipoCodigo,
        ActualizarDictamenEquipoRequestDto request,
        CancellationToken ct = default);
}

/// <summary>
/// Resultado de un GET que soporta <c>If-None-Match</c>:
/// <c>Body</c> + <c>ETag</c> presentes cuando <c>NotModified=false</c>; <c>Body=null</c>
/// cuando <c>NotModified=true</c>. Construido por el cliente a través de
/// <see cref="EtagResult.Modified{T}"/> y <see cref="EtagResult.NotModified{T}"/>.
/// </summary>
public sealed record EtagResult<T>(T? Body, string? ETag, bool NotModified) where T : class;

/// <summary>Factories no-genéricas para <see cref="EtagResult{T}"/> (evita CA1000 sobre tipo genérico).</summary>
public static class EtagResult
{
    public static EtagResult<T> Modified<T>(T body, string? etag) where T : class =>
        new(body, etag, false);

    public static EtagResult<T> NotModified<T>(string? etag) where T : class =>
        new(null, etag, true);
}
