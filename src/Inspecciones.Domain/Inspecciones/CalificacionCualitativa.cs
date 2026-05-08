namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Calificación visual de un ítem cualitativo de la rutina de monitoreo.
/// Definida en §12.11.5 punto 3 del modelo. Solo <see cref="Malo"/> dispara
/// hallazgo automático (P-2 — spec slice 1i').
/// </summary>
public enum CalificacionCualitativa
{
    Bueno,    // Estado correcto — sin hallazgo automático.
    Regular,  // Estado deteriorado, requiere atención eventual — sin hallazgo automático.
    Malo      // Estado crítico — dispara HallazgoRegistrado_v1 con AccionRequerida=RequiereSeguimiento.
}
