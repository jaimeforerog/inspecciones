namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Read model local del equipo, sincronizado desde Sinco on-prem vía M-3b
/// (ADR-004 + refinamientos 2026-05-05). Extendido en slice 1c con
/// <see cref="Partes"/> para la validación INV-PartePerteneceAlEquipo.
/// </summary>
/// <remarks>
/// Asignación equipo↔rutinas (decisión 2026-05-05, modelo §12.11.1 + §12.11.5):
/// <list type="bullet">
///   <item>
///     <c>RutinaTecnicaId</c> — asignación <b>explícita per-equipo</b> (1 rutina técnica por equipo).
///     El handler <c>IniciarInspeccion</c> la resuelve auto; el técnico no elige.
///   </item>
///   <item>
///     <c>GrupoMantenimientoId</c> — clave para <b>derivar las rutinas de monitoreo</b> client-side.
///     El cliente filtra el catálogo de rutinas de monitoreo (M-16) con
///     <c>rutina.GrupoMantenimientoId == equipo.GrupoMantenimientoId</c>. Sin tabla intermedia
///     equipo↔rutinas-monitoreo en el ERP (decisión 2026-05-05).
///   </item>
/// </list>
/// </remarks>
public sealed record EquipoLocal(
    int EquipoId,
    string EquipoCodigo,
    int ProyectoId,
    int? RutinaTecnicaId,
    int? GrupoMantenimientoId = null,
    IReadOnlyList<ParteEquipoLocal>? Partes = null);
