using System.Net;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Listeners;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Inspecciones.Infrastructure.Tests.Erp.Listeners;

/// <summary>
/// Tests Given/When/Then del listener <see cref="DescartarNovedadPreopErpListener"/>.
/// Cada test cubre un escenario de §6 de la spec erp-2.
///
/// Stack:
///   - WireMock.Net para emular Maquinaria_V4 (P-6 /preoperacional-fallas/cerrar).
///   - El listener se invoca directamente en proceso (sin Wolverine host) para
///     verificar las reglas de negocio del adaptador. Los tests de Wolverine retry
///     policy y dead-letter (§6.3, §6.4) usan WireMock con escenarios secuenciales
///     y verifican el comportamiento mediante el número de llamadas HTTP recibidas.
///   - Sin Testcontainers/Postgres — el listener no toca el event store.
/// </summary>
public sealed class NovedadPreopDescartadaListenerTests : IDisposable
{
    private readonly WireMockServer _wiremock;
    private readonly HttpClient _httpClient;
    private readonly MaquinariaErpClient _erpClient;
    private readonly DescartarNovedadPreopErpListener _listener;

    // Fixture estándar de evento bien formado
    private static readonly DateTimeOffset FechaDescarte =
        new DateTimeOffset(2026, 5, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly NovedadPreopDescartada_v1 EventoEstandar = new(
        InspeccionId: Guid.Parse("11111111-0000-7000-8000-000000000001"),
        NovedadId: 9001,
        MotivoDescarte: "Cerrado por ana.gomez el 2026-05-19 10:00 UTC desde Inspecciones",
        DescartadaPor: "ana.gomez",
        DescartadaEn: FechaDescarte);

    private const string PathCerrar = "/api/preoperacional-fallas/cerrar";

    public NovedadPreopDescartadaListenerTests()
    {
        _wiremock = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_wiremock.Urls[0] + "/"),
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer service-account-token");
        _erpClient = new MaquinariaErpClient(_httpClient);
        _listener = new DescartarNovedadPreopErpListener(_erpClient);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _wiremock.Stop();
        _wiremock.Dispose();
    }

    // ─── §6.1 Happy path ────────────────────────────────────────────────────

