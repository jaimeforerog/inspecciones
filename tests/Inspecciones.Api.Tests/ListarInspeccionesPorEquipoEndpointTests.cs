using System.Net;
using System.Net.Http.Json;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Api.Tests;

/// <summary>
/// Tests E2E del endpoint <c>GET /api/v1/inspecciones?equipoId={int}</c> — historia
/// completa de inspecciones de un equipo (cualquier estado), más recientes primero.
/// Fuente: proyección inline <c>InspeccionResumenView</c>.
///
/// Cubre:
/// Orden/contenido — 200 OK + todas las inspecciones del equipo ordenadas por
///                   IniciadaEn descendente, con estado terminal correcto.
/// Aislamiento     — no incluye inspecciones de otro equipo.
/// Vacío           — equipo sin inspecciones → 200 OK + total 0.
/// PRE-1           — capability "ejecutar-inspeccion" ausente → 403.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public class ListarInspeccionesPorEquipoEndpointTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset Base =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Siembra una inspección técnica EnEjecucion con IniciadaEn explícito.</summary>
    private async Task<Guid> SembrarEnEjecucion(int equipoId, DateTimeOffset iniciadaEn)
    {
        var inspeccionId = Guid.NewGuid();
        await using var session = factory.OpenSeedingSessionForDefaultTenant();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "1",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, iniciadaEn),
                IniciadaEn: iniciadaEn,
                FechaReportada: DateOnly.FromDateTime(iniciadaEn.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    /// <summary>Siembra una inspección firmada completa (estado terminal Firmada + dictamen).</summary>
    private async Task<Guid> SembrarFirmada(int equipoId, DateTimeOffset iniciadaEn)
    {
        var inspeccionId = Guid.NewGuid();
        var hallazgoId = Guid.NewGuid();
        await using var session = factory.OpenSeedingSessionForDefaultTenant();

        session.Events.StartStream<Inspeccion>(inspeccionId,
            new InspeccionIniciada_v1(
                InspeccionId: inspeccionId,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: equipoId,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "1",
                ProyectoId: 3,
                Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, iniciadaEn),
                IniciadaEn: iniciadaEn,
                FechaReportada: DateOnly.FromDateTime(iniciadaEn.UtcDateTime),
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
                ActividadDescripcion: "Revisión general",
                NovedadTecnica: "Estado satisfactorio",
                AccionRequerida: AccionRequerida.NoRequiereIntervencion,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: null,
                Ubicacion: null,
                EmitidoPor: "1",
                RegistradoEn: iniciadaEn),
            new DiagnosticoEmitido_v1(
                InspeccionId: inspeccionId,
                DiagnosticoFinal: "Inspección completa",
                EmitidoPor: "1",
                EmitidoEn: iniciadaEn),
            new DictamenEstablecido_v1(
                InspeccionId: inspeccionId,
                Dictamen: DictamenOperacion.PuedeOperar,
                Justificacion: "Sin hallazgos críticos",
                EmitidoPor: "1",
                EstablecidoEn: iniciadaEn),
            new InspeccionFirmada_v1(
                InspeccionId: inspeccionId,
                FirmadoPor: "1",
                FirmaUri: "https://blobs/firma-01.png",
                UbicacionFirma: new UbicacionGps(4.711m, -74.072m, 8.5m, iniciadaEn),
                FirmadaEn: iniciadaEn));

        await session.SaveChangesAsync();
        return inspeccionId;
    }

    [Fact]
    public async Task GET_inspecciones_por_equipo_lista_todas_mas_recientes_primero()
    {
        const int equipoId = 90401;
        // Más antigua primero al sembrar; el endpoint debe devolver la más reciente primero.
        var vieja = await SembrarFirmada(equipoId, Base.AddDays(-5));
        var nueva = await SembrarEnEjecucion(equipoId, Base.AddDays(-1));

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/inspecciones?equipoId={equipoId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RespuestaLista>();
        body.Should().NotBeNull();
        body!.EquipoId.Should().Be(equipoId);
        body.Total.Should().Be(2);
        body.Inspecciones.Should().HaveCount(2);

        // Orden: más recientes primero (IniciadaEn desc).
        body.Inspecciones[0].InspeccionId.Should().Be(nueva);
        body.Inspecciones[0].Estado.Should().Be("EnEjecucion");
        body.Inspecciones[1].InspeccionId.Should().Be(vieja);
        body.Inspecciones[1].Estado.Should().Be("Firmada");
        body.Inspecciones[1].Dictamen.Should().Be("PuedeOperar");
        body.Inspecciones[1].FirmadaEn.Should().NotBeNull();
        // Gap #2 — la firmada tiene 1 hallazgo NoRequiereIntervencion.
        body.Inspecciones[1].Hallazgos.Total.Should().Be(1);
        body.Inspecciones[1].Hallazgos.SinIntervencion.Should().Be(1);
    }

    // ── Gap #3 — motivoCancelación ───────────────────────────────────────────

    [Fact]
    public async Task GET_inspecciones_por_equipo_expone_motivo_cancelacion()
    {
        const int equipoId = 90405;
        const string motivo = "Equipo trasladado a otra obra sin previo aviso";
        var inspeccionId = Guid.NewGuid();

        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
            session.Events.StartStream<Inspeccion>(inspeccionId,
                new InspeccionIniciada_v1(
                    InspeccionId: inspeccionId, Tipo: TipoInspeccion.Tecnica, EquipoId: equipoId,
                    RutinaId: 18, RutinaCodigo: "INSP. BULL.MOTOR", TecnicoIniciador: "1", ProyectoId: 3,
                    Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Base), IniciadaEn: Base.AddDays(-1),
                    FechaReportada: DateOnly.FromDateTime(Base.UtcDateTime),
                    LecturaMedidorPrimario: null, LecturaMedidorSecundario: null),
                new InspeccionCancelada_v1(
                    InspeccionId: inspeccionId, Motivo: motivo, CanceladaPor: "1", CanceladaEn: Base));
            await session.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var body = await client.GetFromJsonAsync<RespuestaLista>($"/api/v1/inspecciones?equipoId={equipoId}");

        var fila = body!.Inspecciones.Single(i => i.InspeccionId == inspeccionId);
        fila.Estado.Should().Be("Cancelada");
        fila.MotivoCancelacion.Should().Be(motivo);
        fila.CanceladaEn.Should().NotBeNull();
    }

    // ── Gap #2 — conteo por acción a través de Registrar/Actualizar/Eliminar ──

    [Fact]
    public async Task GET_inspecciones_por_equipo_cuenta_hallazgos_por_accion()
    {
        const int equipoId = 90406;
        var inspeccionId = Guid.NewGuid();
        var hSinIntervencion = Guid.NewGuid();
        var hIntervencion = Guid.NewGuid();
        var hSeguimiento = Guid.NewGuid();

        await using (var session = factory.OpenSeedingSessionForDefaultTenant())
        {
            session.Events.StartStream<Inspeccion>(inspeccionId,
                new InspeccionIniciada_v1(
                    InspeccionId: inspeccionId, Tipo: TipoInspeccion.Tecnica, EquipoId: equipoId,
                    RutinaId: 18, RutinaCodigo: "INSP. BULL.MOTOR", TecnicoIniciador: "1", ProyectoId: 3,
                    Ubicacion: new UbicacionGps(4.711m, -74.072m, 8.5m, Base), IniciadaEn: Base.AddDays(-1),
                    FechaReportada: DateOnly.FromDateTime(Base.UtcDateTime),
                    LecturaMedidorPrimario: null, LecturaMedidorSecundario: null),
                Hallazgo(inspeccionId, hSinIntervencion, AccionRequerida.NoRequiereIntervencion),
                Hallazgo(inspeccionId, hIntervencion, AccionRequerida.RequiereIntervencion),
                Hallazgo(inspeccionId, hSeguimiento, AccionRequerida.RequiereSeguimiento),
                // Eliminar el de NoRequiereIntervencion → debe salir del conteo vigente.
                new HallazgoEliminado_v1(
                    InspeccionId: inspeccionId, HallazgoId: hSinIntervencion,
                    Motivo: "Duplicado", EliminadoPor: "1", EliminadoEn: Base));
            await session.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var body = await client.GetFromJsonAsync<RespuestaLista>($"/api/v1/inspecciones?equipoId={equipoId}");

        var h = body!.Inspecciones.Single(i => i.InspeccionId == inspeccionId).Hallazgos;
        h.Total.Should().Be(2, "el hallazgo eliminado no cuenta como vigente");
        h.RequierenIntervencion.Should().Be(1);
        h.RequierenSeguimiento.Should().Be(1);
        h.SinIntervencion.Should().Be(0);
    }

    private static HallazgoRegistrado_v1 Hallazgo(Guid inspeccionId, Guid hallazgoId, AccionRequerida accion) =>
        new(
            InspeccionId: inspeccionId,
            HallazgoId: hallazgoId,
            Origen: OrigenHallazgo.Manual,
            NovedadPreopOrigenId: null,
            MedicionOrigenId: null,
            EvaluacionOrigenId: null,
            ParteEquipoId: 77,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: "Hallazgo de prueba",
            AccionRequerida: accion,
            AccionCorrectiva: accion == AccionRequerida.RequiereIntervencion ? "Reemplazar" : null,
            TipoFallaId: accion == AccionRequerida.RequiereIntervencion ? 1 : null,
            CausaFallaId: accion == AccionRequerida.RequiereIntervencion ? 1 : null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: "1",
            RegistradoEn: Base);

    [Fact]
    public async Task GET_inspecciones_por_equipo_no_incluye_otro_equipo()
    {
        const int equipoId = 90402;
        const int otroEquipo = 90403;
        var propia = await SembrarEnEjecucion(equipoId, Base.AddDays(-2));
        await SembrarEnEjecucion(otroEquipo, Base.AddDays(-2));

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/inspecciones?equipoId={equipoId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RespuestaLista>();
        body!.Total.Should().Be(1);
        body.Inspecciones.Should().ContainSingle()
            .Which.InspeccionId.Should().Be(propia);
    }

    [Fact]
    public async Task GET_inspecciones_equipo_sin_inspecciones_responde_200_lista_vacia()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/inspecciones?equipoId=90499");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RespuestaLista>();
        body!.Total.Should().Be(0);
        body.Inspecciones.Should().BeEmpty();
    }

    [Fact]
    public async Task GET_inspecciones_por_equipo_sin_capability_responde_403()
    {
        const int equipoId = 90404;
        await SembrarEnEjecucion(equipoId, Base.AddDays(-1));

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/inspecciones?equipoId={equipoId}");
        request.Headers.Add("X-Sin-Capability-Ejecutar", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record RespuestaLista(
        int EquipoId,
        int Total,
        IReadOnlyList<InspeccionResumen> Inspecciones);

    private sealed record InspeccionResumen(
        Guid InspeccionId,
        string Tipo,
        string Estado,
        string RutinaCodigo,
        string TecnicoIniciador,
        int ProyectoId,
        DateTimeOffset IniciadaEn,
        DateTimeOffset? FirmadaEn,
        DateTimeOffset? CanceladaEn,
        string? Dictamen,
        bool OtSolicitada,
        bool OtRechazada,
        string? MotivoCancelacion,
        HallazgosResumen Hallazgos);

    private sealed record HallazgosResumen(
        int Total,
        int RequierenIntervencion,
        int RequierenSeguimiento,
        int SinIntervencion);
}
