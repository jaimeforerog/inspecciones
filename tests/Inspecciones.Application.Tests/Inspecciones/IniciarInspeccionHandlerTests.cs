using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Microsoft.Extensions.Time.Testing;

namespace Inspecciones.Application.Tests.Inspecciones;

/// <summary>
/// Tests del handler <see cref="IniciarInspeccionHandler"/> que requieren un
/// Marten real (Postgres en Testcontainers). Cubre §6.10 (I-I1 shortcut: ya
/// hay activa, redirige) y §6.11 (I-I1 race condition concurrente con índice
/// único parcial Postgres como defensa dura).
/// </summary>
[Collection(nameof(PostgresFixtureCollection))]
public class IniciarInspeccionHandlerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 5, 8, 30, 12, TimeSpan.FromHours(-5));

    private static IniciarInspeccion Comando(Guid? inspeccionId = null, int equipoId = 4521) =>
        new(InspeccionId: inspeccionId ?? Guid.NewGuid(),
            EquipoId: equipoId,
            ProyectoId: 3,
            UbicacionInicio: new(Latitud: 4.711m, Longitud: -74.072m, PrecisionMetros: 8.5m, CapturadoEn: Ahora),
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null);

    private static ClaimsTecnico Claims() =>
        new(TecnicoIniciador: "rmartinez",
            ProyectosAsignados: new HashSet<int> { 3 },
            TieneCapabilityEjecutarInspeccion: true);

    // ─────────────────────────────────────────────────────────────────────
    // §6.10 — I-I1 shortcut: equipo con inspección activa, retornar existente
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Slice 1b — pendiente: requiere IniciarInspeccionHandler implementado y proyección InspeccionAbiertaPorEquipoView con índice único parcial Postgres. Ver slices/1b-iniciar-inspeccion-handler/spec.md.")]
    public async Task IniciarInspeccion_sobre_equipo_con_activa_retorna_existente_I_I1()
    {
        // Given: una inspección ya creada y aún en EnEjecucion para el equipo 4521
        await using var seed = postgres.Store.LightweightSession();
        var primero = await EjecutarHandler(seed, Comando());
        primero.RedirigeAExistente.Should().BeFalse("la primera inspección sobre el equipo debe ser nueva");

        // When: un segundo comando sobre el mismo equipo
        await using var session = postgres.Store.LightweightSession();
        var segundo = await EjecutarHandler(session, Comando());

        // Then: el handler corto-circuita y devuelve el InspeccionId del primero
        segundo.RedirigeAExistente.Should().BeTrue("I-I1 — un equipo no puede tener dos inspecciones activas");
        segundo.InspeccionId.Should().Be(primero.InspeccionId);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §6.11 — I-I1 race condition: dos handlers concurrentes, uno gana
    // ─────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Slice 1b — pendiente: requiere IniciarInspeccionHandler implementado y proyección InspeccionAbiertaPorEquipoView con índice único parcial Postgres. Ver slices/1b-iniciar-inspeccion-handler/spec.md.")]
    public async Task Dos_IniciarInspeccion_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1()
    {
        // Given: ambos handlers ven proyección stale (sin fila aún)
        const int equipoId = 7777;
        var cmdA = Comando(equipoId: equipoId);
        var cmdB = Comando(equipoId: equipoId);

        // When: dos invocaciones paralelas contra el mismo Postgres
        await using var sessionA = postgres.Store.LightweightSession();
        await using var sessionB = postgres.Store.LightweightSession();

        var taskA = Task.Run(() => EjecutarHandler(sessionA, cmdA));
        var taskB = Task.Run(() => EjecutarHandler(sessionB, cmdB));

        var resultados = await Task.WhenAll(taskA, taskB);

        // Then: exactamente un InspeccionIniciada_v1 quedó persistido en el event store
        var inspeccionIdGanadora = resultados.Should().OnlyContain(r => r.InspeccionId != Guid.Empty).And.Subject.First().InspeccionId;
        await using var verificacion = postgres.Store.QuerySession();
        var eventos = await verificacion.Events.FetchStreamAsync(inspeccionIdGanadora);
        eventos.Select(e => e.Data).OfType<InspeccionIniciada_v1>()
            .Should().ContainSingle("la defensa dura del índice único parcial Postgres impide doble persistencia");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IniciarInspeccionResult> EjecutarHandler(
        Marten.IDocumentSession session,
        IniciarInspeccion cmd)
    {
        var time = new FakeTimeProvider(Ahora);
        var handler = new IniciarInspeccionHandler(session, time);
        return await handler.ManejarAsync(cmd, Claims());
    }
}
