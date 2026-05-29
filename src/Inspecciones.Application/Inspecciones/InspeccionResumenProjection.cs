using Inspecciones.Domain.Inspecciones;
using Marten;
using Marten.Events.Aggregation;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Proyección de stream único de <see cref="InspeccionResumenView"/> — una fila por
/// inspección, historia completa para el listado por equipo. Materializa el
/// <c>EquipoId</c> desde el evento inicial y mantiene estado/timestamps/dictamen/conteo
/// de hallazgos a partir de los eventos de ciclo de vida.
///
/// <para><b>Robustez ante streams sin fila previa (fix bug 500).</b>
/// Una aggregation inline, al recibir un evento de ciclo de vida sobre un stream cuya
/// fila no existe (inspecciones <c>InspeccionIniciada_v1</c> anteriores al registro de
/// esta proyección), normalmente invoca <c>CreateDefault</c> y revienta el
/// <c>SaveChanges</c> con 500 + rollback (no hay <c>Create</c> para esos eventos ni ctor
/// por defecto del record). Para evitarlo, además del <see cref="Apply"/> incremental
/// hay un <c>Create</c> por evento terminal/de detalle que <b>reconstruye la fila base
/// desde el inicio del stream</b> (<c>AggregateStreamAsync&lt;Inspeccion&gt;</c>) y aplica
/// el delta del evento. Ese <c>Create</c> solo se dispara cuando el snapshot es null y el
/// evento es el primero del batch — justo el caso del stream no proyectado; el flujo
/// normal (con fila existente, o batch que empieza por <c>InspeccionIniciada_v1</c>) usa
/// <c>Apply</c>.</para>
///
/// <para><b>Backfill.</b> El autocurado cubre la recurrencia, pero para que las inspecciones
/// viejas sean <i>visibles</i> en el listado sin esperar a que reciban un evento, hay que
/// correr un rebuild una vez (endpoint <c>POST /api/v1/admin/proyecciones/inspeccion-resumen/rebuild</c>
/// o el CLI Oakton <c>projections</c>). <b>Regla operativa:</b> al agregar o cambiar
/// cualquier proyección sobre el event store existente, correr un rebuild.</para>
/// </summary>
public sealed class InspeccionResumenProjection : SingleStreamProjection<InspeccionResumenView>
{
    // ── Creación normal (stream nuevo: el batch empieza por InspeccionIniciada_v1) ──
    public static InspeccionResumenView Create(InspeccionIniciada_v1 e) => new(
        Id: e.InspeccionId,
        EquipoId: e.EquipoId,
        Tipo: e.Tipo,
        Estado: EstadoInspeccion.EnEjecucion,
        RutinaId: e.RutinaId,
        RutinaCodigo: e.RutinaCodigo,
        TecnicoIniciador: e.TecnicoIniciador,
        ProyectoId: e.ProyectoId,
        FechaReportada: e.FechaReportada,
        IniciadaEn: e.IniciadaEn,
        FirmadaEn: null,
        CanceladaEn: null,
        CerradaEn: null,
        Dictamen: null,
        OTSolicitada: false,
        OTRechazada: false,
        MotivoCancelacion: null,
        HallazgosActivos: new Dictionary<Guid, AccionRequerida>());

    // ── Apply incremental (fila ya existe; flujo normal post-backfill) ──────────
    public static InspeccionResumenView Apply(DictamenEstablecido_v1 e, InspeccionResumenView v) => ConDictamen(v, e);
    public static InspeccionResumenView Apply(InspeccionFirmada_v1 e, InspeccionResumenView v) => ConFirma(v, e);
    public static InspeccionResumenView Apply(InspeccionCancelada_v1 e, InspeccionResumenView v) => ConCancelacion(v, e);
    public static InspeccionResumenView Apply(InspeccionCerradaSinOT_v1 e, InspeccionResumenView v) => ConCierre(v, e);
    public static InspeccionResumenView Apply(OTSolicitada_v1 e, InspeccionResumenView v) => ConOTSolicitada(v);
    public static InspeccionResumenView Apply(GeneracionOTRechazada_v1 e, InspeccionResumenView v) => ConOTRechazada(v);
    public static InspeccionResumenView Apply(HallazgoRegistrado_v1 e, InspeccionResumenView v) => ConHallazgoRegistrado(v, e);
    public static InspeccionResumenView Apply(HallazgoActualizado_v1 e, InspeccionResumenView v) => ConHallazgoActualizado(v, e);
    public static InspeccionResumenView Apply(HallazgoEliminado_v1 e, InspeccionResumenView v) => ConHallazgoEliminado(v, e);

