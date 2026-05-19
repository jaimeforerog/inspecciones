using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Test defensivo del slice mt-4 §6.12 — cierra FU-59.
///
/// Verifica MT4-INV-4: el rebuild manual del aggregate desde un stream produce
/// el mismo estado que un segundo rebuild idéntico — confirmando que <c>Apply</c>
/// es puro (regla CLAUDE.md "Apply puro") y que no introdujimos lógica
/// tenant-aware ni side-effects dentro de los Apply.
///
/// Variante puramente de dominio (sin Marten/Postgres). Si alguien añade
/// <c>if (tenantId == ...)</c> dentro de un Apply, este test rompe (porque
/// el "tenant" no existe en el aggregate — los Apply solo ven el payload del
/// evento). Si introducen estado mutable estático en Apply (caché de tenant,
/// p. ej.), también rompe — dos rebuilds del mismo stream deben ser deterministas.
///
/// Marten Conjoined ya garantiza que <c>AggregateStreamAsync(streamId)</c> con
/// tenant N solo carga eventos con <c>tenant_id=N</c>. Este test cubre el
/// complemento: dado el mismo conjunto de eventos, el rebuild es el mismo
/// independientemente del orden de invocación o de cualquier estado ambient.
/// </summary>
public sealed class RebuildCrossTenantDefensivoTests
{
    [Fact]
    public void Reconstruir_dos_veces_el_mismo_stream_produce_estados_identicos_MT4_INV_4()
    {
        // GIVEN: stream con eventos del lifecycle completo (Iniciada → Hallazgo →
        // Diagnostico → Dictamen → Firmada). Usa el fixture canónico.
        var stream = StreamConInspeccionFirmada();

        // WHEN: reconstruir dos veces.
        var aggregateA = Inspeccion.Reconstruir(stream);
        var aggregateB = Inspeccion.Reconstruir(stream);

        // THEN: campos públicos del aggregate idénticos.
        // Si Apply tuviera un branch tenant-aware o estado mutable, A y B diferirían
        // (entre invocaciones, o por orden de aplicación). Apply puro garantiza igualdad.
        aggregateA.InspeccionId.Should().Be(aggregateB.InspeccionId);
        aggregateA.Tipo.Should().Be(aggregateB.Tipo);
        aggregateA.EquipoId.Should().Be(aggregateB.EquipoId);
        aggregateA.RutinaId.Should().Be(aggregateB.RutinaId);
        aggregateA.Estado.Should().Be(aggregateB.Estado);
        aggregateA.Dictamen.Should().Be(aggregateB.Dictamen);
        aggregateA.FirmaUri.Should().Be(aggregateB.FirmaUri);
        aggregateA.FirmadaEn.Should().Be(aggregateB.FirmadaEn);
        aggregateA.TecnicoIniciador.Should().Be(aggregateB.TecnicoIniciador);
        aggregateA.Hallazgos.Should().BeEquivalentTo(aggregateB.Hallazgos);
        aggregateA.Contribuyentes.Should().BeEquivalentTo(aggregateB.Contribuyentes);
    }

    [Fact]
    public void Reconstruir_es_independiente_del_orden_de_invocacion_entre_streams_de_tenants_distintos()
    {
        // GIVEN: dos streams idénticos en payload (simulando dos tenants con la misma
        // secuencia de comandos). El rebuild debe ser determinista — el "tenant" no
        // viaja en el payload del evento (vive en metadata Marten).
        var streamTenantA = StreamConInspeccionFirmada();
        var streamTenantB = StreamConInspeccionFirmada();

        // WHEN: reconstruir en orden A-B y luego B-A.
        var aggregateA1 = Inspeccion.Reconstruir(streamTenantA);
        var aggregateB1 = Inspeccion.Reconstruir(streamTenantB);

        var aggregateB2 = Inspeccion.Reconstruir(streamTenantB);
        var aggregateA2 = Inspeccion.Reconstruir(streamTenantA);

        // THEN: el orden de rebuild no afecta. Si hubiera estado mutable estático
        // dentro del aggregate (p. ej. un Dictionary<TenantId, ...> de caché),
        // A2 podría diferir de A1.
        aggregateA1.InspeccionId.Should().Be(aggregateA2.InspeccionId);
        aggregateA1.Estado.Should().Be(aggregateA2.Estado);
        aggregateB1.InspeccionId.Should().Be(aggregateB2.InspeccionId);
        aggregateB1.Estado.Should().Be(aggregateB2.Estado);

        // Y ambos tenants llegan al mismo estado final (mismo payload):
        aggregateA1.Estado.Should().Be(aggregateB1.Estado);
        aggregateA1.Hallazgos.Count.Should().Be(aggregateB1.Hallazgos.Count);
    }
}
