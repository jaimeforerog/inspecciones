using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="GenerarOT"/>. Orquesta:
/// <list type="number">
///   <item>PRE-1 — capability check (validada en capa HTTP antes de llegar aquí).</item>
///   <item>PRE-2 — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>Delega PRE-3..PRE-7 (I-F4) al método de decisión <see cref="Inspeccion.SolicitarOT"/>.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// Emite exactamente un evento <see cref="OTSolicitada_v1"/>. El POST a MYE (M-1) lo realiza
/// la saga <c>EjecutarOTSaga</c> (slice 3.24b) al reaccionar a <see cref="OTSolicitada_v1"/>
/// via outbox Wolverine (ADR-006). Este handler no hace POST síncronos al ERP.
/// </summary>
public sealed class GenerarOTHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste el evento emitido.</summary>
    public async Task<GenerarOTResult> Handle(
        GenerarOT cmd,
        CancellationToken ct = default)
    {
        // PRE-2: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"La inspección {cmd.InspeccionId} no existe.");
        }

        // Delegar PRE-3..PRE-7 (I-F4) al aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.SolicitarOT(cmd, ahora);

        _session.Events.Append(cmd.InspeccionId, eventos.ToArray());
        await _session.SaveChangesAsync(ct);

        var evtOT = (OTSolicitada_v1)eventos[0];

        return new GenerarOTResult(
            InspeccionId: evtOT.InspeccionId,
            SolicitadaEn: evtOT.SolicitadaEn,
            SolicitadaPor: evtOT.SolicitadaPor,
            Responsable: evtOT.Responsable.ToString(),
            Prioridad: evtOT.Prioridad.ToString());
    }
}
