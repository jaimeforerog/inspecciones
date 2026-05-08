using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <c>IniciarInspeccionMonitoreo</c>. Orquesta:
/// <list type="number">
///   <item>PRE-7 (I-I1 blanda) — lee <c>InspeccionAbiertaPorEquipoView</c>; corto-circuito si activa.</item>
///   <item>PRE-3 — resuelve <c>EquipoLocal</c>; lanza <see cref="EquipoNoEncontradoException"/> si falta.</item>
///   <item>PRE-4 — resuelve <c>RutinaMonitoreoLocal</c>; lanza <see cref="RutinaMonitoreoNoSincronizadaException"/> si falta.</item>
///   <item>PRE-5 (I-I-Mon-2) — valida que la rutina pertenece al grupo del equipo; lanza <see cref="RutinaNoAplicableAlGrupoException"/>.</item>
///   <item>PRE-6 (I-I-Mon-1) — valida items activos ≥1; lanza <see cref="EquipoSinRutinasMonitoreoException"/>.</item>
///   <item>Construye snapshot de items activos ordenados por Orden.</item>
///   <item>Delega PRE-8, PRE-9 al método de decisión <see cref="Domain.Inspecciones.Inspeccion.IniciarMonitoreo"/>.</item>
///   <item>Append al stream + proyección + commit atómico (un único <c>SaveChangesAsync</c>).</item>
///   <item>Race condition I-I1 dura: atrapa <c>MartenCommandException(23505)</c>.</item>
/// </list>
/// </summary>
public sealed class IniciarInspeccionMonitoreoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    private const string MensajeActiva = "Ya hay inspección activa, abriendo la existente";

    /// <summary>Ejecuta el comando y devuelve el resultado.</summary>
    public async Task<IniciarInspeccionMonitoreoResult> ManejarAsync(
        IniciarInspeccionMonitoreo cmd,
        ClaimsTecnico claims,
        CancellationToken ct = default)
    {
        // PRE-7 (I-I1 blanda) — corto-circuito si el equipo ya tiene inspección activa.
        var activa = await _session.LoadAsync<InspeccionAbiertaPorEquipoView>(cmd.EquipoId, ct);
        if (activa is not null)
        {
            return new IniciarInspeccionMonitoreoResult(
                InspeccionId: activa.InspeccionId,
                RedirigeAExistente: true,
                Version: 1,
                Mensaje: MensajeActiva);
        }

        // PRE-3 — el equipo debe existir en el catálogo local.
        var equipo = await _session.LoadAsync<EquipoLocal>(cmd.EquipoId, ct);
        if (equipo is null)
        {
            throw new EquipoNoEncontradoException(
                $"Equipo {cmd.EquipoId} no encontrado en catálogo local. Refresca catálogos.");
        }

        // PRE-4 — la rutina de monitoreo debe estar sincronizada.
        var rutina = await _session.LoadAsync<RutinaMonitoreoLocal>(cmd.RutinaMonitoreoId, ct);
        if (rutina is null)
        {
            throw new RutinaMonitoreoNoSincronizadaException(
                $"Rutina de monitoreo {cmd.RutinaMonitoreoId} no sincronizada. Refresca catálogos.");
        }

        // PRE-5 (I-I-Mon-2) — la rutina debe pertenecer al mismo grupo de mantenimiento que el equipo.
        if (rutina.GrupoMantenimientoId != equipo.GrupoMantenimientoId)
        {
            throw new RutinaNoAplicableAlGrupoException(
                $"Rutina {cmd.RutinaMonitoreoId} pertenece al grupo {rutina.GrupoMantenimientoId} ({rutina.GrupoMantenimiento}). " +
                $"El equipo {cmd.EquipoId} pertenece al grupo {equipo.GrupoMantenimientoId}.");
        }

        // PRE-6 (I-I-Mon-1) — la rutina debe tener al menos un item activo.
        var itemsActivos = rutina.Items
            .Where(i => i.Activo)
            .OrderBy(i => i.Orden)
            .Select(i => new ItemRutinaMonitoreoSnapshot(
                ItemId: i.ItemId,
                Parte: i.Parte,
                Actividad: i.Actividad,
                Evaluacion: i.Evaluacion))
            .ToList();

        if (itemsActivos.Count == 0)
        {
            throw new EquipoSinRutinasMonitoreoException(
                $"La rutina de monitoreo {cmd.RutinaMonitoreoId} no tiene items activos.");
        }

        // Delegar PRE-8 y PRE-9 al método de decisión del aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = Inspeccion.IniciarMonitoreo(cmd, claims, rutina.Nombre, itemsActivos, ahora);

        // Append al stream en un único SaveChangesAsync (atomicidad — CLAUDE.md).
        _session.Events.StartStream<Inspeccion>(cmd.InspeccionId, eventos);

        try
        {
            await _session.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Es23505(ex))
        {
            // I-I1 defensa dura: el equipo ya tenía inspección activa (race ganada por otro handler).
            // Marten 7 puede envolver el 23505 como MartenCommandException o DocumentAlreadyExistsException.
            await using var lecturaRace = _session.DocumentStore.QuerySession();
            var activaRace = await lecturaRace.LoadAsync<InspeccionAbiertaPorEquipoView>(cmd.EquipoId, ct);
            return new IniciarInspeccionMonitoreoResult(
                InspeccionId: activaRace?.InspeccionId ?? cmd.InspeccionId,
                RedirigeAExistente: true,
                Version: 1,
                Mensaje: MensajeActiva);
        }

        return new IniciarInspeccionMonitoreoResult(
            InspeccionId: cmd.InspeccionId,
            RedirigeAExistente: false,
            Version: 1,
            Mensaje: null);
    }

    /// <summary>
    /// Detecta si la excepción es un 23505 (unique violation) en cualquiera de las formas
    /// que Marten 7 puede lanzarla. Ver <see cref="IniciarInspeccionHandler.Es23505"/>.
    /// </summary>
    private static bool Es23505(Exception ex) =>
        ex is DocumentAlreadyExistsException ||
        (ex is MartenCommandException mce &&
         mce.InnerException is PostgresException pg &&
         pg.SqlState == "23505");
}
