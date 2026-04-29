# Modelo de dominio — Módulo de Inspecciones Técnicas

**Fecha:** 2026-04-27 (creación) · 2026-04-28 (consolidación final en §15)
**Estado:** Modelo cerrado para MVP tras review event-by-event.
**Stack:** .NET 8+, Marten (event store sobre PostgreSQL), Wolverine (mediator + outbox) opcional pero recomendado.
**Documentos relacionados:** `00-investigacion-mercado.md` para contexto, ADRs y decisiones de integración.

> ⚠️ **IMPORTANTE — Cómo leer este documento:**
>
> La **§15 — Decisiones consolidadas (2026-04-28)** es la **fuente de verdad operativa**. Toma precedencia sobre las §2.1 a §14 si hay conflictos.
>
> Las secciones §2.1 a §14 quedan como **histórico de evolución del modelo** y pueden contener referencias a conceptos eliminados o renombrados. Específicamente:
> - `Severidad` fue eliminada del Hallazgo → reemplazada por `AccionRequerida` (§15.2).
> - `ResultadoVerificacion` fue eliminado → descartar emite `NovedadPreopDescartada_v1` (§15.4).
> - `OrigenHallazgo.Inspector` fue renombrado a `Manual`.
> - `AdjuntoAgregado_v1` fue renombrado a `AdjuntoSubido_v1`.
> - `TecnicoSeIncorporo_v1`, `NovedadPreopVerificada_v1`, `HallazgoEnRutina_v2`, `HallazgoFueraDeRutina_v2`, `HallazgoDescubierto_v1`, `InspeccionProgramada_v1` y `OTCorrectivaSugerida_v1` fueron eliminados o renombrados.
> - El catálogo MVP final es de **20 eventos** (no 16, 17 o 15 como aparece en secciones históricas).
>
> Tabla completa de referencias obsoletas en §15.11.

---

## 1. Bounded contexts

El módulo se descompone en cuatro contextos. El núcleo (Inspecciones) es event-sourced; los demás son de soporte.

```
┌──────────────────────────────────────────────────────────────────────┐
│                  Sinco Inspecciones Técnicas (sistema)               │
│                                                                      │
│  ┌──────────────────────────┐    ┌──────────────────────────────┐    │
│  │   Inspecciones (CORE)    │    │   Catálogo (SUPPORTING)      │    │
│  │   ─────────────────      │    │   ──────────────────────     │    │
│  │   - InspeccionTecnica    │◀───│  - EquipoLocal (proj)        │    │
│  │   - TipoInspeccion       │    │  - ParteLocal (proj)         │    │
│  │   - Hallazgos            │    │  - RepuestoLocal (proj)      │    │
│  │   - Eventos de dominio   │    │  Sync REST desde Sinco       │    │
│  └────────────┬─────────────┘    └──────────────────────────────┘    │
│               │                                                       │
│               ▼ outbox                                                │
│  ┌──────────────────────────┐    ┌──────────────────────────────┐    │
│  │   Integración (ACL)      │    │   Reporting (SUPPORTING)     │    │
│  │   ──────────────────     │    │   ─────────────────────      │    │
│  │  - Adapter Preoperac.    │    │  - BandejaTecnico            │    │
│  │  - Adapter MYE núcleo    │    │  - DetalleInspeccion         │    │
│  │  - Adapter Inventario    │    │  - KPIs                      │    │
│  │  Traduce eventos → REST  │    │  - HistoricoEquipo           │    │
│  └──────────────────────────┘    └──────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
```

| BC | Tipo | Responsabilidad | Persistencia |
|---|---|---|---|
| **Inspecciones** | Core | Aggregate `InspeccionTecnica`, `TipoInspeccion`. Reglas de negocio, invariantes, eventos. | Marten event store |
| **Catálogo** | Supporting | Proyecciones locales de equipos, partes, repuestos sincronizadas desde Sinco. Read-only. | Marten document store |
| **Integración** | Supporting (ACL) | Traduce eventos de dominio salientes a llamadas REST hacia Sinco on-prem. Idempotencia. | Outbox table en Postgres |
| **Reporting** | Supporting | Proyecciones para UI (bandejas, dashboards, KPIs). | Marten projections |

---

## 2. Aggregates del core

### 2.1 `InspeccionTecnica`

El aggregate central. Event-sourced. Una inspección por (equipo + rutina + técnico + momento).

> **Decisión MVP (2026-04-27):** se elimina la programación previa de inspección. El técnico llega a planta, escoge equipo y rutina, e inicia la inspección directamente. La programación (con estado `Programada`, evento `InspeccionProgramada_v1`, comando `ProgramarInspeccion`) se difiere a versión posterior — es agregable de forma puramente aditiva sin reescribir el modelo.

#### Estados

```
                    ┌─────────────┐
                    │ EnEjecucion │ ◀── crear con IniciarInspeccion (ad-hoc)
                    └──────┬──────┘
                           │ (loop) Verificar/Descubrir/Estimar/Medir/Adjuntar
                           │
                           │ Firmar (requiere diagnóstico + dictamen)
                           ▼
                    ┌─────────────┐
                    │   Firmada   │ ──▶ saga publica POST a MYE si hay intervención
                    └──────┬──────┘
                           │ (cuando integración confirma POSTs en Sinco)
                           ▼
                    ┌─────────────┐
                    │   Cerrada   │ (si MYE creó OT) o CerradaSinOT (si no aplicaba)
                    └─────────────┘

       Cancelable desde EnEjecucion ──▶ Cancelada
```

#### Estructura interna (proyectada desde el stream)

```csharp
public sealed class InspeccionTecnica
{
    // Identidad y contexto
    public Guid InspeccionId        { get; private set; }
    public Guid EquipoId            { get; private set; }
    public Guid RutinaId            { get; private set; }
    public string TecnicoIniciador  { get; private set; } = default!;
    public Guid ObraId              { get; private set; }
    public string RutinaCodigo      { get; private set; } = default!;
    public UbicacionGps UbicacionInicio { get; private set; } = default!;

    // Colaboración multi-técnico
    private readonly HashSet<string> _contribuyentes = new();
    public IReadOnlyCollection<string> TecnicosContribuyentes => _contribuyentes;

    // Ciclo de vida
    public InspeccionEstado Estado  { get; private set; }
    public DateTime IniciadaEn      { get; private set; }
    public DateTime? FirmadaEn      { get; private set; }
    public DateTime? CerradaEn      { get; private set; }
    public string? FirmadoPor       { get; private set; }       // técnico individual que firmó
    public string? FirmaUri         { get; private set; }
    public string? MotivoCancelacion { get; private set; }

    // Datos capturados
    private readonly List<Hallazgo> _hallazgos = new();
    private readonly List<RepuestoEstimado> _repuestos = new();
    private readonly List<Medicion> _mediciones = new();
    private readonly HashSet<Guid> _adjuntosEliminados = new();    // soft delete tracking
    public IReadOnlyList<Hallazgo> Hallazgos => _hallazgos;
    public IReadOnlyList<RepuestoEstimado> Repuestos => _repuestos;
    public IReadOnlyList<Medicion> Mediciones => _mediciones;
    public IReadOnlyCollection<Guid> AdjuntosEliminados => _adjuntosEliminados;

    // Cierre
    public string? DiagnosticoFinal   { get; private set; }
    public DictamenOperacion? Dictamen { get; private set; }

    // Trazabilidad de salida
    // OT correctiva en MYE — poblados solo cuando MYE confirma creación
    public Guid? OTCorrectivaIdSinco  { get; private set; }   // identificador técnico interno (Guid)
    public string? OTCorrectivaNumero { get; private set; }   // autonumérico humano de MYE (ej. "OT-123456")
}

public enum InspeccionEstado
{
    EnEjecucion, Firmada, Cerrada, Cancelada
}
```

#### Invariantes

| # | Invariante | Aplica al recibir |
|---|---|---|
| I1 | El comando `IniciarInspeccion` **crea el aggregate**. El `InspeccionId` no debe existir previamente. Equipo válido en catálogo, rutina aplicable al grupo del equipo, técnico con permisos. | `IniciarInspeccion` |
| I1b | **No puede haber dos inspecciones en estado `EnEjecucion` para el mismo equipo simultáneamente** (sin importar el técnico). Si otro técnico intenta iniciar una para un equipo ocupado, el sistema lo redirige a la existente para que se incorpore. | `IniciarInspeccion` |
| I2 | Solo se pueden agregar hallazgos / mediciones / repuestos / adjuntos en estado `EnEjecucion` | todos los comandos de captura |
| I2b | Cualquier técnico autenticado puede contribuir a una inspección `EnEjecucion`. La lista `TecnicosContribuyentes` se mantiene **derivada** automáticamente: cada `Apply` de evento de captura agrega `e.EmitidoPor` al HashSet. No hay evento dedicado de incorporación. | comandos de captura |
| I3 | Para firmar: `Estado == EnEjecucion` AND `DiagnosticoFinal != null` AND `Dictamen != null` AND firma técnica presente | `FirmarInspeccion` |
| I4 | Una novedad del preop solo se puede verificar **una vez** dentro de la inspección | `VerificarNovedadPreoperacional` |
| I5 | Cada repuesto debe pertenecer a un hallazgo existente en la inspección | `EstimarRepuesto` |
| I6 | Solo se puede cancelar en `EnEjecucion` | `CancelarInspeccion` |
| I7 | (Refinada en §12.9.4 con AccionRequerida — ver allí) | `EstablecerDictamen` |
| I8 | El técnico que firma debe ser **un técnico contribuyente** (iniciador o incorporado) o un supervisor (claim `sinco_roles` contiene `supervisor`) | `FirmarInspeccion` |
| I9-I11 | Reglas de obligatoriedad/prohibición de campos del Hallazgo según AccionRequerida — ver §12.9.4 | `RegistrarHallazgo` |

#### Comandos

```csharp
// IniciarInspeccion crea el aggregate (no transiciona desde Programada).
// El cliente genera el InspeccionId (Guid v7 recomendado) y envía todos los datos.
// La rutina técnica NO se selecciona — se deriva del grupo del equipo: una por grupo.
public sealed record IniciarInspeccion(
    Guid InspeccionId,                   // nuevo, no debe existir
    Guid EquipoId,                       // del catálogo, debe estar en obra del técnico
    string IniciadaPor,                  // del JWT (ID del técnico)
    Guid ObraId,                         // de los claims sinco_obras del JWT
    UbicacionGps Ubicacion) : ICommand;  // OBLIGATORIO — GPS del teléfono al iniciar

public sealed record VerificarNovedadPreoperacional(
    Guid InspeccionId,
    Guid NovedadPreopId,                 // referencia al sistema preop
    ResultadoVerificacion Resultado,
    string DiagnosticoEspecifico,
    string EmitidoPor) : ICommand;

public sealed record DescubrirHallazgo(
    Guid InspeccionId,
    Guid ParteId,                        // del catálogo Sinco
    string ActividadDescripcion,
    Severidad Severidad,
    string Descripcion,
    string EmitidoPor) : ICommand;

public sealed record RegistrarMedicion(
    Guid InspeccionId,
    Guid HallazgoId,                     // contexto
    string TipoMedicion,                 // p.ej. "Presión", "Temperatura"
    decimal Valor,
    string Unidad,
    string? Observacion) : ICommand;

public sealed record AdjuntarArchivo(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid AdjuntoId,
    string BlobUri,
    string MimeType,                    // image/jpeg, image/png, image/heic, image/webp, application/pdf
    int TamanoBytes,                    // máximo 3 MB (3 * 1024 * 1024)
    string Sha256,
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record EliminarAdjunto(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid AdjuntoId,
    string Motivo,                      // texto libre obligatorio
    string EmitidoPor) : ICommand;

// EstimarRepuesto: el handler deriva la Unidad del catálogo del SKU
// (no la envía el cliente — siempre debe coincidir con UnidadMedida del SKU).
public sealed record EstimarRepuesto(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid SkuId,
    decimal Cantidad,                      // > 0 obligatorio (puede ser decimal — galones, litros)
    string Justificacion,                  // texto libre obligatorio, no vacío
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record RemoverRepuestoEstimado(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    string? Motivo,                        // opcional — el técnico puede no querer justificar
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

// EditarRepuestoEstimado: solo cambia Cantidad y/o Justificacion.
// SKU, Unidad y HallazgoId no son editables — para cambiar SKU, remover y agregar.
public sealed record EditarRepuestoEstimado(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    decimal Cantidad,                      // > 0
    string Justificacion,                  // no vacía
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record EmitirDiagnostico(
    Guid InspeccionId,
    string DiagnosticoFinal,
    string EmitidoPor) : ICommand;

public sealed record EstablecerDictamen(
    Guid InspeccionId,
    DictamenOperacion Dictamen,
    string Justificacion,
    string EmitidoPor) : ICommand;

public sealed record FirmarInspeccion(
    Guid InspeccionId,
    string FirmadoPor,
    string FirmaUri) : ICommand;

public sealed record CancelarInspeccion(
    Guid InspeccionId,
    string Motivo,
    string CanceladaPor) : ICommand;
```

#### Eventos del stream `inspeccion-{id}`

```csharp
// InspeccionIniciada_v1 es el evento de creación del aggregate.
// La rutina técnica solo se referencia (no se embebe snapshot de items) porque
// su rol es filtrar el selector de partes, no determinar la ejecución.
// Las inspecciones históricas se reconstruyen sin depender de la versión de la rutina.
public sealed record InspeccionIniciada_v1(
    Guid InspeccionId,
    Guid EquipoId,
    Guid RutinaId,
    string RutinaCodigo,                        // legible para UI ("INSP. BULL.MOTOR")
    string TecnicoIniciador,
    Guid ObraId,
    UbicacionGps Ubicacion,                     // OBLIGATORIO — GPS al iniciar
    DateTime IniciadaEn);

// Nota: la lista de técnicos contribuyentes se deriva automáticamente del
// EmitidoPor de los demás eventos. NO hay evento `TecnicoSeIncorporo_v1`
// dedicado — sería redundante. Cada Apply de evento de captura agrega
// e.EmitidoPor al HashSet de contribuyentes (idempotente).

// HallazgoRegistrado_v1 es el evento ÚNICO consolidado.
// Reemplaza HallazgoEnRutina_v2, HallazgoFueraDeRutina_v2, HallazgoDescubierto_v1
// y NovedadPreopVerificada_v1 (ver §12.10.9). Cuando origen=PreOperacional,
// lleva ResultadoVerificacion para informar al ERP qué pasó con la novedad.
public sealed record HallazgoRegistrado_v1(
    Guid InspeccionId, Guid HallazgoId,
    OrigenHallazgo Origen,                          // PreOperacional | Manual
    Guid? NovedadPreopId,                           // poblado si Origen == PreOperacional (I12b)
    ResultadoVerificacion? ResultadoVerificacion,   // poblado si Origen == PreOperacional (I12c)
    Guid ParteId,                                   // siempre, validado contra rutina del aggregate
    Guid? ActividadId,                              // poblado si Origen == PreOperacional (catálogo)
    string? ActividadDescripcion,                   // poblado si Origen == Manual (texto libre)
    string NovedadTecnica,                          // texto libre obligatorio
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,                       // obligatorio solo si AccionRequerida = RequiereIntervencion
    Guid? CausaFallaId,                             // obligatorio solo si AccionRequerida = RequiereIntervencion
    Guid? TipoFallaId,                              // obligatorio solo si AccionRequerida = RequiereIntervencion
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,                        // GPS al registrar (opcional)
    string EmitidoPor, DateTime RegistradoEn);

// NOTA: NovedadPreopVerificada_v1 fue eliminado por consolidación (§12.10.9, 2026-04-27).
// Su información cabe en HallazgoRegistrado_v1 con Origen=PreOperacional + ResultadoVerificacion.
// La saga CerrarInspeccionSaga sigue haciendo POST /preop/novedades/{id}/verificar,
// pero recorre hallazgos con Origen=PreOperacional en lugar de eventos dedicados.

public sealed record MedicionRegistrada_v1(
    Guid InspeccionId, Guid HallazgoId, Guid MedicionId,
    string TipoMedicion, decimal Valor, string Unidad,
    string? Observacion, DateTime RegistradaEn);

public sealed record AdjuntoAgregado_v1(
    Guid InspeccionId, Guid HallazgoId, Guid AdjuntoId,
    string BlobUri, string MimeType, int TamanoBytes,
    string Sha256,
    UbicacionGps? Ubicacion,                          // GPS al capturar (preserva EXIF también)
    string EmitidoPor, DateTime AgregadoEn);

public sealed record AdjuntoEliminado_v1(
    Guid InspeccionId, Guid HallazgoId, Guid AdjuntoId,
    string Motivo,
    string EmitidoPor, DateTime EliminadoEn);

public sealed record RepuestoEstimadoAgregado_v1(
    Guid InspeccionId, Guid HallazgoId, Guid RepuestoEstimadoId,
    Guid SkuId, decimal Cantidad,
    string Unidad,                                    // derivada del catálogo del SKU
    string Justificacion,
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime EstimadoEn);

public sealed record RepuestoEstimadoRemovido_v1(
    Guid InspeccionId, Guid RepuestoEstimadoId,
    string? Motivo,                                   // opcional
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime RemovidoEn);

public sealed record RepuestoEstimadoActualizado_v1(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    decimal Cantidad,                                 // valor nuevo
    string Justificacion,                             // valor nuevo
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime ActualizadoEn);

public sealed record DiagnosticoEmitido_v1(
    Guid InspeccionId, string DiagnosticoFinal,
    string EmitidoPor, DateTime EmitidoEn);

public sealed record DictamenEstablecido_v1(
    Guid InspeccionId, DictamenOperacion Dictamen,
    string Justificacion, string EmitidoPor, DateTime EstablecidoEn);

public sealed record InspeccionFirmada_v1(
    Guid InspeccionId, string FirmadoPor, string FirmaUri,
    DateTime FirmadaEn);

public sealed record InspeccionCancelada_v1(
    Guid InspeccionId, string Motivo,
    string CanceladaPor, DateTime CanceladaEn);

// Se emite cuando la integración confirma que MYE creó la OT.
// Lleva tanto el ID técnico (Guid) como el número humano (autonumérico de MYE).
public sealed record InspeccionCerrada_v1(
    Guid InspeccionId,
    Guid OTCorrectivaIdSinco,           // identificador técnico interno
    string OTCorrectivaNumero,          // ej. "OT-123456" — visible al usuario
    DateTime CerradaEn);
```

#### Esquema del aplicar eventos (Marten Apply)

```csharp
public sealed class InspeccionTecnica
{
    public void Apply(InspeccionIniciada_v1 e)
    {
        InspeccionId     = e.InspeccionId;
        EquipoId         = e.EquipoId;
        RutinaId         = e.RutinaId;
        RutinaCodigo     = e.RutinaCodigo;
        TecnicoIniciador = e.TecnicoIniciador;
        ObraId           = e.ObraId;
        UbicacionInicio  = e.Ubicacion;
        IniciadaEn       = e.IniciadaEn;
        Estado           = InspeccionEstado.EnEjecucion;
        _contribuyentes.Add(e.TecnicoIniciador);
    }

    // Cada Apply de evento de captura agrega EmitidoPor al HashSet
    // de contribuyentes. Idempotente: si ya estaba, no pasa nada.
    // Esto reemplaza al evento dedicado TecnicoSeIncorporo_v1.

    // Apply de NovedadPreopVerificada_v1 eliminado — el evento fue consolidado
    // en HallazgoRegistrado_v1 con campo ResultadoVerificacion (§12.10.9).

    public void Apply(HallazgoRegistrado_v1 e)
    {
        _hallazgos.Add(new Hallazgo(
            e.HallazgoId, e.Origen,
            e.NovedadPreopId, e.ParteId,
            e.ActividadId, e.ActividadDescripcion,
            e.NovedadTecnicaDescripcion,
            e.AccionRequerida, e.AccionCorrectiva,
            e.CausaFallaId, e.TipoFallaId,
            e.ObservacionCampo,
            new List<Guid>(), e.RegistradoEn));
        _contribuyentes.Add(e.EmitidoPor);
    }

    public void Apply(MedicionRegistrada_v1 e)
    {
        _mediciones.Add(new Medicion(
            e.MedicionId, e.HallazgoId, e.TipoMedicion,
            e.Valor, e.Unidad, e.Observacion, e.RegistradaEn));
        // Si MedicionRegistrada_v1 lleva EmitidoPor, agregar aquí también.
    }

    public void Apply(AdjuntoAgregado_v1 e)
    {
        var i = _hallazgos.FindIndex(h => h.HallazgoId == e.HallazgoId);
        if (i < 0) return;
        var h = _hallazgos[i];
        var nuevos = h.AdjuntosIds.Append(e.AdjuntoId).ToList();
        _hallazgos[i] = h with { AdjuntosIds = nuevos };
        _contribuyentes.Add(e.EmitidoPor);
    }

    public void Apply(AdjuntoEliminado_v1 e)
    {
        _adjuntosEliminados.Add(e.AdjuntoId);
        _contribuyentes.Add(e.EmitidoPor);
    }

    public void Apply(RepuestoEstimadoAgregado_v1 e)
    {
        _repuestos.Add(new RepuestoEstimado(
            e.RepuestoEstimadoId, e.HallazgoId,
            e.SkuId, e.Cantidad, e.Unidad,
            e.Justificacion, e.EstimadoEn));
        _contribuyentes.Add(e.EmitidoPor);
    }

    public void Apply(RepuestoEstimadoRemovido_v1 e)
    {
        _repuestos.RemoveAll(r => r.RepuestoEstimadoId == e.RepuestoEstimadoId);
        _contribuyentes.Add(e.EmitidoPor);
    }

    public void Apply(RepuestoEstimadoActualizado_v1 e)
    {
        var idx = _repuestos.FindIndex(r => r.RepuestoEstimadoId == e.RepuestoEstimadoId);
        if (idx < 0) return;
        var anterior = _repuestos[idx];
        _repuestos[idx] = anterior with
        {
            Cantidad = e.Cantidad,
            Justificacion = e.Justificacion,
        };
        _contribuyentes.Add(e.EmitidoPor);
    }

    public void Apply(DiagnosticoEmitido_v1 e)         => DiagnosticoFinal = e.DiagnosticoFinal;
    public void Apply(DictamenEstablecido_v1 e)        => Dictamen          = e.Dictamen;
    public void Apply(InspeccionFirmada_v1 e)
    {
        Estado    = InspeccionEstado.Firmada;
        FirmadaEn = e.FirmadaEn;
        FirmaUri  = e.FirmaUri;
    }
    public void Apply(InspeccionCancelada_v1 e)
    {
        Estado            = InspeccionEstado.Cancelada;
        MotivoCancelacion = e.Motivo;
    }
    public void Apply(InspeccionCerrada_v1 e)
    {
        Estado              = InspeccionEstado.Cerrada;
        OTCorrectivaIdSinco = e.OTCorrectivaIdSinco;
        OTCorrectivaNumero  = e.OTCorrectivaNumero;
        CerradaEn           = e.CerradaEn;
    }
}
```

