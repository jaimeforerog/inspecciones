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

// ── Slice 1e — EliminarHallazgo ──────────────────────────────────────────────

/// <summary>PRE-C (aggregate) — <c>Motivo</c> de eliminación es null, vacío o solo whitespace.</summary>
public sealed class MotivoEliminacionVacioException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-D / I-H9 (aggregate) — el hallazgo tiene repuestos o adjuntos activos.</summary>
public sealed class HallazgoTieneHijosActivosException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1f — AsignarRepuesto ───────────────────────────────────────────────

/// <summary>PRE-C / I-H12 (aggregate) — el hallazgo no tiene <c>AccionRequerida=RequiereIntervencion</c>.</summary>
public sealed class HallazgoNoRequiereIntervencionException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-E (aggregate) — <c>Cantidad</c> debe ser mayor que cero.</summary>
public sealed class CantidadInvalidaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-G (aggregate) — el <c>SkuId</c> ya fue estimado en el hallazgo con distinto <c>RepuestoId</c>.</summary>
public sealed class SkuDuplicadoEnHallazgoException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1g — FirmarInspeccion ──────────────────────────────────────────────

/// <summary>PRE-4 (handler) — el campo <c>Diagnostico</c> del comando es null, vacío
/// o solo whitespace. Validado en el handler antes de cargar el aggregate. Mapea a <c>422</c>.</summary>
public sealed class DiagnosticoRequeridoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-3 / V-F1 (aggregate) — no existe ningún hallazgo vigente (no eliminado) en la inspección.</summary>
public sealed class SinHallazgosException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-5 / V-F8 (aggregate) — el dictamen elegido es PuedeOperar pero existen hallazgos que requieren seguimiento o intervención.</summary>
public sealed class DictamenIncoherenteException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-6 / V-F3 (aggregate) — un hallazgo con RequiereIntervencion le falta TipoFallaId, CausaFallaId o al menos un adjunto activo.</summary>
public sealed class HallazgoIntervencionIncompletoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-7 / V-F5 (aggregate) — <c>FirmaUri</c> es null, vacío o solo whitespace.</summary>
public sealed class FirmaRequeridaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-8 / V-F6 (aggregate) — <c>UbicacionFirma</c> es null.</summary>
public sealed class GpsRequeridoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-9 / I-F (aggregate) — el técnico que intenta firmar no es un contribuyente registrado en el stream.</summary>
public sealed class TecnicoNoContribuyenteException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1h — IniciarInspeccionMonitoreo ────────────────────────────────────
// (Las excepciones de handler PRE-3..PRE-6 viven en Inspecciones.Application.Inspecciones.Excepciones.cs)
// Las excepciones PRE-8 y PRE-9 reusan ProyectoNoAutorizadoException y FechaReportadaFueraDeRangoException
// ya definidas en este archivo (slices 1a/1b) — no se duplican.

// ── Slice 1i — RegistrarMedicion ─────────────────────────────────────────────

/// <summary>PRE-3 / I-M1 (aggregate) — <c>RegistrarMedicion</c> se invocó sobre una
/// inspección de <see cref="TipoInspeccion.Tecnica"/>. Solo es válido para Monitoreo.</summary>
public sealed class InspeccionNoEsMonitoreoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-5 / I-M3 (aggregate) — el <c>ItemId</c> no existe en el snapshot de ítems
/// capturado al iniciar la inspección de monitoreo.</summary>
public sealed class ItemNoEncontradoEnSnapshotException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-6 / I-M4 (aggregate) — el ítem fue previamente omitido
/// (<see cref="ItemMonitoreoOmitido_v1"/>); no puede recibir medición posterior.</summary>
public sealed class ItemOmitidoNoPuedeMedirseException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-7 / I-M5 (aggregate) — el ítem tiene evaluación cualitativa
/// (<see cref="EvaluacionCualitativaEsperada"/>) y no acepta <c>RegistrarMedicion</c>.
/// Usar el comando <c>RegistrarEvaluacionCualitativa</c>.</summary>
public sealed class ItemNoEsNumericoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-8 / I-M6 (aggregate) — el ítem ya fue medido en esta inspección.
/// Una sola medición por ítem por inspección. Corrección requiere <c>ActualizarMedicion</c>.</summary>
public sealed class ItemYaMedidoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>Guard I-H1 en RegistrarMedicion — el snapshot del ítem no tiene <c>ParteEquipoId</c>.
/// Ocurre cuando la inspección fue iniciada con una versión de M-16 que no expone el campo.
/// Followup #22: confirmar que M-16 expone ParteEquipoId por ítem.</summary>
public sealed class ParteEquipoIdAusenteEnSnapshotException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1i' — RegistrarEvaluacionCualitativa ───────────────────────────────

