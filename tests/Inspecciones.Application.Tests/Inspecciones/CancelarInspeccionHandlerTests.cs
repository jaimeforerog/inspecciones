using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="CancelarInspeccionHandler"/> con Marten real (Postgres Testcontainers).
/// Cubre los escenarios de capa handler del slice 1m §6:
/// §6.5  — PRE-2 (InspeccionId no existe → InspeccionNoEncontradaException / 404).
/// §6.4  — PRE-1 (capability "ejecutar-inspeccion" ausente → manejado en capa HTTP, Skip aquí).
/// §6.1  — camino feliz: handler hidrata aggregate, valida PRE-3/PRE-4, llama Cancelar, persiste un evento.
/// §6.6  — PRE-3 (técnico no contribuyente → TecnicoNoContribuyenteException / 403).
/// §6.7  — PRE-4 (motivo vacío → MotivoCancelacionInvalidoException / 422).
/// §6.10 — PRE-5 (inspección firmada → InspeccionNoEnEjecucionException / 409).
/// TimeProvider — verificar que el handler usa _time.GetUtcNow() y no DateTime.UtcNow.
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class CancelarInspeccionHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de siembra
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Siembra un stream de inspección técnica en estado EnEjecucion.
    /// TecnicoIniciador = "carlos.ruiz" — único contribuyente.
    /// </summary>
    private static async Task<Guid> SembrarInspeccionEnEjecucion(
        IDocumentStore store,
        int equipoId = 4521,
        string tecnicoId = "carlos.ruiz")
    {
        var inspeccionId = Guid.NewGuid();

        await using var session = store.LightweightSession();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: tecnicoId,
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Ahora),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>
    /// Siembra una inspección firmada completa (para escenario PRE-5 §6.10).
    /// </summary>
    private static async Task<Guid> SembrarInspeccionFirmada(
        IDocumentStore store,
        int equipoId = 4522)
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
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "carlos.ruiz",
                RegistradoEn: Ahora),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Inspección completa",
                EmitidoPor: "carlos.ruiz",
                EmitidoEn: Ahora),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin hallazgos críticos",
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

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 PRE-2 — InspeccionId no existe en Marten → 404
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task handler_inspeccion_inexistente_lanza_InspeccionNoEncontradaException_PRE_2()
    {
        // Given: ningún stream con el InspeccionId
        var idInexistente = Guid.NewGuid();
        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new CancelarInspeccion(
            InspeccionId: idInexistente,
            Motivo: "Motivo de cancelación suficientemente largo",
            CanceladaPor: "carlos.ruiz");

        // When
        var handler = new CancelarInspeccionHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: 404 — inspección no encontrada
        await act.Should().ThrowAsync<InspeccionNoEncontradaException>()
            .WithMessage($"*{idInexistente}*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.4 PRE-1 — capability ausente → Skip (capa HTTP)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "PRE-1 vive en el middleware HTTP / endpoint (capa API), no en el handler. " +
                 "Cubierto por CancelarInspeccionEndpointTests — ver spec §4 y §6.4.")]
    public async Task handler_sin_capability_ejecutar_inspeccion_lanza_CapabilityRequerida_PRE_1()
    {
        await Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 Happy path — handler hidrata aggregate, persiste un evento, retorna resultado
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task handler_cancela_inspeccion_existente_persiste_evento_y_devuelve_ok()
    {
        // Given: inspección en EnEjecucion con carlos.ruiz como contribuyente
        var inspeccionId = await SembrarInspeccionEnEjecucion(
            postgres.Store, equipoId: 70001, tecnicoId: "carlos.ruiz");

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new CancelarInspeccion(
            InspeccionId: inspeccionId,
            Motivo: "Equipo trasladado a otra obra sin previo aviso",
            CanceladaPor: "carlos.ruiz");

        // When
        var handler = new CancelarInspeccionHandler(session, tp);
        var resultado = await handler.Handle(cmd);

        // Then: resultado correcto
        resultado.InspeccionId.Should().Be(inspeccionId);
        resultado.Estado.Should().Be("Cancelada");
        resultado.CanceladaPor.Should().Be("carlos.ruiz");
        resultado.Motivo.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        resultado.CanceladaEn.Should().Be(Ahora);

        // Verificación profunda: un solo InspeccionCancelada_v1 en el stream
        await using var verificacion = postgres.Store.QuerySession();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        var streamData = eventos.Select(e => e.Data).ToList();

        var cancelada = streamData.OfType<InspeccionCancelada_v1>()
            .Should().ContainSingle("debe existir exactamente un InspeccionCancelada_v1 en el stream")
            .Which;

        cancelada.InspeccionId.Should().Be(inspeccionId);
        cancelada.Motivo.Should().Be("Equipo trasladado a otra obra sin previo aviso");
        cancelada.CanceladaPor.Should().Be("carlos.ruiz");
        cancelada.CanceladaEn.Should().Be(Ahora);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 PRE-3 — técnico no contribuyente → TecnicoNoContribuyenteException / 403
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task handler_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException()
    {
        // Given: inspección con TecnicoIniciador="carlos.ruiz" (único contribuyente)
        var inspeccionId = await SembrarInspeccionEnEjecucion(
            postgres.Store, equipoId: 70002, tecnicoId: "carlos.ruiz");

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        // When: técnico externo que no contribuyó al stream intenta cancelar
        var cmd = new CancelarInspeccion(
            InspeccionId: inspeccionId,
            Motivo: "Motivo de cancelación suficientemente largo",
            CanceladaPor: "tecnico.externo.99");

        var handler = new CancelarInspeccionHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: PRE-3 — TecnicoNoContribuyenteException
        await act.Should().ThrowAsync<TecnicoNoContribuyenteException>()
            .WithMessage("*tecnico.externo.99*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 PRE-4 — motivo vacío → MotivoCancelacionInvalidoException / 422
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task handler_motivo_vacio_lanza_MotivoCancelacionInvalidoException()
    {
        // Given: inspección en EnEjecucion con el contribuyente correcto
        var inspeccionId = await SembrarInspeccionEnEjecucion(
            postgres.Store, equipoId: 70003, tecnicoId: "carlos.ruiz");

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        // When: motivo vacío
        var cmd = new CancelarInspeccion(
            InspeccionId: inspeccionId,
            Motivo: "",
            CanceladaPor: "carlos.ruiz");

        var handler = new CancelarInspeccionHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: PRE-4 — MotivoCancelacionInvalidoException
        await act.Should().ThrowAsync<MotivoCancelacionInvalidoException>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 PRE-5 — inspección firmada → InspeccionNoEnEjecucionException / 409
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task handler_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I6()
    {
        // Given: inspección en estado Firmada (no EnEjecucion)
        var inspeccionId = await SembrarInspeccionFirmada(
            postgres.Store, equipoId: 70004);

        await using var session = postgres.Store.LightweightSession();
        var tp = new FakeTimeProvider(Ahora);

        var cmd = new CancelarInspeccion(
            InspeccionId: inspeccionId,
            Motivo: "Intento de cancelar inspección ya firmada",
            CanceladaPor: "carlos.ruiz");

        // When
        var handler = new CancelarInspeccionHandler(session, tp);
        var act = async () => await handler.Handle(cmd);

        // Then: PRE-5 — I6: estado Firmada != EnEjecucion
        await act.Should().ThrowAsync<InspeccionNoEnEjecucionException>()
            .WithMessage("*Firmada*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // TimeProvider — handler usa _time.GetUtcNow(), no DateTime.UtcNow
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task handler_uso_TimeProvider_para_CanceladaEn()
    {
        // Given: inspección en EnEjecucion; TimeProvider fijo en un timestamp conocido
        var inspeccionId = await SembrarInspeccionEnEjecucion(
            postgres.Store, equipoId: 70005, tecnicoId: "carlos.ruiz");

        var timestampEsperado = new DateTimeOffset(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);
        var tp = new FakeTimeProvider(timestampEsperado);

        await using var session = postgres.Store.LightweightSession();

        var cmd = new CancelarInspeccion(
            InspeccionId: inspeccionId,
            Motivo: "Verificación de timestamp inyectado por TimeProvider",
            CanceladaPor: "carlos.ruiz");

        // When
        var handler = new CancelarInspeccionHandler(session, tp);
        var resultado = await handler.Handle(cmd);

        // Then: CanceladaEn proviene del TimeProvider, no de DateTime.UtcNow
        resultado.CanceladaEn.Should().Be(timestampEsperado,
            "el handler debe usar _time.GetUtcNow() — TimeProvider inyectado — no DateTime.UtcNow");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Idempotencia — replay con mismo X-Client-Command-Id (Skip)
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
                 "Implementar en test E2E con WebApplicationFactory cuando " +
                 "el handler esté registrado en Wolverine. Ver spec §6.14, §7, ADR-008.")]
    public async Task handler_replay_mismo_clientCommandId_no_re_ejecuta_handler_ADR_008()
    {
        await Task.CompletedTask;
    }
}
