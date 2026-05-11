using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="DescartarNovedadPreop"/>. Slice 1n.
/// PRE-1 (inspección existe) vive aquí. PRE-2 (estado EnEjecucion), PRE-5 y PRE-6
/// viven en el aggregate. PRE-3 (contribuyente o capability) y PRE-4 (capability)
/// viven en la capa HTTP.
/// Un único <see cref="IDocumentSession.SaveChangesAsync"/> — atomicidad garantizada.
/// </summary>
public sealed class DescartarNovedadPreopHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    public async Task<DescartarNovedadPreopResult> Handle(
        DescartarNovedadPreop cmd,
        CancellationToken ct = default)
    {
        // PRE-1: el stream debe existir.
        var aggregate = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (aggregate is null)
        {
            throw new InspeccionNoEncontradaException(
                $"La inspección {cmd.InspeccionId} no existe.");
        }

        // Motivo autogenerado D-4: "Cerrado por {usuario} el {fecha:yyyy-MM-dd HH:mm} UTC desde Inspecciones"
        var descartadaEn = _time.GetUtcNow();
        var motivoDescarte = $"Cerrado por {cmd.DescartadaPor} el {descartadaEn:yyyy-MM-dd HH:mm} UTC desde Inspecciones";

        // PRE-2, PRE-5, PRE-6 viven en el método de decisión del aggregate.
        var eventos = aggregate.Descartar(cmd, motivoDescarte, descartadaEn);

        _session.Events.Append(cmd.InspeccionId, eventos.ToArray());
        await _session.SaveChangesAsync(ct);

        return new DescartarNovedadPreopResult(
            InspeccionId: cmd.InspeccionId,
            NovedadId: cmd.NovedadId,
            MotivoDescarte: motivoDescarte,
            DescartadaPor: cmd.DescartadaPor,
            DescartadaEn: descartadaEn);
    }
}
