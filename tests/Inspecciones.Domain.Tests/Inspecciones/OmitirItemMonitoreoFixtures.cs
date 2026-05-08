using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1j — OmitirItemMonitoreo.
/// Timestamp <see cref="AhoraOmision"/> posterior al inicio para distinguir el
/// momento de inicio del momento de omisión. ItemId=3 es el ítem de referencia del
/// slice (sensor de presión hidráulica), ItemId=5 es el secundario para coexistencia.
/// </summary>
internal static class OmitirItemMonitoreoFixtures
{
    /// <summary>Timestamp del handler de OmitirItemMonitoreo (posterior al inicio).</summary>
    public static readonly DateTimeOffset AhoraOmision =
        new(2026, 5, 8, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Motivo válido del happy path §6.1 (≥10 chars, no vacío).</summary>
    public const string MotivoValido =
        "Sensor inaccesible por barro acumulado en el compartimento";

    /// <summary>Motivo con exactamente 10 chars (límite inferior válido, §6.2).</summary>
    public const string MotivoExactamente10Chars = "Sin acceso";   // 10 chars

    // ── Snapshots de ítems ────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot estándar del slice 1j: ItemId=3 numérico (sensor de presión) con
    /// ParteEquipoId=77, ItemId=5 numérico (vibración motor) con ParteEquipoId=92.
    /// Ambos son omitibles (no cualitativo ni medido).
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotOmision() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 3,
                Parte: "Sensor de presión",
                Actividad: "Medir presión hidráulica",
                Evaluacion: new MedicionEsperada("presión", "bar", 120m, 150m),
                ParteEquipoId: 77),
            new(ItemId: 5,
                Parte: "Motor principal",
                Actividad: "Medir vibración",
                Evaluacion: new MedicionEsperada("vibración", "mm/s", 0m, 4.5m),
                ParteEquipoId: 92),
        };

    /// <summary>
    /// Snapshot con solo ItemId=3 para el escenario §6.9 (I-M3: ítem 999 no existe).
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotConSoloItemId3() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 3,
                Parte: "Sensor de presión",
                Actividad: "Medir presión hidráulica",
                Evaluacion: new MedicionEsperada("presión", "bar", 120m, 150m),
                ParteEquipoId: 77),
        };

    // ── Constructores de comandos ─────────────────────────────────────────────

    /// <summary>Construye un <see cref="OmitirItemMonitoreo"/> con defaults del happy path.</summary>
    public static OmitirItemMonitoreo ComandoOmitir(
        Guid? inspeccionId = null,
        int itemId = 3,
        string motivo = MotivoValido,
        string emitidoPor = "carlos.ruiz") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdMonitoreo,
            ItemId: itemId,
            Motivo: motivo,
            EmitidoPor: emitidoPor,
            Capabilities: new[] { "ejecutar-inspeccion" });

    // ── Streams de Given ──────────────────────────────────────────────────────

    /// <summary>
    /// Stream base: inspección Monitoreo en EnEjecucion con ItemsSnapshotOmision.
    /// ItemId=3 e ItemId=5 disponibles. _itemsMedidos={}, _itemsEvaluados={}, _itemsOmitidos={}.
    /// </summary>
    public static object[] StreamMonitoreoBase() =>
    [
        new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema hidráulico",
            TecnicoIniciador: "carlos.ruiz",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotOmision()),
    ];

    /// <summary>
    /// Stream con ItemId=3 ya omitido (para §6.3 — doble omisión I-M9).
    /// </summary>
    public static object[] StreamMonitoreoConItemId3YaOmitido()
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema hidráulico",
            TecnicoIniciador: "carlos.ruiz",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotOmision());

        var omisionEvt = new ItemMonitoreoOmitido_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 3,
            Motivo: "Primera omisión: sensor descalibrado",
            EmitidoPor: "carlos.ruiz",
            OmitidoEn: AhoraOmision.AddMinutes(-30));

        return [inicioEvt, omisionEvt];
    }

    /// <summary>
    /// Stream con ItemId=3 ya medido (para §6.4 — I-M8 ítem ya medido).
    /// Usa MedicionRegistrada_v1 directamente en el stream (el Apply puro lo procesa).
    /// </summary>
    public static object[] StreamMonitoreoConItemId3YaMedido()
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema hidráulico",
            TecnicoIniciador: "carlos.ruiz",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotOmision());

        var medicionEvt = new MedicionRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 3,
            ValorMedido: 135m,
            Observacion: null,
            FueraDeRango: false,
            EmitidoPor: "carlos.ruiz",
            RegistradaEn: AhoraOmision.AddMinutes(-20));

        return [inicioEvt, medicionEvt];
    }

    /// <summary>
    /// Stream con ItemId=4 cualitativo ya evaluado (para §6.5 — I-M8 ítem ya evaluado).
    /// ItemId=4 es cualitativo con ParteEquipoId=99.
    /// </summary>
    public static object[] StreamMonitoreoConItemId4YaEvaluado()
    {
        var snapshotConCualitativo = new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 3,
                Parte: "Sensor de presión",
                Actividad: "Medir presión hidráulica",
                Evaluacion: new MedicionEsperada("presión", "bar", 120m, 150m),
                ParteEquipoId: 77),
            new(ItemId: 4,
                Parte: "Conectores hidráulicos",
                Actividad: "Revisar estado visual",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 99),
        };

        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema hidráulico",
            TecnicoIniciador: "carlos.ruiz",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: snapshotConCualitativo);

        var evaluacionEvt = new EvaluacionCualitativaRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 4,
            Calificacion: CalificacionCualitativa.Bueno,
            Observacion: null,
            EmitidoPor: "carlos.ruiz",
            RegistradaEn: AhoraOmision.AddMinutes(-15));

        return [inicioEvt, evaluacionEvt];
    }

    /// <summary>
    /// Stream de inspección Tipo=Tecnica en EnEjecucion (para §6.8 — I-M1).
    /// </summary>
    public static object[] StreamTecnicaEnEjecucion() =>
    [
        new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: 4521,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.HIDRO",
            TecnicoIniciador: "carlos.ruiz",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null),
    ];

    /// <summary>
    /// Stream Monitoreo firmado (Estado=Firmada) con hallazgo previo para poder firmar.
    /// Para §6.10 — I-M2 en estado Firmada.
    /// </summary>
    public static object[] StreamMonitoreoFirmado()
    {
        var inicioEvt = new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: 4521,
            RutinaId: 42,
            RutinaCodigo: "Sistema hidráulico",
            TecnicoIniciador: "carlos.ruiz",
            ProyectoId: 3,
            Ubicacion: UbicacionColombia(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            RutinaMonitoreoSeleccionadaId: 42,
            ItemsSnapshot: ItemsSnapshotOmision());

        // Necesitamos hallazgo para firmar (V-F1).
        var hallazgoEvt = new HallazgoRegistrado_v1(
            InspeccionId: InspeccionIdMonitoreo,
            HallazgoId: Guid.Parse("0195f001-1111-7000-bbbb-000000000001"),
            Origen: OrigenHallazgo.Manual,
            NovedadPreopOrigenId: null,
            MedicionOrigenId: null,
            EvaluacionOrigenId: null,
            ParteEquipoId: 77,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: "Fuga de aceite hidráulico detectada visualmente",
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: "carlos.ruiz",
            RegistradoEn: AhoraOmision.AddMinutes(-10));

        var firmaEvt = new InspeccionFirmada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            FirmadoPor: "carlos.ruiz",
            FirmaUri: "https://blobs/firma-monitoreo-1j.png",
            UbicacionFirma: UbicacionColombia(),
            FirmadaEn: AhoraOmision);

        return [inicioEvt, hallazgoEvt, firmaEvt];
    }

    /// <summary>
    /// Stream con ItemId=3 ya omitido e ItemId=5 libre (para §6.13 — coexistencia).
    /// </summary>
    public static object[] StreamMonitoreoConItemId3OmitidoItemId5Libre() =>
        StreamMonitoreoConItemId3YaOmitido();

    /// <summary>
    /// Stream base con solo ItemId=3 en snapshot (para §6.9 — ítem 999 inexistente).
    /// </summary>
    public static object[] StreamMonitoreoConSoloItemId3()
    {
        return
        [
            new InspeccionIniciada_v1(
                InspeccionId: InspeccionIdMonitoreo,
                Tipo: TipoInspeccion.Monitoreo,
                EquipoId: 4521,
                RutinaId: 42,
                RutinaCodigo: "Sistema hidráulico",
                TecnicoIniciador: "carlos.ruiz",
                ProyectoId: 3,
                Ubicacion: UbicacionColombia(),
                IniciadaEn: Ahora,
                FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
                LecturaMedidorPrimario: null,
                LecturaMedidorSecundario: null,
                RutinaMonitoreoSeleccionadaId: 42,
                ItemsSnapshot: ItemsSnapshotConSoloItemId3()),
        ];
    }
}
