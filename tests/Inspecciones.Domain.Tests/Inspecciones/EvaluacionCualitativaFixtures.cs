using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1i' — RegistrarEvaluacionCualitativa.
/// Timestamp fijo <see cref="AhoraEvaluacion"/> diferente al de MonitoreoFixtures
/// (inicio) para distinguir el momento del inicio del momento del registro.
/// IDs de hallazgos prefijados con "E" para distinguirlos de los del slice 1i (M*).
/// </summary>
internal static class EvaluacionCualitativaFixtures
{
    /// <summary>Timestamp para el handler de RegistrarEvaluacionCualitativa (posterior al inicio).</summary>
    public static readonly DateTimeOffset AhoraEvaluacion =
        new(2026, 5, 8, 14, 30, 0, TimeSpan.Zero);

    // ── IDs de hallazgos automáticos generados por calificaciones Malo ──

    public static readonly Guid HallazgoE1 = Guid.Parse("0195e001-1111-7000-aaaa-000000000001");
    public static readonly Guid HallazgoE2 = Guid.Parse("0195e001-1111-7000-aaaa-000000000002");

    // ── Snapshots de ítems ───────────────────────────────────────────────

    /// <summary>
    /// Snapshot estándar del slice 1i': ItemId=2 cualitativo con ParteEquipoId=55,
    /// Parte="Conectores batería". ItemId=1 numérico voltaje [12.3, 12.5] ParteEquipoId=88.
    /// Paralelo al ItemsSnapshotConParteEquipoId de MedicionFixtures pero con ItemId=2
    /// como el ítem cualitativo para los tests de este slice.
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotCualitativo() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m),
                ParteEquipoId: 88),
            new(ItemId: 2,
                Parte: "Conectores batería",
                Actividad: "Revisar estado",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 55),
        };

    /// <summary>
    /// Snapshot con ItemId=2 cualitativo y ParteEquipoId=null.
    /// Simula streams del slice 1h creados antes de la extensión P-1 (followup #22).
    /// Usado para el escenario 6.11 (guard I-H1 cuando Calificacion=Malo).
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotSinParteEquipoId() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 2,
                Parte: "Conectores batería",
                Actividad: "Revisar estado",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: null),
        };

    /// <summary>
    /// Snapshot extendido con ItemId=4 cualitativo adicional (ParteEquipoId=60,
    /// Parte="Mangueras hidráulicas"). Para el escenario 6.13 (múltiples ítems Malo).
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotDosItemsCualitativos() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m),
                ParteEquipoId: 88),
            new(ItemId: 2,
                Parte: "Conectores batería",
                Actividad: "Revisar estado",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 55),
            new(ItemId: 4,
                Parte: "Mangueras hidráulicas",
                Actividad: "Revisar fugas",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 60),
        };

    // ── Constructores de comandos ─────────────────────────────────────────

    /// <summary>Construye un <see cref="RegistrarEvaluacionCualitativa"/> con defaults razonables.</summary>
    public static RegistrarEvaluacionCualitativa ComandoRegistrarEvaluacion(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        int itemId = 2,
        CalificacionCualitativa calificacion = CalificacionCualitativa.Bueno,
        string? observacion = null,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdMonitoreo,
            HallazgoId: hallazgoId ?? HallazgoE1,
            ItemId: itemId,
            Calificacion: calificacion,
            Observacion: observacion,
            EmitidoPor: emitidoPor,
            Capabilities: new[] { "ejecutar-inspeccion" });

    // ── Streams de Given ──────────────────────────────────────────────────

    /// <summary>
    /// Stream básico de monitoreo en EnEjecucion con snapshot estándar.
    /// ItemId=1 numérico, ItemId=2 cualitativo con ParteEquipoId=55.
    /// </summary>
    public static object[] StreamMonitoreoConItemsCualitativos() =>
    [
        new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotCualitativo()),
    ];

    /// <summary>
    /// Stream de inspección Tipo=Tecnica en EnEjecucion.
    /// Para el escenario §6.4 (PRE-3 / I-M1).
    /// </summary>
    public static object[] StreamTecnicaEnEjecucion() =>
    [
        new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: 4521,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null),
    ];

    /// <summary>
    /// Stream de inspección Monitoreo firmada (Estado=Firmada).
    /// Para el escenario §6.5 (PRE-4 / I-M2).
    /// </summary>
    public static object[] StreamMonitoreoFirmado()
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotCualitativo());

        // Necesitamos un hallazgo para poder firmar (V-F1).
        var hallazgoEvt = HallazgoCualitativoEjemplo(
            hallazgoId: HallazgoE1,
            itemId: 2,
            emitidoPor: "ana.gomez");

        var evaluacionEvt = new EvaluacionCualitativaRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 2,
            Calificacion: CalificacionCualitativa.Malo,
            Observacion: null,
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraEvaluacion.AddMinutes(-5));

        var firmaEvt = new InspeccionFirmada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            FirmadoPor: "ana.gomez",
            FirmaUri: "https://blobs/firma-monitoreo.png",
            UbicacionFirma: UbicacionColombia(),
            FirmadaEn: AhoraEvaluacion);

        return [inicioEvt, evaluacionEvt, hallazgoEvt, firmaEvt];
    }

    /// <summary>
    /// Stream de monitoreo con ItemId=2 ya omitido (ItemMonitoreoOmitido_v1).
    /// Para el escenario §6.7 (PRE-6 / I-M4).
    /// </summary>
    public static object[] StreamMonitoreoConItemOmitido(int itemIdOmitido = 2)
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotCualitativo());

        var omisionEvt = new ItemMonitoreoOmitido_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: itemIdOmitido,
            Motivo: "El técnico no pudo acceder al compartimento",
            EmitidoPor: "ana.gomez",
            OmitidoEn: Ahora.AddMinutes(5));

        return [inicioEvt, omisionEvt];
    }

    /// <summary>
    /// Stream de monitoreo con ItemId=2 ya evaluado (EvaluacionCualitativaRegistrada_v1).
    /// Para el escenario §6.9 (PRE-8 / I-M7 — doble evaluación).
    /// </summary>
    public static object[] StreamMonitoreoConItemYaEvaluado(
        int itemId = 2,
        CalificacionCualitativa calificacion = CalificacionCualitativa.Bueno)
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotCualitativo());

        var evaluacionEvt = new EvaluacionCualitativaRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: itemId,
            Calificacion: calificacion,
            Observacion: null,
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraEvaluacion.AddMinutes(-5));

        return [inicioEvt, evaluacionEvt];
    }

    /// <summary>
    /// Stream de monitoreo con ItemId=2 ya evaluado como Malo (con su hallazgo derivado).
    /// Para el escenario §6.13 (múltiples ítems Malo).
    /// </summary>
    public static object[] StreamMonitoreoConDosItemsCualitativosYUnoEvaluadoMalo()
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotDosItemsCualitativos());

        var evaluacionEvt = new EvaluacionCualitativaRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 2,
            Calificacion: CalificacionCualitativa.Malo,
            Observacion: null,
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraEvaluacion.AddMinutes(-10));

        var hallazgoDerivado = HallazgoCualitativoEjemplo(
            hallazgoId: HallazgoE1,
            itemId: 2,
            parteEquipoId: 55,
            novedadTecnica: "Estado calificado Malo en Conectores batería",
            emitidoPor: "ana.gomez");

        return [inicioEvt, evaluacionEvt, hallazgoDerivado];
    }

    /// <summary>
    /// Stream de monitoreo con snapshot donde ItemId=2 tiene ParteEquipoId=null.
    /// Para el escenario §6.11 (guard I-H1 cuando Calificacion=Malo).
    /// </summary>
    public static object[] StreamMonitoreoConSnapshotSinParteEquipoId()
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema eléctrico",
            TecnicoIniciador: "ana.gomez",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotSinParteEquipoId());

        return [inicioEvt];
    }

    // ── Helpers de eventos ────────────────────────────────────────────────

    /// <summary>
    /// Construye un <see cref="HallazgoRegistrado_v1"/> con Origen=Monitoreo
    /// y los campos obligatorios para hallazgos automáticos cualitativos.
    /// Usa EvaluacionOrigenId=itemId (trazabilidad bidireccional hacia el ítem cualitativo).
    /// </summary>
    public static HallazgoRegistrado_v1 HallazgoCualitativoEjemplo(
        Guid? hallazgoId = null,
        int itemId = 2,
        int parteEquipoId = 55,
        string novedadTecnica = "Estado calificado Malo en Conectores batería",
        string? observacionCampo = null,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdMonitoreo,
            HallazgoId: hallazgoId ?? HallazgoE1,
            Origen: OrigenHallazgo.Monitoreo,
            NovedadPreopOrigenId: null,
            MedicionOrigenId: null,          // null para hallazgos cualitativos (no son medición)
            EvaluacionOrigenId: itemId,      // Slice 1i': ítem cualitativo origen de la calificación
            ParteEquipoId: parteEquipoId,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: observacionCampo,
            Ubicacion: null,
            EmitidoPor: emitidoPor,
            RegistradoEn: AhoraEvaluacion);
}