#### Esquema de los handlers (decisión)

```csharp
public sealed class InspeccionTecnicaHandler
{
    // IniciarInspeccion crea el aggregate desde cero. Validaciones:
    // - InspeccionId no debe existir todavía (idempotency check en repo).
    // - Equipo existe en el catálogo.
    // - Existe exactamente una rutina técnica para el grupo del equipo (una por grupo).
    // - **NO existe otra inspección EnEjecucion para el mismo equipo (sin importar técnico).**
    //   Si ya existe, el cliente UI debe dirigir al técnico a "continuar la existente".
    public async Task<IEnumerable<object>> Handle(
        IniciarInspeccion cmd,
        IReferenceDataService refData,
        IInspeccionQueries queries,
        CancellationToken ct)
    {
        var equipo = await refData.ObtenerEquipo(cmd.EquipoId, ct)
            ?? throw new DomainException("Equipo no existe");

        // Rutina técnica derivada del grupo del equipo (una por grupo).
        // El módulo solo consume rutinas técnicas — §12.11.1.
        var rutina = await refData.ObtenerRutinaTecnicaPorGrupo(equipo.GrupoMantenimiento, ct)
            ?? throw new DomainException(
                $"No hay rutina técnica configurada para el grupo {equipo.GrupoMantenimiento}. " +
                "Contacta al admin del catálogo de rutinas en Sinco.");

        var inspeccionActiva = await queries.ObtenerInspeccionActivaParaEquipo(cmd.EquipoId, ct);
        if (inspeccionActiva is not null)
            throw new DomainException(
                $"Equipo ya tiene inspección en ejecución (#{inspeccionActiva.InspeccionId}, " +
                $"iniciada por {inspeccionActiva.TecnicoIniciador}). " +
                "Continúa la existente en lugar de iniciar nueva.");

        return new[]
        {
            new InspeccionIniciada_v1(
                cmd.InspeccionId,
                cmd.EquipoId,
                rutina.RutinaId,                    // resuelto por el handler
                rutina.Codigo,
                cmd.IniciadaPor,
                cmd.ObraId,
                cmd.Ubicacion,
                DateTime.UtcNow)
        };
    }

    // Handler de VerificarNovedadPreoperacional eliminado — el comando se
    // consolidó en RegistrarHallazgo con NovedadPreopId + ResultadoVerificacion
    // (§12.10.9). El handler de RegistrarHallazgo valida I12c e I14 cuando
    // Origen == PreOperacional.

    public IEnumerable<object> Handle(
        FirmarInspeccion cmd, InspeccionTecnica state, ClaimsPrincipal user)
    {
        if (state.Estado != InspeccionEstado.EnEjecucion)
            throw new DomainException("Solo se firma desde EnEjecucion");
        if (state.DiagnosticoFinal is null)
            throw new DomainException("Falta diagnóstico");
        if (state.Dictamen is null)
            throw new DomainException("Falta dictamen");

        // I8: el firmante debe ser un técnico contribuyente o un supervisor.
        var esContribuyente = state.TecnicosContribuyentes.Contains(cmd.FirmadoPor);
        var esSupervisor = user.HasClaim("sinco_roles", "supervisor");
        if (!esContribuyente && !esSupervisor)
            throw new DomainException(
                "Solo un técnico que haya contribuido a la inspección o un supervisor puede firmarla");

        // I7 refinada (§12.9.4): si hay hallazgo RequiereIntervencion, debe generar OT
        // — eso lo controla la saga, no esta invariante. Aquí no validamos dictamen vs severidad.

        yield return new InspeccionFirmada_v1(
            cmd.InspeccionId, cmd.FirmadoPor, cmd.FirmaUri,
            DateTime.UtcNow);
    }

    // ... resto de comandos análogos
}
```

(Si se usa **Wolverine** como mediator, los handlers son métodos estáticos sobre el aggregate o clases con convención. Aquí se muestra estilo neutral.)

### 2.2 `TipoInspeccion` (*superseded por §12.7 `Rutina` + §12.11 enum `TipoInspeccion`*)

> Esta sección reflejaba un aggregate de configuración inicial que fue reemplazado por `Rutina` (§12.7) tras reconciliar con plantillas Excel del ERP. Adicionalmente, el nombre `TipoInspeccion` se reusa en §12.11 con un significado distinto: enum del aggregate `Inspeccion` que distingue qué tipo de flujo se ejecuta (Tecnica por ahora, Monitoreo a futuro). Léase §12.11 para el contrato vigente.

**Contenido histórico (no usar como referencia activa):**

Aggregate de configuración. CRUD parametrizable por administradores. NO event-sourced (cambia poco, conviene como document store de Marten o tabla relacional).

```csharp
public sealed class TipoInspeccion
{
    public Guid TipoInspeccionId { get; set; }
    public string Codigo { get; set; } = default!;     // "MOTOR", "HIDRAULICA"
    public string Nombre { get; set; } = default!;
    public string Descripcion { get; set; } = default!;
    public bool Activo { get; set; }

    // Plantilla: parte → actividades sugeridas
    public List<PartePlantilla> PartesEsperadas { get; set; } = new();

    // Mediciones esperadas por tipo de inspección
    public List<MedicionPlantilla> MedicionesEsperadas { get; set; } = new();

    // Reglas
    public bool RequiereSupervisor { get; set; }       // sólo supervisor puede firmar
    public TimeSpan VigenciaDictamen { get; set; }     // 7d, 30d, etc.
}

public sealed record PartePlantilla(
    Guid ParteId,                          // del catálogo Sinco
    bool Obligatoria,                       // se debe revisar siempre
    List<string> ActividadesSugeridas);

public sealed record MedicionPlantilla(
    string TipoMedicion,                    // "Presión hidráulica"
    string Unidad,                          // "PSI"
    decimal? RangoMin,
    decimal? RangoMax);
```

---

## 3. Value objects

```csharp
public sealed record Hallazgo(
    Guid HallazgoId,
    OrigenHallazgo Origen,
    Guid? NovedadPreopId,           // null si Origen == Manual
    Guid ParteId,
    string ActividadDescripcion,
    Severidad Severidad,
    string Descripcion,
    IReadOnlyList<Guid> AdjuntosIds,
    DateTime RegistradoEn);

public sealed record Medicion(
    Guid MedicionId, Guid HallazgoId,
    string TipoMedicion, decimal Valor, string Unidad,
    string? Observacion, DateTime RegistradaEn);

public sealed record RepuestoEstimado(
    Guid RepuestoEstimadoId, Guid HallazgoId,
    Guid SkuId, decimal Cantidad, string Unidad,
    string Justificacion, DateTime EstimadoEn);

public sealed record UbicacionGps(double Latitud, double Longitud, double? PrecisionMetros);

public enum OrigenHallazgo  { PreOperacional, Manual }
public enum Severidad        { Baja, Media, Alta, Critica }
public enum DictamenOperacion { PuedeOperar, ConRestriccion, NoPuedeOperar }
public enum ResultadoVerificacion { Confirmada, Descartada, RequiereSeguimiento }
```

---

## 4. Aggregates de soporte (Catálogo)

Documentos `Marten` read-only mantenidos por sincronización REST contra Sinco on-prem. Estrategia de sincronización detallada en **ADR-004** (`00-investigacion-mercado.md §9.15`): sync inicial al desplegar + cron diario nocturno con `If-Modified-Since`/`ETag` + stale-while-revalidate como fallback. Reglas operativas vinculantes: IDs/códigos inmutables, renombrar = cambiar descripción, descontinuar = `activa = false` no delete.

> **Nota:** las definiciones de tipos vigentes están en §12.7 (`EquipoLocal`, `UbicacionLocal`, `ObraLocal`, `RepuestoLocal`, `Rutina`) y §12.9.6 (`CausaFallaCatalogo`, `TipoFallaCatalogo`). Las definiciones que aparecen abajo son la primera versión preliminar — fueron refinadas en §12 tras la reconciliación con plantillas Excel del ERP. Léase §12.7 y §12.9.6 para los contratos vigentes.

```csharp
// Definiciones preliminares — superseded por §12.7 y §12.9.6
public sealed record EquipoLocal(
    Guid EquipoId,
    string Codigo,                  // "EXC-320D-014"
    string Modelo,
    string Marca,
    Guid ObraActualId,
    decimal HorometroActual,
    Dictionary<string, string> AtributosExtra,
    DateTime SincronizadoEn);

public sealed record ParteLocal(
    Guid ParteId,
    Guid EquipoId,
    string Nombre,
    string? Categoria,
    string? PosicionFisica,
    bool Critica,
    Guid? ParteIdPadre,
    DateTime SincronizadoEn);

public sealed record RepuestoLocal(
    Guid SkuId,
    string Codigo,
    string Nombre,
    string Unidad,
    decimal? PrecioReferencia,
    List<Guid> ParteIdsCompatibles,
    DateTime SincronizadoEn);
```

### Patrón de acceso desde el dominio

El dominio puro **nunca llama HTTP directo** — siempre lee de la cache local. La abstracción es un puerto `IReferenceDataService` cuyas implementaciones cumplen con ADR-004:

```csharp
public interface IReferenceDataService
{
    Task<EquipoLocal?> ObtenerEquipo(Guid equipoId, CancellationToken ct);
    Task<IReadOnlyList<ParteCatalogo>> ListarPartes(CancellationToken ct);
    Task<IReadOnlyList<CausaFallaCatalogo>> ListarCausasFalla(CancellationToken ct);
    Task<IReadOnlyList<TipoFallaCatalogo>> ListarTiposFalla(CancellationToken ct);
    Task<IReadOnlyList<UbicacionLocal>> ListarUbicaciones(CancellationToken ct);
    Task<IReadOnlyList<ObraLocal>> ListarObras(CancellationToken ct);
    Task<IReadOnlyList<RepuestoLocal>> BuscarRepuestos(Guid? parteId, string? q, CancellationToken ct);
    Task<Rutina?> ObtenerRutina(Guid rutinaId, CancellationToken ct);
    Task<Rutina?> ObtenerRutinaTecnicaPorGrupo(string grupoMantenimiento, CancellationToken ct);
    // El parámetro TipoRutina se eliminó; el módulo solo consume rutinas técnicas (§12.11.1).
}
```

Implementación concreta en `Sinco.Inspecciones.Infrastructure.ReferenceData`:
- Lectura desde proyección Marten (siempre fresh dentro del proceso, hasta cron diario nocturno).
- Si la proyección está vacía (módulo recién desplegado o BD reseteada), la primera lectura dispara sync sincrónico desde ERP.
- En modo degradado (VPN caído al momento del cron), la cache previa se sigue sirviendo (stale-while-revalidate).

### Job de sincronización

Wolverine timer trigger ejecuta diariamente (~3 AM hora Bogotá):

```csharp
public sealed class SincronizarCatalogosJob
{
    public async Task Execute(IDocumentSession session,
                              IReferenceDataAdapter erpAdapter,
                              ILogger<SincronizarCatalogosJob> log)
    {
        await SincronizarConditionalGet<CausaFallaCatalogo>(session, erpAdapter, log);
        await SincronizarConditionalGet<TipoFallaCatalogo>(session, erpAdapter, log);
        await SincronizarConditionalGet<ParteCatalogo>(session, erpAdapter, log);
        await SincronizarConditionalGet<UbicacionLocal>(session, erpAdapter, log);
        await SincronizarConditionalGet<ObraLocal>(session, erpAdapter, log);
        await SincronizarConditionalGet<RepuestoLocal>(session, erpAdapter, log);
        await SincronizarConditionalGet<Rutina>(session, erpAdapter, log);
        await SincronizarConditionalGet<EquipoLocal>(session, erpAdapter, log);
    }

    // Por cada tipo: usa If-Modified-Since/ETag persistido junto a la proyección.
    // Si ERP responde 304: log "no changes" y retorna.
    // Si ERP responde 200: reemplaza/actualiza documentos, persiste nuevo ETag.
    // Si error: log + alerta + sigue con los demás (un catálogo caído no bloquea los otros).
}
```

El **botón admin "refrescar ahora"** está diferido a v1.1 (ver ADR-004). En v1.0 se acepta ventana de hasta 24h entre cambio de catálogo en ERP y disponibilidad en módulo cloud.

---

## 5. Proyecciones de Reporting

Para no martillar el event store con queries de UI.

### 5.1 `BandejaTecnico`

Una fila por inspección por técnico. Útil para la pantalla principal del móvil.

```csharp
public sealed record BandejaItem(
    Guid InspeccionId,
    string TecnicoId,
    Guid EquipoId, string EquipoCodigo,
    Guid RutinaId, string RutinaCodigo, string RutinaNombre,
    Guid ObraId, string ObraNombre,
    DateTime IniciadaEn,
    InspeccionEstado Estado,
    int HallazgosCount,
    int RepuestosEstimadosCount,
    DictamenOperacion? UltimoDictamen);
```

Una proyección Marten agregada construida por `MultiStreamProjection`.

### 5.2 `DetalleInspeccion`

Vista completa para la pantalla de ejecución del técnico. Reagrupa el stream con datos de catálogo (joins en proyección).

### 5.3 `KPIsPorObra`

Conteos: inspecciones por estado, hallazgos por severidad, repuestos consumidos. Para dashboard de supervisor.

### 5.4 `HistoricoEquipo`

Lista paginada de inspecciones cerradas por equipo, con dictámenes y costos estimados acumulados.

---

## 6. Integración saliente (Outbox + ACL)

### 6.1 Decisión

Cuando una inspección llega a `InspeccionFirmada_v1`, hay que materializar tres cosas hacia Sinco on-prem (todas vía REST, ADR-001):

1. Marcar las novedades del preoperacional como verificadas: `POST /api/v1/preop/novedades/{id}/verificar` por cada hallazgo con `Origen == PreOperacional` (con su `ResultadoVerificacion` y `NovedadTecnica` como diagnóstico).
2. Crear OT correctiva sugerida en MYE: `POST /api/v1/mye/ot-correctivas` con BOM consolidado.
3. Esperar la respuesta de MYE con el `OTCorrectivaIdSinco` y emitir `InspeccionCerrada_v1` para cerrar el ciclo.

Esto se hace con **outbox transaccional** — los eventos de salida se persisten en una tabla outbox dentro de la misma transacción del stream, y un worker los procesa garantizando entrega y idempotencia.

### 6.2 Implementación recomendada

**Wolverine** (de la misma familia que Marten, by Jeremy Miller) implementa outbox + saga + retry nativamente sobre Marten. Si no se usa Wolverine, el patrón outbox debe hacerse a mano en código (más boilerplate, mismo concepto).

```csharp
// Saga / process manager: reacciona a InspeccionFirmada_v1
public sealed class CerrarInspeccionSaga
{
    public async Task Handle(
        InspeccionFirmada_v1 evt,
        IDocumentSession session,
        IPreoperacionalAdapter preopAcl,
        IMyeAdapter myeAcl,
        ILogger<CerrarInspeccionSaga> log)
    {
        var inspeccion = await session.Events
            .AggregateStreamAsync<InspeccionTecnica>(evt.InspeccionId);

        if (inspeccion is null) return;

        // 1. Verificar novedades del preop — POST a ERP para que las marque
        //    como atendidas y no aparezcan en próximas inspecciones (§12.10.9).
        foreach (var h in inspeccion.Hallazgos
                     .Where(h => h.Origen == OrigenHallazgo.PreOperacional))
        {
            await preopAcl.MarcarVerificada(
                h.NovedadPreopId!.Value,
                inspeccion.InspeccionId,
                h.ResultadoVerificacion!.Value,           // directamente del hallazgo
                h.NovedadTecnica,                         // diagnóstico técnico
                inspeccion.DiagnosticoFinal!,
                inspeccion.TecnicoAsignadoId);
        }

        // 2. Crear OT correctiva en MYE — recibe ID técnico + número humano
        var bom = ConstruirBom(inspeccion);
        var response = await myeAcl.CrearOTCorrectiva(new CrearOTCorrectivaRequest_v1(
            inspeccion.InspeccionId,
            inspeccion.EquipoId,
            CalcularPrioridad(inspeccion),
            ConstruirDescripcion(inspeccion),
            inspeccion.Hallazgos
                .Where(h => h.Origen == OrigenHallazgo.PreOperacional)
                .Select(h => h.NovedadPreopId!.Value).ToList(),
            inspeccion.Hallazgos
                .Where(h => h.Origen == OrigenHallazgo.Manual)
                .Select(h => h.HallazgoId).ToList(),
            bom,
            inspeccion.Dictamen!.Value));

        // 3. Cerrar ciclo con ID técnico + número humano
        session.Events.Append(evt.InspeccionId,
            new InspeccionCerrada_v1(
                evt.InspeccionId,
                response.OTCorrectivaIdSinco,
                response.OTCorrectivaNumero,           // "OT-123456" visible al técnico
                DateTime.UtcNow));
        await session.SaveChangesAsync();
    }
}
```

### 6.3 Adapters (puertos)

Interfaces en el core, implementaciones en `Sinco.Inspecciones.Adapters.*`:

```csharp
public interface IPreoperacionalAdapter
{
    Task<IReadOnlyList<NovedadPreopDto>> ObtenerNovedadesPendientes(
        Guid obraId, int page, int size, CancellationToken ct);
    Task<NovedadPreopDto> ObtenerNovedad(Guid novedadId, CancellationToken ct);
    Task<Stream> ObtenerAdjunto(Guid adjuntoId, CancellationToken ct);
    Task MarcarVerificada(
        Guid novedadId, Guid inspeccionId,
        ResultadoVerificacion resultado, string diagnostico,
        string verificadaPor, CancellationToken ct = default);
}

public interface IMyeAdapter
{
    Task<EquipoDto> ObtenerEquipo(Guid equipoId, CancellationToken ct);
    Task<IReadOnlyList<ParteDto>> ObtenerPartesPorEquipo(
        Guid equipoId, CancellationToken ct);
    Task<ParteDto> ObtenerParte(Guid parteId, CancellationToken ct);
    Task<Guid> CrearOTCorrectiva(
        CrearOTCorrectivaRequest_v1 request,
        string idempotencyKey,
        CancellationToken ct = default);
}

public interface IInventarioAdapter
{
    Task<IReadOnlyList<RepuestoDto>> BuscarRepuestos(
        Guid? parteId, string? termino, int page, int size,
        CancellationToken ct);
}
```

### 6.4 Idempotencia

- `POST /preop/novedades/{id}/verificar` se llama con header `Idempotency-Key: {inspeccionId}-{novedadId}`. El preop ignora la segunda llamada con la misma key.
- `POST /mye/ot-correctivas` con `Idempotency-Key: {inspeccionId}`. Si la OT ya existe, MYE devuelve el mismo `OTCorrectivaIdSinco`.
- Si la red falla en (2), el worker reintenta con backoff exponencial. La firma del comando en el stream sigue siendo la misma; los efectos externos eventualmente se aplican.

---

## 7. Flujos representativos

### 7.1 Flujo "feliz" — verificación + descubrimiento + OT

```
Técnico llega a planta y escoge equipo + rutina técnica aplicable
   │
   ▼
IniciarInspeccion (cmd) ─▶ InspeccionIniciada_v1
   (crea el aggregate; lleva snapshot de la rutina al momento de inicio)
   │
   ▼
(loop) ObtenerNovedadesPendientes (REST GET preop, vía adapter)
   │
   ▼
Por cada novedad: RegistrarHallazgo (Origen=PreOperacional + ResultadoVerificacion) ─▶ HallazgoRegistrado_v1
   │
   ▼
(loop) DescubrirHallazgo + RegistrarMedicion + AdjuntarArchivo
        ─▶ HallazgoDescubierto_v1, MedicionRegistrada_v1, AdjuntoAgregado_v1
   │
   ▼
Por cada hallazgo: EstimarRepuesto ─▶ RepuestoEstimadoAgregado_v1
   │
   ▼
EmitirDiagnostico ─▶ DiagnosticoEmitido_v1
EstablecerDictamen ─▶ DictamenEstablecido_v1
   │
   ▼
FirmarInspeccion ─▶ InspeccionFirmada_v1
   │
   ▼ (saga)
CerrarInspeccionSaga
   ├─ POST /preop/novedades/{id}/verificar (×N)
   ├─ POST /mye/ot-correctivas (recibe OTCorrectivaIdSinco)
   └─ Append InspeccionCerrada_v1 ─▶ Estado = Cerrada
```

### 7.2 Flujo de cancelación

