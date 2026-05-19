using FluentAssertions;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Wolverine;

namespace Inspecciones.Infrastructure.Tests.Erp.Listeners;

/// <summary>
/// Tests rojos del slice mt-3 §6.2.
///
/// Verifica que <see cref="SincronizarDictamenVigenteListener"/> propaga el
/// Bearer del envelope (header <c>X-Forwarded-Authorization</c>) al ERP via
/// el <see cref="AmbientBearerTokenAccessor"/> seteado al inicio del
/// <c>HandleAsync(evento, envelope, ct)</c>.
///
/// Preserva los 11 tests existentes de
/// <see cref="SincronizarDictamenVigenteListenerTenantTests"/> (que validan
/// la propagación del TenantId al reader).
/// </summary>
public sealed class SincronizarDictamenVigenteBearerPropagationTests : IDisposable
{
    private static readonly Guid IdInspeccion = Guid.Parse("77777777-0000-7000-8000-000000000001");
    private const int EquipoId = 7777;

    private static readonly UbicacionGps GpsFirma = new(
        Latitud: 4.711m,
        Longitud: -74.072m,
        PrecisionMetros: 10m,
        CapturadoEn: new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero));

    private static readonly DateTimeOffset FirmadaEn =
        new(2026, 5, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly WireMockServer _server;

    public SincronizarDictamenVigenteBearerPropagationTests()
    {
        _server = WireMockServer.Start();
        _server
            .Given(Request.Create().WithPath($"/api/equipos/{EquipoId}/dictamen-vigente").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                Codigo = EquipoId,
                Estado = 0,
                EstadoUsuario = 0,
                EstadoFecha = "2026-05-19T15:00:00Z",
            }));
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    [Fact]
    public async Task Listener_propaga_JWT_envelope_al_ERP_PUT_dictamen()
    {
        // GIVEN: envelope con TenantId + X-Forwarded-Authorization.
        var envelope = new Envelope { TenantId = "7" };
        envelope.Headers["X-Forwarded-Authorization"] = "Bearer jwt-tenant-7";

        var aggregate = AggregateCon(EquipoId, DictamenOperacion.PuedeOperar);
        var reader = new FakeTenantAwareReader(IdInspeccion, aggregate);

        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient, serviceAccount: "service-account");
        var listener = new SincronizarDictamenVigenteListener(reader, erp);

        // WHEN
        await listener.HandleAsync(EventoFirmada(IdInspeccion), envelope, CancellationToken.None);

        // THEN: ERP recibió el JWT del envelope.
        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer jwt-tenant-7");

        // AND: reader fue invocado con el tenant correcto (mt-2 preservado).
        reader.UltimoTenantId.Should().Be("7");
    }

    [Fact]
    public async Task Listener_sin_X_Forwarded_Authorization_cae_a_service_account()
    {
        var envelope = new Envelope { TenantId = "7" };
        // sin X-Forwarded-Authorization header

        var aggregate = AggregateCon(EquipoId, DictamenOperacion.PuedeOperar);
        var reader = new FakeTenantAwareReader(IdInspeccion, aggregate);
        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient, serviceAccount: "service-fallback");
        var listener = new SincronizarDictamenVigenteListener(reader, erp);

        await listener.HandleAsync(EventoFirmada(IdInspeccion), envelope, CancellationToken.None);

        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer service-fallback");
    }

    [Fact]
    public async Task Listener_overload_legacy_sin_envelope_sigue_funcionando_compat()
    {
        // §D-MT3-6 — la overload legacy (sin envelope) usa service-account.
        var aggregate = AggregateCon(EquipoId, DictamenOperacion.PuedeOperar);
        var reader = new FakeTenantAwareReader(IdInspeccion, aggregate);
        var ambient = new AmbientBearerTokenAccessor();
        var erp = ConErpClientConAmbient(ambient, serviceAccount: "service-token");
        var listener = new SincronizarDictamenVigenteListener(reader, erp);

        // Overload sin envelope.
        await listener.HandleAsync(EventoFirmada(IdInspeccion), CancellationToken.None);

        var auth = _server.LogEntries.Single().RequestMessage.Headers!["Authorization"];
        auth.Should().ContainSingle(v => v == "Bearer service-token");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static InspeccionFirmada_v1 EventoFirmada(Guid inspeccionId) =>
        new(
            InspeccionId: inspeccionId,
            FirmadoPor: "tecnico-01",
            FirmaUri: "https://blob.example.com/firma-01.png",
            UbicacionFirma: GpsFirma,
            FirmadaEn: FirmadaEn);

    private static Inspeccion AggregateCon(int equipoId, DictamenOperacion dictamen)
    {
        var inspeccionId = Guid.NewGuid();
        var stream = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 10,
                RutinaCodigo: "RUT-01",
                TecnicoIniciador: "tecnico-01",
                ProyectoId: 100,
                Ubicacion: GpsFirma,
                IniciadaEn: FirmadaEn.AddHours(-2),
                FechaReportada: DateOnly.FromDateTime(FirmadaEn.DateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Sin hallazgos críticos",
                EmitidoPor: "tecnico-01",
                EmitidoEn: FirmadaEn.AddMinutes(-5)),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: dictamen,
                Justificacion: "Dictamen técnico establecido",
                EmitidoPor: "tecnico-01",
                EstablecidoEn: FirmadaEn.AddMinutes(-5)),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "tecnico-01",
                FirmaUri: "https://blob.example.com/firma-01.png",
                UbicacionFirma: GpsFirma,
                FirmadaEn: FirmadaEn),
        };
        return Inspeccion.Reconstruir(stream);
    }

    private MaquinariaErpClient ConErpClientConAmbient(
        AmbientBearerTokenAccessor ambient,
        string serviceAccount)
    {
        var service = new ServiceAccountBearerTokenAccessor(
            Options.Create(new MaquinariaErpOptions { JwtToken = serviceAccount }));
        var chained = new ChainedBearerTokenAccessor(
            new HttpContextBearerTokenAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            ambient,
            service);
        var handler = new BearerTokenPropagationHandler(chained) { InnerHandler = new HttpClientHandler() };
        var http = new HttpClient(handler) { BaseAddress = new Uri(_server.Urls[0] + "/") };
        return new MaquinariaErpClient(http);
    }

    private sealed class FakeTenantAwareReader : IInspeccionReader
    {
        private readonly Guid _id;
        private readonly Inspeccion? _aggregate;
        public FakeTenantAwareReader(Guid id, Inspeccion? aggregate) { _id = id; _aggregate = aggregate; }
        public string? UltimoTenantId { get; private set; }

        public Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default)
        {
            UltimoTenantId = null;
            return Task.FromResult(inspeccionId == _id ? _aggregate : null);
        }

        public Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default)
        {
            UltimoTenantId = tenantId;
            return Task.FromResult(inspeccionId == _id ? _aggregate : null);
        }
    }
}
