using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Adapter de producción de <see cref="IInspeccionReader"/> que reconstruye
/// el aggregate <see cref="Inspeccion"/> desde el event store Marten mediante
/// <c>IQuerySession.Events.AggregateStreamAsync&lt;Inspeccion&gt;</c>.
///
/// <para>
/// Refactor mt-2: recibe <see cref="IDocumentStore"/> (singleton) en lugar de
/// <see cref="IQuerySession"/> (scoped) para poder abrir sesiones discriminadas
/// por tenant. La overload <c>LeerAsync(Guid, CancellationToken)</c> sin tenant
/// se mantiene por compatibilidad pero abre sesión con el tenant resuelto del
/// scope ambient (HTTP) — internamente se delega a <see cref="IQuerySession"/>
/// inyectado por DI cuando Wolverine ya provee uno tenanted.
/// </para>
///
/// Devuelve <c>null</c> si el stream no existe (PRE-L1 del listener erp-3).
/// </summary>
public sealed class MartenInspeccionReader : IInspeccionReader
{
    private readonly IDocumentStore _store;
    private readonly IQuerySession _ambientSession;

    public MartenInspeccionReader(IDocumentStore store, IQuerySession ambientSession)
    {
        _store = store;
        _ambientSession = ambientSession;
    }

    public Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default) =>
        _ambientSession.Events.AggregateStreamAsync<Inspeccion>(inspeccionId, token: ct);

    public async Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(tenantId);
        return await session.Events.AggregateStreamAsync<Inspeccion>(inspeccionId, token: ct).ConfigureAwait(false);
    }
}
