using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/generar-ot</c> contra la
/// app real con Postgres en Testcontainers. Spec slice 1k Â§9.
/// Cubre:
/// Â§6.1  â€” happy path (202 Accepted + body con SolicitadaEn/SolicitadaPor + Location).
/// Â§6.3  â€” PRE-1 capability "generar-ot" ausente â†’ 403 Forbidden.
/// Â§6.10 â€” PRE-2 InspeccionId inexistente â†’ 404 Not Found.
/// Â§6.6  â€” PRE-5 OT ya solicitada â†’ 409 Conflict + codigoError I-F4-OT-DUPLICADA.
/// Â§6.4  â€” PRE-3 inspecciÃ³n no firmada â†’ 422 Unprocessable Entity + codigoError I-F4-ESTADO.
/// Â§6.5  â€” PRE-4 sin hallazgos RequiereIntervencion â†’ 422 + codigoError I-F4-SIN-INTERVENCION.
/// Â§6.8  â€” PRE-7 dictamen PuedeOperar â†’ 422 + codigoError I-F4-DICTAMEN.
/// Â§6.9  â€” Idempotencia ADR-008 (Skip: requiere Wolverine envelope dedup en producciÃ³n).
/// Header â€” X-Client-Command-Id ausente â†’ 400 Bad Request + codigoError HEADER-REQUERIDO.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class GenerarOTEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Helpers de siembra
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Siembra un stream de inspecciÃ³n tÃ©cnica en estado Firmada con hallazgo
    /// RequiereIntervencion y dictamen NoPuedeOperar â€” cumple todas las I-F4.
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

    /// <summary>
    /// Siembra inspecciÃ³n firmada con hallazgo solo RequiereSeguimiento â€” PRE-4 violada.
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmadaSinIntervencion(int equipoId)
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
                ActividadDescripcion: "RevisiÃ³n visual",
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
                Justificacion: "Operar con restricciÃ³n",
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
    /// Siembra inspecciÃ³n firmada con dictamen PuedeOperar â€” PRE-7 violada (defensa segunda lÃ­nea).
    /// Se usa siembra directa de eventos para evitar la validaciÃ³n V-F8 de la capa de dominio
    /// (que bloquea emitir dictamen PuedeOperar con hallazgos de seguimiento/intervenciÃ³n).
    /// </summary>
    private async Task<Guid> SembrarInspeccionFirmadaConDictamenPuedeOperar(int equipoId)
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
                ActividadDescripcion: "RevisiÃ³n visual OK",
                NovedadTecnica: "Sin anomalÃ­as",
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
                DiagnosticoFinal: "Sin anomalÃ­as detectadas",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: CapturadoEn),
            // Siembra dictamen PuedeOperar directamente sobre el event store
            // (la validaciÃ³n V-F8 se saltea a propÃ³sito â€” este test verifica
            // la defensa de 2da lÃ­nea en el mÃ©todo de decisiÃ³n SolicitarOT).
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
        string? observaciones = "Equipo fuera de operaciÃ³n â€” prioridad mÃ¡xima",
        string? comentarioJefe = null) => new
    {
        responsable,
        prioridad,
        observaciones,
        comentarioJefe
    };

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.1 Happy path â€” 202 Accepted con body y Location header
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto()
    {
        // Given: inspecciÃ³n firmada con hallazgo RequiereIntervencion, dictamen NoPuedeOperar
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

        // Then: 202 Accepted â€” spec Â§9 (proceso asÃ­ncrono via saga EjecutarOTSaga)
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

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.3 PRE-1 â€” capability "generar-ot" ausente â†’ 403 Forbidden
    // El endpoint verifica PRE-1 usando el header X-Sin-Capability-Generar-OT
    // (patrÃ³n de test: seÃ±al de que el claims mock NO debe incluir la capability).
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1()
    {
        // Given: inspecciÃ³n firmada vÃ¡lida (las capabilities se validan en capa HTTP)
        var inspeccionId = await SembrarInspeccionFirmadaConIntervencion(equipoId: 40002);

        // When: se envÃ­a el header X-Sin-Capability-Generar-OT para que el endpoint
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

        // Then: 403 Forbidden â€” PRE-1
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-1: el endpoint debe rechazar si el aprobador no tiene capability 'generar-ot'");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.10 PRE-2 â€” InspeccionId inexistente â†’ 404 Not Found
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // Then: 404 Not Found â€” PRE-2
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.6 PRE-5 / I-F4.c â€” OT ya solicitada â†’ 409 Conflict
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // Then: 409 Conflict â€” PRE-5 (OT ya solicitada)
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await segundaRespuesta.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-OT-DUPLICADA");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.4 PRE-3 / I-F4.a â€” inspecciÃ³n no firmada â†’ 422 Unprocessable Entity
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_generar_ot_inspeccion_no_firmada_responde_422_I_F4_ESTADO()
    {
        // Given: inspecciÃ³n en estado EnEjecucion (sin firmar)
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();
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

        // Then: 422 â€” inspecciÃ³n no firmada (PRE-3 I-F4.a)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-ESTADO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.5 PRE-4 / I-F4.b â€” sin hallazgos RequiereIntervencion â†’ 422
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_generar_ot_sin_hallazgos_RequiereIntervencion_responde_422_I_F4_SIN_INTERVENCION()
    {
        // Given: inspecciÃ³n firmada con solo hallazgos RequiereSeguimiento
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

        // Then: 422 â€” sin hallazgos con RequiereIntervencion (PRE-4 I-F4.b)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-SIN-INTERVENCION");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.8 PRE-7 / I-F4.e â€” dictamen PuedeOperar â†’ 422 Unprocessable Entity
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_generar_ot_dictamen_PuedeOperar_responde_422_I_F4_DICTAMEN()
    {
        // Given: inspecciÃ³n firmada con dictamen PuedeOperar (defensa segunda lÃ­nea V-F8)
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

        // Then: 422 â€” dictamen PuedeOperar no permite OT (PRE-7 I-F4.e)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-F4-DICTAMEN");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.9 Idempotencia ADR-008 â€” mismo X-Client-Command-Id no duplica evento (Skip)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
                 "Implementar cuando el handler estÃ© registrado como Wolverine handler " +
                 "con durable local queues. Ver spec Â§6.9, Â§7, ADR-008 Â§9.16.")]
    public async Task POST_generar_ot_replay_mismo_ClientCommandId_no_duplica_OTSolicitada_v1_ADR_008()
    {
        await Task.CompletedTask;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Header X-Client-Command-Id ausente â†’ 400 Bad Request
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_generar_ot_sin_header_X_Client_Command_Id_responde_400_Bad_Request()
    {
        // Given: inspecciÃ³n firmada vÃ¡lida
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

        // Then: 400 Bad Request â€” header requerido (patrÃ³n ADR-008 igual a todos los endpoints)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("HEADER-REQUERIDO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // DTOs locales de lectura â€” independientes del namespace de la API
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed record RespuestaGenerarOT(
        Guid           InspeccionId,
        DateTimeOffset SolicitadaEn,
        string         SolicitadaPor,
        string         Responsable,
        string         Prioridad);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
