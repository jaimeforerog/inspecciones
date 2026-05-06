using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="ActualizarHallazgo"/>. Orquesta:
/// <list type="number">
///   <item>PRE-1 (handler) — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>Delega PRE-2..PRE-7 al método de decisión del aggregate.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// Spec slice 2 §4. No aplica ADR-003/ADR-006 (sin integración ERP), ADR-005 (sin SignalR), ADR-004.
/// </summary>
public sealed class ActualizarHallazgoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste el evento emitido.</summary>
    public async Task<ActualizarHallazgoResult> ManejarAsync(
        ActualizarHallazgo cmd,
        CancellationToken ct = default)
    {
        // PRE-1: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"Inspección {cmd.InspeccionId} no encontrada.");
        }

        // Delegar PRE-2..PRE-7 al método de decisión del aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.ActualizarHallazgo(cmd, ahora);

        // Append al stream — un único SaveChangesAsync (regla CLAUDE.md atomicidad).
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        var evento = (HallazgoActualizado_v1)eventos[0];
        return new ActualizarHallazgoResult(
            HallazgoId: evento.HallazgoId,
            InspeccionId: evento.InspeccionId,
            ActualizadoEn: evento.ActualizadoEn);
    }
}

/// <summary>Resultado de ejecutar <see cref="ActualizarHallazgo"/>.</summary>
public sealed record ActualizarHallazgoResult(
    Guid HallazgoId,
    Guid InspeccionId,
    DateTimeOffset ActualizadoEn);
