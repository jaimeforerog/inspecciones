using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="RegistrarHallazgoHandler"/> que requieren un
/// Marten real (Postgres en Testcontainers). Cubre los escenarios §6.6
/// (INV-PartePerteneceAlEquipo) y §6.14 (PRE-2 — InspeccionId no existe).
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class RegistrarHallazgoHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 6, 10, 0, 0, TimeSpan.FromHours(-5));

    private static readonly Guid InspeccionId = new("0193c4f7-1234-7abc-8def-000000000001");
    private static readonly Guid HallazgoG1  = new("0193d4f7-1234-7abc-8def-000000000011");

    private static RegistrarHallazgo ComandoBase(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        int parteEquipoId = 77) =>
        new(InspeccionId: inspeccionId ?? InspeccionId,
            HallazgoId: hallazgoId ?? HallazgoG1,
            Origen: OrigenHallazgo.Manual,
            ParteEquipoId: parteEquipoId,
            NovedadPreopOrigenId: null,
            ActividadId: null,
            ActividadDescripcion: "Revisión visual de manguera",
            NovedadTecnica: "Manguera con desgaste leve superficial",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: "ana.gomez");

    /// <summary>
    /// Siembra un stream de inspección y el catálogo de equipo con partes en Marten.
    /// </summary>
    private static async Task SembrarInspeccionYEquipo(
        IDocumentStore store,
        Guid inspeccionId,
        int equipoId = 4521,
        int[] partesValidas = null!)
    {
        partesValidas ??= [77, 88, 99];

        await using var session = store.LightweightSession();

        // Stream de inspección en EnEjecucion
        var evento = new InspeccionIniciada_v1(
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
            LecturaMedidorSecundario: null);

        session.Events.StartStream<Inspeccion>(inspeccionId, evento);

        // Catálogo del equipo con partes
        var partes = partesValidas.Select(id =>
            new ParteEquipoLocal(id, $"PARTE-{id}", $"Parte {id}")).ToList();

        var equipo = new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
            RutinaTecnicaId: 18,
            Partes: partes);

        session.Store(equipo);
        await session.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.14 Violación PRE-2 — InspeccionId no existe en Marten
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarHallazgo_inspeccion_no_existe_lanza_InspeccionNoEncontrada_PRE_2()
    {
        // Given: ningún stream con InspeccionId=Z en Marten
        var idInexistente = Guid.NewGuid();
        await using var session = postgres.Store.LightweightSession();

        // When
        var cmd = ComandoBase(inspeccionId: idInexistente);
        var act = async () => await EjecutarHandler(session, cmd);

        // Then: 404 — inspección no encontrada
        (await act.Should().ThrowAsync<InspeccionNoEncontradaException>())
            .WithMessage($"*{idInexistente}*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 Violación INV-PartePerteneceAlEquipo — parte no pertenece al equipo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarHallazgo_parte_no_pertenece_al_equipo_lanza_ParteNoCorrespondeAlEquipo_INV()
    {
        // Given: equipo 4521 con partes [77, 88], comando con parte 9999
        var inspeccionId = Guid.NewGuid();
        await SembrarInspeccionYEquipo(postgres.Store, inspeccionId, equipoId: 4521,
            partesValidas: [77, 88]);

        await using var session = postgres.Store.LightweightSession();
        var cmd = ComandoBase(inspeccionId: inspeccionId, parteEquipoId: 9999);

        // When / Then
        var act = async () => await EjecutarHandler(session, cmd);

        (await act.Should().ThrowAsync<ParteNoCorrespondeAlEquipoException>())
            .WithMessage("*9999*4521*");
    }

    [Fact]
    public async Task RegistrarHallazgo_con_parte_valida_del_equipo_persiste_evento_y_retorna_resultado()
    {
        // Given: equipo con parte 77 incluida, inspección en EnEjecucion
        var inspeccionId = Guid.NewGuid();
        await SembrarInspeccionYEquipo(postgres.Store, inspeccionId, equipoId: 4521,
            partesValidas: [77, 88]);

        await using var session = postgres.Store.LightweightSession();
        var cmd = ComandoBase(inspeccionId: inspeccionId, parteEquipoId: 77);

        // When: handler ejecuta con parte válida
        var resultado = await EjecutarHandler(session, cmd);

        // Then: retorna resultado correcto — INV-PartePerteneceAlEquipo no bloqueó
        resultado.HallazgoId.Should().Be(cmd.HallazgoId);
        resultado.InspeccionId.Should().Be(inspeccionId);
        resultado.AccionRequerida.Should().Be(AccionRequerida.NoRequiereIntervencion);

        // Verificación profunda: evento persistido en el stream
        await using var verificacion = postgres.Store.QuerySession();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionId);
        eventos.Select(e => e.Data).OfType<HallazgoRegistrado_v1>()
            .Should().ContainSingle("debe existir exactamente un HallazgoRegistrado_v1");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<RegistrarHallazgoResult> EjecutarHandler(
        IDocumentSession session,
        RegistrarHallazgo cmd)
    {
        var time = new FakeTimeProvider(Ahora);
        var handler = new RegistrarHallazgoHandler(session, time);
        return await handler.ManejarAsync(cmd);
    }
}
