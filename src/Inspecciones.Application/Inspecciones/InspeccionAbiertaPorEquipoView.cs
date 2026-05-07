using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Read model que materializa la afirmación "el equipo {EquipoId} tiene una
/// inspección activa". PK Marten = <see cref="Id"/> (= <see cref="EquipoId"/>
/// semánticamente). La existencia de la fila es la afirmación de "EnEjecucion";
/// cuando lleguen los eventos terminales (<c>InspeccionFirmada_v1</c>,
/// <c>InspeccionCancelada_v1</c>) en slices futuros se eliminará la fila.
///
/// Defensa dura I-I1: el handler usa <c>session.Insert(view)</c> (INSERT sin
/// ON CONFLICT UPDATE). Si el equipo ya tiene fila, Postgres lanza
/// <c>PostgresException(SqlState=23505)</c> via <c>MartenCommandException</c>.
/// El handler atrapa la excepción, relee la proyección y retorna
/// <c>RedirigeAExistente=true</c>.
/// Slice 1h: añadido campo <see cref="Tipo"/> para que el frontend distinga el tipo
/// de inspección activa al redirigir (I-I1 cruzando tipos — decisión D5).
/// </summary>
/// <param name="Id">PK Marten (convención nombre "Id") — igual a EquipoId. Garantiza Insert-only semántica.</param>
/// <param name="InspeccionId">Stream-id de la inspección activa.</param>
/// <param name="TecnicoIniciador">Login del técnico que inició la inspección activa.</param>
/// <param name="IniciadaEn">Timestamp del evento <c>InspeccionIniciada_v1</c>.</param>
/// <param name="ProyectoId">Proyecto al que pertenece la inspección activa.</param>
/// <param name="Tipo">Tipo de inspección activa (Tecnica o Monitoreo). Slice 1h §8.1.</param>
public sealed record InspeccionAbiertaPorEquipoView(
    int Id,
    Guid InspeccionId,
    string TecnicoIniciador,
    DateTimeOffset IniciadaEn,
    int ProyectoId,
    TipoInspeccion Tipo = TipoInspeccion.Tecnica)
{
    /// <summary>
    /// Alias semántico de <see cref="Id"/> — el Id del documento Marten es el
    /// EquipoId por convención de nomenclatura del modelo de dominio.
    /// </summary>
    public int EquipoId => Id;
}
