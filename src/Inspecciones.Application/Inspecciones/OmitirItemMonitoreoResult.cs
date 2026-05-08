namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Resultado del handler <see cref="OmitirItemMonitoreoHandler"/>.
/// Shape canónico según spec slice 1j §2. La omisión nunca genera hallazgo
/// automático (§12.11.5 punto 6) — no existe campo <c>HallazgoGeneradoId</c>.
/// </summary>
public sealed record OmitirItemMonitoreoResult(
    Guid           InspeccionId,
    int            ItemId,
    string         Motivo,
    DateTimeOffset OmitidoEn);
