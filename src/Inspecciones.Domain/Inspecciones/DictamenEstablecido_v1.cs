namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando el técnico establece el dictamen de operación del equipo.
/// Segundo evento del acto atómico de firma (orden 2/3). Slice 1g — FirmarInspeccion.
/// </summary>
public sealed record DictamenEstablecido_v1(
    Guid              InspeccionId,
    DictamenOperacion Dictamen,
    string            Justificacion,
    string            EmitidoPor,
    DateTimeOffset    EstablecidoEn);
