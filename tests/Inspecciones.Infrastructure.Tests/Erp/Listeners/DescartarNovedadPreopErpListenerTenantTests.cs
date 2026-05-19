using System.Net;
using FluentAssertions;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Wolverine;

namespace Inspecciones.Infrastructure.Tests.Erp.Listeners;

/// <summary>
/// Tests rojos del slice mt-3 §6.7 (cierre FU-57) + verificación de propagación
/// del Bearer al ERP via envelope.
///
/// El listener <see cref="DescartarNovedadPreopErpListener"/> gana en mt-3 una
/// overload <c>HandleAsync(NovedadPreopDescartada_v1, Envelope, ct)</c> que:
/// 1. Lee <c>envelope.Headers["X-Forwarded-Authorization"]</c> y lo setea en
///    <see cref="AmbientBearerTokenAccessor"/> para que el DelegatingHandler lo
///    propague al ERP.
/// 2. Enriquece los logs estructurados con <c>envelope.TenantId</c> (FU-57).
/// </summary>
public sealed class DescartarNovedadPreopErpListenerTenantTests : IDisposable
{
    private static readonly Guid IdInspeccion = Guid.Parse("66666666-0000-7000-8000-000000000001");
    private const int NovedadId = 4242;

    private readonly WireMockServer _server;

    public DescartarNovedadPreopErpListenerTenantTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task Listener_propaga_JWT_del_envelope_al_ERP_via_AmbientBearer()
    {
        // GIVEN: envelope con TenantId + X-Forwarded-Authorization.
        var envelope = new Envelope { TenantId = "7" };
        envelope.Headers["X-Forwarded-Authorization"] = "Bearer jwt-envelope-7";

        StubbearPost200();

        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient);
        var listener = new DescartarNovedadPreopErpListener(erp);

        // WHEN: el listener procesa con envelope.
        await listener.HandleAsync(EventoDescartada(), envelope, CancellationToken.None);

        // THEN: WireMock recibe el JWT del envelope, NO el service-account.
        _server.LogEntries.Should().HaveCount(1);
        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer jwt-envelope-7");
    }

    [Fact]
    public async Task Listener_sin_X_Forwarded_Authorization_cae_a_service_account_via_chain()
    {
        // GIVEN: envelope con tenant pero sin X-Forwarded-Authorization
        // (caso patológico: mensaje legacy publicado antes de mt-3).
        var envelope = new Envelope { TenantId = "7" };

        StubbearPost200();

        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient, serviceAccount: "service-fallback");
        var listener = new DescartarNovedadPreopErpListener(erp);

        await listener.HandleAsync(EventoDescartada(), envelope, CancellationToken.None);

        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer service-fallback");
    }

    [Fact]
    public async Task Listener_overload_legacy_sin_envelope_sigue_funcionando_compat()
    {
        // GIVEN: invocación legacy SIN envelope (compat con tests pre-mt-3).
        StubbearPost200();
        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient, serviceAccount: "service-token");
        var listener = new DescartarNovedadPreopErpListener(erp);

        // Overload sin envelope: el ambient no se setea → cae al service-account.
        await listener.HandleAsync(EventoDescartada(), CancellationToken.None);

        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer service-token");
    }

    [Fact]
    public async Task Listener_log_estructurado_incluye_TenantId_del_envelope_en_fallo_5xx()
    {
        // FU-57 cierre: el log estructurado debe incluir TenantId cuando viene del envelope.
        var envelope = new Envelope { TenantId = "7" };
        envelope.Headers["X-Forwarded-Authorization"] = "Bearer jwt-7";

        _server
            .Given(Request.Create().WithPath("/api/preoperacional-fallas/cerrar").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("{\"Codigo\":\"ERR_INTERNO\",\"Mensaje\":\"db down\"}"));

        var captureLogger = new CaptureLogger<DescartarNovedadPreopErpListener>();
        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient);
        var listener = new DescartarNovedadPreopErpListener(erp, captureLogger);

        Func<Task> act = () => listener.HandleAsync(EventoDescartada(), envelope, CancellationToken.None);

        await act.Should().ThrowAsync<MaquinariaErpException>();

        // El log estructurado debe haber capturado el tenantId.
        captureLogger.Entradas.Should().NotBeEmpty();
        captureLogger.Entradas.Should().Contain(e => e.Contains("TenantId", StringComparison.Ordinal) && e.Contains('7'));
    }

    [Fact]
    public async Task Ambient_se_limpia_despues_de_HandleAsync_aunque_lance()
    {
        // GIVEN: envelope con JWT, ERP responde 4xx → listener relanza.
        var envelope = new Envelope { TenantId = "7" };
        envelope.Headers["X-Forwarded-Authorization"] = "Bearer jwt-7";

        _server
            .Given(Request.Create().WithPath("/api/preoperacional-fallas/cerrar").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody("{\"Codigo\":\"BAD\",\"Mensaje\":\"x\"}"));

        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient);
        var listener = new DescartarNovedadPreopErpListener(erp);

        Func<Task> act = () => listener.HandleAsync(EventoDescartada(), envelope, CancellationToken.None);
        await act.Should().ThrowAsync<MaquinariaErpException>();

        // THEN: el ambient quedó limpio post-handle.
        ambient.ObtenerBearerToken().Should().BeNull();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static NovedadPreopDescartada_v1 EventoDescartada() =>
        new(
            InspeccionId: IdInspeccion,
            NovedadId: NovedadId,
            MotivoDescarte: "Falsa alarma",
            DescartadaPor: "tecnico-01",
            DescartadaEn: new DateTimeOffset(2026, 5, 19, 16, 0, 0, TimeSpan.Zero));

    private void StubbearPost200() =>
        _server
            .Given(Request.Create().WithPath("/api/preoperacional-fallas/cerrar").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { cerradasAhora = 1, yaCerradas = 0 }));

    private MaquinariaErpClient ConErpClientConAmbient(
        AmbientBearerTokenAccessor ambient,
        string? serviceAccount = null)
    {
        var service = new ServiceAccountBearerTokenAccessor(
            Options.Create(new MaquinariaErpOptions { JwtToken = serviceAccount ?? string.Empty }));
        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            ambient,
            service);
        var handler = new BearerTokenPropagationHandler(chained)
        {
            InnerHandler = new HttpClientHandler(),
        };
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(_server.Urls[0] + "/"),
        };
        return new MaquinariaErpClient(http);
    }

    /// <summary>
    /// Logger que captura cada entrada formateada como string para inspección
    /// en assertions. No es un mock — es un sink simple.
    /// </summary>
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Entradas { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Captura el string formateado + los valores estructurados.
            var mensaje = formatter(state, exception);
            // Incluye los KV del state para que el assertion pueda buscar por nombre.
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kv)
            {
                var extra = string.Join(" ", kv.Select(p => $"{p.Key}={p.Value}"));
                Entradas.Add($"{mensaje} | {extra}");
            }
            else
            {
                Entradas.Add(mensaje);
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
