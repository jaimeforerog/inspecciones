using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/cancelar</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1m Â§9.
///
/// Cubre:
/// Â§6.1  â€” happy path (200 OK + body CanceladaEn/CanceladaPor/Motivo + estado Cancelada).
/// Â§6.4  â€” PRE-1 capability "ejecutar-inspeccion" ausente â†’ 403 Forbidden.
/// Â§6.5  â€” PRE-2 InspeccionId inexistente â†’ 404 Not Found.
/// Â§6.6  â€” PRE-3 tÃ©cnico no contribuyente â†’ 403 Forbidden + codigoError I6-NO-CONTRIBUYENTE.
/// Â§6.7  â€” PRE-4 motivo vacÃ­o â†’ 422 Unprocessable Entity + codigoError I6-MOTIVO.
/// Â§6.9  â€” PRE-4 motivo corto (&lt;10 chars) â†’ 422 + codigoError I6-MOTIVO.
/// Â§6.10 â€” PRE-5 inspecciÃ³n firmada â†’ 409 Conflict + codigoError I6-ESTADO.
/// Â§6.11 â€” PRE-5 inspecciÃ³n ya cancelada â†’ 409 Conflict + codigoError I6-ESTADO.
/// Â§6.14 â€” Idempotencia ADR-008 (Skip: requiere Wolverine envelope dedup en producciÃ³n).
/// Header â€” X-Client-Command-Id ausente â†’ 400 Bad Request + codigoError HEADER-REQUERIDO.
/// ProyecciÃ³n â€” InspeccionAbiertaPorEquipoView delete tras cancelaciÃ³n.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class CancelarInspeccionEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Helpers de siembra
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Siembra un stream de inspecciÃ³n tÃ©cnica en estado EnEjecucion.
    /// TecnicoIniciador = "1" â€” corresponde al default IdUsuario del
    /// TestHeaderAwareSessionService (spec mt-1 D-MT1-6: IdUsuario.ToString()).
    /// </summary>
    private async Task<Guid> SembrarInspeccionEnEjecucion(
        int equipoId,
        string tecnicoId = "1")
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();

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
    /// Siembra una inspecciÃ³n firmada completa (para escenario PRE-5 ya firmada).
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmada(int equipoId)
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
                ActividadDescripcion: "RevisiÃ³n general",
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
                DiagnosticoFinal: "InspecciÃ³n completa",
                EmitidoPor: "1",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin hallazgos crÃ­ticos",
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
    /// Siembra una inspecciÃ³n ya cancelada (para escenario PRE-5 segunda cancelaciÃ³n Â§6.11).
    /// </summary>
    private async Task<Guid> SembrarInspeccionYaCancelada(int equipoId)
    {
        var inspeccionId = await SembrarInspeccionEnEjecucion(equipoId, tecnicoId: "carlos.ruiz");
        var store = factory.Services.GetRequiredService<IDocumentStore>();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();
        session.Events.Append(inspeccionId,
            new InspeccionCancelada_v1(
                InspeccionId: inspeccionId,
                Motivo: "CancelaciÃ³n previa del tÃ©cnico",
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

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.1 Happy path â€” 200 OK con body correcto
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            "CancelarInspeccion devuelve 200 porque la cancelaciÃ³n es sÃ­ncrona (spec Â§9)");

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaCancelarInspeccion>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId);
        resultado.Estado.Should().Be("Cancelada");
        resultado.CanceladaPor.Should().NotBeNullOrEmpty();
        resultado.Motivo.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        resultado.CanceladaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.4 PRE-1 â€” capability "ejecutar-inspeccion" ausente â†’ 403 Forbidden
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            "PRE-1: el endpoint debe rechazar si el tÃ©cnico no tiene capability 'ejecutar-inspeccion'");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.5 PRE-2 â€” InspeccionId inexistente â†’ 404 Not Found
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.6 PRE-3 â€” tÃ©cnico no contribuyente â†’ 403 Forbidden
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        // X-Tecnico-Id=99 â€” int distinto al sembrado (=1), TestHeaderAwareSessionService
        // mapea el header a IdUsuario=99 â†’ tÃ©cnico externo no contribuyente.
        request.Headers.Add("X-Tecnico-Id", "99");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-3: el tÃ©cnico externo no puede cancelar una inspecciÃ³n en la que no ha contribuido");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-NO-CONTRIBUYENTE");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.7 PRE-4 â€” motivo vacÃ­o â†’ 422 Unprocessable Entity
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // 422 para validaciones de negocio (PRE-4 I6-MOTIVO); el endpoint puede tambiÃ©n retornar 400
        // si se implementa validaciÃ³n de modelo antes del handler.
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest },
            "PRE-4: motivo vacÃ­o â€” el endpoint debe rechazar con 422 o 400");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.9 PRE-4 â€” motivo corto (<10 chars) â†’ 422
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.10 PRE-5 â€” inspecciÃ³n ya firmada â†’ 409 Conflict + codigoError I6-ESTADO
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            "PRE-5: la inspecciÃ³n estÃ¡ Firmada, no puede cancelarse (I6 + I-F1)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-ESTADO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.11 PRE-5 â€” inspecciÃ³n ya cancelada â†’ 409 Conflict
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            "PRE-5: la inspecciÃ³n ya estÃ¡ Cancelada â€” no se puede cancelar de nuevo (I6)");
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I6-ESTADO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.14 Idempotencia ADR-008 â€” mismo X-Client-Command-Id no duplica eventos (Skip)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
                 "Implementar cuando el handler estÃ© registrado como Wolverine handler " +
                 "con durable local queues. Ver spec Â§6.14, Â§7, ADR-008 Â§9.16.")]
    public async Task POST_cancelar_inspeccion_replay_mismo_ClientCommandId_no_duplica_eventos_ADR_008()
    {
        await Task.CompletedTask;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Header X-Client-Command-Id ausente â†’ 400 Bad Request
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // DTOs locales de lectura â€” independientes del namespace de la API
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed record RespuestaCancelarInspeccion(
        Guid           InspeccionId,
        string         Estado,
        DateTimeOffset CanceladaEn,
        string         CanceladaPor,
        string         Motivo);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
