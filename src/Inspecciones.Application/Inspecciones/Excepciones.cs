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

/// <summary>
/// PRE-2 (handler slice 1c) — el comando referencia un <c>InspeccionId</c> que no
/// existe como stream en Marten. Mapea a <c>404 Not Found</c>.
/// </summary>
public sealed class InspeccionNoEncontradaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>
/// PRE-4 / INV-PartePerteneceAlEquipo (handler slice 1c) — el <c>ParteEquipoId</c>
/// no pertenece al equipo de la inspección según el catálogo local. Mapea a <c>422</c>.
/// </summary>
public sealed class ParteNoCorrespondeAlEquipoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>
/// PRE-H1 (handler slice 1f) — el <c>SkuId</c> del comando no existe en el catálogo
/// local <c>RepuestoLocal</c>. Sucede cuando el sync ADR-004 no se ejecutó o el SKU
/// fue eliminado del ERP. Mapea a <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class RepuestoNoEncontradoEnCatalogoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>
/// PRE-H2 (handler slice 1f) — el SKU referenciado no está catalogado como compatible
/// con la parte del hallazgo destino (<c>RepuestoLocal.ParteIdsCompatibles</c>).
/// Hard error per decisión §12.10.12. Mapea a <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class SkuIncompatibleConParteException(string mensaje)
    : InspeccionDomainException(mensaje);
