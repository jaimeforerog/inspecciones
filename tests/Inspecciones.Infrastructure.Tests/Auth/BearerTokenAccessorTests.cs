using FluentAssertions;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Inspecciones.Infrastructure.Tests.Auth;

/// <summary>
/// Tests rojos del slice mt-3 §6.5, §6.6.
///
/// Cubre los tres accessors (HttpContext, Ambient, ServiceAccount) y la
/// composición <see cref="ChainedBearerTokenAccessor"/>. La cadena de
/// resolución HTTP → Ambient → ServiceAccount debe devolver el primer
/// no-vacío. Si todos son nulos, retorna null (el DelegatingHandler
/// es el que aplica fail-closed con <see cref="BearerTokenAusenteException"/>).
/// </summary>
public sealed class BearerTokenAccessorTests
{
    // ─── HttpContextBearerTokenAccessor ─────────────────────────────────────

    [Fact]
    public void HttpContextAccessor_extrae_token_del_header_Authorization()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer jwt-empresa-7";

        var httpAccessor = new HttpContextAccessor { HttpContext = http };
        var accessor = new HttpContextBearerTokenAccessor(httpAccessor);

        accessor.ObtenerBearerToken().Should().Be("jwt-empresa-7");
    }

    [Fact]
    public void HttpContextAccessor_sin_HttpContext_retorna_null()
    {
        var httpAccessor = new HttpContextAccessor();
        var accessor = new HttpContextBearerTokenAccessor(httpAccessor);

        accessor.ObtenerBearerToken().Should().BeNull();
    }

    [Fact]
    public void HttpContextAccessor_sin_header_Authorization_retorna_null()
    {
        var http = new DefaultHttpContext();
        var httpAccessor = new HttpContextAccessor { HttpContext = http };
        var accessor = new HttpContextBearerTokenAccessor(httpAccessor);

        accessor.ObtenerBearerToken().Should().BeNull();
    }

    [Fact]
    public void HttpContextAccessor_header_sin_prefijo_Bearer_retorna_null()
    {
        // Tokens sin "Bearer " no son válidos para propagar.
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";
        var httpAccessor = new HttpContextAccessor { HttpContext = http };
        var accessor = new HttpContextBearerTokenAccessor(httpAccessor);

        accessor.ObtenerBearerToken().Should().BeNull();
    }

    // ─── AmbientBearerTokenAccessor ─────────────────────────────────────────

    [Fact]
    public void AmbientAccessor_sin_set_retorna_null()
    {
        var accessor = new AmbientBearerTokenAccessor();
        accessor.ObtenerBearerToken().Should().BeNull();
    }

    [Fact]
    public void AmbientAccessor_set_y_obtener_en_mismo_scope()
    {
        var accessor = new AmbientBearerTokenAccessor();
        using var _ = accessor.SetForCurrentScope("jwt-tenant-7");
        accessor.ObtenerBearerToken().Should().Be("jwt-tenant-7");
    }

    [Fact]
    public void AmbientAccessor_dispose_limpia_el_token()
    {
        var accessor = new AmbientBearerTokenAccessor();
        using (var _ = accessor.SetForCurrentScope("jwt-tenant-7"))
        {
            accessor.ObtenerBearerToken().Should().Be("jwt-tenant-7");
        }
        accessor.ObtenerBearerToken().Should().BeNull();
    }

    [Fact]
    public async Task AmbientAccessor_aislado_entre_tareas_paralelas()
    {
        var accessor = new AmbientBearerTokenAccessor();

        var t1 = Task.Run(async () =>
        {
            using var _ = accessor.SetForCurrentScope("jwt-task-1");
            await Task.Delay(50);
            return accessor.ObtenerBearerToken();
        });

        var t2 = Task.Run(async () =>
        {
            using var _ = accessor.SetForCurrentScope("jwt-task-2");
            await Task.Delay(50);
            return accessor.ObtenerBearerToken();
        });

        var resultados = await Task.WhenAll(t1, t2);
        resultados.Should().Contain("jwt-task-1").And.Contain("jwt-task-2");
    }

    // ─── ServiceAccountBearerTokenAccessor ──────────────────────────────────

    [Fact]
    public void ServiceAccountAccessor_devuelve_JwtToken_de_options()
    {
        var options = Options.Create(new MaquinariaErpOptions { JwtToken = "service-account-token" });
        var accessor = new ServiceAccountBearerTokenAccessor(options);

        accessor.ObtenerBearerToken().Should().Be("service-account-token");
    }

    [Fact]
    public void ServiceAccountAccessor_JwtToken_vacio_retorna_null()
    {
        var options = Options.Create(new MaquinariaErpOptions { JwtToken = string.Empty });
        var accessor = new ServiceAccountBearerTokenAccessor(options);

        accessor.ObtenerBearerToken().Should().BeNull();
    }

    // ─── ChainedBearerTokenAccessor — orden HTTP → Ambient → ServiceAccount ─

    [Fact]
    public void Chained_HTTP_gana_sobre_envelope_y_service_account()
    {
        // §6.6 del spec: orden HTTP > ambient > service-account.
        var http = ConHttpContextConBearer("jwt-http-call");
        var ambient = new AmbientBearerTokenAccessor();
        using var _ = ambient.SetForCurrentScope("jwt-envelope-stale");
        var service = ConServiceAccount("service-account");

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(http),
            ambient,
            service);

        chained.ObtenerBearerToken().Should().Be("jwt-http-call");
    }

    [Fact]
    public void Chained_sin_HTTP_usa_ambient()
    {
        // Caso típico listener Wolverine.
        var http = new HttpContextAccessor(); // sin HttpContext
        var ambient = new AmbientBearerTokenAccessor();
        using var _ = ambient.SetForCurrentScope("jwt-envelope-7");
        var service = ConServiceAccount("service-account");

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(http),
            ambient,
            service);

        chained.ObtenerBearerToken().Should().Be("jwt-envelope-7");
    }

    [Fact]
    public void Chained_sin_HTTP_ni_ambient_cae_a_service_account()
    {
        // Caso patológico: mensaje legacy / publish sin HttpContext.
        var http = new HttpContextAccessor();
        var ambient = new AmbientBearerTokenAccessor();
        var service = ConServiceAccount("service-account-fallback");

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(http),
            ambient,
            service);

        chained.ObtenerBearerToken().Should().Be("service-account-fallback");
    }

    [Fact]
    public void Chained_todos_vacios_retorna_null()
    {
        var http = new HttpContextAccessor();
        var ambient = new AmbientBearerTokenAccessor();
        var service = ConServiceAccount(string.Empty);

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(http),
            ambient,
            service);

        chained.ObtenerBearerToken().Should().BeNull();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static HttpContextAccessor ConHttpContextConBearer(string token)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        return new HttpContextAccessor { HttpContext = http };
    }

    private static ServiceAccountBearerTokenAccessor ConServiceAccount(string token) =>
        new(Options.Create(new MaquinariaErpOptions { JwtToken = token }));
}
