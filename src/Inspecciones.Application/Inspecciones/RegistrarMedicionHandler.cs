using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="RegistrarMedicion"/>. Orquesta:
/// <list type="number">
///   <item>PRE-2 — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>Delega PRE-3..PRE-8 al método de decisión del aggregate.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// </summary>
public sealed class RegistrarMedicionHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste los eventos emitidos.</summary>
    public async Task<RegistrarMedicionResult> Handle(
        RegistrarMedicion cmd,
        CancellationToken ct = default)
    {
        // PRE-2: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"La inspección {cmd.InspeccionId} no existe.");
        }

        // Delegar PRE-3..PRE-8 al aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.RegistrarMedicion(cmd, ahora);

        _session.Events.Append(cmd.InspeccionId, eventos.ToArray());
        await _session.SaveChangesAsync(ct);

        // Construir resultado con FueraDeRango y HallazgoGeneradoId.
        var evMedicion = (MedicionRegistrada_v1)eventos[0];
        var hallazgoGeneradoId = evMedicion.FueraDeRango ? cmd.HallazgoId : (Guid?)null;

        return new RegistrarMedicionResult(
            InspeccionId: cmd.InspeccionId,
            ItemId: cmd.ItemId,
            ValorMedido: cmd.ValorMedido,
            FueraDeRango: evMedicion.FueraDeRango,
            HallazgoGeneradoId: hallazgoGeneradoId,
            RegistradaEn: ahora);
    }
}
