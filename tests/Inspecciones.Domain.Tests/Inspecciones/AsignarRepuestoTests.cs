using FluentAssertions;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Tests en rojo para el slice 1f — AsignarRepuesto.
/// Un test por escenario de la spec §6. Todos fallan con
/// <see cref="NotImplementedException"/> hasta que green implemente la lógica,
/// salvo §6.13 que falla porque EliminarHallazgo no verifica _repuestos aún.
/// Los escenarios §6.10, §6.11, §6.12 viven en el handler (capa aplicación,
/// requieren acceso al catálogo local en Marten) — son tests de integración,
/// documentados en red-notes §6.10..§6.12.
/// </summary>
public sealed class AsignarRepuestoTests
{
    // ── §6.1 — Happy path: repuesto asignado correctamente ──────────────────

    [Fact]
    public void AsignarRepuesto_en_inspeccion_en_ejecucion_con_RequiereIntervencion_emite_RepuestoEstimado_v1()
    {
        // Given: inspección EnEjecucion con hallazgo G1 (RequiereIntervencion)
        var dados = StreamConHallazgoParaRepuesto(hallazgoId: HallazgoG1, parteEquipoId: 77);

        // When: asignar repuesto R1 (SkuId=501)
        var cmd = ComandoAsignarRepuesto(
            hallazgoId: HallazgoG1,
            repuestoId: RepuestoR1,
            skuId: 501,
            cantidad: 2m,
            justificacion: "Sello desgastado — requiere 2 unidades",
            tecnicoId: "rmartinez");

        var resultado = CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: exactamente un RepuestoEstimado_v1 con todos los campos correctos
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoEstimado_v1>().Subject;

        evt.InspeccionId.Should().Be(InspeccionIdNueva);
        evt.HallazgoId.Should().Be(HallazgoG1);
        evt.RepuestoId.Should().Be(RepuestoR1);
        evt.SkuId.Should().Be(501);
        evt.SkuCodigo.Should().Be("INS-501");
        evt.Cantidad.Should().Be(2m);
        evt.Justificacion.Should().Be("Sello desgastado — requiere 2 unidades");
        evt.Unidad.Should().Be("unidad");
        evt.AsignadoPor.Should().Be("rmartinez");
        evt.AsignadoEn.Should().Be(Ahora);
    }

    // ── §6.2 — Happy path: cantidad fraccionaria (litros/galones) ────────────

    [Fact]
    public void AsignarRepuesto_con_cantidad_fraccionaria_emite_RepuestoEstimado_v1_con_fraccion()
    {
        // Given: inspección EnEjecucion con hallazgo G2 (RequiereIntervencion, parte 33)
        var dados = StreamConHallazgoParaRepuesto(hallazgoId: HallazgoG2, parteEquipoId: 33);

        // When: asignar repuesto con Cantidad=0.5 (galones)
        var cmd = ComandoAsignarRepuesto(
            hallazgoId: HallazgoG2,
            repuestoId: RepuestoR2,
            skuId: 201,
            cantidad: 0.5m,
            justificacion: null,
            tecnicoId: "jperez");

        var resultado = CasoDeUso.AsignarRepuesto(dados, cmd, "FLT-201", "galón", Ahora);

        // Then: fracción y unidad persisten correctamente
        var evt = resultado.Should().ContainSingle()
            .Which.Should().BeOfType<RepuestoEstimado_v1>().Subject;

        evt.Cantidad.Should().Be(0.5m);
        evt.Unidad.Should().Be("galón");
        evt.Justificacion.Should().BeNull();
        evt.AsignadoPor.Should().Be("jperez");
    }

    // ── §6.3 — Idempotencia PRE-D: retry con el mismo RepuestoId ─────────────

    [Fact]
    public void AsignarRepuesto_con_RepuestoId_ya_existente_devuelve_lista_vacia_sin_lanzar_PRE_D()
    {
        // Given: stream con repuesto R1 ya estimado en hallazgo G1
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                parteEquipoId: 77,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            RepuestoEstimadoEjemplo(hallazgoId: HallazgoG1, repuestoId: RepuestoR1, skuId: 501),
        };

