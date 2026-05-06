using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

public sealed class ActualizarHallazgoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    public async Task<ActualizarHallazgoResult> ManejarAsync(
        ActualizarHallazgo cmd,
        CancellationToken ct = default)
    {
        // PRE-F: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"Inspección {cmd.InspeccionId} no encontrada.");
        }

        // Delegar PRE-A, PRE-B, PRE-C, PRE-D, PRE-E al método de decisión del aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.ActualizarHallazgo(cmd, ahora);

        // Append al stream — un único SaveChangesAsync (regla CLAUDE.md atomicidad).
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        var evento = (HallazgoActualizado_v1)eventos[0];
        return new ActualizarHallazgoResult(
            HallazgoId: evento.HallazgoId,
            InspeccionId: evento.InspeccionId,
            AccionRequerida: evento.AccionRequerida,
            ActualizadoEn: evento.ActualizadoEn);
    }
}

public sealed record ActualizarHallazgoResult(
    Guid HallazgoId,
    Guid InspeccionId,
    AccionRequerida AccionRequerida,
    DateTimeOffset ActualizadoEn);
