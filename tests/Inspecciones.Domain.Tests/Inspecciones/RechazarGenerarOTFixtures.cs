using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;
using static Inspecciones.Domain.Tests.Inspecciones.GenerarOTFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1l — RechazarGenerarOT.
/// Reutiliza <see cref="GenerarOTFixtures"/> para los streams base (inspección firmada).
/// Timestamp <see cref="AhoraRechazo"/> posterior al de aprobación.
/// </summary>
internal static class RechazarGenerarOTFixtures
{
    /// <summary>Timestamp del handler de RechazarGenerarOT (2026-05-08T15:00:00Z — fijado en spec §6.1).</summary>
    public static readonly DateTimeOffset AhoraRechazo =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Motivo de rechazo válido del happy path §6.1 (≥10 chars, spec exacto).</summary>
    public const string MotivoHappyPath = "El equipo será dado de baja definitiva en 10 días";

    /// <summary>Motivo de rechazo válido del happy path §6.2 (presupuesto).</summary>
    public const string MotivoPresupuesto = "Presupuesto no disponible hasta el próximo trimestre";

    // ── Constructores de comandos ─────────────────────────────────────────────

    /// <summary>
    /// Comando happy path §6.1: aprobador "jefe.campo.01" con capability "generar-ot",
    /// motivo suficientemente largo.
    /// </summary>
    public static RechazarGenerarOT ComandoRechazarOTUrgente(
        Guid? inspeccionId = null,
        string rechazadoPor = "jefe.campo.01",
        string motivo = MotivoHappyPath) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: motivo,
            RechazadoPor: rechazadoPor,
            Capabilities: new[] { "generar-ot" });

    /// <summary>
    /// Comando happy path §6.2: aprobador "supervisor.01", dictamen ConRestriccion.
    /// </summary>
    public static RechazarGenerarOT ComandoRechazarOTSupervisor(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: MotivoPresupuesto,
            RechazadoPor: "supervisor.01",
            Capabilities: new[] { "generar-ot" });

    /// <summary>
    /// Comando con capability incorrecta (§6.3 PRE-1 — se valida en middleware HTTP).
    /// </summary>
    public static RechazarGenerarOT ComandoSinCapabilityRechazarOT(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: "Motivo de ejemplo suficientemente largo",
            RechazadoPor: "carlos.ruiz",
            Capabilities: new[] { "ejecutar-inspeccion" });

    /// <summary>Comando con motivo demasiado corto (§6.4 PRE-3 — "Corto").</summary>
    public static RechazarGenerarOT ComandoMotivoCorto(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: "Corto",
            RechazadoPor: "jefe.campo.01",
            Capabilities: new[] { "generar-ot" });

    /// <summary>Comando con motivo solo espacios (§6.5 PRE-3 — trim da longitud 0).</summary>
    public static RechazarGenerarOT ComandoMotivoSoloEspacios(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: "   ",
            RechazadoPor: "jefe.campo.01",
            Capabilities: new[] { "generar-ot" });

    /// <summary>Comando con motivo exacto de 10 chars (borde inferior válido).</summary>
    public static RechazarGenerarOT ComandoMotivoBordeMinimo(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: "1234567890",       // exactamente 10 chars — válido
            RechazadoPor: "jefe.campo.01",
            Capabilities: new[] { "generar-ot" });

    /// <summary>Comando con motivo de 9 chars (borde inferior inválido).</summary>
    public static RechazarGenerarOT ComandoMotivoNueveChars(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: "123456789",        // 9 chars — inválido (< 10)
            RechazadoPor: "jefe.campo.01",
            Capabilities: new[] { "generar-ot" });

    /// <summary>
    /// Comando de segundo rechazo (§6.11 PRE-7): aprobador distinto, motivo nuevo.
    /// </summary>
    public static RechazarGenerarOT ComandoSegundoRechazo(
        Guid? inspeccionId = null) =>
        new(
            InspeccionId: inspeccionId ?? InspeccionIdNueva,
            Motivo: "Segundo intento de rechazo innecesario",
            RechazadoPor: "jefe.campo.02",
            Capabilities: new[] { "generar-ot" });

    // ── Streams de Given ──────────────────────────────────────────────────────
    // Nota: StreamEnEjecucion, StreamFirmadoConSoloHallazgoSeguimiento,
    // StreamFirmadoConHallazgoIntervencionEliminado y StreamFirmadoConOTYaSolicitada
    // se reusan de GenerarOTFixtures (mismo stream — no duplicar).
    // StreamCerradaSinOT se reusa de GenerarOTFixtures también.

    /// <summary>
    /// Stream para §6.7 PRE-4 variante: inspección ya cerrada CerradaSinOT por rechazo explícito.
    /// A diferencia del GenerarOTFixtures.StreamCerradaSinOT (cierre automático/saga),
    /// este usa MotivoCierre=RechazadaPorAprobador para el test de §6.7 de este slice.
    /// </summary>
    public static object[] StreamCerradaSinOTPorRechazo()
    {
        var h1 = HallazgoG1;
        return
        [
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdNueva,
                Tipo: TipoInspeccion.Tecnica,
                EquipoId: 42,
                RutinaId: 18,
                RutinaCodigo: "INSP. BULL.MOTOR",
                TecnicoIniciador: "carlos.ruiz",
                ProyectoId: 3,
                Ubicacion: UbicacionTipo(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null),
            HallazgoRegistradoEjemplo(
                hallazgoId: h1,
                accionRequerida: AccionRequerida.NoRequiereIntervencion,
                emitidoPor: "carlos.ruiz"),
            new InspeccionCerradaSinOT_v1(
                InspeccionId: InspeccionIdNueva,
                MotivoCierre: MotivoCierreSinOT.RechazadaPorAprobador,
                CerradaEn: Ahora),
        ];
    }

    /// <summary>
    /// Stream para §6.11 PRE-7 aislado: inspección firmada con GeneracionOTRechazada_v1
    /// pero SIN InspeccionCerradaSinOT_v1 subsiguiente (stream hipotéticamente inconsistente).
    /// Reproduce el único escenario donde PRE-7 (!OTRechazada) puede disparar antes que PRE-4.
    /// </summary>
    public static object[] StreamFirmadoConOTRechazadaSinCierre()
    {
        var baseStream = StreamFirmadoNoPuedeOperar();
        var otRechazada = new GeneracionOTRechazada_v1(
            InspeccionId: InspeccionIdNueva,
            Motivo: "Primer rechazo — presupuesto agotado para este trimestre",
            RechazadoPor: "gerente.01",
            RechazadaEn: AhoraOT.AddMinutes(-60));
        return [.. baseStream, otRechazada];
    }

    /// <summary>
    /// Stream para §6.11 variante normal: inspección con rechazo completo (dos eventos).
    /// Aggregate.Estado == CerradaSinOT — PRE-4 intercepta antes que PRE-7.
    /// </summary>
    public static object[] StreamRechazadoCompleto()
    {
        var baseStream = StreamFirmadoNoPuedeOperar();
        var otRechazada = new GeneracionOTRechazada_v1(
            InspeccionId: InspeccionIdNueva,
            Motivo: "Rechazo inicial por baja definitiva planificada",
            RechazadoPor: "gerente.01",
            RechazadaEn: AhoraRechazo.AddMinutes(-30));
        var cerradaSinOT = new InspeccionCerradaSinOT_v1(
            InspeccionId: InspeccionIdNueva,
            MotivoCierre: MotivoCierreSinOT.RechazadaPorAprobador,
            CerradaEn: AhoraRechazo.AddMinutes(-30));
        return [.. baseStream, otRechazada, cerradaSinOT];
    }
}
