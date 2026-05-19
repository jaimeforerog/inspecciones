namespace Inspecciones.Infrastructure.Erp.Dtos;

/// <summary>
/// Body del request a <c>PUT /api/v4/Maquinaria/api/equipos/{codigo}/dictamen-vigente</c>
/// (slice 11 de Maquinaria_V4). Inspecciones lo emite desde el handler M-W-1 tras
/// <c>FirmarInspeccion</c> (ADR-006 outbox + saga <c>SincronizarDictamenVigenteSaga</c>).
/// </summary>
/// <remarks>
/// Estado: 0=puede operar, 1=con restricción, 2=no puede operar.
/// Idempotencia last-write-wins (cerradura natural — no requiere Idempotency-Key).
/// </remarks>
public sealed record ActualizarDictamenEquipoRequestDto
{
    public int Estado { get; init; }
}

/// <summary>Respuesta 200 OK del endpoint.</summary>
public sealed record ActualizarDictamenEquipoResponseDto
{
    public int Codigo { get; init; }
    public int Estado { get; init; }
    public int EstadoUsuario { get; init; }
    public DateTimeOffset EstadoFecha { get; init; }
}
