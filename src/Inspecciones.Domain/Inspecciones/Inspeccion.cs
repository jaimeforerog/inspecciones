using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;

namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Agregado raíz <c>Inspeccion</c>. Stream de eventos cuyo identificador es
/// <see cref="InspeccionId"/>. Estados según §15.7 del modelo. Reglas de
/// rebuild puro: <c>Apply</c> no valida, no lanza, solo muta estado.
/// Las pre-condiciones viven en los métodos de decisión.
/// </summary>
public sealed class Inspeccion
{
    public Guid InspeccionId { get; private set; }
    public TipoInspeccion Tipo { get; private set; }
    public EstadoInspeccion Estado { get; private set; }
    public int EquipoId { get; private set; }
    public int RutinaId { get; private set; }
    public string RutinaCodigo { get; private set; } = string.Empty;
    public string TecnicoIniciador { get; private set; } = string.Empty;
    public int ProyectoId { get; private set; }
    public UbicacionGps? Ubicacion { get; private set; }
    public DateTimeOffset IniciadaEn { get; private set; }
    public DateOnly FechaReportada { get; private set; }
    public LecturaMedidor? LecturaMedidorPrimario { get; private set; }
    public LecturaMedidor? LecturaMedidorSecundario { get; private set; }

    private Inspeccion() { }

    /// <summary>
    /// Decisión de creación del stream <c>Inspeccion</c> sobre estado vacío.
    /// Valida pre-condiciones contra el comando, claims, equipo y rutina del
    /// catálogo local. Devuelve la lista de eventos a appendear al stream.
    /// </summary>
    /// <remarks>
    /// El handler debe haber corto-circuitado antes vía I-I1 si ya existe una
    /// inspección activa para el equipo (no llega aquí en ese caso).
    /// </remarks>
    public static IReadOnlyList<object> Iniciar(
        IniciarInspeccion cmd,
        ClaimsTecnico claims,
        EquipoLocal equipo,
        RutinaTecnicaLocal rutina,
        DateTimeOffset ahora)
    {
        // PRE-2 — el proyecto del comando debe estar entre los asignados al técnico.
        if (!claims.ProyectosAsignados.Contains(cmd.ProyectoId))
        {
            throw new ProyectoNoAutorizadoException(
                $"El técnico {claims.TecnicoIniciador} no tiene asignación al proyecto {cmd.ProyectoId}.");
        }

        // PRE-4 — el equipo debe pertenecer al proyecto del comando.
        if (equipo.ProyectoId != cmd.ProyectoId)
        {
            throw new EquipoNoPerteneceAProyectoException(
                $"El equipo {equipo.EquipoCodigo} pertenece al proyecto {equipo.ProyectoId}, " +
                $"no al proyecto {cmd.ProyectoId} del comando.");
        }

        // PRE-5 — el equipo debe tener rutina técnica asignada en el ERP (I-I2).
        if (equipo.RutinaTecnicaId is null)
        {
            throw new EquipoSinRutinaTecnicaException(
                $"El equipo {equipo.EquipoCodigo} no tiene rutina técnica asignada en el ERP. " +
                "Contacta al admin del catálogo en Sinco.");
        }

        // PRE-6 — la rutina del catálogo local debe coincidir con la referenciada por el equipo
        // y ser de tipo técnica (I-I2). Defensa contra cache stale o sync inconsistente.
        if (rutina.RutinaId != equipo.RutinaTecnicaId.Value || rutina.Tipo != TipoRutina.Tecnica)
        {
            throw new RutinaTecnicaNoSincronizadaException(
                $"La rutina referenciada por el equipo {equipo.EquipoCodigo} no está sincronizada " +
                "en el catálogo local — refresca catálogos.");
        }

        // PRE-7 — FechaReportada debe estar en el rango [hoy-30, hoy] (I-I3).
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);
        var limiteInferior = hoy.AddDays(-30);
        if (cmd.FechaReportada > hoy || cmd.FechaReportada < limiteInferior)
        {
            throw new FechaReportadaFueraDeRangoException(
                $"FechaReportada {cmd.FechaReportada:yyyy-MM-dd} está fuera del rango aceptable " +
                $"[{limiteInferior:yyyy-MM-dd}, {hoy:yyyy-MM-dd}].");
        }

        var evento = new InspeccionIniciada_v1(
            InspeccionId: cmd.InspeccionId,
            Tipo: TipoInspeccion.Tecnica,
            EquipoId: cmd.EquipoId,
            RutinaId: rutina.RutinaId,
            RutinaCodigo: rutina.Codigo,
            TecnicoIniciador: claims.TecnicoIniciador,
            ProyectoId: cmd.ProyectoId,
            Ubicacion: cmd.UbicacionInicio,
            IniciadaEn: ahora,
            FechaReportada: cmd.FechaReportada,
            LecturaMedidorPrimario: cmd.LecturaMedidorPrimario,
            LecturaMedidorSecundario: cmd.LecturaMedidorSecundario);

        return new object[] { evento };
    }

    /// <summary>
    /// Reconstruye el agregado reproyectando el stream completo desde un
    /// estado vacío. Útil para tests de rebuild (§6.X de la spec) y para que
    /// Marten pueda materializar el aggregate desde <c>mt_events</c>.
    /// </summary>
    public static Inspeccion Reconstruir(IEnumerable<object> eventos)
    {
        var aggregate = new Inspeccion();
        foreach (var evento in eventos)
        {
            aggregate.AplicarEvento(evento);
        }
        return aggregate;
    }

    private void AplicarEvento(object evento)
    {
        switch (evento)
        {
            case InspeccionIniciada_v1 e:
                Apply(e);
                break;
            default:
                throw new InvalidOperationException(
                    $"Evento no soportado por Inspeccion en este slice: {evento.GetType().Name}");
        }
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="InspeccionIniciada_v1"/>: muta
    /// estado del agregado sin validar. Si validás aquí, rompés el rebuild
    /// histórico. Las pre-condiciones viven en <see cref="Iniciar"/>.
    /// </summary>
    public void Apply(InspeccionIniciada_v1 e)
    {
        InspeccionId = e.InspeccionId;
        Tipo = e.Tipo;
        Estado = EstadoInspeccion.EnEjecucion;
        EquipoId = e.EquipoId;
        RutinaId = e.RutinaId;
        RutinaCodigo = e.RutinaCodigo;
        TecnicoIniciador = e.TecnicoIniciador;
        ProyectoId = e.ProyectoId;
        Ubicacion = e.Ubicacion;
        IniciadaEn = e.IniciadaEn;
        FechaReportada = e.FechaReportada;
        LecturaMedidorPrimario = e.LecturaMedidorPrimario;
        LecturaMedidorSecundario = e.LecturaMedidorSecundario;
    }
}
