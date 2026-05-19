using FluentAssertions;
using Inspecciones.Infrastructure.Auth;

namespace Inspecciones.Api.Tests.Auth;

/// <summary>
/// Tests unitarios del fake <see cref="FakeSessionService"/> que reemplaza a
/// <c>SincoMiddlewareSessionService</c> en env <c>Test</c> (spec slice mt-1 §2, D-MT1-2).
///
/// El fake es una clase de la suite de tests (no del runtime de producción) — vive en
/// <c>tests/Inspecciones.Api.Tests/Auth/FakeSessionService.cs</c> y se registra como
/// <see cref="ISessionService"/> en <see cref="InspeccionesAppFactory"/>.
///
/// Estos tests no son end-to-end; son contrato del fake.
/// </summary>
public class FakeSessionServiceTests
{
    // ─────────────────────────────────────────────────────────────────────
    // Test #1 — default constructor expone los 5 claims y el set completo
    // de capabilities (spec §2 + D-MT1-2 + D-MT1-4).
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void FakeSessionService_constructor_default_expone_los_5_claims_canonical_y_set_completo_de_capabilities()
    {
        // Given: un FakeSessionService con valores default (sin parámetros).
        var session = new FakeSessionService();

        // Then: los 5 claims canónicos del JWT del host (06-contrato-apis-erp.md §0.B.5)
        // están presentes y son los defaults del fake.
        session.IdEmpresa.Should().Be(1, "default empresa de tests — paridad Attachment");
        session.IdUsuario.Should().Be(1, "default usuario de tests — paridad Attachment");
        session.NomUsuario.Should().Be("TestUser", "paridad con bypass del proyecto Attachment");
        session.IdSucursal.Should().Be(0, "0 = no aplica en defaults (spec §2)");
        session.IdProyecto.Should().Be(0, "0 = no aplica en defaults (spec §2)");

        // D-MT1-4: capabilities default incluyen las tres del MVP.
        session.Capabilities.Should().BeEquivalentTo(new[]
        {
            "ejecutar-inspeccion",
            "generar-ot",
            "administrar-catalogos"
        }, "spec §2 + D-MT1-4: default always-allow hasta que el host emita la claim");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test #2 — constructor de override permite forzar capabilities = []
    // (necesario para los tests E2E §6.4 y §6.5 que verifican el 403).
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void FakeSessionService_constructor_con_capabilities_vacias_devuelve_lista_vacia_PRE_CAP_1()
    {
        // Given: fake con capabilities explícitamente vacías (caso §6.5 — cierre FU-52).
        var session = new FakeSessionService(capabilities: Array.Empty<string>());

        // Then: el getter devuelve la lista vacía exactamente — el endpoint que valide
        // PRE-CAP-1 (capability requerida) devolverá 403 Forbidden.
        session.Capabilities.Should().BeEmpty(
            "PRE-CAP-1: cuando el host PWA no propaga la capability esperada, " +
            "el endpoint debe rechazar con 403 (helper Forbidden403, fix FU-38).");

        // Y los demás claims mantienen los defaults sensibles para no bloquear el resto
        // del happy path.
        session.IdEmpresa.Should().Be(1);
        session.IdUsuario.Should().Be(1);
    }
}
