using System.Net;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Inspecciones.Infrastructure.Tests.Erp.Listeners;

/// <summary>
/// Tests Given/When/Then del listener <see cref="SincronizarDictamenVigenteListener"/>.
/// Cada test cubre un escenario de §6 de la spec erp-3.
///
/// Stack:
///   - WireMock.Net para emular Maquinaria_V4 (M-W-1 PUT /api/equipos/{id}/dictamen-vigente).
///   - <see cref="FakeInspeccionReader"/> para simular AggregateStreamAsync sin Postgres.
///     Opción B elegida por coherencia con el patrón erp-2 (sin Testcontainers en tests
///     de comportamiento del listener) — decisión documentada en red-notes.md.
///   - El listener se invoca directamente en proceso (sin Wolverine host).
/// </summary>
public sealed class SincronizarDictamenVigenteListenerTests : IDisposable
{
    private readonly WireMockServer _wiremock;
    private readonly HttpClient _httpClient;
    private readonly MaquinariaErpClient _erpClient;

    // Fixtures de IDs
    private static readonly Guid IdInspeccion1 = Guid.Parse("33333333-0000-7000-8000-000000000001");
    private static readonly Guid IdInspeccion2 = Guid.Parse("33333333-0000-7000-8000-000000000002");
    private static readonly Guid IdInspeccion3 = Guid.Parse("33333333-0000-7000-8000-000000000003");
    private static readonly Guid IdInspeccion4 = Guid.Parse("33333333-0000-7000-8000-000000000004");
    private const int EquipoId1 = 1234;
    private const int EquipoId2 = 5678;
    private const int EquipoId3 = 9012;
    private const int EquipoIdDesconocido = 9999;

