using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del agregado <see cref="Inspeccion"/>.
/// Datos de relleno realistas: equipo CARGADOR-EX-201, proyecto 3, rutina
/// "INSP. BULL.MOTOR". Los tests parten de estos defaults y sobrescriben
/// solo lo que cada escenario afirma específicamente.
/// </summary>
internal static class Fixtures
{
    public static readonly DateTimeOffset Ahora = new(2026, 5, 5, 8, 30, 12, TimeSpan.FromHours(-5));
    public static readonly Guid InspeccionIdNueva = new("0193a4f7-1234-7abc-8def-000000000001");

    public static UbicacionGps UbicacionTipo() =>
        new(Latitud: 4.711m, Longitud: -74.072m, PrecisionMetros: 8.5m, CapturadoEn: Ahora);

    public static ClaimsTecnico ClaimsValidos(int proyectoId = 3) =>
        new(TecnicoIniciador: "rmartinez",
            ProyectosAsignados: new HashSet<int> { proyectoId, 5 },
            TieneCapabilityEjecutarInspeccion: true);

    public static EquipoLocal EquipoConRutina(
        int equipoId = 4521,
        int proyectoId = 3,
        int? rutinaTecnicaId = 18) =>
        new(EquipoId: equipoId,
            EquipoCodigo: "CARGADOR-EX-201",
            ProyectoId: proyectoId,
            RutinaTecnicaId: rutinaTecnicaId);

    public static RutinaTecnicaLocal RutinaTecnicaTipo(int rutinaId = 18) =>
        new(RutinaId: rutinaId,
            Codigo: "INSP. BULL.MOTOR",
            Nombre: "Inspección bulldozer motor",
            Tipo: TipoRutina.Tecnica,
            GrupoMantenimiento: "BULLDOZER",
            ParteId: 88,
            ParteCodigo: "MOTOR",
            SincronizadoEn: Ahora.AddDays(-1));

    public static IniciarInspeccion ComandoTipo(
        Guid? inspeccionId = null,
        int equipoId = 4521,
        int proyectoId = 3,
        DateOnly? fechaReportada = null,
        LecturaMedidor? lecturaPrimario = null,
        LecturaMedidor? lecturaSecundario = null) =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            EquipoId: equipoId,
            ProyectoId: proyectoId,
            UbicacionInicio: UbicacionTipo(),
            FechaReportada: fechaReportada ?? DateOnly.FromDateTime(Ahora.UtcDateTime),
            LecturaMedidorPrimario: lecturaPrimario,
            LecturaMedidorSecundario: lecturaSecundario);
}
