using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Inspecciones.Infrastructure.Erp.Dtos;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Implementación HTTP del adapter contra Maquinaria_V4. El <see cref="HttpClient"/>
/// inyectado ya tiene <c>BaseAddress</c> y el header <c>Authorization</c> configurados
/// por la fábrica de DI en <c>Program.cs</c>.
/// </summary>
public sealed class MaquinariaErpClient : IMaquinariaErpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public MaquinariaErpClient(HttpClient http)
    {
        _http = http;
    }

    // ─── Equipos ────────────────────────────────────────────────────────────
    public Task<EtagResult<ListarEquiposResponseDto>> ListarEquiposAsync(
        string? filtro,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(filtro)
            ? "api/equipos"
            : $"api/equipos?filtro={Uri.EscapeDataString(filtro)}";
        return GetWithEtagAsync<ListarEquiposResponseDto>(url, ifNoneMatch, ct);
    }

    // ─── Partes-equipos ─────────────────────────────────────────────────────
    public Task<EtagResult<ListarPartesEquiposResponseDto>> ListarPartesEquipoAsync(
        int idEquipo,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var url = $"api/partes-equipos?idEquipo={idEquipo.ToString(CultureInfo.InvariantCulture)}";
        return GetWithEtagAsync<ListarPartesEquiposResponseDto>(url, ifNoneMatch, ct);
    }

    // ─── Causas-falla ───────────────────────────────────────────────────────
    public Task<EtagResult<ListarCausasFallaResponseDto>> ListarCausasFallaAsync(
        string texto,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var url = $"api/causas-falla?texto={Uri.EscapeDataString(texto)}";
        return GetWithEtagAsync<ListarCausasFallaResponseDto>(url, ifNoneMatch, ct);
    }

    // ─── Tipos-falla ────────────────────────────────────────────────────────
    public Task<EtagResult<ListarTiposFallaResponseDto>> ListarTiposFallaAsync(
        string texto,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var url = $"api/tipos-falla?texto={Uri.EscapeDataString(texto)}";
        return GetWithEtagAsync<ListarTiposFallaResponseDto>(url, ifNoneMatch, ct);
    }

    // ─── Productos ──────────────────────────────────────────────────────────
    public Task<EtagResult<ListarProductosResponseDto>> ListarProductosAsync(
        string texto,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var url = $"api/productos?texto={Uri.EscapeDataString(texto)}";
        return GetWithEtagAsync<ListarProductosResponseDto>(url, ifNoneMatch, ct);
    }

    // ─── Preoperacional-fallas listar (sin ETag) ────────────────────────────
    public async Task<ListarPreoperacionalFallasResponseDto> ListarPreoperacionalFallasAsync(
        DateOnly desde,
        DateOnly hasta,
        int equipoId,
        string texto,
        CancellationToken ct = default)
    {
        var qs = new StringBuilder("api/preoperacional-fallas?");
        qs.Append("desde=").Append(desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        qs.Append("&hasta=").Append(hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        qs.Append("&equipoId=").Append(equipoId.ToString(CultureInfo.InvariantCulture));
        qs.Append("&texto=").Append(Uri.EscapeDataString(texto));

        using var response = await _http.GetAsync(qs.ToString(), ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, qs.ToString(), ct).ConfigureAwait(false);
        return await DeserializeOrThrowAsync<ListarPreoperacionalFallasResponseDto>(response, ct).ConfigureAwait(false);
    }

    // ─── Preoperacional-fallas cerrar ───────────────────────────────────────
    public async Task<CerrarPreoperacionalFallasResponseDto> CerrarPreoperacionalFallasAsync(
        CerrarPreoperacionalFallasRequestDto request,
        CancellationToken ct = default)
    {
        const string url = "api/preoperacional-fallas/cerrar";
        using var response = await _http.PostAsJsonAsync(url, request, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, url, ct).ConfigureAwait(false);
        return await DeserializeOrThrowAsync<CerrarPreoperacionalFallasResponseDto>(response, ct).ConfigureAwait(false);
    }

    // ─── Rutinas-monitoreo por equipo ───────────────────────────────────────
    public Task<EtagResult<ListarRutinasMonitoreoPorEquipoResponseDto>> ListarRutinasMonitoreoPorEquipoAsync(
        int equipoId,
        string? ifNoneMatch = null,
        CancellationToken ct = default)
    {
        var url = $"api/rutinas-monitoreo?equipoId={equipoId.ToString(CultureInfo.InvariantCulture)}";
        return GetWithEtagAsync<ListarRutinasMonitoreoPorEquipoResponseDto>(url, ifNoneMatch, ct);
    }

    // ─── Actualizar dictamen equipo ─────────────────────────────────────────
    public async Task<ActualizarDictamenEquipoResponseDto> ActualizarDictamenEquipoAsync(
        int equipoCodigo,
        ActualizarDictamenEquipoRequestDto request,
        CancellationToken ct = default)
    {
        var url = $"api/equipos/{equipoCodigo.ToString(CultureInfo.InvariantCulture)}/dictamen-vigente";
        using var response = await _http.PutAsJsonAsync(url, request, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, url, ct).ConfigureAwait(false);
        return await DeserializeOrThrowAsync<ActualizarDictamenEquipoResponseDto>(response, ct).ConfigureAwait(false);
    }

    // ─── Internals ──────────────────────────────────────────────────────────

    private async Task<EtagResult<T>> GetWithEtagAsync<T>(
        string relativeUrl,
        string? ifNoneMatch,
        CancellationToken ct)
        where T : class
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            // EntityTagHeaderValue.TryParse soporta el quoting estándar; si falla, lo pasamos crudo.
            if (EntityTagHeaderValue.TryParse(ifNoneMatch, out var tag))
            {
                req.Headers.IfNoneMatch.Add(tag);
            }
            else
            {
                req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            }
        }

        using var response = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return EtagResult.NotModified<T>(response.Headers.ETag?.ToString());
        }

        await EnsureSuccessOrThrowAsync(response, relativeUrl, ct).ConfigureAwait(false);
        var body = await DeserializeOrThrowAsync<T>(response, ct).ConfigureAwait(false);
        return EtagResult.Modified(body, response.Headers.ETag?.ToString());
    }

    private static async Task<T> DeserializeOrThrowAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var dto = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        if (dto is null)
        {
            throw new MaquinariaErpException(
                $"Maquinaria_V4 devolvió cuerpo nulo al deserializar a {typeof(T).Name}.",
                response.StatusCode);
        }
        return dto;
    }

    private static async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Intentamos extraer el error envelope estándar de Maquinaria_V4
        // ({Codigo, Mensaje}); si el body no es JSON (p. ej. plaintext del
        // middleware 401), lo tratamos como mensaje crudo.
        string? codigoErp = null;
        string? mensajeErp = null;
        string rawBody = string.Empty;
        try
        {
            rawBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(rawBody)
                && rawBody.AsSpan().TrimStart().StartsWith("{"))
            {
                var envelope = JsonSerializer.Deserialize<ErrorEnvelopeDto>(rawBody, JsonOptions);
                if (envelope is not null)
                {
                    codigoErp = string.IsNullOrWhiteSpace(envelope.Codigo) ? null : envelope.Codigo;
                    mensajeErp = string.IsNullOrWhiteSpace(envelope.Mensaje) ? null : envelope.Mensaje;
                }
            }
        }
        catch (JsonException)
        {
            // Body no parseable — caemos a "raw body" en el mensaje.
        }

        var summary = mensajeErp ?? Truncate(rawBody, 500);
        var msg =
            $"Maquinaria_V4 respondió {(int)response.StatusCode} {response.StatusCode} en {url}." +
            (codigoErp is not null ? $" CodigoErp={codigoErp}." : "") +
            (string.IsNullOrWhiteSpace(summary) ? "" : $" Mensaje: {summary}");

        throw new MaquinariaErpException(msg, response.StatusCode, codigoErp);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "…");
}

/// <summary>
/// Excepción genérica al llamar a Maquinaria_V4. Incluye el código HTTP
/// y el <c>Codigo</c> del envelope de error de Maquinaria (cuando aplica)
/// para que la capa Api decida si mapear a 502/401/etc.
/// </summary>
public sealed class MaquinariaErpException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? CodigoErp { get; }

    public MaquinariaErpException(string message, HttpStatusCode? statusCode = null, string? codigoErp = null)
        : base(message)
    {
        StatusCode = statusCode;
        CodigoErp = codigoErp;
    }
}
