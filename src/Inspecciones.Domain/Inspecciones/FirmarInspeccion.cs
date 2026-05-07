using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando que sella la inspección con diagnóstico, dictamen y firma manuscrita.
/// Slice 1g — FirmarInspeccion. §2 del spec.
/// </summary>
public sealed record FirmarInspeccion(
    Guid             InspeccionId,
    string           Diagnostico,
    DictamenOperacion Dictamen,
    string           JustificacionDictamen,
    string           FirmaUri,
    UbicacionGps?    UbicacionFirma,
    string           TecnicoId);