using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <c>IniciarInspeccion</c>. Orquesta:
/// <list type="number">
///   <item>I-I1 validación blanda contra <c>InspeccionAbiertaPorEquipoView</c>; corto-circuito si activa.</item>
///   <item>Resolución de <c>EquipoLocal</c> y <c>RutinaTecnicaLocal</c> desde Marten.</item>
///   <item>Invocación de <see cref="Inspeccion.Iniciar"/> para validar pre-condiciones y producir eventos.</item>
///   <item>Append al stream + commit atómico (Marten + Wolverine envelope dedup).</item>
/// </list>
/// Defensa dura I-I1 vive en el índice único parcial Postgres de la proyección.
/// </summary>
public sealed class IniciarInspeccionHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y devuelve el resultado.</summary>
    public Task<IniciarInspeccionResult> ManejarAsync(
        IniciarInspeccion cmd,
        ClaimsTecnico claims,
        CancellationToken ct = default)
    {
        // STUB — fase red. Implementación en fase green.
        throw new NotImplementedException();
    }
}
