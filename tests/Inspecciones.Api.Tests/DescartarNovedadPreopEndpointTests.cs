using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint
/// <c>POST /api/v1/inspecciones/{inspeccionId}/novedades-preop/{novedadId}/descartar</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1n §9.
///
/// Cubre:
/// §6.1  — happy path (200 OK + body NovedadId/MotivoDescarte/DescartadaPor/DescartadaEn).
/// §6.2  — PRE-2 inspección firmada → 422 Unprocessable Entity.
/// §6.3  — PRE-2 inspección cancelada → 422 Unprocessable Entity.
/// §6.4  — PRE-5 novedad ya descartada → 422 Unprocessable Entity.
/// §6.5  — PRE-6 novedad ya convertida en hallazgo → 422 Unprocessable Entity.
/// §6.7  — PRE-4 capability "ejecutar-inspeccion" ausente → 403 Forbidden.
/// §6.8  — PRE-1 InspeccionId inexistente → 404 Not Found.
/// Header — X-Client-Command-Id ausente → 400 Bad Request + codigoError HEADER-REQUERIDO.
/// §6.14 — Idempotencia ADR-008 (Skip: requiere Wolverine envelope dedup en producción).
///
/// TODOS los tests fallan con NotImplementedException hasta que green implemente
/// DescartarNovedadPreopHandler.Handle.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class DescartarNovedadPreopEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream de inspección técnica en estado EnEjecucion.
    /// TecnicoIniciador = "1" (coincide con el header X-Tecnico-Id de los tests).
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
    /// Siembra una inspección firmada completa (para escenario PRE-2 ya firmada).
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
    /// Siembra inspección en ejecución + novedad ya descartada (para PRE-5 §6.4).
    /// </summary>
    private async Task<Guid> SembrarInspeccionConNovedadDescartada(int equipoId, int novedadId = 9001)
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId, tecnicoId: "1");
        var store = factory.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        session.Events.Append(inspeccionId,
            new NovedadPreopDescartada_v1(
                InspeccionId: inspeccionId,
                NovedadId: novedadId,
                MotivoDescarte: $"Cerrado por ana.gomez el 2026-05-08 15:00 UTC desde Inspecciones",
                DescartadaPor: "1",
                DescartadaEn: CapturadoEn));
        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra inspección en ejecución + hallazgo con Origen=PreOperacional y
    /// NovedadPreopOrigenId=9001 (para PRE-6 §6.5 — INV-ND1).
    /// </summary>
    private async Task<Guid> SembrarInspeccionConHallazgoPreop(int equipoId, int novedadPreopOrigenId = 9001)
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId, tecnicoId: "1");
        var store = factory.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        session.Events.Append(inspeccionId,
            new HallazgoRegistrado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: Guid.NewGuid(),
                Origen: OrigenHallazgo.PreOperacional,
                NovedadPreopOrigenId: novedadPreopOrigenId,
                MedicionOrigenId: null,
                EvaluacionOrigenId: null,
                ParteEquipoId: 88,
                ActividadId: 55,
                ActividadDescripcion: null,
                NovedadTecnica: "Fuga confirmada en sello hidráulico",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar sello hidráulico",
                TipoFallaId: 3,
                CausaFallaId: 12,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "1",
                RegistradoEn: CapturadoEn));
        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra inspección cancelada (para PRE-2 §6.3).
    /// </summary>
    private async Task<Guid> SembrarInspeccionCancelada(int equipoId)
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId, tecnicoId: "1");
        var store = factory.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        session.Events.Append(inspeccionId,
            new InspeccionCancelada_v1(
                InspeccionId: inspeccionId,
                Motivo: "Cancelada para tests de DescartarNovedadPreop",
                CanceladaPor: "1",
                CanceladaEn: CapturadoEn));
        await session.SaveChangesAsync();
        return inspeccionId;
    }

    private static object RequestBodyBase(string descartadaPor = "1") => new
    {
        descartadaPor
    };

    private static HttpRequestMessage BuildRequest(
        Guid inspeccionId,
        int novedadId,
        string? tecnicoId = "1",
        bool incluirClientCommandId = true,
        bool sinCapability = false)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/novedades-preop/{novedadId}/descartar")
        {
            Content = JsonContent.Create(RequestBodyBase(tecnicoId ?? "1"))
        };

        if (incluirClientCommandId)
        {
            request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        }

        if (tecnicoId is not null)
        {
            request.Headers.Add("X-Tecnico-Id", tecnicoId);
        }

        if (sinCapability)
        {
            request.Headers.Add("X-Sin-Capability-Ejecutar", "true");
        }

        return request;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — 200 OK con body correcto
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_preop_happy_path_responde_200_OK()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 90001);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "DescartarNovedadPreop devuelve 200 OK en el happy path (spec §9)");

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaDescartarNovedad>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId);
        resultado.NovedadId.Should().Be(9001);
        resultado.DescartadaPor.Should().Be("1");
        resultado.MotivoDescarte.Should().Contain("1",
            "el motivo autogenerado incluye el userId del técnico (D-4)");
        resultado.MotivoDescarte.Should().Contain("UTC desde Inspecciones",
            "el motivo autogenerado usa la plantilla D-4 del spec §13");
        resultado.DescartadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 PRE-2 — inspección firmada → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_inspeccion_firmada_responde_422()
    {
        var inspeccionId = await SembrarInspeccionFirmada(equipoId: 90002);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "PRE-2: la inspección está Firmada — no se puede descartar novedades (spec §6.2)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-2-ESTADO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 PRE-2 — inspección cancelada → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_inspeccion_cancelada_responde_422()
    {
        var inspeccionId = await SembrarInspeccionCancelada(equipoId: 90003);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "PRE-2: la inspección está Cancelada — estado terminal (spec §6.3)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-2-ESTADO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 PRE-5 — novedad ya descartada → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_ya_descartada_responde_422_PRE5()
    {
        var inspeccionId = await SembrarInspeccionConNovedadDescartada(equipoId: 90004, novedadId: 9001);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "PRE-5: la novedad 9001 ya fue descartada en esta inspección (spec §6.4 / I4 / INV-ND1)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-5-DESCARTADA");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 PRE-6 — novedad ya convertida en hallazgo → 422 (INV-ND1)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_ya_importada_como_hallazgo_responde_422_PRE6()
    {
        var inspeccionId = await SembrarInspeccionConHallazgoPreop(equipoId: 90005, novedadPreopOrigenId: 9001);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "PRE-6 / INV-ND1: la novedad 9001 ya fue importada como hallazgo — exclusividad mutua (spec §6.5)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-6-HALLAZGO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 PRE-4 — capability "ejecutar-inspeccion" ausente → 403 Forbidden
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_sin_capability_responde_403()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 90006);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1", sinCapability: true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-4: el endpoint debe rechazar con 403 si el técnico no tiene capability 'ejecutar-inspeccion'");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 PRE-1 — InspeccionId inexistente → 404 Not Found
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_inspeccion_inexistente_responde_404()
    {
        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(Guid.NewGuid(), novedadId: 9001, tecnicoId: "1"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "PRE-1: la inspección no existe en el store (spec §6.8)");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Header X-Client-Command-Id ausente → 400 Bad Request
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_descartar_novedad_sin_header_X_Client_Command_Id_responde_400()
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId: 90007);

        var client = factory.CreateClient();
        var response = await client.SendAsync(
            BuildRequest(inspeccionId, novedadId: 9001, tecnicoId: "1", incluirClientCommandId: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "El header X-Client-Command-Id es obligatorio (ADR-008 §9.16)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("HEADER-REQUERIDO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Idempotencia ADR-008 — mismo X-Client-Command-Id no duplica eventos (Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
                 "Implementar cuando el handler esté registrado como Wolverine handler " +
                 "con durable local queues. Ver spec §6.14 (idempotencia ADR-008 §9.16).")]
    public async Task POST_descartar_novedad_replay_mismo_ClientCommandId_no_duplica_eventos_ADR008()
    {
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs locales de lectura — independientes del namespace de la API
    // ─────────────────────────────────────────────────────────────────────

    private sealed record RespuestaDescartarNovedad(
        Guid           InspeccionId,
        int            NovedadId,
        string         MotivoDescarte,
        string         DescartadaPor,
        DateTimeOffset DescartadaEn);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
