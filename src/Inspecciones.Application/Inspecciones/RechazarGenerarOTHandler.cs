using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="RechazarGenerarOT"/>. Orquesta:
/// <list type="number">
///   <item>PRE-1 — capability check (validada en capa HTTP antes de llegar aquí).</item>
///   <item>PRE-2 — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>Delega PRE-3..PRE-7 (I-F6) al método de decisión <see cref="Inspeccion.RechazarOT"/>.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// Emite exactamente dos eventos en orden causal:
/// <see cref="GeneracionOTRechazada_v1"/> seguido de <see cref="InspeccionCerradaSinOT_v1"/>.
/// El cierre es síncrono — no hay saga asíncrona ni POST al ERP en este slice (D-4: 200 OK).
/// </summary>
public sealed class RechazarGenerarOTHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste los dos eventos emitidos atómicamente.</summary>
    public async Task<RechazarGenerarOTResult> Handle(
        RechazarGenerarOT cmd,
        CancellationToken ct = default)
    {
        // PRE-2: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"La inspección {cmd.InspeccionId} no existe.");
        }

        // Delegar PRE-3..PRE-7 (I-F6) al aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.RechazarOT(cmd, ahora);

        _session.Events.Append(cmd.InspeccionId, eventos.ToArray());
        await _session.SaveChangesAsync(ct);

        var evtRechazo = (GeneracionOTRechazada_v1)eventos[0];

        return new RechazarGenerarOTResult(
            InspeccionId: evtRechazo.InspeccionId,
            Estado: nameof(EstadoInspeccion.CerradaSinOT),
            RechazadaEn: evtRechazo.RechazadaEn,
            RechazadoPor: evtRechazo.RechazadoPor,
            Motivo: evtRechazo.Motivo);
    }
}
