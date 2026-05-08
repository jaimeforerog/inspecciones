using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Value object que representa un hallazgo registrado en la inspección.
/// Materializado en el agregado a partir de <see cref="HallazgoRegistrado_v1"/>.
/// Shape mínimo según §15.2 del modelo. Campos mutables añadidos en slice 1d:
/// <see cref="NovedadTecnica"/>, <see cref="AccionCorrectiva"/>, <see cref="UbicacionGps"/>.
/// Campo añadido en slice 1e: <see cref="MotivoEliminacion"/>.
/// </summary>
public sealed record Hallazgo(
    Guid            HallazgoId,
    OrigenHallazgo  Origen,
    int             ParteEquipoId,
    int?            NovedadPreopOrigenId,
    string          NovedadTecnica,
    AccionRequerida AccionRequerida,
    string?         AccionCorrectiva,
    int?            TipoFallaId,
    int?            CausaFallaId,
    UbicacionGps?   UbicacionGps,
    bool            Eliminado,
    string?         MotivoEliminacion,
    // Slice 1i — MedicionOrigenId: int? (nullable — null para Manual/PreOperacional/Seguimiento;
    // = ItemId cuando Origen=Monitoreo). Backward compat: Apply(HallazgoRegistrado_v1) propaga null.
    int?            MedicionOrigenId = null);
