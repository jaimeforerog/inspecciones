using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.IniciarMonitoreo"/>.
/// Cobertura de los escenarios §6.1, §6.9, §6.10, §6.11, §6.12, §6.14 del spec
/// del slice 1h (escenarios de aggregate puro). Los escenarios §6.2..§6.8 y §6.13
/// involucran Marten real o infraestructura y viven en
/// <c>Inspecciones.Application.Tests</c>.
/// </summary>
public class IniciarInspeccionMonitoreoTests
{
    // ─────────────────────────────────────────────────────────────────────
    // §6.1 happy path — inicio de inspección de monitoreo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarMonitoreo_sobre_stream_vacio_emite_InspeccionIniciada_v1_con_Tipo_Monitoreo()
    {
        // Given
        var dados = Array.Empty<object>();
        var cmd = ComandoMonitoreo();
        var claims = ClaimsMonitoreo();
        var items = ItemsSnapshot();

        // When
        var resultado = CasoDeUso.IniciarMonitoreo(
            dados, cmd, claims,
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: items,
            ahora: Ahora);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionIniciada_v1>()
            .Which.Tipo.Should().Be(TipoInspeccion.Monitoreo);
    }

    [Fact]
    public void IniciarMonitoreo_emite_evento_con_payload_completo_incluyendo_snapshot_y_rutina()
    {
        // Given
        var cmd = ComandoMonitoreo();
        var claims = ClaimsMonitoreo();
        var items = ItemsSnapshot();

        // When
        var resultado = CasoDeUso.IniciarMonitoreo(
            Array.Empty<object>(), cmd, claims,
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: items,
            ahora: Ahora);

        // Then
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionIniciada_v1>().Subject;

        evt.InspeccionId.Should().Be(cmd.InspeccionId);
        evt.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        evt.EquipoId.Should().Be(4521);
        evt.RutinaId.Should().Be(42, "RutinaId lleva el RutinaMonitoreoId cuando Tipo=Monitoreo");
        evt.RutinaCodigo.Should().Be("Sistema eléctrico");
        evt.TecnicoIniciador.Should().Be("ana.gomez");
        evt.ProyectoId.Should().Be(3);
        evt.Ubicacion.Should().Be(cmd.Ubicacion);
        evt.IniciadaEn.Should().Be(Ahora);
        evt.FechaReportada.Should().Be(cmd.FechaReportada);
        evt.RutinaMonitoreoSeleccionadaId.Should().Be(42);
        evt.ItemsSnapshot.Should().NotBeNull().And.HaveCount(2);
        evt.LecturaMedidorPrimario.Should().BeNull();
        evt.LecturaMedidorSecundario.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 PRE-9 / I-I3 — FechaReportada futura
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarMonitoreo_con_FechaReportada_futura_lanza_FechaReportadaFueraDeRangoException_I_I3()
    {
        // Given
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var manana = hoy.AddDays(1);
        var cmd = ComandoMonitoreo(fechaReportada: manana);
        var items = ItemsSnapshot();

        // When
        var act = () => CasoDeUso.IniciarMonitoreo(
            Array.Empty<object>(), cmd, ClaimsMonitoreo(),
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: items,
            ahora: Ahora);

        // Then
        act.Should().Throw<FechaReportadaFueraDeRangoException>()
            .WithMessage("*futura*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 PRE-9 / I-I3 — FechaReportada con más de 30 días retroactivos
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarMonitoreo_con_FechaReportada_mas_de_30_dias_atras_lanza_FechaReportadaFueraDeRangoException_I_I3()
    {
        // Given
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var treintaYTresDiasAtras = hoy.AddDays(-33);
        var cmd = ComandoMonitoreo(fechaReportada: treintaYTresDiasAtras);
        var items = ItemsSnapshot();

        // When
        var act = () => CasoDeUso.IniciarMonitoreo(
            Array.Empty<object>(), cmd, ClaimsMonitoreo(),
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: items,
            ahora: Ahora);

        // Then: el mensaje debe mencionar el mínimo aceptable para que el técnico pueda diagnosticar.
        act.Should().Throw<FechaReportadaFueraDeRangoException>()
            .WithMessage("*30 días*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 PRE-8 / I-I2 defensa en profundidad — proyecto no autorizado
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarMonitoreo_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizadoException_PRE_8()
    {
        // Given: claims solo asignan proyectos {1, 2}; comando usa proyecto 99.
        var claims = new ClaimsTecnico(
            TecnicoIniciador: "ana.gomez",
            ProyectosAsignados: new HashSet<int> { 1, 2 },
            TieneCapabilityEjecutarInspeccion: true);
        var cmd = ComandoMonitoreo(proyectoId: 99);
        var items = ItemsSnapshot();

        // When
        var act = () => CasoDeUso.IniciarMonitoreo(
            Array.Empty<object>(), cmd, claims,
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: items,
            ahora: Ahora);

        // Then
        act.Should().Throw<ProyectoNoAutorizadoException>()
            .WithMessage("*99*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 Snapshot solo incluye items activos
    // (este escenario valida en el aggregate que el snapshot llega completo
    // y se refleja en el evento — la lógica de filtrado vive en el handler)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarMonitoreo_snapshot_con_dos_items_activos_los_propaga_al_evento_sin_modificar()
    {
        // Given: handler ya filtró — pasa exactamente ItemId=1 e ItemId=3 (Activo=true)
        var itemsActivos = new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1, Parte: "Batería",    Actividad: "Medir voltaje",  Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m)),
            new(ItemId: 3, Parte: "Alternador", Actividad: "Estado general", Evaluacion: new EvaluacionCualitativaEsperada()),
        };
        var cmd = ComandoMonitoreo();

        // When
        var resultado = CasoDeUso.IniciarMonitoreo(
            Array.Empty<object>(), cmd, ClaimsMonitoreo(),
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: itemsActivos,
            ahora: Ahora);

        // Then: el evento lleva exactamente los 2 items — ItemId=2 (inactivo) no aparece.
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionIniciada_v1>().Subject;

        evt.ItemsSnapshot.Should().NotBeNull()
            .And.HaveCount(2, "el handler filtra items con Activo=false antes de llamar al aggregate");
        evt.ItemsSnapshot!.Select(i => i.ItemId).Should().BeEquivalentTo(new[] { 1, 3 },
            "ItemId=2 (Activo=false) no forma parte del snapshot — spec §6.12 / decisión D4");
        evt.ItemsSnapshot.Should().NotContain(i => i.ItemId == 2,
            "ItemId=2 fue marcado Activo=false — no debe aparecer en el snapshot");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Rebuild desde stream (obligatorio — CLAUDE.md + spec §6.14)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarMonitoreo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones()
    {
        // Given: ejecutar el comando happy path para obtener el evento emitido
        var cmd = ComandoMonitoreo();
        var items = ItemsSnapshot();
        var emitidos = CasoDeUso.IniciarMonitoreo(
            Array.Empty<object>(), cmd, ClaimsMonitoreo(),
            rutinaNombre: "Sistema eléctrico",
            itemsSnapshot: items,
            ahora: Ahora);

        // When: reproyectar el stream completo (previos vacíos + emitidos) sobre un agregado vacío.
        // Garantiza que Apply(InspeccionIniciada_v1) es puro y no tiene validaciones intrusas.
        var stream = Array.Empty<object>().Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: el rebuild no lanza y el estado es coherente con el escenario 6.1.
        var agregado = act.Should().NotThrow().Subject;
        agregado.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        agregado.RutinaId.Should().Be(42);
        agregado.RutinaCodigo.Should().Be("Sistema eléctrico");
        agregado.TecnicoIniciador.Should().Be("ana.gomez");
        agregado.EquipoId.Should().Be(4521);
        agregado.ProyectoId.Should().Be(3);
        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion,
            "tras InspeccionIniciada_v1 el agregado entra en EnEjecucion");
        agregado.ItemsSnapshot.Should().NotBeNull().And.HaveCount(2,
            "el snapshot de 2 items activos debe persistir en el estado del agregado");
        agregado.RutinaMonitoreoSeleccionadaId.Should().Be(42,
            "el alias explícito de RutinaMonitoreoSeleccionadaId debe coincidir con RutinaId");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 complementario — streams Tipo=Tecnica (backward compat)
    // garantiza que Apply tolera null en los campos nuevos del slice 1h
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_InspeccionIniciada_v1_con_campos_monitoreo_null_Tecnica_no_lanza()
    {
        // Given: evento Tipo=Tecnica sin campos de monitoreo (streams previos del 1b).
        var eventoTecnica = new InspeccionIniciada_v1(
            InspeccionId: Guid.Parse("0193a4f7-1234-7abc-8def-000000000010"),
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: 4521,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: "rmartinez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            // Campos slice 1h ausentes (null) — backward compat
            RutinaMonitoreoSeleccionadaId: null,
            ItemsSnapshot: null);

        // When: reproyectar sobre aggregate vacío
        var act = () => Inspeccion.Reconstruir(new object[] { eventoTecnica });

        // Then: no lanza; ItemsSnapshot queda null (tolera nulls backward compat)
        var agregado = act.Should().NotThrow().Subject;
        agregado.Tipo.Should().Be(TipoInspeccion.Tecnica);
        agregado.ItemsSnapshot.Should().BeNull(
            "el aggregate Tecnica no tiene snapshot — Apply debe tolerar null sin excepción");
        agregado.RutinaMonitoreoSeleccionadaId.Should().BeNull();
    }
}
