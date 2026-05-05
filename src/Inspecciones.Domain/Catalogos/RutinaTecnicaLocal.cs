namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Read model local de una rutina técnica del catálogo, sincronizada vía M-17
/// con shape mínimo (ADR-004 punto 1 — sin <c>Items[]</c> ni
/// <c>ActividadId</c>; la rutina técnica MVP es filtro del catálogo de partes,
/// no checklist). Ver §12.10.5 modelo.
/// </summary>
public sealed record RutinaTecnicaLocal(
    int RutinaId,
    string Codigo,
    string Nombre,
    TipoRutina Tipo,
    string GrupoMantenimiento,
    int ParteId,
    string ParteCodigo,
    DateTimeOffset SincronizadoEn);
