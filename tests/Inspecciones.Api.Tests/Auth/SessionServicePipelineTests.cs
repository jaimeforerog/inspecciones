using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Inspecciones.Api.Tests;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests.Auth;

/// <summary>
/// Tests E2E del pipeline de identidad del host PWA (spec slice mt-1 §6).
///
/// Cubre:
///   §6.1 — happy path en env Test con FakeSessionService default → 201 Created.
///   §6.3 — claim IdEmpresa ausente (FakeSessionService.IdEmpresa lanza
///          ClaimRequeridaException) → 401 Unauthorized + codigoError CLAIM-IDEMPRESA-AUSENTE.
///   §6.4 — capability "ejecutar-inspeccion" ausente → 403 Forbidden + codigoError PRE-1.
///   §6.5 — POST /api/v1/catalogos/sync sin capability → 403 Forbidden (cierre FU-52).
///   §6.6 — POST /api/v1/catalogos/sync con capability → 200 OK (regression existing erp-4).
///   §6.7 — FakeSessionService con IdUsuario=42 propaga TecnicoIniciador="42" al evento
///          (regression test que mt-2 extenderá para tenant_id).
///   §6.2 — Skip: requiere arrancar Program.cs en env Development con
///          SincoMiddlewareSessionService real (cubierto out-of-band en mt-2).
///
/// Estos tests asumen un hook <c>InspeccionesAppFactory.WithSessionService(fake)</c>
/// que reemplaza el <see cref="ISessionService"/> registrado en DI antes de crear el
/// cliente HTTP. Ese hook NO existe en el factory hoy — green lo añade junto con el
/// resto del cableado.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class SessionServicePipelineTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra (calcados de IniciarInspeccionEndpointTests.cs)
    // ─────────────────────────────────────────────────────────────────────

    private Task SembrarCatalogo(int equipoId, int proyectoId = 3, int rutinaId = 18) =>
        SembrarCatalogoEnTenant("1", equipoId, proyectoId, rutinaId);

    private async Task SembrarCatalogoEnTenant(string tenantId, int equipoId, int proyectoId = 3, int rutinaId = 18)
    {
        await using var session = factory.OpenSeedingSessionForTenant(tenantId);

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

    private static object NuevoIniciarInspeccionRequestBody(Guid inspeccionId, int equipoId, int proyectoId = 3) =>
        new
        {
            inspeccionId,
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

    // ─────────────────────────────────────────────────────────────────────
    // Test #3 — §6.1 happy path con FakeSessionService default → 201
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_con_FakeSessionService_default_responde_201_Created_y_emite_evento_con_TecnicoIniciador_desde_IdUsuario()
    {
        // Given: equipo+rutina sembrados, FakeSessionService con defaults (IdUsuario=1,
        // todas las capabilities). El factory por default registra el fake en env Test.
        const int equipoId = 50001;
        await SembrarCatalogo(equipoId);

        var inspeccionId = Guid.NewGuid();
        var body = NuevoIniciarInspeccionRequestBody(inspeccionId, equipoId);

        // When: POST sin Authorization header (env Test no requiere JWT — bypass §6.1).
        var client = factory.WithSessionService(new FakeSessionService()).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Then: 201 Created. El evento emitido tiene TecnicoIniciador="1" (string del
        // IdUsuario default del fake) — paridad funcional con el mock anterior pero
        // cableado al puerto.
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "happy path en env Test: el fake suple las claims sin requerir JWT");

        await using var verificacion = factory.OpenSeedingSessionForDefaultTenant();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        var iniciada = eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>().Single();
        iniciada.TecnicoIniciador.Should().Be("1",
            "D-MT1-5/D-MT1-6: ClaimsTecnico.TecnicoIniciador se construye desde " +
            "ISessionService.IdUsuario.ToString(CultureInfo.InvariantCulture).");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #4 — §6.3 claim IdEmpresa ausente → 401 + CLAIM-IDEMPRESA-AUSENTE
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_con_ISessionService_que_lanza_ClaimRequeridaException_en_IdEmpresa_responde_401_Unauthorized_PRE_AUTH_3()
    {
        // Given: equipo+rutina sembrados, fake que lanza ClaimRequeridaException("IdEmpresa")
        // al acceder al getter — simula JWT real con claim faltante (sin tocar el paquete
        // corporativo, decisión §12.B firmada).
        const int equipoId = 50002;
        await SembrarCatalogo(equipoId);

        var fakeQueLanza = new FakeSessionService(
            lanzarEnClaim: "IdEmpresa");

        var inspeccionId = Guid.NewGuid();
        var body = NuevoIniciarInspeccionRequestBody(inspeccionId, equipoId);

        // When
        var client = factory.WithSessionService(fakeQueLanza).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Then: 401 con código específico (no 500, no NRE, no genérico).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "PRE-AUTH-3: claim IdEmpresa ausente ⇒ 401 (problema del token, no de autorización).");

        var problema = await response.Content.ReadFromJsonAsync<ErrorBody>();
        problema.Should().NotBeNull();
        problema!.CodigoError.Should().Be("CLAIM-IDEMPRESA-AUSENTE",
            "spec §4 PRE-AUTH-3 — el handler de excepción global mapea " +
            "ClaimRequeridaException(\"IdEmpresa\") a este código.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #5 — §6.4 capability "ejecutar-inspeccion" ausente → 403 PRE-1
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_con_FakeSessionService_sin_capability_ejecutar_inspeccion_responde_403_Forbidden_PRE_CAP_1()
    {
        // Given: equipo+rutina sembrados, fake con SOLO "generar-ot" (sin "ejecutar-inspeccion").
        const int equipoId = 50003;
        await SembrarCatalogo(equipoId);

        var fakeSinCapability = new FakeSessionService(capabilities: new[] { "generar-ot" });

        var body = NuevoIniciarInspeccionRequestBody(Guid.NewGuid(), equipoId);

        // When
        var client = factory.WithSessionService(fakeSinCapability).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Then: 403 con el patrón fix-FU-38 (Forbidden403 helper).
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "PRE-CAP-1: capability faltante ⇒ 403 vía Forbidden403 (no Results.Forbid).");

        var problema = await response.Content.ReadFromJsonAsync<ErrorBody>();
        problema.Should().NotBeNull();
        problema!.CodigoError.Should().Be("PRE-1");
        problema.Mensaje.Should().Contain("ejecutar-inspeccion",
            "el mensaje debe nombrar la capability requerida (paridad con MensajeCapabilityEjecutarInspeccion).");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #6 — §6.5 POST /catalogos/sync sin capability → 403 (cierre FU-52)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_catalogos_sync_sin_capability_responde_403_Forbidden_cierre_FU_52()
    {
        // Given: fake con lista vacía de capabilities — el endpoint de sync hoy NO valida
        // capability (FU-52). El test rojo prueba el nuevo comportamiento del slice.
        var fakeSinCapability = new FakeSessionService(capabilities: Array.Empty<string>());

        // When
        var client = factory.WithSessionService(fakeSinCapability).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/catalogos/sync")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Then: 403 con código PRE-1 — D-MT1-9 cierra FU-52.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "D-MT1-9 + FU-52: /catalogos/sync gana capability check ('ejecutar-inspeccion' o 'administrar-catalogos').");

        var problema = await response.Content.ReadFromJsonAsync<ErrorBody>();
        problema.Should().NotBeNull();
        problema!.CodigoError.Should().Be("PRE-1");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #7 — §6.6 POST /catalogos/sync con capability → 200 (regression erp-4)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_catalogos_sync_con_capability_administrar_catalogos_no_devuelve_403_regression_erp_4()
    {
        // Given: fake con SOLO "administrar-catalogos" — el endpoint debe procesar la sync
        // normalmente (no 403). El status real puede ser 200/304/207 dependiendo del ETag/
        // mocks de catálogos; lo importante es que NO sea 403.
        var fakeConCapability = new FakeSessionService(
            capabilities: new[] { "administrar-catalogos" });

        // When
        var client = factory.WithSessionService(fakeConCapability).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/catalogos/sync")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Then: el endpoint no rechaza por PRE-1. El status puede variar (200/304/207)
        // pero NUNCA debe ser 403 cuando hay capability — eso prueba que la admisión
        // funciona y que erp-4 sigue verde.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "spec §6.6: con la capability presente, el endpoint procesa normalmente " +
            "(comportamiento existente de erp-4 preservado).");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #8 — §6.7 IdUsuario distinto al default propaga al evento
    // (regression que mt-2 extenderá para tenant_id).
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_con_FakeSessionService_IdUsuario_42_emite_evento_con_TecnicoIniciador_42_regression_mt_2()
    {
        // Given: fake con IdEmpresa=7, IdUsuario=42 (simula JWT real con UsuarioId=42 — spec §6.7).
        // mt-2: la siembra y la lectura ocurren en el tenant "7" para alinear con el FakeSessionService.
        const int equipoId = 50004;
        await SembrarCatalogoEnTenant("7", equipoId);

        var fakeNoDefault = new FakeSessionService(
            idEmpresa: 7,
            idUsuario: 42,
            nomUsuario: "rmartinez");

        var inspeccionId = Guid.NewGuid();
        var body = NuevoIniciarInspeccionRequestBody(inspeccionId, equipoId);

        // When
        var client = factory.WithSessionService(fakeNoDefault).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Then: 201 + el evento lleva TecnicoIniciador="42" (no "rmartinez", no "1").
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var verificacion = factory.OpenSeedingSessionForTenant("7");
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        var iniciada = eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>().Single();

        iniciada.TecnicoIniciador.Should().Be("42",
            "D-MT1-5/D-MT1-6: el endpoint construye ClaimsTecnico desde " +
            "ISessionService.IdUsuario (no hardcoded 'rmartinez'). " +
            "mt-2 extenderá este test para verificar tenant_id=7 en el documento Marten.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #9 — §6.2 JWT real ausente en env Development → 401 (SKIP).
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip =
        "spec §6.2 + §12.B firmada: el escenario requiere arrancar Program.cs con env " +
        "Development y registrar SincoMiddlewareSessionService real, lo cual implica " +
        "validar JWT firmado por el host PWA (paquete SincoSoft.MYE.Common). La validación " +
        "real (firma/issuer/exp) se cubre out-of-band en mt-2 cuando ya haya datos por " +
        "empresa, o se difiere a piloto. En mt-1 el bypass via ISessionService cubre los " +
        "escenarios §6.3/§6.4/§6.5 funcionalmente.")]
    public Task POST_inspecciones_sin_Authorization_en_env_Development_responde_401_Unauthorized_PRE_AUTH_1()
    {
        // Placeholder — se desbloquea cuando exista una forma testeable de instanciar
        // MiddlewareAuthorizationToken (mt-2 o slice dedicado de seguridad).
        throw new NotImplementedException(
            "Ver atributo [Fact(Skip=...)] arriba. mt-2 lo aborda.");
    }

    /// <summary>Body de error canonical de los endpoints del módulo.</summary>
    private sealed record ErrorBody(string CodigoError, string Mensaje);
}