`EnEjecucion` → `CancelarInspeccion` → `InspeccionCancelada_v1`. La saga NO ejecuta posts a Sinco. Estado terminal: `Cancelada`.

### 7.3 Flujo de re-inspección

Si el dictamen es `RequiereSeguimiento` para alguna novedad, la novedad **NO se marca verificada en preop**. Permanece pendiente y aparece en la siguiente inspección programada para el equipo. El técnico la vuelve a abordar.

### 7.4 Lifecycle de novedades del preoperacional vs. inspección técnica

Las novedades del preoperacional y las inspecciones técnicas tienen ciclos de vida independientes que se cruzan en momentos puntuales. Esta sección documenta cómo interactúan.

#### 7.4.1 Patrón de acumulación

Una novedad del preoperacional, una vez reportada por el operario, queda en estado **"pendiente"** en el lado del preop hasta que un técnico la verifica desde una inspección. No expira automáticamente.

Las novedades pendientes **se acumulan a nivel del equipo**, no de la inspección. Mientras nadie las verifique, siguen acumulándose. Múltiples preops del mismo equipo durante días/semanas pueden generar N novedades pendientes a verificar.

Cuando un técnico inicia una inspección técnica para ese equipo, todas las novedades pendientes (sin importar antigüedad) son candidatas a ser verificadas en esa inspección.

#### 7.4.2 Lista viva, no snapshot

Cuando el técnico abre la pantalla "Importar desde preoperacional", el módulo hace `GET /preop/novedades?equipo={id}&estado=pendiente` en ese momento. **La lista es viva**, no se congela al iniciar la inspección.

**Implicación operativa:** si entre las 9 AM (técnico inicia inspección) y las 11 AM (técnico aún ejecutando), el operario reporta una nueva novedad para ese equipo, esa nueva novedad **aparece** en la pantalla del técnico cuando refresque. Y el técnico la puede verificar dentro de la misma inspección activa.

Esto refleja la realidad: las inspecciones técnicas duran horas, los operarios pueden seguir generando preops en ese rango.

#### 7.4.3 Boundary: la firma cierra la ventana

| Momento | Estado de la inspección | ¿Pueden entrar novedades nuevas a esta inspección? |
|---|---|---|
| Al iniciar (`InspeccionIniciada_v1`) | EnEjecucion | ✅ Sí |
| Mientras técnico verifica/descubre | EnEjecucion | ✅ Sí |
| Técnico firma (`InspeccionFirmada_v1`) | Firmada (saga procesando) | ❌ No, aggregate inmutable |
| Saga confirma OT (`InspeccionCerrada_v1`) | Cerrada | ❌ Terminal |
| Cancelación (`InspeccionCancelada_v1`) | Cancelada | ❌ Terminal |

Cualquier novedad que aparezca en preop después de `InspeccionFirmada_v1` queda pendiente para la **siguiente** inspección del equipo.

#### 7.4.4 Por qué `I1b` (bloqueo por equipo) es coherente con esta semántica

La invariante I1b — "una sola inspección EnEjecucion por equipo, sin importar técnico" — es lo que mantiene esta semántica limpia. Si pudieran existir dos inspecciones EnEjecucion en paralelo para el mismo equipo:

- Dos técnicos podrían intentar verificar la misma novedad pendiente al mismo tiempo.
- I4 ("una novedad se verifica una sola vez") generaría errores frecuentes de carrera.
- El concepto de "ventana de acumulación" se rompe (¿en cuál de las dos entra una novedad nueva?).

Con I1b en vigor, hay **un único punto de acumulación a la vez por equipo**. Es claro y predecible.

#### 7.4.5 Novedades no verificadas

Si el técnico cierra una inspección sin verificar todas las novedades pendientes (porque alguna no aplicaba a su alcance, no tenía evidencia, etc.), las no verificadas **siguen pendientes** en el lado del preop. Quedan disponibles para la siguiente inspección.

El descarte explícito requiere acción del técnico: registrar un hallazgo con `Origen = PreOperacional` y `ResultadoVerificacion = Descartada` (con su diagnóstico). Sin esa acción, la novedad sigue pendiente en el preop.

#### 7.4.6 Edge case: muchas novedades pendientes acumuladas

En operación real, un equipo poco inspeccionado puede acumular decenas de novedades pendientes (50+ no es inaudito). Esto degrada UX si la pantalla "Importar desde preoperacional" las lista todas sin más.

**Mitigaciones recomendadas (UX, no modelo):**

- **Paginación** en `GET /preop/novedades?...&page=&size=` (ya en el contrato API, ver §12.9.7).
- **Filtros** por severidad reportada, fecha de reporte, parte afectada.
- **Bandeja del supervisor** con alerta "equipo X tiene 30+ novedades pendientes hace >60 días" para escalación operativa.
- **Sugerencia de orden por defecto** en el listado: primero las más nuevas, o primero las de severidad mayor.

Estas son decisiones de producto que el consultor mecánico de producto debe validar (ver `04-brief-consultor-producto.md §8` áreas críticas).

#### 7.4.7 Resumen del lifecycle

```
Preop                                            Inspección técnica
─────                                            ──────────────────

[novedad reportada]
   │
   ▼
[pendiente]  ◀── puede acumular N días/semanas
   │
   │ (técnico inicia inspección
   │  y abre "Importar desde preop")
   │
   ▼
   ◀──────────── lista viva ─────────────►  [InspeccionIniciada_v1]
                                                    │
                                              EnEjecucion
                                                    │
   (mientras técnico ejecuta)                       │
   nuevas novedades pueden seguir         (técnico verifica novedades,
   apareciendo, lista se refresca         emite NovedadPreopVerificada
                                          + HallazgoRegistrado origen Preop)
                                                    │
                                              [InspeccionFirmada_v1]
                                              (ventana cerrada)
                                                    │
   novedades nuevas en preop ──────►  esperan próxima inspección
   se acumulan en equipo

   novedades NO verificadas en
   esta inspección permanecen
   pendientes para la próxima
```

---

## 8. Naming convention y versionado

| Categoría | Convención | Ubicación | Versionado |
|---|---|---|---|
| **Streams** | `inspeccion-{guid}` (lowercase, kebab) | Marten | — |
| **Eventos de dominio** | `<Sustantivo><Verbo>Pasado_v<N>` ej: `InspeccionFirmada_v1`, `HallazgoEnRutina_v2` | `Sinco.Inspecciones.Core/Events` | `_vN` obligatorio desde el día 1 |
| **Comandos** | Imperativo presente, sin sufijo de versión: `IniciarInspeccion`, `EstimarRepuesto` | `Sinco.Inspecciones.Core/Commands` | Sin versión (contrato interno) |
| **DTOs de respuesta de adapters** (datos que entran del exterior) | Sufijo `Dto`: `NovedadPreopDto`, `EquipoDto`, `ParteDto` | `Sinco.Inspecciones.Adapters.<X>` | Implícito en URL `/api/v1/...` |
| **DTOs de request HTTP** (datos que salen del módulo) | Patrón `<Verbo><Sustantivo>Request_v<N>`: `CrearOTCorrectivaRequest_v1`, `VerificarNovedadRequest_v1` | `Sinco.Inspecciones.Adapters.<X>` | `_vN` recomendado |
| **DTOs de response HTTP** | `<Verbo><Sustantivo>Response_v<N>`: `CrearOTCorrectivaResponse_v1` | `Sinco.Inspecciones.Adapters.<X>` | `_vN` recomendado |
| **Tipos compartidos** | Sin sufijo: `Severidad`, `DictamenOperacion`, `AccionRequerida` | `Sinco.Inspecciones.Core.SharedKernel` | — |

### Regla crítica: evento de dominio ≠ DTO de request

Un **evento de dominio** describe un hecho ocurrido dentro del bounded context y se persiste en el event store. Un **DTO de request HTTP** es la forma serializada que viaja por la red al llamar otro bounded context — es un **contrato de transporte**, no un hecho.

Pueden tener forma similar pero **nunca son la misma clase**, por dos razones:
1. **Versionado independiente**: el contrato de red puede evolucionar sin tocar la historia de eventos del aggregate (y al revés).
2. **Cumplimiento §2 de la guía EDA Sinco**: "Nunca exponer un evento de dominio directamente como evento de integración."

**Patrón aplicado en este módulo:**

| Caso | Evento de dominio (en stream Marten) | DTO de request HTTP (al adapter) |
|---|---|---|
| Solicitar OT a MYE al firmar | (no se emite — el efecto se registra como `InspeccionCerrada_v1` cuando MYE confirma) | `CrearOTCorrectivaRequest_v1` viaja al adapter; respuesta `CrearOTCorrectivaResponse_v1` |
| Verificar novedad del preop | `HallazgoRegistrado_v1` con Origen=PreOperacional + ResultadoVerificacion (en stream de la inspección, §12.10.9) | parámetros del método del adapter (sin DTO) |

### Reglas de evolución

- Cuando un evento tenga que cambiar de forma: se crea `<Evento>_v2` y se mantiene `_v1` con un upcaster en Marten. **Nunca editar la forma de un evento ya escrito en producción.**
- Cuando un DTO de request/response tenga que cambiar: se publica nueva versión en el adapter (`_v2`) y, si la API expuesta cambia incompatiblemente, se sube la versión de la URL (`/api/v2/...`).

---

## 9. Estructura de proyecto sugerida

```
src/
  Sinco.Inspecciones.Core/                  # dominio puro, no conoce HTTP ni Marten
    Aggregates/
      InspeccionTecnica.cs
      TipoInspeccion.cs
    Events/
      InspeccionEvents.cs                   # records _v1
    Commands/
      InspeccionCommands.cs
    ValueObjects/
    Ports/                                  # interfaces de adapters
      IPreoperacionalAdapter.cs
      IMyeAdapter.cs
      IInventarioAdapter.cs

  Sinco.Inspecciones.Application/           # handlers, sagas, validation
    Handlers/
    Sagas/
      CerrarInspeccionSaga.cs

  Sinco.Inspecciones.Infrastructure/        # Marten, Wolverine, DI
    Marten/
      MartenConfig.cs
      Projections/
        BandejaTecnicoProjection.cs
        DetalleInspeccionProjection.cs
        CatalogoSyncProjection.cs

  Sinco.Inspecciones.Adapters.Preoperacional/
  Sinco.Inspecciones.Adapters.Mye/
  Sinco.Inspecciones.Adapters.Inventario/

  Sinco.Inspecciones.Api/                   # ASP.NET endpoints, auth
    Endpoints/
    Auth/

tests/
  Sinco.Inspecciones.Core.Tests/             # unit, sin infra
  Sinco.Inspecciones.Integration.Tests/      # con Marten + adapters mockeados
  Sinco.Inspecciones.Contract.Tests/         # Pact contra Sinco
```

---

## 10. Lo que NO está modelado todavía y por qué

| Área | Por qué se posterga |
|---|---|
| **Sub-aggregate por hallazgo** | Tratado como value object dentro de `InspeccionTecnica`. Si en el futuro cada hallazgo desarrolla ciclo de vida propio (asignar a otro técnico, reabrir tras reparación), promover a aggregate. Por ahora, no hay esa demanda. |
| **Inspección con múltiples equipos** | Por simplicidad MVP, una inspección = un equipo. Si surge "inspeccionar todos los equipos de un frente", se modela como conjunto de inspecciones independientes con un `BatchId`. |
| **Workflow de aprobación** (revisor además del técnico) | No se levantó como requisito. Si aparece, agregar `InspeccionRevisada_v1` antes de `InspeccionFirmada_v1` con rol distinto. |
| **Calibración de instrumentos** | Si las mediciones requieren instrumento certificado, agregar value object `Instrumento` con expiración. No prioritario MVP. |
| **Comentarios cruzados / chat** | Si el técnico necesita preguntar al supervisor, mejor fuera del aggregate (módulo de mensajería aparte). |
| **Versiones de plantilla** | `TipoInspeccion` debería congelarse al momento de programar la inspección (snapshot) para que cambiar la plantilla mañana no rompa inspecciones en curso. Patrón: copiar la plantilla al stream al programar, o referenciar `tipoInspeccionId@version`. Decidir cuando se construya. |

---

## 12. Reconciliación con plantillas Excel del ERP (2026-04-27)

Las plantillas en `Plantillas Excel/` (Equipos.xlsx, Insumos.xlsx, preoperacional.xlsx) son la **estructura real con la que ya se modelan los datos en Sinco MYE**. Esto cambia varios supuestos del modelo y agrega conceptos que no había aterrizado.

### 12.1 Estructura observada

**Equipos.xlsx** — Maestro de equipos:

| Campo | Ejemplo | Nota |
|---|---|---|
| `codigo` | `1` | Identificador natural de negocio. Numérico/string corto, no Guid. |
| `placa` | `xemp899` | Para vehículos / equipos sobre rueda. Opcional. |
| `descripcion` | `bulldozer caterpillar d6` | Texto libre. |
| `Grupo mantenimiento` | `bulldozer` | **Clave**: agrupa equipos para aplicación de rutinas. |
| `ubicación` | `bogota` | Texto libre — ciudad u obra (ambiguo, ver pregunta abierta). |

**Insumos.xlsx** — Catálogo de repuestos e insumos:

| Campo | Ejemplo | Nota |
|---|---|---|
| `codigo` | `101`, `103` | Identificador natural. |
| `descripcion` | `acpm`, `flltro de aceite` | Texto libre. |
| `Agrupacion` | `combustible`, `repuestos` | Categoría. |
| `unidad de medida` | `galon`, `unidad` | Catálogo de unidades. |
| `AplicaMYE` | `opcional` / `requerido` | **Importante**: indica si el insumo es MYE-relevante. |

**preoperacional.xlsx** — Definición de la rutina preoperacional:

| Campo | Ejemplo | Nota |
|---|---|---|
| `Grupo` | `BULLDOZER` | Grupo de mantenimiento. |
| `Rutina Mantenimiento` | `D65PX2` | El modelo del equipo dentro del grupo. |
| `Rutina Preoperacional` | `PERO. BULL` | Código de la rutina (plantilla aplicada). |
| `Parte` | `MOTOR`, `TRANSMISION` | Componente del equipo. |
| `Actividad` | `VERIFICACION ESTADO`, `CAMBIO DE REPUESTO`, `COMPLETAR NIVEL`, `VERIFICAR FUGA` | Catálogo de verbos estandarizados. |

### 12.2 Modelo conceptual real (revisado)

El árbol no es plano `Equipo → Parte → Actividad`. Es:

```
Grupo (BULLDOZER, EXCAVADORA, …)
   │
   ├── Modelo dentro del grupo (D65PX2, …)
   │     │
   │     └── Rutina aplicable (PERO. BULL para preop, ¿INSP. BULL para técnica?)
   │           │
   │           └── Items de rutina = (Parte, Actividad)
   │
   └── Equipos concretos (placa xemp899, código 1)
         │
         └── Reportes de inspección que instancian la rutina aplicable
```

**Conclusiones clave:**

1. **La plantilla de inspección NO se define a nivel de equipo individual**, se define a nivel `(Grupo, Modelo)`. Un mismo PERO. BULL aplica a todos los bulldozers D65PX2.
2. **Parte y Actividad son catálogos referenciados**, no texto libre. `MOTOR`, `TRANSMISION`, `VERIFICACION ESTADO` son códigos del catálogo.
3. El concepto **`Rutina`** existe ya en Sinco con `Codigo`, `Grupo`, `Modelo` y un set de `(Parte, Actividad)`. La rutina técnica del módulo nuevo es un nuevo tipo de rutina dentro del mismo modelo, no un concepto distinto.
4. **`AplicaMYE`** debe filtrar el selector de repuestos en el módulo nuevo (probablemente solo muestra `requerido` por defecto, con opción de ver opcionales).
5. **Identificadores naturales**: el negocio piensa en `codigo` (1, 101, PERO.BULL, MOTOR), no en Guids. El modelo debe almacenar ambos: Guid técnico interno + código de negocio Sinco.

### 12.3 Cambios al modelo de dominio

#### `EquipoLocal` (revisado)

```csharp
public sealed record EquipoLocal(
    Guid EquipoId,                      // Id técnico interno
    string CodigoSinco,                 // "1" — clave natural
    string? Placa,                      // "xemp899" — opcional
    string Descripcion,
    string GrupoMantenimiento,          // "BULLDOZER" — clave para rutina
    string Modelo,                      // "D65PX2"
    string? UbicacionDescripcion,       // texto libre por ahora
    Guid? ObraActualId,                 // si existe catálogo de obras
    decimal? HorometroActual,
    Dictionary<string, string> AtributosExtra,
    DateTime SincronizadoEn);
```

#### `ParteCatalogo` y `ActividadCatalogo` (nuevos)

Antes había modelado `ParteLocal` como "una parte por equipo". Lo correcto es: **`ParteCatalogo`** es independiente del equipo, y se asocia a grupos/modelos vía la rutina.

```csharp
public sealed record ParteCatalogo(
    Guid ParteId,
    string Codigo,                      // "MOTOR"
    string Nombre,
    string? Categoria,
    DateTime SincronizadoEn);

public sealed record ActividadCatalogo(
    Guid ActividadId,
    string Codigo,                      // "VERIFICACION ESTADO"
    string Descripcion,
    DateTime SincronizadoEn);
```

#### `Rutina` (nuevo aggregate de catálogo)

Reemplaza al `TipoInspeccion` que tenía en §2.2. La rutina es el concepto del ERP.

```csharp
public sealed record Rutina(
    Guid RutinaId,
    string Codigo,                          // "PERO. BULL", "INSP.BULL.MOTOR"
    string Nombre,
    TipoRutina Tipo,                        // Preoperacional | Tecnica
    string GrupoMantenimiento,              // "BULLDOZER"
    string? Modelo,                         // "D65PX2" o null si aplica al grupo entero
    IReadOnlyList<ItemRutina> Items,
    DateTime SincronizadoEn);

public sealed record ItemRutina(
    Guid ParteId,
    Guid ActividadId,
    bool Obligatorio,
    string? Instruccion);                   // texto guía opcional para el ejecutor

public enum TipoRutina { Preoperacional, Tecnica }
```

**Ventaja**: la rutina técnica reusa la misma estructura. La diferencia es `Tipo = Tecnica` y posiblemente más detalle por `ItemRutina` (mediciones esperadas, criterios de aprobación). Si el ERP ya soporta esto, no hay que inventar nada.

#### `RepuestoLocal` (revisado)

```csharp
public sealed record RepuestoLocal(
    Guid SkuId,
    string CodigoSinco,                     // "101", "103"
    string Descripcion,
    string Agrupacion,                      // "combustible", "repuestos"
    string UnidadMedida,                    // "galon", "unidad"
    AplicabilidadMYE AplicaMYE,
    decimal? PrecioReferencia,
    List<Guid> ParteIdsCompatibles,         // si la relación existe
    DateTime SincronizadoEn);

public enum AplicabilidadMYE { NoAplica, Opcional, Requerido }
```

#### Eventos del aggregate `InspeccionTecnica` (ajustes)

`HallazgoDescubierto_v1.ActividadDescripcion` (string libre) cambia a referencia:

```csharp
public sealed record HallazgoDescubierto_v1(
    Guid InspeccionId, Guid HallazgoId,
    Guid ParteId,                       // referencia ParteCatalogo
    Guid ActividadId,                   // referencia ActividadCatalogo (NUEVO)
    string? ObservacionLibre,           // si el técnico necesita comentar más
    Severidad Severidad,
    string Descripcion,
    string EmitidoPor, DateTime RegistradoEn);
```

El value object `Hallazgo` también pasa a tener `ActividadId` en vez de `ActividadDescripcion`.

### 12.4 Cambios al inventario de APIs (referencia a §9.13 de `00-investigacion-mercado.md`)

El endpoint `GET /api/v1/equipos/{id}/partes` cambia de naturaleza. Lo correcto, dado el modelo real:

```
GET /api/v1/equipos/{equipoCodigo}
GET /api/v1/equipos/{equipoCodigo}/rutinas-aplicables
       → Devuelve las rutinas (preop + técnicas) que aplican a este equipo
         según su grupo+modelo, con sus items (parte + actividad).

GET /api/v1/rutinas?grupo={g}&modelo={m}&tipo={preop|tecnica}
       → Búsqueda de rutinas por grupo/modelo/tipo.

GET /api/v1/rutinas/{rutinaCodigo}
       → Detalle de la rutina con items.

GET /api/v1/catalogos/partes?q=
GET /api/v1/catalogos/actividades?q=
       → Búsqueda en catálogos para autocomplete cuando el técnico
         registra un hallazgo proactivo fuera de la rutina.

GET /api/v1/insumos?aplicaMye=requerido&parteId=&q=
       → Búsqueda en catálogo de repuestos/insumos, filtrable por
         AplicaMYE. Reemplaza al "GET /repuestos" anterior.
```

Esto **reduce dos endpoints y agrega tres** respecto al inventario anterior. Hay que actualizar §9.13 cuando esté validado contigo.

### 12.5 Implicaciones para el flujo del técnico

1. **Al iniciar una inspección**: el módulo carga la **rutina aplicable** al equipo (`GET /equipos/{cod}/rutinas-aplicables` filtrada por `tipo=tecnica`). El técnico recorre los items de la rutina como SOPs paso a paso (estilo Tractian).
2. **Para hallazgos proactivos fuera de la rutina**: el selector usa `GET /catalogos/partes` y `GET /catalogos/actividades` con autocomplete. NO se permite texto libre como código — solo como observación.
3. **Para repuestos**: por defecto el filtro `AplicaMYE=requerido`. Toggle para mostrar opcionales.

### 12.6 Respuestas del usuario que cierran la reconciliación (2026-04-27)

