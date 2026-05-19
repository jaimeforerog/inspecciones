using FluentAssertions;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Inspecciones.Infrastructure.Tests.Auth;

/// <summary>
/// Tests rojos del slice mt-3 §6.1, §6.2, §6.3, §6.4, §6.5.
///
/// Verifica que <see cref="BearerTokenPropagationHandler"/> (DelegatingHandler)
/// consulta <see cref="IBearerTokenAccessor"/> en cada request, setea el header
/// <c>Authorization</c> correspondiente, y falla con
/// <see cref="BearerTokenAusenteException"/> si no hay token (fail-closed
/// MT3-INV-3).
///
/// Los tests usan WireMock para inspeccionar el header recibido por el ERP mock.
/// </summary>
public sealed class BearerTokenPropagationHandlerTests : IDisposable
{
    private readonly WireMockServer _server;

    public BearerTokenPropagationHandlerTests()
    {
        _server = WireMockServer.Start();
        _server
            .Given(Request.Create().WithPath("/api/equipos").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { Equipos = Array.Empty<object>(), Total = 0 }));
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    // ─── §6.1 Happy path HTTP ─────────────────────────────────────────────

    [Fact]
    public async Task HTTP_scope_propaga_Bearer_del_request_al_ERP()
    {
        // GIVEN: HttpContext con Authorization = Bearer jwt-empresa-7.
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer jwt-empresa-7";
        var httpAccessor = new HttpContextAccessor { HttpContext = http };

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(httpAccessor),
            new AmbientBearerTokenAccessor(),
            ConServiceAccount("service-account-token"));

        var client = ConErpClient(chained);

        // WHEN: el adapter invoca al ERP.
        await client.ListarEquiposAsync(filtro: null);

        // THEN: WireMock recibe el JWT del request, NO el service-account.
        _server.LogEntries.Should().HaveCount(1);
        var headers = _server.LogEntries.Single().RequestMessage.Headers!;
        headers.Should().ContainKey("Authorization");
        headers["Authorization"].Should().ContainSingle(v => v == "Bearer jwt-empresa-7");
        headers["Authorization"].Should().NotContain(v => v == "Bearer service-account-token");
    }

    // ─── §6.2 Happy path listener ──────────────────────────────────────────

    [Fact]
    public async Task Listener_scope_propaga_Bearer_del_envelope_al_ERP()
    {
        // GIVEN: sin HttpContext, ambient seteado por el listener con el JWT del envelope.
        var ambient = new AmbientBearerTokenAccessor();
        using var _ = ambient.SetForCurrentScope("jwt-tenant-7");

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(new HttpContextAccessor()),
            ambient,
            ConServiceAccount("service-account-token"));

        var client = ConErpClient(chained);

        // WHEN
        await client.ListarEquiposAsync(filtro: null);

        // THEN
        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer jwt-tenant-7");
    }

    // ─── §6.3 Fallback service-account ────────────────────────────────────

    [Fact]
    public async Task Sin_HTTP_ni_envelope_cae_a_service_account()
    {
        // GIVEN: sin HttpContext, sin ambient seteado, solo service-account.
        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(new HttpContextAccessor()),
            new AmbientBearerTokenAccessor(),
            ConServiceAccount("service-account-fallback"));

        var client = ConErpClient(chained);

        // WHEN
        await client.ListarEquiposAsync(filtro: null);

        // THEN
        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer service-account-fallback");
    }

    // ─── §6.4 Envelope con header vacío cae al service-account ────────────

    [Fact]
    public async Task Ambient_con_string_vacio_cae_a_service_account()
    {
        var ambient = new AmbientBearerTokenAccessor();
        using var _ = ambient.SetForCurrentScope(string.Empty);

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(new HttpContextAccessor()),
            ambient,
            ConServiceAccount("service-account-token"));

        var client = ConErpClient(chained);

        await client.ListarEquiposAsync(filtro: null);

        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer service-account-token");
    }

    // ─── §6.5 Fail-closed ─────────────────────────────────────────────────

    [Fact]
    public async Task Sin_ningun_token_lanza_BearerTokenAusenteException_antes_de_salir_al_ERP()
    {
        // GIVEN: todos los accessors vacíos.
        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(new HttpContextAccessor()),
            new AmbientBearerTokenAccessor(),
            ConServiceAccount(string.Empty));

        var client = ConErpClient(chained);

        // WHEN/THEN: la llamada lanza ANTES de salir al ERP.
        var act = () => client.ListarEquiposAsync(filtro: null);
        await act.Should().ThrowAsync<BearerTokenAusenteException>();

        // AND: WireMock no recibe request (la verificación de fail-closed).
        _server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task DelegatingHandler_reescribe_Authorization_aunque_HttpClient_lo_tenga_setado()
    {
        // MT3-INV-4: si por error queda http.DefaultRequestHeaders.Authorization fijo
        // en Program.cs, el DelegatingHandler debe sobrescribirlo con el del accessor.
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer jwt-correcto";
        var httpAccessor = new HttpContextAccessor { HttpContext = http };

        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(httpAccessor),
            new AmbientBearerTokenAccessor(),
            ConServiceAccount("service"));

        // HttpClient con header default "stale" (simula la regresión).
        var handler = new BearerTokenPropagationHandler(chained)
        {
            InnerHandler = new HttpClientHandler(),
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_server.Urls[0] + "/"),
        };
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer jwt-stale-default");

        var erpClient = new MaquinariaErpClient(httpClient);

        await erpClient.ListarEquiposAsync(filtro: null);

        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer jwt-correcto");
        auth.Should().NotContain(v => v == "Bearer jwt-stale-default");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private MaquinariaErpClient ConErpClient(IBearerTokenAccessor accessor)
    {
        var handler = new BearerTokenPropagationHandler(accessor)
        {
            InnerHandler = new HttpClientHandler(),
        };
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(_server.Urls[0] + "/"),
        };
        return new MaquinariaErpClient(http);
    }

    private static ServiceAccountBearerTokenAccessor ConServiceAccount(string token) =>
        new(Options.Create(new MaquinariaErpOptions { JwtToken = token }));
}
