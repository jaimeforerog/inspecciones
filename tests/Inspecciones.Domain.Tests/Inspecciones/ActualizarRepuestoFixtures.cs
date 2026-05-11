using Inspecciones.Domain.Inspecciones;
using static Inspecciones.Domain.Tests.Inspecciones.Fixtures;
using static Inspecciones.Domain.Tests.Inspecciones.HallazgoFixtures;

namespace Inspecciones.Domain.Tests.Inspecciones;

/// <summary>
/// Fixtures reusables para los tests del slice 1o — ActualizarRepuesto.
/// Todos los IDs y valores por defecto están alineados con los escenarios §6.1..§6.14 del spec.
/// </summary>
internal static class ActualizarRepuestoFixtures
{
    // ── Timestamps de fixture ──────────────────────────────────────────────────

    /// <summary>Timestamp T0 — momento de asignación del repuesto.</summary>
    public static readonly DateTimeOffset T0 =
        new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);

    /// <summary>Timestamp T1 — primera actualización (§6.4).</summary>
    public static readonly DateTimeOffset T1 =
        new(2026, 5, 8, 14, 30, 0, TimeSpan.Zero);

    /// <summary>Timestamp AhoraActualizar — usado en los tests happy path de §6.1..§6.3.</summary>
    public static readonly DateTimeOffset AhoraActualizar =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    // ── Stream base §6.1 — inspección EnEjecucion con un repuesto activo R1/G1 ──

    /// <summary>
    /// Stream base para §6.1..§6.3 y §6.10..§6.11 y §6.13.
    /// Contiene:
    ///   1. InspeccionIniciada_v1 (EnEjecucion)
    ///   2. HallazgoRegistrado_v1 (G1, RequiereIntervencion, parte 77)
    ///   3. RepuestoEstimado_v1 (R1, G1, SkuId=501, Cantidad=1, Justificacion="Cambio rutinario")
    /// </summary>
    public static object[] StreamBaseConRepuesto() =>
        [
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                parteEquipoId: 77,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            RepuestoEstimadoConJustificacion(
                hallazgoId: HallazgoG1,
                repuestoId: RepuestoR1,
                skuId: 501,
                cantidad: 1m,
                justificacion: "Cambio rutinario"),
        ];

    /// <summary>
    /// Stream §6.4 — incluye un primer RepuestoActualizado_v1(Cantidad=2, Justificacion=null, T1)
    /// sobre el stream base.
    /// </summary>
    public static object[] StreamConPrimeraActualizacion() =>
        [
            .. StreamBaseConRepuesto(),
            new RepuestoActualizado_v1(
                InspeccionId: InspeccionIdNueva,
                HallazgoId: HallazgoG1,
                RepuestoId: RepuestoR1,
                Cantidad: 2m,
                Justificacion: null,
                ActualizadoPor: "rmartinez",
                ActualizadoEn: T1),
        ];

    /// <summary>
    /// Stream §6.7 — hallazgo G2 eliminado con repuesto R2 en él.
    /// </summary>
    public static object[] StreamConHallazgoEliminadoYRepuesto() =>
        [
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                parteEquipoId: 33,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            new HallazgoEliminado_v1(
                InspeccionId: InspeccionIdNueva,
                HallazgoId: HallazgoG2,
                Motivo: "Error de registro",
                EliminadoPor: "rmartinez",
                EliminadoEn: T0),
            RepuestoEstimadoConJustificacion(
                hallazgoId: HallazgoG2,
                repuestoId: RepuestoR2,
                skuId: 502,
                cantidad: 1m,
                justificacion: null),
        ];

    /// <summary>
    /// Stream §6.9 — dos hallazgos (G1, G2) con repuesto R1 en G1.
    /// </summary>
    public static object[] StreamConDosHallazgosYRepuestoEnG1() =>
        [
            EventoInspeccionIniciada(),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG1,
                parteEquipoId: 77,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            HallazgoRegistradoEjemplo(
                hallazgoId: HallazgoG2,
                parteEquipoId: 33,
                accionRequerida: AccionRequerida.RequiereIntervencion,
                tipoFallaId: 3,
                causaFallaId: 12),
            RepuestoEstimadoConJustificacion(
                hallazgoId: HallazgoG1,
                repuestoId: RepuestoR1,
                skuId: 501,
                cantidad: 1m,
                justificacion: null),
        ];

    // ── Constructores de comandos ──────────────────────────────────────────────

    /// <summary>Comando happy path §6.1 — solo actualiza Cantidad (ObservacionNueva=null).</summary>
    public static ActualizarRepuesto ComandoActualizarSoloCantidad(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        Guid? repuestoId = null,
        decimal cantidadNueva = 2m,
        string actualizadoPor = "rmartinez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            RepuestoId: repuestoId ?? RepuestoR1,
            CantidadNueva: cantidadNueva,
            ObservacionNueva: null,
            ActualizadoPor: actualizadoPor);

    /// <summary>Comando happy path §6.2 — solo actualiza Observacion (CantidadNueva=null).</summary>
    public static ActualizarRepuesto ComandoActualizarSoloObservacion(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        Guid? repuestoId = null,
        string observacion = "Filtro doble en este modelo de motor",
        string actualizadoPor = "rmartinez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            RepuestoId: repuestoId ?? RepuestoR1,
            CantidadNueva: null,
            ObservacionNueva: observacion,
            ActualizadoPor: actualizadoPor);

    /// <summary>Comando happy path §6.3 — actualiza ambos campos.</summary>
    public static ActualizarRepuesto ComandoActualizarAmbos(
        Guid? inspeccionId = null,
        Guid? hallazgoId = null,
        Guid? repuestoId = null,
        decimal cantidadNueva = 3m,
        string observacion = "Revisión extendida, se necesitan 3",
        string actualizadoPor = "jperez") =>
        new(InspeccionId: inspeccionId ?? InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            RepuestoId: repuestoId ?? RepuestoR1,
            CantidadNueva: cantidadNueva,
            ObservacionNueva: observacion,
            ActualizadoPor: actualizadoPor);

    /// <summary>Comando vacío (ambos campos null) — dispara PRE-8.</summary>
    public static ActualizarRepuesto ComandoSinCambios(
        Guid? hallazgoId = null,
        Guid? repuestoId = null) =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            RepuestoId: repuestoId ?? RepuestoR1,
            CantidadNueva: null,
            ObservacionNueva: null,
            ActualizadoPor: "rmartinez");

    // ── Constructores de eventos de ejemplo ───────────────────────────────────

    /// <summary>
    /// Variante de <see cref="HallazgoFixtures.RepuestoEstimadoEjemplo"/> que acepta
    /// <c>justificacion</c> posicional, para simplificar la construcción de streams del slice 1o.
    /// </summary>
    public static RepuestoEstimado_v1 RepuestoEstimadoConJustificacion(
        Guid? hallazgoId = null,
        Guid? repuestoId = null,
        int skuId = 501,
        string skuCodigo = "INS-501",
        decimal cantidad = 1m,
        string? justificacion = "Cambio rutinario",
        string unidad = "unidad",
        string asignadoPor = "rmartinez") =>
        new(InspeccionId: InspeccionIdNueva,
            HallazgoId: hallazgoId ?? HallazgoG1,
            RepuestoId: repuestoId ?? RepuestoR1,
            SkuId: skuId,
            SkuCodigo: skuCodigo,
            Cantidad: cantidad,
            Justificacion: justificacion,
            Unidad: unidad,
            AsignadoPor: asignadoPor,
            AsignadoEn: T0);
}
