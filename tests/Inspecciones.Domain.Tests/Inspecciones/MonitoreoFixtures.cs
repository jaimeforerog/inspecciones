using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1h — IniciarInspeccionMonitoreo.
/// Datos de fixture realistas: equipo 4521 BULLDOZER, grupo 7, rutina "Sistema
/// eléctrico" id=42, técnico "ana.gomez", proyecto 3. GPS plausible Colombia
/// (Latitud=4.711, Longitud=-74.072). Los tests sobrescriben solo lo que cada
/// escenario afirma específicamente.
/// </summary>
internal static class MonitoreoFixtures
{
    /// <summary>Timestamp del TimeProvider en los tests de dominio puro del slice 1h.</summary>
    public static readonly DateTimeOffset Ahora = new(2026, 5, 7, 10, 0, 0, TimeSpan.FromHours(-5));

    public static readonly Guid InspeccionIdMonitoreo =
        Guid.Parse("0193a4f7-1234-7abc-8def-000000000099");

    public static UbicacionGps UbicacionColombia() =>
        new(Latitud: 4.711m, Longitud: -74.072m, PrecisionMetros: 8.5m, CapturadoEn: Ahora);

    public static ClaimsTecnico ClaimsMonitoreo(int proyectoId = 3) =>
        new(TecnicoIniciador: "ana.gomez",
            ProyectosAsignados: new HashSet<int> { proyectoId, 5 },
            TieneCapabilityEjecutarInspeccion: true);

    public static IniciarInspeccionMonitoreo ComandoMonitoreo(
        Guid? inspeccionId = null,
        int equipoId = 4521,
        int proyectoId = 3,
        int rutinaMonitoreoId = 42,
        DateOnly? fechaReportada = null) =>
        new(InspeccionId: inspeccionId ?? InspeccionIdMonitoreo,
            EquipoId: equipoId,
            ProyectoId: proyectoId,
            RutinaMonitoreoId: rutinaMonitoreoId,
            IniciadaPor: "ana.gomez",
            Ubicacion: UbicacionColombia(),
            FechaReportada: fechaReportada ?? DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: null,
            LecturaMedidorSecundario: null,
            Capabilities: new[] { "ejecutar-inspeccion" });

    /// <summary>
    /// Dos items activos de la rutina "Sistema eléctrico" (ItemId=1 y ItemId=2).
    /// Representa el snapshot que el handler construye tras filtrar Activo=true.
    /// </summary>
    public static IReadOnlyList<ItemRutinaMonitoreoSnapshot> ItemsSnapshot() =>
        new List<ItemRutinaMonitoreoSnapshot>
        {
            new(ItemId: 1,
                Parte: "Batería",
                Actividad: "Medir voltaje",
                Evaluacion: new MedicionEsperada("voltaje", "V", 12.3m, 12.5m)),
            new(ItemId: 2,
                Parte: "Conectores",
                Actividad: "Estado visual",
                Evaluacion: new EvaluacionCualitativaEsperada()),
        };
}
