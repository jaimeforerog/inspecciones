using FluentAssertions;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests en rojo para el slice 1d — ActualizarHallazgo.
/// Un test por escenario de la spec §6. Todos fallan con
/// <see cref="NotImplementedException"/> hasta que green implemente la lógica.
/// El escenario §6.14 (PRE-F: stream no existe en Marten) es de integración
/// y se omite aquí — ver red-notes §6.14.
/// </summary>
public sealed class ActualizarHallazgoTests
{
    // ── §6.1 — Happy path: upgrade a RequiereIntervencion ─────────────────

    [Fact]
    public void ActualizarHallazgo_upgrade_a_RequiereIntervencion_emite_HallazgoActualizado_v1()
    {
        // Given: inspección en ejecución con hallazgo NoRequiereIntervencion
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.NoRequiereIntervencion);

        // When: actualizar a RequiereIntervencion con todos los campos obligatorios
        var cmd = ComandoActualizarConIntervencion(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Fuga confirmada en sello hidráulico — requiere intervención",
            accionCorrectiva: "Reemplazar sello hidráulico y rellenar aceite",
            tipoFallaId: 3,
            causaFallaId: 12);

        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: emite exactamente un evento con los campos correctos
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.HallazgoId.Should().Be(HallazgoG1);
        evt.NovedadTecnica.Should().Be("Fuga confirmada en sello hidráulico — requiere intervención");
        evt.AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        evt.AccionCorrectiva.Should().Be("Reemplazar sello hidráulico y rellenar aceite");
        evt.TipoFallaId.Should().Be(3);
        evt.CausaFallaId.Should().Be(12);
        evt.ActualizadoEn.Should().Be(Ahora);
        evt.EmitidoPor.Should().Be("ana.gomez");
    }

    // ── §6.2 — Happy path: downgrade a RequiereSeguimiento ────────────────

    [Fact]
    public void ActualizarHallazgo_downgrade_a_RequiereSeguimiento_limpia_campos_intervencion()
    {
        // Given: inspección con hallazgo en RequiereIntervencion
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.RequiereIntervencion,
            tipoFallaId: 3,
            causaFallaId: 12);

        // When: actualizar a RequiereSeguimiento — sin campos de intervención
        var cmd = ComandoActualizarConSeguimiento(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Desgaste progresivo, requiere monitoreo continuo");

        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: evento emitido tiene campos de intervención nulos
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        evt.AccionRequerida.Should().Be(AccionRequerida.RequiereSeguimiento);
        evt.AccionCorrectiva.Should().BeNull();
        evt.TipoFallaId.Should().BeNull();
        evt.CausaFallaId.Should().BeNull();
    }

    // ── §6.3 — Happy path: recaptura GPS sin cambiar AccionRequerida ───────

    [Fact]
    public void ActualizarHallazgo_recaptura_GPS_sin_cambiar_accion_requerida_emite_ubicacion_actualizada()
    {
        // Given: inspección con hallazgo NoRequiereIntervencion sin GPS
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.NoRequiereIntervencion);

        var nuevaUbicacion = new UbicacionGps(4.800m, -74.100m, 5.0m, Ahora);

        // When: actualizar solo capturando GPS nuevo
        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "Manguera con desgaste leve superficial",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: nuevaUbicacion,
            EmitidoPor: "ana.gomez");

        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: evento tiene la nueva ubicación
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        evt.UbicacionGps.Should().Be(nuevaUbicacion);
        evt.AccionRequerida.Should().Be(AccionRequerida.NoRequiereIntervencion);
    }

    // ── §6.4 — Happy path: solo texto, mantiene RequiereIntervencion ───────

    [Fact]
    public void ActualizarHallazgo_solo_texto_mantiene_RequiereIntervencion_emite_evento_con_datos_correctos()
    {
        // Given: inspección con hallazgo RequiereIntervencion
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.RequiereIntervencion,
            tipoFallaId: 3,
            causaFallaId: 12);

        // When: actualizar solo el texto de novedad, manteniendo RequiereIntervencion
        var cmd = ComandoActualizarConIntervencion(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Fuga severa confirmada — descripción ampliada tras inspección detallada",
            accionCorrectiva: "Reemplazar sello hidráulico y rellenar aceite",
            tipoFallaId: 3,
            causaFallaId: 12);

        var resultado = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: evento refleja el nuevo texto sin alterar tipo/causa
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        evt.NovedadTecnica.Should().Be("Fuga severa confirmada — descripción ampliada tras inspección detallada");
        evt.AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        evt.TipoFallaId.Should().Be(3);
        evt.CausaFallaId.Should().Be(12);
    }

    // ── §6.5 — PRE-A / I-H7: Firmada bloquea la actualización ─────────────

    [Fact]
    public void ActualizarHallazgo_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException()
    {
        // Given: inspección firmada
        var dados = StreamConInspeccionFirmada();

        var cmd = ComandoActualizarSoloTexto(hallazgoId: HallazgoG1);

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: lanza porque el estado no es EnEjecucion (I-H7 / PRE-A)
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ── §6.6 — PRE-B1: HallazgoId no existe ───────────────────────────────

    [Fact]
    public void ActualizarHallazgo_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException()
    {
        // Given: inspección con hallazgo G1 — se intenta actualizar G3 (no existe)
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = ComandoActualizarSoloTexto(hallazgoId: HallazgoG3);

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-B1
        act.Should().Throw<HallazgoNoEncontradoException>();
    }

    // ── §6.7 — PRE-B2: HallazgoId existe pero está eliminado ──────────────

    [Fact]
    public void ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException()
    {
        // Given: stream con inspección iniciada y hallazgo G5 marcado Eliminado=true
        // (via HallazgoEliminado_v1 implementado en slice 1e — followup #21).
        var dados = StreamConHallazgoEliminado();

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG5,
            NovedadTecnica: "intento de actualizar hallazgo eliminado",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-B2 — el hallazgo G5 está Eliminado=true, lanza HallazgoEliminadoException
        act.Should().Throw<HallazgoEliminadoException>();
    }

    // ── §6.8 — PRE-C: NovedadTecnica vacía ────────────────────────────────

    [Fact]
    public void ActualizarHallazgo_con_NovedadTecnica_vacia_lanza_NovedadTecnicaVaciaException()
    {
        // Given: inspección con hallazgo válido
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "   ",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-C
        act.Should().Throw<NovedadTecnicaVaciaException>();
    }

    // ── §6.9 — PRE-D1: RequiereIntervencion sin TipoFallaId ───────────────

    [Fact]
    public void ActualizarHallazgo_RequiereIntervencion_sin_TipoFallaId_lanza_TipoYCausaFallaRequeridosException()
    {
        // Given: inspección con hallazgo válido
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "Fuga en sello",
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: "Reemplazar sello",
            TipoFallaId: null,         // falta TipoFallaId
            CausaFallaId: 12,
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-D1
        act.Should().Throw<TipoYCausaFallaRequeridosException>();
    }

    // ── §6.10 — PRE-D1: RequiereIntervencion sin CausaFallaId ─────────────

    [Fact]
    public void ActualizarHallazgo_RequiereIntervencion_sin_CausaFallaId_lanza_TipoYCausaFallaRequeridosException()
    {
        // Given: inspección con hallazgo válido
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "Fuga en sello",
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: "Reemplazar sello",
            TipoFallaId: 3,
            CausaFallaId: null,        // falta CausaFallaId
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-D1
        act.Should().Throw<TipoYCausaFallaRequeridosException>();
    }

    // ── §6.11 — PRE-D2: RequiereIntervencion sin AccionCorrectiva ─────────

    [Fact]
    public void ActualizarHallazgo_RequiereIntervencion_sin_AccionCorrectiva_lanza_AccionCorrectivaRequeridaException()
    {
        // Given: inspección con hallazgo válido
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "Fuga en sello",
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: null,    // falta AccionCorrectiva
            TipoFallaId: 3,
            CausaFallaId: 12,
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-D2
        act.Should().Throw<AccionCorrectivaRequeridaException>();
    }

    // ── §6.12 — PRE-E: NoRequiereIntervencion con TipoFallaId poblado ─────

    [Fact]
    public void ActualizarHallazgo_NoRequiereIntervencion_con_TipoFallaId_lanza_CamposIntervencionNoPermitidosException()
    {
        // Given: inspección con hallazgo válido
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "Manguera revisada",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: 3,            // campo de intervención no permitido
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-E
        act.Should().Throw<CamposIntervencionNoPermitidosException>();
    }

    // ── §6.13 — PRE-E: RequiereSeguimiento con AccionCorrectiva poblada ───

    [Fact]
    public void ActualizarHallazgo_RequiereSeguimiento_con_AccionCorrectiva_lanza_CamposIntervencionNoPermitidosException()
    {
        // Given: inspección con hallazgo válido
        var dados = StreamConHallazgoRegistrado(hallazgoId: HallazgoG1);

        var cmd = new ActualizarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            NovedadTecnica: "Desgaste progresivo",
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: "acción que no debería estar aquí",  // campo no permitido
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: null,
            EmitidoPor: "ana.gomez");

        var act = () => CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: PRE-E
        act.Should().Throw<CamposIntervencionNoPermitidosException>();
    }

    // ── §6.14 — PRE-F: InspeccionId no existe (integración, omitido aquí) ─
    // Este escenario requiere Marten/Postgres. Se implementa en tests de integración.
    // Ver red-notes §6.14.

    // ── §6.15 — I-H8: campos inmutables no cambian tras actualización ──────

    [Fact]
    public void ActualizarHallazgo_campos_inmutables_no_cambian_tras_actualizacion()
    {
        // Given: inspección con hallazgo G1 (Origen=PreOperacional, ParteEquipoId=88)
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            new HallazgoRegistrado_v1(
                InspeccionId: InspeccionIdNueva,
                HallazgoId: HallazgoG1,
                Origen: OrigenHallazgo.PreOperacional,
                NovedadPreopOrigenId: 1042,
                MedicionOrigenId: null,      // Slice 1i: null para PreOperacional (backward compat)
                EvaluacionOrigenId: null,    // Slice 1i': null para PreOperacional (backward compat)
                ParteEquipoId: 88,
                ActividadId: null,
                ActividadDescripcion: null,
                NovedadTecnica: "novedad original",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "ana.gomez",
                RegistradoEn: Ahora)
        };

        // When: actualizar el hallazgo (happy path)
        var cmd = ComandoActualizarSoloTexto(
            hallazgoId: HallazgoG1,
            novedadTecnica: "novedad actualizada");

        var emitidos = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // Then: el evento emitido no contiene Origen, ParteEquipoId ni NovedadPreopOrigenId
        var evt = emitidos.Should().ContainSingle()
            .Which.Should().BeOfType<HallazgoActualizado_v1>().Subject;

        // I-H8: el evento no tiene esos campos — verificamos que el tipo del record
        // no expone propiedades de campos inmutables
        var tipoEvt = evt.GetType();
        tipoEvt.GetProperty("Origen").Should().BeNull(
            because: "Origen es inmutable y no debe incluirse en HallazgoActualizado_v1 (I-H8)");
        tipoEvt.GetProperty("ParteEquipoId").Should().BeNull(
            because: "ParteEquipoId es inmutable y no debe incluirse en HallazgoActualizado_v1 (I-H8)");
        tipoEvt.GetProperty("NovedadPreopOrigenId").Should().BeNull(
            because: "NovedadPreopOrigenId es inmutable y no debe incluirse en HallazgoActualizado_v1 (I-H8)");
    }

    // ── §6.16 — Rebuild desde stream — Apply puro y orden causal ──────────

    [Fact]
    public void ActualizarHallazgo_rebuild_desde_stream_reproduce_estado()
    {
        // Given: inspección con hallazgo NoRequiereIntervencion
        var dados = StreamConHallazgoRegistrado(
            hallazgoId: HallazgoG1,
            accionRequerida: AccionRequerida.NoRequiereIntervencion);

        // When: emitir evento de actualización
        var cmd = ComandoActualizarConIntervencion(
            hallazgoId: HallazgoG1,
            novedadTecnica: "Fuga confirmada — rebuild test",
            accionCorrectiva: "Reemplazar sello",
            tipoFallaId: 3,
            causaFallaId: 12);

        var emitidos = CasoDeUso.ActualizarHallazgo(dados, cmd, Ahora);

        // When: reproyectar el stream completo (previos + emitidos) sobre agregado vacío
        var streamCompleto = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(streamCompleto);

        // Then: rebuild no lanza y el estado resultante es coherente
        var agregado = act.Should().NotThrow().Subject;

        var hallazgo = agregado.Hallazgos
            .Should().ContainSingle(h => h.HallazgoId == HallazgoG1).Subject;

        hallazgo.NovedadTecnica.Should().Be("Fuga confirmada — rebuild test");
        hallazgo.AccionRequerida.Should().Be(AccionRequerida.RequiereIntervencion);
        hallazgo.TipoFallaId.Should().Be(3);
        hallazgo.CausaFallaId.Should().Be(12);
        hallazgo.Eliminado.Should().BeFalse();
    }
}
