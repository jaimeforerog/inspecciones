namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Estado de una novedad de preoperacional respecto de una inspección concreta.
/// NO es un campo del ERP: se deriva del stream del aggregate <see cref="Inspeccion"/>
/// (slice 1q). Los tres valores son mutuamente excluyentes dentro de una inspección
/// por INV-ND1 (§15.3 del modelo).
/// </summary>
public enum EstadoNovedadImportacion
{
    /// <summary>Ni importada ni descartada en esta inspección — candidata a importar.</summary>
    Disponible,

    /// <summary>Convertida en un hallazgo activo (no eliminado) con <c>Origen=PreOperacional</c>.</summary>
    Importada,

    /// <summary>Descartada explícitamente (<see cref="NovedadPreopDescartada_v1"/>) en esta inspección.</summary>
    Descartada
}
