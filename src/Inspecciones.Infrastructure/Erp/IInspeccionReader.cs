using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Puerto de lectura del aggregate <see cref="Inspeccion"/> desde el event store.
/// Abstrae <c>IQuerySession.Events.AggregateStreamAsync&lt;Inspeccion&gt;</c>
/// para que los listeners de integración sean testeables sin Marten real.
///
/// Implementación de producción: <c>MartenInspeccionReader</c> (a cargo de green).
/// Implementación de test: <c>FakeInspeccionReader</c> en el proyecto de tests.
/// </summary>
public interface IInspeccionReader
{
    /// <summary>
    /// Reconstruye el aggregate <see cref="Inspeccion"/> desde su stream de eventos.
    /// Devuelve <c>null</c> si el stream no existe (PRE-L1 del listener erp-3).
    /// </summary>
    Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default);
}
