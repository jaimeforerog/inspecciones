using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="GenerarOTHandler"/> con Marten real (Postgres Testcontainers).
/// Cubre los escenarios de capa handler del slice 1k §6:
/// §6.10 — PRE-2 (InspeccionId no existe → InspeccionNoEncontradaException / 404).
/// §6.3  — PRE-1 (capability "generar-ot" ausente → manejado en capa HTTP, Skip aquí).
/// §6.1  — camino feliz: handler hidrata aggregate, llama SolicitarOT, persiste OTSolicitada_v1.
/// §6.6  — PRE-5 (OT ya solicitada → OTYaSolicitadaException / 409).
/// §6.4  — PRE-3 (inspección no firmada → InspeccionNoFirmadaException / 422).
/// §6.9  — Idempotencia ADR-008 (Skip: requiere Wolverine envelope storage).
/// §6.14 — Atomicidad: SolicitarOT sin SaveChangesAsync → evento no persiste.
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class GenerarOTHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora =
        new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream de inspección técnica en estado Firmada con un hallazgo
    /// RequiereIntervencion y dictamen NoPuedeOperar — cumple todas las precondiciones
    /// I-F4 para ejecutar GenerarOT exitosamente.
    /// </summary>
    private static async Task<Guid> SembrarInspeccionFirmadaConHallazgoIntervencion(
        IDocumentStore store,
        int equipoId = 4521)
    {
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "carlos.ruiz",
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
                ActividadDescripcion: "Revisión brazo hidráulico",
                NovedadTecnica: "Falla estructural en brazo hidráulico",
                AccionRequerida: AccionRequerida.RequiereIntervencion,
                AccionCorrectiva: "Reemplazar brazo",
                TipoFallaId: 1,
                CausaFallaId: 2,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "carlos.ruiz",
                RegistradoEn: Ahora),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Falla estructural en brazo hidráulico",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.NoPuedeOperar,
                Justificacion: "Equipo fuera de operación",
                EmitidoPor: "carlos.ruiz",
                EstablecidoEn: Ahora),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "carlos.ruiz",
                FirmaUri: "https://blobs/firma-01.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, Ahora),
                FirmadaEn: Ahora));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra una inspección firmada y luego añade un OTSolicitada_v1 al stream
    /// para cubrir el escenario PRE-5 (§6.6).
    /// </summary>
    private static async Task<Guid> SembrarInspeccionConOTYaSolicitada(
        IDocumentStore store,
        int equipoId = 4525)
    {
        var inspeccionId = await SembrarInspeccionFirmadaConHallazgoIntervencion(store, equipoId);

        await using var session = store.LightweightSession();
        session.Events.Append(inspeccionId,
            new OTSolicitada_v1(
                InspeccionId: inspeccionId,
                SolicitadaPor: "jefe.campo.previo",
                Responsable: ResponsableCosto.Proyecto,
                Prioridad: PrioridadOT.Urgente,
                Observaciones: null,
                ComentarioJefe: null,
                SolicitadaEn: Ahora));
        await session.SaveChangesAsync();
        return inspeccionId;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 PRE-2 — InspeccionId no existe en Marten → 404
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2()
    {
        // Given: ningún stream con el InspeccionId
        var idInexistente = Guid.NewGuid();
        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new GenerarOT(
            InspeccionId: idInexistente,
            SolicitadaPor: "jefe.campo.01",
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: null,
            ComentarioJefe: null,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Normal);

        // When
        var handler = new GenerarOTHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: 404 — inspección no encontrada
        await act.Should().ThrowAsync<InspeccionNoEncontradaException>()
            .WithMessage($"*{idInexistente}*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 PRE-1 — capability "generar-ot" ausente → 403
    // La capability se verifica en la capa HTTP (middleware de autorización)
    // ANTES de invocar el handler. El handler no la re-verifica.
    // Este test delega la cobertura al test E2E del endpoint.
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-1 vive en el middleware HTTP / endpoint (capa API), no en el handler. " +
                 "Cubierto por GenerarOTEndpointTests — ver spec §4 y §6.3.")]
    public async Task GenerarOT_sin_capability_generar_ot_lanza_CapabilityRequerida_PRE_1()
    {
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — handler hidrata aggregate, persiste OTSolicitada_v1
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerarOT_happy_path_handler_persiste_OTSolicitada_v1_y_retorna_resultado()
    {
        // Given: inspección firmada con hallazgo RequiereIntervencion y NoPuedeOperar
        var inspeccionId = await SembrarInspeccionFirmadaConHallazgoIntervencion(
            postgres.Store, equipoId: 30001);

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new GenerarOT(
            InspeccionId: inspeccionId,
            SolicitadaPor: "jefe.campo.01",
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: "Equipo fuera de operación — prioridad máxima",
            ComentarioJefe: null,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Urgente);

        // When
        var handler = new GenerarOTHandler(session, tp);
        var resultado = await handler.Handle(cmd);

        // Then: resultado correcto
        resultado.InspeccionId.Should().Be(inspeccionId);
        resultado.SolicitadaPor.Should().Be("jefe.campo.01");
        resultado.Responsable.Should().Be("Proyecto");
        resultado.Prioridad.Should().Be("Urgente");
        resultado.SolicitadaEn.Should().Be(Ahora);

        // Verificación profunda: OTSolicitada_v1 persistido en el stream
        await using var verificacion = postgres.Store.QuerySession();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        var evtOT = eventos.Select(e => e.Data).OfType<OTSolicitada_v1>()
            .Should().ContainSingle("debe existir exactamente un OTSolicitada_v1 en el stream")
            .Which;

        evtOT.InspeccionId.Should().Be(inspeccionId);
        evtOT.SolicitadaPor.Should().Be("jefe.campo.01");
        evtOT.Responsable.Should().Be(ResponsableCosto.Proyecto);
        evtOT.Prioridad.Should().Be(PrioridadOT.Urgente);
        evtOT.Observaciones.Should().Be("Equipo fuera de operación — prioridad máxima");
        evtOT.ComentarioJefe.Should().BeNull();
        evtOT.SolicitadaEn.Should().Be(Ahora);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 PRE-5 / I-F4.c — OT ya solicitada previamente → 409
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerarOT_con_OT_ya_solicitada_en_stream_lanza_OTYaSolicitadaException_I_F4_c()
    {
        // Given: inspección firmada con OTSolicitada_v1 previo en el stream
        var inspeccionId = await SembrarInspeccionConOTYaSolicitada(
            postgres.Store, equipoId: 30002);

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new GenerarOT(
            InspeccionId: inspeccionId,
            SolicitadaPor: "jefe.campo.02",
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: null,
            ComentarioJefe: null,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Alta);

        // When
        var handler = new GenerarOTHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: 409 — OT ya solicitada (PRE-5 I-F4.c)
        await act.Should().ThrowAsync<OTYaSolicitadaException>()
            .WithMessage("*solicitada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 PRE-3 / I-F4.a — inspección no firmada (EnEjecucion) → 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerarOT_con_inspeccion_no_firmada_EnEjecucion_lanza_InspeccionNoFirmadaException_I_F4_a()
    {
        // Given: inspección en estado EnEjecucion (sin firmar)
        var inspeccionId = Guid.NewGuid();
        await using var sessionSiembra = postgres.Store.LightweightSession();

        sessionSiembra.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 4530,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "carlos.ruiz",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Ahora),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null));
        await sessionSiembra.SaveChangesAsync();

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new GenerarOT(
            InspeccionId: inspeccionId,
            SolicitadaPor: "jefe.campo.01",
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: null,
            ComentarioJefe: null,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Normal);

        // When
        var handler = new GenerarOTHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: 422 — inspección no está en estado Firmada (PRE-3 I-F4.a)
        await act.Should().ThrowAsync<InspeccionNoFirmadaException>()
            .WithMessage("*EnEjecucion*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.9 Idempotencia — replay con mismo X-Client-Command-Id (Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "Implementar en test E2E con WebApplicationFactory cuando " +
                 "el handler esté registrado en Wolverine. Ver spec §6.9, §7, ADR-008.")]
    public async Task GenerarOT_replay_mismo_clientCommandId_no_re_ejecuta_handler_ADR_008()
    {
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 (análogo) Atomicidad — sin SaveChangesAsync el evento no persiste
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerarOT_sin_SaveChangesAsync_el_evento_no_se_persiste_atomicidad()
    {
        // Given: inspección firmada con las condiciones correctas
        var inspeccionId = await SembrarInspeccionFirmadaConHallazgoIntervencion(
            postgres.Store, equipoId: 30003);

        await using var sessionAntes = postgres.Store.QuerySession();
        var streamAntes = await sessionAntes.Events.FetchStreamAsync(inspeccionId);
        var countAntes = streamAntes.Count;

        // When: construir el aggregate y llamar SolicitarOT, pero NO hacer SaveChangesAsync
        await using var sessionParcial = postgres.Store.LightweightSession();
        var aggregate = await sessionParcial.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
        aggregate.Should().NotBeNull("la inspección debe existir");

        var cmd = new GenerarOT(
            InspeccionId: inspeccionId,
            SolicitadaPor: "jefe.campo.01",
            Responsable: ResponsableCosto.Proyecto,
            Observaciones: null,
            ComentarioJefe: null,
            Capabilities: new[] { "generar-ot" },
            Prioridad: PrioridadOT.Urgente);

        // Llamar directamente al método de dominio (no el handler)
        // para simular que el handler falla antes del commit
        var emitidos = aggregate!.SolicitarOT(cmd, Ahora);
        sessionParcial.Events.Append(inspeccionId, emitidos.ToArray());
        // Intencionalmente NO hacemos SaveChangesAsync — descartamos la sesión sin persistir

        // Then: el stream en Marten permanece con el count original
        await using var sessionDespues = postgres.Store.QuerySession();
        var streamDespues = await sessionDespues.Events.FetchStreamAsync(inspeccionId);
        streamDespues.Count.Should().Be(countAntes,
            "sin SaveChangesAsync los eventos no deben persistirse — atomicidad garantizada por Marten");
    }
}
