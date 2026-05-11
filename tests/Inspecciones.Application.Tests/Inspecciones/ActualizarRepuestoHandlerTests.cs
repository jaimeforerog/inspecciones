using FluentAssertions;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="ActualizarRepuestoHandler"/> con Marten real
/// (Postgres en Testcontainers). Spec slice 1o §4 — escenarios de capa handler:
/// <list type="bullet">
///   <item>§6.12 — PRE-1 (InspeccionId no existe → <see cref="InspeccionNoEncontradaException"/>).</item>
///   <item>§6.1  — Happy path: handler carga aggregate, normaliza observación, delega, persiste.</item>
///   <item>TimeProvider — el handler usa <c>_time.GetUtcNow()</c>, no <c>DateTime.UtcNow</c>.</item>
/// </list>
/// Los tests de PRE-0 (capability) viven en <c>ActualizarRepuestoEndpointTests</c> (capa HTTP).
/// Los tests de PRE-2..PRE-8 viven en <c>ActualizarRepuestoTests</c> (dominio puro).
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class ActualizarRepuestoHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream con inspección EnEjecucion + hallazgo G1 + repuesto R1 (Cantidad=1).
    /// </summary>
    private static async Task<(Guid InspeccionId, Guid HallazgoId, Guid RepuestoId)>
        SembrarInspeccionConRepuesto(IDocumentStore store, int equipoId)
    {
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        var repuestoId = Guid.NewGuid();

        await using var session = store.LightweightSession();
        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "rmartinez",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Ahora),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            new HallazgoRegistrado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                Origen: OrigenHallazgo.Manual,
                NovedadPreopOrigenId: null,
                MedicionOrigenId: null,
                EvaluacionOrigenId: null,
                ParteEquipoId: 77,
                ActividadId: null,
                ActividadDescripcion: "Revisión sello hidráulico",
                NovedadTecnica: "Sello con desgaste avanzado",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar sello hidráulico",
                TipoFallaId: 3,
                CausaFallaId: 12,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "rmartinez",
                RegistradoEn: Ahora),
            new RepuestoEstimado_v1(
                InspeccionId: inspeccionId,
                HallazgoId: hallazgoId,
                RepuestoId: repuestoId,
                SkuId: 501,
                SkuCodigo: "INS-501",
                Cantidad: 1m,
                Justificacion: "Cambio rutinario",
                Unidad: "unidad",
                AsignadoPor: "rmartinez",
                AsignadoEn: Ahora));

        await session.SaveChangesAsync();
        return (inspeccionId, hallazgoId, repuestoId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 — PRE-1: InspeccionId no existe → InspeccionNoEncontradaException
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarRepuesto_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE1()
    {
        // Given: no existe ningún stream con este InspeccionId
        var store = postgres.Store;
        var fakeTime = new FakeTimeProvider(Ahora);
        var handler = new ActualizarRepuestoHandler(store.LightweightSession(), fakeTime);

        var cmd = new ActualizarRepuesto(
            InspeccionId: Guid.NewGuid(), // no existe en Marten
            HallazgoId: Guid.NewGuid(),
            RepuestoId: Guid.NewGuid(),
            CantidadNueva: 2m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");

        // When
        var act = () => handler.Handle(cmd);

        // Then: PRE-1 — InspeccionId no encontrado
        await act.Should().ThrowAsync<InspeccionNoEncontradaException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 (handler) — Happy path: handler persiste un RepuestoActualizado_v1
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarRepuesto_handler_happy_path_persiste_evento_y_devuelve_resultado()
    {
        // Given: inspección EnEjecucion con repuesto R1 (Cantidad=1)
        var store = postgres.Store;
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(store, equipoId: 50001);

        var fakeTime = new FakeTimeProvider(Ahora);
        await using var session = store.LightweightSession();
        var handler = new ActualizarRepuestoHandler(session, fakeTime);

        var cmd = new ActualizarRepuesto(
            InspeccionId: inspeccionId,
            HallazgoId: hallazgoId,
            RepuestoId: repuestoId,
            CantidadNueva: 2m,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");

        // When
        var resultado = await handler.Handle(cmd);

        // Then: resultado refleja el estado post-actualización
        resultado.InspeccionId.Should().Be(inspeccionId);
        resultado.HallazgoId.Should().Be(hallazgoId);
        resultado.RepuestoId.Should().Be(repuestoId);
        resultado.Cantidad.Should().Be(2m, "Cantidad actualizada a 2");
        resultado.Justificacion.Should().Be("Cambio rutinario",
            "Justificacion no cambió (ObservacionNueva=null); se preserva el valor anterior");
        resultado.ActualizadoEn.Should().Be(Ahora,
            "el handler usa TimeProvider.GetUtcNow(), no DateTime.UtcNow");
    }

    [Fact]
    public async Task ActualizarRepuesto_handler_persiste_evento_en_stream_de_Marten()
    {
        // Given: inspección EnEjecucion con repuesto R1
        var store = postgres.Store;
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(store, equipoId: 50002);

        var fakeTime = new FakeTimeProvider(Ahora);
        await using var session = store.LightweightSession();
        var handler = new ActualizarRepuestoHandler(session, fakeTime);

        var cmd = new ActualizarRepuesto(
            InspeccionId: inspeccionId,
            HallazgoId: hallazgoId,
            RepuestoId: repuestoId,
            CantidadNueva: 3m,
            ObservacionNueva: "Revisión extendida",
            ActualizadoPor: "rmartinez");

        // When: ejecutar el handler
        await handler.Handle(cmd);

        // Then: el aggregate reconstruido desde Marten refleja el estado post-actualización
        await using var readSession = store.LightweightSession();
        var agregadoPostUpdate = await readSession.Events
            .AggregateStreamAsync<Inspeccion>(inspeccionId);

        agregadoPostUpdate.Should().NotBeNull();
        var repuesto = agregadoPostUpdate!.Repuestos.Single(r => r.RepuestoId == repuestoId);
        repuesto.Cantidad.Should().Be(3m);
        repuesto.Justificacion.Should().Be("Revisión extendida");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Normalización P-2: ObservacionNueva vacía se normaliza a null
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarRepuesto_observacion_vacia_se_normaliza_a_null_P2()
    {
        // Given: inspección EnEjecucion con repuesto R1 (Justificacion="Cambio rutinario")
        var store = postgres.Store;
        var (inspeccionId, hallazgoId, repuestoId) =
            await SembrarInspeccionConRepuesto(store, equipoId: 50003);

        var fakeTime = new FakeTimeProvider(Ahora);
        await using var session = store.LightweightSession();
        var handler = new ActualizarRepuestoHandler(session, fakeTime);

        // When: ObservacionNueva = "   " (solo espacios — debe normalizarse a null en handler P-2)
        // El handler normaliza ObservacionNueva?.Trim() == "" → null, por lo que
        // el comando resultante tiene ObservacionNueva=null → limpiar la justificacion.
        // Dado que "no cambiar" y "limpiar a null" se representan de la misma forma (null en
        // el evento delta), el resultado observable es que Justificacion queda null.
        var cmd = new ActualizarRepuesto(
            InspeccionId: inspeccionId,
            HallazgoId: hallazgoId,
            RepuestoId: repuestoId,
            CantidadNueva: null,
            ObservacionNueva: "   ",   // solo whitespace — normalizar a null según P-2
            ActualizadoPor: "rmartinez");

        // Then: el handler normaliza a null (P-2 opción A); como CantidadNueva también es null,
        // PRE-8 lanzará ComandoSinCambiosException porque ambos campos resultan null post-normalización.
        // Esto es el comportamiento correcto según P-2 + PRE-8.
        var act = () => handler.Handle(cmd);
        await act.Should().ThrowAsync<ComandoSinCambiosException>(
            "P-2: ObservacionNueva vacía se normaliza a null en handler, " +
            "y PRE-8 rechaza el comando cuando ambos campos son null post-normalización");
    }
}
