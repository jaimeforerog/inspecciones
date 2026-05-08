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

    // ── Slice 1h — IniciarInspeccionMonitoreo ────────────────────────────────
    /// <summary>Id de la rutina de monitoreo elegida. Null cuando Tipo=Tecnica.</summary>
    public int? RutinaMonitoreoSeleccionadaId { get; private set; }
    /// <summary>Snapshot inmutable de items activos al momento de iniciar. Null cuando Tipo=Tecnica.</summary>
    public IReadOnlyList<ItemRutinaMonitoreoSnapshot>? ItemsSnapshot { get; private set; }

    // ── Slice 1i — RegistrarMedicion ─────────────────────────────────────────
    /// <summary>Ítems del snapshot que ya recibieron medición en esta inspección (I-M6).</summary>
    public IReadOnlySet<int> ItemsMedidos => _itemsMedidos;
    private readonly HashSet<int> _itemsMedidos = [];

    /// <summary>Ítems del snapshot que fueron omitidos en esta inspección (I-M4).
    /// Inicialmente vacío hasta que exista el slice OmitirItemMonitoreo.
    /// Este slice lee el set para validar PRE-6 / I-M4.</summary>
    public IReadOnlySet<int> ItemsOmitidos => _itemsOmitidos;
    private readonly HashSet<int> _itemsOmitidos = [];

    // ── Slice 1g — FirmarInspeccion ──────────────────────────────────────────
    public string? DiagnosticoFinal { get; private set; }
    public DictamenOperacion? Dictamen { get; private set; }
    public string? FirmaUri { get; private set; }
    public UbicacionGps? UbicacionFirma { get; private set; }
    public DateTimeOffset? FirmadaEn { get; private set; }

    /// <summary>Hallazgos registrados en esta inspección (I-H6: multiplicidad permitida).</summary>
    public IReadOnlyList<Hallazgo> Hallazgos => _hallazgos.AsReadOnly();
    private readonly List<Hallazgo> _hallazgos = [];

    /// <summary>Repuestos estimados en esta inspección (slice 1f — AsignarRepuesto).</summary>
    public IReadOnlyList<Repuesto> Repuestos => _repuestos.AsReadOnly();
    private readonly List<Repuesto> _repuestos = [];

    /// <summary>
    /// Adjuntos activos (no eliminados) por hallazgo. Clave = HallazgoId.
    /// Necesario para PRE-6 del slice 1g (FirmarInspeccion): la firma exige
    /// ≥1 adjunto activo por hallazgo con RequiereIntervencion.
    /// </summary>
    private readonly Dictionary<Guid, HashSet<Guid>> _adjuntosPorHallazgo = [];

    /// <summary>Técnicos que han contribuido eventos al stream (I2b — derivado, sin evento propio).</summary>
    public IReadOnlySet<string> Contribuyentes => _contribuyentes;
    private readonly HashSet<string> _contribuyentes = [];

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
    /// Método de decisión del slice 1h. Valida PRE-8 (I-I2 defensa en
    /// profundidad) y PRE-9 (I-I3 FechaReportada) en el aggregate. Las demás
    /// pre-condiciones (PRE-3..PRE-7) se validan en el handler antes de
    /// llamar aquí. El handler pasa los <paramref name="itemsSnapshot"/>
    /// ya filtrados (solo activos) y ordenados.
    /// </summary>
    public static IReadOnlyList<object> IniciarMonitoreo(
        IniciarInspeccionMonitoreo cmd,
        ClaimsTecnico claims,
        string rutinaNombre,
        IReadOnlyList<ItemRutinaMonitoreoSnapshot> itemsSnapshot,
        DateTimeOffset ahora)
    {
        // PRE-8 (I-I2 defensa en profundidad) — el proyecto del comando debe estar entre los asignados.
        if (!claims.ProyectosAsignados.Contains(cmd.ProyectoId))
        {
            throw new ProyectoNoAutorizadoException(
                $"El técnico {claims.TecnicoIniciador} no tiene asignación al proyecto {cmd.ProyectoId}.");
        }

        // PRE-9 (I-I3) — FechaReportada debe estar en rango [hoy-30, hoy].
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);
        var limiteInferior = hoy.AddDays(-30);
        if (cmd.FechaReportada > hoy)
        {
            throw new FechaReportadaFueraDeRangoException(
                $"FechaReportada {cmd.FechaReportada:yyyy-MM-dd} no puede ser futura. Fecha hoy: {hoy:yyyy-MM-dd}.");
        }

        if (cmd.FechaReportada < limiteInferior)
        {
            throw new FechaReportadaFueraDeRangoException(
                $"FechaReportada {cmd.FechaReportada:yyyy-MM-dd} excede la ventana de 30 días retroactivos. Mínimo aceptable: {limiteInferior:yyyy-MM-dd}.");
        }

        var evento = new InspeccionIniciada_v1(
            InspeccionId: cmd.InspeccionId,
            Tipo: TipoInspeccion.Monitoreo,
            EquipoId: cmd.EquipoId,
            RutinaId: cmd.RutinaMonitoreoId,
            RutinaCodigo: rutinaNombre,
            TecnicoIniciador: cmd.IniciadaPor,
            ProyectoId: cmd.ProyectoId,
            Ubicacion: cmd.Ubicacion,
            IniciadaEn: ahora,
            FechaReportada: cmd.FechaReportada,
            LecturaMedidorPrimario: cmd.LecturaMedidorPrimario,
            LecturaMedidorSecundario: cmd.LecturaMedidorSecundario,
            RutinaMonitoreoSeleccionadaId: cmd.RutinaMonitoreoId,
            ItemsSnapshot: itemsSnapshot);

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

    /// <summary>
    /// Método de decisión del slice 1c. Valida las pre-condiciones en el orden
    /// definido en spec §4 y emite <see cref="HallazgoRegistrado_v1"/>.
    /// Apply es puro — las validaciones viven aquí, nunca en Apply.
    /// </summary>
    public IReadOnlyList<object> RegistrarHallazgo(RegistrarHallazgo cmd, DateTimeOffset ahora)
    {
        // PRE-3: estado debe ser EnEjecucion.
        if (Estado != EstadoInspeccion.EnEjecucion)
        {
            throw new InspeccionNoEnEjecucionException(
                $"La inspección está en estado '{Estado}'. Solo se pueden registrar hallazgos en estado EnEjecucion.");
        }

        // PRE-10: Origen debe ser Manual o PreOperacional.
        if (cmd.Origen != OrigenHallazgo.Manual && cmd.Origen != OrigenHallazgo.PreOperacional)
        {
            throw new OrigenNoSoportadoException(
                $"Origen '{cmd.Origen}' aún no soportado. Se implementa en slice posterior.");
        }

        // PRE-5 / I-H2: PreOperacional requiere NovedadPreopOrigenId.
        if (cmd.Origen == OrigenHallazgo.PreOperacional && cmd.NovedadPreopOrigenId is null)
        {
            throw new NovedadPreopOrigenIdRequeridoException(
                "NovedadPreopOrigenId es obligatorio cuando Origen=PreOperacional.");
        }

        // PRE-6 / I-H3: Manual prohíbe NovedadPreopOrigenId.
        if (cmd.Origen == OrigenHallazgo.Manual && cmd.NovedadPreopOrigenId is not null)
        {
            throw new NovedadPreopOrigenIdNoPermitidoException(
                "NovedadPreopOrigenId debe ser null cuando Origen=Manual.");
        }

        // PRE-7 / I-H4: RequiereIntervencion exige TipoFallaId y CausaFallaId.
        if (cmd.AccionRequerida == AccionRequerida.RequiereIntervencion
            && (cmd.TipoFallaId is null || cmd.CausaFallaId is null))
        {
            throw new TipoYCausaFallaRequeridosException(
                "TipoFallaId y CausaFallaId son obligatorios cuando AccionRequerida=RequiereIntervencion.");
        }

        // PRE-8: RequiereIntervencion exige AccionCorrectiva no vacía.
        if (cmd.AccionRequerida == AccionRequerida.RequiereIntervencion
            && string.IsNullOrWhiteSpace(cmd.AccionCorrectiva))
        {
            throw new AccionCorrectivaRequeridaException(
                "AccionCorrectiva es obligatoria cuando AccionRequerida=RequiereIntervencion.");
        }

        // PRE-9: NovedadTecnica no puede ser vacía.
        if (string.IsNullOrWhiteSpace(cmd.NovedadTecnica))
        {
            throw new NovedadTecnicaVaciaException(
                "NovedadTecnica es obligatoria y no puede estar vacía.");
        }

        var evento = new HallazgoRegistrado_v1(
            InspeccionId: InspeccionId,
            HallazgoId: cmd.HallazgoId,
            Origen: cmd.Origen,
            NovedadPreopOrigenId: cmd.NovedadPreopOrigenId,
            MedicionOrigenId: null,  // Slice 1i: null para orígenes Manual/PreOperacional
            ParteEquipoId: cmd.ParteEquipoId,
            ActividadId: cmd.ActividadId,
            ActividadDescripcion: cmd.ActividadDescripcion,
            NovedadTecnica: cmd.NovedadTecnica,
            AccionRequerida: cmd.AccionRequerida,
            AccionCorrectiva: cmd.AccionCorrectiva,
            TipoFallaId: cmd.TipoFallaId,
            CausaFallaId: cmd.CausaFallaId,
            ObservacionCampo: cmd.ObservacionCampo,
            Ubicacion: cmd.Ubicacion,
            EmitidoPor: cmd.EmitidoPor,
            RegistradoEn: ahora);

        return new object[] { evento };
    }

    private void AplicarEvento(object evento)
    {
        switch (evento)
        {
            case InspeccionIniciada_v1 e:
                Apply(e);
                break;
            case HallazgoRegistrado_v1 e:
                Apply(e);
                break;
            case InspeccionFirmada_v1 e:
                Apply(e);
                break;
            case InspeccionCancelada_v1 e:
                Apply(e);
                break;
            case HallazgoActualizado_v1 e:
                Apply(e);
                break;
            case HallazgoEliminado_v1 e:
                Apply(e);
                break;
            case RepuestoEstimado_v1 e:
                Apply(e);
                break;
            case DiagnosticoEmitido_v1 e:
                Apply(e);
                break;
            case DictamenEstablecido_v1 e:
                Apply(e);
                break;
            case AdjuntoSubido_v1 e:
                Apply(e);
                break;
            case AdjuntoEliminado_v1 e:
                Apply(e);
                break;
            // Slice 1i — RegistrarMedicion
            case MedicionRegistrada_v1 e:
                Apply(e);
                break;
            case ItemMonitoreoOmitido_v1 e:
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
        // Slice 1h — campos de monitoreo (null cuando Tipo=Tecnica — backward compat).
        RutinaMonitoreoSeleccionadaId = e.RutinaMonitoreoSeleccionadaId;
        ItemsSnapshot = e.ItemsSnapshot;
        _contribuyentes.Add(e.TecnicoIniciador);
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="HallazgoRegistrado_v1"/>: añade el
    /// hallazgo a la lista y actualiza contribuyentes. Sin validaciones — solo
    /// mutación de estado. Las pre-condiciones viven en <see cref="RegistrarHallazgo"/>.
    /// </summary>
    public void Apply(HallazgoRegistrado_v1 e)
    {
        _hallazgos.Add(new Hallazgo(
            HallazgoId: e.HallazgoId,
            Origen: e.Origen,
            ParteEquipoId: e.ParteEquipoId,
            NovedadPreopOrigenId: e.NovedadPreopOrigenId,
            NovedadTecnica: e.NovedadTecnica,
            AccionRequerida: e.AccionRequerida,
            AccionCorrectiva: e.AccionCorrectiva,
            TipoFallaId: e.TipoFallaId,
            CausaFallaId: e.CausaFallaId,
            UbicacionGps: e.Ubicacion,
            Eliminado: false,
            MotivoEliminacion: null,
            MedicionOrigenId: e.MedicionOrigenId));  // Slice 1i — propagado desde el evento
        _contribuyentes.Add(e.EmitidoPor);
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="InspeccionFirmada_v1"/>: transiciona
    /// el estado a <see cref="EstadoInspeccion.Firmada"/> y persiste los campos de
    /// firma. Apply es puro — sin validaciones. Las pre-condiciones viven en
    /// <see cref="Firmar"/>. Slice 1g — FirmarInspeccion.
    /// </summary>
    public void Apply(InspeccionFirmada_v1 e)
    {
        Estado = EstadoInspeccion.Firmada;
        FirmaUri = e.FirmaUri;
        UbicacionFirma = e.UbicacionFirma;
        FirmadaEn = e.FirmadaEn;
        _contribuyentes.Add(e.FirmadoPor);
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="InspeccionCancelada_v1"/>: transiciona
    /// el estado a <see cref="EstadoInspeccion.Cancelada"/>. Stub para soporte de
    /// tests PRE-3 del slice 1c.
    /// </summary>
    public void Apply(InspeccionCancelada_v1 e)
    {
        Estado = EstadoInspeccion.Cancelada;
        if (e.CanceladoPor is not null)
        {
            _contribuyentes.Add(e.CanceladoPor);
        }
    }

    // ── Slice 1d — ActualizarHallazgo ────────────────────────────────────────

    /// <summary>
    /// Método de decisión del slice 1d. Valida las pre-condiciones en el orden
    /// definido en spec §4 y emite <see cref="HallazgoActualizado_v1"/>.
    /// Apply es puro — las validaciones viven aquí, nunca en Apply.
    /// </summary>
    public IReadOnlyList<object> ActualizarHallazgo(ActualizarHallazgo cmd, DateTimeOffset ahora)
    {
        // PRE-A: estado debe ser EnEjecucion (I-H7).
        if (Estado != EstadoInspeccion.EnEjecucion)
        {
            throw new InspeccionNoEnEjecucionException(
                $"La inspección está en estado '{Estado}'. Solo se pueden actualizar hallazgos en estado EnEjecucion.");
        }

        // PRE-B1 + PRE-B2: el hallazgo debe existir y no estar eliminado.
        var hallazgo = ObtenerHallazgoActivo(cmd.HallazgoId);

        // PRE-C: NovedadTecnica no puede ser vacía.
        if (string.IsNullOrWhiteSpace(cmd.NovedadTecnica))
        {
            throw new NovedadTecnicaVaciaException(
                "NovedadTecnica es obligatoria y no puede estar vacía.");
        }

        // PRE-E: campos de intervención no permitidos cuando AccionRequerida != RequiereIntervencion.
        if (cmd.AccionRequerida != AccionRequerida.RequiereIntervencion
            && (cmd.AccionCorrectiva is not null || cmd.TipoFallaId is not null || cmd.CausaFallaId is not null))
        {
            throw new CamposIntervencionNoPermitidosException(
                $"AccionCorrectiva, TipoFallaId y CausaFallaId solo se permiten cuando AccionRequerida=RequiereIntervencion.");
        }

        // PRE-D1: RequiereIntervencion exige TipoFallaId y CausaFallaId.
        if (cmd.AccionRequerida == AccionRequerida.RequiereIntervencion
            && (cmd.TipoFallaId is null || cmd.CausaFallaId is null))
        {
            throw new TipoYCausaFallaRequeridosException(
                "TipoFallaId y CausaFallaId son obligatorios cuando AccionRequerida=RequiereIntervencion.");
        }

        // PRE-D2: RequiereIntervencion exige AccionCorrectiva no vacía.
        if (cmd.AccionRequerida == AccionRequerida.RequiereIntervencion
            && string.IsNullOrWhiteSpace(cmd.AccionCorrectiva))
        {
            throw new AccionCorrectivaRequeridaException(
                "AccionCorrectiva es obligatoria cuando AccionRequerida=RequiereIntervencion.");
        }

        var evento = new HallazgoActualizado_v1(
            InspeccionId: InspeccionId,
            HallazgoId: cmd.HallazgoId,
            NovedadTecnica: cmd.NovedadTecnica,
            AccionRequerida: cmd.AccionRequerida,
            AccionCorrectiva: cmd.AccionCorrectiva,
            TipoFallaId: cmd.TipoFallaId,
            CausaFallaId: cmd.CausaFallaId,
            ObservacionCampo: cmd.ObservacionCampo,
            UbicacionGps: cmd.UbicacionGps,
            ActualizadoEn: ahora,
            EmitidoPor: cmd.EmitidoPor);

        return new object[] { evento };
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="HallazgoActualizado_v1"/>: actualiza
    /// los campos mutables del hallazgo en la lista. Sin validaciones — solo
    /// mutación de estado. Las pre-condiciones viven en <see cref="ActualizarHallazgo"/>.
    /// </summary>
    public void Apply(HallazgoActualizado_v1 e)
    {
        var idx = _hallazgos.FindIndex(h => h.HallazgoId == e.HallazgoId);
        if (idx < 0)
        {
            return; // rebuild con gaps — puro, no lanza.
        }

        _hallazgos[idx] = _hallazgos[idx] with
        {
            NovedadTecnica = e.NovedadTecnica,
            AccionRequerida = e.AccionRequerida,
            AccionCorrectiva = e.AccionCorrectiva,
            TipoFallaId = e.TipoFallaId,
            CausaFallaId = e.CausaFallaId,
            UbicacionGps = e.UbicacionGps,
            MotivoEliminacion = _hallazgos[idx].MotivoEliminacion,
        };
        _contribuyentes.Add(e.EmitidoPor);
    }

    // ── Slice 1e — EliminarHallazgo ──────────────────────────────────────────

    /// <summary>
    /// Método de decisión del slice 1e. Valida las pre-condiciones en el orden
    /// definido en spec §4 y emite <see cref="HallazgoEliminado_v1"/>.
    /// Apply es puro — las validaciones viven aquí, nunca en Apply.
    /// </summary>
    public IReadOnlyList<object> EliminarHallazgo(EliminarHallazgo cmd, DateTimeOffset ahora)
    {
        // PRE-A: estado debe ser EnEjecucion (I-H7, I-F1).
        if (Estado != EstadoInspeccion.EnEjecucion)
        {
            throw new InspeccionNoEnEjecucionException(
                $"La inspección está en estado '{Estado}'. Solo se pueden eliminar hallazgos en estado EnEjecucion.");
        }

        // PRE-B1 + PRE-B2: el hallazgo debe existir y no estar eliminado.
        ObtenerHallazgoActivo(cmd.HallazgoId);

        // PRE-C: Motivo no puede ser null, vacío ni solo whitespace.
        if (string.IsNullOrWhiteSpace(cmd.Motivo))
        {
            throw new MotivoEliminacionVacioException(
                "Motivo de eliminación es obligatorio.");
        }

        // PRE-D / I-H9: el hallazgo no puede tener repuestos activos (slice 1f).
        if (_repuestos.Any(r => r.HallazgoId == cmd.HallazgoId))
        {
            throw new HallazgoTieneHijosActivosException(
                $"El hallazgo {cmd.HallazgoId} tiene repuestos o adjuntos activos. Elimínalos antes de eliminar el hallazgo.");
        }

        var evento = new HallazgoEliminado_v1(
            InspeccionId: InspeccionId,
            HallazgoId: cmd.HallazgoId,
            Motivo: cmd.Motivo,
            EliminadoPor: cmd.TecnicoId,
            EliminadoEn: ahora);

        return new object[] { evento };
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="HallazgoEliminado_v1"/>: marca el hallazgo
    /// como eliminado y persiste el motivo. Sin validaciones — solo mutación de estado.
    /// Las pre-condiciones viven en <see cref="EliminarHallazgo"/>.
    /// </summary>
    public void Apply(HallazgoEliminado_v1 e)
    {
        var idx = _hallazgos.FindIndex(h => h.HallazgoId == e.HallazgoId);
        if (idx < 0)
        {
            return; // rebuild con gaps — puro, no lanza.
        }

        _hallazgos[idx] = _hallazgos[idx] with { Eliminado = true, MotivoEliminacion = e.Motivo };
        _contribuyentes.Add(e.EliminadoPor);
    }

    // ── Slice 1f — AsignarRepuesto ───────────────────────────────────────────

    /// <summary>
    /// Método de decisión del slice 1f. Valida las pre-condiciones en el orden
    /// definido en spec §4 y emite <see cref="RepuestoEstimado_v1"/>.
    /// Apply es puro — las validaciones viven aquí, nunca en Apply.
    /// </summary>
    public IReadOnlyList<object> AsignarRepuesto(
        AsignarRepuesto cmd,
        string skuCodigo,
        string unidad,
        DateTimeOffset ahora)
    {
        // PRE-D (idempotencia): si el RepuestoId ya existe, retorno silencioso.
        // Se evalúa antes de PRE-A para que los retries no fallen por estado cambiado.
        if (_repuestos.Any(r => r.RepuestoId == cmd.RepuestoId))
        {
            return Array.Empty<object>();
        }

        // PRE-A: estado debe ser EnEjecucion (I-H7).
        if (Estado != EstadoInspeccion.EnEjecucion)
        {
            throw new InspeccionNoEnEjecucionException(
                $"La inspección está en estado '{Estado}'. Solo se pueden asignar repuestos en estado EnEjecucion.");
        }

        // PRE-B1 + PRE-B2: el hallazgo debe existir y no estar eliminado.
        var hallazgo = ObtenerHallazgoActivo(cmd.HallazgoId);

        // PRE-C: el hallazgo debe tener AccionRequerida=RequiereIntervencion (I-H12).
        if (hallazgo.AccionRequerida != AccionRequerida.RequiereIntervencion)
        {
            throw new HallazgoNoRequiereIntervencionException(
                $"Solo se pueden asignar repuestos a hallazgos con AccionRequerida=RequiereIntervencion. " +
                $"El hallazgo {cmd.HallazgoId} tiene AccionRequerida={hallazgo.AccionRequerida}.");
        }

        // PRE-E: Cantidad debe ser mayor que cero.
        if (cmd.Cantidad <= 0)
        {
            throw new CantidadInvalidaException(
                "Cantidad debe ser mayor que cero.");
        }

        // PRE-G: el mismo SkuId no puede estar ya asignado al mismo hallazgo con distinto RepuestoId.
        if (_repuestos.Any(r => r.HallazgoId == cmd.HallazgoId && r.SkuId == cmd.SkuId && r.RepuestoId != cmd.RepuestoId))
        {
            throw new SkuDuplicadoEnHallazgoException(
                $"El SKU {cmd.SkuId} ya fue estimado en el hallazgo {cmd.HallazgoId}. " +
                "Edita o elimina el repuesto existente antes de volver a agregar.");
        }

        var evento = new RepuestoEstimado_v1(
            InspeccionId: InspeccionId,
            HallazgoId: cmd.HallazgoId,
            RepuestoId: cmd.RepuestoId,
            SkuId: cmd.SkuId,
            SkuCodigo: skuCodigo,
            Cantidad: cmd.Cantidad,
            Justificacion: cmd.Justificacion,
            Unidad: unidad,
            AsignadoPor: cmd.TecnicoId,
            AsignadoEn: ahora);

        return new object[] { evento };
    }

    /// <summary>
    /// Aplicación pura del evento <see cref="RepuestoEstimado_v1"/>: añade el
    /// repuesto a la colección <see cref="_repuestos"/> y registra el contribuyente.
    /// Sin validaciones — solo mutación de estado.
    /// Las pre-condiciones viven en <see cref="AsignarRepuesto"/>.
    /// </summary>
    public void Apply(RepuestoEstimado_v1 e)
    {
        _repuestos.Add(new Repuesto(
            RepuestoId: e.RepuestoId,
            HallazgoId: e.HallazgoId,
            SkuId: e.SkuId,
            SkuCodigo: e.SkuCodigo,
            Cantidad: e.Cantidad,
            Justificacion: e.Justificacion,
            Unidad: e.Unidad));
        _contribuyentes.Add(e.AsignadoPor);
    }

    // ── Slice 1g — FirmarInspeccion ──────────────────────────────────────────

    /// <summary>
    /// Método de decisión del slice 1g. Valida las pre-condiciones PRE-2..PRE-9
    /// (en ese orden) y emite los tres eventos en orden causal:
    /// <see cref="DiagnosticoEmitido_v1"/> → <see cref="DictamenEstablecido_v1"/> →
    /// <see cref="InspeccionFirmada_v1"/>. Apply es puro — las validaciones viven aquí.
    /// PRE-1 (capability) y PRE-4 (diagnóstico no vacío) los verifica la capa de handler.
    /// </summary>
    public IReadOnlyList<object> Firmar(FirmarInspeccion cmd, DateTimeOffset ahora)
    {
        // PRE-2 — estado debe ser EnEjecucion (V-F7).
        if (Estado != EstadoInspeccion.EnEjecucion)
        {
            throw new InspeccionNoEnEjecucionException(
                $"La inspección está en estado '{Estado}'. Solo se puede firmar en estado EnEjecucion.");
        }

        // PRE-3 — al menos un hallazgo vigente (no eliminado) (V-F1).
        var hallazgosVigentes = _hallazgos.Where(h => !h.Eliminado).ToList();
        if (hallazgosVigentes.Count == 0)
        {
            throw new SinHallazgosException(
                "La inspección no tiene hallazgos vigentes. Registra al menos un hallazgo antes de firmar.");
        }

        // PRE-7 — FirmaUri no vacío (V-F5).
        if (string.IsNullOrWhiteSpace(cmd.FirmaUri))
        {
            throw new FirmaRequeridaException(
                "FirmaUri es obligatorio. El cliente debe subir la imagen de firma antes de invocar este comando.");
        }

        // PRE-8 — UbicacionFirma no nula (V-F6).
        if (cmd.UbicacionFirma is null)
        {
            throw new GpsRequeridoException(
                "UbicacionFirma es obligatoria. El GPS debe recapturarse al momento de firmar.");
        }

        // PRE-9 — el firmante debe ser contribuyente del stream (P-2 confirmado).
        if (!_contribuyentes.Contains(cmd.TecnicoId))
        {
            throw new TecnicoNoContribuyenteException(
                $"El técnico '{cmd.TecnicoId}' no es contribuyente de esta inspección. Solo los contribuyentes pueden firmar.");
        }

        // PRE-5 — coherencia dictamen ↔ hallazgos (V-F8).
        // PuedeOperar solo es válido cuando ningún hallazgo vigente requiere seguimiento o intervención.
        if (cmd.Dictamen == DictamenOperacion.PuedeOperar)
        {
            var conSeguimientoOIntervencion = hallazgosVigentes
                .Where(h => h.AccionRequerida == AccionRequerida.RequiereSeguimiento
                         || h.AccionRequerida == AccionRequerida.RequiereIntervencion)
                .ToList();

            if (conSeguimientoOIntervencion.Count > 0)
            {
                var tieneSeguimiento = conSeguimientoOIntervencion.Any(h => h.AccionRequerida == AccionRequerida.RequiereSeguimiento);
                var tieneIntervencion = conSeguimientoOIntervencion.Any(h => h.AccionRequerida == AccionRequerida.RequiereIntervencion);
                var motivo = (tieneSeguimiento, tieneIntervencion) switch
                {
                    (true, true)  => "seguimiento e intervención",
                    (true, false) => "seguimiento",
                    _             => "intervención"
                };
                throw new DictamenIncoherenteException(
                    $"No puedes firmar con dictamen 'Apto'. Hay hallazgos que requieren {motivo}. " +
                    "Selecciona 'Con restricciones' o 'No apto'.");
            }
        }

        // PRE-6 — hallazgos con RequiereIntervencion deben tener TipoFallaId, CausaFallaId y ≥1 adjunto activo (V-F3).
        foreach (var hallazgo in hallazgosVigentes.Where(h => h.AccionRequerida == AccionRequerida.RequiereIntervencion))
        {
            if (hallazgo.TipoFallaId is null)
            {
                throw new HallazgoIntervencionIncompletoException(
                    $"El hallazgo {hallazgo.HallazgoId} requiere intervención pero le falta TipoFallaId.");
            }

            if (hallazgo.CausaFallaId is null)
            {
                throw new HallazgoIntervencionIncompletoException(
                    $"El hallazgo {hallazgo.HallazgoId} requiere intervención pero le falta CausaFallaId.");
            }

            if (!_adjuntosPorHallazgo.TryGetValue(hallazgo.HallazgoId, out var adjuntos) || adjuntos.Count == 0)
            {
                throw new HallazgoIntervencionIncompletoException(
                    $"El hallazgo {hallazgo.HallazgoId} requiere intervención pero no tiene al menos un adjunto de evidencia.");
            }
        }

        return new object[]
        {
            new DiagnosticoEmitido_v1(InspeccionId, cmd.Diagnostico, cmd.TecnicoId, ahora),
            new DictamenEstablecido_v1(InspeccionId, cmd.Dictamen, cmd.JustificacionDictamen, cmd.TecnicoId, ahora),
            new InspeccionFirmada_v1(InspeccionId, cmd.TecnicoId, cmd.FirmaUri, cmd.UbicacionFirma, ahora)
        };
    }

    /// <summary>
    /// Aplicación pura de <see cref="DiagnosticoEmitido_v1"/>: persiste el texto
    /// de diagnóstico. Sin validaciones — solo mutación de estado. Slice 1g.
    /// </summary>
    public void Apply(DiagnosticoEmitido_v1 e)
    {
        DiagnosticoFinal = e.DiagnosticoFinal;
        _contribuyentes.Add(e.EmitidoPor);
    }

    /// <summary>
    /// Aplicación pura de <see cref="DictamenEstablecido_v1"/>: persiste el dictamen
    /// y su justificación. Sin validaciones — solo mutación de estado. Slice 1g.
    /// </summary>
    public void Apply(DictamenEstablecido_v1 e)
    {
        Dictamen = e.Dictamen;
        _contribuyentes.Add(e.EmitidoPor);
    }

    /// <summary>
    /// Aplicación pura de <see cref="AdjuntoSubido_v1"/>: registra el adjunto como
    /// activo en el índice por hallazgo. Sin validaciones. Slice 1g (stub para PRE-6).
    /// </summary>
    public void Apply(AdjuntoSubido_v1 e)
    {
        if (!_adjuntosPorHallazgo.TryGetValue(e.HallazgoId, out var set))
        {
            set = [];
            _adjuntosPorHallazgo[e.HallazgoId] = set;
        }
        set.Add(e.AdjuntoId);
    }

    /// <summary>
    /// Aplicación pura de <see cref="AdjuntoEliminado_v1"/>: quita el adjunto del
    /// índice activo para su hallazgo. Sin validaciones. Slice 1g (stub para PRE-6 §6.13).
    /// </summary>
    public void Apply(AdjuntoEliminado_v1 e)
    {
        if (_adjuntosPorHallazgo.TryGetValue(e.HallazgoId, out var set))
        {
            set.Remove(e.AdjuntoId);
        }
    }

    // ── Slice 1i — RegistrarMedicion ─────────────────────────────────────────────

    /// <summary>
    /// Método de decisión del slice 1i. Valida las pre-condiciones PRE-3..PRE-8
    /// y emite 1 o 2 eventos (en orden causal) en un único <c>SaveChangesAsync</c>.
    /// </summary>
    public IReadOnlyList<object> RegistrarMedicion(RegistrarMedicion cmd, DateTimeOffset ahora)
    {
        // PRE-3 / I-M1: solo inspecciones de Tipo=Monitoreo.
        if (Tipo != TipoInspeccion.Monitoreo)
        {
            throw new InspeccionNoEsMonitoreoException(
                $"La inspección {InspeccionId} es de tipo {Tipo}. RegistrarMedicion solo aplica a inspecciones de Tipo=Monitoreo.");
        }

        // PRE-4 / I-M2: estado debe ser EnEjecucion.
        if (Estado != EstadoInspeccion.EnEjecucion)
        {
            throw new InspeccionNoEnEjecucionException(
                $"La inspección está en estado '{Estado}'. Solo se pueden registrar mediciones en estado EnEjecucion.");
        }

        // PRE-5 / I-M3: el ItemId debe existir en el snapshot.
        var snapshot = ItemsSnapshot?.FirstOrDefault(i => i.ItemId == cmd.ItemId);
        if (snapshot is null)
        {
            var ids = ItemsSnapshot?.Select(i => i.ItemId.ToString()) ?? [];
            throw new ItemNoEncontradoEnSnapshotException(
                $"El ítem {cmd.ItemId} no forma parte del snapshot de esta inspección. Solo pueden medirse los ítems del snapshot: [{string.Join(", ", ids)}].");
        }

        // PRE-6 / I-M4: el ítem no debe haber sido omitido.
        if (_itemsOmitidos.Contains(cmd.ItemId))
        {
            throw new ItemOmitidoNoPuedeMedirseException(
                $"El ítem {cmd.ItemId} fue omitido en esta inspección. Un ítem omitido no puede recibir medición posterior.");
        }

        // PRE-7 / I-M5: el ítem debe tener evaluación numérica (MedicionEsperada).
        if (snapshot.Evaluacion is not MedicionEsperada medicionEsperada)
        {
            throw new ItemNoEsNumericoException(
                $"El ítem {cmd.ItemId} ('{snapshot.Parte}') tiene evaluación cualitativa. Usa el comando RegistrarEvaluacionCualitativa para este ítem.");
        }

        // PRE-8 / I-M6: el ítem no debe haber sido medido previamente.
        if (_itemsMedidos.Contains(cmd.ItemId))
        {
            throw new ItemYaMedidoException(
                $"El ítem {cmd.ItemId} ya fue medido en esta inspección. Para corregir una medición, usa el comando ActualizarMedicion (disponible en slice futuro).");
        }

        // Calcular FueraDeRango — rango cerrado [ValorMinimo, ValorMaximo] (P-2).
        var fueraDeRango = cmd.ValorMedido < medicionEsperada.ValorMinimo
                           || cmd.ValorMedido > medicionEsperada.ValorMaximo;

        var eventos = new List<object>();

        // Evento 1 (siempre): MedicionRegistrada_v1.
        var evMedicion = new MedicionRegistrada_v1(
            InspeccionId: InspeccionId,
            ItemId: cmd.ItemId,
            ValorMedido: cmd.ValorMedido,
            Observacion: cmd.Observacion,
            FueraDeRango: fueraDeRango,
            EmitidoPor: cmd.EmitidoPor,
            RegistradaEn: ahora);
        eventos.Add(evMedicion);

        // Evento 2 (solo si fuera de rango): HallazgoRegistrado_v1 con Origen=Monitoreo.
        if (fueraDeRango)
        {
            // Guard I-H1: ParteEquipoId obligatorio. Si el snapshot no lo tiene (streams del slice 1h
            // creados antes de que M-16 expusiera ParteEquipoId), el hallazgo no puede generarse.
            // Followup #22: confirmar con David que M-16 expone ParteEquipoId por ítem.
            if (snapshot.ParteEquipoId is null)
            {
                throw new ParteEquipoIdAusenteEnSnapshotException(
                    $"El snapshot del ítem {cmd.ItemId} no tiene ParteEquipoId. " +
                    "El hallazgo automático requiere ParteEquipoId (I-H1). Refresha los catálogos y reinicia la inspección (followup #22).");
            }

            // NovedadTecnica autogenerada (spec §6.2 — formato exacto del test).
            var novedadTecnica =
                $"{CapitalizarPrimera(medicionEsperada.Magnitud)} {cmd.ValorMedido}{medicionEsperada.Unidad} " +
                $"fuera de rango esperado [{medicionEsperada.ValorMinimo}, {medicionEsperada.ValorMaximo}]";

            var evHallazgo = new HallazgoRegistrado_v1(
                InspeccionId: InspeccionId,
                HallazgoId: cmd.HallazgoId,
                Origen: OrigenHallazgo.Monitoreo,
                NovedadPreopOrigenId: null,
                MedicionOrigenId: cmd.ItemId,
                ParteEquipoId: snapshot.ParteEquipoId.Value,
                ActividadId: null,
                ActividadDescripcion: null,
                NovedadTecnica: novedadTecnica,
                AccionRequerida: AccionRequerida.RequiereSeguimiento,
                AccionCorrectiva: null,
                TipoFallaId: null,
                CausaFallaId: null,
                ObservacionCampo: cmd.Observacion,
                Ubicacion: null,
                EmitidoPor: cmd.EmitidoPor,
                RegistradoEn: ahora);
            eventos.Add(evHallazgo);
        }

        return eventos;
    }

    /// <summary>Capitaliza la primera letra de una cadena.</summary>
    private static string CapitalizarPrimera(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        return char.ToUpper(texto[0]) + texto[1..];
    }

    /// <summary>
    /// Aplicación pura de <see cref="MedicionRegistrada_v1"/>: añade el ítem a
    /// <see cref="_itemsMedidos"/> y registra el contribuyente. Sin validaciones.
    /// </summary>
    public void Apply(MedicionRegistrada_v1 e)
    {
        _itemsMedidos.Add(e.ItemId);
        _contribuyentes.Add(e.EmitidoPor);
    }

    /// <summary>
    /// Aplicación pura de <see cref="ItemMonitoreoOmitido_v1"/>: añade el ítem a
    /// <see cref="_itemsOmitidos"/>. Sin validaciones. Stub mínimo — el slice
    /// <c>OmitirItemMonitoreo</c> lo completará.
    /// </summary>
    public void Apply(ItemMonitoreoOmitido_v1 e)
    {
        _itemsOmitidos.Add(e.ItemId);
        _contribuyentes.Add(e.OmitidoPor);
    }

    // ── Helpers privados ─────────────────────────────────────────────────────

    /// <summary>
    /// Busca el hallazgo por <paramref name="hallazgoId"/> en el stream y verifica
    /// que no esté eliminado (PRE-B1 + PRE-B2). Usado por <see cref="ActualizarHallazgo"/>,
    /// <see cref="EliminarHallazgo"/> y <see cref="AsignarRepuesto"/> — única fuente de verdad
    /// para estas dos pre-condiciones.
    /// </summary>
    /// <exception cref="HallazgoNoEncontradoException">PRE-B1 — no existe en el stream.</exception>
    /// <exception cref="HallazgoEliminadoException">PRE-B2 — fue eliminado (soft delete).</exception>
    private Hallazgo ObtenerHallazgoActivo(Guid hallazgoId)
    {
        var hallazgo = _hallazgos.Find(h => h.HallazgoId == hallazgoId);
        if (hallazgo is null)
        {
            throw new HallazgoNoEncontradoException(
                $"El hallazgo {hallazgoId} no existe en la inspección {InspeccionId}.");
        }

        if (hallazgo.Eliminado)
        {
            throw new HallazgoEliminadoException(
                $"El hallazgo {hallazgoId} está eliminado.");
        }

        return hallazgo;
    }
}
