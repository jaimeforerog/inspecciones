using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests del método de decisión <see cref="Inspeccion.Iniciar"/>.
/// Cobertura de §6.1–§6.9 y §6.12 del spec del slice 1. Los escenarios §6.10
/// (I-I1 shortcut) y §6.11 (I-I1 race condition) son tests del handler con
/// Marten real — viven en <c>Inspecciones.Application.Tests</c>.
/// </summary>
public class IniciarInspeccionTests
{
    // ─────────────────────────────────────────────────────────────────────
    // §6.1 happy path — inicio nuevo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_sobre_stream_vacio_emite_InspeccionIniciada_v1()
    {
        // Given
        var dados = Array.Empty<object>();
        var cmd = ComandoTipo();
        var claims = ClaimsValidos();
        var equipo = EquipoConRutina();
        var rutina = RutinaTecnicaTipo();

        // When
        var resultado = CasoDeUso.Iniciar(dados, cmd, claims, equipo, rutina, Ahora);

        // Then
        resultado.Should().ContainSingle()
            .Which.Should().BeOfType<InspeccionIniciada_v1>();
    }

    [Fact]
    public void IniciarInspeccion_emite_evento_con_payload_completo()
    {
        var cmd = ComandoTipo();
        var claims = ClaimsValidos();
        var equipo = EquipoConRutina();
        var rutina = RutinaTecnicaTipo();

        var resultado = CasoDeUso.Iniciar(Array.Empty<object>(), cmd, claims, equipo, rutina, Ahora);

        var evento = resultado.Should().ContainSingle().Which.Should().BeOfType<InspeccionIniciada_v1>().Subject;
        evento.InspeccionId.Should().Be(cmd.InspeccionId);
        evento.Tipo.Should().Be(TipoInspeccion.Tecnica);
        evento.EquipoId.Should().Be(4521);
        evento.RutinaId.Should().Be(18);
        evento.RutinaCodigo.Should().Be("INSP. BULL.MOTOR", "se denormaliza desde el catálogo para que el evento sea autosuficiente");
        evento.TecnicoIniciador.Should().Be("rmartinez");
        evento.ProyectoId.Should().Be(3);
        evento.Ubicacion.Should().Be(cmd.UbicacionInicio);
        evento.IniciadaEn.Should().Be(Ahora, "el TimeProvider del handler genera el timestamp del sistema");
        evento.FechaReportada.Should().Be(cmd.FechaReportada);
        evento.LecturaMedidorPrimario.Should().BeNull();
        evento.LecturaMedidorSecundario.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 happy path — inicio con lecturas de ambos medidores
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_lecturas_de_ambos_medidores_las_propaga_al_evento()
    {
        var lecturaPrimario = new LecturaMedidor(Tipo: "Hr", Valor: 4523.5m, CapturadoEn: Ahora);
        var lecturaSecundario = new LecturaMedidor(Tipo: "Km", Valor: 187432.0m, CapturadoEn: Ahora);
        var cmd = ComandoTipo(lecturaPrimario: lecturaPrimario, lecturaSecundario: lecturaSecundario);

        var resultado = CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        var evento = resultado.OfType<InspeccionIniciada_v1>().Single();
        evento.LecturaMedidorPrimario.Should().Be(lecturaPrimario);
        evento.LecturaMedidorSecundario.Should().Be(lecturaSecundario);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 happy path — inicio retroactivo (FechaReportada en rango)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_FechaReportada_dos_dias_atras_es_aceptada_I_I3()
    {
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var fechaRetroactiva = hoy.AddDays(-2);
        var cmd = ComandoTipo(fechaReportada: fechaRetroactiva);

        var resultado = CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        resultado.OfType<InspeccionIniciada_v1>().Single()
            .FechaReportada.Should().Be(fechaRetroactiva);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 violación PRE-2 — proyecto no autorizado
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizado_PRE_2()
    {
        var claims = new ClaimsTecnico(
            TecnicoIniciador: "rmartinez",
            ProyectosAsignados: new HashSet<int> { 1, 2 },
            TieneCapabilityEjecutarInspeccion: true);
        var cmd = ComandoTipo(proyectoId: 99);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), cmd, claims, EquipoConRutina(proyectoId: 99), RutinaTecnicaTipo(), Ahora);

        // §C — assertion estricta: el mensaje debe identificar al técnico y al proyecto
        // exactos para que el operativo pueda diagnosticar sin recurrir a logs.
        act.Should().Throw<ProyectoNoAutorizadoException>()
            .WithMessage("*rmartinez*proyecto*99*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 violación PRE-4 — equipo no pertenece al proyecto
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_equipo_de_otro_proyecto_lanza_EquipoNoPerteneceAProyecto_PRE_4()
    {
        var equipoEnOtroProyecto = EquipoConRutina(proyectoId: 1);
        var cmd = ComandoTipo(proyectoId: 3);
        var claims = ClaimsValidos(proyectoId: 3);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), cmd, claims, equipoEnOtroProyecto, RutinaTecnicaTipo(), Ahora);

        act.Should().Throw<EquipoNoPerteneceAProyectoException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 violación PRE-5 — equipo sin rutina técnica (I-I2)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_equipo_sin_rutina_tecnica_lanza_EquipoSinRutinaTecnica_I_I2()
    {
        var equipoSinRutina = EquipoConRutina(rutinaTecnicaId: null);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), ComandoTipo(), ClaimsValidos(), equipoSinRutina, RutinaTecnicaTipo(), Ahora);

        act.Should().Throw<EquipoSinRutinaTecnicaException>()
            .WithMessage("*CARGADOR-EX-201*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 violación PRE-6 — rutina referenciada no sincronizada (I-I2)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_rutina_referenciada_inconsistente_lanza_RutinaTecnicaNoSincronizada_I_I2()
    {
        var equipo = EquipoConRutina(rutinaTecnicaId: 18);
        var rutinaConIdDistinto = RutinaTecnicaTipo(rutinaId: 999);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), ComandoTipo(), ClaimsValidos(), equipo, rutinaConIdDistinto, Ahora);

        act.Should().Throw<RutinaTecnicaNoSincronizadaException>()
            .WithMessage("*sincronizada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 violación PRE-7 — FechaReportada futura (I-I3)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_FechaReportada_futura_lanza_FechaReportadaFueraDeRango_I_I3()
    {
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var futura = hoy.AddDays(5);
        var cmd = ComandoTipo(fechaReportada: futura);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        act.Should().Throw<FechaReportadaFueraDeRangoException>()
            .WithMessage("*rango*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 violación PRE-7 — FechaReportada >30 días retroactiva (I-I3)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_FechaReportada_mas_de_30_dias_atras_lanza_FechaReportadaFueraDeRango_I_I3()
    {
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var muyRetroactiva = hoy.AddDays(-35);
        var cmd = ComandoTipo(fechaReportada: muyRetroactiva);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        act.Should().Throw<FechaReportadaFueraDeRangoException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §A — Boundary tests I-I3 (detectan off-by-one en validación de rango)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_FechaReportada_igual_a_hoy_es_aceptada_I_I3_boundary_superior()
    {
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var cmd = ComandoTipo(fechaReportada: hoy);

        var resultado = CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        resultado.OfType<InspeccionIniciada_v1>().Single().FechaReportada.Should().Be(hoy);
    }

    [Fact]
    public void IniciarInspeccion_con_FechaReportada_exactamente_30_dias_atras_es_aceptada_I_I3_boundary_inferior()
    {
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var limite = hoy.AddDays(-30);
        var cmd = ComandoTipo(fechaReportada: limite);

        var resultado = CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        resultado.OfType<InspeccionIniciada_v1>().Single().FechaReportada.Should().Be(limite);
    }

    [Fact]
    public void IniciarInspeccion_con_FechaReportada_31_dias_atras_lanza_FechaReportadaFueraDeRango_I_I3_off_by_one()
    {
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var unDiaFueraDelRango = hoy.AddDays(-31);
        var cmd = ComandoTipo(fechaReportada: unDiaFueraDelRango);

        var act = () => CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        act.Should().Throw<FechaReportadaFueraDeRangoException>("31 días atrás está fuera del rango aceptado [hoy-30, hoy]");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §G — Verificación explícita: cuando hay excepción, no hay eventos emitidos
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_con_invariante_violada_no_emite_evento_alguno()
    {
        // Caso: I-I2 violada (equipo sin rutina). Si la excepción se levanta antes de
        // construir el evento, el aggregate nunca debería haber accedido a la lista de
        // eventos emitidos. Reaseguramos que no hay efecto observable.
        var equipoSinRutina = EquipoConRutina(rutinaTecnicaId: null);
        IReadOnlyList<object>? capturado = null;

        var act = () =>
        {
            capturado = CasoDeUso.Iniciar(Array.Empty<object>(), ComandoTipo(), ClaimsValidos(), equipoSinRutina, RutinaTecnicaTipo(), Ahora);
        };

        act.Should().Throw<EquipoSinRutinaTecnicaException>();
        capturado.Should().BeNull("la excepción se lanza antes de retornar la lista de eventos — no debe haber side effect observable");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 rebuild desde stream (obligatorio)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IniciarInspeccion_rebuild_desde_stream_reproduce_estado()
    {
        // Given: ejecutar el comando happy path
        var cmd = ComandoTipo();
        var emitidos = CasoDeUso.Iniciar(Array.Empty<object>(), cmd, ClaimsValidos(), EquipoConRutina(), RutinaTecnicaTipo(), Ahora);

        // When: reproyectar todos los eventos sobre un agregado vacío
        var act = () => Inspeccion.Reconstruir(emitidos);

        // Then: el rebuild no lanza y el estado refleja el evento
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.InspeccionId.Should().Be(cmd.InspeccionId);
        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion, "tras InspeccionIniciada_v1 el agregado entra en EnEjecucion");
        aggregate.EquipoId.Should().Be(cmd.EquipoId);
        aggregate.RutinaId.Should().Be(18);
        aggregate.RutinaCodigo.Should().Be("INSP. BULL.MOTOR");
        aggregate.ProyectoId.Should().Be(cmd.ProyectoId);
        aggregate.FechaReportada.Should().Be(cmd.FechaReportada);
        aggregate.IniciadaEn.Should().Be(Ahora);
    }
}
