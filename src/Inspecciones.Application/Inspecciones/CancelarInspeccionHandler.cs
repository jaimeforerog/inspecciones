using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="CancelarInspeccion"/>. Slice 1m.
/// PRE-2 (inspección existe), PRE-3 (técnico contribuyente), PRE-4 (motivo ≥10 chars)
/// viven aquí. PRE-5 (estado EnEjecucion — I6) vive en el aggregate.
/// Un único <see cref="IDocumentSession.SaveChangesAsync"/> — atomicidad garantizada.
/// </summary>
public sealed class CancelarInspeccionHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    public async Task<CancelarInspeccionResult> Handle(
        CancelarInspeccion cmd,
        CancellationToken ct = default)
    {
        // PRE-2: la inspección debe existir en el store.
        var aggregate = await _session.Events
            .AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);

        if (aggregate is null)
        {
            throw new InspeccionNoEncontradaException(
                $"La inspección '{cmd.InspeccionId}' no fue encontrada.");
        }

        // PRE-3: solo un técnico contribuyente puede cancelar.
        if (!aggregate.Contribuyentes.Contains(cmd.CanceladaPor))
        {
            throw new TecnicoNoContribuyenteException(
                $"El técnico '{cmd.CanceladaPor}' no ha contribuido a la inspección {cmd.InspeccionId}. Solo un técnico contribuyente puede cancelarla.");
        }

        // PRE-4: el motivo debe tener al menos 10 caracteres (trimmed).
        var motivoTrimmed = cmd.Motivo.Trim();
        if (motivoTrimmed.Length < 10)
        {
            throw new MotivoCancelacionInvalidoException(
                motivoTrimmed.Length == 0
                    ? "El motivo de cancelación no puede estar vacío."
                    : $"El motivo de cancelación debe tener al menos 10 caracteres. Longitud actual (trimmed): {motivoTrimmed.Length}.");
        }

        var canceladaEn = _time.GetUtcNow();

        // PRE-5 vive en el aggregate (I6 + I-F1).
        var eventos = aggregate.Cancelar(cmd.Motivo, cmd.CanceladaPor, canceladaEn);

        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        return new CancelarInspeccionResult(
            InspeccionId: cmd.InspeccionId,
            Estado: "Cancelada",
            CanceladaEn: canceladaEn,
            CanceladaPor: cmd.CanceladaPor,
            Motivo: cmd.Motivo);
    }
}
