using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests de <c>RegistrarHallazgo</c> (slice 1c).
/// Incluye constructores de comandos, eventos de ejemplo y streams de estado.
/// Los tests parten de estos defaults y sobrescriben solo lo que afirman.
/// </summary>
internal static class HallazgoFixtures
{
    public static readonly Guid HallazgoG1 = new("0193b4f7-1234-7abc-8def-000000000011");
    public static readonly Guid HallazgoG2 = new("0193b4f7-1234-7abc-8def-000000000022");

    // ── Streams de Given ──────────────────────────────────────────────────

    /// <summary>Stream con solo <see cref="InspeccionIniciada_v1"/> — estado EnEjecucion.</summary>
    public static object[] StreamConInspeccionIniciada() =>
        [EventoInspeccionIniciada()];

    /// <summary>
    /// Evento de inicio de inspección para usar directamente en streams de rebuild.
    /// Reutiliza datos de los fixtures del slice 1a.
    /// </summary>
    public static InspeccionIniciada_v1 EventoInspeccionIniciada() =>
        new(InspeccionId: InspeccionIdNueva,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: 4521,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: "rmartinez",
            ProyectoId: 3,
            Ubicacion: UbicacionTipo(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null);

    /// <summary>
    /// Stream que simula una inspección en estado Firmada.
    /// Usa <see cref="InspeccionFirmada_v1"/> (stub del dominio añadido en slice 1c
    /// para soportar el test PRE-3 antes de que exista el slice FirmarInspeccion).
    /// </summary>
    public static object[] StreamConInspeccionFirmada() =>
        [EventoInspeccionIniciada(),
         new InspeccionFirmada_v1(InspeccionIdNueva, Ahora, "rmartinez")];

    /// <summary>
    /// Stream que simula una inspección en estado Cancelada.
    /// </summary>
    public static object[] StreamConInspeccionCancelada() =>
        [EventoInspeccionIniciada(),
         new InspeccionCancelada_v1(InspeccionIdNueva, Ahora, "rmartinez", "Cancelada por el técnico")];

    // ── Constructores de comandos ─────────────────────────────────────────

    /// <summary>Comando happy path: Origen=Manual, AccionRequerida=NoRequiereIntervencion.</summary>
    public static RegistrarHallazgo ComandoManualSinIntervencion(
        Guid? hallazgoId = null,
        int parteEquipoId = 77,
        int? novedadPreopOrigenId = null,
        string novedadTecnica = "Manguera con desgaste leve superficial",
        string actividadDescripcion = "Revisión visual de manguera",
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            Origen: OrigenHallazgo.Manual,
            ParteEquipoId: parteEquipoId,
            NovedadPreopOrigenId: novedadPreopOrigenId,
            ActividadId: null,
            ActividadDescripcion: actividadDescripcion,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: emitidoPor);

    /// <summary>Comando: Origen=PreOperacional, AccionRequerida=RequiereIntervencion.</summary>
    public static RegistrarHallazgo ComandoPreopConIntervencion(
        Guid? hallazgoId = null,
        int parteEquipoId = 88,
        int? novedadPreopOrigenId = 1042,
        int? actividadId = 55,
        string novedadTecnica = "Fuga confirmada en sello hidráulico",
        string? accionCorrectiva = "Reemplazar sello hidráulico y rellenar aceite",
        int? tipoFallaId = 3,
        int? causaFallaId = 12,
        UbicacionGps? ubicacion = null,
        string? observacionCampo = null,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG2,
            Origen: OrigenHallazgo.PreOperacional,
            ParteEquipoId: parteEquipoId,
            NovedadPreopOrigenId: novedadPreopOrigenId,
            ActividadId: actividadId,
            ActividadDescripcion: null,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: accionCorrectiva,
            TipoFallaId: tipoFallaId,
            CausaFallaId: causaFallaId,
            ObservacionCampo: observacionCampo,
            Ubicacion: ubicacion,
            EmitidoPor: emitidoPor);

    /// <summary>Comando: Origen=Manual, AccionRequerida=RequiereSeguimiento (sin tipo/causa — I-H5).</summary>
    public static RegistrarHallazgo ComandoManualConSeguimiento(
        int parteEquipoId = 77,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            Origen: OrigenHallazgo.Manual,
            ParteEquipoId: parteEquipoId,
            NovedadPreopOrigenId: null,
            ActividadId: null,
            ActividadDescripcion: "Inspección de desgaste",
            NovedadTecnica: "Desgaste progresivo, requiere monitoreo",
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: emitidoPor);

    /// <summary>Comando: Origen=Manual, AccionRequerida=RequiereIntervencion — para tests de I-H4/PRE-8.</summary>
    public static RegistrarHallazgo ComandoManualConIntervencion(
        int parteEquipoId = 77,
        int? tipoFallaId = 3,
        int? causaFallaId = 12,
        string? accionCorrectiva = "Reemplazar componente dañado",
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            Origen: OrigenHallazgo.Manual,
            ParteEquipoId: parteEquipoId,
            NovedadPreopOrigenId: null,
            ActividadId: null,
            ActividadDescripcion: "Inspección de componente",
            NovedadTecnica: "Componente con daño severo",
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: accionCorrectiva,
            TipoFallaId: tipoFallaId,
            CausaFallaId: causaFallaId,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: emitidoPor);

    /// <summary>Comando con Origen arbitrario — para tests de PRE-10.</summary>
    public static RegistrarHallazgo ComandoConOrigen(OrigenHallazgo origen) =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            Origen: origen,
            ParteEquipoId: 77,
            NovedadPreopOrigenId: origen == OrigenHallazgo.PreOperacional ? 1042 : null,
            ActividadId: null,
            ActividadDescripcion: "descripción",
            NovedadTecnica: "novedad técnica",
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: "ana.gomez");