        // When: retry con el mismo RepuestoId
        var cmd = ComandoAsignarRepuesto(
            hallazgoId: HallazgoG1,
            repuestoId: RepuestoR1,
            skuId: 501,
            cantidad: 2m,
            justificacion: null,
            tecnicoId: "rmartinez");

        var resultado = CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: no se emite ningún evento (idempotencia silenciosa)
        resultado.Should().BeEmpty();
    }

    // ── §6.4 — PRE-A / I-H7: inspección no está en EnEjecucion ──────────────

    [Fact]
    public void AsignarRepuesto_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7()
    {
        // Given: inspección en estado Firmada con hallazgo activo
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            new InspeccionFirmada_v1(
                InspeccionId: InspeccionIdNueva,
                FirmadoPor: "rmartinez",
                FirmaUri: "https://blobs/firma-fixture.png",
                UbicacionFirma: UbicacionTipo(),
                FirmadaEn: Ahora),
        };

        // When: intentar asignar repuesto con inspección firmada
        var cmd = ComandoAsignarRepuesto(hallazgoId: HallazgoG1, repuestoId: RepuestoR1, skuId: 501);

        var act = () => CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: PRE-A — estado no es EnEjecucion (I-H7)
        act.Should().Throw<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ── §6.5 — PRE-B1: HallazgoId no existe en el aggregate ─────────────────

    [Fact]
    public void AsignarRepuesto_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE_B1()
    {
        // Given: inspección EnEjecucion sin hallazgos
        var dados = new object[] { EventoInspeccionIniciada() };

        // When: hallazgoId que no existe en el stream
        var cmd = ComandoAsignarRepuesto(hallazgoId: HallazgoG3, repuestoId: RepuestoR1, skuId: 501);

        var act = () => CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: PRE-B1
        act.Should().Throw<HallazgoNoEncontradoException>();
    }

    // ── §6.6 — PRE-B2: HallazgoId existe pero está eliminado ────────────────

    [Fact]
    public void AsignarRepuesto_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException_PRE_B2()
    {
        // Given: hallazgo G3 eliminado (soft delete)
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG3,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            HallazgoEliminadoEjemplo(hallazgoId: HallazgoG3),
        };

        // When: intentar asignar repuesto a hallazgo eliminado
        var cmd = ComandoAsignarRepuesto(hallazgoId: HallazgoG3, repuestoId: RepuestoR1, skuId: 501);

        var act = () => CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: PRE-B2 — hallazgo eliminado
        act.Should().Throw<HallazgoEliminadoException>()
            .WithMessage($"*{HallazgoG3}*");
    }

    // ── §6.7 — PRE-C / I-H12: hallazgo no requiere intervención ─────────────

    [Fact]
    public void AsignarRepuesto_en_hallazgo_NoRequiereIntervencion_lanza_HallazgoNoRequiereIntervencionException_I_H12()
    {
        // Given: hallazgo G4 con AccionRequerida=NoRequiereIntervencion
        var dados = new object[]
        {
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG4,
                accionRequerida: AccionRequerida.NoRequiereIntervencion),
        };

        // When: intentar asignar repuesto a hallazgo sin intervención
        var cmd = ComandoAsignarRepuesto(hallazgoId: HallazgoG4, repuestoId: RepuestoR1, skuId: 501);

        var act = () => CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: PRE-C / I-H12
        act.Should().Throw<HallazgoNoRequiereIntervencionException>()
            .WithMessage($"*{HallazgoG4}*");
    }

    // ── §6.8 — PRE-E: Cantidad igual o menor a cero ──────────────────────────

    [Fact]
    public void AsignarRepuesto_con_Cantidad_cero_lanza_CantidadInvalidaException_PRE_E()
    {
        // Given: inspección EnEjecucion con hallazgo G1 (RequiereIntervencion)
        var dados = StreamConHallazgoParaRepuesto(hallazgoId: HallazgoG1);

        // When: cantidad = 0
        var cmd = ComandoAsignarRepuesto(
            hallazgoId: HallazgoG1,
            repuestoId: RepuestoR1,
            skuId: 501,
            cantidad: 0m);

        var act = () => CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: PRE-E
        act.Should().Throw<CantidadInvalidaException>()
            .WithMessage("*cero*");
    }

    // ── §6.9 — PRE-G: mismo SkuId en el mismo hallazgo con distinto RepuestoId

    [Fact]
    public void AsignarRepuesto_con_SkuId_duplicado_en_hallazgo_distinto_RepuestoId_lanza_SkuDuplicadoEnHallazgoException_PRE_G()
    {
        // Given: hallazgo G5 con repuesto R1 (SkuId=501) ya estimado
        var dados = StreamConHallazgoConRepuestoActivo();  // G5, R1, SkuId=501

        // When: nuevo repuesto R2 con el mismo SkuId=501 en el mismo hallazgo G5
        var cmd = ComandoAsignarRepuesto(
            hallazgoId: HallazgoG5,
            repuestoId: RepuestoR2,   // distinto RepuestoId — no es retry
            skuId: 501);

        var act = () => CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // Then: PRE-G — SKU duplicado con distinto ID es error de negocio
        act.Should().Throw<SkuDuplicadoEnHallazgoException>()
            .WithMessage("*501*");
    }

    // ── §6.13 — DoD I-H9: levantar skip del test EliminarHallazgo §6.7 ──────
    // Este test verifica que EliminarHallazgo lanza HallazgoTieneHijosActivosException
    // cuando el hallazgo tiene repuestos activos. El skip fue levantado porque
    // ahora existe StreamConHallazgoConRepuestoActivo() con RepuestoEstimado_v1.
    // El test llama a CasoDeUso.EliminarHallazgo — NO a CasoDeUso.AsignarRepuesto.

    [Fact]
    public void EliminarHallazgo_con_repuesto_activo_lanza_HallazgoTieneHijosActivosException_I_H9()
    {
        // Given: hallazgo G5 con repuesto activo R1
        var dados = StreamConHallazgoConRepuestoActivo();

        // When: intentar eliminar el hallazgo que tiene hijos
        var cmd = new EliminarHallazgo(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG5,
            Motivo: "Error de registro",
            TecnicoId: "rmartinez");

        var act = () => CasoDeUso.EliminarHallazgo(dados, cmd, Ahora);

        // Then: PRE-D / I-H9 — no se puede eliminar hallazgo con hijos activos
        act.Should().Throw<HallazgoTieneHijosActivosException>()
            .WithMessage($"*{HallazgoG5}*");
    }

    // ── §6.14 — Rebuild desde stream: Apply puro y orden causal (obligatorio) ─

    [Fact]
    public void AsignarRepuesto_rebuild_desde_stream_reproduce_estado_con_repuesto()
    {
        // Given: stream previo con hallazgo G1 (RequiereIntervencion)
        var dados = StreamConHallazgoParaRepuesto(hallazgoId: HallazgoG1, parteEquipoId: 77);

        // When: emitir el evento de asignación
        var cmd = ComandoAsignarRepuesto(
            hallazgoId: HallazgoG1,
            repuestoId: RepuestoR1,
            skuId: 501,
            cantidad: 2m,
            justificacion: "Sello desgastado",
            tecnicoId: "rmartinez");

        var emitidos = CasoDeUso.AsignarRepuesto(dados, cmd, "INS-501", "unidad", Ahora);

        // When: reproyectar el stream completo (previos + emitidos) sobre un agregado vacío
        var streamCompleto = dados.Concat(emitidos).ToArray();
        var act = () => Inspeccion.Reconstruir(streamCompleto);

        // Then: rebuild no lanza (Apply es puro — sin validaciones)
        var agregado = act.Should().NotThrow().Subject;

        agregado.Estado.Should().Be(EstadoInspeccion.EnEjecucion);
        agregado.Repuestos.Should().HaveCount(1);
        agregado.Repuestos[0].RepuestoId.Should().Be(RepuestoR1);
        agregado.Repuestos[0].HallazgoId.Should().Be(HallazgoG1);
        agregado.Repuestos[0].SkuId.Should().Be(501);
        agregado.Repuestos[0].Cantidad.Should().Be(2m);
        agregado.Repuestos[0].Unidad.Should().Be("unidad");
        agregado.Contribuyentes.Should().Contain("rmartinez");
    }
}