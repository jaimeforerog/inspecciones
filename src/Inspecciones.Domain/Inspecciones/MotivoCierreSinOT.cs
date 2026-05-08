namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Discriminador del motivo de cierre de una inspección sin generar OT.
/// Slice 1l — RechazarGenerarOT (D-5: un único event record con discriminador).
/// Consume <see cref="InspeccionCerradaSinOT_v1"/>.
/// </summary>
public enum MotivoCierreSinOT
{
    /// <summary>
    /// La saga <c>CerrarInspeccionSaga</c> cerró automáticamente la inspección
    /// al firmar porque ningún hallazgo activo tenía <c>AccionRequerida=RequiereIntervencion</c>.
    /// No hay persona responsable del cierre — el proceso es automático.
    /// </summary>
    AutomaticoSinIntervencion,

    /// <summary>
    /// Un usuario con capability <c>generar-ot</c> rechazó explícitamente la generación
    /// de la OT mediante el comando <c>RechazarGenerarOT</c>.
    /// El aprobador queda auditado en <see cref="GeneracionOTRechazada_v1.RechazadoPor"/>.
    /// </summary>
    RechazadaPorAprobador,
}
