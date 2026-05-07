namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento emitido cuando el técnico registra el diagnóstico final de la inspección.
/// Primer evento del acto atómico de firma (orden 1/3). Slice 1g — FirmarInspeccion.
/// </summary>
public sealed record DiagnosticoEmitido_v1(
    Guid           InspeccionId,
    string         DiagnosticoFinal,
    string         EmitidoPor,
    DateTimeOffset EmitidoEn);