1. **Rutinas son formato estándar; la ejecución es lo que varía.** La rutina (preop o técnica) define un set fijo de items `(Parte, Actividad)` para un grupo. En cada ejecución, quien la corre (operario o técnico) recorre los items y selecciona cuáles tienen novedad/hallazgo, agregando documentos cuando aplica o solo observaciones. El catálogo de rutinas es compartido — la rutina técnica es un nuevo tipo dentro del mismo concepto. **Implicación**: el aggregate `Rutina` con `Tipo = Preoperacional | Tecnica` queda confirmado. Solo las actividades con novedad/hallazgo emiten eventos en el stream; las OK se infieren por ausencia respecto al snapshot de la rutina cargada al iniciar.

2. **El selector de repuestos muestra ambos** (requerido y opcional). El campo `AplicaMYE` queda en el modelo como informativo pero **no filtra la UX por defecto**. El usuario considera que el campo podría ser innecesario — se mantiene en el sync por si en el futuro se usa, pero el módulo no toma decisiones de negocio sobre él.

3. **`Parte` y `Actividad` son catálogos cerrados.** Confirmado. El técnico no crea partes ni actividades. Si encuentra algo no catalogado, queda como `ObservacionLibre` en el hallazgo y se escala manualmente al admin del catálogo.

4. **`ubicación` es un catálogo de ubicaciones del ERP**, no texto libre. Se modela como `UbicacionId` con su propia proyección local.

5. **No hay campo `Modelo` del equipo en estos formatos.** La columna que había interpretado como "modelo" (D65PX2) es en realidad `Rutina Mantenimiento` — un código de rutina, no del modelo del equipo. Eso significa que el dominio **no necesita campo `Modelo`** y la rutina se asocia solo al `Grupo`. Los aggregates ajustados se ven abajo.

6. **Existe catálogo de obras en el ERP.** Se modela como `ObraLocal` con su propia proyección.

> ⚠️ **Nota:** El §12.7 representó el modelo final tras reconciliar las plantillas Excel. Tras revisar `Plantillas Excel/imagenes app.docx` (mockups reales de la app de Sinco MYE), **el modelo de `Hallazgo` se refinó nuevamente en §12.9**. Los cambios principales: `Severidad` se reemplaza por `AccionRequerida` (3 valores accionables), se agregan `CausaFalla`, `TipoFalla`, `AccionCorrectiva`, `NovedadTecnicaDescripcion`, `ObservacionCampo`, y la captura es un **wizard de 2 pasos**. Léase §12.9 para el contrato vigente. Las estructuras de Equipo, Rutina, Ubicación, Obra, Repuesto siguen tal cual están en §12.7.

### 12.7 Modelo final ajustado tras la reconciliación (*Hallazgo superseded por §12.9*)

#### `EquipoLocal` (final)

```csharp
public sealed record EquipoLocal(
    Guid EquipoId,                      // Id técnico interno
    string CodigoSinco,                 // "1" — clave natural
    string? Placa,                      // "xemp899" — opcional
    string Descripcion,
    string GrupoMantenimiento,          // "BULLDOZER" — clave para rutina
    Guid UbicacionId,                   // referencia a UbicacionLocal
    Guid? ObraActualId,                 // referencia a ObraLocal (asignación dinámica)
    decimal? HorometroActual,
    Dictionary<string, string> AtributosExtra,
    DateTime SincronizadoEn);
```

Sin campo `Modelo`. La rutina aplicable se determina por `GrupoMantenimiento`.

#### `Rutina` (*campo `Tipo` y enum `TipoRutina` superseded por §12.11.1*)

```csharp
public sealed record Rutina(
    Guid RutinaId,
    string Codigo,                          // "PERO. BULL", "INSP. BULL", "D65PX2"
    string Nombre,
    // ❌ El campo Tipo se eliminó del modelo del módulo en §12.11.1.
    //    Las únicas rutinas que el módulo consume son técnicas implícitamente.
    string GrupoMantenimiento,              // "BULLDOZER" — único atributo de aplicabilidad
    Guid? RutinaPadreId,                    // si hay derivación entre rutinas
    IReadOnlyList<ItemRutina> Items,
    DateTime SincronizadoEn);

public sealed record ItemRutina(
    Guid ItemId,                            // estable en el catálogo
    Guid ParteId,                           // referencia a ParteCatalogo
    Guid ActividadId,                       // referencia a ActividadCatalogo
    bool Obligatorio,
    string? Instruccion);

// ❌ enum TipoRutina eliminado (ver §12.11.1)
```

#### `UbicacionLocal` y `ObraLocal` (nuevos)

```csharp
public sealed record UbicacionLocal(
    Guid UbicacionId,
    string Codigo,
    string Nombre,
    DateTime SincronizadoEn);

public sealed record ObraLocal(
    Guid ObraId,
    string Codigo,
    string Nombre,
    Guid? UbicacionId,                  // si la obra tiene ubicación geográfica catalogada
    bool Activa,
    DateTime SincronizadoEn);
```

#### `RepuestoLocal.AplicaMYE` — nota de uso

Se mantiene en el sync pero el módulo **no lo usa para filtrar selectores**. El selector muestra todos los repuestos del catálogo (filtrados solo por `parteId` y término de búsqueda). Si el usuario decide más adelante que sí filtra, el cambio es de UX, no de modelo.

#### Modelo de la "ejecución de la rutina" en `InspeccionTecnica`

Como cada rutina es un set fijo de items y solo los items con hallazgo emiten eventos, el aggregate **carga la rutina al iniciar** (snapshot embebido en el evento `InspeccionIniciada_v1`) para que las actividades en estado OK estén disponibles para reportes:

```csharp
public sealed record InspeccionIniciada_v1(
    Guid InspeccionId, string IniciadaPor,
    UbicacionGps? Ubicacion,
    Guid RutinaId,                          // referencia
    string RutinaCodigo,
    IReadOnlyList<ItemRutina> ItemsSnapshot, // congelado al iniciar
    DateTime IniciadaEn);
```

Esto resuelve el problema de **versionado de plantillas** que había marcado como pendiente: si la rutina cambia mañana, las inspecciones en curso tienen el snapshot de la versión que las inició.

Cuando el técnico marca una actividad de la rutina con hallazgo:

```csharp
public sealed record HallazgoEnRutina_v1(   // sustituye a HallazgoDescubierto_v1 cuando viene de rutina
    Guid InspeccionId, Guid HallazgoId,
    Guid ItemRutinaId,                      // referencia al snapshot
    Guid ParteId, Guid ActividadId,         // duplicado para query directo
    Severidad Severidad,
    string? ObservacionLibre,
    string EmitidoPor, DateTime RegistradoEn);
```

Y cuando el técnico descubre algo **fuera** de la rutina:

```csharp
public sealed record HallazgoFueraDeRutina_v1(  // sustituye a HallazgoDescubierto_v1 cuando es ad-hoc
    Guid InspeccionId, Guid HallazgoId,
    Guid ParteId, Guid ActividadId,
    Severidad Severidad,
    string? ObservacionLibre,
    string EmitidoPor, DateTime RegistradoEn);
```

Diferenciar los dos eventos da reporte limpio: "cuántos hallazgos vienen de la rutina vs. cuántos descubre el técnico fuera de ella" sin tener que mirar campos opcionales.

### 12.8 Inventario de APIs revisado (sustituye §9.13 del doc de investigación, *actualizado en §12.9*)

```
GET  /api/v1/equipos?codigo=&grupo=&page=&size=
GET  /api/v1/equipos/{equipoCodigo}
       → trae equipo con grupoMantenimiento, ubicacionId, obraActualId

GET  /api/v1/rutinas?grupo={g}&tipo={preoperacional|tecnica|mantenimiento}
GET  /api/v1/rutinas/{rutinaCodigo}
       → trae rutina con items (parteId, actividadId)

GET  /api/v1/catalogos/partes?q=
GET  /api/v1/catalogos/actividades?q=
GET  /api/v1/catalogos/ubicaciones
GET  /api/v1/catalogos/obras
       → catálogos cerrados, sincronizados a Azure como proyecciones locales

GET  /api/v1/insumos?parteId=&q=
       → catálogo de repuestos/insumos. AplicaMYE viene en la respuesta pero
         no filtra; queda como atributo informativo.

GET  /api/v1/preop/novedades?obra=&estado=&page=&size=
GET  /api/v1/preop/novedades/{id}
GET  /api/v1/preop/adjuntos/{id}
POST /api/v1/preop/novedades/{id}/verificar

POST /api/v1/mye/ot-correctivas
       → con BOM consolidado al cerrar la inspección

GET  /api/v1/admin/usuarios?desde={lastSync}
       → para sync de identidades hacia Entra (ADR-002)
```

Total: **15 endpoints** en cuatro módulos Sinco. Pendiente actualizar §9.13 del doc principal con esta lista. **Nota:** §12.9.7 actualiza este inventario a 17 endpoints sumando catálogos de causa/tipo de falla.

---

### 12.9 Refinamiento del Hallazgo tras revisar la app actual de Sinco MYE (2026-04-27)

`Plantillas Excel/imagenes app.docx` contiene cuatro mockups reales de la app móvil de Sinco MYE — el módulo "Inspecciones" como entrada en el home, la pantalla de inspección de equipo, y un wizard de dos pasos para registrar un hallazgo. Esto permite alinear el modelo a la UX real en lugar de inferirlo.

#### 12.9.1 Cambios principales al `Hallazgo`

**Reemplazo: `Severidad` → `AccionRequerida`.** El concepto que la app usa es más accionable: en lugar de etiquetar una severidad cualitativa, se pregunta directamente qué se hace con el hallazgo. Tres valores con descripción al usuario:

| Valor | Texto en UI | Implicación |
|---|---|---|
| `NoRequiereIntervencion` (verde) | "El hallazgo se registra, pero no requiere acción correctiva inmediata." | Queda en histórico. No genera OT. |
| `RequiereSeguimiento` (naranja) | "Se debe monitorear la condición. No es urgente pero requiere atención." | Queda en bandeja del supervisor para reinspección programada. No genera OT. |
| `RequiereIntervencion` (rojo) | "Se requiere acción correctiva formal. Puede derivar a orden de trabajo." | Aporta línea al BOM del `CrearOTCorrectivaRequest_v1` enviado a MYE al cerrar. |

**Nuevos campos del `Hallazgo`:**

- `NovedadTecnicaDescripcion` (texto libre obligatorio) — "Describe la novedad técnica observada".
- `AccionCorrectiva` (texto libre obligatorio cuando `AccionRequerida = RequiereIntervencion`) — "Describe la acción correctiva necesaria".
- `CausaFallaId` (catálogo cerrado, obligatorio en paso 2).
- `TipoFallaId` (catálogo cerrado, obligatorio en paso 2).
- `ObservacionCampo` (texto libre opcional) — "Observaciones / Nota de campo".

**Eliminado / aclarado:**

- `Severidad` desaparece del aggregate. Si se necesita para reportes/dashboards, se deriva: `RequiereIntervencion` se mapea a alta-crítica, `RequiereSeguimiento` a media, `NoRequiereIntervencion` a baja.
- `ActividadId` **NO** se captura en el registro de hallazgo. Cuando el hallazgo viene de un item de rutina, la actividad está implícita en el `ItemRutinaId`. Para hallazgos ad-hoc (fuera de rutina), no hay actividad — solo Parte + texto libre.

#### 12.9.2 Value object `Hallazgo` (*superseded por §12.10.3*)

> El `Hallazgo` definido aquí incluía `ItemRutinaId` y permitía `ActividadId` opcional. Ambos cambios se revisaron en §12.10: `ItemRutinaId` se elimina (la rutina técnica no es checklist con items específicos) y `ActividadId` pasa a obligatorio. Léase §12.10.3 para el contrato vigente.

```csharp
public sealed record Hallazgo(
    Guid HallazgoId,
    OrigenHallazgo Origen,                          // PreOperacional | Manual
    Guid? NovedadPreopId,                           // si Origen == PreOperacional
    Guid? ItemRutinaId,                             // si fue capturado desde rutina
    Guid ParteId,                                   // siempre, del catálogo
    string NovedadTecnicaDescripcion,               // texto libre obligatorio
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,                       // obligatorio si AccionRequerida = RequiereIntervencion
    Guid? CausaFallaId,                             // obligatorio en paso 2 si AccionRequerida = RequiereIntervencion
    Guid? TipoFallaId,                              // obligatorio en paso 2 si AccionRequerida = RequiereIntervencion
    string? ObservacionCampo,
    IReadOnlyList<Guid> AdjuntosIds,
    IReadOnlyList<RepuestoEstimado> Repuestos,
    DateTime RegistradoEn);

public enum AccionRequerida
{
    NoRequiereIntervencion,
    RequiereSeguimiento,
    RequiereIntervencion
}
```

#### 12.9.3 Eventos refinados (*superseded por §12.10.2*)

> Los eventos `HallazgoEnRutina_v2` y `HallazgoFueraDeRutina_v2` se consolidan en un único `HallazgoRegistrado_v1` en §12.10.2. Razón: tras aclaración del usuario el 2026-04-27, las rutinas técnicas no son checklists de items sino filtros del catálogo de partes — no hay vínculo a un "item de rutina" que justifique distinguir hallazgos. Léase §12.10.2 para el contrato vigente.

```csharp
// Reemplaza HallazgoEnRutina_v1
public sealed record HallazgoEnRutina_v2(
    Guid InspeccionId, Guid HallazgoId,
    Guid ItemRutinaId,
    Guid ParteId,
    string NovedadTecnicaDescripcion,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor, DateTime RegistradoEn);

// Reemplaza HallazgoFueraDeRutina_v1
public sealed record HallazgoFueraDeRutina_v2(
    Guid InspeccionId, Guid HallazgoId,
    Guid ParteId,
    string NovedadTecnicaDescripcion,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor, DateTime RegistradoEn);
```

Las versiones `_v1` se descartan al inicio del proyecto (no estaban en producción aún). Si aparecen en código posterior, agregar upcaster.

#### 12.9.4 Invariantes ajustadas

La invariante I7 cambia:

| Antes (severidad) | Ahora (acción requerida) |
|---|---|
| Si hay al menos un hallazgo `Critica`, el dictamen debe ser `NoPuedeOperar` | Si hay al menos un hallazgo con `AccionRequerida = RequiereIntervencion`, **se debe generar OT correctiva** al cerrar la inspección |

El concepto de `DictamenOperacion` se mantiene como decisión separada del técnico al cerrar. La regla exacta de "no contradecirse" (cuándo hallazgos `RequiereIntervencion` impiden `Dictamen = PuedeOperar`) vale la pena validarla con técnicos reales — no es derivable solo de los mockups.

**Nuevas invariantes (refinadas 2026-04-27 con regla condicional confirmada por el usuario):**

- I9: Si `AccionRequerida = RequiereIntervencion`, entonces `AccionCorrectiva`, `CausaFallaId` y `TipoFallaId` son **obligatorios** y no nulos.
- I10: Si `AccionRequerida` es `RequiereSeguimiento` o `NoRequiereIntervencion`, **NO se pueden cargar** `AccionCorrectiva`, `CausaFallaId`, `TipoFallaId` ni `Repuestos`. Estos campos deben quedar null/vacíos. El handler rechaza el comando si vienen poblados con esa combinación.
- I11: Cualquier hallazgo requiere `NovedadTecnicaDescripcion` no vacía.

**Regla resumen:**

| AccionRequerida | AccionCorrectiva | CausaFalla | TipoFalla | Insumos |
|---|---|---|---|---|
| `NoRequiereIntervencion` | ❌ no permitido | ❌ no permitido | ❌ no permitido | ❌ no permitido |
| `RequiereSeguimiento` | ❌ no permitido | ❌ no permitido | ❌ no permitido | ❌ no permitido |
| `RequiereIntervencion` | ✅ obligatorio | ✅ obligatorio | ✅ obligatorio | ⚠️ al menos uno recomendable |

#### 12.9.5 UX: wizard condicional según acción requerida

El registro de hallazgo es un **wizard de 1 o 2 pasos según la acción requerida**:

| Paso | Nombre | Captura | Cuándo aplica |
|---|---|---|---|
| **1** | Identificación | Parte, Novedad técnica (descripción), Acción requerida (3 opciones), Observación de campo | Siempre |
| **2** | Análisis técnico | Acción correctiva, Causa de falla, Tipo de falla, Insumos requeridos | **Solo si `AccionRequerida = RequiereIntervencion`** |

**Comportamiento del paso 1 según selección:**

- Si selecciona **`NoRequiereIntervencion`** → el botón inferior dice **"Guardar hallazgo"** y el wizard termina ahí.
- Si selecciona **`RequiereSeguimiento`** → el botón inferior dice **"Guardar hallazgo"** y el wizard termina ahí.
- Si selecciona **`RequiereIntervencion`** → el botón cambia a **"Continuar"** y aparece el paso 2.

El subtítulo del wizard refleja el ramal: "Paso 1 de 1" o "Paso 1 de 2".

**Implicación para el modelo de comandos:** el aggregate sigue recibiendo **un solo comando** `RegistrarHallazgo` con todos los campos opcionales del paso 2 que aplica. El handler valida las invariantes I9 e I10. El wizard es UX puro — el modelo de dominio no sabe de pasos. El paso intermedio (si hay) se guarda como draft local en el cliente móvil; si se necesita persistencia cross-device, se modela `HallazgoEnBorrador_v1` opcional — por ahora draft local es suficiente.

#### 12.9.6 Catálogos cerrados nuevos

```csharp
public sealed record CausaFallaCatalogo(
    Guid CausaFallaId,
    string Codigo,                          // "DESGASTE_NORMAL", "FALLA_LUBRICACION", etc.
    string Descripcion,
    DateTime SincronizadoEn);

public sealed record TipoFallaCatalogo(
    Guid TipoFallaId,
    string Codigo,                          // "MECANICA", "HIDRAULICA", "ELECTRICA", etc.
    string Descripcion,
    DateTime SincronizadoEn);
```

**Estado de existencia en Sinco: confirmado existentes (2026-04-27).** Ambos catálogos ya viven en el ERP. El workstream B-2 solo necesita exponerlos vía REST — no hay que modelarlos desde cero. Es trabajo de exposición, no de diseño de datos.

#### 12.9.7 Inventario de APIs actualizado (sustituye §12.8)

```
GET  /api/v1/equipos?codigo=&grupo=&page=&size=
GET  /api/v1/equipos/{equipoCodigo}

GET  /api/v1/rutinas?grupo={g}&tipo={preoperacional|tecnica|mantenimiento}
GET  /api/v1/rutinas/{rutinaCodigo}

GET  /api/v1/catalogos/partes?q=
GET  /api/v1/catalogos/causas-falla                    ◀ exponer catálogo existente
GET  /api/v1/catalogos/tipos-falla                     ◀ exponer catálogo existente
GET  /api/v1/catalogos/ubicaciones
GET  /api/v1/catalogos/obras

GET  /api/v1/insumos?parteId=&q=

GET  /api/v1/preop/novedades?obra=&estado=&page=&size=
GET  /api/v1/preop/novedades/{id}
GET  /api/v1/preop/adjuntos/{id}
POST /api/v1/preop/novedades/{id}/verificar

POST /api/v1/mye/ot-correctivas

GET  /api/v1/admin/usuarios?desde={lastSync}
```

**Total: 17 endpoints** (vs. 15 anteriores). Se mantienen los 15 + dos nuevos catálogos.

#### 12.9.8 Patrones visuales adoptados de la app real

- **Color primario**: azul Sinco intenso (~`#0D47A1`). El `#1976d2` que usé era demasiado claro.
- **Header gradient**: azul claro → blanco en pantallas internas. Da aire y diferencia del primary fuerte del app bar.
- **Cards**: bordes sutiles, no elevation fuerte. Estilo Material 3 outlined.
- **Asterisco rojo** (`*`) marca campo obligatorio.
- **Iconos ilustrados** (no flat) en home — estilo más cálido/humano. En pantallas internas, iconos Material estándar.
- **Botones**: filled primary o outlined, esquinas ligeramente redondeadas (~6-8px).
- **Empty state ilustrado** con bombilla cuando no hay hallazgos.
- **Identificación del equipo**: formato `Marca Modelo` + `Placa • Código` debajo (ej: "Caterpillar D11T Custom" / "HD-9908-TX • M-109").

#### 12.9.9 Preguntas abiertas tras esta revisión

1. ~~¿`CausaFalla` y `TipoFalla` existen ya como catálogos?~~ **Confirmado existentes (2026-04-27).** Solo hay que exponerlos vía REST. Comentario del usuario: en el diseño original del módulo no se habían considerado — aparecieron al revisar la app real, validando el valor de la reconciliación contra mockups.
2. **¿La regla "AccionRequerida = RequiereIntervencion → genera OT"** se valida cómo en la UX cuando el técnico cierra la inspección? ¿Hay un step de revisión del BOM consolidado antes del POST a MYE?
3. **¿Existe la pantalla de "Importar desde preoperacional"** en el módulo? El botón aparece en mockup 2 pero la pantalla destino no está en los mockups compartidos.
4. **¿Hay pantalla de cierre / firma / dictamen** ya diseñada en Sinco MYE, o esa parte aún no está mockeada?
5. **El `Dictamen` de operación** (PuedeOperar/ConRestriccion/NoPuedeOperar) — ¿lo usa Sinco MYE o solo el módulo nuevo?

---

### 12.11 Eliminación de `TipoRutina` del módulo + `TipoInspeccion` en el aggregate (2026-04-27)

> **Nota de orden:** esta sección está físicamente **antes** de §12.10 por orden cronológico de edición pero numéricamente posterior. Para una lectura coherente: léase §12.10 primero (consolidación del evento de hallazgo y simplificación de rutina), luego esta sección §12.11. Los cambios de §12.11 son **adicionales** a los de §12.10, no los reemplazan.

