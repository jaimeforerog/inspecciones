using FluentAssertions;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.ActualizarRepuestoFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests en rojo para el slice 1o — ActualizarRepuesto.
/// Un test por escenario de la spec §6. Todos fallan con
/// <see cref="NotImplementedException"/> hasta que green implemente
/// <see cref="Inspeccion.ActualizarRepuesto"/>.
/// Excepción: §6.14 (rebuild) pasa porque <see cref="Inspeccion.Apply(RepuestoActualizado_v1)"/>
/// ya es puro y opera sobre el estado creado por <see cref="RepuestoEstimado_v1"/>.
/// </summary>
public sealed class ActualizarRepuestoTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // §6.1 — Happy path: actualizar solo la cantidad
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_solo_cantidad_emite_RepuestoActualizado_v1_con_delta_cantidad()
    {
        // Given: stream con inspección EnEjecucion + hallazgo G1 + repuesto R1 (Cantidad=1, Justificacion="Cambio rutinario")
        var dados = StreamBaseConRepuesto();

        // When: actualizar solo la cantidad a 2
        var cmd = ComandoActualizarSoloCantidad(cantidadNueva: 2m, actualizadoPor: "rmartinez");
        var resultado = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: exactamente un evento con delta de cantidad (Justificacion=null = no cambió)
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoActualizado_v1>().Subject;

        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.HallazgoId.Should().Be(HallazgoG1);
        evt.RepuestoId.Should().Be(RepuestoR1);
        evt.Cantidad.Should().Be(2m);
        evt.Justificacion.Should().BeNull("Justificacion=null en el delta significa que no cambió");
        evt.ActualizadoPor.Should().Be("rmartinez");
        evt.ActualizadoEn.Should().Be(AhoraActualizar);
    }

    [Fact]
    public void ActualizarRepuesto_solo_cantidad_preserva_justificacion_anterior_en_aggregate()
    {
        // Given: repuesto con Justificacion="Cambio rutinario"
        var dados = StreamBaseConRepuesto();
        var cmd = ComandoActualizarSoloCantidad(cantidadNueva: 2m);

        // When: emitir + reproyectar
        var emitidos = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: Apply aplica el delta — Cantidad actualizada, Justificacion preservada
        var repuesto = aggregate.Repuestos.Single(r => r.RepuestoId == RepuestoR1);
        repuesto.Cantidad.Should().Be(2m, "delta Cantidad=2 reemplaza el valor anterior 1");
        repuesto.Justificacion.Should().Be("Cambio rutinario", "Justificacion=null en evento = no cambió");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.2 — Happy path: actualizar solo la observación/justificación
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_solo_observacion_emite_RepuestoActualizado_v1_con_delta_justificacion()
    {
        // Given: stream base (repuesto R1, Cantidad=1)
        var dados = StreamBaseConRepuesto();

        // When: actualizar solo la observación
        var cmd = ComandoActualizarSoloObservacion(observacion: "Filtro doble en este modelo de motor");
        var resultado = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: evento con Cantidad=null (no cambió), Justificacion con el nuevo valor
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoActualizado_v1>().Subject;

        evt.Cantidad.Should().BeNull("Cantidad=null en delta significa que no cambió");
        evt.Justificacion.Should().Be("Filtro doble en este modelo de motor");
    }

    [Fact]
    public void ActualizarRepuesto_solo_observacion_preserva_cantidad_anterior_en_aggregate()
    {
        // Given: repuesto con Cantidad=1
        var dados = StreamBaseConRepuesto();
        var cmd = ComandoActualizarSoloObservacion(observacion: "Filtro doble en este modelo de motor");

        // When: emitir + reproyectar
        var emitidos = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: Cantidad preservada, Justificacion actualizada
        var repuesto = aggregate.Repuestos.Single(r => r.RepuestoId == RepuestoR1);
        repuesto.Cantidad.Should().Be(1m, "Cantidad=null en evento = no cambió, preserva valor original");
        repuesto.Justificacion.Should().Be("Filtro doble en este modelo de motor");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.3 — Happy path: actualizar ambos campos en una sola operación
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_ambos_campos_emite_RepuestoActualizado_v1_con_delta_completo()
    {
        // Given: stream base
        var dados = StreamBaseConRepuesto();

        // When: actualizar cantidad y observación simultáneamente
        var cmd = ComandoActualizarAmbos(
            cantidadNueva: 3m,
            observacion: "Revisión extendida, se necesitan 3",
            actualizadoPor: "jperez");
        var resultado = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: evento con ambos campos no-null
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoActualizado_v1>().Subject;

        evt.Cantidad.Should().Be(3m);
        evt.Justificacion.Should().Be("Revisión extendida, se necesitan 3");
        evt.ActualizadoPor.Should().Be("jperez",
            "el técnico que actualiza puede ser distinto al que asignó el repuesto");
    }

    [Fact]
    public void ActualizarRepuesto_ambos_campos_agrega_tecnico_a_contribuyentes()
    {
        // Given: stream base (rmartinez es contribuyente inicial)
        var dados = StreamBaseConRepuesto();
        var cmd = ComandoActualizarAmbos(actualizadoPor: "jperez");

        // When: emitir + reproyectar
        var emitidos = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: jperez queda registrado como contribuyente
        aggregate.Contribuyentes.Should().Contain("jperez",
            "Apply(RepuestoActualizado_v1) agrega ActualizadoPor a _contribuyentes");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.4 — Happy path: segunda actualización sobre el mismo repuesto (trazabilidad)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_segunda_actualizacion_emite_segundo_evento_trazabilidad()
    {
        // Given: stream con primera actualización ya aplicada (Cantidad=2)
        var dados = StreamConPrimeraActualizacion();

        // When: segunda actualización — solo observación
        var cmd = ComandoActualizarSoloObservacion(
            observacion: "Filtro doble en este modelo",
            actualizadoPor: "rmartinez");
        var resultado = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: se emite el segundo evento (el stream conserva ambos para trazabilidad)
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoActualizado_v1>().Subject;

        evt.Cantidad.Should().BeNull("segunda actualización solo toca Justificacion");
        evt.Justificacion.Should().Be("Filtro doble en este modelo");
    }

    [Fact]
    public void ActualizarRepuesto_segunda_actualizacion_combina_ambos_deltas_en_aggregate()
    {
        // Given: stream con primera actualización (Cantidad=2)
        var dados = StreamConPrimeraActualizacion();
        var cmd = ComandoActualizarSoloObservacion(observacion: "Filtro doble en este modelo");

        // When: segunda actualización + reproyectar
        var emitidos = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);

        // Then: Cantidad=2 (de primera actualización), Justificacion actualizada por segunda
        var repuesto = aggregate.Repuestos.Single(r => r.RepuestoId == RepuestoR1);
        repuesto.Cantidad.Should().Be(2m, "preservado de la primera actualización");
        repuesto.Justificacion.Should().Be("Filtro doble en este modelo");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.5 — Violación PRE-2 (I-H7): inspección no está en EnEjecucion
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7()
    {
        // Given: inspección en estado Firmada
        var dados = StreamConInspeccionFirmada();

        // When: intentar actualizar repuesto en inspeccion firmada
        var cmd = ComandoActualizarSoloCantidad(cantidadNueva: 2m);
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-2 — I-H7 editable solo en EnEjecucion
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    [Fact]
    public void ActualizarRepuesto_en_inspeccion_Cancelada_lanza_InspeccionNoEnEjecucionException_I_H7()
    {
        // Given: inspección en estado Cancelada
        var dados = StreamConInspeccionCancelada();

        // When
        var cmd = ComandoActualizarSoloCantidad(cantidadNueva: 2m);
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-2 — estado Cancelada también bloquea I-H7
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Cancelada*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.6 — Violación PRE-3: HallazgoId no existe en el aggregate
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE3()
    {
        // Given: inspección EnEjecucion sin hallazgos
        var dados = (object[])[ EventoInspeccionIniciada() ];

        // When: hallazgoId que no existe en el stream
        var cmd = new ActualizarRepuesto(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG3,
            RepuestoId: RepuestoR1,
            CantidadNueva: 2m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-3 — hallazgo no existe
        act.Should().Throw<HallazgoNoEncontradoException>()
            .WithMessage($"*{HallazgoG3}*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.7 — Violación PRE-4: hallazgo existe pero está eliminado
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_con_hallazgo_eliminado_lanza_HallazgoEliminadoException_PRE4()
    {
        // Given: hallazgo G2 eliminado con repuesto R2
        var dados = StreamConHallazgoEliminadoYRepuesto();

        // When: intentar actualizar repuesto de hallazgo eliminado
        var cmd = new ActualizarRepuesto(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG2,
            RepuestoId: RepuestoR2,
            CantidadNueva: 2m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-4 — hallazgo eliminado
        act.Should().Throw<HallazgoEliminadoException>()
            .WithMessage($"*{HallazgoG2}*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.8 — Violación PRE-5: RepuestoId no existe en el aggregate
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_con_RepuestoId_inexistente_lanza_RepuestoNoEncontradoException_PRE5()
    {
        // Given: inspección EnEjecucion con hallazgo G1 activo pero _repuestos vacío
        var dados = (object[])
        [
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                parteEquipoId: 77,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
        ];

        // When: RepuestoId que no existe en _repuestos
        var repuestoInexistente = new Guid("0199ffff-0000-7000-0000-000000099999");
        var cmd = new ActualizarRepuesto(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            RepuestoId: repuestoInexistente,
            CantidadNueva: 2m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-5 — repuesto no encontrado
        act.Should().Throw<RepuestoNoEncontradoException>()
            .WithMessage($"*{repuestoInexistente}*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.9 — Violación PRE-5: RepuestoId existe pero pertenece a hallazgo distinto
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_con_RepuestoId_en_hallazgo_incorrecto_lanza_RepuestoNoEncontradoException_PRE5()
    {
        // Given: R1 pertenece a G1; el cliente intenta actualizar R1 usando el path de G2
        var dados = StreamConDosHallazgosYRepuestoEnG1();

        // When: HallazgoId=G2 pero RepuestoId=R1 (que pertenece a G1)
        var cmd = new ActualizarRepuesto(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG2,
            RepuestoId: RepuestoR1,
            CantidadNueva: 2m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-5 — R1 no pertenece al hallazgo G2 (D-2: 404 es más seguro que 422)
        act.Should().Throw<RepuestoNoEncontradoException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.10 — Violación PRE-7: CantidadNueva igual o menor a cero
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_con_CantidadNueva_cero_lanza_CantidadInvalidaException_PRE7()
    {
        // Given: stream base con repuesto activo
        var dados = StreamBaseConRepuesto();

        // When: Cantidad=0 (inválida)
        var cmd = new ActualizarRepuesto(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            RepuestoId: RepuestoR1,
            CantidadNueva: 0m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-7 — cantidad debe ser mayor que cero
        act.Should().Throw<CantidadInvalidaException>()
            .WithMessage("*cero*");
    }

    [Fact]
    public void ActualizarRepuesto_con_CantidadNueva_negativa_lanza_CantidadInvalidaException_PRE7()
    {
        // Given: stream base con repuesto activo
        var dados = StreamBaseConRepuesto();

        // When: Cantidad=-1 (negativa)
        var cmd = new ActualizarRepuesto(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            RepuestoId: RepuestoR1,
            CantidadNueva: -1m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-7 — cantidad negativa también es inválida
        act.Should().Throw<CantidadInvalidaException>()
            .WithMessage("*cero*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.11 — Violación PRE-8: comando sin campos patcheables (ambos null)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_sin_campos_patcheables_lanza_ComandoSinCambiosException_PRE8()
    {
        // Given: stream base con repuesto activo
        var dados = StreamBaseConRepuesto();

        // When: ambos campos null — comando vacío
        var cmd = ComandoSinCambios();
        var act = () => CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: PRE-8 — se requiere al menos un campo para actualizar
        act.Should().Throw<ComandoSinCambiosException>()
            .WithMessage("*CantidadNueva*");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.12 — Violación PRE-1: InspeccionId no existe (handler — Skip en dominio puro)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-1 vive en el handler (IDocumentSession.Events.AggregateStreamAsync). " +
                 "Requiere Marten/Testcontainers. Cubierto por ActualizarRepuestoHandlerTests.")]
    public void ActualizarRepuesto_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE1()
    {
        // Escenario §6.12 — responsabilidad de la capa Application.Tests.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.13 — Inmutabilidad INV-RA1: campos inmutables no cambian tras actualización
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_no_modifica_campos_inmutables_SkuId_Unidad_HallazgoId_INV_RA1()
    {
        // Given: repuesto R1 con SkuId=501, Unidad="unidad", HallazgoId=G1
        var dados = StreamBaseConRepuesto();

        // When: actualizar Cantidad (no toca campos inmutables)
        var cmd = ComandoActualizarSoloCantidad(cantidadNueva: 3m);
        var emitidos = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);

        // Then: el evento no contiene SkuId, Unidad ni HallazgoId
        var evt = emitidos.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoActualizado_v1>().Subject;

        // El evento solo tiene delta — no hay SkuId, Unidad ni HallazgoId en el tipo
        evt.RepuestoId.Should().Be(RepuestoR1);
        evt.HallazgoId.Should().Be(HallazgoG1, "HallazgoId del evento es la referencia de pertenencia, no campo patcheable");

        // Reproyectar y verificar que los campos del VO Repuesto siguen intactos
        var stream = dados.Concat(emitidos).ToArray();
        var aggregate = Inspeccion.Reconstruir(stream);
        var repuesto = aggregate.Repuestos.Single(r => r.RepuestoId == RepuestoR1);

        repuesto.SkuId.Should().Be(501, "SkuId es inmutable — INV-RA1");
        repuesto.Unidad.Should().Be("unidad", "Unidad es inmutable — INV-RA1");
        repuesto.HallazgoId.Should().Be(HallazgoG1, "HallazgoId es inmutable — INV-RA1");
        repuesto.Cantidad.Should().Be(3m, "Cantidad sí se actualizó");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // §6.14 — Rebuild desde stream: Apply puro y orden causal (OBLIGATORIO)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ActualizarRepuesto_rebuild_desde_stream_reproduce_estado()
    {
        // Given: los 4 eventos en orden causal del spec §6.14
        var stream = new object[]
        {
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4521,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                parteEquipoId: 77,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            new RepuestoEstimado_v1(
                InspeccionId: InspeccionIdNueva,
                HallazgoId: HallazgoG1,
                RepuestoId: RepuestoR1,
                SkuId: 501,
                SkuCodigo: "INS-501",
                Cantidad: 1m,
                Justificacion: "Cambio rutinario",
                Unidad: "unidad",
                AsignadoPor: "rmartinez",
                AsignadoEn: T0),
            new RepuestoActualizado_v1(
                InspeccionId: InspeccionIdNueva,
                HallazgoId: HallazgoG1,
                RepuestoId: RepuestoR1,
                Cantidad: 2m,
                Justificacion: null,
                ActualizadoPor: "rmartinez",
                ActualizadoEn: T1),
        };

        // When: reproyectar los 4 eventos en orden sobre un aggregate vacío
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: rebuild no lanza, estado coherente (§6.14)
        var aggregate = act.Should().NotThrow().Subject;

        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        aggregate.Hallazgos.Should().HaveCount(1);
        aggregate.Hallazgos[0].Eliminado.Should().BeFalse();
        aggregate.Repuestos.Should().HaveCount(1);

        var repuesto = aggregate.Repuestos[0];
        repuesto.Cantidad.Should().Be(2m, "evento #4 delta Cantidad=2 se aplicó sobre Cantidad=1 del evento #3");
        repuesto.Justificacion.Should().Be("Cambio rutinario",
            "Justificacion=null en evento #4 = no cambió; valor preservado del evento #3");
        repuesto.SkuId.Should().Be(501, "SkuId inmutable");
        repuesto.HallazgoId.Should().Be(HallazgoG1, "HallazgoId inmutable");

        aggregate.Contribuyentes.Should().Contain("rmartinez");
    }

    [Fact]
    public void ActualizarRepuesto_rebuild_desde_stream_completo_previos_mas_emitidos()
    {
        // Given: stream previo + emitir happy path
        var dados = (IReadOnlyList<object>)StreamBaseConRepuesto();
        var cmd = ComandoActualizarSoloCantidad(cantidadNueva: 2m);

        // When: emitir + reproyectar el stream completo (previos + emitidos)
        var emitidos = CasoDeUso.ActualizarRepuesto(dados, cmd, AhoraActualizar);
        var stream = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(stream);

        // Then: rebuild no lanza y el estado es coherente con el happy path §6.1
        var aggregate = act.Should().NotThrow().Subject;
        aggregate.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        var repuesto = aggregate.Repuestos.Single(r => r.RepuestoId == RepuestoR1);
        repuesto.Cantidad.Should().Be(2m);
        repuesto.Justificacion.Should().Be("Cambio rutinario", "preservada desde RepuestoEstimado");
    }
}