    // ── Create-desde-stream (snapshot null + evento de ciclo de vida primero del
    //    batch ⇒ stream anterior al registro de la proyección). Reconstruye la base
    //    desde el stream committed y aplica el delta del evento en curso. ──────────
    public static async Task<InspeccionResumenView> Create(DictamenEstablecido_v1 e, IQuerySession s)
        => ConDictamen(await BaseDesdeStream(e.InspeccionId, s), e);
    public static async Task<InspeccionResumenView> Create(InspeccionFirmada_v1 e, IQuerySession s)
        => ConFirma(await BaseDesdeStream(e.InspeccionId, s), e);
    public static async Task<InspeccionResumenView> Create(InspeccionCancelada_v1 e, IQuerySession s)
        => ConCancelacion(await BaseDesdeStream(e.InspeccionId, s), e);
    public static async Task<InspeccionResumenView> Create(InspeccionCerradaSinOT_v1 e, IQuerySession s)
        => ConCierre(await BaseDesdeStream(e.InspeccionId, s), e);
    public static async Task<InspeccionResumenView> Create(OTSolicitada_v1 e, IQuerySession s)
        => ConOTSolicitada(await BaseDesdeStream(e.InspeccionId, s));
    public static async Task<InspeccionResumenView> Create(GeneracionOTRechazada_v1 e, IQuerySession s)
        => ConOTRechazada(await BaseDesdeStream(e.InspeccionId, s));
    public static async Task<InspeccionResumenView> Create(HallazgoRegistrado_v1 e, IQuerySession s)
        => ConHallazgoRegistrado(await BaseDesdeStream(e.InspeccionId, s), e);
    public static async Task<InspeccionResumenView> Create(HallazgoActualizado_v1 e, IQuerySession s)
        => ConHallazgoActualizado(await BaseDesdeStream(e.InspeccionId, s), e);
    public static async Task<InspeccionResumenView> Create(HallazgoEliminado_v1 e, IQuerySession s)
        => ConHallazgoEliminado(await BaseDesdeStream(e.InspeccionId, s), e);

    // ── Deltas (compartidos entre Apply y Create-desde-stream) ──────────────────
    private static InspeccionResumenView ConDictamen(InspeccionResumenView v, DictamenEstablecido_v1 e)
        => v with { Dictamen = e.Dictamen };
    private static InspeccionResumenView ConFirma(InspeccionResumenView v, InspeccionFirmada_v1 e)
        => v with { Estado = EstadoInspeccion.Firmada, FirmadaEn = e.FirmadaEn };
    private static InspeccionResumenView ConCancelacion(InspeccionResumenView v, InspeccionCancelada_v1 e)
        => v with { Estado = EstadoInspeccion.Cancelada, CanceladaEn = e.CanceladaEn, MotivoCancelacion = e.Motivo };
    private static InspeccionResumenView ConCierre(InspeccionResumenView v, InspeccionCerradaSinOT_v1 e)
        => v with { Estado = EstadoInspeccion.CerradaSinOT, CerradaEn = e.CerradaEn };
    private static InspeccionResumenView ConOTSolicitada(InspeccionResumenView v)
        => v with { OTSolicitada = true };
    private static InspeccionResumenView ConOTRechazada(InspeccionResumenView v)
        => v with { OTRechazada = true };
    private static InspeccionResumenView ConHallazgoRegistrado(InspeccionResumenView v, HallazgoRegistrado_v1 e)
        => v with { HallazgosActivos = ConMutacion(v.HallazgosActivos, m => m[e.HallazgoId] = e.AccionRequerida) };
    private static InspeccionResumenView ConHallazgoActualizado(InspeccionResumenView v, HallazgoActualizado_v1 e)
        => v.HallazgosActivos.ContainsKey(e.HallazgoId)
            ? v with { HallazgosActivos = ConMutacion(v.HallazgosActivos, m => m[e.HallazgoId] = e.AccionRequerida) }
            : v;
    private static InspeccionResumenView ConHallazgoEliminado(InspeccionResumenView v, HallazgoEliminado_v1 e)
        => v with { HallazgosActivos = ConMutacion(v.HallazgosActivos, m => m.Remove(e.HallazgoId)) };

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reconstruye la fila base reproyectando el agregado desde el inicio del stream
    /// (eventos committed). Usado solo por los <c>Create</c>-desde-stream: el evento en
    /// curso aún no está committed, así que el agregado refleja el estado previo y el
    /// delta se aplica encima en el método que llama.
    /// </summary>
    private static async Task<InspeccionResumenView> BaseDesdeStream(Guid inspeccionId, IQuerySession s)
    {
        var a = await s.Events.AggregateStreamAsync<Inspeccion>(inspeccionId);
        if (a is null)
        {
            // Defensivo: no debería ocurrir (si hay un evento de ciclo de vida, el stream existe).
            throw new InvalidOperationException(
                $"No se pudo reconstruir el stream {inspeccionId} para InspeccionResumenView.");
        }

        return new InspeccionResumenView(
            Id: a.InspeccionId,
            EquipoId: a.EquipoId,
            Tipo: a.Tipo,
            Estado: a.Estado,
            RutinaId: a.RutinaId,
            RutinaCodigo: a.RutinaCodigo,
            TecnicoIniciador: a.TecnicoIniciador,
            ProyectoId: a.ProyectoId,
            FechaReportada: a.FechaReportada,
            IniciadaEn: a.IniciadaEn,
            FirmadaEn: a.FirmadaEn,
            CanceladaEn: null,
            CerradaEn: null,
            Dictamen: a.Dictamen,
            OTSolicitada: a.OTSolicitada,
            OTRechazada: a.OTRechazada,
            MotivoCancelacion: a.MotivoCancelacion,
            HallazgosActivos: a.Hallazgos
                .Where(h => !h.Eliminado)
                .ToDictionary(h => h.HallazgoId, h => h.AccionRequerida));
    }

    /// <summary>Copia inmutable del mapa con la mutación aplicada (preserva el patrón record/with).</summary>
    private static Dictionary<Guid, AccionRequerida> ConMutacion(
        IReadOnlyDictionary<Guid, AccionRequerida> origen,
        Action<Dictionary<Guid, AccionRequerida>> mutacion)
    {
        var copia = origen is null
            ? new Dictionary<Guid, AccionRequerida>()
            : new Dictionary<Guid, AccionRequerida>(origen);
        mutacion(copia);
        return copia;
    }
}
