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
    public static readonly Guid HallazgoG3 = new("0193b4f7-1234-7abc-8def-000000000033");
    public static readonly Guid HallazgoG4 = new("0193b4f7-1234-7abc-8def-000000000044");
    public static readonly Guid HallazgoG5 = new("0193b4f7-1234-7abc-8def-000000000055");
    public static readonly Guid HallazgoG6 = new("0193b4f7-1234-7abc-8def-000000000066");

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

    // ── Slice 1d — Fixtures de ActualizarHallazgo ────────────────────────

    /// <summary>
    /// Stream con inspección iniciada y un hallazgo <see cref="HallazgoG1"/> registrado
    /// con AccionRequerida=NoRequiereIntervencion. Estado de partida para los tests
    /// de happy path del slice 1d.
    /// </summary>
    public static object[] StreamConHallazgoRegistrado(
        Guid? hallazgoId = null,
        AccionRequerida accionRequerida = AccionRequerida.NoRequiereIntervencion,
        int? tipoFallaId = null,
        int? causaFallaId = null,
        string? accionCorrectiva = null) =>
        [EventoInspeccionIniciada(),
         HallazgoRegistradoEjemplo(
             hallazgoId: hallazgoId ?? HallazgoG1,
             accionRequerida: accionRequerida,
             tipoFallaId: tipoFallaId,
             causaFallaId: causaFallaId)];

    /// <summary>
    /// Stream con inspección iniciada y hallazgo <see cref="HallazgoG1"/> eliminado
    /// (soft delete). Para test PRE-B2.
    /// </summary>
    public static object[] StreamConHallazgoEliminado() =>
        throw new NotImplementedException("Stub — se implementa cuando exista EliminarHallazgo_v1");

    /// <summary>Comando happy path de actualización — upgrade a RequiereIntervencion.</summary>
    public static ActualizarHallazgo ComandoActualizarConIntervencion(
        Guid? hallazgoId = null,
        string novedadTecnica = "Fuga confirmada en sello hidráulico — requiere intervención",
        string? accionCorrectiva = "Reemplazar sello hidráulico y rellenar aceite",
        int? tipoFallaId = 3,
        int? causaFallaId = 12,
        string? observacionCampo = null,
        UbicacionGps? ubicacionGps = null,
        string tecnicoId = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: accionCorrectiva,
            TipoFallaId: tipoFallaId,
            CausaFallaId: causaFallaId,
            ObservacionCampo: observacionCampo,
            UbicacionGps: ubicacionGps,
            TecnicoId: tecnicoId);

    /// <summary>Comando de actualización — downgrade a RequiereSeguimiento (limpia campos intervención).</summary>
    public static ActualizarHallazgo ComandoActualizarConSeguimiento(
        Guid? hallazgoId = null,
        string novedadTecnica = "Desgaste progresivo, requiere monitoreo continuo",
        string tecnicoId = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: null,
            TecnicoId: tecnicoId);

    /// <summary>Comando de actualización — solo texto, mantiene AccionRequerida=NoRequiereIntervencion.</summary>
    public static ActualizarHallazgo ComandoActualizarSoloTexto(
        Guid? hallazgoId = null,
        string novedadTecnica = "Manguera con desgaste leve — actualización de descripción",
        string tecnicoId = "ana.gomez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.NoRequiereIntervencion,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            UbicacionGps: null,
            TecnicoId: tecnicoId);

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
}

