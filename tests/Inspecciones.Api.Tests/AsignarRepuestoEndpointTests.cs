using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones/{id}/hallazgos/{hid}/repuestos</c>
/// contra la app real con Postgres en Testcontainers. Spec slice 1f Â§9.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class AsignarRepuestoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 7, 10, 0, 0, TimeSpan.FromHours(-5));

    private const int ParteEquipoId = 77;
    private const int SkuIdOk = 501;

    private async Task<(Guid InspeccionId, Guid HallazgoId)> SembrarInspeccionConHallazgo(int equipoId)
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
                MedicionOrigenId: null,      // Slice 1i: null para Manual (backward compat)
                EvaluacionOrigenId: null,    // Slice 1i': null para Manual (backward compat)
                ParteEquipoId: ParteEquipoId,
                ActividadId: null,
                ActividadDescripcion: "RevisiÃ³n sello hidrÃ¡ulico",
                NovedadTecnica: "Sello con desgaste avanzado",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar sello hidrÃ¡ulico",
                TipoFallaId: 3,
                CausaFallaId: 12,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "rmartinez",
                RegistradoEn: CapturadoEn));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId);
    }

    private async Task SembrarRepuestoLocal(int skuId = SkuIdOk, int[]? parteIdsCompatibles = null)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        await using var session = factory.OpenSeedingSessionForDefaultTenant();
        session.Store(new RepuestoLocal(
            SkuId: skuId,
            CodigoSinco: $"INS-{skuId}",
            Descripcion: "Sello hidrÃ¡ulico",
            UnidadMedida: "unidad",
            ParteIdsCompatibles: parteIdsCompatibles ?? [ParteEquipoId]));
        await session.SaveChangesAsync();
    }

    private static HttpRequestMessage NuevoRequest(
        Guid inspeccionId,
        Guid hallazgoId,
        Guid? repuestoId = null,
        int skuId = SkuIdOk,
        decimal cantidad = 2m,
        string? justificacion = "Sello desgastado") =>
        new(HttpMethod.Post,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos")
        {
            Content = JsonContent.Create(new
            {
                repuestoId = repuestoId ?? Guid.NewGuid(),
                skuId,
                cantidad,
                justificacion
            })
        };

    // â”€â”€ Happy path E2E â€” Â§6.1 via POST â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_repuesto_happy_path_responde_201_Created_con_body()
    {
        // Given: inspecciÃ³n EnEjecucion con hallazgo RequiereIntervencion + catÃ¡logo poblado
        await SembrarRepuestoLocal();
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 16001);
        var repuestoId = Guid.NewGuid();

        var client = factory.CreateClient();
        var request = NuevoRequest(inspeccionId, hallazgoId, repuestoId);
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 201 Created con todos los campos del body â€” spec Â§9
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Contain(repuestoId.ToString());

        var body = await response.Content.ReadFromJsonAsync<RespuestaAsignarRepuesto>();
        body.Should().NotBeNull();
        body!.RepuestoId.Should().Be(repuestoId);
        body.SkuId.Should().Be(SkuIdOk);
        body.SkuCodigo.Should().Be($"INS-{SkuIdOk}");
        body.Cantidad.Should().Be(2m);
        body.Unidad.Should().Be("unidad");
        body.Justificacion.Should().Be("Sello desgastado");
        body.AsignadoEn.Should().NotBe(default);
    }

    // â”€â”€ PRE-D: retry con mismo RepuestoId devuelve 201 idempotente â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_repuesto_retry_con_mismo_RepuestoId_responde_201_PRE_D()
    {
        // Given: primer intento exitoso ya procesado
        await SembrarRepuestoLocal();
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 16002);
        var repuestoId = Guid.NewGuid();
        var client = factory.CreateClient();

        var primerRequest = NuevoRequest(inspeccionId, hallazgoId, repuestoId);
        primerRequest.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        (await client.SendAsync(primerRequest)).StatusCode.Should().Be(HttpStatusCode.Created);

        // When: retry con mismo RepuestoId pero X-Client-Command-Id distinto
        var retryRequest = NuevoRequest(inspeccionId, hallazgoId, repuestoId);
        retryRequest.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var respuesta = await client.SendAsync(retryRequest);

        // Then: 201 â€” PRE-D silencioso, el aggregate retorna estado actual
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await respuesta.Content.ReadFromJsonAsync<RespuestaAsignarRepuesto>();
        body!.RepuestoId.Should().Be(repuestoId);
        body.SkuId.Should().Be(SkuIdOk);
    }

    // â”€â”€ PRE-F: InspeccionId inexistente devuelve 404 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_repuesto_inspeccion_inexistente_responde_404_PRE_F()
    {
        // Given: no existe ningÃºn stream con este InspeccionId
        await SembrarRepuestoLocal();
        var client = factory.CreateClient();
        var request = NuevoRequest(Guid.NewGuid(), Guid.NewGuid());
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 404 PRE-F
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-F");
    }

    // â”€â”€ PRE-H1: SkuId no existe en catÃ¡logo local â†’ 422 â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task POST_repuesto_sku_no_existe_en_catalogo_responde_422_PRE_H1()
    {
        // Given: catÃ¡logo no tiene SkuId=9999
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 16003);
        var client = factory.CreateClient();
        var request = NuevoRequest(inspeccionId, hallazgoId, skuId: 9999);
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 422 PRE-H1
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<RespuestaError>();
        body!.CodigoError.Should().Be("PRE-H1");
    }

    // â”€â”€ Compatibilidad SKU↔Parte ELIMINADA: cualquier SKU del catÃ¡logo es asignable â”€â”€
    // Antes esto daba 422 PRE-H2. DecisiÃ³n de negocio: no hay limitante sobre quÃ© insumo
    // se gasta en un hallazgo. AdemÃ¡s el ERP no expone ParteIdsCompatibles (sync lo deja
    // vacÃ­o), por lo que la validaciÃ³n rechazaba todo insumo en producciÃ³n.

    [Fact]
    public async Task POST_repuesto_sku_sin_compatibilidad_declarada_responde_201()
    {
        // Given: RepuestoLocal con ParteIdsCompatibles que NO incluye la parte del hallazgo
        // (incluso vacÃ­o, como en el sync real). Debe asignarse igual.
        await SembrarRepuestoLocal(skuId: 888, parteIdsCompatibles: []);
        var (inspeccionId, hallazgoId) = await SembrarInspeccionConHallazgo(equipoId: 16004);
        var client = factory.CreateClient();
        var request = NuevoRequest(inspeccionId, hallazgoId, skuId: 888);
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        // When
        var response = await client.SendAsync(request);

        // Then: 201 Created â€” sin restricciÃ³n de compatibilidad
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RespuestaAsignarRepuesto>();
        body!.SkuId.Should().Be(888);
    }

    private sealed record RespuestaAsignarRepuesto(
        Guid RepuestoId,
        int SkuId,
        string SkuCodigo,
        decimal Cantidad,
        string Unidad,
        string? Justificacion,
        DateTimeOffset AsignadoEn);

    private sealed record RespuestaError(string CodigoError, string Mensaje);
}
