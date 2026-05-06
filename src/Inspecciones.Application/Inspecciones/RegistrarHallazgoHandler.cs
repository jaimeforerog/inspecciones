using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="RegistrarHallazgo"/>. Orquesta:
/// <list type="number">
///   <item>PRE-2 — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>PRE-4 / INV-PartePerteneceAlEquipo — valida <c>ParteEquipoId</c> contra <c>EquipoLocal.Partes</c>.</item>
///   <item>Delega PRE-3, PRE-5..PRE-10 al método de decisión del aggregate.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// </summary>
public sealed class RegistrarHallazgoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste el evento emitido.</summary>
    public async Task<RegistrarHallazgoResult> ManejarAsync(
        RegistrarHallazgo cmd,
        CancellationToken ct = default)
    {
        // PRE-2: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"Inspección {cmd.InspeccionId} no encontrada.");
        }

        // PRE-4 / INV-PartePerteneceAlEquipo: la parte debe pertenecer al equipo de la inspección.
        var equipo = await _session.LoadAsync<EquipoLocal>(inspeccion.EquipoId, ct);
        var partesDelEquipo = equipo?.Partes ?? [];
        if (!partesDelEquipo.Any(p => p.ParteEquipoId == cmd.ParteEquipoId))
        {
            throw new ParteNoCorrespondeAlEquipoException(
                $"La parte {cmd.ParteEquipoId} no pertenece al equipo {inspeccion.EquipoId}. " +
                "Selecciona una parte válida de este equipo.");
        }

        // Delegar PRE-3, PRE-5..PRE-10 al método de decisión del aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.RegistrarHallazgo(cmd, ahora);

        // Append al stream — un único SaveChangesAsync (regla CLAUDE.md atomicidad).
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        var evento = (HallazgoRegistrado_v1)eventos[0];
        return new RegistrarHallazgoResult(
            HallazgoId: evento.HallazgoId,
            InspeccionId: evento.InspeccionId,
            AccionRequerida: evento.AccionRequerida,
            RegistradoEn: evento.RegistradoEn);
    }
}

/// <summary>Resultado de ejecutar <see cref="RegistrarHallazgo"/>.</summary>
public sealed record RegistrarHallazgoResult(
    Guid HallazgoId,
    Guid InspeccionId,
    AccionRequerida AccionRequerida,
    DateTimeOffset RegistradoEn);
