namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Estados del agregado <see cref="Inspeccion"/>. Transiciones permitidas
/// (§15.7 modelo): <c>EnEjecucion → Firmada | Cancelada</c>;
/// <c>Firmada → Cerrada | CerradaSinOT | RechazadaPorAprobador</c>.
/// </summary>
public enum EstadoInspeccion
{
    EnEjecucion,
    Firmada,
    Cerrada,
    CerradaSinOT,
    Cancelada,
    RechazadaPorAprobador
}
