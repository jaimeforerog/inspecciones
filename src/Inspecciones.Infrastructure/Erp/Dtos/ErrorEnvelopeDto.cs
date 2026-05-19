namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Envelope de error que devuelve Maquinaria_V4 para respuestas 4xx (ver
/// <c>SincoSoft.MYE.MiddlewareErrorHandler</c>). Inspecciones lo deserializa
/// para mapear a <see cref="MaquinariaErpException"/> con mensaje legible.
/// </summary>
/// <remarks>
/// Divergencia con el contrato de Inspecciones (§1.5): el contrato espera
/// <c>{ code, message, details, traceId }</c> camelCase. Maquinaria_V4 usa
/// <c>{ Codigo, Mensaje }</c> PascalCase. El mapeo se hace acá — no
/// se modifica Maquinaria_V4.
/// </remarks>
public sealed record ErrorEnvelopeDto
{
    public string Codigo { get; init; } = string.Empty;
    public string Mensaje { get; init; } = string.Empty;
}
