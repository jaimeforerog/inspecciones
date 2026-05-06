using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

public sealed class EliminarHallazgoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    public async Task ManejarAsync(EliminarHallazgo cmd, CancellationToken ct = default)
    {
        // PRE-F: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"Inspección {cmd.InspeccionId} no encontrada.");
        }

        // Delegar PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-D al método de decisión del aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.EliminarHallazgo(cmd, ahora);

        // Append al stream — un único SaveChangesAsync (regla CLAUDE.md atomicidad).
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);
    }
}
