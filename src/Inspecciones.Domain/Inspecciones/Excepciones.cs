namespace Inspecciones.Domain.Inspecciones;

/// <summary>Base de las excepciones de dominio del agregado <see cref="Inspeccion"/>.</summary>
public abstract class InspeccionDomainException(string mensaje) : Exception(mensaje);

/// <summary>PRE-1 — el técnico no tiene capability <c>ejecutar-inspeccion</c>.</summary>
public sealed class CapabilityRequeridaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-2 — el proyecto no está entre los asignados al técnico.</summary>
public sealed class ProyectoNoAutorizadoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-4 — el equipo no pertenece al proyecto del comando.</summary>
public sealed class EquipoNoPerteneceAProyectoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-5 — el equipo no tiene rutina técnica asignada en el ERP (I-I2).</summary>
public sealed class EquipoSinRutinaTecnicaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-6 — la rutina referenciada por el equipo no está en el catálogo local (I-I2).</summary>
public sealed class RutinaTecnicaNoSincronizadaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-7 — <c>FechaReportada</c> fuera del rango válido (I-I3).</summary>
public sealed class FechaReportadaFueraDeRangoException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1c — RegistrarHallazgo ──────────────────────────────────────────────

/// <summary>PRE-3 (aggregate) — la inspección no está en estado <c>EnEjecucion</c>.</summary>
public sealed class InspeccionNoEnEjecucionException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-5 / I-H2 (aggregate) — Origen=PreOperacional pero <c>NovedadPreopOrigenId</c> es null.</summary>
public sealed class NovedadPreopOrigenIdRequeridoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-6 / I-H3 (aggregate) — Origen=Manual pero <c>NovedadPreopOrigenId</c> no es null.</summary>
public sealed class NovedadPreopOrigenIdNoPermitidoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-7 / I-H4 (aggregate) — AccionRequerida=RequiereIntervencion sin <c>TipoFallaId</c> o <c>CausaFallaId</c>.</summary>
public sealed class TipoYCausaFallaRequeridosException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-8 (aggregate) — AccionRequerida=RequiereIntervencion sin <c>AccionCorrectiva</c>.</summary>
public sealed class AccionCorrectivaRequeridaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-9 (aggregate) — <c>NovedadTecnica</c> es null o vacía.</summary>
public sealed class NovedadTecnicaVaciaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-10 (aggregate) — <c>Origen</c> no soportado en este slice (Seguimiento, Monitoreo).</summary>
public sealed class OrigenNoSoportadoException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1d — ActualizarHallazgo ────────────────────────────────────────────

/// <summary>PRE-B1 (aggregate) — el <c>HallazgoId</c> no existe en el stream.</summary>
public sealed class HallazgoNoEncontradoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-B2 (aggregate) — el hallazgo existe pero fue eliminado (soft delete).</summary>
public sealed class HallazgoEliminadoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-E (aggregate) — campos de intervención (TipoFallaId, CausaFallaId, AccionCorrectiva)
/// poblados cuando <c>AccionRequerida != RequiereIntervencion</c>.</summary>
public sealed class CamposIntervencionNoPermitidosException(string mensaje)
    : InspeccionDomainException(mensaje);
