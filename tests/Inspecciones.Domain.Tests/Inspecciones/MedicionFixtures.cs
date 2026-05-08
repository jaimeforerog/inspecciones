using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.MonitoreoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1i — RegistrarMedicion.
/// Timestamp fijo <see cref="AhoraRegistro"/> diferente al de MonitoreoFixtures
/// (inicio) para distinguir el momento del inicio del momento del registro.
/// IDs de hallazgos prefijados con "M" para distinguirlos de los del slice 1c.
/// </summary>
internal static class MedicionFixtures
{
    /// <summary>Timestamp para el handler de RegistrarMedicion (posterior al inicio).</summary>
    public static readonly DateTimeOffset AhoraRegistro =
        new(2026, 5, 8, 10, 0, 0, TimeSpan.Zero);

    // ── IDs de hallazgos automáticos generados por mediciones fuera de rango ──

    public static readonly Guid HallazgoM1 = Guid.Parse("0194a001-1111-7000-aaaa-000000000001");
    public static readonly Guid HallazgoM2 = Guid.Parse("0194a001-1111-7000-aaaa-000000000002");

    // ── Snapshot de items (reutilizado desde MonitoreoFixtures + item extra ItemId=3) ──

    /// <summary>
    /// Snapshot estándar del slice 1i: ItemId=1 numérico voltaje [12.3, 12.5],
    /// ItemId=2 cualitativo. Igual al de MonitoreoFixtures pero explícito aquí
    /// para que los tests del slice 1i sean autocontenidos.
    /// ParteEquipoId=88 en ItemId=1 (P-1 opción A — nullable backward compat).
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotConParteEquipoId() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m),
                ParteEquipoId: 88),
            new(ItemId: 2,
                Parte: "Conectores batería",
                Actividad: "Estado visual",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 77),
        };

    /// <summary>
    /// Snapshot extendido con ItemId=3 (numérico, corriente [0.9, 1.1]).
    /// Para tests §6.13 donde dos ítems pueden estar fuera de rango.
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshotConTresItems() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m),
                ParteEquipoId: 88),
            new(ItemId: 2,
                Parte: "Conectores batería",
                Actividad: "Estado visual",
                Evaluacion: new EvaluacionCualitativaEsperada(),
                ParteEquipoId: 77),
            new(ItemId: 3,
                Parte: "Alternador",
                Actividad: "Medir corriente",
                Evaluacion: new MedicionEsperada("corriente", "A", 0.9m, 1.1m),
                ParteEquipoId: 102),
        };

    // ── Constructores de comandos ─────────────────────────────────────────

    /// <summary>Construye un <see cref="RegistrarMedicion"/> con defaults razonables.</summary>
    public static RegistrarMedicion ComandoRegistrarMedicion(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        int itemId = 1,
        decimal valorMedido = 12.4m,
        string? observacion = null,
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdMonitoreo,
            HallazgoId: hallazgoId ?? HallazgoM1,
            ItemId: itemId,
            ValorMedido: valorMedido,
            Observacion: observacion,
            EmitidoPor: emitidoPor,
            Capabilities: new[] { "ejecutar-inspeccion" });

    // ── Streams de Given ──────────────────────────────────────────────────

    /// <summary>
    /// Stream básico de monitoreo en EnEjecucion con 2 ítems en el snapshot
    /// (ItemId=1 numérico, ItemId=2 cualitativo).
    /// </summary>
    public static object[] StreamMonitoreoConItems() =>
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
            ItemsSnapshot: ItemsSnapshotConParteEquipoId()),
    ];

    /// <summary>
    /// Stream de inspección Tipo=Tecnica en EnEjecucion.
    /// Usado para el escenario §6.5 (PRE-3 / I-M1).
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
    /// Para el escenario §6.6 (PRE-4 / I-M2).
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
            ItemsSnapshot: ItemsSnapshotConParteEquipoId());

        // Para poder firmar necesitamos al menos un hallazgo (V-F1) — usamos hallazgo automático
        var hallazgoEvt = HallazgoMonitoreoEjemplo(hallazgoId: HallazgoM1, itemId: 1, emitidoPor: "ana.gomez");

        var firmaEvt = new InspeccionFirmada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            FirmadoPor: "ana.gomez",
            FirmaUri: "https://blobs/firma-monitoreo.png",
            UbicacionFirma: UbicacionColombia(),
            FirmadaEn: AhoraRegistro);

        return [inicioEvt, hallazgoEvt, firmaEvt];
    }

    /// <summary>
    /// Stream de monitoreo con ItemId=1 ya omitido (ItemMonitoreoOmitido_v1).
    /// Para el escenario §6.8 (PRE-6 / I-M4).
    /// </summary>
    public static object[] StreamMonitoreoConItemOmitido(int itemIdOmitido = 1)
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
            ItemsSnapshot: ItemsSnapshotConParteEquipoId());

        var omisionEvt = new ItemMonitoreoOmitido_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: itemIdOmitido,
            MotivoOmision: "El técnico no pudo acceder al compartimento",
            OmitidoPor: "ana.gomez",
            OmitidoEn: Ahora.AddMinutes(5));

        return [inicioEvt, omisionEvt];
    }

    /// <summary>
    /// Stream de monitoreo con ItemId=1 ya medido (dentro del rango).
    /// Para el escenario §6.10 (PRE-8 / I-M6 — doble medición).
    /// </summary>
    public static object[] StreamMonitoreoConItemYaMedido(int itemId = 1, decimal valorMedido = 12.4m)
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
            ItemsSnapshot: ItemsSnapshotConParteEquipoId());

        var medicionEvt = new MedicionRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: itemId,
            ValorMedido: valorMedido,
            Observacion: null,
            FueraDeRango: false,
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraRegistro.AddMinutes(-5));

        return [inicioEvt, medicionEvt];
    }

    /// <summary>
    /// Stream de monitoreo con 3 ítems + ItemId=1 ya medido fuera de rango
    /// (con su HallazgoM1 generado). Para escenario §6.13.
    /// </summary>
    public static object[] StreamMonitoreoConDosItemsYUnoMedidoFueraDeRango()
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
            ItemsSnapshot: ItemsSnapshotConTresItems());

        var medicionFueraDeRango = new MedicionRegistrada_v1(
            InspeccionId: InspeccionIdMonitoreo,
            ItemId: 1,
            ValorMedido: 10.2m,
            Observacion: null,
            FueraDeRango: true,
            EmitidoPor: "ana.gomez",
            RegistradaEn: AhoraRegistro.AddMinutes(-10));

        var hallazgoDerivado = HallazgoMonitoreoEjemplo(
            hallazgoId: HallazgoM1,
            itemId: 1,
            parteEquipoId: 88,
            novedadTecnica: "Voltaje 10.2V fuera de rango esperado [12.3, 12.5]",
            emitidoPor: "ana.gomez");

        return [inicioEvt, medicionFueraDeRango, hallazgoDerivado];
    }

    // ── Helpers de eventos ────────────────────────────────────────────────

    /// <summary>
    /// Construye un <see cref="HallazgoRegistrado_v1"/> con Origen=Monitoreo
    /// y los campos obligatorios para hallazgos automáticos. Requiere MedicionOrigenId.
    /// </summary>
    public static HallazgoRegistrado_v1 HallazgoMonitoreoEjemplo(
        Guid? hallazgoId = null,
        int itemId = 1,
        int parteEquipoId = 88,
        string novedadTecnica = "Voltaje 10.2V fuera de rango esperado [12.3, 12.5]",
        string emitidoPor = "ana.gomez") =>
        new(InspeccionId: InspeccionIdMonitoreo,
            HallazgoId: hallazgoId ?? HallazgoM1,
            Origen: OrigenHallazgo.Monitoreo,
            NovedadPreopOrigenId: null,
            MedicionOrigenId: itemId,
            ParteEquipoId: parteEquipoId,
            ActividadId: null,
            ActividadDescripcion: null,
            NovedadTecnica: novedadTecnica,
            AccionRequerida: AccionRequerida.RequiereSeguimiento,
            AccionCorrectiva: null,
            TipoFallaId: null,
            CausaFallaId: null,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: emitidoPor,
            RegistradoEn: AhoraRegistro);
}