    // ── Eventos de ejemplo para Given ────────────────────────────────────

    /// <summary>Evento <see cref="HallazgoRegistrado_v1"/> de ejemplo para poblamiento del stream.</summary>
    public static HallazgoRegistrado_v1 HallazgoRegistradoEjemplo(
        Guid? hallazgoId = null,
        int parteEquipoId = 77,
        OrigenHallazgo origen = OrigenHallazgo.Manual,
        int? novedadPreopOrigenId = null,
        AccionRequerida accionRequerida = AccionRequerida.NoRequiereIntervencion,
        int? tipoFallaId = null,
        int? causaFallaId = null,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            Origen: origen,
            NovedadPreopOrigenId: novedadPreopOrigenId,
            ParteEquipoId: parteEquipoId,
            ActividadId: null,
            ActividadDescripcion: "descripción de ejemplo",
            NovedadTecnica: "novedad técnica de ejemplo",
            AccionRequerida: accionRequerida,
            AccionCorrectiva: accionRequerida == AccionRequerida.RequiereIntervencion
                ? "acción correctiva" : null,
            TipoFallaId: tipoFallaId,
            CausaFallaId: causaFallaId,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: emitidoPor,
            RegistradoEn: Ahora);

    // ── Fixtures slice 2 — ActualizarHallazgo ────────────────────────────

    /// <summary>
    /// Stream con inspección iniciada + un hallazgo Manual/NoRequiereIntervencion.
    /// Base del Given para la mayoría de escenarios de ActualizarHallazgo.
    /// </summary>
    public static object[] StreamConUnHallazgoManual(
        Guid? hallazgoId = null,
        AccionRequerida accionRequerida = AccionRequerida.NoRequiereIntervencion,
        int? tipoFallaId = null,
        int? causaFallaId = null) =>
        StreamConInspeccionIniciada()
            .Append(HallazgoRegistradoEjemplo(
                hallazgoId: hallazgoId ?? HallazgoG1,
                parteEquipoId: 77,
                origen: OrigenHallazgo.Manual,
                accionRequerida: accionRequerida,
                tipoFallaId: tipoFallaId,
                causaFallaId: causaFallaId))
            .ToArray();

