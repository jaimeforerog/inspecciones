namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento stub para representar el cierre de una inspección sin generar OT
/// (estado terminal CerradaSinOT). Slice 1k — GenerarOT (stub para PRE-3 / §6.12).
/// La implementación completa pertenece a un slice futuro (CerrarSinOT).
/// </summary>
public sealed record InspeccionCerradaSinOT_v1(
    Guid           InspeccionId,
    string         CerradoPor,
    DateTimeOffset CerradaEn);
