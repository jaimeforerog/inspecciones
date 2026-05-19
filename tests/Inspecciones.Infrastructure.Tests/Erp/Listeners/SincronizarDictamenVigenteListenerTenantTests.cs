using System.Net;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Wolverine;

namespace Inspecciones.Infrastructure.Tests.Erp.Listeners;

/// <summary>
/// Tests del listener <see cref="SincronizarDictamenVigenteListener"/> verificando
/// la propagación del tenant del envelope Wolverine (mt-2 §6.5 + §6.6 del spec).
///
/// MT2-PRE-2: el listener recibe el <see cref="Envelope"/> de Wolverine y lee su
/// <c>TenantId</c>. Si está ausente, lanza <see cref="TenantRequeridoEnEnvelopeException"/>
/// → dead-letter inmediato. Si está presente, lo pasa explícitamente a
/// <see cref="IInspeccionReader.LeerAsync(Guid, string, CancellationToken)"/>
/// (nueva overload tenant-aware introducida en mt-2) para que la lectura del aggregate
/// sea tenant-aware.
///
/// Los tests son in-process (sin Wolverine host real) — el contrato del listener
/// se valida construyendo un <see cref="Envelope"/> manualmente y pasándoselo.
/// </summary>
public sealed class SincronizarDictamenVigenteListenerTenantTests : IDisposable
{
    private static readonly Guid IdInspeccion = Guid.Parse("44444444-0000-7000-8000-000000000001");
    private const int EquipoId = 4321;