    // Fixture GPS de firma (requerido en InspeccionFirmada_v1)
    private static readonly UbicacionGps GpsFirma = new(
        Latitud: 4.711m,
        Longitud: -74.072m,
        PrecisionMetros: 10m,
        CapturadoEn: new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero));

    private static readonly DateTimeOffset FirmadaEn =
        new DateTimeOffset(2026, 5, 19, 15, 0, 0, TimeSpan.Zero);

    public SincronizarDictamenVigenteListenerTests()
    {
        _wiremock = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_wiremock.Urls[0] + "/"),
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer service-account-token");
        _erpClient = new MaquinariaErpClient(_httpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _wiremock.Stop();
        _wiremock.Dispose();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string PathDictamen(int equipoId) =>
        $"/api/equipos/{equipoId}/dictamen-vigente";

    private static InspeccionFirmada_v1 EventoFirmada(Guid inspeccionId) =>
        new(
            InspeccionId: inspeccionId,
            FirmadoPor: "tecnico-01",
            FirmaUri: "https://blob.example.com/firma-01.png",
            UbicacionFirma: GpsFirma,
            FirmadaEn: FirmadaEn);

    private static Inspeccion AgregateCon(int equipoId, DictamenOperacion dictamen)
    {
        // Construye un aggregate con el estado mínimo necesario para el listener:
        // EquipoId y Dictamen poblados via Reconstruir desde stream.
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
                FirmaUri: "https://blob.example.com/firma.png",
                UbicacionFirma: GpsFirma,
                FirmadaEn: FirmadaEn),
        };
        return Inspeccion.Reconstruir(stream);
    }

    private SincronizarDictamenVigenteListener CrearListenerCon(IInspeccionReader reader) =>
        new(reader, _erpClient);

    private static void StubbearPut200(WireMockServer server, int equipoId) =>
        server
            .Given(Request.Create().WithPath(PathDictamen(equipoId)).UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    Codigo = equipoId,
                    Estado = 0,
                    EstadoUsuario = 0,
                    EstadoFecha = "2026-05-19T15:00:00Z",
                }));

    // ─── §6.1 Happy path — dictamen PuedeOperar ────────────────────────────

    /// <summary>§6.1 — Dictamen PuedeOperar mapeado a Estado=0 en el cuerpo HTTP.</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_dictamen_PuedeOperar_envia_Estado_0()
    {
        // Given: aggregate con Dictamen=PuedeOperar, EquipoId=1234
        var aggregate = AgregateCon(EquipoId1, DictamenOperacion.PuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion1, aggregate);
        StubbearPut200(_wiremock, EquipoId1);
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion1));

        // Then: completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y el adapter recibio exactamente 1 PUT con Estado=0
        var entries = _wiremock.LogEntries;
        entries.Should().HaveCount(1);
        entries.Single().RequestMessage.Path.Should().Be(PathDictamen(EquipoId1));
        entries.Single().RequestMessage.Method.Should().Be("PUT");
        var body = entries.Single().RequestMessage.Body ?? string.Empty;
        body.Should().Contain("\"Estado\":0");
    }

    // ─── §6.2 Happy path — dictamen ConRestriccion ────────────────────────

    /// <summary>§6.2 — Dictamen ConRestriccion mapeado a Estado=1 en el cuerpo HTTP.</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_dictamen_ConRestriccion_envia_Estado_1()
    {
        // Given: aggregate con Dictamen=ConRestriccion, EquipoId=5678
        var aggregate = AgregateCon(EquipoId2, DictamenOperacion.ConRestriccion);
        var reader = new FakeInspeccionReader(IdInspeccion2, aggregate);
        StubbearPut200(_wiremock, EquipoId2);
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion2));

        // Then: completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y el body HTTP contiene Estado=1
        var body = _wiremock.LogEntries.Single().RequestMessage.Body ?? string.Empty;
        body.Should().Contain("\"Estado\":1");
    }

    // ─── §6.3 Happy path — dictamen NoPuedeOperar ────────────────────────

    /// <summary>§6.3 — Dictamen NoPuedeOperar mapeado a Estado=2 en el cuerpo HTTP.</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_dictamen_NoPuedeOperar_envia_Estado_2()
    {
        // Given: aggregate con Dictamen=NoPuedeOperar, EquipoId=9012
        var aggregate = AgregateCon(EquipoId3, DictamenOperacion.NoPuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion3, aggregate);
        StubbearPut200(_wiremock, EquipoId3);
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion3));

        // Then: completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y el body HTTP contiene Estado=2
        var body = _wiremock.LogEntries.Single().RequestMessage.Body ?? string.Empty;
        body.Should().Contain("\"Estado\":2");
    }

    // ─── §6.4 Idempotencia — replay del outbox (INV-L4) ──────────────────

    /// <summary>§6.4 — Segundo PUT al mismo equipo con mismo dictamen: exito sin efecto colateral (INV-L4).</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_replay_outbox_es_inocuo_last_write_wins_INV_L4()
    {
        // Given: ERP stubbea 200 last-write-wins en todas las llamadas
        var aggregate = AgregateCon(EquipoId1, DictamenOperacion.PuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion1, aggregate);
        StubbearPut200(_wiremock, EquipoId1);
        var listener = CrearListenerCon(reader);

        // When: se procesa el mismo evento dos veces (replay del outbox)
        await listener.HandleAsync(EventoFirmada(IdInspeccion1));
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion1));

        // Then: la segunda entrega tambien completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y el ERP recibio exactamente 2 PUTs (no se deduplica en el listener — last-write-wins)
        _wiremock.LogEntries.Should().HaveCount(2);
    }

    // ─── §6.5 ERP 5xx — propaga excepcion para retry Wolverine (ADR-006) ─

    /// <summary>§6.5 — ERP 5xx: el listener lanza excepcion para que Wolverine reintente (ADR-006).</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_erp_5xx_propaga_excepcion_para_retry_Wolverine()
    {
        // Given: ERP siempre responde 503
        var aggregate = AgregateCon(EquipoId1, DictamenOperacion.PuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion1, aggregate);
        _wiremock
            .Given(Request.Create().WithPath(PathDictamen(EquipoId1)).UsingPut())
            .RespondWith(Response.Create().WithStatusCode(503).WithBody("Service Unavailable"));
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion1));

        // Then: lanza excepcion para que Wolverine gestione backoff + dead-letter (ADR-006)
        await act.Should().ThrowAsync<Exception>();

        // Y se realizo exactamente 1 llamada HTTP (Wolverine gestiona el retry externamente)
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.6 ERP 5xx persistente — dead-letter + alerta INV-L2 ─────────

    /// <summary>§6.6 — 5xx persistente: el listener lanza en cada intento; señal de observabilidad INV-L2.</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_erp_5xx_persistente_lanza_excepcion_cada_intento()
    {
        // Given: ERP siempre 500
        var aggregate = AgregateCon(EquipoId1, DictamenOperacion.PuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion1, aggregate);
        _wiremock
            .Given(Request.Create().WithPath(PathDictamen(EquipoId1)).UsingPut())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));
        var listener = CrearListenerCon(reader);

        // When/Then: cada intento lanza — Wolverine es quien acumula intentos y luego dead-lettera
        await Assert.ThrowsAnyAsync<Exception>(() => listener.HandleAsync(EventoFirmada(IdInspeccion1)));
        await Assert.ThrowsAnyAsync<Exception>(() => listener.HandleAsync(EventoFirmada(IdInspeccion1)));

        // Y cada intento generó exactamente 1 llamada HTTP
        _wiremock.LogEntries.Should().HaveCount(2);
    }

    // ─── §6.7 ERP 4xx (400) — no reintenta, dead-letter inmediato INV-L3 ─

    /// <summary>§6.7 — ERP 400 Bad Request: no reintenta, dead-letter inmediato (INV-L3).</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_erp_400_no_reintenta_dead_letter_INV_L3()
    {
        // Given: ERP responde 400 con body de error
        var aggregate = AgregateCon(EquipoId1, DictamenOperacion.PuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion1, aggregate);
        _wiremock
            .Given(Request.Create().WithPath(PathDictamen(EquipoId1)).UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithBodyAsJson(new
                {
                    Codigo = "ESTADO_INVALIDO",
                    Mensaje = "El valor de Estado no es admitido",
                }));
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion1));

        // Then: lanza MaquinariaErpException con StatusCode=BadRequest (dead-letter permanente, INV-L3)
        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Y exactamente 1 llamada HTTP (sin reintentos — INV-L3)
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.8 ERP 4xx (404) — equipo desconocido en MYE, dead-letter INV-L3

    /// <summary>§6.8 — ERP 404 Not Found: equipo desconocido, dead-letter inmediato (INV-L3).</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_erp_404_equipo_desconocido_dead_letter_INV_L3()
    {
        // Given: aggregate con EquipoId=9999, ERP devuelve 404
        var aggregate = AgregateCon(EquipoIdDesconocido, DictamenOperacion.NoPuedeOperar);
        var reader = new FakeInspeccionReader(IdInspeccion4, aggregate);
        _wiremock
            .Given(Request.Create().WithPath(PathDictamen(EquipoIdDesconocido)).UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBodyAsJson(new
                {
                    Codigo = "EQUIPO_NOT_FOUND",
                    Mensaje = "El equipo 9999 no existe en MYE",
                }));
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion4));

        // Then: lanza MaquinariaErpException con StatusCode=NotFound (permanente, INV-L3)
        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Y exactamente 1 llamada HTTP
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.9 Aggregate no reconstruible (PRE-L1 — stream inexistente) ───

    /// <summary>§6.9 — Aggregate nulo (stream no existe): dead-letter inmediato sin llamada HTTP (PRE-L1).</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_aggregate_no_encontrado_dead_letter_inmediato_PRE_L1()
    {
        // Given: el reader devuelve null (stream no existe en Marten)
        var reader = new FakeInspeccionReader(IdInspeccion1, null);
        var listener = CrearListenerCon(reader);
        var eventoConIdInexistente = EventoFirmada(Guid.NewGuid());

        // When
        var act = () => listener.HandleAsync(eventoConIdInexistente);

        // Then: lanza excepcion antes de cualquier llamada HTTP (PRE-L1)
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stream*");

        // Y NO se realizo ninguna llamada HTTP
        _wiremock.LogEntries.Should().BeEmpty();
    }

    // ─── §6.10 Dictamen nulo en aggregate (PRE-L1 — estado corrupto) ─────

    /// <summary>§6.10 — Dictamen null en aggregate (DictamenEstablecido_v1 ausente): dead-letter inmediato (PRE-L1).</summary>
    [Fact]
    public async Task SincronizarDictamenVigente_dictamen_nulo_en_aggregate_dead_letter_inmediato_PRE_L1()
    {
        // Given: aggregate con EquipoId poblado pero Dictamen=null (stream corrupto — falta DictamenEstablecido_v1)
        var aggregateCorrupto = AgregateSoloCon(EquipoId1); // solo InspeccionIniciada_v1 + InspeccionFirmada_v1
        var reader = new FakeInspeccionReader(IdInspeccion1, aggregateCorrupto);
        var listener = CrearListenerCon(reader);

        // When
        var act = () => listener.HandleAsync(EventoFirmada(IdInspeccion1));

        // Then: lanza excepcion (Dictamen==null → estado corrupto, PRE-L1)
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Dictamen*");

        // Y NO se realizo ninguna llamada HTTP
        _wiremock.LogEntries.Should().BeEmpty();
    }

    // ─── §6.11 Dictamen no mapeable (PRE-L3) ─────────────────────────────

    /// <summary>§6.11 — Dictamen no mapeable (valor de enum sin mapeo definido): ArgumentOutOfRangeException (PRE-L3).</summary>
    [Fact]
    public void SincronizarDictamenVigente_dictamen_no_mapeable_lanza_ArgumentOutOfRangeException_PRE_L3()
    {
        // Given: un valor de DictamenOperacion fuera del rango definido (cast directo de int)
        var dictamenInvalido = (DictamenOperacion)99;

        // When: se intenta mapear el valor inválido
        var act = () => SincronizarDictamenVigenteListener.MapearDictamen(dictamenInvalido);

        // Then: lanza ArgumentOutOfRangeException (dead-letter inmediato — PRE-L3)
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ─── Helpers privados ────────────────────────────────────────────────

    /// <summary>
    /// Construye un aggregate con solo <see cref="InspeccionIniciada_v1"/> +
    /// <see cref="InspeccionFirmada_v1"/> pero sin <see cref="DictamenEstablecido_v1"/>.
    /// Representa un stream corrupto donde el dictamen nunca se emitió.
    /// </summary>
    private static Inspeccion AgregateSoloCon(int equipoId)
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
            // NO se emite DictamenEstablecido_v1 — stream corrupto
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "tecnico-01",
                FirmaUri: "https://blob.example.com/firma.png",
                UbicacionFirma: GpsFirma,
                FirmadaEn: FirmadaEn),
        };
        return Inspeccion.Reconstruir(stream);
    }
}

// ─── Fake de IInspeccionReader para tests ────────────────────────────────────

/// <summary>
/// Doble de <see cref="IInspeccionReader"/> para los tests del listener erp-3.
/// Devuelve el aggregate preconstruido para el InspeccionId configurado;
/// devuelve null para cualquier otro id (simula stream inexistente).
/// </summary>
internal sealed class FakeInspeccionReader : IInspeccionReader
{
    private readonly Guid _inspeccionId;
    private readonly Inspeccion? _aggregate;

    public FakeInspeccionReader(Guid inspeccionId, Inspeccion? aggregate)
    {
        _inspeccionId = inspeccionId;
        _aggregate = aggregate;
    }

    public Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var resultado = inspeccionId == _inspeccionId ? _aggregate : null;
        return Task.FromResult(resultado);
    }

    // mt-2: overload tenant-aware. Los tests de erp-3 no validan tenant — solo delegan al método base.
    public Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default) =>
        LeerAsync(inspeccionId, ct);
}