Decisión del usuario: eliminar el enum `TipoRutina` del modelo del módulo (todas las rutinas que el módulo consume son técnicas implícitamente) y agregar un nuevo enum `TipoInspeccion` al aggregate `Inspeccion` que distingue qué flujo se está ejecutando.

#### 12.11.1 Eliminación de `TipoRutina` del modelo del módulo

**Razón:** el campo `Tipo` en el `Rutina` aggregate generaba confusión. Mezclaba dos conceptos:
- Metadato del catálogo del ERP (donde Sinco organiza sus rutinas como Preoperacional, Mantenimiento, etc.).
- Flujo operativo (qué hace el técnico).

**Decisión:** el módulo nuevo solo conoce **rutinas técnicas**. Las rutinas preoperacionales y de mantenimiento existen en el catálogo de Sinco, pero **no son consumidas por el módulo de inspecciones técnicas**:

- Las rutinas preoperacionales viven en el módulo Preoperacional de Sinco MYE (otro flujo, otro consumidor).
- Las rutinas de mantenimiento viven en MYE núcleo o módulo de planificación (otro flujo, otro consumidor).

El endpoint que el módulo consume filtra del lado ERP: `GET /api/v1/rutinas-tecnicas?grupo={g}` o `GET /api/v1/rutinas?tipo=tecnica&grupo={g}`. La selección del filtro se documenta en §9.13 / §12.9.7.

**Cambios al value object `Rutina` (final):**

```csharp
public sealed record Rutina(
    Guid RutinaId,
    string Codigo,                          // "INSP. BULL"
    string Nombre,
    string GrupoMantenimiento,              // único atributo de aplicabilidad
    Guid? RutinaPadreId,                    // si hay derivación entre rutinas
    IReadOnlyList<ItemRutina> Items,
    DateTime SincronizadoEn);

// El enum TipoRutina se elimina del modelo del módulo.
```

#### 12.11.2 Nuevo enum `TipoInspeccion` en el aggregate

**Razón:** preparar el modelo para soportar tipos de inspección distintos al actual (técnica), sin tocar `Rutina`. El campo distintivo va en el aggregate `Inspeccion`, no en `Rutina`.

```csharp
public enum TipoInspeccion
{
    Tecnica          // único valor activo en MVP
    // , Monitoreo  // futuro — ver §12.11.3
}
```

**Cambios al aggregate (que conceptualmente se renombra de `InspeccionTecnica` a `Inspeccion`):**

```csharp
public sealed class Inspeccion
{
    public Guid InspeccionId        { get; private set; }
    public TipoInspeccion Tipo      { get; private set; }   // NUEVO — Tecnica por ahora
    public Guid EquipoId            { get; private set; }
    public Guid RutinaId            { get; private set; }
    // ... resto idéntico
}
```

> **Nota práctica:** mientras solo exista `Tecnica`, el campo siempre vale lo mismo. No fuerza ningún rename de archivos en código todavía. Cuando se priorice Monitoreo:
> - El campo permite distinguir comportamiento del aggregate.
> - El handler de comandos despacha según el tipo (qué comandos son válidos).
> - Las proyecciones agrupan o separan por tipo según convenga.

#### 12.11.3 Cambios al evento `InspeccionIniciada_v1`

```csharp
public sealed record InspeccionIniciada_v1(
    Guid InspeccionId,
    TipoInspeccion Tipo,                    // NUEVO — siempre Tecnica en MVP
    Guid EquipoId,
    Guid RutinaId,
    string RutinaCodigo,
    string TecnicoIniciador,
    Guid ObraId,
    UbicacionGps Ubicacion,
    DateTime IniciadaEn);
```

El handler de `IniciarInspeccion` siempre lo emite con `Tipo = TipoInspeccion.Tecnica`. Cuando se priorice Monitoreo:
- Aparece un comando hermano `IniciarInspeccionMonitoreo` (o el mismo `IniciarInspeccion` con un parámetro `Tipo`).
- El evento se emite con `Tipo = TipoInspeccion.Monitoreo`.
- Los comandos posteriores válidos cambian (por ejemplo, `TomarMedicion` solo aplica a Monitoreo, `RegistrarHallazgo` libre solo aplica a Tecnica).

#### 12.11.4 Lo que NO cambia con esta decisión

- **`Origen` del Hallazgo se mantiene** intacto: `PreOperacional` o `Manual`. Es independiente de `TipoInspeccion`. Una inspección de tipo `Tecnica` sigue acumulando hallazgos con ambos orígenes.
- **El comando `IniciarInspeccion` actual no agrega `Tipo`** porque solo hay un valor. Cuando se priorice Monitoreo, se agrega.
- **Las invariantes I1-I12** siguen válidas tal como están.
- **Los demás eventos** (`HallazgoRegistrado_v1`, `MedicionRegistrada_v1`, etc.) no cambian estructura. Lo único que cambia es: **algunos serán específicos al tipo de inspección** cuando llegue Monitoreo (ej. `MedicionTomada_v1` futura solo aplica a Monitoreo).

#### 12.11.5 Camino de migración para soportar Monitoreo

Cuando se priorice (escenario futuro discutido en sesión 2026-04-27, no aplicado todavía):

1. Agregar `Monitoreo` al enum `TipoInspeccion`.
2. Extender `ItemRutina` con `MedicionEsperada?` (opcional, poblado solo cuando la rutina es de monitoreo).
3. Definir nuevos eventos específicos para Monitoreo: `MedicionTomada_v1`, `ItemMonitoreoOmitido_v1`.
4. Decidir si `Rutina` se reusa para ambos tipos o si se introduce `RutinaMonitoreo` como entidad separada (decisión a evaluar entonces).
5. Snapshot condicional de items en `InspeccionIniciada_v1` cuando `Tipo == Monitoreo`.
6. Pantalla móvil específica para flujo de monitoreo (checklist con captura de mediciones).

**Nada de esto requiere reescribir código actual** — todo es aditivo. La introducción del campo `TipoInspeccion` en el aggregate desde ya es la única "preparación" que hacemos.

#### 12.11.6 Catálogo final de eventos del aggregate `Inspeccion`

```
1.  InspeccionIniciada_v1                  ◀ con campo Tipo (TipoInspeccion)
2.  HallazgoRegistrado_v1                  ◀ con ResultadoVerificacion si Origen=PreOperacional
3.  HallazgoActualizado_v1                 ◀ edición de campos del hallazgo
4.  HallazgoEliminado_v1                   ◀ soft delete con motivo
5.  MedicionRegistrada_v1                  ◀ DIFERIDO a v1.x (§12.10.15)
6.  AdjuntoAgregado_v1                     ◀ foto/PDF (3 MB max, max 5 por hallazgo, EXIF preservado)
7.  AdjuntoEliminado_v1                    ◀ soft delete adjunto con motivo (§12.10.11)
8.  RepuestoEstimadoAgregado_v1            ◀ agregar repuesto a hallazgo
9.  RepuestoEstimadoRemovido_v1            ◀ hard delete repuesto (§12.10.13)
10. RepuestoEstimadoActualizado_v1         ◀ editar cantidad/justificación (§12.10.14)
11. DiagnosticoEmitido_v1
12. DictamenEstablecido_v1
13. InspeccionFirmada_v1
14. InspeccionCerrada_v1                   ◀ con OTCorrectivaIdSinco + OTCorrectivaNumero
15. InspeccionCerradaSinOT_v1
16. InspeccionCancelada_v1
17. OTGeneracionFallida_v1
```

**Total histórico: 17 eventos del aggregate Inspeccion en este snapshot intermedio.** ⚠️ Superseded por §15.4 — el catálogo MVP final son **20 eventos** (16 de Inspeccion + 1 NovedadPreopDescartada + 3 del aggregate Seguimiento). Cambios respecto a versiones anteriores capturados aquí: `NovedadPreopVerificada_v1` se eliminó (§12.10.9), `TecnicoSeIncorporo_v1` se eliminó (lista derivada), `HallazgoActualizado_v1`/`HallazgoEliminado_v1` para edición de hallazgos, `AdjuntoEliminado_v1` para soft delete adjuntos, `RepuestoEstimadoActualizado_v1` para editar repuestos sin remover (§12.10.14). `InspeccionIniciada_v1` con campo `Tipo`. `InspeccionCerrada_v1` con `OTCorrectivaIdSinco` y `OTCorrectivaNumero`.

---

### 12.10 Consolidación del evento de hallazgo y simplificación de rutina (2026-04-27)

Decisión del usuario:
> "En las inspecciones las rutinas solo sirven para traer las partes, o las actividades que cargan del preoperacional. Pero si se realiza manualmente esta actividad se carga manualmente. No es necesario guardar versión de rutina porque la que viene desde el preoperacional también será editable."

Esto cambia significativamente el modelo del hallazgo y el rol de la rutina técnica.

#### 12.10.1 Rol revisado de la `Rutina` técnica

**Antes:** la rutina técnica era una checklist de items `(Parte, Actividad)` que el técnico debía recorrer paso a paso (estilo SOPs Tractian).

**Ahora:** la rutina técnica es un **filtro del catálogo de partes aplicables al grupo del equipo**. Sirve para:

- Determinar qué partes aparecen en el selector cuando el técnico crea un hallazgo manual.

NO sirve para:
- Forzar al técnico a recorrer items en orden (no es checklist).
- Determinar las actividades disponibles (en hallazgos manuales la actividad es texto libre; en hallazgos del preop es heredada del catálogo del preop).

**Cardinalidad: una rutina técnica por grupo de mantenimiento (decisión 2026-04-27).** BULLDOZER tiene una sola rutina técnica que define todas las partes aplicables. No hay subdivisiones por enfoque (motor / hidráulica) — el técnico decide qué inspeccionar dentro de toda la rutina. La rutina **se deriva automáticamente** del grupo del equipo al iniciar; el técnico no la selecciona.

Si en el futuro aparece la necesidad de **rutinas distintas por contexto** (post-mantenimiento, certificación periódica, etc.), será cambio aditivo: se reabre la selección y se ajusta el comando.

Para las rutinas Preoperacionales, los items con `(Parte, Actividad)` siguen vigentes — ese módulo sí funciona como checklist y ya está en producción.

#### 12.10.2 Consolidación del evento de hallazgo

**Eliminados:**
- `HallazgoEnRutina_v2` (no hay vínculo a item de rutina específico)
- `HallazgoFueraDeRutina_v2` (no hay distinción rutina/fuera)
- `HallazgoDescubierto_v1` (versión histórica)

**Reemplazados por uno solo:**

```csharp
public sealed record HallazgoRegistrado_v1(
    Guid InspeccionId, Guid HallazgoId,
    OrigenHallazgo Origen,                          // PreOperacional | Manual
    Guid? NovedadPreopId,                           // poblado si Origen == PreOperacional (I12b)
    ResultadoVerificacion? ResultadoVerificacion,   // poblado si Origen == PreOperacional (I12c)
    Guid ParteId,                                   // siempre, validado aplicable a la rutina del aggregate
    Guid? ActividadId,                              // poblado si Origen == PreOperacional
    string? ActividadDescripcion,                   // poblado si Origen == Manual (texto libre)
    string NovedadTecnica,                          // texto libre obligatorio
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,                        // GPS al registrar (opcional)
    string EmitidoPor, DateTime RegistradoEn);
```

**De dónde vienen los campos:**

| Campo | Origen `PreOperacional` (verificar novedad) | Origen `Manual` |
|---|---|---|
| ParteId | Heredada de la novedad preop, editable | Elegida del catálogo, filtrada por la rutina técnica |
| **ActividadId** | Heredada de la novedad preop (referencia al catálogo); editable a otra del catálogo si el técnico lo decide | **null — no se usa** |
| **ActividadDescripcion** | **null — no se usa** | **Texto libre escrito por el técnico** (no del catálogo) |
| NovedadPreopId | Poblado | null |
| Demás campos | Capturados por el técnico al verificar | Capturados por el técnico al crear |

**Invariantes de coherencia:**

**I12** (xor de Actividad según Origen):
- Si `Origen == PreOperacional`: `ActividadId` no nulo, `ActividadDescripcion` debe ser null.
- Si `Origen == Manual`: `ActividadDescripcion` no nulo y no vacío, `ActividadId` debe ser null.

**I12b** (xor de NovedadPreopId según Origen):
- Si `Origen == PreOperacional`: `NovedadPreopId` no nulo.
- Si `Origen == Manual`: `NovedadPreopId` debe ser null.

**I13** (Parte aplicable a la rutina):
- `ParteId` debe estar en la lista de partes aplicables a la `RutinaId` del aggregate. Validación contra `IReferenceDataService.PartesDeRutina(state.RutinaId)`.

**Razón:** las novedades del preoperacional ya tienen actividad codificada (catálogo cerrado del preop, ver `preoperacional.xlsx` con valores como VERIFICACION ESTADO, CAMBIO DE REPUESTO). Cuando el técnico crea un hallazgo manual no atado a una novedad, **describe en texto libre** lo que encontró — no se le pide elegir de un catálogo. Esto refleja la práctica real: el técnico no piensa en términos de "actividad de catálogo" cuando descubre algo, piensa en términos de "qué encontré".

#### 12.10.3 Cambios al value object `Hallazgo`

```csharp
public sealed record Hallazgo(
    Guid HallazgoId,
    OrigenHallazgo Origen,
    Guid? NovedadPreopId,
    Guid ParteId,
    Guid? ActividadId,                              // del catálogo si Origen == PreOperacional
    string? ActividadDescripcion,                   // texto libre si Origen == Manual
    string NovedadTecnicaDescripcion,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    IReadOnlyList<Guid> AdjuntosIds,
    DateTime RegistradoEn);
```

**Eliminados:** `ItemRutinaId` (no hay vínculo a item de rutina).
**Cambiados:**
- `ActividadId` y `ActividadDescripcion` son ambos **nullables**, mutuamente excluyentes (xor).
- Coherencia validada por invariante I12: PreOperacional → ActividadId; Manual → ActividadDescripcion.

#### 12.10.4 Cambios a `InspeccionIniciada_v1`

**Eliminado:** `ItemsSnapshot` (la rutina ya no se "ejecuta" como checklist; no hay nada que congelar).
**Cambiado:** `Ubicacion` pasa de `UbicacionGps?` a `UbicacionGps` (obligatoria — GPS del teléfono al iniciar).
**Mantenido:** `RutinaId` y `RutinaCodigo` como referencia para que el cliente UI sepa qué filtrar.

#### 12.10.5 Implicaciones operativas

- **Los catálogos de partes y actividades pueden evolucionar libremente.** Las inspecciones históricas referencian IDs estables (regla operativa de ADR-004); si se renombra una parte o actividad del catálogo, las inspecciones históricas que la referencian siguen resolviendo correctamente.
- **El selector de parte en el wizard de hallazgo** se filtra por rutina (solo muestra partes aplicables al alcance de la rutina), tanto para hallazgos de origen PreOperacional como Manual.
- **Para hallazgos manuales (Origen Manual):** el campo "Actividad" es **input de texto libre**, no selector de catálogo. El técnico describe lo que encontró en sus propias palabras (ej. "Holgura en valvulería" o "Fuga en racord superior").
- **Para hallazgos del preop (Origen PreOperacional):** la actividad se hereda como referencia al catálogo (ej. `VERIFICACION_ESTADO`). El técnico puede dejarla, o cambiar a otra del catálogo si decide reclasificar. No puede cambiarla a texto libre — para eso el técnico crea un hallazgo manual nuevo.
- **El catálogo de actividades** sigue existiendo en Sinco para el módulo preoperacional y se sincroniza al cloud (ADR-004), pero el módulo de inspecciones técnicas **lo consume solo de lectura** para resolver y mostrar la descripción de la actividad heredada cuando viene del preop. No expone selector de actividades para hallazgos manuales.

#### 12.10.9 Consolidación de `NovedadPreopVerificada_v1` en `HallazgoRegistrado_v1` (2026-04-27)

Decisión: **eliminar el evento `NovedadPreopVerificada_v1`** y consolidar su información en `HallazgoRegistrado_v1` agregando un campo opcional `ResultadoVerificacion`.

**Razón:** redundancia. Cada verificación de novedad emitía dos eventos atómicos (uno para registrar la decisión y otro para crear el hallazgo). Toda la información cabe en un solo evento, con `ResultadoVerificacion` como campo opcional poblado solo cuando `Origen == PreOperacional`.

**Importante: NO se elimina la integración con el ERP.** La saga `CerrarInspeccionSaga` sigue haciendo `POST /api/v1/preop/novedades/{id}/verificar` por cada hallazgo con `Origen == PreOperacional`. El ERP mantiene su requisito de saber qué novedades fueron atendidas para no mostrarlas de nuevo en otra inspección. Lo que cambia es que la saga recorre hallazgos con `Origen=PreOperacional` (en vez de eventos dedicados) y hace el POST con los datos del hallazgo.

##### Nueva enumeración

```csharp
public enum ResultadoVerificacion
{
    Confirmada,            // la novedad era real y el técnico la confirma
    Descartada,            // el operario reportó algo que no era real
    RequiereSeguimiento    // no concluyente, monitoreo en próximas inspecciones
}
```

##### Nuevas invariantes

**I12c** (xor `ResultadoVerificacion` según Origen):
- Si `Origen == PreOperacional`: `ResultadoVerificacion` no nulo.
- Si `Origen == Manual`: `ResultadoVerificacion` debe ser null.

**I14** (coherencia entre Resultado y AccionRequerida cuando Origen=PreOperacional):
- Si `ResultadoVerificacion == Descartada`: `AccionRequerida` debe ser `NoRequiereIntervencion`. Por extensión I10, `AccionCorrectiva`, `CausaFallaId`, `TipoFallaId` y repuestos deben estar vacíos. Si la novedad no era real, no hay nada que arreglar.
- Si `ResultadoVerificacion == Confirmada` o `RequiereSeguimiento`: `AccionRequerida` queda libre (cualquiera de los tres valores).

##### Mapeo `ResultadoVerificacion` → estado de la novedad en el ERP

Cuando la saga al cerrar la inspección hace `POST /preop/novedades/{id}/verificar`, el body lleva el `ResultadoVerificacion`. El ERP del preoperacional debe interpretar así (decisión operativa a coordinar con el equipo del preop):

| Resultado enviado | Estado de la novedad en el ERP | ¿Aparece en la próxima inspección del equipo? |
|---|---|---|
| `Confirmada` | Atendida y cerrada | ❌ No, ya está procesada |
| `Descartada` | Atendida y cerrada (descartada) | ❌ No, ya está procesada |
| `RequiereSeguimiento` | Activa con flag "en seguimiento" | ✅ Sí, sigue apareciendo en la lista — el técnico de la próxima inspección la ve con badge "En seguimiento desde [fecha] por [técnico]" |

**El requisito del ERP de "no mostrarla de nuevo" se cumple para `Confirmada` y `Descartada`.** El caso `RequiereSeguimiento` deliberadamente la mantiene activa porque conceptualmente "no se ha resuelto aún". El operario sigue siendo informado de que persiste el reporte.

##### Wizard de hallazgo cuando viene del flujo "Importar desde preop"

El paso 1 del wizard cambia ligeramente:

1. Selector de **Resultado de verificación** aparece primero (3 opciones). Al elegir:
   - **`Descartada`** → `AccionRequerida` se setea automáticamente a `NoRequiereIntervencion` y queda bloqueada. El paso 2 no aplica. Botón "Guardar hallazgo" disponible.
   - **`Confirmada`** o **`RequiereSeguimiento`** → el técnico elige libremente `AccionRequerida` entre los tres valores. Si `RequiereIntervencion`, aparece paso 2. Si no, "Guardar hallazgo".
2. La parte y actividad vienen heredadas de la novedad (editables).
3. La novedad técnica (descripción) se llena con el diagnóstico del técnico.

##### Comando consolidado

El comando `VerificarNovedadPreoperacional` que existía antes desaparece. Se reemplaza por el comando único `RegistrarHallazgo` con campos adicionales:

```csharp
public sealed record RegistrarHallazgo(
    Guid InspeccionId,
    Guid? NovedadPreopId,                           // null si es manual
    ResultadoVerificacion? ResultadoVerificacion,   // obligatorio si NovedadPreopId no null
    Guid ParteId,
    Guid? ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;
```

El handler valida coherencia (I12, I12b, I12c, I14, I9, I10, I11, I13).

##### Cambio en la saga de cierre

Antes:
```csharp
foreach (var ev in stream.OfType<NovedadPreopVerificada_v1>())
    await preopAcl.MarcarVerificada(ev.NovedadPreopId, ev.Resultado, ev.DiagnosticoEspecifico, ...);
```

Ahora:
```csharp
foreach (var hallazgo in inspeccion.Hallazgos
    .Where(h => h.Origen == OrigenHallazgo.PreOperacional))
{
    await preopAcl.MarcarVerificada(
        hallazgo.NovedadPreopId!.Value,
        hallazgo.ResultadoVerificacion!.Value,
        hallazgo.NovedadTecnica,                     // diagnóstico va aquí
        inspeccion.InspeccionId,
        ...);
}
```

Mismo efecto, una vuelta menos en código.

#### 12.10.11 Decisiones operativas de adjuntos (foto / PDF) (2026-04-27)

Decisiones del usuario para el evento `AdjuntoAgregado_v1` y comando `AdjuntarArchivo`:

| Decisión | Valor | Notas |
|---|---|---|
| **Tipos permitidos** | Imágenes (JPEG, PNG, HEIC, WebP) + PDF | Video diferido a versión posterior |
| **Tamaño máximo por archivo** | **3 MB** | Optimiza red y storage; cliente comprime hasta cumplir |
| **Compresión automática en cliente** | Sí | Redimensionar imagen a max 1920x1920 + JPEG ~75% calidad antes de subir |
| **Límite de adjuntos por hallazgo** | **5** (hard) | Si el hallazgo ya tiene 5 no eliminados, el sexto se rechaza |
| **EXIF data** | Preservar | Geolocalización embedded refuerza el GPS del evento (doble dato) |
| **Eliminación de adjuntos** | Sí (soft delete) | Comando `EliminarAdjunto` + evento `AdjuntoEliminado_v1` |
| **Acceso post-cierre** | Solo lectura | Inspección `Firmada` o `Cerrada` no permite agregar/eliminar adjuntos; sí ver |

