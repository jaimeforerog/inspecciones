using System.Net;
using System.Net.Http.Json;
using Inspecciones.Api.Tests.Auth;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests.Tenancy;

/// <summary>
/// Tests E2E del slice mt-2 — Marten Conjoined multi-tenancy.
///
/// Cubre:
///   §6.1 — Factory abre sesión con tenantId del ISessionService.
///   §6.2 — Cross-tenant isolation del aggregate: stream creado por tenant 7 no es visible
///          desde tenant 8 (MT2-INV-2).
///   §6.3 — Cross-tenant isolation de catálogos: documents de tenant 7 no aparecen en queries
///          del tenant 8 (MT2-INV-3).
///   §6.4 — Si el ISessionService lanza ClaimRequeridaException("IdEmpresa") cuando el factory
///          abre sesión, el endpoint mapea a 401.
///   §6.8 — Outbox conjoined-aware: Wolverine outbox persiste tenant_id y lo propaga al
///          listener (verificable indirectamente — el test asegura que el endpoint completa
///          con tenant 7 y el aggregate queda discriminado).
///
/// Reglas del slice:
///   - MT2-INV-1: toda apertura de sesión Marten en producción pasa por
///     ITenantedDocumentSessionFactory. Los tests siembran via OpenSessionForTenant
///     explícito para validar aislamiento.
///   - D5: TODOS los catálogos son Conjoined (EquipoLocal, RutinaTecnicaLocal,
///     CausaFallaCatalogo, TipoFallaCatalogo, RepuestoLocal, CatalogoSyncState,
///     RutinaMonitoreoLocal).
///
/// Estos tests requieren Postgres real (Testcontainers o POSTGRES_TEST_CONNSTRING).
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class MartenConjoinedTenancyTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    private const int Tenant7 = 7;
    private const int Tenant8 = 8;

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 — Factory abre sesión con tenantId del ISessionService
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TenantedDocumentSessionFactory_OpenSession_propaga_IdEmpresa_del_session_service_como_TenantId()
    {
        // GIVEN: FakeSessionService con IdEmpresa=7 registrado.
        var fake = new FakeSessionService(idEmpresa: Tenant7);
        var client = factory.WithSessionService(fake);
        var sp = client.Services;

        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();

        // WHEN: abrimos una sesión via el puerto.
        using var session = factoryUnderTest.OpenSession();

        // THEN: la sesión expone TenantId = "7" (string del IdEmpresa del fake).
        session.TenantId.Should().Be("7");
    }

    [Fact]
    public void TenantedDocumentSessionFactory_OpenQuerySession_propaga_IdEmpresa_como_TenantId()
    {
        var fake = new FakeSessionService(idEmpresa: Tenant8);
        var client = factory.WithSessionService(fake);
        var sp = client.Services;

        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();
        using var session = factoryUnderTest.OpenQuerySession();

        session.TenantId.Should().Be("8");
    }

    [Fact]
    public void TenantedDocumentSessionFactory_OpenSessionForTenant_acepta_tenant_arbitrario()
    {
        // Aplicación legal del bypass: listeners Wolverine que ya conocen el tenant del envelope.
        var fake = new FakeSessionService(idEmpresa: Tenant7);
        var client = factory.WithSessionService(fake);
        var sp = client.Services;

        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();
        using var session = factoryUnderTest.OpenSessionForTenant("99");

        session.TenantId.Should().Be("99");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 — Cross-tenant isolation del aggregate (MT2-INV-2)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_crea_stream_en_tenant_del_session_y_NO_es_visible_desde_otro_tenant()
    {
        // GIVEN: catálogo sembrado para ambos tenants (mismo equipoId, distinto tenant).
        const int equipoId = 7001;
        const int rutinaId = 7081;
        await SembrarCatalogoEnTenant(Tenant7, equipoId, rutinaId);
        await SembrarCatalogoEnTenant(Tenant8, equipoId, rutinaId);

        var inspeccionId = Guid.NewGuid();

        // WHEN: POST con tenant 7 → crea stream del aggregate.
        var fakeTenant7 = new FakeSessionService(idEmpresa: Tenant7);
        var clientTenant7 = factory.WithSessionService(fakeTenant7).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(NuevoIniciarInspeccionRequestBody(inspeccionId, equipoId)),
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var response = await clientTenant7.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // THEN: el stream existe en tenant 7 (lectura via factory tenant-aware).
        var sp = factory.Services;
        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();

        await using (var sesionTenant7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            var aggT7 = await sesionTenant7.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
            aggT7.Should().NotBeNull("el stream debe existir en el tenant que lo creó");
            aggT7!.EquipoId.Should().Be(equipoId);
        }

        // AND: el stream NO existe en tenant 8 (aislamiento cross-tenant — Marten Conjoined).
        await using (var sesionTenant8 = factoryUnderTest.OpenSessionForTenant("8"))
        {
            var aggT8 = await sesionTenant8.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
            aggT8.Should().BeNull("el stream no debe ser visible desde otro tenant — MT2-INV-2");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 — Cross-tenant isolation de catálogos (MT2-INV-3)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EquipoLocal_sembrado_en_tenant_7_no_aparece_en_query_de_tenant_8()
    {
        const int equipoId = 7002;
        const int rutinaId = 7082;

        // GIVEN: equipo sembrado solo en tenant 7.
        await SembrarCatalogoEnTenant(Tenant7, equipoId, rutinaId);

        // WHEN/THEN: query con tenant 7 lo encuentra; con tenant 8 NO.
        var sp = factory.Services;
        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();

        await using (var sesionTenant7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            var equipoT7 = await sesionTenant7.LoadAsync<EquipoLocal>(equipoId);
            equipoT7.Should().NotBeNull();
        }

        await using (var sesionTenant8 = factoryUnderTest.OpenSessionForTenant("8"))
        {
            var equipoT8 = await sesionTenant8.LoadAsync<EquipoLocal>(equipoId);
            equipoT8.Should().BeNull("MT2-INV-3: catálogos son por-empresa (D5)");
        }
    }

    [Fact]
    public async Task CatalogoSyncState_sembrado_en_tenant_7_no_aparece_en_query_de_tenant_8()
    {
        // GIVEN: state sembrado en tenant 7.
        var sp = factory.Services;
        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();

        await using (var sesionTenant7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            sesionTenant7.Store(new CatalogoSyncState
            {
                Id = "causas-falla",
                EtagActual = "\"etag-tenant-7\"",
                UltimaSyncExitosa = CapturadoEn,
                UltimaSyncIntento = CapturadoEn,
                UltimoEstado = "actualizado",
            });
            await sesionTenant7.SaveChangesAsync();
        }

        // WHEN/THEN: query tenant 8 no lo encuentra.
        await using (var sesionTenant8 = factoryUnderTest.OpenSessionForTenant("8"))
        {
            var stateT8 = await sesionTenant8.LoadAsync<CatalogoSyncState>("causas-falla");
            stateT8.Should().BeNull("MT2-INV-3: CatalogoSyncState por-empresa (D5)");
        }

        // AND: tenant 7 sí lo encuentra.
        await using (var sesionTenant7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            var stateT7 = await sesionTenant7.LoadAsync<CatalogoSyncState>("causas-falla");
            stateT7.Should().NotBeNull();
            stateT7!.EtagActual.Should().Be("\"etag-tenant-7\"");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 — Claim IdEmpresa ausente → 401 (heredado mt-1, validado en mt-2)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_con_IdEmpresa_ausente_responde_401_y_no_crea_stream()
    {
        var inspeccionId = Guid.NewGuid();
        const int equipoId = 7003;

        // GIVEN: FakeSessionService que lanza al leer IdEmpresa.
        var fake = new FakeSessionService(lanzarEnClaim: "IdEmpresa");
        var client = factory.WithSessionService(fake).CreateClient();

        // WHEN
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(NuevoIniciarInspeccionRequestBody(inspeccionId, equipoId)),
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);

        // THEN
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // AND: stream NO creado en ningún tenant (verificable indirectamente vía
        // QuerySession sin tenant, que en Conjoined retorna null para streams ajenos).
        var sp = factory.Services;
        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();
        await using var sesion = factoryUnderTest.OpenSessionForTenant("1");
        var agg = await sesion.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
        agg.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 — Smoke: endpoint completo end-to-end con tenant del session service
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_con_tenant_7_persiste_stream_discriminado_por_tenant_id_7()
    {
        const int equipoId = 7004;
        const int rutinaId = 7084;
        await SembrarCatalogoEnTenant(Tenant7, equipoId, rutinaId);

        var inspeccionId = Guid.NewGuid();
        var fake = new FakeSessionService(idEmpresa: Tenant7);
        var client = factory.WithSessionService(fake).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(NuevoIniciarInspeccionRequestBody(inspeccionId, equipoId)),
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verificar persistencia con tenant_id = "7" — abrimos sesión explícita y leemos.
        var sp = factory.Services;
        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();
        await using var sesion = factoryUnderTest.OpenSessionForTenant("7");
        var agg = await sesion.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
        agg.Should().NotBeNull();
        agg!.EquipoId.Should().Be(equipoId);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private async Task SembrarCatalogoEnTenant(int idEmpresa, int equipoId, int rutinaId)
    {
        var sp = factory.Services;
        var factoryUnderTest = sp.GetRequiredService<ITenantedDocumentSessionFactory>();
        var tenantId = idEmpresa.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await using var session = factoryUnderTest.OpenSessionForTenant(tenantId);

        session.Store(new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
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
                capturadoEn = CapturadoEn,
            },
            fechaReportada = DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
            lecturaMedidorPrimario = (object?)null,
            lecturaMedidorSecundario = (object?)null,
        };
}
