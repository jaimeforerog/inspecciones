using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de salida del endpoint <c>GET /api/v1/inspecciones/{id}</c>. Espeja el
/// estado completo del aggregate <see cref="Inspeccion"/> reconstruido desde su
/// stream de eventos (vía <c>IInspeccionReader.LeerAsync</c>). Resuelve de raíz
/// el "no queda guardada": la PWA puede releer el estado persistido para reentrar
/// al flujo tras recargar o perder el contexto local.
/// </summary>
/// <remarks>
/// No se expone <c>ItemsSnapshot</c> del monitoreo porque su campo
/// <c>EvaluacionEsperada</c> es polimórfico y requeriría discriminadores de tipo
/// en el serializer. Los sets de ítems procesados (<see cref="ItemsMedidos"/>,
/// <see cref="ItemsEvaluados"/>, <see cref="ItemsOmitidos"/>) sí se exponen — son
/// la afirmación "qué ítems ya se procesaron" que el cliente necesita al reentrar.
/// </remarks>
public sealed record RecuperarInspeccionResponse(
    Guid InspeccionId,
    TipoInspeccion Tipo,
    EstadoInspeccion Estado,
    int EquipoId,
    int RutinaId,
    string RutinaCodigo,
    string TecnicoIniciador,
    int ProyectoId,
    UbicacionGps? Ubicacion,
    DateTimeOffset IniciadaEn,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario,
    int? RutinaMonitoreoSeleccionadaId,
    IReadOnlyCollection<int> ItemsMedidos,
    IReadOnlyCollection<int> ItemsEvaluados,
    IReadOnlyCollection<int> ItemsOmitidos,
    IReadOnlyList<Hallazgo> Hallazgos,
    IReadOnlyList<Repuesto> Repuestos,
    IReadOnlyCollection<int> NovedadesDescartadas,
    string? DiagnosticoFinal,
    DictamenOperacion? Dictamen,
    string? FirmaUri,
    UbicacionGps? UbicacionFirma,
    DateTimeOffset? FirmadaEn,
    bool OTSolicitada,
    bool OTRechazada,
    DateTimeOffset? SolicitadaEn,
    string? MotivoRechazoOT,
    string? MotivoCancelacion,
    IReadOnlyCollection<string> Contribuyentes)
{
    /// <summary>Proyecta el aggregate reconstruido al DTO de salida.</summary>
    public static RecuperarInspeccionResponse Desde(Inspeccion i) => new(
        InspeccionId: i.InspeccionId,
        Tipo: i.Tipo,
        Estado: i.Estado,
        EquipoId: i.EquipoId,
        RutinaId: i.RutinaId,
        RutinaCodigo: i.RutinaCodigo,
        TecnicoIniciador: i.TecnicoIniciador,
        ProyectoId: i.ProyectoId,
        Ubicacion: i.Ubicacion,
        IniciadaEn: i.IniciadaEn,
        FechaReportada: i.FechaReportada,
        LecturaMedidorPrimario: i.LecturaMedidorPrimario,
        LecturaMedidorSecundario: i.LecturaMedidorSecundario,
        RutinaMonitoreoSeleccionadaId: i.RutinaMonitoreoSeleccionadaId,
        ItemsMedidos: i.ItemsMedidos.ToArray(),
        ItemsEvaluados: i.ItemsEvaluados.ToArray(),
        ItemsOmitidos: i.ItemsOmitidos.ToArray(),
        Hallazgos: i.Hallazgos,
        Repuestos: i.Repuestos,
        NovedadesDescartadas: i.NovedadesDescartadas.ToArray(),
        DiagnosticoFinal: i.DiagnosticoFinal,
        Dictamen: i.Dictamen,
        FirmaUri: i.FirmaUri,
        UbicacionFirma: i.UbicacionFirma,
        FirmadaEn: i.FirmadaEn,
        OTSolicitada: i.OTSolicitada,
        OTRechazada: i.OTRechazada,
        SolicitadaEn: i.SolicitadaEn,
        MotivoRechazoOT: i.MotivoRechazoOT,
        MotivoCancelacion: i.MotivoCancelacion,
        Contribuyentes: i.Contribuyentes.ToArray());
}