##### Validaciones del handler `AdjuntarArchivo`

```csharp
public IEnumerable<object> Handle(AdjuntarArchivo cmd, Inspeccion state, ...)
{
    // I2: Solo en EnEjecucion (acceso post-cierre = solo lectura)
    if (state.Estado != InspeccionEstado.EnEjecucion)
        throw new DomainException("Solo se adjunta en inspección en ejecución");

    // Hallazgo debe existir
    var hallazgo = state.Hallazgos.FirstOrDefault(h => h.HallazgoId == cmd.HallazgoId);
    if (hallazgo is null)
        throw new DomainException($"Hallazgo {cmd.HallazgoId} no existe");

    // Tipos permitidos
    var tiposPermitidos = new[] {
        "image/jpeg", "image/png", "image/heic", "image/webp", "application/pdf"
    };
    if (!tiposPermitidos.Contains(cmd.MimeType))
        throw new DomainException($"Tipo {cmd.MimeType} no permitido");

    // Tamaño máximo 3 MB
    const int MAX_BYTES = 3 * 1024 * 1024;
    if (cmd.TamanoBytes > MAX_BYTES)
        throw new DomainException(
            $"Archivo excede 3 MB (subió {cmd.TamanoBytes / 1024 / 1024:F1} MB). " +
            "El cliente debe comprimir antes de subir.");

    // Límite 5 adjuntos por hallazgo (no eliminados)
    var adjuntosActivos = hallazgo.AdjuntosIds
        .Count(id => !state.AdjuntosEliminados.Contains(id));
    if (adjuntosActivos >= 5)
        throw new DomainException(
            "Hallazgo ya tiene 5 adjuntos. Elimina uno antes de agregar otro.");

    // Idempotencia: AdjuntoId no existe ya
    if (hallazgo.AdjuntosIds.Contains(cmd.AdjuntoId))
        throw new DomainException("AdjuntoId ya registrado");

    yield return new AdjuntoAgregado_v1(
        cmd.InspeccionId, cmd.HallazgoId, cmd.AdjuntoId,
        cmd.BlobUri, cmd.MimeType, cmd.TamanoBytes, cmd.Sha256,
        cmd.Ubicacion,
        cmd.EmitidoPor, DateTime.UtcNow);
}
```

##### Validaciones del handler `EliminarAdjunto`

```csharp
public IEnumerable<object> Handle(EliminarAdjunto cmd, Inspeccion state, ...)
{
    if (state.Estado != InspeccionEstado.EnEjecucion)
        throw new DomainException("Solo se elimina en inspección en ejecución");

    var hallazgo = state.Hallazgos.FirstOrDefault(h => h.HallazgoId == cmd.HallazgoId);
    if (hallazgo is null || !hallazgo.AdjuntosIds.Contains(cmd.AdjuntoId))
        throw new DomainException("Adjunto no existe en este hallazgo");

    if (state.AdjuntosEliminados.Contains(cmd.AdjuntoId))
        throw new DomainException("Adjunto ya fue eliminado");

    if (string.IsNullOrWhiteSpace(cmd.Motivo))
        throw new DomainException("Motivo de eliminación obligatorio");

    yield return new AdjuntoEliminado_v1(
        cmd.InspeccionId, cmd.HallazgoId, cmd.AdjuntoId,
        cmd.Motivo, cmd.EmitidoPor, DateTime.UtcNow);
}
```

##### Patrón de subida cliente → Blob Storage (recapitulado)

1. Cliente PWA solicita SAS al backend con metadata (mimeType, tamanoBytes).
2. Backend valida invariantes preliminares y devuelve URL SAS de escritura (5 min TTL).
3. Cliente comprime/redimensiona imagen si aplica (PDF se sube tal cual).
4. Cliente sube directo al Blob Storage con PUT.
5. Cliente envía `AdjuntarArchivo` con la URI final + sha256 + metadata.
6. Backend valida invariantes finales y emite `AdjuntoAgregado_v1`.

##### Soft delete: el blob no se borra

El evento `AdjuntoEliminado_v1` marca el adjunto como eliminado en el aggregate (HashSet `_adjuntosEliminados`). **El archivo en Blob Storage NO se borra** — queda físicamente para audit y reproducibilidad histórica del stream.

Si se necesita liberar storage físico de adjuntos eliminados (ej. después de N años), Azure Storage Lifecycle Policy puede mover blobs no referenciados a tier "Archive" o eliminarlos según política definida con el equipo de seguridad/compliance Sinco.

##### UX: solo lectura post-cierre

Cuando la inspección está en estado `Firmada` o `Cerrada`, la pantalla 3 (vista de hallazgos) muestra los adjuntos pero:
- No hay botón "+" para agregar más.
- No hay botón "🗑" para eliminar los existentes.
- Los adjuntos se ven en modo galería con zoom para detalle.

Esto se controla en el cliente con base al `Estado` de la inspección — el aggregate ya rechaza los comandos por I2, la UX solo evita renderizar las acciones.

#### 12.10.12 Decisiones operativas de `RepuestoEstimadoAgregado_v1` (2026-04-27)

Decisiones del usuario para el comando `EstimarRepuesto` y su evento:

| Decisión | Valor | Notas |
|---|---|---|
| **`Unidad`** | Eliminada del comando | Se deriva del catálogo `RepuestoLocal.UnidadMedida` al guardar |
| **Compatibilidad SKU↔Parte** | Hard error | El handler rechaza si `ParteId` del hallazgo NO está en `RepuestoLocal.ParteIdsCompatibles` del SKU |
| **`Cantidad`** | Decimal > 0 | Permite galones, litros, fracciones; rechaza ≤ 0 |
| **`AccionRequerida`** | UI bloquea agregar repuestos si no es `RequiereIntervencion` | I10 también enforcea en handler como defensa en profundidad |
| **`UbicacionGps?` y `EmitidoPor`** | SE AGREGAN al evento | Consistencia con resto de eventos de captura. `EmitidoPor` actualiza la lista derivada de contribuyentes |

##### Estructura final

```csharp
public sealed record EstimarRepuesto(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid SkuId,
    decimal Cantidad,
    string Justificacion,
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record RepuestoEstimadoAgregado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid RepuestoEstimadoId,
    Guid SkuId,
    decimal Cantidad,
    string Unidad,                          // derivada del catálogo del SKU
    string Justificacion,
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime EstimadoEn);
```

##### Validaciones del handler

```csharp
public IEnumerable<object> Handle(EstimarRepuesto cmd, Inspeccion state, ...)
{
    // I2: Solo en EnEjecucion
    if (state.Estado != InspeccionEstado.EnEjecucion)
        throw new DomainException("Solo se estima en inspección en ejecución");

    // Hallazgo existe (I5)
    var hallazgo = state.Hallazgos.FirstOrDefault(h => h.HallazgoId == cmd.HallazgoId);
    if (hallazgo is null)
        throw new DomainException($"Hallazgo {cmd.HallazgoId} no existe");

    // I10: Solo se estiman repuestos en hallazgos con RequiereIntervencion
    if (hallazgo.AccionRequerida != AccionRequerida.RequiereIntervencion)
        throw new DomainException(
            "Solo se estiman repuestos en hallazgos con acción 'Sí requiere intervención'. " +
            "La UI debería bloquear esto antes; defensa en profundidad por I10.");

    // Cantidad > 0
    if (cmd.Cantidad <= 0)
        throw new DomainException("Cantidad debe ser mayor que cero");

    // Justificación no vacía
    if (string.IsNullOrWhiteSpace(cmd.Justificacion))
        throw new DomainException("Justificación obligatoria");

    // SKU existe en catálogo
    var sku = await refData.ObtenerRepuesto(cmd.SkuId, ct)
        ?? throw new DomainException($"SKU {cmd.SkuId} no existe en catálogo");

    // HARD ERROR: SKU debe ser compatible con la parte del hallazgo (decisión 2026-04-27)
    if (!sku.ParteIdsCompatibles.Contains(hallazgo.ParteId))
        throw new DomainException(
            $"SKU {sku.CodigoSinco} ({sku.Descripcion}) no está catalogado como compatible " +
            $"con la parte del hallazgo. Si crees que es un error, " +
            $"escala al admin del catálogo de inventario.");

    // No duplicar SKU en el mismo hallazgo
    var yaExiste = state.Repuestos
        .Any(r => r.HallazgoId == cmd.HallazgoId && r.SkuId == cmd.SkuId);
    if (yaExiste)
        throw new DomainException(
            $"SKU {sku.CodigoSinco} ya estimado en este hallazgo. " +
            "Edita o remueve antes de volver a agregar.");

    yield return new RepuestoEstimadoAgregado_v1(
        cmd.InspeccionId, cmd.HallazgoId,
        RepuestoEstimadoId: Guid.NewGuid(),
        cmd.SkuId, cmd.Cantidad,
        sku.UnidadMedida,                       // derivada del catálogo
        cmd.Justificacion,
        cmd.Ubicacion,
        cmd.EmitidoPor,
        DateTime.UtcNow);
}
```

##### Conexión con la saga de cierre

Sin cambios respecto al modelo previo. La saga `CerrarInspeccionSaga` consolida `_repuestos` agrupando por `SkuId` y suma cantidades. El BOM consolidado va al POST de OT correctiva en MYE. La trazabilidad detallada (qué hallazgo aportó qué cantidad de qué SKU) queda en el stream — la saga la pasa a MYE como lista de `HallazgoId` por SKU.

#### 12.10.13 Decisiones operativas de `RepuestoEstimadoRemovido_v1` (2026-04-27)

Decisiones del usuario para el comando `RemoverRepuestoEstimado` y su evento:

| Decisión | Valor | Notas |
|---|---|---|
| **`Motivo`** | Opcional | Si el técnico se equivocó al estimar, puede remover sin justificar; el motivo queda como `null` en el evento |
| **Re-agregar SKU previamente removido** | Permitido | El validador de "SKU no duplicado" en `EstimarRepuesto` solo cuenta repuestos activos; uno removido no bloquea volver a agregar |
| **Mostrar removidos en UI** | No | Solo los activos. El histórico queda en el stream para audit/reportes administrativos si emerge |
| **Confirmación de eliminación** | Modal corto | UX: tap "🗑" abre modal "¿Eliminar repuesto X (cantidad Y)? Motivo (opcional)" + Cancelar/Eliminar |
| **`UbicacionGps?` y `EmitidoPor`** | Sí en evento | Consistencia con resto |
| **Hard delete del estado** | Sí | `_repuestos.RemoveAll`. Stream conserva audit completo (Agregado + Removido). Soft delete innecesario (no hay recurso físico asociado). |

##### Estructuras finales

```csharp
public sealed record RemoverRepuestoEstimado(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    string? Motivo,
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record RepuestoEstimadoRemovido_v1(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    string? Motivo,
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime RemovidoEn);
```

##### Validaciones del handler

```csharp
public IEnumerable<object> Handle(RemoverRepuestoEstimado cmd, Inspeccion state)
{
    if (state.Estado != InspeccionEstado.EnEjecucion)
        throw new DomainException("Solo se remueve en inspección en ejecución");

    var existe = state.Repuestos.Any(r => r.RepuestoEstimadoId == cmd.RepuestoEstimadoId);
    if (!existe)
        throw new DomainException(
            $"Repuesto {cmd.RepuestoEstimadoId} no existe en esta inspección " +
            "(ya fue removido o nunca se agregó)");

    yield return new RepuestoEstimadoRemovido_v1(
        cmd.InspeccionId, cmd.RepuestoEstimadoId,
        cmd.Motivo, cmd.Ubicacion, cmd.EmitidoPor,
        DateTime.UtcNow);
}
```

Nota: el handler **no rechaza** si el motivo es null/vacío — es opcional (decisión 2026-04-27).

##### Flujo end-to-end con re-agregar

Caso típico: el técnico estima filtro de aceite (cantidad 1), después se da cuenta que era el filtro de combustible. La secuencia válida:

```
1. EstimarRepuesto (SKU=filtro-aceite, cantidad=1)
   └─▶ RepuestoEstimadoAgregado_v1 (RepuestoEstimadoId=A)

2. RemoverRepuestoEstimado (RepuestoEstimadoId=A, motivo=null)
   └─▶ RepuestoEstimadoRemovido_v1 (RepuestoEstimadoId=A)

3. EstimarRepuesto (SKU=filtro-combustible, cantidad=1)   ◀ permitido
   └─▶ RepuestoEstimadoAgregado_v1 (RepuestoEstimadoId=B)

4. (al firmar) saga consolida BOM solo con filtro-combustible
```

El stream conserva los 3 eventos. La saga consolida sobre `_repuestos` activos en el aggregate al momento de la firma — el filtro-aceite removido no entra al BOM.

#### 12.10.15 Mediciones diferidas a próxima entrega (2026-04-27)

**Decisión:** el evento `MedicionRegistrada_v1`, su comando `RegistrarMedicion` y todo el flujo de mediciones técnicas con valores numéricos quedan **diferidos** del MVP. El modelo conserva la definición pero el desarrollo se posterga.

**Razones:**

- El uso de mediciones es más natural en el flujo de **inspección de monitoreo** (futuro, ver §12.11.5), donde la rutina define rangos esperados (mín/máx) y el técnico captura valores que el sistema compara contra esos rangos.
- Para inspección técnica MVP, la información operativa relevante (descripción, parte, acción correctiva, repuestos) ya se captura en el hallazgo. Las mediciones puntuales son útiles pero no críticas en v1.0.
- Reducir scope acelera el primer release con valor operativo completo.

**Lo que NO se construye en v1.0:**

- Comando `RegistrarMedicion`.
- Endpoint que reciba el comando.
- Pantalla móvil de captura de medición.
- Apply de `MedicionRegistrada_v1` queda definido en código pero el evento no se emite en producción del MVP.
- Lista `_mediciones` del aggregate queda vacía permanentemente en MVP.

**Lo que SÍ permanece definido:**

- El record `MedicionRegistrada_v1` y la clase `Medicion` value object — listos para activar en v1.x.
- La proyección `DetalleInspeccion` puede tener un slot `Mediciones: []` que en MVP siempre retorna vacío.

**Cuándo se activa:**

Cuando se priorice cualquiera de estos casos:

1. **Inspección de monitoreo** (§12.11.5): rutinas con `MedicionEsperada` por item; las mediciones son output principal.
2. **Demanda explícita del cliente**: equipos donde la evidencia numérica (presión, temperatura, vibración) es parte de la auditoría regulatoria.

**Catálogo MVP histórico (snapshot 2026-04-27):** 17 eventos del aggregate Inspeccion, 16 implementables tras diferir `MedicionRegistrada_v1`. ⚠️ Superseded por §15.4 — el catálogo MVP final consolidado el 2026-04-28 son **20 eventos** (incluye 3 nuevos del aggregate `SeguimientoHallazgo` y `NovedadPreopDescartada_v1`, eliminado `MedicionRegistrada_v1` del scope MVP).

#### 12.10.14 Edición de repuestos estimados — `RepuestoEstimadoActualizado_v1` (2026-04-27)

Caso operativo común: el técnico estimó "filtro de aceite × 1" pero al revisar mejor se da cuenta que necesita 2. Antes de este evento, la única forma era removerlo y volver a agregarlo (5+ taps). Ahora puede editar directamente.

##### Lo que se permite editar

- `Cantidad` — caso típico ("eran 2, no 1").
- `Justificacion` — el técnico aclara o corrige el motivo.

##### Lo que NO se permite editar

- `SkuId` — si el SKU cambia, semánticamente es otro repuesto. Para cambiar de SKU: remover y agregar.
- `Unidad` — siempre derivada del catálogo del SKU; no se cambia.
- `HallazgoId` — el repuesto pertenece al hallazgo donde se estimó originalmente.

##### Estructuras

```csharp
public sealed record EditarRepuestoEstimado(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    decimal Cantidad,
    string Justificacion,
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record RepuestoEstimadoActualizado_v1(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    decimal Cantidad,
    string Justificacion,
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime ActualizadoEn);
```

##### Validaciones del handler

```csharp
public IEnumerable<object> Handle(EditarRepuestoEstimado cmd, Inspeccion state)
{
    if (state.Estado != InspeccionEstado.EnEjecucion)
        throw new DomainException("Solo se edita en inspección en ejecución");

    var existe = state.Repuestos.Any(r => r.RepuestoEstimadoId == cmd.RepuestoEstimadoId);
    if (!existe)
        throw new DomainException(
            $"Repuesto {cmd.RepuestoEstimadoId} no existe en esta inspección " +
            "(ya fue removido o nunca se agregó)");

    if (cmd.Cantidad <= 0)
        throw new DomainException("Cantidad debe ser mayor que cero");

    if (string.IsNullOrWhiteSpace(cmd.Justificacion))
        throw new DomainException("Justificación obligatoria");

    yield return new RepuestoEstimadoActualizado_v1(
        cmd.InspeccionId, cmd.RepuestoEstimadoId,
        cmd.Cantidad, cmd.Justificacion,
        cmd.Ubicacion, cmd.EmitidoPor,
        DateTime.UtcNow);
}
```

##### UX

En la lista de repuestos del hallazgo, cada item gana un icono ✏️ junto al 🗑. Tap ✏️ → modal corto con cantidad y justificación pre-rellenadas. Cambiar y guardar.

##### Trazabilidad

Cada actualización queda en el stream con su timestamp y técnico. Una proyección lateral puede reconstruir "historial de cambios del repuesto X":

```
RepuestoEstimadoAgregado_v1   (cantidad=1, justificacion="Cambio rutinario")
RepuestoEstimadoActualizado_v1 (cantidad=2, justificacion="Filtro doble en este modelo")
```

#### 12.10.10 Acciones rápidas inline por novedad (variante B, 2026-04-27)

**Patrón UX adoptado:** cada novedad en la lista "Importar desde preoperacional" muestra **tres botones inline** debajo de su contenido — verde "Verificar" / naranja "Seguimiento" / rojo "Descartar". Tap directo procesa esa novedad sola, sin paso de selección intermedio.

**Razón de la decisión:** comparado con el patrón de modo selección + comando bulk (descartado), la variante B reduce fricción para el caso operativo dominante (1-3 novedades por inspección, atendidas individualmente) y simplifica la implementación. El costo es que N descartes son N modales si hubiera muchos duplicados — aceptable porque ese caso es excepcional.

##### Comportamiento por botón

| Botón | UX | Comando emitido | Evento generado |
|---|---|---|---|
| **🟢 Verificar** | Abre wizard de verificación completo (pantalla 5 de `02b`): ResultadoVerificacion + diagnóstico + AccionRequerida + (paso 2 si aplica). | `RegistrarHallazgo` con `Origen=PreOperacional`, datos del wizard | `HallazgoRegistrado_v1` |
| **🟠 Seguimiento** | Mini-modal corto con campo "Motivo del seguimiento" (texto libre). Al guardar, evita el wizard completo. | `RegistrarHallazgo` con `Origen=PreOperacional`, `ResultadoVerificacion=RequiereSeguimiento`, `AccionRequerida=RequiereSeguimiento`, `NovedadTecnica=motivo`, parte+actividad heredadas | `HallazgoRegistrado_v1` |
| **🔴 Descartar** | Mini-modal corto con campo "Motivo del descarte" (texto libre). Al guardar, evita el wizard. | `RegistrarHallazgo` con `Origen=PreOperacional`, `ResultadoVerificacion=Descartada`, `AccionRequerida=NoRequiereIntervencion`, `NovedadTecnica=motivo`, parte+actividad heredadas | `HallazgoRegistrado_v1` |

##### Lo que NO se modela (decisión consciente)

- **No hay comando `DescartarNovedadesPreopBulk`.** Se evaluó y se descartó porque la variante B no requiere modo bulk — cada acción es individual y rápida. Si en el futuro emerge un caso operativo donde 10+ novedades duplicadas son frecuentes y los modales individuales se vuelven penosos, se agrega como cambio aditivo (el comando descartado en la sesión anterior queda como referencia de implementación).
- **No hay modo selección con checkboxes.** La lista mantiene un solo modo de interacción (tap directo en los botones), simplifica la UX.

##### Atajos UX implícitos

- "Seguimiento" y "Descartar" **NO requieren llegar al wizard de inspección completo** — la regla "atajo para acciones rápidas" se cumple. Solo "Verificar" abre wizard porque ahí sí se necesita análisis técnico (causa, tipo, repuestos cuando aplica).
- Cada acción genera UN evento `HallazgoRegistrado_v1` que va al stream y queda como hallazgo en la inspección.
- La saga `CerrarInspeccionSaga` al cerrar recorre los hallazgos con `Origen=PreOperacional` y hace POST a `/preop/.../verificar` por cada uno con su `ResultadoVerificacion` (mapeo §12.10.9: Confirmada y Descartada cierran la novedad en el ERP; RequiereSeguimiento la deja activa).

##### Caso de descartar como duplicada de otra "real"

Patrón típico: el técnico verifica una novedad como `Confirmada` con su wizard completo, identificándola como "la real". Para las parecidas, tap directo en "Descartar" y motivo "Duplicada de hallazgo MOTOR-VALV (HD-001)". Tres taps + tres motivos = tres novedades cerradas en el ERP.

Si en el futuro emerge necesidad de **vinculación estructurada** (agrupar duplicados a un hallazgo padre formalmente), se agrega:

