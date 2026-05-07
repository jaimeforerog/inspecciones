using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento que sella la inspección como firmada por el técnico.
/// Tercer evento del acto atómico de firma (orden 3/3). Slice 1g — FirmarInspeccion.
/// Payload completo según spec §3.
/// </summary>
public sealed record InspeccionFirmada_v1(
    Guid           InspeccionId,
    string         FirmadoPor,
    string         FirmaUri,
    UbicacionGps   UbicacionFirma,
    DateTimeOffset FirmadaEn);
