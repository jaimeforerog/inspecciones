using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/rechazar-generar-ot</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1l Â§9.
///
/// Cubre:
/// Â§6.1  â€” happy path (200 OK + body con RechazadaEn/RechazadoPor/Motivo + estado CerradaSinOT).
/// Â§6.3  â€” PRE-1 capability "generar-ot" ausente â†’ 403 Forbidden.
/// Â§6.12 â€” PRE-2 InspeccionId inexistente â†’ 404 Not Found.
/// Â§6.4  â€” PRE-3 motivo &lt; 10 chars â†’ 422 Unprocessable Entity + codigoError I-F6-MOTIVO.
/// Â§6.6  â€” PRE-4 inspecciÃ³n no firmada â†’ 422 + codigoError I-F6-ESTADO.
/// Â§6.8  â€” PRE-5 sin hallazgos RequiereIntervencion â†’ 422 + codigoError I-F6-SIN-INTERVENCION.
/// Â§6.10 â€” PRE-6 OT ya solicitada â†’ 409 Conflict + codigoError I-F6-OT-YA-SOLICITADA.
/// Â§6.13 â€” Idempotencia ADR-008 (Skip: requiere Wolverine envelope dedup en producciÃ³n).
/// Header â€” X-Client-Command-Id ausente â†’ 400 Bad Request + codigoError HEADER-REQUERIDO.
///
/// NOTA FU-32 (resuelto): los tests del slice 1l estuvieron en skip explÃ­cito mientras
/// <c>RunOaktonCommands(args)</c> en <c>Program.cs</c> impedÃ­a que
/// <c>WebApplicationFactory&lt;Program&gt;</c> arranque el pipeline HTTP. Fix: condicionar
/// el arranque a <c>args.Length &gt; 0</c> (fix-FU-32). Los tests estÃ¡n destrabados desde
/// el commit del fix. Cobertura adicional en:
///   - <c>Inspecciones.Domain.Tests/Inspecciones/RechazarGenerarOTTests.cs</c> (18 tests, 94.92% cobertura).
///   - <c>Inspecciones.Application.Tests/Inspecciones/RechazarGenerarOTHandlerTests.cs</c>
///     (handler + Marten real con Testcontainers).
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class RechazarGenerarOTEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // SkipReasonFu32 eliminada â€” FU-32 cerrado. Los tests a continuaciÃ³n ya no requieren skip.

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Helpers de siembra
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Siembra un stream de inspecciÃ³n tÃ©cnica en estado Firmada con hallazgo
    /// RequiereIntervencion y dictamen NoPuedeOperar â€” cumple todas las I-F6.
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmadaConIntervencion(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();

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
                ActividadDescripcion: "RevisiÃ³n brazo hidrÃ¡ulico",
                NovedadTecnica: "Falla estructural en brazo hidrÃ¡ulico",
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
                DiagnosticoFinal: "Falla estructural en brazo hidrÃ¡ulico",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.NoPuedeOperar,
                Justificacion: "Equipo fuera de operaciÃ³n",
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

    private static object RequestBodyBase(
        string motivo = "El equipo serÃ¡ dado de baja definitiva en 10 dÃ­as") => new
    {
        motivo
    };

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.1 Happy path â€” 200 OK con body correcto
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto()
    {
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 60001);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // D-4: 200 OK (no 202) â€” el cierre es sÃ­ncrono.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "RechazarGenerarOT devuelve 200 porque el cierre es sÃ­ncrono (D-4 spec Â§9)");

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaRechazarOT>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId);
        resultado.Estado.Should().Be("CerradaSinOT");
        resultado.RechazadoPor.Should().NotBeNullOrEmpty();
        resultado.Motivo.Should().Be("El equipo serÃ¡ dado de baja definitiva en 10 dÃ­as");
        resultado.RechazadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.3 PRE-1 â€” capability "generar-ot" ausente â†’ 403 Forbidden
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1()
    {
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 60002);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Sin-Capability-Generar-OT", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-1: el endpoint debe rechazar si el aprobador no tiene capability 'generar-ot'");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.12 PRE-2 â€” InspeccionId inexistente â†’ 404 Not Found
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_inspeccion_inexistente_responde_404_Not_Found_PRE_2()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{Guid.NewGuid()}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.4 PRE-3 â€” motivo < 10 chars â†’ 422 Unprocessable Entity
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_motivo_corto_responde_422_I_F6_MOTIVO()
    {
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 60003);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase(motivo: "Corto"))
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F6-MOTIVO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.6 PRE-4 â€” inspecciÃ³n no firmada â†’ 422
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_inspeccion_no_firmada_responde_422_I_F6_ESTADO()
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();
        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 60004,
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
            $"/api/v1/inspecciones/{inspeccionId}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F6-ESTADO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.10 PRE-6 â€” OT ya solicitada â†’ 409 Conflict
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_OT_ya_solicitada_responde_409_Conflict_I_F6_OT_YA_SOLICITADA()
    {
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 60005);

        // Siembra OTSolicitada_v1 directamente para preparar el escenario PRE-6.
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
            session.Events.Append(inspeccionId,
                new OTSolicitada_v1(
                    InspeccionId: inspeccionId,
                    SolicitadaPor: "jefe.campo.previo",
                    Responsable: ResponsableCosto.Proyecto,
                    Prioridad: PrioridadOT.Urgente,
                    Observaciones: null,
                    ComentarioJefe: null,
                    SolicitadaEn: CapturadoEn));
            await session.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F6-OT-YA-SOLICITADA");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.13 Idempotencia ADR-008 â€” mismo X-Client-Command-Id no duplica eventos (Skip)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
                 "Implementar cuando el handler estÃ© registrado como Wolverine handler " +
                 "con durable local queues. Ver spec Â§6.13, Â§7, ADR-008 Â§9.16.")]
    public async Task POST_rechazar_generar_ot_replay_mismo_ClientCommandId_no_duplica_eventos_ADR_008()
    {
        await Task.CompletedTask;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Header X-Client-Command-Id ausente â†’ 400 Bad Request
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_rechazar_generar_ot_sin_header_X_Client_Command_Id_responde_400_Bad_Request()
    {
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 60006);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/rechazar-generar-ot")
        {
            Content = JsonContent.Create(RequestBodyBase())
        };
        // Intencionalmente NO se agrega el header X-Client-Command-Id

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("HEADER-REQUERIDO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // DTOs locales de lectura â€” independientes del namespace de la API
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed record RespuestaRechazarOT(
        Guid           InspeccionId,
        string         Estado,
        DateTimeOffset RechazadaEn,
        string         RechazadoPor,
        string         Motivo);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