```csharp
// Futuro, no aplicar ahora
public sealed record HallazgoVinculadoComoDuplicado_v1(
    Guid InspeccionId,
    Guid HallazgoIdDuplicado,
    Guid HallazgoIdOriginal,
    DateTime VinculadoEn);
```

Por ahora, el motivo de descarte en texto libre es suficiente para audit humano.

##### Resumen del soporte para los tres caminos

| Escenario | Cómo el modelo lo soporta |
|---|---|
| Verificar 1 novedad con análisis técnico | Tap "Verificar" → wizard completo |
| Marcar 1 novedad para seguimiento | Tap "Seguimiento" → mini-modal con motivo → guardar |
| Descartar 1 novedad (duplicada/falso reporte) | Tap "Descartar" → mini-modal con motivo → guardar |
| Procesar N novedades duplicadas | N taps en "Descartar" + N motivos individuales (aceptable; si se vuelve frecuente, agregar bulk en v1.x) |
| Vincular formalmente duplicados a un hallazgo padre | Futuro — agregable como `HallazgoVinculadoComoDuplicado_v1` si emerge |

#### 12.10.6 Edición de hallazgos ya registrados

Caso de uso real: el técnico ya guardó el hallazgo, pero después se da cuenta de que faltó un repuesto, una foto, o quiere ajustar el diagnóstico técnico. **¿Es posible editar?**

**Sí, mientras la inspección esté en estado `EnEjecucion`** (no firmada). Una vez firmada, todos los hallazgos quedan inmutables — la firma sella la inspección.

**Tres tipos de "edición" según qué se quiere cambiar:**

**(a) Agregar fotos / repuestos / mediciones — ya está cubierto sin código nuevo:**

Estos son eventos hermanos al hallazgo, no modificaciones del hallazgo en sí. Sus comandos toman `HallazgoId` como referencia y suman al aggregate sin tocar el hallazgo original:

- `AdjuntarArchivo(InspeccionId, HallazgoId, ...)` → emite `AdjuntoAgregado_v1`. Permite subir foto al hallazgo después.
- `EstimarRepuesto(InspeccionId, HallazgoId, SkuId, ...)` → emite `RepuestoEstimadoAgregado_v1`. Permite agregar otro repuesto.
- `RemoverRepuestoEstimado(InspeccionId, RepuestoEstimadoId, ...)` → emite `RepuestoEstimadoRemovido_v1`. Permite quitar uno mal estimado.
- `RegistrarMedicion(InspeccionId, HallazgoId, ...)` → emite `MedicionRegistrada_v1`. Permite agregar otra medición.

Todos validan que la inspección esté `EnEjecucion` y que el `HallazgoId` exista en el aggregate.

**(b) Editar campos del hallazgo en sí (descripción, causa, tipo, acción correctiva, etc.) — requiere evento nuevo:**

```csharp
public sealed record EditarHallazgo(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid ParteId,                                   // permite cambiar parte
    Guid? ActividadId,                              // si Origen == PreOperacional
    string? ActividadDescripcion,                   // si Origen == Manual
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor) : ICommand;

public sealed record HallazgoActualizado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    // mismos campos editables que en RegistrarHallazgo, pero sin Origen ni NovedadPreopId
    // (esos no se pueden cambiar — son metadata fija desde el registro original)
    Guid ParteId,
    Guid? ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    Guid? CausaFallaId,
    Guid? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor,
    DateTime ActualizadoEn);
```

**Validaciones del handler `EditarHallazgo`:**

- I2: inspección en `EnEjecucion`.
- El `HallazgoId` debe existir en el aggregate.
- I12 e I12b: coherencia ActividadId/ActividadDescripcion según el `Origen` original del hallazgo (no se permite cambiar origen).
- I13: ParteId aplicable a la rutina.
- I9 e I10: coherencia campos del paso 2 según la nueva `AccionRequerida`. Si el técnico cambia de "RequiereIntervencion" a "RequiereSeguimiento", el handler valida que `AccionCorrectiva`, `CausaFallaId`, `TipoFallaId` vengan en null.
- I11: NovedadTecnica no vacía.

**Apply del evento:**

```csharp
public void Apply(HallazgoActualizado_v1 e)
{
    var idx = _hallazgos.FindIndex(h => h.HallazgoId == e.HallazgoId);
    if (idx < 0) return;

    var anterior = _hallazgos[idx];
    _hallazgos[idx] = anterior with
    {
        ParteId = e.ParteId,
        ActividadId = e.ActividadId,
        ActividadDescripcion = e.ActividadDescripcion,
        NovedadTecnica = e.NovedadTecnica,
        AccionRequerida = e.AccionRequerida,
        AccionCorrectiva = e.AccionCorrectiva,
        CausaFallaId = e.CausaFallaId,
        TipoFallaId = e.TipoFallaId,
        ObservacionCampo = e.ObservacionCampo,
    };
    _contribuyentes.Add(e.EmitidoPor);
}
```

**Lo que NO se puede cambiar al editar:**

- `Origen` (PreOperacional vs Manual). Si el técnico verificó una novedad del preop y luego se da cuenta de que era un error, descarta esa verificación con un comando aparte (ver más abajo) y crea un hallazgo manual nuevo.
- `NovedadPreopId`. Mismo razonamiento.
- `HallazgoId`. Identidad inmutable.

**Audit completo:** los valores anteriores quedan en el stream del aggregate. Una proyección de "histórico del hallazgo" puede reconstruir cómo evolucionó: `HallazgoRegistrado_v1` (estado inicial) → `HallazgoActualizado_v1` (cambio 1) → `HallazgoActualizado_v1` (cambio 2)... Cada cambio queda fechado y atribuido.

**(c) Eliminar un hallazgo:**

Un nuevo evento `HallazgoEliminado_v1` con motivo:

```csharp
public sealed record HallazgoEliminado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    string Motivo,
    string EmitidoPor,
    DateTime EliminadoEn);
```

El Apply marca el hallazgo como eliminado pero **no lo borra del HashSet/lista** — así audit preserva su historia. La proyección de UI filtra los eliminados al mostrar al técnico.

Casos de uso típicos:
- "Registré un hallazgo en el equipo equivocado."
- "Era un duplicado de otro hallazgo ya registrado."
- "El operario desestimó la novedad antes de que yo verificara."

#### 12.10.7 Eventos relacionados con edición — agregables al catálogo

Sumar al inventario de eventos del aggregate:

```
+ HallazgoActualizado_v1                   ◀ NUEVO — edita campos del hallazgo
+ HallazgoEliminado_v1                     ◀ NUEVO — soft delete con motivo
```

**Total con edición habilitada: 16 eventos** (vs. 14 anteriores).

#### 12.10.8 Inventario final de eventos del aggregate `InspeccionTecnica`

```
1.  InspeccionIniciada_v1
2.  HallazgoRegistrado_v1                  ◀ con ResultadoVerificacion si Origen=PreOp (§12.10.9)
3.  HallazgoActualizado_v1                 ◀ edita campos del hallazgo (§12.10.6)
4.  HallazgoEliminado_v1                   ◀ soft delete hallazgo (§12.10.6)
5.  MedicionRegistrada_v1                  ◀ DIFERIDO a v1.x (§12.10.15)
6.  AdjuntoAgregado_v1                     ◀ foto/PDF, 3 MB max, max 5 por hallazgo (§12.10.11)
7.  AdjuntoEliminado_v1                    ◀ soft delete adjunto (§12.10.11)
8.  RepuestoEstimadoAgregado_v1            ◀ con UbicacionGps + EmitidoPor (§12.10.12)
9.  RepuestoEstimadoRemovido_v1            ◀ hard delete (§12.10.13)
10. RepuestoEstimadoActualizado_v1         ◀ editar cantidad/justificación (§12.10.14)
11. DiagnosticoEmitido_v1
12. DictamenEstablecido_v1
13. InspeccionFirmada_v1
14. InspeccionCerrada_v1                   ◀ con OTCorrectivaIdSinco + OTCorrectivaNumero
15. InspeccionCerradaSinOT_v1
16. InspeccionCancelada_v1
17. OTGeneracionFallida_v1
```

**Total: 17 eventos.** Cambios respecto a versiones anteriores: se eliminó `TecnicoSeIncorporo_v1` (lista contribuyentes derivada), se eliminó `NovedadPreopVerificada_v1` por consolidación (§12.10.9), se sumaron `HallazgoActualizado_v1`/`HallazgoEliminado_v1` para edición de hallazgos (§12.10.6), `AdjuntoEliminado_v1` para soft delete adjuntos (§12.10.11), `RepuestoEstimadoActualizado_v1` para editar repuestos sin remover (§12.10.14). `InspeccionCerrada_v1` lleva `OTCorrectivaIdSinco` + `OTCorrectivaNumero`.

---

## 13. ADR-003 — Generación de OT correctiva en Sinco MYE (2026-04-27)

**Estado:** Aceptada.

### Contexto

Cuando una inspección técnica termina con uno o más hallazgos `AccionRequerida = RequiereIntervencion`, el módulo debe generar una **OT correctiva** en Sinco MYE con el BOM consolidado de los repuestos estimados. Esto cierra el ciclo: novedad → diagnóstico → OT en planificación.

### Decisión

La generación de OT se dispara **automáticamente al firmar la inspección**, vía la saga `CerrarInspeccionSaga` que reacciona a `InspeccionFirmada_v1`. La saga solo invoca el POST si la inspección tiene al menos un hallazgo con `RequiereIntervencion`. Si no hay hallazgos con intervención, no se crea OT — la inspección se cierra sin OT.

```csharp
public sealed class CerrarInspeccionSaga
{
    public async Task Handle(InspeccionFirmada_v1 evt, ...)
    {
        var inspeccion = await session.Events
            .AggregateStreamAsync<InspeccionTecnica>(evt.InspeccionId);

        // 1. Verificar novedades del preop (siempre)
        foreach (var h in inspeccion.Hallazgos.Where(h => h.Origen == OrigenHallazgo.PreOperacional))
            await preopAcl.MarcarVerificada(...);

        // 2. ¿Hay hallazgos que requieren OT?
        var hallazgosOT = inspeccion.Hallazgos
            .Where(h => h.AccionRequerida == AccionRequerida.RequiereIntervencion)
            .ToList();

        if (hallazgosOT.Count == 0)
        {
            // No se genera OT
            session.Events.Append(evt.InspeccionId,
                new InspeccionCerradaSinOT_v1(evt.InspeccionId, DateTime.UtcNow));
            await session.SaveChangesAsync();
            return;
        }

        // 3. Construir BOM consolidado (suma cantidades por SKU)
        var bom = ConsolidarBom(hallazgosOT);

        // 4. POST a MYE con idempotency
        var otId = await myeAcl.CrearOTCorrectiva(
            new CrearOTCorrectivaRequest_v1(...),
            idempotencyKey: evt.InspeccionId.ToString());

        // 5. Cerrar
        session.Events.Append(evt.InspeccionId,
            new InspeccionCerrada_v1(evt.InspeccionId, otId, DateTime.UtcNow));
        await session.SaveChangesAsync();
    }
}
```

### Reglas de idempotencia y reintento

- **`Idempotency-Key`** = `InspeccionId`. Si el módulo reintenta el POST por timeout/red, MYE devuelve la misma OT y no duplica.
- **Wolverine (recomendado)** maneja el reintento con backoff exponencial: 5s, 30s, 2m, 10m. Después de N reintentos el mensaje va a dead-letter y se alerta al equipo de operaciones.
- **El stream del aggregate no se cierra hasta confirmar el POST**: `InspeccionFirmada_v1` se emite, pero `InspeccionCerrada_v1` (con `OTCorrectivaIdSinco`) solo se emite cuando MYE confirma. Si MYE está caído, la inspección queda en estado **Firmada (pendiente de OT)** y se reintenta.

### Retroalimentación al técnico

- Al firmar, el técnico ve un toast "Inspección firmada. Generando OT…" y se cierra la pantalla de cierre.
- Cuando la saga termina exitosamente, push notification o entrada en bandeja: "OT-123456 generada para Caterpillar D11T".
- Si falla repetidamente, el supervisor (no el técnico) recibe la alerta — la inspección está firmada y los hallazgos persistidos; la OT puede generarse después manualmente desde un panel admin.

### Consolidación del BOM

Los repuestos estimados pueden duplicarse cuando dos hallazgos distintos requieren el mismo SKU (ej. dos hallazgos de motor que ambos piden filtro de aceite). El consolidador:

1. Agrupa por `SkuId`.
2. Suma cantidades.
3. Concatena justificaciones de cada hallazgo origen.
4. Mantiene trazabilidad: el BOM resultante lleva, por SKU, la lista de `HallazgoId` que aportaron.

### Qué se envía a MYE

`CrearOTCorrectivaRequest_v1` es un **DTO de request HTTP**, no un evento de dominio. Vive en el namespace del adapter (`Sinco.Inspecciones.Adapters.Mye`) y NUNCA se persiste en el event store. El stream de la inspección registra `InspeccionFirmada_v1` (causa) e `InspeccionCerrada_v1` (efecto con `OTCorrectivaIdSinco` y `OTCorrectivaNumero`); el DTO que viajó por la red no se guarda como evento — la información necesaria para reconstruir qué se envió está en el aggregate al momento de la firma. Ver §8 sobre la convención de naming.

```csharp
// DTO de request HTTP — vive en Sinco.Inspecciones.Adapters.Mye
public sealed record CrearOTCorrectivaRequest_v1(
    Guid InspeccionId,                                   // idempotency key
    Guid EquipoId,
    PrioridadOT Prioridad,                               // derivada de hallazgos críticos
    string DescripcionTrabajo,                           // texto consolidado
    IReadOnlyList<Guid> HallazgosRelacionados,           // ambos orígenes
    IReadOnlyList<RepuestoBomConsolidado> Bom,
    DictamenOperacion Dictamen,                          // PuedeOperar / ConRestriccion / NoPuedeOperar
    DateTime InspeccionFirmadaEn,
    string TecnicoFirmante);

public sealed record RepuestoBomConsolidado(
    Guid SkuId,
    string CodigoSku,
    decimal CantidadTotal,
    string Unidad,
    IReadOnlyList<Guid> HallazgosOrigen);

public enum PrioridadOT { Baja, Normal, Alta, Urgente }

// Respuesta de MYE
public sealed record CrearOTCorrectivaResponse_v1(
    Guid OTCorrectivaIdSinco,                            // el ID técnico interno de MYE
    string OTCorrectivaNumero,                           // autonumérico humano "OT-123456" — visible al usuario
    DateTime CreadaEn);
```

**Derivación de prioridad** (proposta a validar con MYE):
- Dictamen `NoPuedeOperar` → Prioridad `Urgente`.
- Dictamen `ConRestriccion` con ≥1 hallazgo `RequiereIntervencion` → `Alta`.
- Dictamen `PuedeOperar` con `RequiereIntervencion` → `Normal`.
- (Si no hay `RequiereIntervencion` no se llega a este punto.)

### Flujo de error

| Escenario | Comportamiento |
|---|---|
| MYE responde 4xx (validación, equipo no existe) | Saga marca `OTGeneracionFallida_v1` con detalle. Inspección queda Firmada-pendiente. Notificación a supervisor. No reintenta automático. |
| MYE responde 5xx o timeout | Wolverine reintenta con backoff. Tras N intentos, dead-letter. |
| MYE responde 200 con `OTCorrectivaId` | Saga emite `InspeccionCerrada_v1`. Inspección pasa a estado `Cerrada`. Notificación al técnico. |
| Idempotency hit (segunda llamada con misma key) | MYE responde con la OT existente. Saga procede normal. |

### Casos especiales

- **Inspección sin intervención requerida**: se emite `InspeccionCerradaSinOT_v1`. No se llama a MYE. La inspección queda cerrada en histórico.
- **Inspección con solo seguimientos**: igual al caso anterior. Los seguimientos quedan en bandeja del supervisor para reinspección, no generan OT.
- **Inspección cancelada antes de firmar**: no se llama a MYE. No se generan eventos de cierre — solo `InspeccionCancelada_v1`.

### Eventos involucrados (consolidación)

```csharp
// Ya existentes
InspeccionFirmada_v1                  // dispara la saga
InspeccionCerrada_v1                  // marca cierre con OTCorrectivaIdSinco
InspeccionCancelada_v1                // ramal de cancelación

// Nuevos en este ADR
InspeccionCerradaSinOT_v1             // cuando no hay hallazgos RequiereIntervencion
OTGeneracionFallida_v1                // si MYE rechaza con 4xx
```

---

## 14. ADR-005 — SignalR para notificación push del cierre de inspección (2026-04-27)

**Estado:** Aceptada.

### Contexto

El cierre de inspección dispara la saga `CerrarInspeccionSaga` (ADR-003) que hace POST async a MYE para crear la OT correctiva. La saga puede tardar segundos a minutos según latencia de VPN, carga de MYE, reintentos. Mientras tanto, el técnico está en la pantalla 7 esperando ver el código de OT (ej. "OT-123456").

Hay tres formas de comunicar al cliente cuando MYE responde:

| Patrón | Pro | Con |
|---|---|---|
| **Polling HTTP** | Simple de implementar | Latencia + tráfico + batería; mala UX en móvil |
| **SignalR / WebSocket** | Push real-time, latencia mínima, eficiente | Conexión persistente; más infra |
| **Push notification (APN/FCM)** | Funciona con app cerrada | Requiere setup nativo; PWA limitada en iOS |

### Decisión

**Usar Azure SignalR Service** para push real-time de eventos de cierre de inspección hacia los clientes conectados.

### Razones

- **Latencia mínima**: tan pronto como el aggregate emite `InspeccionCerrada_v1` u `OTGeneracionFallida_v1`, el cliente recibe el push (típicamente <100ms).
- **Eficiente en móvil**: una sola conexión persistente vs múltiples roundtrips de polling. Mejor batería.
- **Multi-técnico**: si tres técnicos contribuyentes están viendo la inspección, los tres reciben la actualización simultáneamente.
- **Azure managed**: SignalR Service en Azure es serverless, escala automático, integración con Entra ID.
- **PWA-friendly**: el cliente SignalR estándar funciona en navegadores modernos sin requerir Service Worker complejo.

### Arquitectura

```
Cliente PWA                       Backend (Container App)             MYE on-prem
─────────                         ─────────────────────                ────────────

[Pantalla 7: esperando OT]
        │
        ├──── SignalR connect (JWT auth) ────▶ InspeccionesHub
        │                                            │
        ├──── JoinInspeccion(inspeccionId) ──────────┤
        │                                            │
        │                                     [valida que técnico
        │                                      sea contribuyente]
        │                                            │
        ◀──── JoinAck ───────────────────────────────┤
                                                     │
                                          [saga CerrarInspeccionSaga
                                           reacciona a InspeccionFirmada_v1]
                                                     │
                                                     ├──── POST /mye/ot-correctivas ───▶ MYE
                                                     │                                       │
                                                     ◀───── 200 OK + OTId + OTNumero ────────┤
                                                     │
                                          [emite InspeccionCerrada_v1
                                           al stream Marten]
                                                     │
                                          [proyector lateral SignalR
                                           consume del stream]
                                                     │
        ◀──── push "OTGenerada" ──────────────────────┤
        │     { OTId, OTNumero, CerradaEn }
        │
[Pantalla 7 actualiza:
 OT-123456 visible]
```

### Componentes

**Hub backend `InspeccionesHub`:**

```csharp
public sealed class InspeccionesHub : Hub
{
    public async Task JoinInspeccion(Guid inspeccionId)
    {
        // Validar que el técnico autenticado sea contribuyente o supervisor
        var techId = Context.User.GetTechId();
        var inspeccion = await queries.ObtenerInspeccion(inspeccionId);
        if (!inspeccion.TecnicosContribuyentes.Contains(techId)
            && !Context.User.HasClaim("sinco_roles", "supervisor"))
        {
            throw new HubException("No autorizado a esta inspección");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"inspeccion-{inspeccionId}");
    }

    public Task LeaveInspeccion(Guid inspeccionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"inspeccion-{inspeccionId}");
}
```

**Proyector lateral SignalR:**

Suscrito al stream Marten. Consume eventos terminales y publica al hub:

```csharp
public sealed class InspeccionSignalRProjection : IProjection
{
    public async Task Apply(IDocumentSession session, InspeccionCerrada_v1 e, IHubContext<InspeccionesHub> hub)
    {
        await hub.Clients.Group($"inspeccion-{e.InspeccionId}")
            .SendAsync("OTGenerada", new {
                e.InspeccionId,
                e.OTCorrectivaIdSinco,
                e.OTCorrectivaNumero,
                e.CerradaEn
            });
    }

    public async Task Apply(IDocumentSession session, InspeccionCerradaSinOT_v1 e, IHubContext<InspeccionesHub> hub)
    {
        await hub.Clients.Group($"inspeccion-{e.InspeccionId}")
            .SendAsync("InspeccionCerradaSinOT", new {
                e.InspeccionId,
                e.CerradaEn
            });
    }

    public async Task Apply(IDocumentSession session, OTGeneracionFallida_v1 e, IHubContext<InspeccionesHub> hub)
    {
        await hub.Clients.Group($"inspeccion-{e.InspeccionId}")
            .SendAsync("OTGeneracionFallida", new {
                e.InspeccionId,
                e.MotivoError,
                e.FallidaEn
            });
    }
}
```

**Cliente React (PWA):**

```typescript
// Al entrar a la pantalla 7 tras firmar
import { HubConnectionBuilder } from '@microsoft/signalr';

const connection = new HubConnectionBuilder()
    .withUrl('/hubs/inspecciones', { accessTokenFactory: () => jwt })
    .withAutomaticReconnect()
    .build();

connection.on('OTGenerada', ({ OTCorrectivaNumero }) => {
    setEstado('generada');
    setNumeroOT(OTCorrectivaNumero);   // "OT-123456"
});

connection.on('OTGeneracionFallida', ({ MotivoError }) => {
    setEstado('error');
    setError(MotivoError);
});

await connection.start();
await connection.invoke('JoinInspeccion', inspeccionId);

// Al salir de la pantalla
await connection.invoke('LeaveInspeccion', inspeccionId);
await connection.stop();
```

