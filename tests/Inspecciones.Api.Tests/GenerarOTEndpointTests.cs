using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/generar-ot</c> contra la
/// app real con Postgres en Testcontainers. Spec slice 1k §9.
/// Cubre:
/// §6.1  — happy path (202 Accepted + body con SolicitadaEn/SolicitadaPor + Location).
/// §6.3  — PRE-1 capability "generar-ot" ausente → 403 Forbidden.
/// §6.10 — PRE-2 InspeccionId inexistente → 404 Not Found.
/// §6.6  — PRE-5 OT ya solicitada → 409 Conflict + codigoError I-F4-OT-DUPLICADA.
/// §6.4  — PRE-3 inspección no firmada → 422 Unprocessable Entity + codigoError I-F4-ESTADO.
/// §6.5  — PRE-4 sin hallazgos RequiereIntervencion → 422 + codigoError I-F4-SIN-INTERVENCION.
/// §6.8  — PRE-7 dictamen PuedeOperar → 422 + codigoError I-F4-DICTAMEN.
/// §6.9  — Idempotencia ADR-008 (Skip: requiere Wolverine envelope dedup en producción).
/// Header — X-Client-Command-Id ausente → 400 Bad Request + codigoError HEADER-REQUERIDO.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class GenerarOTEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream de inspección técnica en estado Firmada con hallazgo
    /// RequiereIntervencion y dictamen NoPuedeOperar — cumple todas las I-F4.
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmadaConIntervencion(int equipoId)
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
                TecnicoIniciador: "carlos.ruiz",
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
                ActividadDescripcion: "Revisión brazo hidráulico",
                NovedadTecnica: "Falla estructural en brazo hidráulico",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar brazo",
                TipoFallaId: 1,
                CausaFallaId: 2,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "carlos.ruiz",
                RegistradoEn: CapturadoEn),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Falla estructural en brazo hidráulico",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.NoPuedeOperar,
                Justificacion: "Equipo fuera de operación",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: CapturadoEn),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-01.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                FirmadaEn: CapturadoEn));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra inspección firmada con hallazgo solo RequiereSeguimiento — PRE-4 violada.
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmadaSinIntervencion(int equipoId)
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
                TecnicoIniciador: "carlos.ruiz",
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
                ActividadDescripcion: "Revisión visual",
                NovedadTecnica: "Desgaste leve",
                AccionRequerida: AccionRequerida.RequiereSeguimiento,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "carlos.ruiz",
                RegistradoEn: CapturadoEn),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Desgaste leve, requiere seguimiento",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.ConRestriccion,
                Justificacion: "Operar con restricción",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: CapturadoEn),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-02.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                FirmadaEn: CapturadoEn));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra inspección firmada con dictamen PuedeOperar — PRE-7 violada (defensa segunda línea).
    /// Se usa siembra directa de eventos para evitar la validación V-F8 de la capa de dominio
    /// (que bloquea emitir dictamen PuedeOperar con hallazgos de seguimiento/intervención).
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmadaConDictamenPuedeOperar(int equipoId)
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
                TecnicoIniciador: "carlos.ruiz",
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
                ActividadDescripcion: "Revisión visual OK",
                NovedadTecnica: "Sin anomalías",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "carlos.ruiz",
                RegistradoEn: CapturadoEn),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Sin anomalías detectadas",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: CapturadoEn),
            // Siembra dictamen PuedeOperar directamente sobre el event store
            // (la validación V-F8 se saltea a propósito — este test verifica
            // la defensa de 2da línea en el método de decisión SolicitarOT).
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Equipo operativo",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: CapturadoEn),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-03.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                FirmadaEn: CapturadoEn));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    private static object RequestBodyBase(
        string responsable = "Proyecto",
        string prioridad = "Urgente",
        string? observaciones = "Equipo fuera de operación — prioridad máxima",
        string? comentarioJefe = null) => new
    {
        responsable,
        prioridad,
        observaciones,
        comentarioJefe
    };

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — 202 Accepted con body y Location header
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto()
    {
        // Given: inspección firmada con hallazgo RequiereIntervencion, dictamen NoPuedeOperar
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 40001);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 202 Accepted — spec §9 (proceso asíncrono via saga EjecutarOTSaga)
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "GenerarOT devuelve 202 porque el cierre real ocurre via saga (ADR-005, ADR-006)");

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaGenerarOT>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId);
        resultado.SolicitadaPor.Should().NotBeNullOrEmpty();
        resultado.Responsable.Should().Be("Proyecto");
        resultado.Prioridad.Should().Be("Urgente");
        resultado.SolicitadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(60));
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 PRE-1 — capability "generar-ot" ausente → 403 Forbidden
    // El endpoint verifica PRE-1 usando el header X-Sin-Capability-Generar-OT
    // (patrón de test: señal de que el claims mock NO debe incluir la capability).
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1()
    {
        // Given: inspección firmada válida (las capabilities se validan en capa HTTP)
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 40002);

        // When: se envía el header X-Sin-Capability-Generar-OT para que el endpoint
        // stub devuelva 403 (simula un JWT sin la capability "generar-ot")
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Sin-Capability-Generar-OT", "true");

        var response = await client.SendAsync(request);

        // Then: 403 Forbidden — PRE-1
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-1: el endpoint debe rechazar si el aprobador no tiene capability 'generar-ot'");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 PRE-2 — InspeccionId inexistente → 404 Not Found
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_inspeccion_inexistente_responde_404_Not_Found_PRE_2()
    {
        // Given: InspeccionId que no existe en Marten
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{Guid.NewGuid()}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 404 Not Found — PRE-2
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 PRE-5 / I-F4.c — OT ya solicitada → 409 Conflict
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_OT_ya_solicitada_responde_409_Conflict_I_F4_c()
    {
        // Given: primera solicitud exitosa
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 40003);

        var client = factory.CreateClient();
        var primerRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        primerRequest.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var primeraRespuesta = await client.SendAsync(primerRequest);
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "primera solicitud debe ser exitosa para preparar el escenario PRE-5");

        // When: segunda solicitud con X-Client-Command-Id DISTINTO
        var segundoRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase(prioridad: "Alta"))
        };
        segundoRequest.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var segundaRespuesta = await client.SendAsync(segundoRequest);

        // Then: 409 Conflict — PRE-5 (OT ya solicitada)
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await segundaRespuesta.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-OT-DUPLICADA");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 PRE-3 / I-F4.a — inspección no firmada → 422 Unprocessable Entity
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_inspeccion_no_firmada_responde_422_I_F4_ESTADO()
    {
        // Given: inspección en estado EnEjecucion (sin firmar)
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();

        await using var session = store.LightweightSession();
        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 40004,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "carlos.ruiz",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                IniciadaEn: CapturadoEn,
                FechaReportada: DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null));
        await session.SaveChangesAsync();

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 422 — inspección no firmada (PRE-3 I-F4.a)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-ESTADO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 PRE-4 / I-F4.b — sin hallazgos RequiereIntervencion → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_sin_hallazgos_RequiereIntervencion_responde_422_I_F4_SIN_INTERVENCION()
    {
        // Given: inspección firmada con solo hallazgos RequiereSeguimiento
        var inspeccionId = await SembrarInspeccionFirmadaSinIntervencion(equipoId: 40005);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 422 — sin hallazgos con RequiereIntervencion (PRE-4 I-F4.b)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-SIN-INTERVENCION");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 PRE-7 / I-F4.e — dictamen PuedeOperar → 422 Unprocessable Entity
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_dictamen_PuedeOperar_responde_422_I_F4_DICTAMEN()
    {
        // Given: inspección firmada con dictamen PuedeOperar (defensa segunda línea V-F8)
        var inspeccionId = await SembrarInspeccionFirmadaConDictamenPuedeOperar(equipoId: 40006);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 422 — dictamen PuedeOperar no permite OT (PRE-7 I-F4.e)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-DICTAMEN");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 Idempotencia ADR-008 — mismo X-Client-Command-Id no duplica evento (Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
                 "Implementar cuando el handler esté registrado como Wolverine handler " +
                 "con durable local queues. Ver spec §6.9, §7, ADR-008 §9.16.")]
    public async Task POST_generar_ot_replay_mismo_ClientCommandId_no_duplica_OTSolicitada_v1_ADR_008()
    {
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Header X-Client-Command-Id ausente → 400 Bad Request
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_generar_ot_sin_header_X_Client_Command_Id_responde_400_Bad_Request()
    {
        // Given: inspección firmada válida
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 40007);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        // Intencionalmente NO se agrega el header X-Client-Command-Id

        // When
        var response = await client.SendAsync(request);

        // Then: 400 Bad Request — header requerido (patrón ADR-008 igual a todos los endpoints)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("HEADER-REQUERIDO");
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs locales de lectura — independientes del namespace de la API
    // ─────────────────────────────────────────────────────────────────────

    private sealed record RespuestaGenerarOT(
        Guid           InspeccionId,
        DateTimeOffset SolicitadaEn,
        string         SolicitadaPor,
        string         Responsable,
        string         Prioridad);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
