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
/// Slice 1l (FU-35): <c>InspeccionCerradaSinOT_v1</c> NO se maneja aquí. En el flujo canónico
/// del aggregate <c>Inspeccion</c>, <c>InspeccionFirmada_v1</c> siempre precede a
/// <c>InspeccionCerradaSinOT_v1</c> (el equipo ya queda libre al firmar — el cierre es un
/// estado terminal posterior). Por lo tanto la fila ya fue eliminada por el handler de
/// <c>InspeccionFirmada_v1</c> antes de que llegue el evento de cierre. Si en el futuro
/// emerge un flujo donde <c>InspeccionCerradaSinOT_v1</c> aparezca sin un
/// <c>InspeccionFirmada_v1</c> previo (p. ej. cierre directo desde un estado de excepción),
/// añadir el handler aquí.
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
            ProyectoId: e.ProyectoId,
            Tipo: e.Tipo);
        ops.Insert(view);
    }

    /// <summary>Elimina la fila cuando la inspección es firmada (equipo queda libre).</summary>
    /// <remarks>
    /// Fix slice 1k: (1) se eliminó el parámetro <c>IQuerySession session</c> que Marten 7
    /// no soporta en <c>EventProjection.Project</c>; (2) se reemplazó el patrón
    /// query-entonces-delete por <c>DeleteWhere</c> para evitar que inserts del mismo batch
    /// no sean visibles al query en modo inline (race condition dentro del mismo
    /// <c>SaveChangesAsync</c> cuando la siembra incluye InspeccionIniciada_v1 y
    /// InspeccionFirmada_v1 en el mismo stream commit).
    /// </remarks>
    public static void Project(InspeccionFirmada_v1 e, IDocumentOperations ops)
    {
        // DeleteWhere por InspeccionId resuelve la fila sin necesitar el EquipoId (que no está
        // en InspeccionFirmada_v1). No requiere visibilidad del insert previo en el mismo batch.
        ops.DeleteWhere<InspeccionAbiertaPorEquipoView>(v => v.InspeccionId == e.InspeccionId);
    }

    /// <summary>Elimina la fila cuando la inspección es cancelada (equipo queda libre).</summary>
    /// <remarks>
    /// Fix slice 1k: mismo fix que <c>InspeccionFirmada_v1</c> — DeleteWhere sin query previo.
    /// </remarks>
    public static void Project(InspeccionCancelada_v1 e, IDocumentOperations ops)
    {
        ops.DeleteWhere<InspeccionAbiertaPorEquipoView>(v => v.InspeccionId == e.InspeccionId);
    }
}
