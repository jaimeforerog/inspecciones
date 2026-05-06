namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Value object que representa un hallazgo registrado en la inspección.
/// Materializado en el agregado a partir de <see cref="HallazgoRegistrado_v1"/>.
/// Shape mínimo según §15.2 del modelo.
/// </summary>
public sealed record Hallazgo(
    Guid   HallazgoId,
    OrigenHallazgo Origen,
    int    ParteEquipoId,
    int?   NovedadPreopOrigenId,
    AccionRequerida AccionRequerida,
    int?   TipoFallaId,
    int?   CausaFallaId,
    bool   Eliminado);
