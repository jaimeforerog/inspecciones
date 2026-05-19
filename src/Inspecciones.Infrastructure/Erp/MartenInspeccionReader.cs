using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Adapter de producción de <see cref="IInspeccionReader"/> que reconstruye
/// el aggregate <see cref="Inspeccion"/> desde el event store Marten mediante
/// <c>IQuerySession.Events.AggregateStreamAsync&lt;Inspeccion&gt;</c>.
///
/// Devuelve <c>null</c> si el stream no existe (PRE-L1 del listener erp-3).
/// </summary>
public sealed class MartenInspeccionReader : IInspeccionReader
{
    private readonly IQuerySession _session;

    public MartenInspeccionReader(IQuerySession session)
    {
        _session = session;
    }

    public Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default) =>
        _session.Events.AggregateStreamAsync<Inspeccion>(inspeccionId, token: ct);
}
