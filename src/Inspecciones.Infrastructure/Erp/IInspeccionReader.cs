using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Puerto de lectura del aggregate <see cref="Inspeccion"/> desde el event store.
/// Abstrae <c>IQuerySession.Events.AggregateStreamAsync&lt;Inspeccion&gt;</c>
/// para que los listeners de integración sean testeables sin Marten real.
///
/// Implementación de producción: <c>MartenInspeccionReader</c>.
/// Implementación de test: <c>FakeInspeccionReader</c> en el proyecto de tests.
/// </summary>
public interface IInspeccionReader
{
    /// <summary>
    /// Reconstruye el aggregate <see cref="Inspeccion"/> desde su stream de eventos.
    /// Devuelve <c>null</c> si el stream no existe (PRE-L1 del listener erp-3).
    /// Esta overload usa el tenant resuelto del scope ambient (HTTP request o
    /// envelope Wolverine) — usable solo cuando ese tenant existe.
    /// </summary>
    Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>
    /// Reconstruye el aggregate <see cref="Inspeccion"/> desde su stream de eventos
    /// en el tenant especificado (Marten Conjoined — ADR-009). Usado por los
    /// listeners Wolverine post-mt-2 que leen el <c>TenantId</c> del envelope del
    /// mensaje entrante y lo propagan explícitamente.
    /// </summary>
    Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default);
}
