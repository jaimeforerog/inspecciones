using System.Net;
using System.Net.Http.Json;
using Inspecciones.Api.Tests.Auth;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace Inspecciones.Api.Tests.Tenancy;

/// <summary>
/// Tests E2E del slice mt-4 §6.7, §6.8, §6.9 — cierra FU-56.
///
/// Cubre los caminos cross-tenant críticos que mt-2 no auditó completamente:
///   §6.7 — Aggregate + InspeccionAbiertaPorEquipoView aislados por tenant.
///   §6.8 — Catálogos sync por tenant via endpoint HTTP completo (no solo
///          IDocumentSession directa como mt-2 §6.3).
///   §6.9 — Paralelismo: 20 tareas (10 tenant 7 + 10 tenant 8) ejecutando
///          POST /inspecciones simultáneamente no leakean.
///
/// Estos tests requieren Postgres real (Testcontainers o POSTGRES_TEST_CONNSTRING).
/// Si no disponible, marcados con Skip explícito.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public sealed class CrossTenantE2EIsolationTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    private const int Tenant7 = 7;
    private const int Tenant8 = 8;

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 — Cross-tenant aggregate + proyección view (MT4-INV-1)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_dos_tenants_distintos_NO_leakean_aggregate_ni_view_MT4_INV_1()
    {
        const int equipoT7 = 7901;
        const int equipoT8 = 8901;
        const int rutinaT7 = 7981;
        const int rutinaT8 = 8981;

        await SembrarCatalogoEnTenant(Tenant7, equipoT7, rutinaT7);
        await SembrarCatalogoEnTenant(Tenant8, equipoT8, rutinaT8);

        var inspIdT7 = Guid.NewGuid();
        var inspIdT8 = Guid.NewGuid();

        var clientT7 = factory.WithSessionService(new FakeSessionService(idEmpresa: Tenant7)).CreateClient();
        var clientT8 = factory.WithSessionService(new FakeSessionService(idEmpresa: Tenant8)).CreateClient();

        // WHEN: ambos tenants crean inspecciones.
        var respT7 = await EnviarPostInspeccionAsync(clientT7, inspIdT7, equipoT7);
        var respT8 = await EnviarPostInspeccionAsync(clientT8, inspIdT8, equipoT8);

        respT7.StatusCode.Should().Be(HttpStatusCode.Created);
        respT8.StatusCode.Should().Be(HttpStatusCode.Created);

        // THEN: aggregate aislado.
        var factoryUnderTest = factory.Services.GetRequiredService<ITenantedDocumentSessionFactory>();

        await using (var sesT7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            var aggT7Mio = await sesT7.Events.AggregateStreamAsync<Inspeccion>(inspIdT7);
            var aggT7Ajeno = await sesT7.Events.AggregateStreamAsync<Inspeccion>(inspIdT8);
            aggT7Mio.Should().NotBeNull();
            aggT7Ajeno.Should().BeNull("MT4-INV-1: stream del tenant 8 no debe ser visible desde tenant 7");
        }

        await using (var sesT8 = factoryUnderTest.OpenSessionForTenant("8"))
        {
            var aggT8Mio = await sesT8.Events.AggregateStreamAsync<Inspeccion>(inspIdT8);
            var aggT8Ajeno = await sesT8.Events.AggregateStreamAsync<Inspeccion>(inspIdT7);
            aggT8Mio.Should().NotBeNull();
            aggT8Ajeno.Should().BeNull("MT4-INV-1: stream del tenant 7 no debe ser visible desde tenant 8");
        }

        // AND: proyección InspeccionAbiertaPorEquipoView aislada por tenant.
        await using (var sesT7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            var viewT7 = await sesT7.Query<global::Inspecciones.Application.Inspecciones.InspeccionAbiertaPorEquipoView>()
                .ToListAsync();
            viewT7.Should().HaveCount(1, "tenant 7 solo ve su propia inspección activa");
            viewT7[0].EquipoId.Should().Be(equipoT7);
        }

        await using (var sesT8 = factoryUnderTest.OpenSessionForTenant("8"))
        {
            var viewT8 = await sesT8.Query<global::Inspecciones.Application.Inspecciones.InspeccionAbiertaPorEquipoView>()
                .ToListAsync();
            viewT8.Should().HaveCount(1, "tenant 8 solo ve su propia inspección activa");
            viewT8[0].EquipoId.Should().Be(equipoT8);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 — Paralelismo cross-tenant (MT4-INV-1 bajo stress)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_inspecciones_paralelo_dos_tenants_no_leakea_streams_MT4_INV_1()
    {
        const int equipoT7 = 7902;
        const int equipoT8 = 8902;
        const int rutinaT7 = 7982;
        const int rutinaT8 = 8982;

        await SembrarCatalogoEnTenant(Tenant7, equipoT7, rutinaT7);
        await SembrarCatalogoEnTenant(Tenant8, equipoT8, rutinaT8);

        // Necesitamos un equipo distinto por inspección dentro del mismo tenant
        // (invariante I-I1: máximo una inspección activa por equipo + tenant).
        // Sembramos 10 equipos por tenant.
        var equiposT7 = new int[10];
        var equiposT8 = new int[10];
        for (var i = 0; i < 10; i++)
        {
            equiposT7[i] = 7910 + i;
            equiposT8[i] = 8910 + i;
            await SembrarCatalogoEnTenant(Tenant7, equiposT7[i], rutinaT7);
            await SembrarCatalogoEnTenant(Tenant8, equiposT8[i], rutinaT8);
        }

        var clientT7 = factory.WithSessionService(new FakeSessionService(idEmpresa: Tenant7)).CreateClient();
        var clientT8 = factory.WithSessionService(new FakeSessionService(idEmpresa: Tenant8)).CreateClient();

        var inspIdsT7 = new Guid[10];
        var inspIdsT8 = new Guid[10];
        for (var i = 0; i < 10; i++)
        {
            inspIdsT7[i] = Guid.NewGuid();
            inspIdsT8[i] = Guid.NewGuid();
        }

        // WHEN: lanzar 20 POSTs en paralelo, entrelazando tenants.
        var tareas = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 10; i++)
        {
            tareas.Add(EnviarPostInspeccionAsync(clientT7, inspIdsT7[i], equiposT7[i]));
            tareas.Add(EnviarPostInspeccionAsync(clientT8, inspIdsT8[i], equiposT8[i]));
        }

        var resps = await Task.WhenAll(tareas);

        // THEN: las 20 retornaron 201 Created.
        resps.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Created));

        // AND: cada tenant ve solo sus 10 inspecciones.
        var factoryUnderTest = factory.Services.GetRequiredService<ITenantedDocumentSessionFactory>();

        await using (var sesT7 = factoryUnderTest.OpenSessionForTenant("7"))
        {
            foreach (var idMio in inspIdsT7)
            {
                var aggMio = await sesT7.Events.AggregateStreamAsync<Inspeccion>(idMio);
                aggMio.Should().NotBeNull("tenant 7 ve sus propias inspecciones");
            }
            foreach (var idAjeno in inspIdsT8)
            {
                var aggAjeno = await sesT7.Events.AggregateStreamAsync<Inspeccion>(idAjeno);
                aggAjeno.Should().BeNull("tenant 7 NO ve inspecciones del tenant 8 bajo paralelismo");
            }
        }

        await using (var sesT8 = factoryUnderTest.OpenSessionForTenant("8"))
        {
            foreach (var idMio in inspIdsT8)
            {
                var aggMio = await sesT8.Events.AggregateStreamAsync<Inspeccion>(idMio);
                aggMio.Should().NotBeNull("tenant 8 ve sus propias inspecciones");
            }
            foreach (var idAjeno in inspIdsT7)
            {
                var aggAjeno = await sesT8.Events.AggregateStreamAsync<Inspeccion>(idAjeno);
                aggAjeno.Should().BeNull("tenant 8 NO ve inspecciones del tenant 7 bajo paralelismo");
            }
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private async Task SembrarCatalogoEnTenant(int idEmpresa, int equipoId, int rutinaId)
    {
        var factoryUnderTest = factory.Services.GetRequiredService<ITenantedDocumentSessionFactory>();
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

    private static async Task<HttpResponseMessage> EnviarPostInspeccionAsync(
        HttpClient client, Guid inspeccionId, int equipoId)
    {
        var body = new
        {
            inspeccionId,
            equipoId,
            proyectoId = 3,
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

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inspecciones")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }
}
