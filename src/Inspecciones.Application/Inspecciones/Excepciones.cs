using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// PRE-3 (handler) — el comando referencia un <c>EquipoId</c> que no existe en
/// el catálogo local <c>EquipoLocal</c>. Sucede cuando el sync M-3b no se ejecutó,
/// o el equipo fue dado de baja en el ERP entre la apertura de la app y el
/// envío del comando. Mapea a <c>404 Not Found</c> en la capa HTTP (spec §9).
/// </summary>
public sealed class EquipoNoEncontradoException(string mensaje)
    : InspeccionDomainException(mensaje);