    /// <summary>
    /// Stream con inspección iniciada + hallazgo PreOperacional.
    /// </summary>
    public static object[] StreamConUnHallazgoPreop(
        Guid? hallazgoId = null,
        AccionRequerida accionRequerida = AccionRequerida.RequiereIntervencion,
        int? tipoFallaId = 7,
        int? causaFallaId = 3) =>
        StreamConInspeccionIniciada()
            .Append(HallazgoRegistradoEjemplo(
                hallazgoId: hallazgoId ?? HallazgoG2,
                parteEquipoId: 88,
                origen: OrigenHallazgo.PreOperacional,
                novedadPreopOrigenId: 99,
                accionRequerida: accionRequerida,
                tipoFallaId: tipoFallaId,
                causaFallaId: causaFallaId))
            .ToArray();

    /// <summary>
    /// Stream con inspección firmada + un hallazgo — para test PRE-2 de ActualizarHallazgo.
    /// </summary>
    public static object[] StreamFirmadaConUnHallazgo(Guid? hallazgoId = null) =>
        [
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(hallazgoId: hallazgoId ?? HallazgoG1),
            new InspeccionFirmada_v1(InspeccionIdNueva, Ahora, "rmartinez")
        ];

    /// <summary>
    /// Stream con inspección iniciada + hallazgo + hallazgo eliminado — para test PRE-4.
    /// </summary>
    public static object[] StreamConHallazgoEliminado(Guid? hallazgoId = null) =>
        [
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(hallazgoId: hallazgoId ?? HallazgoG1),
            new HallazgoEliminado_v1(InspeccionIdNueva, hallazgoId ?? HallazgoG1, Ahora, "rmartinez")
        ];

    // ── Constructores de comandos ActualizarHallazgo ──────────────────────

    /// <summary>
    /// Comando <c>ActualizarHallazgo</c> happy path: AccionRequerida=RequiereIntervencion.
    /// Escenario 6.1 del spec.
    /// </summary>
    public static ActualizarHallazgo ComandoActualizarConIntervencion(
        Guid? hallazgoId = null,
        string novedadTecnica = "Fisura en bloque motor",
        string accionCorrectiva = "Reemplazar bloque",
        int tipoFallaId = 10,
        int causaFallaId = 5,
        string actualizadoPor = "tecnico-01") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: accionCorrectiva,
            TipoFallaId: tipoFallaId,
            CausaFallaId: causaFallaId,
            ObservacionCampo: null,
            Ubicacion: null,
            ActualizadoPor: actualizadoPor);

    /// <summary>
    /// Comando <c>ActualizarHallazgo</c> happy path: AccionRequerida=RequiereSeguimiento.
    /// Escenario 6.2 del spec.
    /// </summary>
    public static ActualizarHallazgo ComandoActualizarConSeguimiento(
        Guid? hallazgoId = null,
        string novedadTecnica = "Vibración leve en eje",
        string actualizadoPor = "tecnico-02") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG2,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            ActualizadoPor: actualizadoPor);

    /// <summary>
    /// Comando <c>ActualizarHallazgo</c> mínimo para tests de validación.
    /// </summary>
    public static ActualizarHallazgo ComandoActualizarMinimo(
        Guid? hallazgoId = null,
        string novedadTecnica = "Novedad corregida",
        AccionRequerida accionRequerida = AccionRequerida.NoRequiereIntervencion,
        string? accionCorrectiva = null,
        int? tipoFallaId = null,
        int? causaFallaId = null,
        string actualizadoPor = "tecnico-01") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: accionRequerida,
            AccionCorrectiva: accionCorrectiva,
            TipoFallaId: tipoFallaId,
            CausaFallaId: causaFallaId,
            ObservacionCampo: null,
            Ubicacion: null,
            ActualizadoPor: actualizadoPor);
}

