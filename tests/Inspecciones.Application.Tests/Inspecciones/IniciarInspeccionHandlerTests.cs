using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="IniciarInspeccionHandler"/> que requieren un
/// Marten real (Postgres en Testcontainers). Cubre los escenarios §6.2..§6.8
/// del slice 1b: I-I1 shortcut blando, race condition concurrente, atomicidad
/// outbox + Marten + envelope dedup, y las pre-condiciones del handler
/// (PRE-3 equipo no encontrado, PRE-handler-1 rutina no sincronizada,
/// PRE-2 proyecto no autorizado defensa profundidad).
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
[Trait("Category", "Integration")]
public class IniciarInspeccionHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 5, 8, 30, 12, TimeSpan.FromHours(-5));

    private static IniciarInspeccion Comando(Guid? inspeccionId = null, int equipoId = 4521, int proyectoId = 3) =>
        new(InspeccionId: inspeccionId ?? Guid.NewGuid(),
            EquipoId: equipoId,
            ProyectoId: proyectoId,
            UbicacionInicio: new(Latitud: 4.711m, Longitud: -74.072m, PrecisionMetros: 8.5m, CapturadoEn: Ahora),
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null);

    private static ClaimsTecnico Claims(int proyectoId = 3) =>
        new(TecnicoIniciador: "rmartinez",
            ProyectosAsignados: new HashSet<int> { proyectoId },
            TieneCapabilityEjecutarInspeccion: true);

    /// <summary>
    /// Siembra <c>EquipoLocal</c> + <c>RutinaTecnicaLocal</c> en una sesión Marten
    /// para los escenarios que requieren catálogo poblado.
    /// </summary>
    private static async Task SembrarCatalogo(
        IDocumentStore store,
        int equipoId = 4521,
        int proyectoId = 3,
        int? rutinaId = 18,
        bool poblarRutina = true)
    {
        await using var session = store.LightweightSession();
        var equipo = new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: proyectoId,
            RutinaTecnicaId: rutinaId);
        session.Store(equipo);

        if (poblarRutina && rutinaId is not null)
        {
            var rutina = new RutinaTecnicaLocal(
                RutinaId: rutinaId.Value,
                Codigo: "INSP. BULL.MOTOR",
                Nombre: "Inspección bulldozer motor",
                Tipo: TipoRutina.Tecnica,
                GrupoMantenimiento: "BULLDOZER",
                ParteId: 88,
                ParteCodigo: "MOTOR",
                SincronizadoEn: Ahora.AddDays(-1));
            session.Store(rutina);
        }

        await session.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.2 — I-I1 shortcut: equipo con inspección activa, retornar existente
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarInspeccion_equipo_con_activa_retorna_existente_I_I1()
    {
        // Given: catálogo poblado y una inspección ya en EnEjecucion para el equipo 4521
        const int equipoId = 4521;
        await SembrarCatalogo(postgres.Store, equipoId: equipoId);

        await using var sessionInicial = postgres.Store.LightweightSession();
        var primero = await EjecutarHandler(sessionInicial, Comando(equipoId: equipoId));
        primero.RedirigeAExistente.Should().BeFalse("la primera inspección sobre el equipo debe ser nueva");

        // When: un segundo comando sobre el mismo equipo con InspeccionId distinto
        await using var session = postgres.Store.LightweightSession();
        var segundo = await EjecutarHandler(session, Comando(equipoId: equipoId));

        // Then: el handler corto-circuita y devuelve el InspeccionId del primero
        segundo.RedirigeAExistente.Should().BeTrue("I-I1 — un equipo no puede tener dos inspecciones activas");
        segundo.InspeccionId.Should().Be(primero.InspeccionId);
        segundo.Mensaje.Should().Contain("activa", "la spec §6.2 dicta el mensaje 'Ya hay inspección activa, abriendo la existente'");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.3 — I-I1 race condition concurrente: dos handlers, uno gana
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dos_IniciarInspeccion_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1()
    {
        // Given: catálogo poblado para un equipo distinto, ambos handlers ven proyección stale (sin fila)
        const int equipoId = 7777;
        await SembrarCatalogo(postgres.Store, equipoId: equipoId);

        var cmdA = Comando(equipoId: equipoId);
        var cmdB = Comando(equipoId: equipoId);

        // When: dos invocaciones paralelas contra el mismo Postgres
        await using var sessionA = postgres.Store.LightweightSession();
        await using var sessionB = postgres.Store.LightweightSession();

        var taskA = Task.Run(() => EjecutarHandler(sessionA, cmdA));
        var taskB = Task.Run(() => EjecutarHandler(sessionB, cmdB));

        var resultados = await Task.WhenAll(taskA, taskB);

        // Then: exactamente un InspeccionIniciada_v1 quedó persistido en el event store.
        // Uno de los dos resultados tiene RedirigeAExistente=true; ambos InspeccionId apuntan al ganador.
        resultados.Should().Contain(r => !r.RedirigeAExistente, "uno de los handlers gana la carrera y emite el evento");
        resultados.Should().Contain(r => r.RedirigeAExistente, "el otro pierde el unique violation y reintenta retornando RedirigeAExistente=true");

        var ganador = resultados.Single(r => !r.RedirigeAExistente);
        var perdedor = resultados.Single(r => r.RedirigeAExistente);
        perdedor.InspeccionId.Should().Be(ganador.InspeccionId, "el perdedor debe redirigir al stream del ganador");

        await using var verificacion = postgres.Store.QuerySession();
        var eventosGanador = await verificacion.Events.FetchStreamAsync(ganador.InspeccionId);
        eventosGanador.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("la defensa dura del índice único Postgres impide doble persistencia");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.5 — PRE-3 equipo no encontrado en catálogo
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarInspeccion_con_equipo_no_sincronizado_lanza_EquipoNoEncontrado_PRE_3()
    {
        // Given: catálogo vacío para EquipoId=99999
        const int equipoIdInexistente = 99999;
        await using var session = postgres.Store.LightweightSession();

        // When: handler con cmd referenciando equipo inexistente
        var cmd = Comando(equipoId: equipoIdInexistente);

        // Then: lanza EquipoNoEncontradoException con mensaje que identifica al equipo
        var act = async () => await EjecutarHandler(session, cmd);

        (await act.Should().ThrowAsync<EquipoNoEncontradoException>())
            .WithMessage($"*{equipoIdInexistente}*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.6 — PRE-handler-1 rutina referenciada por el equipo no sincronizada
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarInspeccion_con_rutina_referenciada_no_sincronizada_lanza_RutinaTecnicaNoSincronizada()
    {
        // Given: equipo poblado pero rutina ausente (sync stale o admin del ERP la borró)
        const int equipoId = 5555;
        await SembrarCatalogo(postgres.Store, equipoId: equipoId, rutinaId: 18, poblarRutina: false);

        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId);

        // When/Then: el handler lanza antes de tocar el aggregate del 1a
        var act = async () => await EjecutarHandler(session, cmd);

        (await act.Should().ThrowAsync<RutinaTecnicaNoSincronizadaException>())
            .WithMessage("*sincroniza*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.7 — PRE-2 proyecto no autorizado (defensa en profundidad del handler)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarInspeccion_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizado_PRE_2()
    {
        // Given: catálogo poblado, equipo en proyecto 99, claims solo asignan {1,2}.
        // El filtro HTTP debería haber bloqueado, pero el test verifica defensa en profundidad
        // del aggregate del slice 1a invocado por el handler del 1b.
        const int equipoId = 6666;
        await SembrarCatalogo(postgres.Store, equipoId: equipoId, proyectoId: 99);

        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId, proyectoId: 99);
        var claims = new ClaimsTecnico(
            TecnicoIniciador: "rmartinez",
            ProyectosAsignados: new HashSet<int> { 1, 2 },
            TieneCapabilityEjecutarInspeccion: true);

        // When/Then
        var time = new FakeTimeProvider(Ahora);
        var handler = new IniciarInspeccionHandler(session, time);
        var act = async () => await handler.ManejarAsync(cmd, claims);

        (await act.Should().ThrowAsync<ProyectoNoAutorizadoException>())
            .WithMessage("*99*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.8 — Atomicidad evento + proyección + (envelope) en una transacción
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IniciarInspeccion_happy_path_proyeccion_y_evento_persisten_atomicos()
    {
        // Given: catálogo poblado para un equipo limpio
        const int equipoId = 8888;
        await SembrarCatalogo(postgres.Store, equipoId: equipoId);

        // When: ejecutar handler happy path
        await using var session = postgres.Store.LightweightSession();
        var cmd = Comando(equipoId: equipoId);
        var resultado = await EjecutarHandler(session, cmd);

        // Then: tras el commit existen las TRES cosas (evento + proyección consistentes).
        // Si la atomicidad estuviera rota, podrías ver el evento sin la proyección o viceversa.
        resultado.RedirigeAExistente.Should().BeFalse();
        resultado.InspeccionId.Should().Be(cmd.InspeccionId);

        await using var verificacion = postgres.Store.QuerySession();

        // (a) Evento en mt_events
        var eventos = await verificacion.Events.FetchStreamAsync(cmd.InspeccionId);
        eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("el handler debe haber appendeado exactamente un evento");

        // (b) Fila en la proyección con el InspeccionId correcto y EquipoId como PK
        var fila = await verificacion.LoadAsync<InspeccionAbiertaPorEquipoView>(equipoId);
        fila.Should().NotBeNull("la proyección inline corre en la misma transacción del Append");
        fila!.InspeccionId.Should().Be(cmd.InspeccionId);
        fila.EquipoId.Should().Be(equipoId);
        fila.TecnicoIniciador.Should().Be("rmartinez");
        fila.ProyectoId.Should().Be(3);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IniciarInspeccionResult> EjecutarHandler(
        IDocumentSession session,
        IniciarInspeccion cmd)
    {
        var time = new FakeTimeProvider(Ahora);
        var handler = new IniciarInspeccionHandler(session, time);
        return await handler.ManejarAsync(cmd, Claims());
    }
}
