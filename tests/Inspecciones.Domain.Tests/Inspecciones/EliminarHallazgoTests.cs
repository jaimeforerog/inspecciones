using FluentAssertions;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests en rojo para el slice 1e — EliminarHallazgo.
/// Un test por escenario de la spec §6. Los tests §6.1..§6.6 y §6.9 fallan con
/// <see cref="NotImplementedException"/> hasta que green implemente la lógica.
/// El test §6.7 (PRE-D/I-H9) está marcado Skip — requiere slices de repuestos/adjuntos.
/// El escenario §6.8 (PRE-F: stream no existe en Marten) es de integración y se omite
/// — ver red-notes §6.8.
/// El escenario §6.10 (DoD: levantar skip de ActualizarHallazgoTests §6.7) se verifica
/// ejecutando la suite completa — documentado en red-notes §6.10.
/// </summary>
public sealed class EliminarHallazgoTests
{
    // ── §6.1 — Happy path: hallazgo sin hijos activos, inspección en ejecución ──

    [Fact]
    public void EliminarHallazgo_en_inspeccion_en_ejecucion_emite_HallazgoEliminado_v1()
    {
        // Given: inspección en ejecución con hallazgo G1 (Manual, NoRequiereIntervencion)
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.NoRequiereIntervencion);

