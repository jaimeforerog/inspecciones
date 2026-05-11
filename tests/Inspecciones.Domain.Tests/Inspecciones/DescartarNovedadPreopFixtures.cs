using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1n — DescartarNovedadPreop.
/// Agregan datos de novedades preop sobre la base de los fixtures existentes.
/// NovedadId usa valores > 9000 para distinguirlos de otras entidades ERP en los tests.
/// </summary>
internal static class DescartarNovedadPreopFixtures
{
    /// <summary>NovedadId de ejemplo (PK ERP int) — para happy path y escenarios PRE-5/PRE-6.</summary>
    public const int NovedadId9001 = 9001;

    /// <summary>NovedadId alternativo — para test §6.10 (verificación de plantilla exacta).</summary>
    public const int NovedadId9002 = 9002;

    /// <summary>NovedadId arbitrario nunca importado — para escenario PRE-7 D-2 (skip).</summary>
    public const int NovedadIdDesconocida = 9999;

    // ── Streams de Given ──────────────────────────────────────────────────────

    /// <summary>
    /// Stream base: solo <see cref="InspeccionIniciada_v1"/>.
    /// Estado EnEjecucion, TecnicoIniciador="ana.gomez", ProyectoId=3.
    /// </summary>
    public static object[] StreamEnEjecucionBase(
        string tecnicoId = "ana.gomez",
        int proyectoId = 3) =>
    [
        new InspeccionIniciada_v1(
            InspeccionId: InspeccionIdNueva,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: 42,
            RutinaId: 18,
            RutinaCodigo: "INSP. BULL.MOTOR",
            TecnicoIniciador: tecnicoId,
            ProyectoId: proyectoId,
            Ubicacion: UbicacionTipo(),
            IniciadaEn: Ahora,
            FechaReportada: DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null),
    ];

    /// <summary>
    /// Stream con inspección EnEjecucion + novedad ya descartada (NovedadId=9001).
    /// Punto de partida para el escenario §6.4 (PRE-5 — doble descarte).
    /// </summary>
    public static object[] StreamConNovedadYaDescartada(
        int novedadId = NovedadId9001,
        string descartadaPor = "ana.gomez",
        string motivoDescarte = "Cerrado por ana.gomez el 2026-05-10 09:00 UTC desde Inspecciones") =>
    [
        .. StreamEnEjecucionBase(tecnicoId: "ana.gomez"),
        new NovedadPreopDescartada_v1(
            InspeccionId: InspeccionIdNueva,
            NovedadId: novedadId,
            MotivoDescarte: motivoDescarte,
            DescartadaPor: descartadaPor,
            DescartadaEn: Ahora.AddHours(-1)),
    ];

    /// <summary>
    /// Stream con inspección EnEjecucion + hallazgo con Origen=PreOperacional y
    /// NovedadPreopOrigenId=9001. Para escenario §6.5 (PRE-6 — novedad ya convertida
    /// en hallazgo, INV-ND1).
    /// </summary>
    public static object[] StreamConHallazgoPreopConNovedadImportada(
        int novedadPreopOrigenId = NovedadId9001) =>
    [
        .. StreamEnEjecucionBase(tecnicoId: "ana.gomez"),
        new HallazgoRegistrado_v1(
            InspeccionId: InspeccionIdNueva,
            HallazgoId: HallazgoG1,
            Origen: OrigenHallazgo.PreOperacional,
            NovedadPreopOrigenId: novedadPreopOrigenId,
            MedicionOrigenId: null,
            EvaluacionOrigenId: null,
            ParteEquipoId: 88,
            ActividadId: 55,
            ActividadDescripcion: null,
            NovedadTecnica: "Fuga confirmada en sello hidráulico",
            AccionRequerida: AccionRequerida.RequiereIntervencion,
            AccionCorrectiva: "Reemplazar sello hidráulico y rellenar aceite",
            TipoFallaId: 3,
            CausaFallaId: 12,
            ObservacionCampo: null,
            Ubicacion: null,
            EmitidoPor: "ana.gomez",
            RegistradoEn: Ahora),
    ];

    // ── Constructores de comandos ─────────────────────────────────────────────

    /// <summary>
    /// Comando happy path del slice 1n.
    /// NovedadId=9001, DescartadaPor="ana.gomez".
    /// </summary>
    public static DescartarNovedadPreop ComandoDescartarNovedad(
        Guid? inspeccionId = null,
        int novedadId = NovedadId9001,
        string descartadaPor = "ana.gomez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            NovedadId: novedadId,
            DescartadaPor: descartadaPor);
}
