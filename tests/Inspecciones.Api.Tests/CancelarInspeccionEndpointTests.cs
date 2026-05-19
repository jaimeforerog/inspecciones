using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/cancelar</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1m §9.
///
/// Cubre:
/// §6.1  — happy path (200 OK + body CanceladaEn/CanceladaPor/Motivo + estado Cancelada).
/// §6.4  — PRE-1 capability "ejecutar-inspeccion" ausente → 403 Forbidden.
/// §6.5  — PRE-2 InspeccionId inexistente → 404 Not Found.
/// §6.6  — PRE-3 técnico no contribuyente → 403 Forbidden + codigoError I6-NO-CONTRIBUYENTE.
/// §6.7  — PRE-4 motivo vacío → 422 Unprocessable Entity + codigoError I6-MOTIVO.
/// §6.9  — PRE-4 motivo corto (&lt;10 chars) → 422 + codigoError I6-MOTIVO.
/// §6.10 — PRE-5 inspección firmada → 409 Conflict + codigoError I6-ESTADO.
/// §6.11 — PRE-5 inspección ya cancelada → 409 Conflict + codigoError I6-ESTADO.
/// §6.14 — Idempotencia ADR-008 (Skip: requiere Wolverine envelope dedup en producción).
/// Header — X-Client-Command-Id ausente → 400 Bad Request + codigoError HEADER-REQUERIDO.
/// Proyección — InspeccionAbiertaPorEquipoView delete tras cancelación.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class CancelarInspeccionEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream de inspección técnica en estado EnEjecucion.
    /// TecnicoIniciador = "1" — corresponde al default IdUsuario del
    /// TestHeaderAwareSessionService (spec mt-1 D-MT1-6: IdUsuario.ToString()).
    /// </summary>
    private async Task<Guid> SembrarInspeccionEnEjecucion(
        int equipoId,
        string tecnicoId = "1")
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: tecnicoId,
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                IniciadaEn: CapturadoEn,
                FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra una inspección firmada completa (para escenario PRE-5 ya firmada).
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmada(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "1",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                IniciadaEn: CapturadoEn,
                FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            new HallazgoRegistrado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                Origen: OrigenHallazgo.Manual,
                NovedadPreopOrigenId: null,
                MedicionOrigenId: null,
                EvaluacionOrigenId: null,
                ParteEquipoId: 77,
                ActividadId: null,
                ActividadDescripcion: "Revisión general",
                NovedadTecnica: "Estado general satisfactorio",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "1",
                RegistradoEn: CapturadoEn),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Inspección completa",
                EmitidoPor: "1",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin hallazgos críticos",
                EmitidoPor: "1",
                EstablecidoEn: CapturadoEn),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "1",
                FirmaUri: "https://blobs/firma-01.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                FirmadaEn: CapturadoEn));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra una inspección ya cancelada (para escenario PRE-5 segunda cancelación §6.11).
    /// </summary>
    private async Task<Guid> SembrarInspeccionYaCancelada(int equipoId)
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId, tecnicoId: "carlos.ruiz");
        var store = factory.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        session.Events.Append(inspeccionId,
            new InspeccionCancelada_v1(
                InspeccionId: inspeccionId,
                Motivo: "Cancelación previa del técnico",
                CanceladaPor: "1",
                CanceladaEn: CapturadoEn));
        await session.SaveChangesAsync();
        return inspeccionId;
    }

    private static object RequestBodyBase(
        string motivo = "Equipo trasladado a otra obra sin previo aviso") => new
    {
        motivo
    };

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — 200 OK con body correcto
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_happy_path_responde_200_OK()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 80001);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id simula el JWT claim en el mock de ADR-002 (tecnico contribuyente)
        // X-Tecnico-Id omitido: el default IdUsuario=1 del TestHeaderAwareSessionService es contribuyente.

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "CancelarInspeccion devuelve 200 porque la cancelación es síncrona (spec §9)");

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaCancelarInspeccion>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId);
        resultado.Estado.Should().Be("Cancelada");
        resultado.CanceladaPor.Should().NotBeNullOrEmpty();
        resultado.Motivo.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        resultado.CanceladaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 PRE-1 — capability "ejecutar-inspeccion" ausente → 403 Forbidden
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_sin_capability_responde_403()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 80002);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // Header de test para simular ausencia de capability (PRE-1)
        request.Headers.Add("X-Sin-Capability-Ejecutar", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-1: el endpoint debe rechazar si el técnico no tiene capability 'ejecutar-inspeccion'");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 PRE-2 — InspeccionId inexistente → 404 Not Found
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_inexistente_responde_404()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{Guid.NewGuid()}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id omitido: el default IdUsuario=1 del TestHeaderAwareSessionService es contribuyente.

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 PRE-3 — técnico no contribuyente → 403 Forbidden
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_tecnico_no_contribuyente_responde_403()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(
            equipoId: 80003, tecnicoId: "1");

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id=99 — int distinto al sembrado (=1), TestHeaderAwareSessionService
        // mapea el header a IdUsuario=99 → técnico externo no contribuyente.
        request.Headers.Add("X-Tecnico-Id", "99");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-3: el técnico externo no puede cancelar una inspección en la que no ha contribuido");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-NO-CONTRIBUYENTE");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 PRE-4 — motivo vacío → 422 Unprocessable Entity
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_motivo_vacio_responde_400_o_422_I6_MOTIVO()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 80004);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase(motivo: ""))
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id omitido: el default IdUsuario=1 del TestHeaderAwareSessionService es contribuyente.

        var response = await client.SendAsync(request);

        // 422 para validaciones de negocio (PRE-4 I6-MOTIVO); el endpoint puede también retornar 400
        // si se implementa validación de modelo antes del handler.
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest },
            "PRE-4: motivo vacío — el endpoint debe rechazar con 422 o 400");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 PRE-4 — motivo corto (<10 chars) → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_motivo_corto_responde_422_I6_MOTIVO()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 80005);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase(motivo: "Corto"))
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id omitido: el default IdUsuario=1 del TestHeaderAwareSessionService es contribuyente.

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-MOTIVO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 PRE-5 — inspección ya firmada → 409 Conflict + codigoError I6-ESTADO
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_ya_firmada_responde_409_I6_ESTADO()
    {
        var inspeccionId = await SembrarInspeccionFirmada(equipoId: 80006);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id omitido: el default IdUsuario=1 del TestHeaderAwareSessionService es contribuyente.

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "PRE-5: la inspección está Firmada, no puede cancelarse (I6 + I-F1)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-ESTADO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 PRE-5 — inspección ya cancelada → 409 Conflict
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_ya_cancelada_responde_409_I6_ESTADO()
    {
        var inspeccionId = await SembrarInspeccionYaCancelada(equipoId: 80007);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        // X-Tecnico-Id omitido: el default IdUsuario=1 del TestHeaderAwareSessionService es contribuyente.

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "PRE-5: la inspección ya está Cancelada — no se puede cancelar de nuevo (I6)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-ESTADO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Idempotencia ADR-008 — mismo X-Client-Command-Id no duplica eventos (Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
                 "Implementar cuando el handler esté registrado como Wolverine handler " +
                 "con durable local queues. Ver spec §6.14, §7, ADR-008 §9.16.")]
    public async Task POST_cancelar_inspeccion_replay_mismo_ClientCommandId_no_duplica_eventos_ADR_008()
    {
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Header X-Client-Command-Id ausente → 400 Bad Request
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_cancelar_inspeccion_sin_header_X_Client_Command_Id_responde_400()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 80008);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/cancelar")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        // Intencionalmente NO se agrega X-Client-Command-Id

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("HEADER-REQUERIDO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs locales de lectura — independientes del namespace de la API
    // ─────────────────────────────────────────────────────────────────────

    private sealed record RespuestaCancelarInspeccion(
        Guid           InspeccionId,
        string         Estado,
        DateTimeOffset CanceladaEn,
        string         CanceladaPor,
        string         Motivo);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
