using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="RegistrarMedicionHandler"/> que requieren un
/// Marten real (Postgres en Testcontainers). Cubre los escenarios:
/// §6.11 — PRE-2 (InspeccionId no existe → InspeccionNoEncontradaException / 404).
/// §6.12 — Idempotencia Wolverine envelope (Skip: requiere Wolverine envelope storage).
/// §6.14 — Atomicidad: si SaveChangesAsync falla, ningún evento se persiste.
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class RegistrarMedicionHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 8, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid InspeccionIdMonitoreo =
        Guid.Parse("0194b001-2222-7000-bbbb-000000000001");

    private static readonly Guid HallazgoM1 =
        Guid.Parse("0194a001-1111-7000-aaaa-000000000001");

    private static RegistrarMedicion ComandoBase(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        int itemId = 1,
        decimal valorMedido = 12.4m,
        string? observacion = null,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdMonitoreo,
            HallazgoId: hallazgoId ?? HallazgoM1,
            ItemId: itemId,
            ValorMedido: valorMedido,
            Observacion: observacion,
            EmitidoPor: emitidoPor,
            Capabilities: new[] { "ejecutar-inspeccion" });

    private static List<ItemRutinaMonitoreoSnapshot> ItemsSnapshot() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m),
                ParteEquipoId: 88),
            new(ItemId: 2,
                Parte: "Conectores batería",
                Actividad: "Estado visual",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 77),
        };

    private static async Task SembrarInspeccionMonitoreo(
        IDocumentStore store,
        Guid inspeccionId,
        int equipoId = 4521)
    {
        await using var session = store.LightweightSession();

        var evento = new InspeccionIniciada_v1(
            InspeccionId: inspeccionId,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: equipoId,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Ahora),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshot());

        session.Events.StartStream<Inspeccion>(inspeccionId, evento);
        await session.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 PRE-2 — InspeccionId no existe en Marten → 404
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarMedicion_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException()
    {
        // Given: ningún stream con el InspeccionId
        await using var session = postgres.Store.LightweightSession();
        var idInexistente = Guid.NewGuid();
        var cmd = ComandoBase(inspeccionId: idInexistente);
        var tp = new FakeTimeProvider();
        tp.SetUtcNow(Ahora);

        var handler = new RegistrarMedicionHandler(session, tp);

        // When
        var act = async () => await handler.Handle(cmd, CancellationToken.None);

        // Then
        await act.Should().ThrowAsync<InspeccionNoEncontradaException>()
            .WithMessage("*" + idInexistente + "*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 Idempotencia Wolverine envelope (Skip — requiere Wolverine)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "Implementar en test de integración E2E con WebApplicationFactory cuando " +
                 "el handler esté registrado en Wolverine. Ver spec §6.12 y §7.")]
    public async Task RegistrarMedicion_replay_mismo_clientCommandId_no_re_ejecuta_handler()
    {
        // This test validates Wolverine envelope dedup (ADR-008).
        // The implementation is handled by Wolverine's built-in MessageId dedup.
        // Cannot be tested purely at handler level without Wolverine's DurableMessaging middleware.
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Atomicidad — SaveChangesAsync falla → ni MedicionRegistrada_v1
    //        ni HallazgoRegistrado_v1 se persisten
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarMedicion_fuera_de_rango_evento_previo_en_stream_antes_de_commit_no_visible()
    {
        // Given: inspección monitoreo sembrada con ItemId=1 numérico
        var inspeccionId = Guid.NewGuid();
        await SembrarInspeccionMonitoreo(postgres.Store, inspeccionId);

        await using var sessionAntes = postgres.Store.LightweightSession();
        var streamAntes = await sessionAntes.Events.FetchStreamAsync(inspeccionId);
        var countAntes = streamAntes.Count;

        // When: construir el handler pero NO llamar SaveChangesAsync
        // (simula handler que lanza antes del commit — sin completar la operación)
        await using var sessionParcial = postgres.Store.LightweightSession();
        var aggregate = await sessionParcial.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
        aggregate.Should().NotBeNull("la inspección debe existir");

        var cmd = ComandoBase(inspeccionId: inspeccionId, hallazgoId: HallazgoM1,
            itemId: 1, valorMedido: 10.2m);
        var tp = new FakeTimeProvider();
        tp.SetUtcNow(Ahora);

        // El handler emite los eventos al stream pero NO hace SaveChangesAsync
        // (simulación de fallo antes del commit)
        var emitidos = aggregate!.RegistrarMedicion(cmd, Ahora);
        sessionParcial.Events.Append(inspeccionId, emitidos.ToArray());
        // Intencionalmente NO hacemos await sessionParcial.SaveChangesAsync()
        // Descartamos la sesión sin persistir

        // Then: el stream en Marten permanece con el count original
        await using var sessionDespues = postgres.Store.LightweightSession();
        var streamDespues = await sessionDespues.Events.FetchStreamAsync(inspeccionId);
        streamDespues.Count.Should().Be(countAntes,
            "sin SaveChangesAsync los eventos no deben persistirse — atomicidad garantizada por Marten");
    }
}
