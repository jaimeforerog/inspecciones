using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/hallazgos</c> contra la
/// app real con Postgres en Testcontainers. Cubre §6.16 (idempotencia ADR-008 via
/// X-Client-Command-Id) y un happy path E2E adicional (equivalente a §6.1 via HTTP).
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class RegistrarHallazgoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn = new(2026, 5, 6, 10, 0, 0, TimeSpan.FromHours(-5));

    private static object RequestBodyManualSinIntervencion(Guid? hallazgoId = null) => new
    {
        hallazgoId   = hallazgoId ?? Guid.NewGuid(),
        origen       = "Manual",
        parteEquipoId = 77,
        novedadPreopOrigenId = (object?)null,
        actividadId  = (object?)null,
        actividadDescripcion = "Revisión visual de manguera",
        novedadTecnica = "Manguera con desgaste leve superficial",
        accionRequerida = "NoRequiereIntervencion",
        accionCorrectiva = (object?)null,
        tipoFallaId  = (object?)null,
        causaFallaId = (object?)null,
        observacionCampo = (object?)null,
        ubicacion    = (object?)null
    };

    /// <summary>
    /// Siembra una inspección en EnEjecucion y el catálogo de equipo con la parte 77.
    /// </summary>
    private async Task<Guid> SembrarInspeccionConEquipo(int equipoId = 4521)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        var evento = new InspeccionIniciada_v1(
            InspeccionId: inspeccionId,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: equipoId,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: "rmartinez",
            ProyectoId: 3,
            Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
            IniciadaEn: CapturadoEn,
            FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null);

        session.Events.StartStream<Inspeccion>(inspeccionId, evento);

        var partes = new List<ParteEquipoLocal>
        {
            new(77, "MANGUERA-HID", "Manguera hidráulica"),
            new(88, "SELLO-HID",   "Sello hidráulico")
        };

        session.Store(new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
            RutinaTecnicaId: 18,
            Partes: partes));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Happy path E2E HTTP — equivalente §6.1 via POST
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_id_hallazgos_happy_path_responde_201_Created()
    {
        // Given: inspección en EnEjecucion y equipo con parte 77
        var inspeccionId = await SembrarInspeccionConEquipo(equipoId: 13001);
        var hallazgoId = Guid.NewGuid();
        var clientCommandId = Guid.NewGuid().ToString();

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos")
        {
            Content = JsonContent.Create(RequestBodyManualSinIntervencion(hallazgoId))
        };
        request.Headers.Add("X-Client-Command-Id", clientCommandId);

        // When
        var response = await client.SendAsync(request);

        // Then: 201 Created con el hallazgoId en el body
        // (falla con NotImplementedException hasta que el green implemente el handler/endpoint)
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaRegistrarHallazgo>();
        resultado.Should().NotBeNull();
        resultado!.HallazgoId.Should().Be(hallazgoId);
        resultado.InspeccionId.Should().Be(inspeccionId);
        resultado.AccionRequerida.Should().Be("NoRequiereIntervencion");
        resultado.RegistradoEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(30));
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.16 Idempotencia — replay con mismo X-Client-Command-Id no duplica evento
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008()
    {
        // Given: inspección en EnEjecucion, primer POST ya ejecutado exitosamente
        var inspeccionId = await SembrarInspeccionConEquipo(equipoId: 13002);
        var hallazgoId = Guid.NewGuid();
        var clientCommandId = Guid.NewGuid().ToString();
        var body = RequestBodyManualSinIntervencion(hallazgoId);

        var client = factory.CreateClient();

        // Primer POST
        var primerRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos")
        {
            Content = JsonContent.Create(body)
        };
        primerRequest.Headers.Add("X-Client-Command-Id", clientCommandId);
        var primeraRespuesta = await client.SendAsync(primerRequest);
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);

        // When: el cliente reintenta con el MISMO clientCommandId
        var segundoRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos")
        {
            Content = JsonContent.Create(body)
        };
        segundoRequest.Headers.Add("X-Client-Command-Id", clientCommandId);
        var segundaRespuesta = await client.SendAsync(segundoRequest);

        // Then: Wolverine envelope dedup detecta el MessageId repetido
        // y devuelve la respuesta original sin reaplicar el handler
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.OK,
            "ADR-008 §9.16 — replay devuelve 200 con la respuesta original del envelope");

        var resultado = await segundaRespuesta.Content.ReadFromJsonAsync<RespuestaRegistrarHallazgo>();
        resultado.Should().NotBeNull();
        resultado!.HallazgoId.Should().Be(hallazgoId, "el replay debe devolver el mismo HallazgoId");

        // Verificación profunda: exactamente un HallazgoRegistrado_v1 en el stream
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        await using var verificacion = store.QuerySession();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        eventos.Select(e => e.Data).OfType<HallazgoRegistrado_v1>()
            .Should().ContainSingle("envelope dedup ADR-008 garantiza que el replay no duplica el evento");
    }

    /// <summary>DTO local de lectura — independiente del namespace de la API.</summary>
    private sealed record RespuestaRegistrarHallazgo(
        Guid HallazgoId,
        Guid InspeccionId,
        string AccionRequerida,
        DateTimeOffset RegistradoEn);
}