        // When: eliminar el hallazgo con motivo válido
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG1,
            motivo: "Registrado por error — parte incorrecta",
            tecnicoId: "rmartinez");

        var resultado = CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: exactamente un HallazgoEliminado_v1 con los campos correctos
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoEliminado_v1>().Subject;

        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.HallazgoId.Should().Be(HallazgoG1);
        evt.Motivo.Should().Be("Registrado por error — parte incorrecta");
        evt.EliminadoPor.Should().Be("rmartinez");
        evt.EliminadoEn.Should().Be(Ahora);
    }

    // ── §6.2 — Happy path: hallazgo con RequiereIntervencion — soft delete igual ──

    [Fact]
    public void EliminarHallazgo_con_RequiereIntervencion_emite_HallazgoEliminado_v1_sin_restriccion()
    {
        // Given: inspección con hallazgo G2 (RequiereIntervencion, TipoFallaId=3, CausaFallaId=12)
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG2,
            accionRequerida: AccionRequerida.RequiereIntervencion,
            tipoFallaId: 3,
            causaFallaId: 12);

        // When: eliminar — la AccionRequerida no bloquea el soft delete
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG2,
            motivo: "Hallazgo duplicado — el técnico lo registró dos veces",
            tecnicoId: "rmartinez");

        var resultado = CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: el evento se emite y HallazgoId es correcto
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoEliminado_v1>().Subject;

        evt.HallazgoId.Should().Be(HallazgoG2);
        evt.Motivo.Should().Be("Hallazgo duplicado — el técnico lo registró dos veces");
        evt.EliminadoPor.Should().Be("rmartinez");
    }

    // ── §6.3 — PRE-A / I-H7 / I-F1: inspección Firmada bloquea eliminación ──

    [Fact]
    public void EliminarHallazgo_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7()
    {
        // Given: inspección en estado Firmada (orden causal: iniciada → hallazgo → firmada)
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(hallazgoId: HallazgoG1),
            new InspeccionFirmada_v1(InspeccionIdNueva, Ahora, "rmartinez"),
        };

        // When: intentar eliminar hallazgo con inspección firmada
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG1,
            motivo: "Ya no aplica");

        var act = () => CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: PRE-A — estado no es EnEjecucion (I-H7 / I-F1)
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ── §6.4 — PRE-B1: HallazgoId inexistente ───────────────────────────────

    [Fact]
    public void EliminarHallazgo_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE_B1()
    {
        // Given: inspección en ejecución sin hallazgos
        var dados = new object[] { EventoInspeccionIniciada() };

        // When: intentar eliminar un HallazgoId que no existe en el stream
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG3,   // G3 no fue registrado
            motivo: "Ya no aplica");

        var act = () => CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: PRE-B1
        act.Should().Throw<HallazgoNoEncontradoException>();
    }

    // ── §6.5 — PRE-B2: HallazgoId ya eliminado — idempotencia negativa ──────

    [Fact]
    public void EliminarHallazgo_con_HallazgoId_ya_eliminado_lanza_HallazgoEliminadoException_PRE_B2()
    {
        // Given: stream con inspección iniciada, hallazgo G1 registrado y luego eliminado
        var dados = (object[])
            [EventoInspeccionIniciada(),
             HallazgoRegistradoEjemplo(hallazgoId: HallazgoG1),
             HallazgoEliminadoEjemplo(hallazgoId: HallazgoG1)];

        // When: segundo intento de eliminar el mismo hallazgo
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG1,
            motivo: "Intento duplicado");

        var act = () => CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: PRE-B2 — el hallazgo ya tiene Eliminado=true
        act.Should().Throw<HallazgoEliminadoException>()
            .WithMessage($"*{HallazgoG1}*");
    }

    // ── §6.6 — PRE-C: Motivo vacío o solo whitespace ─────────────────────────

    [Fact]
    public void EliminarHallazgo_con_Motivo_vacio_lanza_MotivoEliminacionVacioException_PRE_C()
    {
        // Given: inspección en ejecución con hallazgo G1 activo
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        // When: intentar eliminar con motivo vacío
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG1,
            motivo: "   ");   // solo whitespace

        var act = () => CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: PRE-C — motivo obligatorio
        act.Should().Throw<MotivoEliminacionVacioException>()
            .WithMessage("*obligatorio*");
    }

    // ── §6.7 — PRE-D / I-H9: hallazgo con hijos activos [Skip] ──────────────

    [Fact(Skip = "I-H9: requiere slices de repuestos/adjuntos")]
    public void EliminarHallazgo_con_hijos_activos_lanza_HallazgoTieneHijosActivosException_I_H9()
    {
        // Given: inspección en ejecución con hallazgo G1 que tiene ≥1 repuesto activo.
        // No existen eventos de repuestos/adjuntos aún — no hay forma de construir
        // el stream con hijos activos sin violar la regla de cero mocks de dominio.
        // El código de PRE-D SÍ se implementa en el método de decisión (colecciones
        // vacías en MVP — invariante activa automáticamente cuando lleguen esos slices).
        // Skip levantado con el DoD del primer slice de AsignarRepuesto o AdjuntarArchivo.
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG1,
            motivo: "Ya no aplica");

        var act = () => CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: PRE-D / I-H9
        act.Should().Throw<HallazgoTieneHijosActivosException>()
            .WithMessage($"*{HallazgoG1}*");
    }

    // ── §6.8 — PRE-F: InspeccionId no existe (integración, omitido aquí) ────
    // Este escenario requiere Marten/Postgres — el handler carga el aggregate con
    // IDocumentSession.Events.AggregateStreamAsync y verifica si retorna null.
    // Se implementa en tests de integración del slice 1e.
    // Ver red-notes §6.8.

    // ── §6.9 — Rebuild desde stream: Apply puro y orden causal ──────────────

    [Fact]
    public void EliminarHallazgo_rebuild_desde_stream_reproduce_estado_Eliminado()
    {
        // Given: stream previo con hallazgo G1 activo
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.RequiereIntervencion,
            tipoFallaId: 3,
            causaFallaId: 12);

        // When: emitir el evento de eliminación
        var cmd = ComandoEliminarHallazgo(
            hallazgoId: HallazgoG1,
            motivo: "Registrado por error",
            tecnicoId: "rmartinez");

        var emitidos = CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // When: reproyectar el stream completo (previos + emitidos) sobre un agregado vacío
        var streamCompleto = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(streamCompleto);

        // Then: rebuild no lanza (Apply es puro — sin validaciones)
        var agregado = act.Should().NotThrow().Subject;

        // Estado general de la inspección intacto
        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion);

        // El hallazgo sigue en la lista (soft delete, no borrado)
        var hallazgo = agregado.Hallazgos
            .Should().ContainSingle(h => h.HallazgoId == HallazgoG1).Subject;

        // Eliminado=true y MotivoEliminacion persistido
        hallazgo.Eliminado.Should().BeTrue();
        hallazgo.MotivoEliminacion.Should().Be("Registrado por error");

        // Campos inmutables/originales inalterados
        hallazgo.Origen.Should().Be(OrigenHallazgo.Manual);
        hallazgo.AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        hallazgo.TipoFallaId.Should().Be(3);
        hallazgo.CausaFallaId.Should().Be(12);

        // rmartinez como contribuyente
        agregado.Contribuyentes.Should().Contain("rmartinez");
    }
}