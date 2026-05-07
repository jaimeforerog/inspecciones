using Inspecciones.Domain.Inspecciones;
using Marten;
using Marten.Events.Projections;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Proyección inline de <see cref="InspeccionAbiertaPorEquipoView"/> (FU-13).
/// Migrada de <c>session.Insert(view)</c> en el handler a proyección registrada inline
/// en Marten. Corre en la misma transacción del <c>SaveChangesAsync</c>.
///
/// - <c>InspeccionIniciada_v1</c>  → upsert de la fila (equipo pasa a tener inspección activa).
/// - <c>InspeccionFirmada_v1</c>   → delete de la fila (equipo queda libre para nueva inspección).
/// - <c>InspeccionCancelada_v1</c> → delete de la fila (equipo queda libre).
///
/// Nota: <c>InspeccionFirmada_v1</c> e <c>InspeccionCancelada_v1</c> no contienen <c>EquipoId</c>
/// directamente. La proyección carga la fila existente por <c>InspeccionId</c> y la elimina.
/// Esto es un <c>EventProjection</c> (no <c>MultiStreamProjection</c>) porque necesita
/// resolver la identidad del documento a eliminar desde el store.
/// El agente <c>refactorer</c> puede considerar agregar <c>EquipoId</c> a los eventos terminales
/// para habilitar <c>MultiStreamProjection</c> puro si se requiere mayor rendimiento.
///
/// // TODO: actualizar BandejaTecnicoView en slice de proyecciones (spec §8.2).
/// </summary>
public sealed class InspeccionAbiertaPorEquipoProjection : EventProjection
{
    /// <summary>
    /// Crea la fila cuando se inicia una inspección.
    /// Usa <c>Insert</c> puro (sin ON CONFLICT UPDATE) para preservar la defensa
    /// dura I-I1 del handler de <c>IniciarInspeccion</c>: si el equipo ya tiene fila
    /// activa, Postgres lanza <c>23505</c> y el handler lo atrapa para redirigir al
    /// stream existente. Ver <see cref="IniciarInspeccionHandler"/> race condition §6.3.
    /// </summary>
    public static void Project(InspeccionIniciada_v1 e, IDocumentOperations ops)
    {
        var view = new InspeccionAbiertaPorEquipoView(
            Id: e.EquipoId,
            InspeccionId: e.InspeccionId,
            TecnicoIniciador: e.TecnicoIniciador,
            IniciadaEn: e.IniciadaEn,
            ProyectoId: e.ProyectoId);
        ops.Insert(view);
    }

    /// <summary>Elimina la fila cuando la inspección es firmada (equipo queda libre).</summary>
    public static async Task Project(InspeccionFirmada_v1 e, IQuerySession session, IDocumentOperations ops)
    {
        // Carga la fila buscando por InspeccionId para obtener el EquipoId (PK del documento).
        // El EventProjection recibe el IQuerySession para poder hacer queries de estado.
        var fila = await session.Query<InspeccionAbiertaPorEquipoView>()
            .Where(v => v.InspeccionId == e.InspeccionId)
            .FirstOrDefaultAsync();

        if (fila is not null)
        {
            ops.Delete<InspeccionAbiertaPorEquipoView>(fila.Id);
        }
    }

    /// <summary>Elimina la fila cuando la inspección es cancelada (equipo queda libre).</summary>
    public static async Task Project(InspeccionCancelada_v1 e, IQuerySession session, IDocumentOperations ops)
    {
        var fila = await session.Query<InspeccionAbiertaPorEquipoView>()
            .Where(v => v.InspeccionId == e.InspeccionId)
            .FirstOrDefaultAsync();

        if (fila is not null)
        {
            ops.Delete<InspeccionAbiertaPorEquipoView>(fila.Id);
        }
    }
}