/// <summary>PRE-7 / I-M5b (aggregate) — el ítem tiene evaluación numérica
/// (<see cref="MedicionEsperada"/>) y no acepta <c>RegistrarEvaluacionCualitativa</c>.
/// Usar el comando <c>RegistrarMedicion</c> para ítems numéricos.</summary>
public sealed class ItemNoEsCualitativoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-8 / I-M7 (aggregate) — el ítem ya fue evaluado cualitativamente en
/// esta inspección. Una sola evaluación por ítem por inspección. La corrección requiere
/// el comando futuro <c>ActualizarEvaluacionCualitativa</c>. HTTP 409 Conflict.</summary>
public sealed class ItemYaEvaluadoException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1j — OmitirItemMonitoreo ───────────────────────────────────────────

/// <summary>PRE-8 / I-M8 (aggregate) — el ítem ya tiene una medición o evaluación
/// cualitativa registrada en esta inspección. Un ítem ya procesado no puede omitirse.
/// HTTP 422 Unprocessable Entity.</summary>
public sealed class ItemYaProcesadoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-9 / I-M9 (aggregate) — el ítem ya fue omitido previamente en esta
/// inspección. Doble omisión del mismo ítem no está permitida. HTTP 409 Conflict.</summary>
public sealed class ItemYaOmitidoException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-3 / PRE-4 — el motivo de omisión está vacío, es solo whitespace, o
/// tiene menos de 10 caracteres. HTTP 400 Bad Request.</summary>
public sealed class MotivoOmisionInvalidoException(string mensaje)
    : InspeccionDomainException(mensaje);

// ── Slice 1k — GenerarOT ──────────────────────────────────────────────────────

/// <summary>PRE-3 / I-F4.a (aggregate) — <c>GenerarOT</c> se invocó sobre una
/// inspección que no está en estado <see cref="EstadoInspeccion.Firmada"/>.
/// Solo es válido para inspecciones firmadas. HTTP 422 Unprocessable Entity.</summary>
public sealed class InspeccionNoFirmadaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-4 / I-F4.b (aggregate) — la inspección no tiene ningún hallazgo activo
/// (no eliminado) con <c>AccionRequerida=RequiereIntervencion</c>. GenerarOT requiere
/// al menos uno. HTTP 422 Unprocessable Entity.</summary>
public sealed class SinHallazgosConIntervencionException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-5 / I-F4.c (aggregate) — ya existe un <see cref="OTSolicitada_v1"/>
/// en el stream. No se aceptan dos solicitudes de OT sobre el mismo stream.
/// HTTP 409 Conflict.</summary>
public sealed class OTYaSolicitadaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-6 / I-F4.d (aggregate) — ya existe un <see cref="GeneracionOTRechazada_v1"/>
/// en el stream. Una vez rechazada no se puede re-solicitar en el MVP (I-F6).
/// HTTP 409 Conflict.</summary>
public sealed class OTRechazadaException(string mensaje)
    : InspeccionDomainException(mensaje);

/// <summary>PRE-7 / I-F4.e (aggregate) — el dictamen es <see cref="DictamenOperacion.PuedeOperar"/>.
/// Defensa explícita de segunda línea (V-F8 debería haberlo bloqueado al firmar).
/// Solo ConRestriccion o NoPuedeOperar permiten GenerarOT. HTTP 422 Unprocessable Entity.</summary>
public sealed class DictamenNoPermiteOTException(string mensaje)
    : InspeccionDomainException(mensaje);
