namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Parte del equipo sincronizada desde el catálogo Sinco (M-3b).
/// El handler del slice 1c valida que <c>ParteEquipoId</c> pertenezca al equipo
/// inspeccionado consultando esta lista (INV-PartePerteneceAlEquipo).
/// </summary>
public sealed record ParteEquipoLocal(
    int ParteEquipoId,
    string ParteCodigo,
    string ParteNombre);
