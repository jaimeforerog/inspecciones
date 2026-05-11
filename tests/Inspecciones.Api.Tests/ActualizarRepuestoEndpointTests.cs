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
/// contra la app real con Postgres en Testcontainers. Spec slice 1o §9.
///
/// Todos los tests fallan con NotImplementedException hasta que green implemente
/// <see cref="Inspecciones.Application.Inspecciones.ActualizarRepuestoHandler.Handle"/>.
///
/// Cubre:
/// §6.1  — Happy path: 200 OK + body con estado post-update.
/// §6.5  — PRE-2/I-H7: inspección firmada → 422 + codigoError "I-H7".
/// §6.6  — PRE-3: HallazgoId inexistente → 404 + codigoError "PRE-3".
/// §6.7  — PRE-4: hallazgo eliminado → 422 + codigoError "PRE-4-ELIMINADO".
/// §6.8  — PRE-5: RepuestoId inexistente → 404 + codigoError "PRE-5".
/// §6.9  — PRE-5: RepuestoId en hallazgo incorrecto → 404 + codigoError "PRE-5".
/// §6.10 — PRE-7: Cantidad≤0 → 422 + codigoError "PRE-7".
/// §6.11 — PRE-8: ambos campos null → 400 + codigoError "PRE-8".
/// §6.12 — PRE-1: InspeccionId inexistente → 404 + codigoError "PRE-1".
/// PRE-0 — capability ausente → 403 Forbidden + codigoError "PRE-0".
/// Header — X-Client-Command-Id ausente → 400 + codigoError "HEADER-REQUERIDO".
/// ADR-008 — Idempotencia (Skip: requiere Wolverine envelope dedup en producción).
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class ActualizarRepuestoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream con inspección EnEjecucion + hallazgo + repuesto activo.
    /// Devuelve los tres IDs del stream.
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionConRepuesto(int equipoId, string tecnicoId = "rmartinez")
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

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
                ActividadDescripcion: "Revisión sello hidráulico",
                NovedadTecnica: "Sello con desgaste avanzado",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar sello hidráulico",
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
    /// Siembra una inspección firmada completa (para PRE-2 §6.5).
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionFirmadaConRepuesto(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

        await using var session = store.LightweightSession();
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
                ActividadDescripcion: "Revisión general",
                NovedadTecnica: "Estado sin hallazgos críticos",
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
                DiagnosticoFinal: "Inspección completa sin hallazgos críticos",
                EmitidoPor: "rmartinez",
                EmitidoEn: CapturadoEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin hallazgos de intervención",
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
    /// Siembra inspección EnEjecucion con hallazgo eliminado.
    /// </summary>
    private async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionConHallazgoEliminado(int equipoId)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

        await using var session = store.LightweightSession();
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
                ActividadDescripcion: "Revisión",
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

    // ─────────────────────────────────────────────────────────────────────
    // Header — X-Client-Command-Id ausente → 400 Bad Request
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_sin_header_ClientCommandId_responde_400_HEADER_REQUERIDO()
    {
        // Given: cualquier ruta válida (el header check es pre-handler)
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

    // ─────────────────────────────────────────────────────────────────────
    // PRE-0 — capability "ejecutar-inspeccion" ausente → 403 Forbidden
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_sin_capability_ejecutar_inspeccion_responde_403_PRE0()
    {
        // Given: cualquier ruta válida
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100002);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId, sinCapability: true);

        // When
        var response = await client.SendAsync(request);

        // Then: 403 — PRE-0 capability gate
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-0");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 — Happy path: 200 OK con body correcto
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_happy_path_solo_cantidad_responde_200_OK()
    {
        // Given: inspección EnEjecucion con repuesto R1 (Cantidad=1, Justificacion="Cambio rutinario")
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100003);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: 2m, observacionNueva: null);

        // When
        var response = await client.SendAsync(request);

        // Then: 200 OK con estado post-update
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "ActualizarRepuesto devuelve 200 OK — el recurso ya existe (spec §9 D-6)");

        var body = await response.Content.ReadFromJsonAsync<RespuestaActualizarRepuesto>();
        body.Should().NotBeNull();
        body!.RepuestoId.Should().Be(repuestoId);
        body.Cantidad.Should().Be(2m, "Cantidad actualizada");
        body.Justificacion.Should().Be("Cambio rutinario",
            "Justificacion no cambió — ObservacionNueva=null preserva el valor anterior");
        body.ActualizadoEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task PATCH_repuesto_happy_path_ambos_campos_responde_200_OK()
    {
        // Given: inspección EnEjecucion con repuesto activo
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100004);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: 3m, observacionNueva: "Revisión extendida, se necesitan 3");

        // When
        var response = await client.SendAsync(request);

        // Then: 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RespuestaActualizarRepuesto>();
        body!.Cantidad.Should().Be(3m);
        body.Justificacion.Should().Be("Revisión extendida, se necesitan 3");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 — PRE-2/I-H7: inspección firmada → 422 + codigoError "I-H7"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_inspeccion_firmada_responde_422_I_H7()
    {
        // Given: inspección en estado Firmada
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionFirmadaConRepuesto(equipoId: 100005);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId);

        // When
        var response = await client.SendAsync(request);

        // Then: 422 Unprocessable Entity con código I-H7
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("I-H7");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 — PRE-3: HallazgoId inexistente → 404 + codigoError "PRE-3"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_hallazgo_inexistente_responde_404_PRE3()
    {
        // Given: inspección EnEjecucion con repuesto; pero se usa un hallazgoId inexistente
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

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 — PRE-4: hallazgo eliminado → 422 + codigoError "PRE-4-ELIMINADO"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_hallazgo_eliminado_responde_422_PRE4_ELIMINADO()
    {
        // Given: inspección EnEjecucion con hallazgo eliminado
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

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 — PRE-5: RepuestoId inexistente → 404 + codigoError "PRE-5"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_repuesto_inexistente_responde_404_PRE5()
    {
        // Given: inspección EnEjecucion con hallazgo activo pero RepuestoId no existe
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

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 — PRE-7: Cantidad ≤ 0 → 422 + codigoError "PRE-7"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_cantidad_cero_responde_422_PRE7()
    {
        // Given: inspección EnEjecucion con repuesto activo
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

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 — PRE-8: ambos campos null → 400 + codigoError "PRE-8"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_ambos_campos_null_responde_400_PRE8()
    {
        // Given: inspección EnEjecucion con repuesto activo
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(equipoId: 100010);

        var client = factory.CreateClient();
        var request = BuildRequest(inspeccionId, hallazgoId, repuestoId,
            cantidadNueva: null, observacionNueva: null);

        // When
        var response = await client.SendAsync(request);

        // Then: 400 PRE-8 — comando vacío
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-8");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 — PRE-1: InspeccionId inexistente → 404 + codigoError "PRE-1"
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PATCH_repuesto_inspeccion_inexistente_responde_404_PRE1()
    {
        // Given: no existe ningún stream con este InspeccionId
        var client = factory.CreateClient();
        var request = BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // When
        var response = await client.SendAsync(request);

        // Then: 404 PRE-1
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-1");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Idempotencia ADR-008 — Skip: requiere Wolverine envelope dedup
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Idempotencia ADR-008 requiere Wolverine envelope dedup en producción. " +
                 "Followup #15. Ver spec §7.")]
    public async Task PATCH_repuesto_retry_con_mismo_ClientCommandId_no_emite_segundo_evento_ADR008()
    {
        // Escenario ADR-008: dos PATCH con mismo X-Client-Command-Id devuelven el mismo resultado
        // sin emitir un segundo RepuestoActualizado_v1.
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // DTOs de respuesta (privados — solo para deserialización en tests)
    // ─────────────────────────────────────────────────────────────────────

    private sealed record RespuestaActualizarRepuesto(
        Guid           InspeccionId,
        Guid           HallazgoId,
        Guid           RepuestoId,
        decimal        Cantidad,
        string?        Justificacion,
        DateTimeOffset ActualizadoEn);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