### Auth

- Conexión SignalR autenticada con JWT (mismo del API REST).
- `JoinInspeccion` valida que el técnico sea contribuyente o supervisor.
- Cada conexión SignalR está vinculada al user del JWT — no hay forma de espiar inspecciones de otros.

### Reconexión

`withAutomaticReconnect()` del cliente SignalR maneja desconexiones temporales (red móvil flaky). Al reconectar:

1. La conexión se restablece automáticamente.
2. El cliente re-invoca `JoinInspeccion(id)` para volver al grupo.
3. Si el evento de cierre ocurrió mientras estaba desconectado, el cliente hace una llamada HTTP fallback `GET /api/v1/inspecciones/{id}` para obtener el estado actual (incluyendo OT si ya fue generada).

### Fallback si SignalR no está disponible

Para casos edge (corporate firewall que bloquea WebSockets, browser viejo, etc.), el cliente cae a **polling HTTP cada 5 segundos** sobre `GET /api/v1/inspecciones/{id}`. Detecta cierre comparando estado.

Esto es híbrido: SignalR primario, polling como respaldo. Garantiza que el técnico siempre vea la actualización.

### Eventos SignalR (catálogo)

| Evento | Disparado por | Payload |
|---|---|---|
| `OTGenerada` | `InspeccionCerrada_v1` | `InspeccionId, OTCorrectivaIdSinco, OTCorrectivaNumero, CerradaEn` |
| `InspeccionCerradaSinOT` | `InspeccionCerradaSinOT_v1` | `InspeccionId, CerradaEn` |
| `OTGeneracionFallida` | `OTGeneracionFallida_v1` | `InspeccionId, MotivoError, FallidaEn` |
| `InspeccionEstadoCambiado` (futuro) | Cualquier transición de estado | Para multi-técnico colaborando en tiempo real |

### Stack Azure

- **Azure SignalR Service** — managed, en la misma suscripción del módulo. Tier Standard para producción (1000 conexiones concurrentes), Free tier para dev/staging.
- **Container App** del backend usa `Microsoft.Azure.SignalR` SDK para publicar al servicio.
- **Cliente** usa `@microsoft/signalr` (npm).
- Costo estimado: ~USD 50/mes producción para clientes pequeños, escala según conexiones.

### Trade-offs aceptados

- **Una pieza de infraestructura más** en Azure (SignalR Service). Es managed, agrega ~USD 50/mes.
- **Conexión persistente** — el cliente mantiene WebSocket abierto. Aceptable: la conexión se cierra cuando el usuario sale de la app.
- **Reconexión en redes flaky** — `withAutomaticReconnect` mitiga; el fallback HTTP cubre el resto.

### Implicación para wireframes

La **pantalla 7 ahora tiene tres sub-estados**:

1. **Esperando** (al llegar tras firma): "Inspección firmada · Generando OT en MYE..." con spinner. Equipo, hallazgos, BOM enviado visibles. Sin código de OT.
2. **OT generada** (al recibir push): cambia a "✓ OT correctiva generada" con código **OT-123456** en grande.
3. **Error** (si MYE rechaza): "⚠ OT pendiente — error de validación: [motivo]". Notifica al supervisor para intervención manual.

El cliente actualiza la pantalla 7 en vivo cuando recibe el push de SignalR, sin que el técnico tenga que hacer nada.

---

## 11. Próximos pasos del modelado

1. Recibir DDL del preoperacional → cerrar el shape exacto del DTO `NovedadPreopDto` y el mapping a `Hallazgo` cuando se verifica.
2. Confirmar lista de tipos de inspección del MVP (motor, hidráulica, etc.) y definir las plantillas correspondientes.
3. Validar invariantes con un PO/operador con experiencia.
4. Definir scopes OAuth2 que protegen cada comando (a partir del ADR-002).
5. Bocetar wireframes que materialicen los flujos del §7 — Tractian como referencia visual prioritaria.

---

## 15. Decisiones consolidadas (2026-04-28) — fuente actualizada de verdad

> **Esta sección consolida todas las decisiones de los review event-by-event y los refinamientos del flujo de hallazgos, firma y seguimientos.**
> **Toma precedencia sobre los §2.1 a §14 si hay conflictos** — esas secciones quedan como histórico de la evolución del modelo.

### 15.1 Resumen de cambios respecto a la versión anterior

| Cambio | Impacto |
|---|---|
| `Severidad` eliminada del modelo | -1 enum, -1 campo en Hallazgo |
| `ResultadoVerificacion` eliminado | -1 enum, -1 campo en Hallazgo |
| `NovedadPreopDescartada_v1` evento dedicado | +1 evento (descarte ya no crea hallazgo) |
| `ParteEquipoId` ahora obligatorio (no nullable) | invariante I-H1 |
| Tipo y causa de falla obligatorios si AccionRequerida≠NoRequiereIntervencion | invariante I-H4 |
| Adjuntos y repuestos obligatorios para hallazgos con intervención | validaciones V-F3 |
| Validaciones pre-firma V-F1 a V-F7 explícitas | bloqueo del botón firmar |
| Regla automática de cierre con/sin OT | la saga decide, técnico no override |
| Inmutabilidad post-firma | no editable, no cancelable, no re-firma |
| Nuevo aggregate `SeguimientoHallazgo` | +3 eventos nuevos |
| Catálogo MVP final | **20 eventos** |

### 15.2 Estructura final del Hallazgo (value object dentro de InspeccionTecnica)

```csharp
public sealed class Hallazgo
{
    public Guid Id                      { get; set; }
    public DateTime RegistradoEn        { get; set; }
    public string EmitidoPor            { get; set; } = default!;

    // Vinculación al árbol del equipo / rutina
    public Guid ParteEquipoId           { get; set; }   // OBLIGATORIO siempre (I-H1)
    public Guid? ActividadRutinaId      { get; set; }   // opcional (si está fuera de rutina)
    public string ActividadDescripcion  { get; set; } = default!;

    // Origen
    public OrigenHallazgo Origen        { get; set; }   // PreOperacional | Manual
    public Guid? NovedadPreopOrigenId   { get; set; }   // inmutable; sólo si Origen=PreOperacional

    // Catálogos cerrados de Sinco MYE
    public Guid? TipoFallaId            { get; set; }   // obligatorio si AccionRequerida ≠ NoRequiereIntervencion
    public Guid? CausaFallaId           { get; set; }   // obligatorio si AccionRequerida ≠ NoRequiereIntervencion

    // Decisión técnica
    public AccionRequerida AccionRequerida  { get; set; }
    public string? Descripcion              { get; set; }   // editable; si Origen=PreOp se hereda como copia

    // Estado (soft delete)
    public bool Eliminado               { get; set; }
    public DateTime? EliminadoEn        { get; set; }
    public string? MotivoEliminacion    { get; set; }
}

public enum OrigenHallazgo
{
    PreOperacional,    // viene de novedad reportada por el operador
    Manual             // detectado por el técnico durante la inspección
}

public enum AccionRequerida
{
    NoRequiereIntervencion,  // hallazgo informativo, no genera OT
    RequiereSeguimiento,     // monitoreo continuo, abre seguimiento
    RequiereIntervencion     // genera OT correctiva en MYE
}
```

**Eliminados del modelo:**
- ❌ enum `Severidad` y campo `Severidad` — no existe en la OT de Sinco MYE; con `AccionRequerida` basta
- ❌ enum `ResultadoVerificacion` y campo en Hallazgo — descartar emite evento dedicado; confirmar/seguimiento se expresan vía `AccionRequerida`

### 15.3 Invariantes del Hallazgo

```
I-H1  ParteEquipoId siempre presente (no nullable)
I-H2  Si Origen = PreOperacional → NovedadPreopOrigenId obligatorio e inmutable
I-H3  Si Origen = Manual → NovedadPreopOrigenId debe ser null
I-H4  Si AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion}
        → TipoFallaId y CausaFallaId obligatorios
I-H5  Si AccionRequerida = NoRequiereIntervencion
        → TipoFallaId y CausaFallaId pueden ser null (opcionales)
I-H6  Múltiples hallazgos sobre la misma ParteEquipoId permitidos
I-H7  Editable solo si la inspección está en estado EnEjecucion
I-H8  HallazgoActualizado_v1 NO puede modificar: Origen, NovedadPreopOrigenId, ParteEquipoId
I-H9  Eliminar hallazgo bloqueado si tiene hijos (repuestos o adjuntos)
```

### 15.4 Catálogo final del MVP — 20 eventos

```
Aggregate InspeccionTecnica (16 eventos):

  Lifecycle (5):
    1. InspeccionIniciada_v1
    2. InspeccionFirmada_v1
    3. InspeccionCerrada_v1            (con OT correctiva)
    4. InspeccionCerradaSinOT_v1       (sin OT, automático según hallazgos)
    5. InspeccionCancelada_v1

  Hallazgos (3):
    6. HallazgoRegistrado_v1
    7. HallazgoActualizado_v1
    8. HallazgoEliminado_v1            (soft delete)

  Novedades preop (1):
    9. NovedadPreopDescartada_v1       (NO crea hallazgo, sólo audit)

  Repuestos (3):
   10. RepuestoEstimado_v1
   11. RepuestoActualizado_v1
   12. RepuestoRemovido_v1

  Adjuntos (2):
   13. AdjuntoSubido_v1
   14. AdjuntoEliminado_v1

  Firma — atómicos (2):
   15. DiagnosticoEmitido_v1
   16. DictamenEstablecido_v1
        (InspeccionFirmada_v1 los acompaña; los 3 se emiten en el mismo Apply)

Aggregate SeguimientoHallazgo (3 eventos):
   17. SeguimientoAbierto_v1           (saga al firmar inspección con AccionRequerida=RequiereSeguimiento)
   18. SeguimientoResuelto_v1          (técnico cierra "Sin intervención")
   19. SeguimientoEscalado_v1          (técnico convierte a "Intervención")

Integración (1):
   20. OTGeneracionFallida_v1

                                       ─────────────
                          TOTAL MVP =   20 eventos

Diferidos a fase 2 (no MVP):
  - MedicionRegistrada_v1
  - MedicionActualizada_v1
```

### 15.5 Validaciones pre-firma — todas bloqueantes

```
V-F1  ≥1 hallazgo registrado en la inspección
V-F2  Todas las novedades preop están verificadas o descartadas
V-F3  Para cada hallazgo con AccionRequerida = RequiereIntervencion:
        - TipoFallaId presente
        - CausaFallaId presente
        - ≥1 adjunto evidencia
        - ≥1 repuesto estimado
V-F4  Dictamen seleccionado (Apto / AptoConRestricciones / NoApto)
V-F5  Firma manuscrita capturada (FirmaUri no vacío)
V-F6  UbicacionFirma capturada (GPS obligatorio, no bloquea si difiere de UbicacionInicio)
V-F7  Estado actual = EnEjecucion
```

El botón "Firmar y cerrar inspección" se deshabilita en UI mientras alguna validación falle, y se bloquea en click para evitar doble submit. El backend revalida (no confía en la UI).

### 15.6 Regla automática de cierre con/sin OT

```
Al firmar la inspección, la saga CerrarInspeccionSaga evalúa:

   si EXISTE hallazgo con AccionRequerida = RequiereIntervencion (no eliminado)
       → POST a Sinco MYE para crear OT correctiva
            ├─ éxito → InspeccionCerrada_v1 (con OTCorrectivaIdSinco + OTCorrectivaNumero)
            └─ falla → OTGeneracionFallida_v1 (estado CierrePendienteOT, reintento)
   en caso contrario (todos NoRequiereIntervencion o RequiereSeguimiento)
       → InspeccionCerradaSinOT_v1
```

**No hay opción de "saltar OT"** si hay hallazgos con intervención. Si el técnico no quiere generar OT, debe editar el hallazgo (HallazgoActualizado_v1) **antes de firmar** y cambiar la AccionRequerida. La decisión vive en la captura del hallazgo, no en el cierre.

### 15.7 Inmutabilidad post-firma

```
I-F1  Una vez en estado Firmada → no se puede:
        - editar hallazgos
        - eliminar hallazgos
        - agregar hallazgos
        - editar repuestos / adjuntos
        - cancelar la inspección
        - re-firmar
I-F2  En estado CierrePendienteOT → solo se permite:
        - reintentar OT (re-encolar saga, máx 1 vez técnico + N veces back-office)
        - back-office puede reasignar / corregir payload
I-F3  Una vez Cerrada o CerradaSinOT → terminal absoluto
```

Si el técnico descubre un error post-firma, la única opción es crear una nueva inspección. Esto preserva auditoría limpia y evita la complejidad de "des-firmar".

### 15.8 Aggregate `SeguimientoHallazgo` (nuevo)

#### 15.8.1 Justificación

Antes del 2026-04-28, un hallazgo con `AccionRequerida = RequiereSeguimiento` quedaba enterrado en una inspección cerrada (inmutable) sin mecanismo para cerrarlo después. Esto creaba seguimientos invisibles que se acumulaban indefinidamente.

El aggregate `SeguimientoHallazgo` resuelve este gap. Es funcionalmente paralelo a las novedades preop pero abierto por el técnico (no por el operador), y transversal a inspecciones del mismo equipo.

#### 15.8.2 Estructura

```csharp
public sealed class SeguimientoHallazgo
{
    // Identidad
    public Guid SeguimientoId          { get; private set; }
    public Guid EquipoId               { get; private set; }   // sigue al equipo cross-obra

    // Origen (referencias inmutables)
    public Guid HallazgoOrigenId       { get; private set; }
    public Guid InspeccionOrigenId     { get; private set; }
    public Guid ParteEquipoId          { get; private set; }
    public string DescripcionOrigen    { get; private set; } = default!;

    // Apertura
    public string AbiertoPor           { get; private set; } = default!;
    public DateTime AbiertoEn          { get; private set; }

    // Estado
    public EstadoSeguimiento Estado    { get; private set; }   // Abierto | Resuelto | Escalado
    public DateTime? CerradoEn         { get; private set; }
    public Guid? InspeccionCierreId    { get; private set; }
    public string? CerradoPor          { get; private set; }
    public string? MotivoCierre        { get; private set; }
    public Guid? HallazgoEscaladoId    { get; private set; }   // si fue escalado, ref al nuevo hallazgo
}

public enum EstadoSeguimiento
{
    Abierto,
    Resuelto,    // cerrado con "Sin intervención"
    Escalado     // convertido a hallazgo con intervención
}
```

#### 15.8.3 Lifecycle

```
[Inspección N firmada con hallazgo AccionRequerida=RequiereSeguimiento]
   └─ saga abre → SeguimientoAbierto_v1
                    Estado = Abierto
        ↓
   [Pasan días/semanas]
        ↓
[Inspección N+k del mismo equipo]
   └─ Pantalla 1 banner: "X seguimientos previos del equipo"
   └─ Botón "↻ Traer de seguimiento [N]" en barra inferior
   └─ Pantalla 2: lista con 3 acciones por seguimiento

   ✓ Sin intervención  → SeguimientoResuelto_v1     (Estado = Resuelto)
   ↻ Seguimiento       → no-op silencioso           (Estado sigue = Abierto)
   ⚠ Intervención      → SeguimientoEscalado_v1     (Estado = Escalado)
                         + HallazgoRegistrado_v1 con RequiereIntervencion en la inspección actual
```

#### 15.8.4 Eventos

```csharp
public sealed record SeguimientoAbierto_v1(
    Guid SeguimientoId,
    Guid EquipoId,
    Guid HallazgoOrigenId,
    Guid InspeccionOrigenId,
    Guid ParteEquipoId,
    string DescripcionOrigen,
    string AbiertoPor,
    DateTime AbiertoEn
);

public sealed record SeguimientoResuelto_v1(
    Guid SeguimientoId,
    Guid InspeccionCierreId,
    string CerradoPor,
    DateTime CerradoEn,
    string MotivoCierre              // texto libre obligatorio
);

public sealed record SeguimientoEscalado_v1(
    Guid SeguimientoId,
    Guid InspeccionCierreId,
    Guid HallazgoEscaladoId,         // ref al nuevo hallazgo creado en la inspección actual
    string EscaladoPor,
    DateTime EscaladoEn
);
```

#### 15.8.5 Decisiones operativas (aprobadas 2026-04-28)

| # | Decisión | Resolución |
|---|---|---|
| 1 | ¿Quién puede cerrar? | Cualquier técnico que inspeccione el equipo posteriormente |
| 2 | ¿"Seguimiento" emite evento? | **No** — es no-op silencioso. Solo feedback visual al técnico (toast + card resaltada). Si más adelante se requiere reportería de "¿hace cuánto nadie revisa?", se agrega `SeguimientoRevisadoSinCambio_v1` como cambio aditivo. |
| 3 | SLA de seguimientos viejos | Alerta visual progresiva + email diario al supervisor a partir de 90 días. Sin bloqueo de inspección. Sin OT automática. |
| 4 | Visibilidad cross-obra | El seguimiento sigue al equipo, no a la obra. Se ve desde cualquier obra a la que el equipo se mueva. |

#### 15.8.6 SLA visual y notificación

```
0–30 días:    badge azul "Abierto"
30–90 días:   badge naranja "Atención" 
+90 días:     badge rojo "Vencido" + email diario al supervisor del equipo
              hasta que alguien lo cierre o escale

NO se genera OT automáticamente (faltan datos: TipoFalla, CausaFalla, repuestos)
NO se bloquea la inspección si hay seguimientos vencidos
NO se "escala" el aggregate solo; siempre requiere acción humana
```

Implementación: job nocturno (Wolverine scheduled task) que escanea seguimientos `Estado=Abierto AND AbiertoEn < now-90d` y dispara correo al `SupervisorEquipo` (proyección leída del catálogo).

### 15.9 Patrón unificado — las mismas 3 opciones en todo el sistema

Las **mismas 3 opciones de `AccionRequerida`** se usan en los 3 lugares donde el técnico decide qué hacer con un hallazgo, novedad o seguimiento. Un solo mental model en todo el sistema.

| Contexto | "Sin intervención" | "Seguimiento" | "Intervención" |
|---|---|---|---|
| **Hallazgo manual** (wizard) | Cierra paso 1, sin tipo/causa/repuestos | Cierra paso 1, sin tipo/causa/repuestos | Continúa a paso 2 (tipo+causa+repuestos) |
| **Novedad preop** (variante B) | ✗ Descartar — `NovedadPreopDescartada_v1` (sin hallazgo) | ↻ Seguimiento — `HallazgoRegistrado_v1` con `RequiereSeguimiento` | ✓ Verificar — wizard 2 pasos → `HallazgoRegistrado_v1` con `RequiereIntervencion` |
| **Seguimiento previo** (nuevo) | ✓ Sin intervención — `SeguimientoResuelto_v1` | ↻ Seguimiento — no-op silencioso | ⚠ Intervención — `SeguimientoEscalado_v1` + `HallazgoRegistrado_v1` |

Beneficios:
- El técnico aprende un único mental model.
- La UI usa los mismos 3 botones con los mismos colores (verde/amarillo/rojo) en los 3 contextos.
- Los reportes pueden agregar uniformemente "decisiones de tipo X tomadas hoy".

### 15.10 UX — pantalla principal de inspección con 3 botones simétricos

Reemplaza el patrón anterior (2 botones) por 3 botones simétricos en la barra inferior:

```
┌─────────────────────────────────────────┐
│  + Agregar hallazgo                     │  primary
├─────────────────────────────────────────┤
│  📁 Importar desde preoperacional   [N] │  outline + badge contador
├─────────────────────────────────────────┤
│  ↻ Traer de seguimiento             [M] │  outline + badge contador
└─────────────────────────────────────────┘
```

Acompañados de banners informativos arriba que muestran el estado actual del backlog (novedades preop pendientes + seguimientos previos del equipo).

Wireframes en:
- `02-wireframes-mobile.html` (pantalla 3 actualizada)
- `02b-wireframes-novedades-preop.html` (pantalla 1 actualizada)
- `02d-wireframes-seguimientos.html` (mock completo del nuevo flujo)

### 15.11 Cambios pendientes en otras secciones del documento

Las secciones §2.1 a §14 quedan como histórico. Las siguientes referencias deben leerse a la luz de §15:

| Sección | Referencia obsoleta | Reemplazar por |
|---|---|---|
| §2.1 Hallazgo | Campo `Severidad` | Eliminado (ver §15.2) |
| §2.1 Eventos | `NovedadPreopVerificada_v1` con `ResultadoVerificacion` | Consolidado en `HallazgoRegistrado_v1` (verificar/seguimiento) o en `NovedadPreopDescartada_v1` (descartar) |
| §6 Saga cierre | "evalúa severidad" | Evalúa `AccionRequerida = RequiereIntervencion` |
| Invariantes I7 (severidad crítica → dictamen) | Usaba `Severidad.Critica` | Usar `AccionRequerida = RequiereIntervencion` para forzar dictamen NoApto/AptoConRestricciones (sugerido, no forzado — ver §15.5 V-F4) |
| §12.10.x decisiones operativas | Algunas usaron `ResultadoVerificacion` | Ahora se decide vía botón en lista (variante B) — el evento emitido depende del botón, no de un selector dentro del wizard |

> **Tarea pendiente**: hacer pasada de limpieza por las secciones §2.1, §6 e invariantes I7, removiendo referencias obsoletas y apuntando a §15. Se difiere para no extender este turno; las secciones siguen siendo útiles como contexto y la fuente de verdad operativa está en §15.
