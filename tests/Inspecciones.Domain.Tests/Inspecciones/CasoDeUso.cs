using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Helper Given/When/Then para el agregado <see cref="Inspeccion"/>. Reconstruye
/// el aggregate desde el historial de eventos previos y delega al método de
/// decisión correspondiente. Patrón estándar de la metodología (ver
/// METHODOLOGY.md §2.1).
/// </summary>
internal static class CasoDeUso
{
    /// <summary>
    /// Decisión <c>Iniciar</c>. Para este slice 1, los eventos previos siempre
    /// son lista vacía (creación del stream); el helper acepta la lista para
    /// preservar la forma canónica Given/When/Then y permitir tests de rebuild.
    /// </summary>
    public static IReadOnlyList<object> Iniciar(
        IReadOnlyList<object> dados,
        IniciarInspeccion cmd,
        ClaimsTecnico claims,
        EquipoLocal equipo,
        RutinaTecnicaLocal rutina,
        DateTimeOffset ahora)
    {
        // En el slice 1 el aggregate se crea sobre stream vacío. Si dados trae eventos,
        // significa que el stream ya existía → I-I1 lo manejaría en el handler. Aquí
        // mantenemos la simetría con futuros slices que sí ejecuten sobre stream con
        // historia.
        _ = Inspeccion.Reconstruir(dados);
        return Inspeccion.Iniciar(cmd, claims, equipo, rutina, ahora);
    }
}
