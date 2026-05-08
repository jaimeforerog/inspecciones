using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="OmitirItemMonitoreo"/>. Orquesta:
/// <list type="number">
///   <item>PRE-2 — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>Delega PRE-3..PRE-9 al método de decisión <see cref="Inspeccion.OmitirItem"/>.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// La omisión nunca genera hallazgo automático (§12.11.5 punto 6) — handler
/// emite exactamente un evento <see cref="ItemMonitoreoOmitido_v1"/>.
/// </summary>
public sealed class OmitirItemMonitoreoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste el evento emitido.</summary>
    public async Task<OmitirItemMonitoreoResult> Handle(
        OmitirItemMonitoreo cmd,
        CancellationToken ct = default)
    {
        // PRE-2: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"La inspección {cmd.InspeccionId} no existe.");
        }

        // Delegar PRE-3..PRE-9 al aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.OmitirItem(cmd, ahora);

        _session.Events.Append(cmd.InspeccionId, eventos.ToArray());
        await _session.SaveChangesAsync(ct);

        return new OmitirItemMonitoreoResult(
            InspeccionId: cmd.InspeccionId,
            ItemId: cmd.ItemId,
            Motivo: cmd.Motivo,
            OmitidoEn: ahora);
    }
}
