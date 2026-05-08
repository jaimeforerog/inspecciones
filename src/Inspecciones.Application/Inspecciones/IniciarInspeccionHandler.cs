using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <c>IniciarInspeccion</c>. Orquesta:
/// <list type="number">
///   <item>I-I1 validación blanda — lee <c>InspeccionAbiertaPorEquipoView</c>; corto-circuito si activa.</item>
///   <item>PRE-3 — resuelve <c>EquipoLocal</c>; lanza <see cref="EquipoNoEncontradoException"/> si falta.</item>
///   <item>PRE-handler-1 — resuelve <c>RutinaTecnicaLocal</c>; lanza <see cref="RutinaTecnicaNoSincronizadaException"/> si falta.</item>
///   <item>Delega validaciones PRE-4..PRE-7 + I-I2..I-I3 al método <see cref="Inspeccion.Iniciar"/>.</item>
///   <item>Append al stream + Insert del read model + commit atómico (un único <c>SaveChangesAsync</c>).</item>
///   <item>Race condition (I-I1 dura): atrapa <c>MartenCommandException(23505)</c>, relee proyección, retorna <c>RedirigeAExistente=true</c>.</item>
/// </list>
/// </summary>
public sealed class IniciarInspeccionHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    private const string MensajeActiva = "Ya hay inspección activa, abriendo la existente";

    /// <summary>Ejecuta el comando y devuelve el resultado.</summary>
    public async Task<IniciarInspeccionResult> ManejarAsync(
        IniciarInspeccion cmd,
        ClaimsTecnico claims,
        CancellationToken ct = default)
    {
        // I-I1 — validación blanda: corto-circuito si el equipo ya tiene inspección activa.
        var activa = await _session.LoadAsync<InspeccionAbiertaPorEquipoView>(cmd.EquipoId, ct);
        if (activa is not null)
        {
            return new IniciarInspeccionResult(
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

        // PRE-handler-1 — la rutina referenciada por el equipo debe estar sincronizada.
        RutinaTecnicaLocal? rutina = null;
        if (equipo.RutinaTecnicaId is not null)
        {
            rutina = await _session.LoadAsync<RutinaTecnicaLocal>(equipo.RutinaTecnicaId.Value, ct);
        }

        if (rutina is null)
        {
            throw new RutinaTecnicaNoSincronizadaException(
                "Rutina técnica referenciada por el equipo no está sincronizada — refresca catálogos.");
        }

        // Delegar PRE-2, PRE-4..PRE-7, I-I2, I-I3 al método de decisión del aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = Inspeccion.Iniciar(cmd, claims, equipo, rutina, ahora);

        // Append al stream en un único SaveChangesAsync (regla CLAUDE.md atomicidad).
        // FU-13: el view InspeccionAbiertaPorEquipoView ya no se inserta aquí manualmente —
        // lo hace la proyección InspeccionAbiertaPorEquipoProjection (EventProjection inline)
        // en la misma transacción al recibir InspeccionIniciada_v1.
        _session.Events.StartStream<Inspeccion>(cmd.InspeccionId, eventos);

        try
        {
            await _session.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Es23505(ex))
        {
            // I-I1 defensa dura: el equipo ya tenía inspección activa (race ganada por otro handler).
            // Marten 7 puede envolver el 23505 como MartenCommandException o DocumentAlreadyExistsException
            // dependiendo de la operación (Insert en el projection vs Insert directo). Se atrapa ambas.
            // Relee la proyección para obtener el InspeccionId del ganador.
            await using var lecturaRace = _session.DocumentStore.QuerySession();
            var activaRace = await lecturaRace.LoadAsync<InspeccionAbiertaPorEquipoView>(cmd.EquipoId, ct);
            return new IniciarInspeccionResult(
                InspeccionId: activaRace?.InspeccionId ?? cmd.InspeccionId,
                RedirigeAExistente: true,
                Version: 1,
                Mensaje: MensajeActiva);
        }

        return new IniciarInspeccionResult(
            InspeccionId: cmd.InspeccionId,
            RedirigeAExistente: false,
            Version: 1,
            Mensaje: null);
    }

    /// <summary>
    /// Detecta si la excepción es un 23505 (unique violation) en cualquiera de las formas
    /// que Marten 7 puede lanzarla: <see cref="MartenCommandException"/> con inner
    /// <see cref="PostgresException"/> SqlState=23505, o <see cref="DocumentAlreadyExistsException"/>
    /// (Marten la lanza cuando la proyección EventProjection usa Insert y el documento ya existe).
    /// </summary>
    private static bool Es23505(Exception ex) =>
        ex is DocumentAlreadyExistsException ||
        (ex is MartenCommandException mce &&
         mce.InnerException is PostgresException pg &&
         pg.SqlState == "23505");
}
