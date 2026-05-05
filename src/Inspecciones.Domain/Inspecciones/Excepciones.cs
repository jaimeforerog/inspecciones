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