    /// <summary>§6.1 — ERP responde 200 cerradasAhora:1.</summary>
    [Fact]
    public async Task Listener_publica_cierre_a_Maquinaria_cuando_NovedadPreopDescartada_v1_emitida()
    {
        // Given: WireMock responde 200 OK con cerradasAhora:1
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    cerradasAhora = 1,
                    yaCerradas = 0,
                    total = 1,
                    podIdsCerradosAhora = new[] { 9001 },
                }));

        // When: el listener procesa el evento
        var act = () => _listener.HandleAsync(EventoEstandar);

        // Then: el listener completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y el adapter recibio exactamente 1 llamada HTTP
        var requests = _wiremock.LogEntries;
        requests.Should().HaveCount(1);
        requests.Single().RequestMessage.Path.Should().Be(PathCerrar);
        requests.Single().RequestMessage.Method.Should().Be("POST");
    }

    /// <summary>§6.1 — El body enviado a Maquinaria mapea podIds y observaciones correctamente.</summary>
    [Fact]
    public async Task Listener_envia_podIds_y_observaciones_correctos_en_el_body_HTTP()
    {
        // Given
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    cerradasAhora = 1,
                    yaCerradas = 0,
                    total = 1,
                    podIdsCerradosAhora = new[] { 9001 },
                }));

        // When
        await _listener.HandleAsync(EventoEstandar);

        // Then: el body debe incluir podIds:[9001] y observaciones correctas
        var entry = _wiremock.LogEntries.Single();
        var body = entry.RequestMessage.Body ?? string.Empty;
        body.Should().Contain("9001");
        body.Should().Contain("Cerrado por ana.gomez el 2026-05-19 10:00 UTC desde Inspecciones");
    }

    // ─── §6.2 Idempotencia 200 yaCerradas ───────────────────────────────────

    /// <summary>§6.2 — ERP devuelve 200 yaCerradas:1, tratado como exito silencioso (INV-L4).</summary>
    [Fact]
    public async Task Listener_trata_200_ya_cerradas_como_exito_silencioso_INV_L4()
    {
        // Given: el PODId ya estaba cerrado, ERP responde yaCerradas:1
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    cerradasAhora = 0,
                    yaCerradas = 1,
                    total = 1,
                    podIdsCerradosAhora = Array.Empty<int>(),
                }));

        var eventoSegundaEntrega = EventoEstandar with { };

        // When: el listener procesa el mismo evento por segunda vez
        var act = () => _listener.HandleAsync(eventoSegundaEntrega);

        // Then: completa sin excepcion (idempotencia natural del ERP)
        await act.Should().NotThrowAsync();

        // Y no se reintento (exactamente 1 llamada)
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.7 Idempotencia 409 YA_CERRADO ───────────────────────────────────

    /// <summary>§6.7 — ERP devuelve 409 YA_CERRADO, tratado como exito (Decision D-1).</summary>
    [Fact]
    public async Task Listener_trata_409_YA_CERRADO_como_exito_silencioso_D1()
    {
        // Given: ERP responde 409 con codigo YA_CERRADO
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(409)
                .WithBodyAsJson(new
                {
                    Codigo = "YA_CERRADO",
                    Mensaje = "La novedad ya fue cerrada",
                }));

        // When
        var act = () => _listener.HandleAsync(EventoEstandar);

        // Then: el listener trata 409 YA_CERRADO como exito — no lanza excepcion
        await act.Should().NotThrowAsync();

        // Y exactamente 1 llamada (sin reintentos)
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.3 Retry 5xx transitorio ─────────────────────────────────────────

    /// <summary>§6.3 — 5xx transitorio: listener reintenta y termina exitoso tras 3 fallos.</summary>
    [Fact]
    public async Task Listener_erp_5xx_transitorio_reintenta_hasta_exito()
    {
        // Given: WireMock devuelve 503 en los primeros 2 intentos, luego 200
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .InScenario("retry-5xx")
            .WillSetStateTo("fallo-1")
            .RespondWith(Response.Create().WithStatusCode(503));

        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .InScenario("retry-5xx")
            .WhenStateIs("fallo-1")
            .WillSetStateTo("fallo-2")
            .RespondWith(Response.Create().WithStatusCode(503));

        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .InScenario("retry-5xx")
            .WhenStateIs("fallo-2")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    cerradasAhora = 1,
                    yaCerradas = 0,
                    total = 1,
                    podIdsCerradosAhora = new[] { 9001 },
                }));

        // When: el listener procesa el evento — se espera que Wolverine reintente
        // En test directo, el listener debe propagar la excepcion 5xx en el primer
        // intento para que Wolverine gestione el retry. Verificamos que lanza
        // para el primer intento y que al reinvocar con exito completa.
        // Primer intento: falla (503)
        await Assert.ThrowsAnyAsync<Exception>(() => _listener.HandleAsync(EventoEstandar));
        // Segundo intento: falla (503)
        await Assert.ThrowsAnyAsync<Exception>(() => _listener.HandleAsync(EventoEstandar));
        // Tercer intento: exito
        var act = () => _listener.HandleAsync(EventoEstandar);

        // Then: el tercer intento completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y se recibieron exactamente 3 llamadas HTTP
        _wiremock.LogEntries.Should().HaveCount(3);
    }

    // ─── §6.4 5xx persistente — dead-letter ─────────────────────────────────

    /// <summary>§6.4 — 5xx persistente: el listener lanza excepcion en cada intento (Wolverine lo envia a dead-letter).</summary>
    [Fact]
    public async Task Listener_erp_5xx_persistente_lanza_excepcion_para_dead_letter()
    {
        // Given: ERP siempre devuelve 503
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503).WithBody("Service Unavailable"));

        // When: se invoca el listener (Wolverine lo reintentara 4 veces antes de dead-letter)
        var act = () => _listener.HandleAsync(EventoEstandar);

        // Then: el listener lanza una excepcion relanzable para que Wolverine gestione el retry
        // (tras agotar reintentos, Wolverine envia a dead-letter)
        await act.Should().ThrowAsync<Exception>();

        // Y se realizo exactamente 1 llamada HTTP en este intento
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.1 MotivoDescarte null — normalización ?? string.Empty ───────────

    /// <summary>
    /// §6.1 — MotivoDescarte null en el evento se normaliza a string vacío antes de
    /// enviarlo al ERP (rama ?? string.Empty, línea 49 del listener).
    /// Cubre el caso de eventos legacy o migraciones donde el campo llegue como null.
    /// </summary>
    [Fact]
    public async Task Listener_envia_observaciones_vacias_cuando_MotivoDescarte_es_null()
    {
        // Given: WireMock responde 200 OK
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    cerradasAhora = 1,
                    yaCerradas = 0,
                    total = 1,
                    podIdsCerradosAhora = new[] { 9001 },
                }));

        // El evento llega con MotivoDescarte=null (caso legacy / migración de datos)
        var eventoSinMotivo = EventoEstandar with { MotivoDescarte = null! };

        // When
        var act = () => _listener.HandleAsync(eventoSinMotivo);

        // Then: el listener completa sin excepcion
        await act.Should().NotThrowAsync();

        // Y el body HTTP contiene Observaciones como cadena vacía (normalización ?? "")
        var entry = _wiremock.LogEntries.Single();
        var body = entry.RequestMessage.Body ?? string.Empty;
        // El serializer usa PascalCase — verificamos campo presente y vacío (no null)
        body.Should().Contain("\"Observaciones\":\"\"");
        body.Should().NotContain("null");
        // El body incluye el podId correcto
        body.Should().Contain("9001");
    }

    // ─── §6.5 4xx sin retry — 400 Bad Request ───────────────────────────────

    /// <summary>§6.5 — 400 Bad Request: no reintenta, dead-letter inmediato (INV-L3).</summary>
    [Fact]
    public async Task Listener_erp_400_no_reintenta_va_a_dead_letter_INV_L3()
    {
        // Given: ERP responde 400 con codigo PAYLOAD_INVALIDO
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithBodyAsJson(new
                {
                    Codigo = "PAYLOAD_INVALIDO",
                    Mensaje = "observaciones no puede estar vacio",
                }));

        var eventoCon400 = EventoEstandar with { MotivoDescarte = string.Empty };

        // When
        var act = () => _listener.HandleAsync(eventoCon400);

        // Then: el listener lanza MaquinariaErpException no reintentable (dead-letter inmediato — INV-L3)
        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Y exactamente 1 llamada HTTP (sin reintentos — INV-L3)
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.6 4xx sin retry — 404 Not Found ─────────────────────────────────

    /// <summary>§6.6 — 404 Not Found: no reintenta, dead-letter inmediato (INV-L3).</summary>
    [Fact]
    public async Task Listener_erp_404_no_reintenta_va_a_dead_letter_INV_L3()
    {
        // Given: el PODId no existe en el ERP
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBodyAsJson(new
                {
                    Codigo = "POD_NOT_FOUND",
                    Mensaje = "El POD 99999 no existe",
                }));

        var eventoConPodInexistente = EventoEstandar with { NovedadId = 99999 };

        // When
        var act = () => _listener.HandleAsync(eventoConPodInexistente);

        // Then: lanza MaquinariaErpException permanente (sin retry — INV-L3)
        var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Y exactamente 1 llamada HTTP
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.7 409 codigo desconocido — tratado como 4xx permanente ──────────

    /// <summary>§6.7b — 409 con codigo distinto de YA_CERRADO: dead-letter inmediato (Decision D-1).</summary>
    [Fact]
    public async Task Listener_trata_409_codigo_desconocido_como_error_permanente_D1()
    {
        // Given: ERP responde 409 con un codigo que NO es YA_CERRADO
        _wiremock
            .Given(Request.Create().WithPath(PathCerrar).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(409)
                .WithBodyAsJson(new
                {
                    Codigo = "CONFLICTO_REAL",
                    Mensaje = "La novedad esta bloqueada por otro proceso",
                }));

        // When
        var act = () => _listener.HandleAsync(EventoEstandar);

        // Then: 409 con codigo no reconocido como idempotencia → error permanente
        await act.Should().ThrowAsync<Exception>();

        // Y exactamente 1 llamada (sin reintentos — es error permanente, no transitorio)
        _wiremock.LogEntries.Should().HaveCount(1);
    }

    // ─── §6.8 Evento malformado — PRE-L1 ────────────────────────────────────

    /// <summary>§6.8 — Evento con NovedadId:0 falla PRE-L1 antes del HTTP call.</summary>
    [Fact]
    public async Task Listener_evento_malformado_NovedadId_cero_dead_letter_inmediato_PRE_L1()
    {
        // Given: evento con NovedadId=0 (invalido segun PRE-L1: NovedadId debe ser > 0)
        var eventoMalformado = new NovedadPreopDescartada_v1(
            InspeccionId: Guid.Parse("22222222-0000-7000-8000-000000000002"),
            NovedadId: 0,
            MotivoDescarte: null!,
            DescartadaPor: "ana.gomez",
            DescartadaEn: FechaDescarte);

        // When
        var act = () => _listener.HandleAsync(eventoMalformado);

        // Then: PRE-L1 falla — excepcion lanzada antes de cualquier llamada HTTP
        await act.Should().ThrowAsync<Exception>();

        // Y NO se realizo ninguna llamada HTTP al ERP
        _wiremock.LogEntries.Should().BeEmpty();
    }
}
