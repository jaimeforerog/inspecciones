using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada del endpoint <c>POST /api/v1/inspecciones/{id}/firmar</c>.
/// La capa API mapea esto al record <c>FirmarInspeccion</c> del dominio. Spec §9.
/// </summary>
public sealed record FirmarInspeccionRequest(
    string           Diagnostico,
    DictamenOperacion Dictamen,
    string           JustificacionDictamen,
    string           FirmaUri,
    UbicacionGps?    UbicacionFirma);