    private static readonly UbicacionGps GpsFirma = new(
        Latitud: 4.711m,
        Longitud: -74.072m,
        PrecisionMetros: 10m,
        CapturadoEn: new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero));

    private static readonly DateTimeOffset FirmadaEn =
        new(2026, 5, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly WireMockServer _wiremock;
    private readonly HttpClient _httpClient;
    private readonly MaquinariaErpClient _erpClient;

    public SincronizarDictamenVigenteListenerTenantTests()
    {
        _wiremock = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_wiremock.Urls[0] + "/") };
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer service-account-token");
        _erpClient = new MaquinariaErpClient(_httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _wiremock.Stop();
        _wiremock.Dispose();
    }

    [Fact]
    public async Task Listener_lee_el_tenant_del_envelope_y_lo_propaga_al_reader_del_aggregate()
    {
        // GIVEN: envelope con tenant "7" — emulación de Wolverine despachando un evento
        // que el outbox persistió con tenant_id="7".
        var envelope = new Envelope { TenantId = "7" };
        var aggregate = AgregateCon(EquipoId, DictamenOperacion.PuedeOperar);
        var reader = new FakeTenantAwareInspeccionReader(IdInspeccion, aggregate);
        StubbearPut200(_wiremock, EquipoId);
        var listener = new SincronizarDictamenVigenteListener(reader, _erpClient);

        // WHEN: el listener recibe el evento más el envelope.
        await listener.HandleAsync(EventoFirmada(IdInspeccion), envelope, CancellationToken.None);

        // THEN: el reader fue invocado con el inspeccionId + el tenantId del envelope.
        reader.UltimoTenantId.Should().Be("7");
        reader.UltimoInspeccionId.Should().Be(IdInspeccion);

        // AND: el ERP fue invocado exactamente 1 vez con el path del equipoId del aggregate.
        _wiremock.LogEntries.Should().HaveCount(1);
        _wiremock.LogEntries.Single().RequestMessage.Path.Should().Be($"/api/equipos/{EquipoId}/dictamen-vigente");
    }

    [Fact]
    public async Task Listener_sin_tenant_en_envelope_lanza_TenantRequeridoEnEnvelopeException()
    {
        // GIVEN: envelope con TenantId = null (caso patológico — mensaje legacy o bug).
        var envelope = new Envelope { TenantId = null };
        var reader = new FakeTenantAwareInspeccionReader(IdInspeccion, AgregateCon(EquipoId, DictamenOperacion.PuedeOperar));
        var listener = new SincronizarDictamenVigenteListener(reader, _erpClient);

        // WHEN/THEN: el listener detecta envelope sin tenant y lanza.
        Func<Task> act = () => listener.HandleAsync(EventoFirmada(IdInspeccion), envelope, CancellationToken.None);

        await act.Should().ThrowAsync<TenantRequeridoEnEnvelopeException>()
            .Where(ex => ex.NombreListener.Contains("SincronizarDictamenVigente")
                         && ex.CodigoError == "TENANT-ENVELOPE-AUSENTE");

        // AND: el ERP NO fue invocado.
        _wiremock.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Listener_sin_tenant_en_envelope_no_invoca_al_reader_del_aggregate()
    {
        var envelope = new Envelope { TenantId = null };
        var reader = new FakeTenantAwareInspeccionReader(IdInspeccion, AgregateCon(EquipoId, DictamenOperacion.PuedeOperar));
        var listener = new SincronizarDictamenVigenteListener(reader, _erpClient);

        Func<Task> act = () => listener.HandleAsync(EventoFirmada(IdInspeccion), envelope, CancellationToken.None);
        await act.Should().ThrowAsync<TenantRequeridoEnEnvelopeException>();

        reader.VecesInvocado.Should().Be(0);
    }

    [Fact]
    public async Task Listener_con_tenant_vacio_string_es_tratado_como_ausente()
    {
        // String vacío también debe lanzar — no es un tenant válido.
        var envelope = new Envelope { TenantId = string.Empty };
        var reader = new FakeTenantAwareInspeccionReader(IdInspeccion, AgregateCon(EquipoId, DictamenOperacion.PuedeOperar));
        var listener = new SincronizarDictamenVigenteListener(reader, _erpClient);

        Func<Task> act = () => listener.HandleAsync(EventoFirmada(IdInspeccion), envelope, CancellationToken.None);
        await act.Should().ThrowAsync<TenantRequeridoEnEnvelopeException>();
        _wiremock.LogEntries.Should().BeEmpty();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static InspeccionFirmada_v1 EventoFirmada(Guid inspeccionId) =>
        new(
            InspeccionId: inspeccionId,
            FirmadoPor: "tecnico-01",
            FirmaUri: "https://blob.example.com/firma-01.png",
            UbicacionFirma: GpsFirma,
            FirmadaEn: FirmadaEn);

    private static Inspeccion AgregateCon(int equipoId, DictamenOperacion dictamen)
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
                DiagnosticoFinal: "Inspección sin hallazgos críticos",
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

    private static void StubbearPut200(WireMockServer server, int equipoId) =>
        server
            .Given(Request.Create().WithPath($"/api/equipos/{equipoId}/dictamen-vigente").UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    Codigo = equipoId,
                    Estado = 0,
                    EstadoUsuario = 0,
                    EstadoFecha = "2026-05-19T15:00:00Z",
                }));

    /// <summary>
    /// Doble del puerto <see cref="IInspeccionReader"/> que captura el tenant pasado
    /// por la overload tenant-aware (mt-2). El listener debe invocar
    /// <see cref="LeerAsync(Guid, string, CancellationToken)"/> cuando el envelope
    /// trae <c>TenantId</c>, NO la overload sin tenant.
    /// </summary>
    private sealed class FakeTenantAwareInspeccionReader : IInspeccionReader
    {
        private readonly Guid _inspeccionId;
        private readonly Inspeccion? _aggregate;

        public FakeTenantAwareInspeccionReader(Guid inspeccionId, Inspeccion? aggregate)
        {
            _inspeccionId = inspeccionId;
            _aggregate = aggregate;
        }

        public string? UltimoTenantId { get; private set; }
        public Guid? UltimoInspeccionId { get; private set; }
        public int VecesInvocado { get; private set; }

        public Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default)
        {
            // No-arg-tenant overload: NO se espera que el listener tenant-aware la use.
            // Si la usa, el test del happy path falla (UltimoTenantId será null).
            VecesInvocado++;
            UltimoInspeccionId = inspeccionId;
            UltimoTenantId = null;
            var resultado = inspeccionId == _inspeccionId ? _aggregate : null;
            return Task.FromResult(resultado);
        }

        public Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default)
        {
            VecesInvocado++;
            UltimoInspeccionId = inspeccionId;
            UltimoTenantId = tenantId;
            var resultado = inspeccionId == _inspeccionId ? _aggregate : null;
            return Task.FromResult(resultado);
        }
    }
}
