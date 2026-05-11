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

    /// <summary>
    /// Decisión <c>RegistrarHallazgo</c>. Reconstruye el aggregate desde el stream
    /// previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> RegistrarHallazgo(
        IReadOnlyList<object> dados,
        RegistrarHallazgo cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.RegistrarHallazgo(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>ActualizarHallazgo</c>. Reconstruye el aggregate desde el stream
    /// previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> ActualizarHallazgo(
        IReadOnlyList<object> dados,
        ActualizarHallazgo cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.ActualizarHallazgo(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>EliminarHallazgo</c>. Reconstruye el aggregate desde el stream
    /// previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> EliminarHallazgo(
        IReadOnlyList<object> dados,
        EliminarHallazgo cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.EliminarHallazgo(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>AsignarRepuesto</c>. Reconstruye el aggregate desde el stream
    /// previo y delega al método de decisión del aggregate. Los parámetros
    /// <paramref name="skuCodigo"/> y <paramref name="unidad"/> son derivados del
    /// catálogo local por el handler (o el test en fase roja).
    /// </summary>
    public static IReadOnlyList<object> AsignarRepuesto(
        IReadOnlyList<object> dados,
        AsignarRepuesto cmd,
        string skuCodigo,
        string unidad,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.AsignarRepuesto(cmd, skuCodigo, unidad, ahora);
    }

    /// <summary>
    /// Decisión <c>Firmar</c>. Reconstruye el aggregate desde el stream previo
    /// y delega al método de decisión del aggregate. Slice 1g — FirmarInspeccion.
    /// </summary>
    public static IReadOnlyList<object> Firmar(
        IReadOnlyList<object> dados,
        FirmarInspeccion cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.Firmar(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>RegistrarMedicion</c>. Slice 1i — RegistrarMedicion. Reconstruye
    /// el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> RegistrarMedicion(
        IReadOnlyList<object> dados,
        RegistrarMedicion cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.RegistrarMedicion(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>RegistrarEvaluacionCualitativa</c>. Slice 1i' — RegistrarEvaluacionCualitativa.
    /// Reconstruye el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> RegistrarEvaluacionCualitativa(
        IReadOnlyList<object> dados,
        RegistrarEvaluacionCualitativa cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.RegistrarEvaluacionCualitativa(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>OmitirItem</c>. Slice 1j — OmitirItemMonitoreo. Reconstruye
    /// el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> OmitirItem(
        IReadOnlyList<object> dados,
        OmitirItemMonitoreo cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.OmitirItem(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>SolicitarOT</c>. Slice 1k — GenerarOT. Reconstruye
    /// el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> SolicitarOT(
        IReadOnlyList<object> dados,
        GenerarOT cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.SolicitarOT(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>RechazarOT</c>. Slice 1l — RechazarGenerarOT. Reconstruye
    /// el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// </summary>
    public static IReadOnlyList<object> RechazarOT(
        IReadOnlyList<object> dados,
        RechazarGenerarOT cmd,
        DateTimeOffset ahora)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        return aggregate.RechazarOT(cmd, ahora);
    }

    /// <summary>
    /// Decisión <c>Cancelar</c>. Slice 1m — CancelarInspeccion. Reconstruye
    /// el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// Los parámetros PRE-3 y PRE-4 (técnico contribuyente y motivo) son evaluados
    /// en el handler o en el método de decisión según la capa definida en spec §4.
    /// Para tests de dominio puro, el helper pasa directamente los valores; el handler
    /// real aplica PRE-3 antes de llamar a Cancelar.
    /// </summary>
    public static IReadOnlyList<object> Cancelar(
        IReadOnlyList<object> dados,
        string motivo,
        string canceladaPor,
        DateTimeOffset canceladaEn)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        // PRE-3 (técnico contribuyente) — el handler la valida; aquí la inlinamos para tests de dominio puro.
        if (!aggregate.Contribuyentes.Contains(canceladaPor))
        {
            throw new TecnicoNoContribuyenteException(
                $"El técnico '{canceladaPor}' no ha contribuido a la inspección {aggregate.InspeccionId}. Solo un técnico contribuyente puede cancelarla.");
        }
        // PRE-4 (motivo válido) — el handler la valida; aquí la inlinamos para tests de dominio puro.
        if (motivo.Trim().Length < 10)
        {
            throw new MotivoCancelacionInvalidoException(
                motivo.Trim().Length == 0
                    ? "El motivo de cancelación no puede estar vacío."
                    : $"El motivo de cancelación debe tener al menos 10 caracteres. Longitud actual (trimmed): {motivo.Trim().Length}.");
        }
        return aggregate.Cancelar(motivo, canceladaPor, canceladaEn);
    }

    /// <summary>
    /// Decisión <c>Descartar</c>. Slice 1n — DescartarNovedadPreop. Reconstruye
    /// el aggregate desde el stream previo y delega al método de decisión del aggregate.
    /// El motivo es autogenerado siguiendo la plantilla D-4 del spec (P-3):
    /// "Cerrado por {usuario} el {fecha:yyyy-MM-dd HH:mm} UTC desde Inspecciones".
    /// </summary>
    public static IReadOnlyList<object> Descartar(
        IReadOnlyList<object> dados,
        DescartarNovedadPreop cmd,
        DateTimeOffset descartadaEn)
    {
        var aggregate = Inspeccion.Reconstruir(dados);
        var motivoDescarte = $"Cerrado por {cmd.DescartadaPor} el {descartadaEn:yyyy-MM-dd HH:mm} UTC desde Inspecciones";
        return aggregate.Descartar(cmd, motivoDescarte, descartadaEn);
    }

    /// <summary>
    /// Decisión <c>IniciarMonitoreo</c>. Slice 1h — IniciarInspeccionMonitoreo.
    /// El aggregate se crea sobre stream vacío (PRE-7 I-I1 corto-circuita en el
    /// handler antes de llegar aquí). El handler pasa <paramref name="itemsSnapshot"/>
    /// ya filtrados y ordenados (PRE-6 / I-I-Mon-1 ya validada).
    /// </summary>
    public static IReadOnlyList<object> IniciarMonitoreo(
        IReadOnlyList<object> dados,
        IniciarInspeccionMonitoreo cmd,
        ClaimsTecnico claims,
        string rutinaNombre,
        IReadOnlyList<ItemRutinaMonitoreoSnapshot> itemsSnapshot,
        DateTimeOffset ahora)
    {
        _ = Inspeccion.Reconstruir(dados);
        return Inspeccion.IniciarMonitoreo(cmd, claims, rutinaNombre, itemsSnapshot, ahora);
    }
}
