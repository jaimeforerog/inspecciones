using System.Net;
using System.Net.Http.Json;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>POST /api/v1/inspecciones</c> contra la app real
/// con Postgres en Testcontainers. Cubre §6.1 (happy path E2E con
/// <c>WebApplicationFactory</c> + Postgres real), §6.4 (idempotencia de cliente —
/// replay con mismo <c>X-Client-Command-Id</c> devuelve respuesta original sin
/// reaplicar).
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class IniciarInspeccionEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn = new(2026, 5, 6, 8, 30, 12, TimeSpan.FromHours(-5));

    private static object NuevoRequestBody(Guid? inspeccionId = null, int equipoId = 4521, int proyectoId = 3) =>
        new
        {
            inspeccionId = inspeccionId ?? Guid.NewGuid(),
            equipoId,
            proyectoId,
            ubicacionInicio = new
            {
                latitud = 4.711m,
                longitud = -74.072m,
                precisionMetros = 8.5m,
                capturadoEn = CapturadoEn
            },
            fechaReportada = DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
            lecturaMedidorPrimario = (object?)null,
            lecturaMedidorSecundario = (object?)null
        };

    private async Task SembrarCatalogo(int equipoId = 4521, int proyectoId = 3, int rutinaId = 18)
    {
        await using var session = factory.OpenSeedingSessionForDefaultTenant();

        session.Store(new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: proyectoId,
            RutinaTecnicaId: rutinaId));

        session.Store(new RutinaTecnicaLocal(
            RutinaId: rutinaId,
            Codigo: "INSP. BULL.MOTOR",
            Nombre: "Inspección bulldozer motor",
            Tipo: TipoRutina.Tecnica,
            GrupoMantenimiento: "BULLDOZER",
            ParteId: 88,
            ParteCodigo: "MOTOR",
            SincronizadoEn: CapturadoEn.AddDays(-1)));

        await session.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 — Happy path E2E HTTP → handler → Marten → 201 Created
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_happy_path_responde_201_Created_con_InspeccionId()
    {
        // Given: catálogo poblado y un comando válido con clientCommandId fresco
        const int equipoId = 4521;
        await SembrarCatalogo(equipoId: equipoId);

        var inspeccionId = Guid.NewGuid();
        var clientCommandId = Guid.NewGuid().ToString();
        var body = NuevoRequestBody(inspeccionId: inspeccionId, equipoId: equipoId);

        // When: POST con header X-Client-Command-Id
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Client-Command-Id", clientCommandId);

        var response = await client.SendAsync(request);

        // Then: 201 Created, header Location apuntando al recurso, body con el result
        response.StatusCode.Should().Be(HttpStatusCode.Created, "es una creación nueva (no replay, no redirige)");
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(inspeccionId.ToString(), "el header Location debe apuntar a /api/v1/inspecciones/{InspeccionId}");

        var resultado = await response.Content.ReadFromJsonAsync<RespuestaIniciarInspeccion>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId);
        resultado.RedirigeAExistente.Should().BeFalse();
        resultado.Version.Should().Be(1, "stream nuevo, primer evento ⇒ versión 1");
        resultado.Mensaje.Should().BeNull("happy path no lleva mensaje");

        // Verificación profunda: el evento está en el event store y la proyección poblada.
        // mt-2: lectura con tenant default ("1") — Conjoined requiere tenant explícito.
        await using var verificacion = factory.OpenSeedingSessionForDefaultTenant();

        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("happy path debe persistir exactamente un InspeccionIniciada_v1");

        var fila = await verificacion.LoadAsync<InspeccionAbiertaPorEquipoView>(equipoId);
        fila.Should().NotBeNull("la proyección inline corre en la misma transacción");
        fila!.InspeccionId.Should().Be(inspeccionId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 — Idempotencia de cliente: replay con mismo X-Client-Command-Id
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_replay_con_mismo_ClientCommandId_no_duplica_evento_idempotencia_ADR_008()
    {
        // Given: catálogo poblado, request original ya ejecutado exitosamente
        const int equipoId = 4522;
        await SembrarCatalogo(equipoId: equipoId);

        var inspeccionId = Guid.NewGuid();
        var clientCommandId = Guid.NewGuid().ToString();
        var body = NuevoRequestBody(inspeccionId: inspeccionId, equipoId: equipoId);

        var client = factory.CreateClient();

        var primerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        primerRequest.Headers.Add("X-Client-Command-Id", clientCommandId);
        var primeraRespuesta = await client.SendAsync(primerRequest);
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created, "primer envío del comando: creación nueva");

        // When: el cliente reintenta tras timeout con el MISMO clientCommandId y body
        var segundoRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        segundoRequest.Headers.Add("X-Client-Command-Id", clientCommandId);
        var segundaRespuesta = await client.SendAsync(segundoRequest);

        // Then: el segundo request es replay idempotente — Wolverine envelope dedup detecta
        // el MessageId repetido y devuelve la respuesta original sin reaplicar el handler.
        // Status: 200 OK (no 201 — ya no es creación nueva). Body idéntico al original.
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.OK,
            "ADR-008 §9.16 — Wolverine replay devuelve 200 con la respuesta cacheada del envelope");

        var resultado = await segundaRespuesta.Content.ReadFromJsonAsync<RespuestaIniciarInspeccion>();
        resultado.Should().NotBeNull();
        resultado!.InspeccionId.Should().Be(inspeccionId, "el replay debe devolver el mismo InspeccionId del envío original");

        // Verificación profunda: el stream tiene UN SOLO evento (no se duplicó por el replay).
        // mt-2: lectura con tenant default ("1") — Conjoined requiere tenant explícito.
        await using var verificacion = factory.OpenSeedingSessionForDefaultTenant();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("envelope dedup ADR-008 garantiza que el replay no duplica el evento");
    }

    /// <summary>DTO de lectura local del response — duplicado del Response de la API
    /// para mantener el test independiente del namespace concreto.</summary>
    private sealed record RespuestaIniciarInspeccion(
        Guid InspeccionId,
        bool RedirigeAExistente,
        int Version,
        string? Mensaje);
}
