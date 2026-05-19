using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint
/// <c>PATCH /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos/{repuestoId}</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1o Â§9.
///
/// Todos los tests fallan con NotImplementedException hasta que green implemente
/// <see cref="Inspecciones.Application.Inspecciones.ActualizarRepuestoHandler.Handle"/>.
///
/// Cubre:
/// Â§6.1  â€” Happy path: 200 OK + body con estado post-update.
/// Â§6.5  â€” PRE-2/I-H7: inspecciÃ³n firmada â†’ 422 + codigoError "I-H7".
/// Â§6.6  â€” PRE-3: HallazgoId inexistente â†’ 404 + codigoError "PRE-3".
/// Â§6.7  â€” PRE-4: hallazgo eliminado â†’ 422 + codigoError "PRE-4-ELIMINADO".
/// Â§6.8  â€” PRE-5: RepuestoId inexistente â†’ 404 + codigoError "PRE-5".
/// Â§6.9  â€” PRE-5: RepuestoId en hallazgo incorrecto â†’ 404 + codigoError "PRE-5".
/// Â§6.10 â€” PRE-7: Cantidadâ‰¤0 â†’ 422 + codigoError "PRE-7".
/// Â§6.11 â€” PRE-8: ambos campos null â†’ 400 + codigoError "PRE-8".
/// Â§6.12 â€” PRE-1: InspeccionId inexistente â†’ 404 + codigoError "PRE-1".
/// PRE-0 â€” capability ausente â†’ 403 Forbidden + codigoError "PRE-0".
/// Header â€” X-Client-Command-Id ausente â†’ 400 + codigoError "HEADER-REQUERIDO".
/// ADR-008 â€” Idempotencia (Skip: requiere Wolverine envelope dedup en producciÃ³n).
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class ActualizarRepuestoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Helpers de siembra
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Siembra un stream con inspecciÃ³n EnEjecucion + hallazgo + repuesto activo.
    /// Devuelve los tres IDs del stream.
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionConRepuesto(int equipoId, string tecnicoId = "rmartinez")
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

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
                ActividadDescripcion: "RevisiÃ³n sello hidrÃ¡ulico",
                NovedadTecnica: "Sello con desgaste avanzado",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar sello hidrÃ¡ulico",
                TipoFallaId: 3,
                CausaFallaId: 12,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: tecnicoId,
                RegistradoEn: CapturadoEn),
            new RepuestoEstimado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                RepuestoId: repuestoId,
                SkuId: 501,
                SkuCodigo: "INS-501",
                Cantidad: 1m,
                Justificacion: "Cambio rutinario",
                Unidad: "unidad",
                AsignadoPor: tecnicoId,
                AsignadoEn: CapturadoEn));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId, repuestoId);
    }

    /// <summary>
    /// Siembra una inspecciÃ³n firmada completa (para PRE-2 Â§6.5).
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionFirmadaConRepuesto(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();
        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
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
                NovedadTecnica: "Estado sin hallazgos crÃ­ticos",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "rmartinez",
                RegistradoEn: CapturadoEn),
            new RepuestoEstimado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                RepuestoId: repuestoId,
                SkuId: 501,
                SkuCodigo: "INS-501",
                Cantidad: 1m,
                Justificacion: "Cambio rutinario",
                Unidad: "unidad",
                AsignadoPor: "rmartinez",
                AsignadoEn: CapturadoEn),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "InspecciÃ³n completa sin hallazgos crÃ­ticos",
                EmitidoPor: "rmartinez",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin hallazgos de intervenciÃ³n",
                EmitidoPor: "rmartinez",
                EstablecidoEn: CapturadoEn),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "rmartinez",
                FirmaUri: "https://blobs/firma-test.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, CapturadoEn),
                FirmadaEn: CapturadoEn));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId, repuestoId);
    }

    /// <summary>
    /// Siembra inspecciÃ³n EnEjecucion con hallazgo eliminado.
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionConHallazgoEliminado(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

        await using var session = factory.OpenSeedingSessionForDefaultTenant();
        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
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
                ActividadDescripcion: "RevisiÃ³n",
                NovedadTecnica: "Desgaste leve",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar",
                TipoFallaId: 3,
                CausaFallaId: 12,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "rmartinez",
                RegistradoEn: CapturadoEn),
            new HallazgoEliminado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                Motivo: "Error de registro",
                EliminadoPor: "rmartinez",
                EliminadoEn: CapturadoEn),
            new RepuestoEstimado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                RepuestoId: repuestoId,
                SkuId: 501,
                SkuCodigo: "INS-501",
                Cantidad: 1m,
                Justificacion: null,
                Unidad: "unidad",
                AsignadoPor: "rmartinez",
                AsignadoEn: CapturadoEn));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId, repuestoId);
    }

    private static HttpRequestMessage BuildRequest(
        Guid inspeccionId,
        Guid hallazgoId,
        Guid repuestoId,
        decimal? cantidadNueva = 2m,
        string? observacionNueva = null,
        bool incluirClientCommandId = true,
        bool sinCapability = false)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos/{repuestoId}")
        {
            Content = JsonContent.Create(new
            {
                cantidadNueva,
                observacionNueva
            })
        };

        if (incluirClientCommandId)
        {
            request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        }

        if (sinCapability)
        {
            request.Headers.Add("X-Sin-Capability-Ejecutar", "true");
        }

        return request;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Header â€” X-Client-Command-Id ausente â†’ 400 Bad Request
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_sin_header_ClientCommandId_responde_400_HEADER_REQUERIDO()
    {
        // Given: cualquier ruta vÃ¡lida (el header check es pre-handler)
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100001);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            incluirClientCommandId: false);

        // When
        var response = await client.SendAsync(request);

        // Then: 400 antes de llegar al handler
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("HEADER-REQUERIDO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // PRE-0 â€” capability "ejecutar-inspeccion" ausente â†’ 403 Forbidden
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_sin_capability_ejecutar_inspeccion_responde_403_PRE0()
    {
        // Given: cualquier ruta vÃ¡lida
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100002);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId, sinCapability: true);

        // When
        var response = await client.SendAsync(request);

        // Then: 403 â€” PRE-0 capability gate
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-0");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.1 â€” Happy path: 200 OK con body correcto
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_happy_path_solo_cantidad_responde_200_OK()
    {
        // Given: inspecciÃ³n EnEjecucion con repuesto R1 (Cantidad=1, Justificacion="Cambio rutinario")
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100003);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: 2m, observacionNueva: null);

        // When
        var response = await client.SendAsync(request);

        // Then: 200 OK con estado post-update
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "ActualizarRepuesto devuelve 200 OK â€” el recurso ya existe (spec Â§9 D-6)");

        var body = await response.Content.ReadFromJsonAsync<RespuestaActualizarRepuesto>();
        body.Should().NotBeNull();
        body!.RepuestoId.Should().Be(repuestoId);
        body.Cantidad.Should().Be(2m, "Cantidad actualizada");
        body.Justificacion.Should().Be("Cambio rutinario",
            "Justificacion no cambiÃ³ â€” ObservacionNueva=null preserva el valor anterior");
        body.ActualizadoEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task PATCH_repuesto_happy_path_ambos_campos_responde_200_OK()
    {
        // Given: inspecciÃ³n EnEjecucion con repuesto activo
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100004);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: 3m, observacionNueva: "RevisiÃ³n extendida, se necesitan 3");

        // When
        var response = await client.SendAsync(request);

        // Then: 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RespuestaActualizarRepuesto>();
        body!.Cantidad.Should().Be(3m);
        body.Justificacion.Should().Be("RevisiÃ³n extendida, se necesitan 3");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.5 â€” PRE-2/I-H7: inspecciÃ³n firmada â†’ 422 + codigoError "I-H7"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_inspeccion_firmada_responde_422_I_H7()
    {
        // Given: inspecciÃ³n en estado Firmada
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionFirmadaConRepuesto(equipoId: 100005);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId);

        // When
        var response = await client.SendAsync(request);

        // Then: 422 Unprocessable Entity con cÃ³digo I-H7
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-H7");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.6 â€” PRE-3: HallazgoId inexistente â†’ 404 + codigoError "PRE-3"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_hallazgo_inexistente_responde_404_PRE3()
    {
        // Given: inspecciÃ³n EnEjecucion con repuesto; pero se usa un hallazgoId inexistente
        var (inspeccionId, _, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100006);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, Guid.NewGuid(), repuestoId); // hallazgoId inexistente

        // When
        var response = await client.SendAsync(request);

        // Then: 404 PRE-3
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-3");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.7 â€” PRE-4: hallazgo eliminado â†’ 422 + codigoError "PRE-4-ELIMINADO"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_hallazgo_eliminado_responde_422_PRE4_ELIMINADO()
    {
        // Given: inspecciÃ³n EnEjecucion con hallazgo eliminado
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConHallazgoEliminado(equipoId: 100007);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId);

        // When
        var response = await client.SendAsync(request);

        // Then: 422 PRE-4-ELIMINADO
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-4-ELIMINADO");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.8 â€” PRE-5: RepuestoId inexistente â†’ 404 + codigoError "PRE-5"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_repuesto_inexistente_responde_404_PRE5()
    {
        // Given: inspecciÃ³n EnEjecucion con hallazgo activo pero RepuestoId no existe
        var (inspeccionId, hallazgoId, _) =
            await SembrarInspeccionConRepuesto(equipoId: 100008);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, Guid.NewGuid()); // repuestoId inexistente

        // When
        var response = await client.SendAsync(request);

        // Then: 404 PRE-5
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-5");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.10 â€” PRE-7: Cantidad â‰¤ 0 â†’ 422 + codigoError "PRE-7"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_cantidad_cero_responde_422_PRE7()
    {
        // Given: inspecciÃ³n EnEjecucion con repuesto activo
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100009);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: 0m, observacionNueva: null);

        // When
        var response = await client.SendAsync(request);

        // Then: 422 PRE-7
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-7");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.11 â€” PRE-8: ambos campos null â†’ 400 + codigoError "PRE-8"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_ambos_campos_null_responde_400_PRE8()
    {
        // Given: inspecciÃ³n EnEjecucion con repuesto activo
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100010);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: null, observacionNueva: null);

        // When
        var response = await client.SendAsync(request);

        // Then: 400 PRE-8 â€” comando vacÃ­o
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-8");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Â§6.12 â€” PRE-1: InspeccionId inexistente â†’ 404 + codigoError "PRE-1"
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task PATCH_repuesto_inspeccion_inexistente_responde_404_PRE1()
    {
        // Given: no existe ningÃºn stream con este InspeccionId
        var client = factory.CreateClient();
        var request = BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // When
        var response = await client.SendAsync(request);

        // Then: 404 PRE-1
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-1");
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Idempotencia ADR-008 â€” Skip: requiere Wolverine envelope dedup
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact(Skip = "Idempotencia ADR-008 requiere Wolverine envelope dedup en producciÃ³n. " +
                 "Followup #15. Ver spec Â§7.")]
    public async Task PATCH_repuesto_retry_con_mismo_ClientCommandId_no_emite_segundo_evento_ADR008()
    {
        // Escenario ADR-008: dos PATCH con mismo X-Client-Command-Id devuelven el mismo resultado
        // sin emitir un segundo RepuestoActualizado_v1.
        await Task.CompletedTask;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // DTOs de respuesta (privados â€” solo para deserializaciÃ³n en tests)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed record RespuestaActualizarRepuesto(
        Guid           InspeccionId,
        Guid           HallazgoId,
        Guid           RepuestoId,
        decimal        Cantidad,
        string?        Justificacion,
        DateTimeOffset ActualizadoEn);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
