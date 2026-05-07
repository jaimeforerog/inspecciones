using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="IniciarInspeccionMonitoreoHandler"/> que requieren
/// un Marten real (Postgres en Testcontainers). Cubre los escenarios §6.2..§6.8
/// y §6.13 del spec del slice 1h: I-I1 shortcut blanda, race condition concurrente,
/// PRE-3 equipo no encontrado, PRE-4 rutina no sincronizada, PRE-5 rutina de grupo
/// distinto, PRE-6 rutina sin items activos, §6.12 snapshot solo items activos, y
/// atomicidad evento + proyección.
///
/// Los escenarios §6.3 (race condition) y §6.4 (idempotencia Wolverine envelope)
/// se incluyen pero pueden requerir infraestructura adicional no disponible en CI
/// local. Ver §5 de red-notes.md.
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class IniciarInspeccionMonitoreoHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 7, 10, 0, 0, TimeSpan.FromHours(-5));

    private static IniciarInspeccionMonitoreo Comando(
        Guid? inspeccionId = null,
        int equipoId = 4521,
        int proyectoId = 3,
        int rutinaMonitoreoId = 42) =>
        new(InspeccionId: inspeccionId ?? Guid.NewGuid(),
            EquipoId: equipoId,
            ProyectoId: proyectoId,
            RutinaMonitoreoId: rutinaMonitoreoId,
            IniciadaPor: "ana.gomez",
            Ubicacion: new UbicacionGps(
                Latitud: 4.711m, Longitud: -74.072m,
                PrecisionMetros: 8.5m, CapturadoEn: Ahora),
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            Capabilities: new[] { "ejecutar-inspeccion" });

    private static ClaimsTecnico Claims(int proyectoId = 3) =>
        new(TecnicoIniciador: "ana.gomez",
            ProyectosAsignados: new HashSet<int> { proyectoId },
            TieneCapabilityEjecutarInspeccion: true);

    /// <summary>
    /// Siembra <c>EquipoLocal</c> + <c>RutinaMonitoreoLocal</c> en una sesión Marten
    /// para los escenarios que requieren catálogo poblado.
    /// </summary>
    private static async Task SembrarCatalogoMonitoreo(
        IDocumentStore store,
        int equipoId = 4521,
        int proyectoId = 3,
        int grupoMantenimientoId = 7,
        int rutinaMonitoreoId = 42,
        bool poblarRutina = true,
        bool rutinaTieneItemsActivos = true,
        int? grupoRutina = null)
    {
        await using var session = store.LightweightSession();

        var equipo = new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: proyectoId,
            RutinaTecnicaId: null,
            GrupoMantenimientoId: grupoMantenimientoId);
        session.Store(equipo);

        if (poblarRutina)
        {
            var items = rutinaTieneItemsActivos
                ? new List<ItemRutinaMonitoreoLocal>
                  {
                      new(ItemId: 1, Parte: "Batería",    Actividad: "Medir voltaje",  Orden: 1, Activo: true,  Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m)),
                      new(ItemId: 2, Parte: "Conectores", Actividad: "Estado visual",  Orden: 2, Activo: true,  Evaluacion: new EvaluacionCualitativaEsperada()),
                  }
                : new List<ItemRutinaMonitoreoLocal>
                  {
                      new(ItemId: 1, Parte: "Batería", Actividad: "Medir voltaje", Orden: 1, Activo: false, Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m)),
                  };

            var rutina = new RutinaMonitoreoLocal(
                RutinaMonitoreoId: rutinaMonitoreoId,
                Nombre: "Sistema eléctrico",
                GrupoMantenimientoId: grupoRutina ?? grupoMantenimientoId,
                GrupoMantenimiento: "BULLDOZER",
                Items: items,
                SincronizadoEn: Ahora.AddDays(-1));
            session.Store(rutina);
        }

        await session.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.1 happy path — inicio de inspección de monitoreo (integración)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_happy_path_evento_y_proyeccion_persisten_atomicos_seccion_6_1()
    {
        // Given: catálogo poblado para un equipo limpio
        const int equipoId = 14521;
        await SembrarCatalogoMonitoreo(postgres.Store, equipoId: equipoId);

        // When: ejecutar handler happy path
        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId);
        var resultado = await EjecutarHandler(session, cmd);

        // Then: handler devuelve resultado correcto y el evento se persiste.
        resultado.RedirigeAExistente.Should().BeFalse();
        resultado.InspeccionId.Should().Be(cmd.InspeccionId);

        await using var verificacion = postgres.Store.QuerySession();

        // (a) Evento en mt_events con Tipo=Monitoreo y snapshot
        var eventos = await verificacion.Events.FetchStreamAsync(cmd.InspeccionId);
        var iniciada = eventos.Select(e => e.Data)
            .OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle().Subject;

        iniciada.Tipo.Should().Be(TipoInspeccion.Monitoreo);
        iniciada.RutinaMonitoreoSeleccionadaId.Should().Be(42);
        iniciada.ItemsSnapshot.Should().NotBeNull().And.HaveCountGreaterThan(0);

        // (b) Fila en InspeccionAbiertaPorEquipoView con EquipoId como PK
        var fila = await verificacion.LoadAsync<InspeccionAbiertaPorEquipoView>(equipoId);
        fila.Should().NotBeNull("la proyección inline corre en la misma transacción");
        fila!.InspeccionId.Should().Be(cmd.InspeccionId);
        fila.EquipoId.Should().Be(equipoId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 I-I1 — equipo con inspección activa redirige a existente
    // (aplica sin distinción de tipo — decisión D5)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_equipo_con_activa_retorna_existente_I_I1_seccion_6_2()
    {
        // Given: catálogo poblado y una inspección ya activa para el equipo
        const int equipoId = 24521;
        await SembrarCatalogoMonitoreo(postgres.Store, equipoId: equipoId);

        await using var sessionInicial = postgres.Store.LightweightSession();
        var primero = await EjecutarHandler(sessionInicial, Comando(equipoId: equipoId));
        primero.RedirigeAExistente.Should().BeFalse("la primera inspección de monitoreo del equipo debe ser nueva");

        // When: segundo comando sobre el mismo equipo con InspeccionId distinto
        await using var session = postgres.Store.LightweightSession();
        var segundo = await EjecutarHandler(session, Comando(equipoId: equipoId));

        // Then: I-I1 corto-circuita y devuelve el InspeccionId del primero
        segundo.RedirigeAExistente.Should().BeTrue("I-I1 — un equipo solo puede tener una inspección activa");
        segundo.InspeccionId.Should().Be(primero.InspeccionId);
        segundo.Mensaje.Should().Contain("activa");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 I-I1 race condition — dos comandos concurrentes, uno gana
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dos_IniciarMonitoreo_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1_seccion_6_3()
    {
        // Given: catálogo poblado, ambos handlers ven proyección stale (sin fila)
        const int equipoId = 34521;
        await SembrarCatalogoMonitoreo(postgres.Store, equipoId: equipoId);

        var cmdA = Comando(equipoId: equipoId);
        var cmdB = Comando(equipoId: equipoId);

        // When: dos invocaciones paralelas contra el mismo Postgres
        await using var sessionA = postgres.Store.LightweightSession();
        await using var sessionB = postgres.Store.LightweightSession();

        var taskA = Task.Run(() => EjecutarHandler(sessionA, cmdA));
        var taskB = Task.Run(() => EjecutarHandler(sessionB, cmdB));
        var resultados = await Task.WhenAll(taskA, taskB);

        // Then: exactamente un InspeccionIniciada_v1 persistido; el otro redirige.
        resultados.Should().Contain(r => !r.RedirigeAExistente, "uno de los handlers gana la carrera");
        resultados.Should().Contain(r => r.RedirigeAExistente, "el otro pierde el unique violation y reintenta");

        var ganador = resultados.Single(r => !r.RedirigeAExistente);
        var perdedor = resultados.Single(r => r.RedirigeAExistente);
        perdedor.InspeccionId.Should().Be(ganador.InspeccionId, "el perdedor debe redirigir al stream del ganador");

        await using var verificacion = postgres.Store.QuerySession();
        var eventosGanador = await verificacion.Events.FetchStreamAsync(ganador.InspeccionId);
        eventosGanador.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("la defensa dura del índice único Postgres impide doble persistencia");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 PRE-3 — equipo no encontrado en catálogo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_con_equipo_no_sincronizado_lanza_EquipoNoEncontrado_PRE_3_seccion_6_5()
    {
        // Given: catálogo vacío para EquipoId=99001
        const int equipoIdInexistente = 99001;
        await using var session = postgres.Store.LightweightSession();

        // When: handler con cmd referenciando equipo inexistente
        var cmd = Comando(equipoId: equipoIdInexistente);

        // Then: lanza EquipoNoEncontradoException con mensaje que identifica al equipo
        var act = async () => await EjecutarHandler(session, cmd);
        (await act.Should().ThrowAsync<EquipoNoEncontradoException>())
            .WithMessage($"*{equipoIdInexistente}*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 PRE-4 — rutina de monitoreo no sincronizada (I-I-Mon-0)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_con_rutina_no_sincronizada_lanza_RutinaMonitoreoNoSincronizada_PRE_4_seccion_6_6()
    {
        // Given: equipo poblado pero rutina ausente
        const int equipoId = 44521;
        await SembrarCatalogoMonitoreo(postgres.Store, equipoId: equipoId, poblarRutina: false);

        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId, rutinaMonitoreoId: 42);

        // When/Then
        var act = async () => await EjecutarHandler(session, cmd);
        (await act.Should().ThrowAsync<RutinaMonitoreoNoSincronizadaException>())
            .WithMessage("*42*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 PRE-5 — rutina no pertenece al grupo del equipo (I-I-Mon-2)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_con_rutina_de_grupo_distinto_lanza_RutinaNoAplicableAlGrupo_PRE_5_seccion_6_7()
    {
        // Given: EquipoLocal[4521].GrupoMantenimientoId=7; RutinaMonitoreoLocal[99].GrupoMantenimientoId=12
        const int equipoId = 54521;
        const int rutinaGrupoCamioneta = 99;
        await SembrarCatalogoMonitoreo(
            postgres.Store,
            equipoId: equipoId,
            grupoMantenimientoId: 7,         // equipo = BULLDOZER
            rutinaMonitoreoId: rutinaGrupoCamioneta,
            grupoRutina: 12);                // rutina = CAMIONETA — grupo distinto

        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId, rutinaMonitoreoId: rutinaGrupoCamioneta);

        // When/Then
        var act = async () => await EjecutarHandler(session, cmd);
        (await act.Should().ThrowAsync<RutinaNoAplicableAlGrupoException>())
            .WithMessage("*12*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 PRE-6 — rutina sin items activos (I-I-Mon-1)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_con_rutina_sin_items_activos_lanza_EquipoSinRutinasMonitoreo_PRE_6_seccion_6_8()
    {
        // Given: equipo y rutina del mismo grupo; rutina con todos los items Activo=false
        const int equipoId = 64521;
        await SembrarCatalogoMonitoreo(
            postgres.Store,
            equipoId: equipoId,
            rutinaTieneItemsActivos: false);

        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId);

        // When/Then
        var act = async () => await EjecutarHandler(session, cmd);
        (await act.Should().ThrowAsync<EquipoSinRutinasMonitoreoException>())
            .WithMessage("*activos*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.12 Snapshot solo incluye items activos (verificación integración)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_snapshot_excluye_items_inactivos_en_evento_persistido_seccion_6_12()
    {
        // Given: rutina con 3 items: ItemId=1 (Activo=true), ItemId=2 (Activo=false), ItemId=3 (Activo=true)
        const int equipoId = 74521;
        await using var sessionSetup = postgres.Store.LightweightSession();

        var equipo = new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
            RutinaTecnicaId: null,
            GrupoMantenimientoId: 7);
        sessionSetup.Store(equipo);

        var rutina = new RutinaMonitoreoLocal(
            RutinaMonitoreoId: 42,
            Nombre: "Sistema eléctrico",
            GrupoMantenimientoId: 7,
            GrupoMantenimiento: "BULLDOZER",
            Items: new List<ItemRutinaMonitoreoLocal>
            {
                new(ItemId: 1, Parte: "Batería",    Actividad: "Medir voltaje",  Orden: 1, Activo: true,  Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m)),
                new(ItemId: 2, Parte: "Obsoleto",   Actividad: "Sin uso",         Orden: 2, Activo: false, Evaluacion: new EvaluacionCualitativaEsperada()),
                new(ItemId: 3, Parte: "Alternador", Actividad: "Estado general", Orden: 3, Activo: true,  Evaluacion: new EvaluacionCualitativaEsperada()),
            },
            SincronizadoEn: Ahora.AddDays(-1));
        sessionSetup.Store(rutina);
        await sessionSetup.SaveChangesAsync();

        // When: ejecutar handler
        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId);
        var resultado = await EjecutarHandler(session, cmd);

        // Then: evento persistido solo tiene ItemId=1 e ItemId=3
        await using var verificacion = postgres.Store.QuerySession();
        var eventos = await verificacion.Events.FetchStreamAsync(cmd.InspeccionId);
        var iniciada = eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>().Single();

        iniciada.ItemsSnapshot.Should().NotBeNull();
        iniciada.ItemsSnapshot!.Should().HaveCount(2, "solo los items Activo=true se incluyen en el snapshot");
        iniciada.ItemsSnapshot.Select(i => i.ItemId).Should().BeEquivalentTo(new[] { 1, 3 },
            "ItemId=2 fue marcado Activo=false y no debe aparecer en el snapshot — spec §6.12 / decisión D4");
        iniciada.ItemsSnapshot.Should().NotContain(i => i.ItemId == 2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.13 Atomicidad evento + proyección + envelope
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarMonitoreo_evento_y_proyeccion_son_atomicos_seccion_6_13()
    {
        // Given: mismo contexto que 6.1 pero verificamos que ambas escrituras son atómicas.
        const int equipoId = 84521;
        await SembrarCatalogoMonitoreo(postgres.Store, equipoId: equipoId);

        // When: ejecutar handler con un único SaveChangesAsync
        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId);
        await EjecutarHandler(session, cmd);

        // Then: las dos escrituras existen o no existen juntas.
        // Si la atomicidad estuviera rota, podrías ver evento sin proyección o viceversa.
        await using var verificacion = postgres.Store.QuerySession();

        var stream = await verificacion.Events.FetchStreamAsync(cmd.InspeccionId);
        stream.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("el handler debe appendear exactamente un evento");

        var fila = await verificacion.LoadAsync<InspeccionAbiertaPorEquipoView>(equipoId);
        fila.Should().NotBeNull(
            "la proyección inline debe correr en la misma transacción que el Append — atomicidad §6.13");
        fila!.InspeccionId.Should().Be(cmd.InspeccionId,
            "el InspeccionId de la proyección debe coincidir con el del stream del evento");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IniciarInspeccionMonitoreoResult> EjecutarHandler(
        IDocumentSession session,
        IniciarInspeccionMonitoreo cmd)
    {
        var time = new FakeTimeProvider(Ahora);
        var handler = new IniciarInspeccionMonitoreoHandler(session, time);
        return await handler.ManejarAsync(cmd, Claims());
    }
}
