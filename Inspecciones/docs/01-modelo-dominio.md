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

> ⚠️ **SECCIÓN HISTÓRICA — fuente de verdad: §15.**
> Esta sección documenta la evolución del aggregate. El contrato vigente vive en §15.2 (estructura del Hallazgo), §15.3 (invariantes I-H1..I-H9), §15.4 (catálogo final de 20 eventos) y §15.7 (inmutabilidad post-firma).
> Conceptos referenciados aquí que ya **no existen** en el modelo MVP:
> - `Severidad` y enum `Severidad` → reemplazados por `AccionRequerida` (§15.2).
> - `ResultadoVerificacion` y comando `VerificarNovedadPreoperacional` → consolidados; el flujo unificado vive en §15.9 (3 botones: Verificar / Seguimiento / Descartar emiten `HallazgoRegistrado_v1` o `NovedadPreopDescartada_v1`).
> - Evento `NovedadPreopVerificada_v1` → eliminado.

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
    public int EquipoId            { get; private set; }
    public int RutinaId            { get; private set; }
    public string TecnicoIniciador  { get; private set; } = default!;
    public int ProyectoId              { get; private set; }
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
    public int? OTCorrectivaIdSinco  { get; private set; }   // identificador técnico (int del ERP)
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
| I7 | ⚠️ **OBSOLETA**. La versión original ataba dictamen a `Severidad.Critica`. Reemplazo vigente: la validación pre-firma **V-F4** (§15.5) exige dictamen seleccionado, y la regla de negocio derivada de §15.6 implica que cualquier hallazgo `AccionRequerida = RequiereIntervencion` genera OT (sin forzar dictamen específico — ver discusión abierta en `04-brief-consultor-producto.md` §6 regla #11). | `EstablecerDictamen` |
| I8 | El técnico que firma debe ser **un técnico contribuyente** (iniciador o incorporado) o un supervisor (claim `sinco_roles` contiene `supervisor`) | `FirmarInspeccion` |
| I9-I11 | Reglas de obligatoriedad/prohibición de campos del Hallazgo según AccionRequerida — ver §12.9.4 | `RegistrarHallazgo` |

#### Comandos

```csharp
// IniciarInspeccion crea el aggregate (no transiciona desde Programada).
// El cliente genera el InspeccionId (Guid v7 recomendado) y envía todos los datos.
// La rutina técnica NO se selecciona — se deriva del grupo del equipo: una por grupo.
public sealed record IniciarInspeccion(
    Guid InspeccionId,                   // nuevo, no debe existir
    int EquipoId,                       // del catálogo, debe estar en proyecto del técnico
    string IniciadaPor,                  // del JWT (ID del técnico)
    int ProyectoId,                         // de los claims sinco_obras del JWT
    UbicacionGps Ubicacion) : ICommand;  // OBLIGATORIO — GPS del teléfono al iniciar

// ⚠️ OBSOLETO — eliminado en §15.9. Reemplazado por los 3 botones de la variante B
// (Verificar / Seguimiento / Descartar) que emiten RegistrarHallazgo o
// NovedadPreopDescartada según el botón presionado.
public sealed record VerificarNovedadPreoperacional(
    Guid InspeccionId,
    int NovedadPreopId,                 // referencia al sistema preop
    ResultadoVerificacion Resultado,
    string DiagnosticoEspecifico,
    string EmitidoPor) : ICommand;

// ⚠️ OBSOLETO — el campo Severidad fue eliminado del modelo (§15.2).
// Reemplazado por AccionRequerida en RegistrarHallazgo (§15.2/§15.9).
public sealed record DescubrirHallazgo(
    Guid InspeccionId,
    int ParteId,                        // del catálogo Sinco
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
    int SkuId,
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
    int EquipoId,
    int RutinaId,
    string RutinaCodigo,                        // legible para UI ("INSP. BULL.MOTOR")
    string TecnicoIniciador,
    int ProyectoId,
    UbicacionGps Ubicacion,                     // OBLIGATORIO — GPS al iniciar
    DateTime IniciadaEn);

// Nota: la lista de técnicos contribuyentes se deriva automáticamente del
// EmitidoPor de los demás eventos. NO hay evento `TecnicoSeIncorporo_v1`
// dedicado — sería redundante. Cada Apply de evento de captura agrega
// e.EmitidoPor al HashSet de contribuyentes (idempotente).

// HallazgoRegistrado_v1 es el evento ÚNICO consolidado.
// Reemplaza HallazgoEnRutina_v2, HallazgoFueraDeRutina_v2, HallazgoDescubierto_v1
// y NovedadPreopVerificada_v1 (ver §12.10.9).
// ⚠️ NOTA HISTÓRICA: la versión vigente del payload NO incluye `ResultadoVerificacion`
// (eliminado en §15.2). El campo de abajo refleja una etapa intermedia del modelo.
// Para la forma vigente, ver §15.2 (Hallazgo) y §15.9 (botones de la variante B
// que determinan el evento emitido: HallazgoRegistrado_v1 con AccionRequerida
// para Verificar/Seguimiento, o NovedadPreopDescartada_v1 para Descartar).
public sealed record HallazgoRegistrado_v1(
    Guid InspeccionId, Guid HallazgoId,
    OrigenHallazgo Origen,                          // PreOperacional | Manual
    int? NovedadPreopId,                           // poblado si Origen == PreOperacional (I12b)
    ResultadoVerificacion? ResultadoVerificacion,   // ⚠️ OBSOLETO — eliminado del payload (§15.2)
    int ParteId,                                   // siempre, validado contra rutina del aggregate
    int? ActividadId,                              // poblado si Origen == PreOperacional (catálogo)
    string? ActividadDescripcion,                   // poblado si Origen == Manual (texto libre)
    string NovedadTecnica,                          // texto libre obligatorio
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,                       // obligatorio solo si AccionRequerida = RequiereIntervencion
    int? CausaFallaId,                             // obligatorio solo si AccionRequerida = RequiereIntervencion
    int? TipoFallaId,                              // obligatorio solo si AccionRequerida = RequiereIntervencion
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
    int SkuId, decimal Cantidad,
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
    int OTCorrectivaIdSinco,           // identificador técnico interno
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
        ProyectoId           = e.ProyectoId;
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
                cmd.ProyectoId,
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
    int ParteId,                          // del catálogo Sinco
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

> ⚠️ **SECCIÓN HISTÓRICA — fuente de verdad: §15.2.**
> El value object `Hallazgo` mostrado abajo refleja la primera versión. La forma vigente vive en §15.2 e incluye `ParteEquipoId` (no `ParteId`), `TipoFallaId`, `CausaFallaId`, `AccionRequerida`, `Eliminado` y `MotivoEliminacion`. **No incluye `Severidad` ni `ResultadoVerificacion`** — ambos enums están eliminados del modelo.

```csharp
// ⚠️ OBSOLETO — versión histórica. Forma vigente en §15.2.
public sealed record Hallazgo(
    Guid HallazgoId,
    OrigenHallazgo Origen,
    int? NovedadPreopId,           // null si Origen == Manual
    int ParteId,
    string ActividadDescripcion,
    Severidad Severidad,            // ⚠️ ELIMINADO en §15.2
    string Descripcion,
    IReadOnlyList<Guid> AdjuntosIds,
    DateTime RegistradoEn);

public sealed record Medicion(
    Guid MedicionId, Guid HallazgoId,
    string TipoMedicion, decimal Valor, string Unidad,
    string? Observacion, DateTime RegistradaEn);

public sealed record RepuestoEstimado(
    Guid RepuestoEstimadoId, Guid HallazgoId,
    int SkuId, decimal Cantidad, string Unidad,
    string Justificacion, DateTime EstimadoEn);

public sealed record UbicacionGps(double Latitud, double Longitud, double? PrecisionMetros);

public enum OrigenHallazgo  { PreOperacional, Manual }
public enum Severidad        { Baja, Media, Alta, Critica }                       // ⚠️ ELIMINADO en §15.2
public enum DictamenOperacion { PuedeOperar, ConRestriccion, NoPuedeOperar }
public enum ResultadoVerificacion { Confirmada, Descartada, RequiereSeguimiento } // ⚠️ ELIMINADO en §15.2

// Enum vigente que reemplaza Severidad (definido en §15.2):
//   public enum AccionRequerida { NoRequiereIntervencion, RequiereSeguimiento, RequiereIntervencion }
```

---

## 4. Aggregates de soporte (Catálogo)

Documentos `Marten` read-only mantenidos por sincronización REST contra Sinco on-prem. Estrategia de sincronización detallada en **ADR-004** (`00-investigacion-mercado.md §9.15`): sync inicial al primer login + sync delta on-app-open con `If-None-Match`/`ETag` + stale-while-revalidate como fallback (decisión 2026-05-05 — sin cron nocturno). Reglas operativas vinculantes: IDs/códigos inmutables, renombrar = cambiar descripción, descontinuar = `activa = false` no delete.

> **Nota:** las definiciones de tipos vigentes están en §12.7 (`EquipoLocal`, `UbicacionLocal`, `ProyectoLocal`, `RepuestoLocal`, `Rutina`) y §12.9.6 (`CausaFallaCatalogo`, `TipoFallaCatalogo`). Las definiciones que aparecen abajo son la primera versión preliminar — fueron refinadas en §12 tras la reconciliación con plantillas Excel del ERP. Léase §12.7 y §12.9.6 para los contratos vigentes.

```csharp
// Definiciones preliminares — superseded por §12.7 y §12.9.6
public sealed record EquipoLocal(
    int EquipoId,
    string Codigo,                  // "EXC-320D-014"
    string Modelo,
    string Marca,
    int ProyectoActualId,
    decimal HorometroActual,
    Dictionary<string, string> AtributosExtra,
    DateTime SincronizadoEn);

public sealed record ParteLocal(
    int ParteId,
    int EquipoId,
    string Nombre,
    string? Categoria,
    string? PosicionFisica,
    bool Critica,
    int? ParteIdPadre,
    DateTime SincronizadoEn);

public sealed record RepuestoLocal(
    int SkuId,
    string Codigo,
    string Nombre,
    string Unidad,
    decimal? PrecioReferencia,
    List<int> ParteIdsCompatibles,          // ya NO se usa para validar (compat. SKU↔Parte retirada 2026-05); el ERP no lo expone
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
    Task<IReadOnlyList<ProyectoLocal>> ListarProyectos(CancellationToken ct);
    Task<IReadOnlyList<RepuestoLocal>> BuscarRepuestos(Guid? parteId, string? q, CancellationToken ct);
    Task<Rutina?> ObtenerRutina(Guid rutinaId, CancellationToken ct);
    Task<Rutina?> ObtenerRutinaTecnicaPorGrupo(string grupoMantenimiento, CancellationToken ct);
    // El parámetro TipoRutina se eliminó; el módulo solo consume rutinas técnicas (§12.11.1).
}
```

Implementación concreta en `Sinco.Inspecciones.Infrastructure.ReferenceData`:
- Lectura desde proyección Marten cuando el backend necesita resolver un id (raro en este módulo — la mayoría del filtrado ocurre client-side desde IndexedDB).
- Si la proyección backend está vacía o stale, hidratación bajo demanda al primer uso (no cron). El cliente PWA es la fuente principal de sync via on-app-open (ADR-004 canonical 2026-05-05).
- En modo degradado (VPN caído), el backend usa la cache previa o pospone el resolve si no es crítico.

### Job de sincronización (cliente PWA)

> **Decisión 2026-05-05 (ADR-004 canonical):** sin cron nocturno. El cliente PWA dispara sync on-app-open. La sub-sección de código abajo describe el patrón de implementación cliente — el Wolverine timer trigger original quedó superseded.

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
        await SincronizarConditionalGet<ProyectoLocal>(session, erpAdapter, log);
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

> ⚠️ **SECCIÓN HISTÓRICA — fuente de verdad: §15.12.**
> Esta sección quedó como sketch preliminar de proyecciones de Reporting. El catálogo vigente (con audiencia, eventos consumidos y endpoint declarados explícitamente, conforme a la convención §15.12.6) vive en §15.12. Mapeo:
> - §5.1 `BandejaTecnico` → §15.12.3 `BandejaTecnicoView` (sirve paso 3.44 del roadmap).
> - §5.2 `DetalleInspeccion` → §15.12.1 `DetalleInspeccionView` (sirve paso 3.45).
> - §5.3 `KPIsPorObra` y §5.4 `HistoricoEquipo` permanecen **preliminares** — sin endpoint asignado en MVP; cuando se prioricen, deberán completar el contrato bajo §15.12.

Para no martillar el event store con queries de UI.

### 5.1 `BandejaTecnico`

> ⚠️ **OBSOLETO** — superseded por §15.12.3 `BandejaTecnicoView`.

Una fila por inspección por técnico. Útil para la pantalla principal del móvil.

```csharp
public sealed record BandejaItem(
    Guid InspeccionId,
    string TecnicoId,
    int EquipoId, string EquipoCodigo,
    int RutinaId, string RutinaCodigo, string RutinaNombre,
    int ProyectoId, string ProyectoNombre,
    DateTime IniciadaEn,
    InspeccionEstado Estado,
    int HallazgosCount,
    int RepuestosEstimadosCount,
    DictamenOperacion? UltimoDictamen);
```

Una proyección Marten agregada construida por `MultiStreamProjection`.

### 5.2 `DetalleInspeccion`

> ⚠️ **OBSOLETO** — superseded por §15.12.1 `DetalleInspeccionView`.

Vista completa para la pantalla de ejecución del técnico. Reagrupa el stream con datos de catálogo (joins en proyección).

### 5.3 `KPIsPorObra`

> 📅 **PRELIMINAR** — sin endpoint asignado en MVP. Cuando se priorice, completar contrato bajo §15.12 (audiencia + eventos consumidos + endpoint).

Conteos: inspecciones por estado, hallazgos por severidad, repuestos consumidos. Para dashboard de supervisor.

### 5.4 `HistoricoEquipo`

> 📅 **PRELIMINAR** — sin endpoint asignado en MVP. Cuando se priorice, completar contrato bajo §15.12.

Lista paginada de inspecciones cerradas por equipo, con dictámenes y costos estimados acumulados.

---

## 6. Integración saliente (Outbox + ACL)

> ⚠️ **SECCIÓN HISTÓRICA — fuente de verdad: §15.6.**
> La saga `CerrarInspeccionSaga` vigente:
> - Evalúa `AccionRequerida = RequiereIntervencion` (no severidad) para decidir si emite OT.
> - Si **no** hay hallazgos con intervención, emite `InspeccionCerradaSinOT_v1` y NO contacta MYE.
> - Si la generación de OT falla, emite `OTGeneracionFallida_v1` y deja el aggregate en `CierrePendienteOT` con reintento.
> - Para novedades preop **descartadas** ya no usa `ResultadoVerificacion` — el evento `NovedadPreopDescartada_v1` (§15.4) lleva su propio motivo y el adapter llama `POST /preop/novedades/{id}/descartar` directamente.
> - Para hallazgos con `AccionRequerida = RequiereSeguimiento`, además abre el aggregate `SeguimientoHallazgo` (§15.8).

### 6.1 Decisión

Cuando una inspección llega a `InspeccionFirmada_v1`, hay que materializar tres cosas hacia Sinco on-prem (todas vía REST, ADR-001):

1. Marcar las novedades del preoperacional como verificadas: `POST /api/v1/preop/novedades/{id}/verificar` por cada hallazgo con `Origen == PreOperacional` (con su `ResultadoVerificacion` y `NovedadTecnica` como diagnóstico). ⚠️ Vigente: el cuerpo lleva `AccionRequerida` y diagnóstico, sin `ResultadoVerificacion` (§15.6).
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
        Guid proyectoId, int page, int size, CancellationToken ct);
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

> ⚠️ **SECCIÓN HISTÓRICA — fuente de verdad: §15.**
> Los diagramas de flujo abajo reflejan el modelo previo. Cambios respecto al MVP vigente:
> - `DescubrirHallazgo` desaparece — todo entra como `RegistrarHallazgo` (§15.2).
> - El cierre se bifurca: `InspeccionCerrada_v1` con OT **o** `InspeccionCerradaSinOT_v1` según §15.6.
> - Aparece el aggregate `SeguimientoHallazgo` cuando hay hallazgos `RequiereSeguimiento` (§15.8).
> - Las validaciones pre-firma V-F1..V-F7 (§15.5) son bloqueantes en el botón "Firmar".

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

El descarte explícito requiere acción del técnico. ⚠️ **Forma vigente (§15.4):** se emite el evento dedicado `NovedadPreopDescartada_v1` (no crea hallazgo, solo audit + POST `/preop/novedades/{id}/descartar` con motivo). La forma anterior — registrar un hallazgo con `Origen = PreOperacional` y `ResultadoVerificacion = Descartada` — ya no aplica. Sin la acción de descarte, la novedad sigue pendiente en el preop.

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

> **Nota sobre el patrón EDA aplicado:** la integración del módulo con el ERP Sinco encaja en **§3.2 "Comando asíncrono"** de la guía EDA Sinco — el módulo emite un comando dirigido a un consumidor conocido (MYE, Preop) vía Wolverine outbox + REST, no en §3.4 "Evento de integración" (publish/subscribe a través de un broker compartido entre BCs). Esto es así por restricción de infraestructura: el ERP Sinco es on-prem y no expone un bus consumible. Si en el futuro otro BC Sinco (Reporting, Costos) necesitara reaccionar a hechos del módulo, ahí sí aplicaría §3.4 y requeriría un ADR específico que defina broker, contrato público versionado y compatibilidad hacia atrás. Hoy no aplica.

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
| `ubicación` | `bogota` | Texto libre — ciudad o proyecto (ambiguo, ver pregunta abierta). |

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
    int EquipoId,                      // Id técnico interno
    string CodigoSinco,                 // "1" — clave natural
    string? Placa,                      // "xemp899" — opcional
    string Descripcion,
    string GrupoMantenimiento,          // "BULLDOZER" — clave para rutina
    string Modelo,                      // "D65PX2"
    string? UbicacionDescripcion,       // texto libre por ahora
    int? ProyectoActualId,                 // si existe catálogo de proyectos (ERP lo llama "obras")
    decimal? HorometroActual,
    Dictionary<string, string> AtributosExtra,
    DateTime SincronizadoEn);
```

#### `ParteCatalogo` y `ActividadCatalogo` (nuevos)

Antes había modelado `ParteLocal` como "una parte por equipo". Lo correcto es: **`ParteCatalogo`** es independiente del equipo, y se asocia a grupos/modelos vía la rutina.

```csharp
public sealed record ParteCatalogo(
    int ParteId,
    string Codigo,                      // "MOTOR"
    string Nombre,
    string? Categoria,
    DateTime SincronizadoEn);

public sealed record ActividadCatalogo(
    int ActividadId,
    string Codigo,                      // "VERIFICACION ESTADO"
    string Descripcion,
    DateTime SincronizadoEn);
```

#### `Rutina` (nuevo aggregate de catálogo)

Reemplaza al `TipoInspeccion` que tenía en §2.2. La rutina es el concepto del ERP.

```csharp
public sealed record Rutina(
    int RutinaId,
    string Codigo,                          // "PERO. BULL", "INSP.BULL.MOTOR"
    string Nombre,
    TipoRutina Tipo,                        // Preoperacional | Tecnica
    string GrupoMantenimiento,              // "BULLDOZER"
    string? Modelo,                         // "D65PX2" o null si aplica al grupo entero
    IReadOnlyList<ItemRutina> Items,
    DateTime SincronizadoEn);

public sealed record ItemRutina(
    int ParteId,
    int ActividadId,
    bool Obligatorio,
    string? Instruccion);                   // texto guía opcional para el ejecutor

public enum TipoRutina { Preoperacional, Tecnica }
```

**Ventaja**: la rutina técnica reusa la misma estructura. La diferencia es `Tipo = Tecnica` y posiblemente más detalle por `ItemRutina` (mediciones esperadas, criterios de aprobación). Si el ERP ya soporta esto, no hay que inventar nada.

#### `RepuestoLocal` (revisado)

```csharp
public sealed record RepuestoLocal(
    int SkuId,
    string CodigoSinco,                     // "101", "103"
    string Descripcion,
    string Agrupacion,                      // "combustible", "repuestos"
    string UnidadMedida,                    // "galon", "unidad"
    AplicabilidadMYE AplicaMYE,
    decimal? PrecioReferencia,
    List<int> ParteIdsCompatibles,         // ya NO se usa para validar (compat. SKU↔Parte retirada 2026-05); el ERP no lo expone
    DateTime SincronizadoEn);

public enum AplicabilidadMYE { NoAplica, Opcional, Requerido }
```

#### Eventos del aggregate `InspeccionTecnica` (ajustes)

`HallazgoDescubierto_v1.ActividadDescripcion` (string libre) cambia a referencia:

```csharp
public sealed record HallazgoDescubierto_v1(
    Guid InspeccionId, Guid HallazgoId,
    int ParteId,                       // referencia ParteCatalogo
    int ActividadId,                   // referencia ActividadCatalogo (NUEVO)
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

6. **Existe catálogo de proyectos en el ERP** (que el ERP nombra internamente "obras" — el módulo lo llama "proyecto" siguiendo decisión 2026-04-30 followup #4). Se modela como `ProyectoLocal` con su propia proyección.

> ⚠️ **Nota:** El §12.7 representó el modelo final tras reconciliar las plantillas Excel. Tras revisar `Plantillas Excel/imagenes app.docx` (mockups reales de la app de Sinco MYE), **el modelo de `Hallazgo` se refinó nuevamente en §12.9**. Los cambios principales: `Severidad` se reemplaza por `AccionRequerida` (3 valores accionables), se agregan `CausaFalla`, `TipoFalla`, `AccionCorrectiva`, `NovedadTecnicaDescripcion`, `ObservacionCampo`, y la captura es un **wizard de 2 pasos**. Léase §12.9 para el contrato vigente. Las estructuras de Equipo, Rutina, Ubicación, Proyecto, Repuesto siguen tal cual están en §12.7.

### 12.7 Modelo final ajustado tras la reconciliación (*Hallazgo superseded por §12.9*)

#### `EquipoLocal` (final — extendido 2026-04-30 con dos medidores; refinado 2026-05-05 con asignación de rutinas)

```csharp
public sealed record EquipoLocal(
    int EquipoId,                       // Id técnico interno (PK ERP)
    string CodigoSinco,                 // "1" — clave natural
    string? Placa,                      // "xemp899" — opcional
    string Descripcion,
    int GrupoMantenimientoId,           // PK ERP del grupo — clave para derivar rutinas de monitoreo (decisión 2026-05-05; ver §12.11.5)
    int UbicacionId,                    // referencia a UbicacionLocal
    int? ProyectoActualId,              // referencia a ProyectoLocal (asignación dinámica)
    int? RutinaTecnicaId,               // asignación per-equipo para rutina técnica (decisión 2026-05-04; ver §12.11.1)
    LecturaMedidor? MedidorPrimario,    // p. ej. odómetro (Km) — extendido 2026-04-30
    LecturaMedidor? MedidorSecundario,  // p. ej. horómetro (Hr) — extendido 2026-04-30
    IReadOnlyList<ParteEquipoLocal>? Partes,  // partes asignadas al equipo — usado por INV-PartePerteneceAlEquipo (slice 1c)
    Dictionary<string, string> AtributosExtra,
    DateTime SincronizadoEn);

public sealed record LecturaMedidor(
    string Magnitud,                    // "Km", "Hr", etc. (string del catálogo del ERP)
    decimal Valor,                      // 123456.78
    DateTime CapturadaEn);              // última actualización del lado ERP

public sealed record ParteEquipoLocal(
    int ParteEquipoId,                  // PK ERP de la asignación parte↔equipo
    string ParteCodigo,
    string ParteNombre);
```

> **Decisión 2026-04-30 (cierre followup #3):** el mock del diseño (image4 de `Plantillas Excel/mock del diseño.docx` — pantalla "Previsualización equipo") muestra explícitamente "Medidor 1: Km 123.456,78" y "Medidor 2: Hr 12.43" como caso normal en la pantalla de selección de equipo. Por tanto, **dos medidores son la norma** (no caso atípico). La forma elegida usa dos campos posicionales (`MedidorPrimario` / `MedidorSecundario`) en lugar de `Dictionary<string,decimal>` por simplicidad — el ERP define la semántica de cuál es primario para cada grupo de mantenimiento. Si en el futuro emerge un caso con N medidores (>2), se evalúa migrar a `IReadOnlyList<LecturaMedidor>` como cambio aditivo. **Pregunta abierta para David** (paso 3.32 sync de catálogos): ¿el ERP define el orden primario/secundario por grupo, o lo decide el técnico al iniciar inspección? El módulo asume **el ERP** — si es decisión del técnico, se ajusta el contrato del endpoint.

> **Decisión 2026-05-05 (asignación de rutinas):** el record exige **dos campos** distintos según el tipo de rutina (asimetría documentada en §12.11.1 vs §12.11.5):
>
> - **`RutinaTecnicaId: int?`** — asignación **explícita per-equipo** (1 rutina técnica por equipo, única). Lo trae M-3b. El handler `IniciarInspeccion` la resuelve auto; el técnico no elige.
> - **`GrupoMantenimientoId: int`** — clave del grupo del equipo. El cliente PWA filtra el catálogo de rutinas de monitoreo (M-16) con `rutina.GrupoMantenimientoId == equipo.GrupoMantenimientoId`. Sin tabla intermedia equipo↔rutinas-monitoreo en el ERP.
>
> El campo legacy `GrupoMantenimiento: string` ("BULLDOZER") fue **reemplazado** por `GrupoMantenimientoId: int` para uniformar con el resto de claves naturales del ERP (PK = `int`, código natural = `string`). Si la UI necesita el nombre del grupo, lo trae denormalizado el sync de catálogos o vía join con el catálogo de grupos.

> **Estado del código a 2026-05-07:** la implementación en `src/Inspecciones.Domain/Catalogos/EquipoLocal.cs` (slices 1a-1f) materializa un **subset funcional** del record canonical aquí descrito — solo `EquipoId`, `EquipoCodigo`, `ProyectoId`, `RutinaTecnicaId`, `GrupoMantenimientoId`, `Partes`. El subset es suficiente para los slices cerrados; los campos restantes (`Placa`, `Descripcion`, medidores, `AtributosExtra`, `SincronizadoEn`) se agregan en el slice de sync M-3b cuando se implemente el adapter ERP.

Sin campo `Modelo`. La rutina aplicable se determina por `GrupoMantenimientoId`.

#### `Rutina` (*campo `Tipo` y enum `TipoRutina` superseded por §12.11.1*)

```csharp
public sealed record Rutina(
    int RutinaId,
    string Codigo,                          // "PERO. BULL", "INSP. BULL", "D65PX2"
    string Nombre,
    // ❌ El campo Tipo se eliminó del modelo del módulo en §12.11.1.
    //    Las únicas rutinas que el módulo consume son técnicas implícitamente.
    string GrupoMantenimiento,              // "BULLDOZER" — único atributo de aplicabilidad
    Guid? RutinaPadreId,                    // si hay derivación entre rutinas
    IReadOnlyList<ItemRutina> Items,
    DateTime SincronizadoEn);

public sealed record ItemRutina(
    int ItemId,                            // estable en el catálogo
    int ParteId,                           // referencia a ParteCatalogo
    int ActividadId,                       // referencia a ActividadCatalogo
    bool Obligatorio,
    string? Instruccion);

// ❌ enum TipoRutina eliminado (ver §12.11.1)
```

#### `UbicacionLocal` y `ProyectoLocal` (nuevos)

```csharp
public sealed record UbicacionLocal(
    int UbicacionId,
    string Codigo,
    string Nombre,
    DateTime SincronizadoEn);

public sealed record ProyectoLocal(
    int ProyectoId,
    string Codigo,
    string Nombre,
    int? UbicacionId,                  // si el proyecto tiene ubicación geográfica catalogada
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
    int RutinaId,                          // referencia
    string RutinaCodigo,
    IReadOnlyList<ItemRutina> ItemsSnapshot, // congelado al iniciar
    DateTime IniciadaEn);
```

Esto resuelve el problema de **versionado de plantillas** que había marcado como pendiente: si la rutina cambia mañana, las inspecciones en curso tienen el snapshot de la versión que las inició.

Cuando el técnico marca una actividad de la rutina con hallazgo:

```csharp
public sealed record HallazgoEnRutina_v1(   // sustituye a HallazgoDescubierto_v1 cuando viene de rutina
    Guid InspeccionId, Guid HallazgoId,
    int ItemRutinaId,                      // referencia al snapshot
    int ParteId, int ActividadId,         // duplicado para query directo
    Severidad Severidad,
    string? ObservacionLibre,
    string EmitidoPor, DateTime RegistradoEn);
```

Y cuando el técnico descubre algo **fuera** de la rutina:

```csharp
public sealed record HallazgoFueraDeRutina_v1(  // sustituye a HallazgoDescubierto_v1 cuando es ad-hoc
    Guid InspeccionId, Guid HallazgoId,
    int ParteId, int ActividadId,
    Severidad Severidad,
    string? ObservacionLibre,
    string EmitidoPor, DateTime RegistradoEn);
```

Diferenciar los dos eventos da reporte limpio: "cuántos hallazgos vienen de la rutina vs. cuántos descubre el técnico fuera de ella" sin tener que mirar campos opcionales.

### 12.8 Inventario de APIs revisado (sustituye §9.13 del doc de investigación, *actualizado en §12.9*)

```
GET  /api/v1/equipos?codigo=&grupo=&page=&size=
GET  /api/v1/equipos/{equipoCodigo}
       → trae equipo con grupoMantenimiento, ubicacionId, proyectoActualId

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
```

> **Nota 2026-05-05:** el endpoint `GET /api/v1/admin/usuarios?desde={lastSync}` que figuraba históricamente aquí fue **eliminado** (decisión Jaime 2026-05-05). El módulo no maneja identidad — toda viene del host PWA. Ver `06-contrato-apis-erp.md` §3.6 (NO APLICA).

Total: **14 endpoints** en tres módulos Sinco (Preop, MYE núcleo, Inventario). Pendiente actualizar §9.13 del doc principal con esta lista. **Nota:** §12.9.7 actualiza este inventario sumando catálogos de causa/tipo de falla.

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
    int? NovedadPreopId,                           // si Origen == PreOperacional
    int? ItemRutinaId,                             // si fue capturado desde rutina
    int ParteId,                                   // siempre, del catálogo
    string NovedadTecnicaDescripcion,               // texto libre obligatorio
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,                       // obligatorio si AccionRequerida = RequiereIntervencion
    int? CausaFallaId,                             // obligatorio en paso 2 si AccionRequerida = RequiereIntervencion
    int? TipoFallaId,                              // obligatorio en paso 2 si AccionRequerida = RequiereIntervencion
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
    int ItemRutinaId,
    int ParteId,
    string NovedadTecnicaDescripcion,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor, DateTime RegistradoEn);

// Reemplaza HallazgoFueraDeRutina_v1
public sealed record HallazgoFueraDeRutina_v2(
    Guid InspeccionId, Guid HallazgoId,
    int ParteId,
    string NovedadTecnicaDescripcion,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor, DateTime RegistradoEn);
```

Las versiones `_v1` se descartan al inicio del proyecto (no estaban en producción aún). Si aparecen en código posterior, agregar upcaster.

#### 12.9.4 Invariantes ajustadas

La invariante I7 cambia:

| Antes (severidad) | Ahora (acción requerida) |
|---|---|
| Si hay al menos un hallazgo `Critica`, el dictamen debe ser `NoPuedeOperar` | Si hay al menos un hallazgo con `AccionRequerida = RequiereIntervencion`, **se debe generar OT correctiva** al cerrar la inspección |

El concepto de `DictamenOperacion` se mantiene como decisión separada del técnico al cerrar. ~~La regla exacta de "no contradecirse" (cuándo hallazgos `RequiereIntervencion` impiden `Dictamen = PuedeOperar`) vale la pena validarla con técnicos reales~~ — **resuelto 2026-05-04 (decisión Jaime, V-F8 §15.5):** si hay ≥1 hallazgo con `AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion}`, el dictamen NO puede ser `PuedeOperar`. Solo `ConRestriccion` o `NoPuedeOperar`.

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
    int CausaFallaId,
    string Codigo,                          // "DESGASTE_NORMAL", "FALLA_LUBRICACION", etc.
    string Descripcion,
    DateTime SincronizadoEn);

public sealed record TipoFallaCatalogo(
    int TipoFallaId,
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
```

> **Nota 2026-05-05:** `GET /api/v1/admin/usuarios?desde={lastSync}` removido — identidad 100% del host PWA.

**Total: 16 endpoints** (vs. 14 anteriores). Se mantienen los 14 + dos nuevos catálogos.

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

#### 12.11.1 `Rutina` técnica — modelo final (refinado 2026-05-04)

> **Refinamiento 2026-05-04 (correcciones Jaime):**
> - Eliminar `RutinaPadreId` (no existe en el ERP real).
> - Agregar `ParteId` + `ParteCodigo` a `Rutina` como variables separadas (la parte mayor a la que aplica la rutina).
> - Agregar `Tipo: TipoRutina` discriminador escalable. En MVP solo aplica `Tecnica`; el campo queda preparado para tipos futuros (`PostMantenimiento`, `Certificacion`) sin migrar el catálogo. **NO se unifica con `RutinaMonitoreo`** — esa entidad permanece separada por las razones operativas en §12.11.5 (lifecycle distinto del item, comandos hermanos, evento `InspeccionIniciada_v1` con shape distinto).
> - Cardinalidad: **una rutina técnica por equipo** (única). La asignación es **explícita per-equipo en el ERP**. El `M-3b` del contrato (`GET /equipos/{id}`) trae `rutinaTecnicaId: int` (singular). **Asimetría con monitoreo (decisión 2026-05-05):** las rutinas de monitoreo NO usan asignación per-equipo — se derivan por `grupoMantenimientoId` que también viaja en M-3b. Ver §12.11.5 punto 9.

**Razón histórica para distinguir rutinas técnicas vs otras:** el módulo nuevo solo conoce **rutinas técnicas y rutinas de monitoreo** (entidad separada — ambas son MVP desde 2026-05-05). Las rutinas preoperacionales y de mantenimiento existen en el catálogo de Sinco, pero **no son consumidas por este módulo**:

- Las rutinas preoperacionales viven en el módulo Preoperacional de Sinco MYE (otro flujo, otro consumidor).
- Las rutinas de mantenimiento viven en MYE núcleo o módulo de planificación (otro flujo, otro consumidor).

**Estructura final:**

```csharp
public enum TipoRutina
{
    Tecnica            // MVP — único valor activo. Discriminador escalable a futuros tipos
                       // (PostMantenimiento, Certificacion, etc.) si emergen.
}

public sealed record Rutina(
    int RutinaId,
    string Codigo,                          // "INSP. BULL.MOTOR"
    string Nombre,
    TipoRutina Tipo,                        // Tecnica en MVP
    string GrupoMantenimiento,              // "BULLDOZER" — descriptor de aplicabilidad
    int ParteId,                            // parte mayor a la que aplica la rutina
    string ParteCodigo,                     // denormalizado — "MOTOR"
    DateTime SincronizadoEn);
```

**Cambios respecto a versión previa:**
- ❌ `RutinaPadreId` eliminado (no existe en el ERP).
- ❌ `IReadOnlyList<ItemRutina> Items` y el record `ItemRutina` **eliminados** (limpieza 2026-05-05, followup #10). La rutina técnica MVP es **filtro del catálogo de partes**, no checklist con items navegables. Ver §12.10.5 — el módulo de inspecciones técnicas no expone selector de actividades; para hallazgos manuales el técnico escribe `ActividadDescripcion` como texto libre. El catálogo `Actividad` viaja por separado bajo ADR-004 y se consume solo en lectura (renderizar la actividad heredada cuando un hallazgo viene del preop con `Origen=PreOperacional`).
- ✅ `Tipo: TipoRutina` agregado (escalabilidad — un solo valor `Tecnica` en MVP).
- ✅ `ParteId` + `ParteCodigo` agregados a `Rutina` — la rutina cubre una parte mayor del equipo.

**Cross-ref:** ADR-004 §9.15 "Refinamientos posteriores (2026-05-05)" punto 1 documenta el shape mínimo de M-17 (`GET /api/v1/catalogos/rutinas`) coherente con esta definición. La rutina de monitoreo (§12.11.5) **sí** trae `IReadOnlyList<ItemRutinaMonitoreo>` con `EvaluacionEsperada` — es una entidad distinta con flujo distinto (también MVP desde 2026-05-05).

**Endpoint del catálogo (vigente al 2026-05-05):** `GET /api/v1/catalogos/rutinas` — sync on-app-open (ADR-004 canonical 2026-05-05 — sin cron), alimenta `RutinaTecnicaLocal` en IndexedDB cliente. Cierra el "Hallazgo 1" detectado en la revisión por flujos del 2026-05-04 (rutina técnica MVP sin sync claro previo). Detalle del contrato: §3.4 catálogos de `06-contrato-apis-erp.md`.

**Asignación equipo ↔ rutina técnica (decisión 2026-05-04):**
- **Mecanismo:** asignación explícita per-equipo en el ERP. `M-3b` trae `Equipo.rutinaTecnicaId: Guid` directamente.
- **Cardinalidad:** 1 rutina técnica por equipo (única). Caso degenerado de N=1 respecto al patrón per-equipo de monitoreo (N=2-3).
- **Resolución del handler `IniciarInspeccion`:** lee `Equipo.RutinaTecnicaId` desde `EquipoLocal` (poblado por M-3b). No requiere selector — el técnico no elige.
- **Validación I-I2 actualizada:** la rutina técnica del equipo existe en `RutinaTecnicaLocal` y tiene `Tipo = TipoRutina.Tecnica`. Sin rutina asignada → bloqueo en el inicio (mensaje accionable).

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
    public int EquipoId            { get; private set; }
    public int RutinaId            { get; private set; }
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
    int EquipoId,
    int RutinaId,
    string RutinaCodigo,
    string TecnicoIniciador,
    int ProyectoId,
    UbicacionGps Ubicacion,
    DateTime IniciadaEn,                    // timestamp del sistema (TimeProvider)
    DateOnly FechaReportada,                // NUEVO 2026-04-30 — fecha que el técnico
                                             // afirma como "fecha real" de la inspección.
                                             // Puede ser distinta a IniciadaEn (caso típico:
                                             // técnico carga al final del día una inspección
                                             // hecha en campo horas antes; o carga retroactiva
                                             // de una jornada previa con conectividad pobre).
    LecturaMedidor? LecturaMedidorPrimario,    // NUEVO 2026-04-30 — capturada al iniciar
    LecturaMedidor? LecturaMedidorSecundario); // NUEVO 2026-04-30 — capturada al iniciar
```

El handler de `IniciarInspeccion` siempre lo emite con `Tipo = TipoInspeccion.Tecnica`. Cuando se priorice Monitoreo:
- Aparece un comando hermano `IniciarInspeccionMonitoreo` (o el mismo `IniciarInspeccion` con un parámetro `Tipo`).
- El evento se emite con `Tipo = TipoInspeccion.Monitoreo`.
- Los comandos posteriores válidos cambian (por ejemplo, `TomarMedicion` solo aplica a Monitoreo, `RegistrarHallazgo` libre solo aplica a Tecnica).

> **Sobre `FechaReportada` vs `IniciadaEn` (decisión 2026-04-30, cierre followup #2):** la pantalla 2 del mock del diseño (image2 de `Plantillas Excel/mock del diseño.docx`) muestra explícitamente un calendario donde el técnico elige la **fecha real de la inspección**, distinta de la fecha de carga al sistema. Caso operativo típico: el técnico inspeccionó en campo a las 10am pero carga al final del día (4pm), o tuvo conectividad pobre y carga al día siguiente.
>
> - **`IniciadaEn`** sigue siendo el timestamp del sistema, generado por el `TimeProvider` inyectado en el handler — **se mantiene la regla dura del CLAUDE.md** (prohibido `DateTime.UtcNow` en dominio; el handler genera con `TimeProvider.GetUtcNow()` y se lo pasa al constructor del evento).
> - **`FechaReportada`** es **input del usuario** (`DateOnly`, sin componente horaria) capturado en pantalla 2. Es la fecha que el técnico afirma como "real" para esta inspección. Tipo `DateOnly` (no `DateTime`) porque la precisión es solo el día, no la hora.
> - **Audit:** ambos campos coexisten en el evento. Reportería puede mostrar al supervisor cuándo se cargó vs cuándo se afirma haber inspeccionado, y filtrar inspecciones cargadas con N días de retraso para revisión.
> - **Validaciones del handler** (no se modela como `Apply` — son pre-condiciones, ver invariante I-I3 §15.7):
>   - `FechaReportada <= DateOnly.FromDateTime(IniciadaEn)` (no puede ser futura).
>   - `FechaReportada >= DateOnly.FromDateTime(IniciadaEn).AddDays(-30)` (no más de 30 días de retroactividad — ventana de gracia razonable; si emerge necesidad de rangos mayores se evalúa caso a caso).
> - **Sin evento separado tipo `InspeccionRetroactiva_v1`**: se descartó porque obliga a bifurcar el camino del agregado y rompe simetría. Un solo evento con dos campos cubre el caso.

#### 12.11.4 Lo que NO cambia con esta decisión

- **`Origen` del Hallazgo se mantiene** intacto: `PreOperacional` o `Manual`. Es independiente de `TipoInspeccion`. Una inspección de tipo `Tecnica` sigue acumulando hallazgos con ambos orígenes.
- **El comando `IniciarInspeccion` actual no agrega `Tipo`** porque solo hay un valor. Cuando se priorice Monitoreo, se agrega.
- **Las invariantes I1-I12** siguen válidas tal como están.
- **Los demás eventos** (`HallazgoRegistrado_v1`, `MedicionRegistrada_v1`, etc.) no cambian estructura. Lo único que cambia es: **algunos serán específicos al tipo de inspección** cuando llegue Monitoreo (ej. `MedicionTomada_v1` futura solo aplica a Monitoreo).

#### 12.11.5 Modelo de Inspección de Monitoreo (refinado 2026-04-30 con archivo `inspeccion.xlsx`)

> **Origen:** archivo `Inspecciones/docs/inspeccion.xlsx` enviado por Jaime el 2026-04-30. Hojas: "Rutinas monitoreo" (catálogo de items por rutina/grupo) e "Inspeccion de monitoreo" (formato de captura de ejemplo). Las decisiones 1–6 fueron confirmadas en chat el 2026-04-30.
>
> **Estado:** **MVP** — promovido el 2026-05-05 (decisión Jaime — antes era Fase 2 / roadmap 10.4). Implementación distribuida en roadmap §3.B' (aggregate), §3.E (`RutinaMonitoreoLocal` MVP), §3.F (endpoints), §4.B (M-16 🚧 crítico MVP), §5.B' (pantallas — wireframes en `02e-wireframes-monitoreo.html`).

**1. Enum `TipoInspeccion` extendido:**

```csharp
public enum TipoInspeccion
{
    Tecnica,
    Monitoreo        // MVP — checklist con mediciones / evaluaciones por item (decisión 2026-05-05)
}
```

**2. Catálogo: `RutinaMonitoreo` (entidad nueva — distinta de `Rutina` de técnica):**

```csharp
public sealed record RutinaMonitoreo(
    int RutinaMonitoreoId,
    string Nombre,                                  // p. ej. "Sistema eléctrico"
    int GrupoMantenimientoId,                       // PK del grupo en el ERP — mecanismo de asignación
    string GrupoMantenimiento,                      // descriptor legible — p. ej. "Camioneta"
    IReadOnlyList<ItemRutinaMonitoreo> Items,
    DateTime SincronizadoEn);

public sealed record ItemRutinaMonitoreo(
    int ItemId,
    string Parte,                                   // "Batería"
    string Actividad,                               // "Medición de voltaje"
    EvaluacionEsperada Evaluacion);                 // numérica O cualitativa (mutuamente exclusivas)
```

> **Sobre `GrupoMantenimientoId` (decisión 2026-05-05 — revierte 2026-05-04):** la asignación equipo↔rutinas-monitoreo es **derivada por grupo de mantenimiento**, no per-equipo. Una rutina aplica a un equipo si y solo si `rutina.GrupoMantenimientoId == equipo.GrupoMantenimientoId`. El catálogo trae la rutina **una sola vez** y todos los equipos del grupo la consumen — sin duplicación, sin tabla intermedia equipo↔rutina del lado ERP. Dos equipos del mismo grupo ven exactamente las mismas rutinas. Esto es **distinto** de la rutina técnica, que sí lleva asignación explícita per-equipo (`Equipo.RutinaTecnicaId`) — ver fila "Asignación equipo↔rutina" en el punto 4.

**3. `EvaluacionEsperada` — dos value objects paralelos (decisión 1, opción A):**

Cada item es **numérico O cualitativo, nunca ambos** (decisión 1, opción A confirmada 2026-04-30). Forma de los dos value objects:

```csharp
// Item con medición numérica (ej. voltaje, presión, temperatura)
public sealed record MedicionEsperada(
    string Magnitud,        // "voltaje"
    string Unidad,          // "V"
    decimal ValorMin,       // 12.3
    decimal ValorMax)       // 12.5
    : EvaluacionEsperada;

// Item con evaluación cualitativa (ej. estado de cableado, conectores)
public sealed record EvaluacionCualitativaEsperada()
    : EvaluacionEsperada;

public abstract record EvaluacionEsperada;

public enum CalificacionCualitativa
{
    Bueno,                  // estado correcto
    Regular,                // estado deteriorado, requiere atención eventual
    Malo                    // estado crítico, dispara hallazgo automático con seguimiento
}
```

Ejemplos del archivo `inspeccion.xlsx`:
- Numérico: `Parte=Batería, Actividad=Medir voltaje, Evaluacion=MedicionEsperada(voltaje, V, 12.3, 12.5)`.
- Cualitativo: `Parte=Conectores batería, Actividad=Revisar estado, Evaluacion=EvaluacionCualitativaEsperada()`.

**4. Diferencia con rutina técnica (decisión 4 confirmada 2026-04-30, refinada 2026-05-04):**

| Aspecto | Rutina técnica (MVP) | Rutina de monitoreo (MVP — desde 2026-05-05) |
|---|---|---|
| Asignación equipo↔rutina | **Asignación explícita en el ERP per-equipo** (decisión 2026-05-04) | **Derivada por grupo de mantenimiento** (decisión 2026-05-05 — revierte 2026-05-04). `rutina.GrupoMantenimientoId == equipo.GrupoMantenimientoId`. Sin tabla intermedia equipo↔rutina en el ERP |
| Cardinalidad | **1 rutina por equipo** (única). `M-3b` trae `rutinaTecnicaId: int` singular | **N rutinas por grupo** (típicamente 2–3). El equipo "hereda" todas las rutinas activas de su grupo. `M-3b` ya no trae `rutinasMonitoreoIds[]` — solo `grupoMantenimientoId` |
| Selección al iniciar | **Auto-resuelta** por el handler desde `Equipo.RutinaTecnicaId` (técnico no elige — única) | **El técnico elige** entre las rutinas activas del grupo del equipo (filtro client-side: `catalogo.filter(r => r.GrupoMantenimientoId == equipo.GrupoMantenimientoId)`) |
| Items | Items con `ActividadId` (qué hacer) — sin medición | Items con `EvaluacionEsperada` (numérica con rango O cualitativa Bueno/Regular/Malo) |
| Snapshot en evento | NO se snapshotean items en `InspeccionIniciada_v1` (flujo libre) | SÍ se snapshotean items en `InspeccionIniciada_v1.ItemsSnapshot` (FueraDeRango se calcula contra rango snapshoteado) |
| Flujo del técnico | Libre — el técnico decide qué inspeccionar; los items son metadata sugerida para reportería | Estructurado — el técnico recorre los items de la rutina elegida; sistema dispara hallazgos automáticos |

**5. Eventos nuevos del aggregate `Inspeccion` cuando `Tipo=Monitoreo`:**

```csharp
public sealed record MedicionRegistrada_v1(
    Guid InspeccionId,
    int ItemId,                    // item de la rutina (snapshot en InspeccionIniciada_v1)
    decimal ValorMedido,
    string? Observacion,            // opcional — p. ej. "multímetro con pila baja"
    bool FueraDeRango,              // calculado por el handler contra MedicionEsperada
    DateTime RegistradaEn);

public sealed record EvaluacionCualitativaRegistrada_v1(
    Guid InspeccionId,
    int ItemId,
    CalificacionCualitativa Calificacion,
    string? Observacion,
    DateTime RegistradaEn);

public sealed record ItemMonitoreoOmitido_v1(
    Guid InspeccionId,
    int ItemId,
    string Motivo,                  // p. ej. "multímetro descargado, no pude medir"
    DateTime OmitidoEn);
```

**6. Trigger de hallazgo automático (decisión 2 confirmada 2026-04-30, opción A):**

| Resultado del item | Hallazgo automático | `AccionRequerida` |
|---|---|---|
| Numérico **dentro de rango** | No | — |
| Numérico **fuera de rango** | Sí | `RequiereSeguimiento` |
| Cualitativo `Bueno` | No | — |
| Cualitativo `Regular` | No (decisión 2 = solo Malo dispara) | — |
| Cualitativo `Malo` | Sí | `RequiereSeguimiento` |

Cuando se dispara hallazgo automático, el handler emite **dos eventos atómicos** en un único `SaveChangesAsync`:
1. El evento del registro (`MedicionRegistrada_v1` con `FueraDeRango=true` o `EvaluacionCualitativaRegistrada_v1` con `Calificacion=Malo`).
2. `HallazgoRegistrado_v1` con `Origen=Monitoreo` (nuevo valor del enum, ver punto 8), `AccionRequerida=RequiereSeguimiento`, `MedicionOrigenId=ItemId` (trazabilidad bidireccional), `ParteEquipoId` heredado del item, `NovedadTecnica` autogenerada del tipo `"Voltaje 10.2V fuera de rango esperado [12.3, 12.5]"` o `"Estado calificado Malo en conectores batería"` (editable por el técnico).

Al firmar la inspección, la saga existente §17 abre `SeguimientoHallazgo` automáticamente para los hallazgos con `RequiereSeguimiento` — **se reusa la maquinaria del MVP sin cambios**.

**7. `InspeccionIniciada_v1` extendido para Monitoreo:**

Cuando `Tipo=Monitoreo`, el evento incluye:
- `RutinaMonitoreoSeleccionadaId` — la rutina que el técnico eligió (decisión 4).
- `ItemsSnapshot: IReadOnlyList<ItemRutinaMonitoreoSnapshot>` — copia inmutable de los items de la rutina al momento de iniciar (necesario porque `FueraDeRango` se calcula contra la `EvaluacionEsperada` snapshotada).

```csharp
public sealed record ItemRutinaMonitoreoSnapshot(
    int ItemId,
    string Parte,
    string Actividad,
    EvaluacionEsperada Evaluacion);
```

Cuando `Tipo=Tecnica`, los nuevos campos son `null` — el handler valida que el comando hermano que se use sea coherente con el `Tipo` del aggregate.

**8. Enum `OrigenHallazgo` extendido con `Monitoreo` (sin cambio respecto a versión previa):**

```csharp
public enum OrigenHallazgo
{
    PreOperacional,
    Manual,
    Seguimiento,
    Monitoreo        // MVP — hallazgo derivado de medición fuera de rango o calificación Malo (decisión 2026-05-05)
}
```

Invariantes derivadas para Monitoreo (MVP):
- Si `Origen=Monitoreo` → `MedicionOrigenId` obligatorio e inmutable (apunta al `ItemId` del item que disparó el hallazgo).
- Si `Origen=Monitoreo` → `AccionRequerida=RequiereSeguimiento` (no genera OT inmediata).
- Si `Origen=Monitoreo` → `TipoInspeccion` del stream = `Monitoreo`.

**9. Sync de catálogo con ERP (decisión 5 confirmada 2026-04-30, refinada 2026-05-05):**

- **Catálogo de definiciones de rutinas (`/catalogos/rutinas-monitoreo`):** sincronizado nocturnamente con el patrón estándar de catálogos (ADR-004, stale-while-revalidate, ETag/`If-Modified-Since`). Trae **todas** las rutinas de monitoreo activas del cliente, cada una con su `GrupoMantenimientoId` + items completos. Alimenta la proyección local `RutinaMonitoreoLocal`.
- **Asignación equipo↔rutinas (decisión 2026-05-05):** derivada client-side por grupo de mantenimiento. El equipo trae `grupoMantenimientoId` en M-3b; el cliente filtra el catálogo local de rutinas por ese id. **No hay tabla intermedia ni endpoint de asignación**. Sin redundancia: 50 BULLDOZERs comparten las mismas rutinas del grupo "BULLDOZER" sin replicar definiciones ni mantener listas per-equipo.

El módulo NO gestiona el catálogo de definiciones localmente (lo sincroniza). La asignación se resuelve client-side al iniciar inspección de monitoreo.

**10. Dictamen de monitoreo (decisión 6 confirmada 2026-04-30, opción A):**

Sin cambio respecto a inspección técnica — V-F4 (§15.5) sigue siendo "Dictamen seleccionado, siempre obligatorio". El técnico interpreta los resultados de los items y decide entre `PuedeOperar` / `ConRestriccion` / `NoPuedeOperar`. Mismo mental model que técnica. **V-F8 también aplica a monitoreo** (decisión 2026-05-04): si hay ≥1 hallazgo con `RequiereSeguimiento` o `RequiereIntervencion` (caso típico del flujo monitoreo, donde los hallazgos automáticos siempre son `RequiereSeguimiento`), el dictamen NO puede ser `PuedeOperar`.

**11. Comando hermano `IniciarInspeccionMonitoreo`:**

```csharp
public sealed record IniciarInspeccionMonitoreo(
    Guid InspeccionId,
    int EquipoId,
    int ProyectoId,
    int RutinaMonitoreoId,                         // selección del técnico (decisión 4)
    string IniciadaPor,
    UbicacionGps Ubicacion,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario,
    IReadOnlyCollection<string> Capabilities);
```

El handler valida (además de I-I1, I-I2, I-I3 §15.7):
- `RutinaMonitoreoId` existe en el catálogo local `RutinaMonitoreoLocal`.
- `RutinaMonitoreo.GrupoMantenimientoId == Equipo.GrupoMantenimientoId` — la rutina pertenece al **mismo grupo de mantenimiento** que el equipo (decisión 2026-05-05). Sin tabla de asignación per-equipo en el ERP.
- `RutinaMonitoreo` tiene ≥1 item (rutinas vacías se rechazan).

Y emite `InspeccionIniciada_v1` con `Tipo=Monitoreo` + `RutinaMonitoreoSeleccionadaId` + `ItemsSnapshot`.

**12. Adjuntos en monitoreo (decisión 2026-05-04):**

> **Decisión:** los items de una rutina de monitoreo **deben permitir adjuntar archivos o fotos**. Confirmado por Jaime el 2026-05-04 — cierra la pregunta abierta de §12.11.5 versión 2026-04-30.

Reusa la maquinaria de adjuntos del MVP (§12.10.11): mismo `BlobUri`, SAS upload pattern (ADR-005), `AdjuntoEliminado_v1` para soft delete, mismos tipos permitidos (JPEG/PNG/HEIC/WebP/PDF), 3 MB max, EXIF preservado.

**12.1 Anclaje del adjunto (decisión 2026-05-04, opción "xor por contexto UI"):**

Un adjunto pertenece a **exactamente uno** de `HallazgoId` o `ItemId` — nunca a ambos, nunca a ninguno. El anclaje queda determinado por dónde toma la foto el técnico en la UI, no por una elección explícita:

| Tipo de inspección | Origen del adjunto | Campo relleno |
|---|---|---|
| Técnica | Botón cámara dentro de un hallazgo | `HallazgoId` (sin cambio respecto a MVP) |
| Monitoreo | Botón cámara en un item del checklist de la rutina | `ItemId` |
| Monitoreo | Botón cámara en un hallazgo manual (`Origen=Manual`) fuera de la rutina | `HallazgoId` |

Cuando un item dispara hallazgo automático (`Origen=Monitoreo`), las fotos del item se muestran en la vista del hallazgo vía el link existente `MedicionOrigenId=ItemId` (§12.11.5 punto 6). **No se duplican** ni se "transfieren" — son las mismas fotos vistas desde dos ángulos.

Implementación: el evento `AdjuntoAgregado_v1` gana campo `ItemId: int?` (id del item del catálogo de rutinas-monitoreo, PK del ERP). Invariante: `(ItemId == null) XOR (HallazgoId == null)`. El handler `AdjuntarArchivo` recibe uno u otro según el contexto del comando y valida coherencia con el `Tipo` del aggregate (en `Tecnica`, `ItemId` siempre `null`).

**Trade-off aceptado:** asimetría leve respecto a técnica donde solo aplica `HallazgoId`. Refleja la realidad — solo monitoreo tiene items. Descartadas: opción (a) "solo `HallazgoId`" (perdía evidencia positiva de items Bueno), opción (c) "el técnico elige a qué anclar" (decisión UX innecesaria).

**12.2 Obligatoriedad (decisión 2026-05-04, opción "siempre opcional"):**

La foto **siempre es opcional**, sin importar el resultado del item (`Bueno` / `Regular` / `Malo` / dentro de rango / `FueraDeRango`). La medición numérica o la calificación cualitativa son la evidencia primaria; la foto es complementaria.

**Razones:**
- Algunos items no se prestan a foto (medición eléctrica con multímetro — la foto del instrumento aporta poco).
- El `SeguimientoHallazgo` que se abre tras `Malo` / `FueraDeRango` tiene su propio ciclo de captura de evidencia (§15.8).
- Endurecer después es reversible y de bajo riesgo: si en producción observamos que los seguimientos llegan sin evidencia útil, se agrega una pre-condición al handler ("foto requerida cuando item dispara hallazgo automático") sin migrar el evento ni reescribir la historia.

**Trade-off aceptado:** los técnicos podrían no documentar fallas críticas con foto. Mitigación posterior si se materializa: invariante condicional en el comando `FirmarInspeccion` que bloquee firma cuando un item con `Malo` / `FueraDeRango` no tenga al menos una foto. **NO se aplica en MVP — solo si emerge la necesidad.**

**12.3 Límite por item (decisión 2026-05-04, opción "heredar del MVP"):**

Mismo tope que MVP técnica (§12.10.11):

| Restricción | Valor | Aplicación |
|---|---|---|
| Adjuntos máximos | 5 (no eliminados) | Por `ItemId` o por `HallazgoId`, individualmente |
| Tamaño máximo por archivo | 3 MB | Igual que MVP — cliente comprime |
| Tipos permitidos | JPEG, PNG, HEIC, WebP, PDF | Igual que MVP |
| EXIF | Preservado | Igual que MVP |

El handler `AdjuntarArchivo` extiende su validación de límite-5 para contar contra el `ItemId` cuando el comando viene anclado a item, contra `HallazgoId` cuando viene anclado a hallazgo. Cada item de la rutina de monitoreo tiene su propio cupo de 5; cada hallazgo manual tiene el suyo.

**Trade-off aceptado:** una rutina extensa con muchas fotos puede pesar 100+ MB. Manejable con Azure Storage Lifecycle Policy moviendo blobs antiguos a tier Archive (ya previsto en §12.10.11). Bajar el límite es reversible si emerge abuso.

**13. Otras preguntas abiertas (no bloqueantes hoy):**

- Si el técnico mide fuera de rango pero la observación lo justifica (p. ej. "multímetro con pila baja" — caso real del archivo `inspeccion.xlsx` línea 8), ¿el hallazgo automático se abre igual o existe acción explícita "descartar marca" antes de firmar? Análogo al descarte de novedad preop pero sobre medición. Posiblemente un evento `MarcaMonitoreoDescartada_v1`.
- ¿Frecuencia / programación previa de inspecciones de monitoreo? (mensual, por horómetro, etc.) — roadmap 10.2 lo difiere; con monitoreo ahora en MVP (decisión 2026-05-05), ¿la programación entra también o queda diferida como lo está para técnica?
- ¿Items obligatorios vs saltables? ¿Al firmar, todos los items deben tener registro o basta con observación general?
- ¿"Observación general" como evento separado o atributo de `InspeccionFirmada_v1`?
- ¿La rutina se elige solo al iniciar o el técnico puede cambiarla mid-inspección?

**Nada de esto requiere reescribir código actual** — todo es aditivo. La introducción del campo `TipoInspeccion` en el aggregate desde ya es la única "preparación" hecha en MVP.

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
    int? NovedadPreopId,                           // poblado si Origen == PreOperacional (I12b)
    ResultadoVerificacion? ResultadoVerificacion,   // poblado si Origen == PreOperacional (I12c)
    int ParteId,                                   // siempre, validado aplicable a la rutina del aggregate
    int? ActividadId,                              // poblado si Origen == PreOperacional
    string? ActividadDescripcion,                   // poblado si Origen == Manual (texto libre)
    string NovedadTecnica,                          // texto libre obligatorio
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
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
    int? NovedadPreopId,
    int ParteId,
    int? ActividadId,                              // del catálogo si Origen == PreOperacional
    string? ActividadDescripcion,                   // texto libre si Origen == Manual
    string NovedadTecnicaDescripcion,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
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

> ⚠️ **DECISIÓN INTERMEDIA — superada en §15.**
> Esta sección registra la **primera** vuelta de consolidación (eliminar `NovedadPreopVerificada_v1` y agregar `ResultadoVerificacion` a `HallazgoRegistrado_v1`). Una segunda vuelta (2026-04-28, §15) eliminó también `ResultadoVerificacion` del payload: la decisión Verificar/Seguimiento/Descartar se expresa por el botón presionado en la variante B (§15.9), y el descarte emite el evento dedicado `NovedadPreopDescartada_v1` (§15.4).
> Léase como contexto histórico de la evolución; el contrato vigente es §15.

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
    int? NovedadPreopId,                           // null si es manual
    ResultadoVerificacion? ResultadoVerificacion,   // obligatorio si NovedadPreopId no null
    int ParteId,
    int? ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
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

> **Forward-reference:** las decisiones de esta sección aplican al MVP técnica. La extensión para monitoreo (también MVP desde 2026-05-05 — anclaje a `ItemId` de monitoreo, mismo tope 5/3MB por item, opcional) se documenta en §12.11.5 puntos 12.1–12.3.

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
| **Compatibilidad SKU↔Parte** | ~~Hard error~~ **RETIRADA (2026-05)** | Decisión original: el handler rechazaba si `ParteId` del hallazgo NO estaba en `RepuestoLocal.ParteIdsCompatibles`. **Eliminada:** no hay limitante sobre qué insumo se gasta en un hallazgo, y el ERP no expone `ParteIdsCompatibles` (reconciliación bilateral 2026-05-13, ver `06-contrato-apis-erp.md §0.B`) por lo que el sync lo dejaba vacío y rechazaba todo insumo. Cualquier SKU del catálogo (PRE-H1) es asignable. |
| **`Cantidad`** | Decimal > 0 | Permite galones, litros, fracciones; rechaza ≤ 0 |
| **`AccionRequerida`** | UI bloquea agregar repuestos si no es `RequiereIntervencion` | I10 también enforcea en handler como defensa en profundidad |
| **`UbicacionGps?` y `EmitidoPor`** | SE AGREGAN al evento | Consistencia con resto de eventos de captura. `EmitidoPor` actualiza la lista derivada de contribuyentes |

##### Estructura final

```csharp
public sealed record EstimarRepuesto(
    Guid InspeccionId,
    Guid HallazgoId,
    int SkuId,
    decimal Cantidad,
    string Justificacion,
    UbicacionGps? Ubicacion,
    string EmitidoPor) : ICommand;

public sealed record RepuestoEstimadoAgregado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid RepuestoEstimadoId,
    int SkuId,
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

    // NOTA (2026-05): la validación de compatibilidad SKU↔Parte (hard error de la
    // decisión 2026-04-27) fue RETIRADA. No hay limitante sobre qué insumo se gasta en
    // un hallazgo, y el ERP no expone ParteIdsCompatibles (sync lo deja vacío). Cualquier
    // SKU existente en el catálogo es asignable. Ver tabla §12.10.12 + 06-contrato §0.B.

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

> ⚠️ **AJUSTE 2026-04-28:** la tabla "Comportamiento por botón" (abajo) referencia `ResultadoVerificacion`, que fue eliminado en §15. Mapeo vigente:
> - **🟢 Verificar** → wizard completo → `HallazgoRegistrado_v1` con `Origen=PreOperacional` y `AccionRequerida` elegida por el técnico (sin `ResultadoVerificacion`).
> - **🟠 Seguimiento** → mini-modal con motivo → `HallazgoRegistrado_v1` con `AccionRequerida=RequiereSeguimiento` (sin `ResultadoVerificacion`).
> - **🔴 Descartar** → mini-modal con motivo → `NovedadPreopDescartada_v1` (evento dedicado, **NO crea hallazgo**, ver §15.4).
> Patrón unificado completo en §15.9.

**Patrón UX adoptado:** cada novedad en la lista "Importar desde preoperacional" muestra **tres botones inline** debajo de su contenido — verde "Verificar" / naranja "Seguimiento" / rojo "Descartar". Tap directo procesa esa novedad sola, sin paso de selección intermedio.

**Razón de la decisión:** comparado con el patrón de modo selección + comando bulk, la variante B reduce fricción para el caso operativo dominante (1-3 novedades por inspección, atendidas individualmente) y simplifica la implementación.

> **Amendment 2026-04-30 (observación Sergio + revisión mock):** la suposición *"caso de duplicados es excepcional"* fue invalidada por el consultor producto — *"pueden existir novedades repetidas (que se permita sea masiva con una única observación)"*. La primera respuesta (modal de selección + bulk con motivo manual) **fue superseded** ese mismo día tras la revisión del mock del diseño (image12 de `Plantillas Excel/mock del diseño.docx`): el patrón final es **descarte rápido individual con motivo autogenerado**. Detalle abajo en sub-sección "Descarte rápido inline (motivo autogenerado, decisión 2026-04-30 final)".

##### Comportamiento por botón

| Botón | UX | Comando emitido | Evento generado |
|---|---|---|---|
| **🟢 Verificar** | Abre wizard de verificación completo (referencia visual histórica: image7+9 del mock de Daniel): ResultadoVerificacion + diagnóstico + AccionRequerida + (paso 2 si aplica). | `RegistrarHallazgo` con `Origen=PreOperacional`, datos del wizard | `HallazgoRegistrado_v1` |
| **🟠 Seguimiento** | Mini-modal corto con campo "Motivo del seguimiento" (texto libre). Al guardar, evita el wizard completo. | `RegistrarHallazgo` con `Origen=PreOperacional`, `ResultadoVerificacion=RequiereSeguimiento`, `AccionRequerida=RequiereSeguimiento`, `NovedadTecnica=motivo`, parte+actividad heredadas | `HallazgoRegistrado_v1` |
| **🔴 Descartar** | Mini-modal corto con campo "Motivo del descarte" (texto libre). Al guardar, evita el wizard. | `RegistrarHallazgo` con `Origen=PreOperacional`, `ResultadoVerificacion=Descartada`, `AccionRequerida=NoRequiereIntervencion`, `NovedadTecnica=motivo`, parte+actividad heredadas | `HallazgoRegistrado_v1` |

##### Descarte rápido inline (motivo autogenerado, decisión 2026-04-30 final)

> **Decisión final 2026-04-30 (Jaime, tras revisión del mock):** el descarte de novedades preop se ejecuta **una a una con motivo autogenerado**. La opción de bulk con modal de motivo manual **fue evaluada y descartada** porque agrega fricción (modal, motivo escrito) que no aporta valor: con N taps en el icono "ojo tachado" y el motivo plantilla, la trazabilidad audit (técnico + fecha + canal) ya queda registrada. Para 5 duplicados, son 5 taps sin modal — operativamente trivial.

**UX (image12 del mock del diseño):** cada novedad en la lista "Importar" muestra:
- Tag de referencia (`PREOP-2026-XXXX`) + fecha + parte + descripción + operador que reportó.
- Footer con **2 elementos**:
  - 🚫 **Icono "ojo tachado"** (gris, izquierda) → tap único → descarta la novedad inmediatamente, sin modal. Motivo autogenerado.
  - **Botón azul "Importar"** (derecha) → abre wizard con la novedad heredada (ver §15.9 atajos).

**Comando:**

```csharp
public sealed record DescartarNovedadPreop(
    Guid InspeccionId,
    int NovedadPreopOrigenId,           // una sola novedad por comando
    string DescartadoPor,                // username del técnico
    IReadOnlyCollection<string> Capabilities);
```

**Validaciones del handler:**

- `Capabilities` contiene `ejecutar-inspeccion`.
- La novedad pertenece a `InspeccionId` (estaba listada en la pantalla de importación).
- La novedad está en estado `Pendiente` (no procesada por otro técnico). Si ya está procesada → `409 Conflict` con la `inspeccionIdPropietaria` que la cerró.

**Motivo autogenerado por el handler:**

```csharp
var ahora = timeProvider.GetUtcNow();
var motivo = $"Cerrado por {cmd.DescartadoPor} el {ahora:yyyy-MM-dd HH:mm} UTC desde Inspecciones";

// Emite UN evento (no N — el comando es individual):
return new NovedadPreopDescartada_v1(
    cmd.InspeccionId,
    cmd.NovedadPreopOrigenId,
    motivo,                          // string template, no input del usuario
    cmd.DescartadoPor,
    ahora.UtcDateTime);
```

El evento `NovedadPreopDescartada_v1` no cambia su shape — el `Motivo` queda como string libre. La diferencia con un descarte futuro con motivo manual sería invisible al schema; el discriminador (si emerge necesidad) se agrega como cambio aditivo.

**Adapter Preop (P-6):** se mantiene el contrato bulk-first acordado el 2026-04-30 con David (`POST /api/v1/preop/novedades/descartar` con array `novedadIds`). En el caso individual, el módulo siempre envía un array de **un elemento** — el contrato del ERP no cambia. Esto preserva un único path de adapter y permite que en el futuro, si emerge un caso bulk legítimo (ej. saga de "limpieza periódica de novedades antiguas"), se reuse el mismo endpoint sin nuevo trabajo cross-team.

**Lo que NO existe en el modelo final (decisiones del 2026-04-30):**

- ❌ Modo selección con checkboxes — sin él. Tap individual al icono.
- ❌ Modal de motivo escrito por el técnico para descarte — el motivo es siempre autogenerado.
- ❌ Comando `DescartarNovedadesPreop` (plural, bulk con motivo único manual) — superseded en la misma sesión por simplicidad de UX.

**Caso "duplicada de otra real" (mantiene aplicabilidad):** si el técnico quiere documentar explícitamente *"duplicada de hallazgo X"*, puede tap "Importar" en lugar de "ojo tachado" → wizard → elegir "No requiere intervención" → escribir el motivo en el campo `NovedadTecnica` del hallazgo. Genera `HallazgoRegistrado_v1` con motivo libre, en lugar de `NovedadPreopDescartada_v1` con motivo plantilla. Es decisión del técnico cuál camino usa según cuán importante sea el motivo manual. La novedad preop queda igualmente "atendida" en ambos caminos (la saga `CerrarInspeccionSaga` invoca P-6 si el origen es `Descartada` o si el hallazgo `RequiereSeguimiento=NoRequiereIntervencion`).

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
    int ParteId,                                   // permite cambiar parte
    int? ActividadId,                              // si Origen == PreOperacional
    string? ActividadDescripcion,                   // si Origen == Manual
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor) : ICommand;

public sealed record HallazgoActualizado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    // mismos campos editables que en RegistrarHallazgo, pero sin Origen ni NovedadPreopId
    // (esos no se pueden cambiar — son metadata fija desde el registro original)
    int ParteId,
    int? ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
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

> ⚠️ **OBSOLETO — superado por §15.4.** El catálogo MVP final es de **20 eventos** (no 17). La lista vigente:
> - Suma `InspeccionCerradaSinOT_v1`, `NovedadPreopDescartada_v1` y los 3 eventos del nuevo aggregate `SeguimientoHallazgo` (`SeguimientoAbierto_v1`, `SeguimientoResuelto_v1`, `SeguimientoEscalado_v1`).
> - Renombra `AdjuntoAgregado_v1` → `AdjuntoSubido_v1` y `RepuestoEstimadoAgregado_v1` → `RepuestoEstimado_v1` (con familia consistente Estimado/Actualizado/Removido).
> - `MedicionRegistrada_v1` queda **diferida a post-MVP** (§15.4).
> - `HallazgoRegistrado_v1` ya **no lleva** `ResultadoVerificacion` (§15.2).

```
1.  InspeccionIniciada_v1
2.  HallazgoRegistrado_v1                  ◀ ⚠️ payload sin ResultadoVerificacion en §15.2
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

#### Contrato exigido al equipo MYE

Lo siguiente NO es opcional para el adaptador — es **requisito del endpoint MYE** y debe quedar firmado en el SOW interno antes de implementar la saga (§3.D del roadmap):

1. **Idempotencia real, no solo aceptación del header**: ante una segunda llamada con la misma `Idempotency-Key`, MYE devuelve `200 OK` con el **mismo** `OTCorrectivaIdSinco` y `OTCorrectivaNumero` que devolvió la primera vez. No crea OT duplicada. No devuelve `409 Conflict`.
2. **Persistencia del mapeo key → respuesta**: el mapeo sobrevive a reinicios del proceso MYE. No es cache en memoria.
3. **Ventana de idempotencia ≥ 30 días** (mínimo MVP). `InspeccionId` es permanente, así que reintentos desde dead-letter o replay manual de la saga deben converger al mismo OT incluso días después del cierre.
4. **Determinismo de la respuesta**: si la primera llamada devolvió `200 OK`, la segunda devuelve `200 OK` con el mismo body. Si la primera devolvió `4xx`, la segunda devuelve el mismo `4xx`. Las claves "envenenadas" no se reciclan.

#### Matriz de respuesta del adaptador MYE

El adaptador es **agnóstico al replay**: trata `200 OK` igual sea creación fresca o respuesta cacheada por idempotencia. La saga no necesita saber cuál fue.

| Respuesta de MYE | Acción del adaptador | Evento emitido | Estado de la inspección |
|---|---|---|---|
| `200 OK` con `OTCorrectivaIdSinco` (fresco o replay) | Devuelve el ID a la saga | `InspeccionCerrada_v1` | `Cerrada` |
| `4xx` (validación: BOM inválido, equipo desconocido, dictamen incompatible) | NO reintenta — error permanente | `OTGeneracionFallida_v1` con detalle del 4xx | `CierrePendienteOT` (queda para resolución manual) |
| `5xx` o timeout | Wolverine reintenta con backoff (5s, 30s, 2m, 10m) | tras agotar reintentos: `OTGeneracionFallida_v1` con tipo `Transitorio` | `CierrePendienteOT` hasta replay manual desde panel admin |
| `409 Conflict` ("ya existe") | **Anti-patrón del lado MYE** — viola contrato §13. Ver fallback abajo. | — | — |

#### Fallback si MYE no implementa idempotencia real (degradación graceful)

Si en pre-implementación se descubre que el equipo MYE no puede entregar el contrato anterior en el cronograma del MVP, el adaptador degrada a un patrón "consulta-antes-de-crear" usando `GET /api/v1/mye/ot-correctivas?inspeccionId={id}` (paso 4.10 del roadmap, deja de ser opcional):

```
intentar POST /mye/ot-correctivas
  ├─ 200 OK                 → procede normal (caso ideal)
  ├─ 5xx / timeout / 409    → GET /mye/ot-correctivas?inspeccionId={id}
  │                           ├─ encuentra OT → tratar como 200 OK con ese ID
  │                           └─ no encuentra  → reintenta POST (Wolverine)
  └─ 4xx                    → OTGeneracionFallida_v1 (sin retry)
```

Este fallback es feo, suma un round-trip por reintento y es vulnerable a ventana de carrera (dos POST simultáneos crean dos OTs si MYE no detecta el conflicto). Solo se acepta como **deuda técnica explícita** durante MVP, con followup para migrar a idempotencia real post-piloto.

#### Tests requeridos del adapter MYE (slice del paso 3.27)

El slice del adapter incluye, como mínimo, tres tests con WireMock cubriendo los escenarios críticos:

- `Crear_OT_con_replay_devuelve_misma_OT_ID` — segundo POST con la misma `Idempotency-Key` devuelve `200 OK` con el `OTCorrectivaIdSinco` original; el adaptador no distingue replay de creación fresca.
- `Crear_OT_con_BOM_invalido_emite_OTGeneracionFallida_sin_retry` — `4xx` no dispara reintento; el evento `OTGeneracionFallida_v1` lleva el detalle del error.
- `Crear_OT_con_timeout_reintenta_y_eventualmente_falla_si_agota` — `5xx`/timeout disparan Wolverine retry con backoff; tras agotar política, `OTGeneracionFallida_v1` con tipo `Transitorio`.

Si se adopta el fallback degradado (sección anterior), se añade un cuarto test:

- `Crear_OT_tras_409_consulta_GET_y_recupera_OT_existente` — `409` redirige al `GET`, encuentra OT, saga procede como si hubiera sido `200 OK`.

Test de saga complementario (en el slice de la saga, no del adapter): `Saga_es_agnostica_a_replay_o_creacion_fresca` — el mismo `OTGenerada(otId=12345)` produce la misma transición a `InspeccionCerrada_v1` independiente de si el adaptador devolvió respuesta fresca o cacheada.

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
    int EquipoId,
    PrioridadOT Prioridad,                               // derivada de hallazgos críticos
    string DescripcionTrabajo,                           // texto consolidado
    IReadOnlyList<Guid> HallazgosRelacionados,           // ambos orígenes
    IReadOnlyList<RepuestoBomConsolidado> Bom,
    DictamenOperacion Dictamen,                          // PuedeOperar / ConRestriccion / NoPuedeOperar
    DateTime InspeccionFirmadaEn,
    string TecnicoFirmante);

public sealed record RepuestoBomConsolidado(
    int SkuId,
    string CodigoSku,
    decimal CantidadTotal,
    string Unidad,
    IReadOnlyList<Guid> HallazgosOrigen);

public enum PrioridadOT { Baja, Normal, Alta, Urgente }

// Respuesta de MYE
public sealed record CrearOTCorrectivaResponse_v1(
    int OTCorrectivaIdSinco,                            // el ID técnico interno de MYE
    string OTCorrectivaNumero,                           // autonumérico humano "OT-123456" — visible al usuario
    DateTime CreadaEn);
```

**Derivación de prioridad** (proposta a validar con MYE):
- Dictamen `NoPuedeOperar` → Prioridad `Urgente`.
- Dictamen `ConRestriccion` con ≥1 hallazgo `RequiereIntervencion` → `Alta`.
- ~~Dictamen `PuedeOperar` con `RequiereIntervencion`~~ — **caso imposible** (decisión 2026-05-04 V-F8): `PuedeOperar` no admite hallazgos con seguimiento o intervención, por tanto nunca llega al flujo de generación de OT.
- (Si no hay `RequiereIntervencion` no se llega a este punto. Por V-F8 + I-F4, `PuedeOperar` también nunca llega a este punto.)

### Flujo de error

Ver **"Matriz de respuesta del adaptador MYE"** arriba en este mismo ADR para el detalle canónico (200/4xx/5xx/409). La matriz aplica tanto en saga como en replay manual desde panel admin.

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
- **Azure managed**: SignalR Service en Azure es serverless, escala automático, integración nativa con OAuth2/OIDC (Entra ID o el IdP que se acuerde en ADR-002).
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

| Evento | Disparado por | Payload | Notas |
|---|---|---|---|
| `OTGenerada` | `InspeccionCerrada_v1` (post M-1 exitoso) | `InspeccionId, OTCorrectivaIdSinco, OTCorrectivaNumero, CerradaEn` | **Push principal** — apenas M-1 completa. El PDF puede seguir procesándose. |
| `InspeccionCerradaSinOT` | `InspeccionCerradaSinOT_v1` | `InspeccionId, CerradaEn` | Cierre sin OT (intervención automática o rechazo de aprobador). |
| `OTGeneracionFallida` | `OTGeneracionFallida_v1` | `InspeccionId, MotivoError, FallidaEn` | Falla de M-1. La OT no se creó. |
| `AdjuntoPdfFallido` | `AdjuntoPdfFallido_v1` | `InspeccionId, OTCorrectivaIdSinco, MotivoError` | Falla de M-1b. La OT existe pero quedó sin PDF — requiere remediación manual. |
| `InspeccionEstadoCambiado` (futuro) | Cualquier transición de estado | `InspeccionId, EstadoAnterior, EstadoNuevo` | Para multi-técnico colaborando en tiempo real. |

### Patrón de timing M-1 vs M-1b (decisión 2026-05-05, followup #8)

El flujo post-firma con OT involucra dos integraciones serializadas:

1. **M-1** (`POST /mye/ot-correctivas`) — crea la OT en MYE. Rápido (~segundos).
2. **M-1b** (`POST /mye/ot-correctivas/{id}/adjuntos`) — adjunta el PDF generado por `GenerarPdfInspeccionSaga`. Más lento (~minutos: render QuestPDF + upload Blob + POST multipart).

Push hacia el cliente sigue el patrón **(a)** — push apenas M-1 termina, silencio durante M-1b si va bien, push solo si M-1b falla:

- ✅ **`OTGenerada`** apenas M-1 completa. El técnico ve el número de OT en segundos tras firma — lo más útil para validación inmediata.
- 🔇 **Sin push cuando M-1b completa exitosamente.** El PDF visible al consultar la OT en MYE es retroactivo; no requiere notificación reactiva. La saga emite `PdfAdjuntadoAOT_v1` al stream para auditoría pero el proyector lateral SignalR lo ignora.
- ⚠️ **`AdjuntoPdfFallido`** si M-1b falla. La OT existe pero sin PDF — caso anómalo que requiere remediación manual; el técnico/supervisor debe saberlo.

Alternativas evaluadas y rechazadas:

- **(b) Push solo tras M-1 + M-1b ambos exitosos:** rechazada por la latencia adicional de minutos antes de mostrar el número de OT. Si la app se cierra entre firma y M-1b éxito, el técnico pierde la notificación primaria.
- **(c) Dos pushes separados (uno por M-1, otro por M-1b éxito):** rechazada por ruido UX. El push de M-1b éxito no aporta valor accionable — el PDF es retroactivo.

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
4. Definir el modelo de autorización por comando (claims/roles que cada handler exige). El mecanismo de auth lo provee el host PWA Sinco MYE móvil; ADR-002 cierra el detalle del IdP (estado tentativo).
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
| Adjuntos obligatorios para hallazgos con intervención; repuestos opcionales | validaciones V-F3 (intervención puede ser solo mano de obra) |
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
    public int ParteEquipoId           { get; set; }   // OBLIGATORIO siempre (I-H1)
    public Guid? ActividadRutinaId      { get; set; }   // opcional (si está fuera de rutina)
    public string ActividadDescripcion  { get; set; } = default!;

    // Origen
    public OrigenHallazgo Origen        { get; set; }   // PreOperacional | Manual | Seguimiento
    public int? NovedadPreopOrigenId   { get; set; }   // inmutable; sólo si Origen=PreOperacional
    public Guid? SeguimientoOrigenId    { get; set; }   // inmutable; sólo si Origen=Seguimiento — apunta al SeguimientoHallazgo escalado (trazabilidad cross-inspección)

    // Catálogos cerrados de Sinco MYE
    public int? TipoFallaId            { get; set; }   // obligatorio si AccionRequerida ≠ NoRequiereIntervencion
    public int? CausaFallaId           { get; set; }   // obligatorio si AccionRequerida ≠ NoRequiereIntervencion

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
    Manual,            // detectado por el técnico durante la inspección
    Seguimiento        // escalado desde un SeguimientoHallazgo previo (cross-inspección)
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
I-H1   ParteEquipoId siempre presente (no nullable)
I-H2   Si Origen = PreOperacional → NovedadPreopOrigenId obligatorio e inmutable
                                  → SeguimientoOrigenId debe ser null
I-H3   Si Origen = Manual → NovedadPreopOrigenId debe ser null
                          → SeguimientoOrigenId debe ser null
I-H4   Si AccionRequerida = RequiereIntervencion
         → TipoFallaId y CausaFallaId obligatorios
         (alineado con V-F3 §15.5; tipo/causa se capturan en paso 2 del wizard,
          surface de "análisis técnico" — no en paso 1 de "registro inicial")
I-H5   Si AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}
         → TipoFallaId y CausaFallaId pueden ser null (opcionales)
         (un seguimiento es estado transitorio: cuando se escala a RequiereIntervencion,
          el hallazgo nuevo SÍ captura tipo/causa por I-H4 + I-H11. Reportería específica
          de seguimientos sin tipo/causa: followup #1.)
I-H6   Múltiples hallazgos sobre la misma ParteEquipoId permitidos
I-H7   Editable solo si la inspección está en estado EnEjecucion
I-H8   HallazgoActualizado_v1 NO puede modificar: Origen, NovedadPreopOrigenId,
       SeguimientoOrigenId, ParteEquipoId
I-H9   Eliminar hallazgo bloqueado si tiene hijos (repuestos o adjuntos)
I-H10  Si Origen = Seguimiento → SeguimientoOrigenId obligatorio e inmutable
                               → NovedadPreopOrigenId debe ser null
I-H11  Si Origen = Seguimiento → AccionRequerida debe ser RequiereIntervencion
       (los seguimientos sólo se escalan a OT, no se reabren como otro seguimiento;
        ver también I-S1 en §15.8.7)
I-H12  Solo hallazgos con AccionRequerida = RequiereIntervencion pueden tener
       repuestos asignados (registrada formalmente al cerrar slice-1f
       AsignarRepuesto, 2026-05-06; antes vivía como "I10" en §12.10.12 sin
       código canónico. Implementada como PRE-C del comando AsignarRepuesto)
I-H13  Una novedad preop puede tener a lo sumo UN hallazgo activo (no eliminado)
       por inspección. Re-importar la misma NovedadPreopOrigenId cuando ya existe
       un HallazgoRegistrado_v1 no eliminado con Origen=PreOperacional se rechaza
       (dedupe de importación). Re-importar tras eliminar el hallazgo previo SÍ se
       permite. Enforcada como PRE-12 de RegistrarHallazgo (slice 1p, 2026-05-29;
       cierra Gap 6b del contrato de la PWA). I-H6 (múltiples hallazgos sobre la
       misma ParteEquipoId) sigue vigente: I-H13 acota por novedad, no por parte.

INV-ND1  Una novedad preop NO puede estar simultáneamente descartada y convertida
         en hallazgo (no eliminado) dentro de la misma inspección. Exclusión mutua:
         o NovedadPreopDescartada_v1 o HallazgoRegistrado_v1 con
         NovedadPreopOrigenId == novedadId, nunca ambos. Enforcada por ambos lados:
         · Descartar PRE-5 (no doble descarte) + PRE-6 (no descartar una ya importada)
           — slice 1n.
         · RegistrarHallazgo PRE-11 (no importar una ya descartada) — slice 1p,
           2026-05-29 (cierra FU-40, simetría que faltaba desde 1n §12 P-3).
```

### 15.4 Catálogo final del MVP — 20 eventos

> **Convención de tipos de IDs (decisión 2026-05-04, opción 1b):**
> - **IDs del ERP (PKs de tablas Sinco) → `int`** (System.Int32, 32-bit). Ejemplos: `EquipoId`, `ProyectoId`/`ObraId`, `RutinaId`, `ParteId`/`ParteEquipoId`, `ActividadId`, `CausaFallaId`, `TipoFallaId`, `NovedadPreopId`, `SkuId`/`InsumoId`, `OTCorrectivaIdSinco`, `ItemId`, `RutinaMonitoreoId`, `GrupoMantenimientoId` (decisión 2026-05-05 — clave para derivar rutinas de monitoreo, ver §12.11.5). Acompañados de `<X>Codigo: string` cuando aplica (legible para UI/URLs, ej. `equipoCodigo="D11T-001"`, `obraCodigo="OB-2026-CALI-001"`, `rutinaCodigo="INSP. BULL.MOTOR"`).
> - **IDs internos del módulo (generados por el cliente / aggregate) → `Guid`** (preferido v7 para ordenamiento natural por tiempo). Ejemplos: `InspeccionId`, `HallazgoId`, `RepuestoId`, `AdjuntoId` (cuando es del Blob Storage propio), `SeguimientoHallazgoId`. Estos IDs no existen en el ERP y no necesitan convivir con códigos legibles.
> - **Path params HTTP del ERP**: usan códigos legibles cuando existen (ej. `GET /equipos/{equipoCodigo}` con `D11T-001`). Bodies y responses usan `int` para los `<X>Id` y `string` para los `<X>Codigo`.


```
Aggregate InspeccionTecnica (16 eventos):

  Lifecycle (5):
    1. InspeccionIniciada_v1
    2. InspeccionFirmada_v1
    3. InspeccionCerrada_v1            (con OT correctiva)
    4. InspeccionCerradaSinOT_v1       (sin OT, con discriminador
                                         MotivoCierreSinOT ∈ {AutomaticoSinIntervencion,
                                         RechazadaPorAprobador} — extendido 2026-04-30)
    5. InspeccionCancelada_v1

  Hallazgos (3):
    6. HallazgoRegistrado_v1
    7. HallazgoActualizado_v1
    8. HallazgoEliminado_v1            (soft delete)

  Novedades preop (1):
    9. NovedadPreopDescartada_v1       (NO crea hallazgo, sólo audit;
                                         emitido por comando individual
                                         DescartarNovedadPreop con motivo
                                         autogenerado por el handler —
                                         ver §15.9 "Descarte rápido inline")

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

Integración OT (3):
   20. OTSolicitada_v1                 (comando GenerarOT autorizado, ADR-007 §17)
   21. OTGeneracionFallida_v1
   22. GeneracionOTRechazada_v1        (NUEVO 2026-04-30 — comando RechazarGenerarOT;
                                         se emite atómicamente con InspeccionCerradaSinOT_v1
                                         de motivo RechazadaPorAprobador, ADR-007 §17)

PDF de inspección (2):
   23. PdfInspeccionGenerado_v1        (NUEVO 2026-04-30 — saga GenerarPdfInspeccionSaga
                                         tras InspeccionFirmada_v1, ADR-007 §17)
   24. PdfAdjuntadoAOT_v1              (NUEVO 2026-04-30 — saga EjecutarOTSaga tras
                                         éxito del POST /mye/ot-correctivas/{id}/adjuntos)

                                       ─────────────
                          TOTAL MVP =   24 eventos

Eventos del flujo Monitoreo — MVP desde 2026-05-05 (antes Fase 2, ver §12.11.5):
  - MedicionRegistrada_v1                  (item numérico — captura valor, calcula FueraDeRango)
  - EvaluacionCualitativaRegistrada_v1     (item cualitativo — Bueno/Regular/Malo)
  - ItemMonitoreoOmitido_v1                (técnico no pudo medir, motivo obligatorio)
  - MedicionActualizada_v1                 (corrección de medición previa, opcional)

Cuando el item dispara hallazgo automático (numérico fuera de rango o cualitativo=Malo,
ver §12.11.5 punto 6), el handler emite el evento del registro + HallazgoRegistrado_v1
con Origen=Monitoreo en un único SaveChangesAsync.
```

### 15.5 Validaciones pre-firma — todas bloqueantes

```
V-F1  ≥1 hallazgo registrado en la inspección
V-F2  Todas las novedades preop están verificadas o descartadas
V-F3  Para cada hallazgo con AccionRequerida = RequiereIntervencion:
        - TipoFallaId presente
        - CausaFallaId presente
        - ≥1 adjunto evidencia
        - Repuestos: 0 o más (NO obligatorio — la intervención puede ser solo
          mano de obra, ajuste, calibración o limpieza). El BOM consolidado
          que se envía a MYE puede ser lista vacía; MYE acepta OT correctiva
          sin repuestos.
V-F4  Dictamen seleccionado (PuedeOperar / ConRestriccion / NoPuedeOperar)
        — siempre obligatorio, independiente de si hay hallazgos con
        RequiereIntervencion (confirmado por Sergio 2026-04-30, cierra
        regla #11 del brief consultor §6). El valor del dictamen está
        restringido por V-F8 (regla nueva 2026-05-04) cuando hay hallazgos
        con AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion}.
V-F5  Firma manuscrita capturada (FirmaUri no vacío)
V-F6  UbicacionFirma capturada (GPS obligatorio, no bloquea si difiere de UbicacionInicio)
V-F7  Estado actual = EnEjecucion
V-F8  Coherencia dictamen ↔ hallazgos (decisión 2026-05-04 — Jaime)
        ─────────────────────────────────────────────────────────────
        Si ∃ ≥1 hallazgo no eliminado con
        AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion}
        entonces Dictamen ∉ {PuedeOperar}.
        Solo se permite Dictamen ∈ {ConRestriccion, NoPuedeOperar}.

        Razón operativa: PuedeOperar (= "Apto") significa que el equipo
        opera sin restricciones. Si hay hallazgos pendientes (intervención
        o seguimiento), por definición hay algo que atender — no es "apto
        sin restricción". Coherente con la decisión 2026-05-04 de Jaime
        de acoplar dictamen y AccionRequerida para evitar inspecciones
        contradictorias en el sentido "todo bien" pero "tengo cosas que
        revisar/reparar".

        Hallazgos con AccionRequerida = NoRequiereIntervencion NO
        restringen el dictamen — un hallazgo "observado pero OK" puede
        coexistir con dictamen PuedeOperar (es información, no problema).

        Implementación: el handler de FirmarInspeccion valida V-F8
        antes de emitir DictamenEstablecido_v1 + InspeccionFirmada_v1.
        Mensaje del rechazo: "No puedes firmar con dictamen 'Apto'.
        Hay {N} hallazgos que requieren {seguimiento|intervención|ambos}.
        Selecciona 'Con restricciones' o 'No apto'."

        UX: el frontend deshabilita el dictamen PuedeOperar mientras
        haya hallazgos elegibles. El backend revalida (no confía en UI).

        Tests obligatorios:
        - Firmar con PuedeOperar + hallazgo RequiereSeguimiento → DomainException.
        - Firmar con PuedeOperar + hallazgo RequiereIntervencion → DomainException.
        - Firmar con PuedeOperar + solo hallazgos NoRequiereIntervencion → 200.
        - Firmar con PuedeOperar sin hallazgos → bloqueado por V-F1 (≥1 hallazgo), no V-F8.
        - Firmar con ConRestriccion + cualquier combinación → 200 (sin restricción de dictamen).
        - Firmar con NoPuedeOperar + cualquier combinación → 200.
```

El botón "Firmar y cerrar inspección" se deshabilita en UI mientras alguna validación falle, y se bloquea en click para evitar doble submit. El backend revalida (no confía en la UI).

> **Sobre la propagación del dictamen al ERP MYE (decisión 2026-04-30, observación Sergio):** además del dictamen embebido en `POST /api/v1/mye/ot-correctivas` cuando se genera OT, el dictamen también se sincroniza al **equipo** en MYE como "dictamen vigente" mediante endpoint dedicado `PUT /api/v1/equipos/{id}/dictamen-vigente`. La invocación es **redundante en el caso con OT** (la OT también lo lleva embebido) pero **necesaria en el caso sin OT** — y consolidamos en un único punto de invocación en lugar de bifurcar la lógica del adapter. Detalle en §17 (ADR-007) sub-sección "Integración con MYE: dictamen vigente del equipo".

### 15.6 Regla automática de cierre con/sin OT

> ⚠️ **SUPERSEDED — fuente de verdad: ADR-007 (§17).**
> A partir del 2026-04-29 (reunión de diseño con Daniel), la generación de OT pasó de **automática al firmar** a **manual con capability gate**. Esta sección queda como referencia histórica del flujo previo. El nuevo flujo:
> - Si la inspección no tiene hallazgos `RequiereIntervencion` → `InspeccionCerradaSinOT_v1` automático (sin cambio).
> - Si los tiene → la saga **NO** invoca MYE inmediatamente. La inspección queda en estado derivado `EsperandoAprobacionOT`. Un usuario con capability `generar-ot` ejecuta el comando `GenerarOT` para emitir `OTSolicitada_v1` y disparar el POST a MYE vía outbox.
> Ver ADR-007 (§17) para decisión, eventos nuevos, state machine completa y trade-offs.

```
[FLUJO HISTÓRICO — superseded por ADR-007]
Al firmar la inspección, la saga CerrarInspeccionSaga evalúa:

   si EXISTE hallazgo con AccionRequerida = RequiereIntervencion (no eliminado)
       → POST a Sinco MYE para crear OT correctiva
            ├─ éxito → InspeccionCerrada_v1 (con OTCorrectivaIdSinco + OTCorrectivaNumero)
            └─ falla → OTGeneracionFallida_v1 (estado CierrePendienteOT, reintento)
   en caso contrario (todos NoRequiereIntervencion o RequiereSeguimiento)
       → InspeccionCerradaSinOT_v1
```

**Nota histórica:** la idea original era que no había opción de "saltar OT" si había hallazgos con intervención. ADR-007 mantiene esa regla — la diferencia es **quién** y **cuándo** dispara el POST a MYE: ya no la saga al firmar, sino un usuario autorizado vía comando explícito.

#### Matriz dictamen × hallazgos × cierre (vigente 2026-05-04)

> **Combinación de V-F8 (§15.5) + I-F4 (§15.7) + flujo automático de cierre.**

| Dictamen | ≥1 RequiereIntervencion | ≥1 RequiereSeguimiento | Cierre | OT |
|---|---|---|---|---|
| **PuedeOperar** ("Apto") | No | No | Auto: `InspeccionCerradaSinOT_v1` motivo `AutomaticoSinIntervencion`. Firma y cierre inmediatos. | Sin OT — `GenerarOT` rechazado por I-F4 (defensa) |
| **PuedeOperar** | Sí | — | ❌ Bloqueado por V-F8 al firmar | (no aplica) |
| **PuedeOperar** | No | Sí | ❌ Bloqueado por V-F8 al firmar | (no aplica) |
| **ConRestriccion** | No | No | Auto: `InspeccionCerradaSinOT_v1` (caso raro pero válido — restricciones derivadas de hallazgos `NoRequiereIntervencion` o de criterio del técnico) | Sin OT |
| **ConRestriccion** | Sí | — | `EsperandoAprobacionOT` → aprobador decide → con OT o rechazo | Posible (M-1 vía outbox tras aprobación manual) |
| **ConRestriccion** | No | Sí | Auto: `InspeccionCerradaSinOT_v1` (no requiere OT — solo seguimiento). Saga abre `SeguimientoHallazgo` para cada hallazgo. | Sin OT — solo seguimientos |
| **NoPuedeOperar** | No | No | Auto: `InspeccionCerradaSinOT_v1` (caso raro — equipo decomisado sin reparación pedida) | Sin OT |
| **NoPuedeOperar** | Sí | — | `EsperandoAprobacionOT` → aprobador decide | Posible |
| **NoPuedeOperar** | No | Sí | Auto: `InspeccionCerradaSinOT_v1` + apertura de `SeguimientoHallazgo` ×N | Sin OT |

**Reglas operativas que se desprenden de la matriz:**

1. **Apto = cierre instantáneo, sin OT.** El usuario no espera, no hay paso de aprobación. Por V-F8, Apto solo es alcanzable cuando todos los hallazgos son `NoRequiereIntervencion` (o no hay hallazgos, lo cual viola V-F1).
2. **OT solo aparece con dictamen `ConRestriccion` o `NoPuedeOperar`** y `≥1 RequiereIntervencion`. Es la única combinación que entra a `EsperandoAprobacionOT`.
3. **Seguimientos se abren independientemente de OT** — la saga `CerrarInspeccionSaga` los crea al firmar para cualquier hallazgo `RequiereSeguimiento`, sin importar el flujo OT.
4. **`SincronizarDictamenVigenteSaga`** (M-W-1) corre en **toda firma** sin importar dictamen — el equipo siempre recibe el dictamen vigente actualizado.

### 15.7 Invariantes de lifecycle

#### Invariantes de inicio (I-I)

```
I-I1  Una sola inspección abierta por equipo
      ────────────────────────────────────────
      Para un EquipoId no puede existir otra inspección con
      Estado = EnEjecucion al ejecutar IniciarInspeccion.

      Confirmada por Sergio (consultor producto) el 2026-04-30. Modelo
      coherente con flujo colaborativo (I2b §15.2): cuando hay inspección
      activa, varios técnicos contribuyen a la misma, no inician otra.

      Implementación robusta (no basta con consulta del handler):
        1. Handler consulta proyección Marten `InspeccionAbiertaPorEquipoView`
           (§15.12.X) — validación blanda con mensaje accionable.
        2. Proyección tiene índice único Postgres sobre EquipoId filtrado
           por `Estado = EnEjecucion` — atrapa race conditions concurrentes
           cuando dos handlers pasan la validación blanda simultáneamente.
        3. Test obligatorio: dos IniciarInspeccion concurrentes sobre
           mismo equipo → uno gana, otro recibe DomainException con
           InspeccionId de la activa.

      UX implícita (decisión 2026-04-30): cuando el técnico tap "Iniciar
      inspección" sobre un equipo que YA tiene inspección activa, el
      frontend NO muestra error — abre la inspección activa para que el
      técnico contribuya (agregue hallazgos, suba evidencia). El backend
      retorna la InspeccionId activa con shortcut "Ya hay inspección
      activa, abriendo la existente" en el response.

      Salida del estado EnEjecucion: solo por firma o cancelación. Una
      vez firmada o cancelada, otro técnico puede iniciar nueva inspección
      sobre el mismo equipo aunque la OT del flujo anterior siga pendiente
      de aprobación (estado derivado EsperandoAprobacionOT, ADR-007 §17).

I-I2  Equipo debe tener rutina técnica asignada para poder ser inspeccionado
      ─────────────────────────────────────────────────────────────────
      Confirmada por Jaime el 2026-04-30. Refinada el 2026-05-04: la
      asignación es **per-equipo en el ERP**, no derivada del grupo
      (decisión 2026-05-04 — opción β). Cardinalidad 1 (única).

      Regla: el handler de IniciarInspeccion rechaza la creación si
      `Equipo.RutinaTecnicaId` (campo del detalle del equipo M-3b) es
      null o si la rutina referenciada no existe en `RutinaTecnicaLocal`.
      Sin rutina asignada no hay catálogo de partes y los hallazgos no
      podrían cumplir I-H1 (ParteEquipoId obligatorio). Por tanto, la
      falta de rutina es bloqueante en el inicio, no un caso a manejar
      tarde.

      Validaciones derivadas (también del handler):
        - rutina.Tipo == TipoRutina.Tecnica (defensa para cuando emerjan
          tipos futuros como PostMantenimiento; en MVP siempre se cumple).
        - rutina existe en RutinaTecnicaLocal (sync vía /catalogos/rutinas).

      Mensaje del rechazo (DomainException): "El equipo {EquipoCodigo}
      no tiene rutina técnica asignada en el ERP. Contacta al admin
      del catálogo en Sinco para asignar una rutina técnica al equipo
      antes de inspeccionar."

      Implicaciones operativas:
        - Datos del ERP (corte 2026-04-30) muestran equipos sin partes
          en la hoja "Partes por Equipo" de varios clientes. La hipótesis
          es que esos equipos no tienen rutina técnica asignada;
          esta invariante los bloquea limpiamente sin código especial.
        - UX: el detalle del equipo (M-3b) puede exponer `rutinaTecnicaId`
          null para que el frontend deshabilite el botón "Iniciar
          inspección" antes del rechazo del backend. El backend siempre
          rechaza, el frontend mejora la UX evitando llamadas perdidas.

      Test obligatorio: equipo sin rutina asignada → 422 con mensaje
      accionable. Equipo con rutina asignada cuya rutina no existe en
      catálogo (sync stale) → 422 con mensaje "rutina referenciada no
      sincronizada — refresca catálogos". Equipo con rutina vacía (sin
      items): caso edge no priorizado en MVP — los items en técnica
      MVP son metadata sugerida (no se recorren); la rutina sin items
      se acepta como template trivial.

I-I3  Rango válido de FechaReportada (decisión 2026-04-30, cierre #2)
      ────────────────────────────────────────────────────────────────
      El campo `FechaReportada` del comando `IniciarInspeccion` debe
      satisfacer ambas condiciones:
        1. FechaReportada <= DateOnly.FromDateTime(IniciadaEn)
           (no puede ser fecha futura).
        2. FechaReportada >= DateOnly.FromDateTime(IniciadaEn).AddDays(-30)
           (no más de 30 días retroactivos — ventana de gracia razonable
           para cargas demoradas por conectividad o jornada).
      Violación → DomainException con mensaje accionable indicando el
      rango aceptable. El handler valida antes de emitir el evento; Apply
      es puro y no re-valida.

      Razón del rango de 30 días: balance entre permitir cargas demoradas
      (técnico carga al final del día / próximo día con conectividad) y
      evitar abuso (registrar inspección "hecha" hace meses sin evidencia
      real). Si emerge un caso operativo legítimo con > 30 días, se
      evalúa extensión como cambio aditivo + ADR de auditoría especial.

      `IniciadaEn` no entra en esta invariante — siempre es el timestamp
      del sistema generado por TimeProvider en el handler (regla dura
      del CLAUDE.md). Solo `FechaReportada` es validable contra reglas
      operativas.
```

#### Invariantes post-firma (I-F)

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
I-F4  Comando GenerarOT (introducido por ADR-007 §17) requiere TODAS estas
      precondiciones (validadas en handler antes de emitir OTSolicitada_v1):
        - Estado actual = Firmada
        - ≥1 hallazgo no eliminado con AccionRequerida = RequiereIntervencion
        - !OTSolicitada (no se aceptan dos OTSolicitada_v1 sobre el mismo stream)
        - !OTRechazada (no se solicita lo que ya se rechazó)
        - Usuario tiene capability `generar-ot` en su contexto del host PWA
        - Dictamen ∈ {ConRestriccion, NoPuedeOperar} (defensa segunda
          contra dictamen PuedeOperar — extendida 2026-05-04 por
          decisión Jaime: "cuando el dictamen sea Apto no permita
          generar OT y la inspección se firme y se cierre"). Por V-F8
          esta condición ya está garantizada al firmar; la verificación
          en I-F4 es defensa explícita ante inconsistencias previas
          al deploy de V-F8 o ediciones futuras del aggregate.
      Violar cualquiera lanza excepción de dominio en el método de decisión
      (no en Apply — ver convención de capa en CLAUDE.md).
I-F5  Estado derivado EsperandoAprobacionOT (introducido por ADR-007) =
        Estado=Firmada AND existe ≥1 hallazgo RequiereIntervencion no eliminado
        AND !OTSolicitada AND !OTRechazada
      No es estado persistido; lo computa la proyección §15.12.5
      (BandejaInspeccionesPendientesOTView).
I-F6  Comando RechazarGenerarOT (introducido 2026-04-30 por observación
      Sergio, detalle en ADR-007 §17) requiere TODAS estas precondiciones:
        - Estado actual = Firmada
        - ≥1 hallazgo no eliminado con AccionRequerida = RequiereIntervencion
        - !OTSolicitada (alcance limitado: no se puede rechazar tras solicitar)
        - !OTRechazada (sin doble rechazo)
        - Usuario tiene capability `generar-ot`
        - Motivo no vacío y >= 10 chars
      Atomic: el handler emite GeneracionOTRechazada_v1 + InspeccionCerradaSinOT_v1
      (con MotivoCierreSinOT=RechazadaPorAprobador) en un único SaveChangesAsync.
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
    public int EquipoId               { get; private set; }   // sigue al equipo cross-proyecto

    // Origen (referencias inmutables)
    public Guid HallazgoOrigenId       { get; private set; }
    public Guid InspeccionOrigenId     { get; private set; }
    public int ParteEquipoId          { get; private set; }
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
    int EquipoId,
    Guid HallazgoOrigenId,
    Guid InspeccionOrigenId,
    int ParteEquipoId,
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
    Guid InspeccionCierreId,         // inspección donde se decide la escalación = donde nace el nuevo hallazgo
    Guid HallazgoEscaladoId,         // ref al nuevo hallazgo creado en la inspección actual
    string EscaladoPor,
    DateTime EscaladoEn
);
```

> **Trazabilidad bidireccional**: este evento (en el stream de `SeguimientoHallazgo`) apunta hacia adelante con `HallazgoEscaladoId` + `InspeccionCierreId`. El hallazgo nuevo (en el stream de `InspeccionTecnica`) apunta hacia atrás con `Origen=Seguimiento` + `SeguimientoOrigenId` (ver §15.2). La cadena completa permite reconstruir "este hallazgo viene del seguimiento S, que se abrió en la inspección X meses atrás" sin proyecciones laterales.

#### 15.8.5 Decisiones operativas (aprobadas 2026-04-28)

| # | Decisión | Resolución |
|---|---|---|
| 1 | ¿Quién puede cerrar? | Cualquier técnico que inspeccione el equipo posteriormente |
| 2 | ¿"Seguimiento" emite evento? | **No** — es no-op silencioso. Solo feedback visual al técnico (toast + card resaltada). Si más adelante se requiere reportería de "¿hace cuánto nadie revisa?", se agrega `SeguimientoRevisadoSinCambio_v1` como cambio aditivo. |
| 3 | SLA de seguimientos viejos | Alerta visual progresiva + email diario a usuarios con capability `recibir-alertas-sla` configurados como destinatarios para el equipo, a partir de 90 días. Sin bloqueo de inspección. Sin OT automática. |
| 4 | Visibilidad cross-proyecto | El seguimiento sigue al equipo, no al proyecto. Se ve desde cualquier proyecto al que el equipo se mueva. |

#### 15.8.6 SLA visual y notificación

```
0–30 días:    badge azul "Abierto"
30–90 días:   badge naranja "Atención" 
+90 días:     badge rojo "Vencido" + email diario a destinatarios SLA del equipo
              (usuarios con capability `recibir-alertas-sla` configurados para
              ese equipo en el catálogo) hasta que alguien lo cierre o escale

NO se genera OT automáticamente (faltan datos: TipoFalla, CausaFalla, repuestos)
NO se bloquea la inspección si hay seguimientos vencidos
NO se "escala" el aggregate solo; siempre requiere acción humana
```

Implementación: job nocturno (Wolverine scheduled task) que escanea seguimientos `Estado=Abierto AND AbiertoEn < now-90d` y dispara correo a la lista `DestinatariosAlertasSlaPorEquipo` (proyección leída del catálogo de proyectos/equipos sincronizado desde MYE — el contenido de la lista es data, no código). Si la lista está vacía para un equipo, el job emite `AlertaSlaSinDestinatario_v1` (evento de observabilidad) en lugar de fallar silenciosamente.

#### 15.8.7 Invariantes del aggregate `SeguimientoHallazgo`

```
I-S1  SeguimientoEscalado_v1 sólo es válido si el HallazgoEscaladoId referencia
      un hallazgo con AccionRequerida = RequiereIntervencion
      (la escalación implica intervención por definición — no se "escala" a otro
       seguimiento; ver simétrica I-H11 en §15.3)

I-S2  Cross-aggregate (enforzada en el handler de EscalarSeguimiento, no en Apply):
      el HallazgoEscaladoId del evento debe coincidir con el HallazgoId del
      HallazgoRegistrado_v1 emitido en el mismo SaveChangesAsync sobre
      InspeccionCierreId. Es decir: el handler emite atómicamente
      SeguimientoEscalado_v1 (stream del seguimiento) + HallazgoRegistrado_v1
      (stream de la inspección), con HallazgoId compartido y SeguimientoOrigenId
      apuntando al SeguimientoId. Marten lo persiste en una sola transacción.

I-S3  Estado terminal: una vez Resuelto o Escalado, el SeguimientoHallazgo no
      acepta más eventos. Re-emisión de SeguimientoResuelto_v1 o
      SeguimientoEscalado_v1 sobre Estado ≠ Abierto debe lanzar excepción de
      dominio en el método de decisión (no en Apply — ver convención de capa
      en CLAUDE.md)

I-S4  El "↻ Seguimiento" (no-op silencioso) NO emite evento. Si el read model
      necesita reportar "revisado sin cambio", se introduce
      SeguimientoRevisadoSinCambio_v1 como cambio aditivo, no se reabre I-S3.
```

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

Wireframes / referencia visual:
- **Fuente vigente 2026-04-30:** `Plantillas Excel/mock del diseño.docx` (mock de Daniel — image1 home, image2 calendario fecha, image3 buscador equipo, image4 previsualización equipo, image5 inspección vacía, image7+9 wizard, image10 lista de hallazgos, image11+12 pantalla Importar con tabs).
- `02d-wireframes-seguimientos.html` (mock complementario del flujo de seguimientos — sigue vigente, no fue eliminado).
- ~~`02-wireframes-mobile.html`~~ y ~~`02b-wireframes-novedades-preop.html`~~ — eliminados el 2026-04-30 (superseded por mock de Daniel).

### 15.11 Cambios pendientes en otras secciones del documento

Las secciones §2.1 a §14 quedan como histórico. Las siguientes referencias deben leerse a la luz de §15:

| Sección | Referencia obsoleta | Reemplazar por |
|---|---|---|
| §2.1 Hallazgo | Campo `Severidad` | Eliminado (ver §15.2) |
| §2.1 Eventos | `NovedadPreopVerificada_v1` con `ResultadoVerificacion` | Consolidado en `HallazgoRegistrado_v1` (verificar/seguimiento) o en `NovedadPreopDescartada_v1` (descartar) |
| §6 Saga cierre | "evalúa severidad" | Evalúa `AccionRequerida = RequiereIntervencion` |
| Invariantes I7 (severidad crítica → dictamen) | Usaba `Severidad.Critica` | Usar `AccionRequerida = RequiereIntervencion` para forzar dictamen NoApto/AptoConRestricciones (sugerido, no forzado — ver §15.5 V-F4) |
| §12.10.x decisiones operativas | Algunas usaron `ResultadoVerificacion` | Ahora se decide vía botón en lista (variante B) — el evento emitido depende del botón, no de un selector dentro del wizard |

> ✅ **Limpieza completada (2026-04-28).** Las secciones §2.1, §3, §6, §7, §7.4.5, §12.10.8, §12.10.9, §12.10.10 y la fila I7 de la tabla de invariantes ahora llevan banner de obsolescencia (`⚠️ SECCIÓN HISTÓRICA`) o nota inline (`⚠️ OBSOLETO`) apuntando a la subsección de §15 que define el contrato vigente. El histórico se preserva porque documenta la evolución del modelo; los lectores nuevos quedan dirigidos siempre a §15 antes de actuar sobre código antiguo. La tabla de arriba se conserva como índice de referencia rápida.

### 15.12 Read models y proyecciones (audiencias y campos requeridos)

Los aggregates son fuente de verdad; las proyecciones son vistas materializadas que sirven a audiencias específicas. Esta sección documenta qué proyecciones existen, qué eventos consumen y qué campos exponen, para que las decisiones de read model no queden implícitas en endpoints.

#### 15.12.1 `DetalleInspeccionView` — vista de detalle de inspección

Audiencia (por capability, no por perfil ERP):

- **Auto-consulta**: usuario contribuyente del stream (registró ≥1 evento) con capability `ejecutar-inspeccion`.
- **Auditoría puntual**: usuario con capability `auditar-inspecciones` sobre el proyecto al que pertenece la inspección.

El mapping de perfiles ERP (jefe de campo, mecánico líder, ingeniero residente, contralor, etc.) → capabilities lo define el host PWA, no este módulo. Ver ADR-002 + paso 2.5 del roadmap.

Materializada por proyección Marten que consume todos los eventos del stream `InspeccionTecnica`.

Endpoint que la sirve: `GET /inspecciones/{id}` (paso 3.45 del roadmap).

Campos clave que **debe** exponer:

- Lifecycle: estado actual, timestamps de cada transición, contribuyentes del stream, OT correctiva (id + número MYE) si aplica.
- Hallazgos no eliminados: id, parte, descripción, AccionRequerida, tipo/causa de falla, adjuntos, repuestos, Origen y trazabilidad (`NovedadPreopOrigenId` o `SeguimientoOrigenId` según I-H2/I-H10/I-H3).
- Hallazgos eliminados (soft-delete): se incluyen con flag `Eliminado=true` y `MotivoEliminacion`. El flujo de auditoría requiere ver qué se eliminó y por qué.
- **Novedades preop descartadas**: por cada `NovedadPreopDescartada_v1` en el stream, exponer `NovedadPreopId` + `MotivoDescarte` + `DescartadaPor` + `DescartadaEn`. Sin esto, una decisión de gobernanza (la decisión técnica contradice lo reportado por el operador) queda invisible. **Requisito explícito** — no opcional.
- Diagnóstico final + dictamen.
- Firma: usuario firmante, GPS de firma, FirmaUri.

#### 15.12.2 `AuditoriaInspeccionesView` — vista cross-inspección por proyecto

> **Estado MVP (decisión 2026-05-04):** se materializa desde Fase 3 con **shape mínimo** (campos básicos + indicador `DecisionContradiceReporteOperador` regla (a) sola). Métricas agregadas y campos derivados complejos quedan diferidos a Fase 10 post-piloto.

**Audiencia:** usuarios con capability `auditar-inspecciones`. Distinto de `DetalleInspeccionView` porque cruza múltiples inspecciones en una bandeja filtrable, no detalla una inspección individual. El host PWA mapea perfiles ERP (jefe de campo / supervisor / contralor / gerente de mantenimiento) a esta capability.

**Read model:** proyección Marten que consume eventos del aggregate `InspeccionTecnica` (16 eventos MVP) + joins con catálogos locales (`ProyectoLocal`, `EquipoLocal`). **No** consume `SeguimientoHallazgo` en MVP — el conteo de seguimientos disparados se difiere a Fase 10 (decisión 2026-05-04 punto 2).

**Endpoint:** `GET /auditoria/inspecciones?proyecto=&desde=&hasta=&autor=&equipo=&dictamen=&estadoOT=&contradiceReporte=` (paso 3.56 del roadmap).

##### Filtros del listado

| Query param | Tipo | Acción |
|---|---|---|
| `proyecto` | int (multi) | Filtra por uno o más proyectos asignados al auditor |
| `desde` / `hasta` | DateOnly | Rango de fecha de **firma** de la inspección |
| `autor` | string (multi) | Username del firmante |
| `equipo` | string | Búsqueda por código de equipo |
| `dictamen` | enum (multi) | `Apto` / `AptoConRestricciones` / `NoApto` |
| `estadoOT` | enum (multi) | `Generada` / `SinOT` / `Fallida` / `Rechazada` / `Cancelada` |
| `contradiceReporte` | bool | Si true, solo inspecciones con `DecisionContradiceReporteOperador=true` |
| `page` / `size` | int | Paginación estándar (§1.3 contrato) |

##### Forma del item (MVP shape mínimo)

```csharp
public sealed record AuditoriaInspeccionItem(
    Guid InspeccionId,                       // para drill-down a DetalleInspeccionView
    int ProyectoId,
    string ProyectoCodigo,
    string ProyectoNombre,
    int EquipoId,
    string EquipoCodigo,
    string EquipoDescripcion,
    string FirmadaPor,                       // username del firmante
    DateTime FirmadaEn,
    DateOnly FechaReportada,                 // día real de la inspección (input del técnico)
    DateTime IniciadaEn,                     // para calcular TiempoEjecucion
    TimeSpan TiempoEjecucion,                // FirmadaEn - IniciadaEn (derivado al servir)
    DictamenOperacion Dictamen,              // Apto | AptoConRestricciones | NoApto
    EstadoOT EstadoOT,                       // ver enum abajo
    int? OTCorrectivaIdSinco,                // poblado si Generada
    string? OTCorrectivaNumero,              // ej. "OT-123456" si Generada
    string? MotivoCierreSinOT,               // AutomaticoSinIntervencion | RechazadaPorAprobador
    HallazgosCounts Hallazgos,
    NovedadesPreopCounts NovedadesPreop,
    int RepuestosEstimadosCount,
    int HallazgosEliminadosCount,            // count soft-deletes — banderazo de auditoría
    bool DecisionContradiceReporteOperador); // regla (a) sola para MVP — ver abajo

public sealed record HallazgosCounts(
    int Total,                               // no eliminados
    int RequiereIntervencion,
    int RequiereSeguimiento,
    int NoRequiereIntervencion);

public sealed record NovedadesPreopCounts(
    int Verificadas,                         // resultaron en HallazgoRegistrado_v1 con Origen=PreOperacional
    int Descartadas);                        // resultaron en NovedadPreopDescartada_v1

public enum EstadoOT
{
    Generada,                                // InspeccionCerrada_v1 con OTCorrectivaIdSinco
    SinOT,                                   // InspeccionCerradaSinOT_v1 motivo=AutomaticoSinIntervencion
    Rechazada,                               // InspeccionCerradaSinOT_v1 motivo=RechazadaPorAprobador
    Fallida,                                 // OTGeneracionFallida_v1 sin retry exitoso
    Cancelada                                // InspeccionCancelada_v1 (estado terminal alternativo)
}
```

##### Definición de `DecisionContradiceReporteOperador` (MVP — regla a sola)

> **Decisión 2026-05-04:** MVP usa **solo regla (a)**. Regla (b) se evalúa en Fase 10 post-piloto.

**Regla (a):** `true` si hay **≥1 evento `NovedadPreopDescartada_v1`** en el stream de la inspección.

**Por qué:** descartar una novedad reportada por el operario es la decisión técnica más fuerte de gobernanza — el técnico explícitamente niega el reporte. Es el caso que más quiere auditar el supervisor.

**Diferido a Fase 10 — regla (b):** "≥1 hallazgo donde el firmante bajó la urgencia respecto a lo reportado por el operador". Requiere que el preop pueble `severidad` en P-2 consistentemente y un mapeo `severidadPreop ↔ AccionRequerida`. Si el piloto muestra que falta cobertura del indicador, se agrega como cambio aditivo (no rompe shape del read model — solo cambia la fórmula).

##### KPIs del header (cards superiores — agregados sobre el rango filtrado)

| KPI | Cálculo |
|---|---|
| Total inspecciones cerradas | `count(items)` |
| Con OT | `count(items where EstadoOT = Generada)` |
| Sin OT | `count(items where EstadoOT = SinOT)` |
| Rechazadas | `count(items where EstadoOT = Rechazada)` |
| Fallidas | `count(items where EstadoOT = Fallida)` |
| Contradicen reporte | `count(items where DecisionContradiceReporteOperador = true)` |
| Tiempo medio | `avg(TiempoEjecucion)` |
| Tasa de descarte preop | `sum(NovedadesPreop.Descartadas) / sum(NovedadesPreop.Verificadas + Descartadas) * 100%` |

##### Eventos consumidos por la proyección

Stream `InspeccionTecnica` (todos los eventos MVP):
- Lifecycle: `InspeccionIniciada_v1`, `InspeccionFirmada_v1`, `InspeccionCerrada_v1`, `InspeccionCerradaSinOT_v1`, `InspeccionCancelada_v1`
- Hallazgos: `HallazgoRegistrado_v1`, `HallazgoActualizado_v1`, `HallazgoEliminado_v1`
- Novedades preop: `NovedadPreopDescartada_v1`
- Repuestos: `RepuestoEstimado_v1`, `RepuestoActualizado_v1`, `RepuestoRemovido_v1`
- Firma: `DiagnosticoEmitido_v1`, `DictamenEstablecido_v1`
- OT: `OTSolicitada_v1`, `OTGeneracionFallida_v1`, `GeneracionOTRechazada_v1`

Joins con catálogos locales: `ProyectoLocal` (codigo + nombre), `EquipoLocal` (codigo + descripcion).

**No** consume `SeguimientoHallazgo` en MVP. **No** consume `AdjuntoSubido_v1`/`AdjuntoEliminado_v1` (los adjuntos se ven en el drill-down, no en la bandeja).

##### Drill-down

Click en fila → `DetalleInspeccionView` (§15.12.1) — endpoint `GET /inspecciones/{id}` con autorización por capability `auditar-inspecciones`. El detalle ya muestra hallazgos eliminados con `MotivoEliminacion`, novedades descartadas con `MotivoDescarte`, etc.

##### Diferido a Fase 10 (post-piloto)

- **`SeguimientosAbiertosCount`** por fila — requiere consumir stream `SeguimientoHallazgo`.
- **Regla (b)** de `DecisionContradiceReporteOperador` — requiere `severidad` consistente en preop.
- **Métricas agregadas** por usuario firmante / por operador que reportó (tasa de descarte, etc.).
- **Workflow de notificación** al operador cuando su novedad es descartada.

##### Wireframe de referencia

`02l-mock-auditoria-inspecciones.html` (decisión 2026-05-04) — mock interactivo con filtros + KPI cards + tabla + drill-down placeholder.

#### 15.12.3 `BandejaTecnicoView` — bandeja "Mis inspecciones"

Audiencia: usuario con capability `ejecutar-inspeccion` (su propia bandeja). Filtro adicional `?equipo=` y `?estado=` para acotar.

Materializada por proyección Marten `MultiStreamProjection` que consume eventos del stream `InspeccionTecnica` (creación, transiciones de estado, cierre, cancelación) y joins de catálogo (`EquipoLocal`, `ProyectoLocal`, `Rutina`) para denormalizar códigos y nombres.

Endpoint que la sirve: `GET /inspecciones?equipo=&estado=` (paso 3.44 del roadmap).

Campos clave que **debe** exponer (una fila por inspección del técnico):

```csharp
public sealed record BandejaItem(
    Guid InspeccionId,
    string TecnicoId,
    int EquipoId, string EquipoCodigo,
    int RutinaId, string RutinaCodigo, string RutinaNombre,
    int ProyectoId, string ProyectoNombre,
    DateTime IniciadaEn,
    InspeccionEstado Estado,
    int HallazgosCount,
    int RepuestosEstimadosCount,
    DictamenOperacion? UltimoDictamen);
```

Notas operativas:

- `HallazgosCount` excluye hallazgos eliminados (soft-delete) — la bandeja muestra trabajo vigente, no audit.
- Para inspecciones colaborativas (multi-técnico), la fila aparece para **cada** contribuyente del stream (ver `TecnicosContribuyentes` del aggregate); `TecnicoId` distingue a quién pertenece la fila.
- Costo estimado y otros derivados financieros NO van aquí — son para `KPIsPorObra` (§5.3, diferido).

#### 15.12.4 `SeguimientosAbiertosPorEquipoView` — bandeja de seguimientos por equipo

Audiencia:

- **Técnico** iniciando una inspección sobre el equipo (capability `ejecutar-inspeccion`): consume el banner de Pantalla 1 ("X seguimientos previos del equipo") y la lista de Pantalla 2 (§15.8.3 lifecycle).
- **Auditor / supervisor** (capability `auditar-inspecciones`) revisando seguimientos vencidos por equipo.

Materializada por proyección Marten que consume eventos del stream `SeguimientoHallazgo`: `SeguimientoAbierto_v1`, `SeguimientoResuelto_v1`, `SeguimientoEscalado_v1`. Joins con catálogo `EquipoLocal` para denormalizar `EquipoCodigo`.

Endpoint que la sirve: `GET /equipos/{equipoId}/seguimientos?estado=Abierto` (paso 3.46 del roadmap).

Campos clave que **debe** exponer (una fila por seguimiento):

- `SeguimientoId`, `EquipoId`, `EquipoCodigo`.
- Origen: `HallazgoOrigenId`, `InspeccionOrigenId`, `ParteEquipoId`, `DescripcionOrigen`.
- Apertura: `AbiertoPor`, `AbiertoEn`.
- Estado: `Estado` (`Abierto` | `Resuelto` | `Escalado`), `CerradoEn?`, `CerradoPor?`, `MotivoCierre?`, `HallazgoEscaladoId?`, `InspeccionCierreId?`.
- **Antigüedad**: `DiasAbierto` (derivado del `TimeProvider` al consultar — no persistido) + `BadgeSla` (`Azul` < 30d, `Naranja` 30–90d, `Rojo` ≥ 90d, ver §15.8.6).

Notas operativas:

- Filtro por `?estado=` permite recuperar histórico de seguimientos resueltos/escalados por equipo (auditoría de "qué pasó con los seguimientos que abrimos hace meses").
- `BadgeSla` es **derivación de presentación**: se calcula al servir, no se materializa con timestamp congelado. Esto evita reproyecciones diarias por cambio de bucket.
- Cross-proyecto: la proyección agrupa por `EquipoId`, no por `ProyectoActualId` — un seguimiento sigue al equipo (decisión §15.8.5 punto 4).

#### 15.12.5 `BandejaInspeccionesPendientesOTView` — bandeja de aprobación de OT

> Introducida por ADR-007 (§17). El estado `EsperandoAprobacionOT` no se persiste como evento; se deriva.

Audiencia: usuarios con capability `generar-ot`. Es la cola de trabajo del aprobador (jefe de campo / supervisor / contralor — el host PWA mapea perfiles a capability).

Materializada por proyección Marten que consume eventos de `InspeccionTecnica`: `InspeccionFirmada_v1`, `HallazgoRegistrado_v1` / `HallazgoActualizado_v1` / `HallazgoEliminado_v1` (para conocer si hay ≥1 RequiereIntervencion no eliminado), `OTSolicitada_v1`, `GeneracionOTRechazada_v1`, `InspeccionCerrada_v1`, `OTGeneracionFallida_v1`. Una inspección entra a la bandeja al recibir `InspeccionFirmada_v1` con ≥1 RequiereIntervencion no eliminado, y sale por: `OTSolicitada_v1` (pasa a `EnProcesoOT`), `GeneracionOTRechazada_v1` (rechazada por aprobador, decisión 2026-04-30), o nunca aparece si llegó `InspeccionCerradaSinOT_v1` por motivo `AutomaticoSinIntervencion`.

Endpoint que la sirve: `GET /inspecciones/pendientes-ot?proyecto=&firmada-desde=&firmada-hasta=` (paso 3.45b del roadmap).

Campos clave por fila:

- `InspeccionId`, `EquipoId`, `EquipoCodigo`, `ProyectoId`, `ProyectoNombre`.
- `FirmadaPor`, `FirmadaEn`.
- `HallazgosIntervencionCount` — conteo de hallazgos no eliminados con `AccionRequerida = RequiereIntervencion`.
- `RepuestosEstimadosCount` (consolidado del BOM previsto).
- `Dictamen` (Apto / AptoConRestricciones / NoApto).
- `EstadoOT`: `EsperandoAprobacion` | `EnProceso` (post-OTSolicitada, pre-confirmación MYE) | `Fallida` (post-OTGeneracionFallida) | `Rechazada` (post-GeneracionOTRechazada — terminal, decisión 2026-04-30).
- `DiasFirmada` — antigüedad desde firma (derivado al consultar). Útil para SLA visual: una inspección con 7+ días esperando aprobación debe alertar.

Notas operativas:

- Una inspección `EnProceso` (post-`OTSolicitada_v1`, esperando confirmación de MYE) sigue visible en la bandeja con `EstadoOT=EnProceso` para que el aprobador vea que su solicitud está en camino — sale solo al cierre o al fallo.
- Si llega `OTGeneracionFallida_v1`, vuelve a aparecer con `EstadoOT=Fallida` y permite reintento (capability `generar-ot` también autoriza retry, paso 3.G del roadmap).
- SLA de aprobación pendiente (alerta a las X horas/días) — diferido. Followup en `FOLLOWUPS.md` cuando emerja.

#### 15.12.6 `InspeccionAbiertaPorEquipoView` — uniqueness para regla I-I1

**Origen:** decisión 2026-04-30 (Sergio observación). Materializa el constraint "una sola inspección abierta por equipo" (I-I1, §15.7).

**Audiencia:** handler de `IniciarInspeccion` (consulta blanda) + Postgres (constraint duro contra race conditions).

**Forma:**

```csharp
public sealed record InspeccionAbiertaPorEquipoView(
    int EquipoId,                  // KEY con índice único Postgres parcial
                                     // (filtrado por Estado=EnEjecucion)
    Guid InspeccionId,
    string TecnicoIniciador,
    DateTime IniciadaEn,
    int ProyectoId);
```

**Eventos consumidos:**
- `InspeccionIniciada_v1` → upsert fila con `EquipoId` como key.
- `InspeccionFirmada_v1` → delete fila (sale de `EnEjecucion`).
- `InspeccionCancelada_v1` → delete fila.

**Constraint Postgres** (declarado en migración Marten):

```sql
CREATE UNIQUE INDEX ix_inspeccion_abierta_equipo_unique
    ON mt_doc_inspeccionabiertaporequipoview (data->>'EquipoId');
```

(El índice cubre toda la tabla porque la fila solo existe mientras la inspección está `EnEjecucion` — no hace falta filtrado parcial.)

**Comportamiento de race condition:**
1. Técnico A ejecuta `IniciarInspeccion(equipoX)`. Handler valida blando (no hay activa) → emite `InspeccionIniciada_v1`. La proyección upsert.
2. Técnico B simultáneamente ejecuta lo mismo. Handler valida blando contra read model **stale** (no ve la fila aún). Emite `InspeccionIniciada_v1`. La proyección intenta upsert → Postgres rechaza por unique violation.
3. El error de proyección lo atrapa Wolverine: cancela el commit del stream y devuelve excepción al handler de B.
4. B recibe error, recarga, ahora ve la inspección activa, redirige al técnico a la existente (UX I-I1).

**Endpoint que la usa:** ninguno directamente — es interna al handler. La bandeja de inspecciones del técnico la sirve `BandejaTecnicoView` (§15.12.3).

#### 15.12.7 Convenciones para proyecciones futuras

- Cada proyección declara explícitamente: audiencia, eventos consumidos, endpoint que la sirve.
- Las proyecciones son **stale-while-revalidate-friendly**: tolerar lag de segundos respecto al stream.
- Marten reconstruye proyecciones desde stream cuando cambia su shape — no asumir migraciones tipo SQL.
- Los read models nunca son fuente de verdad. Si un read model contradice el stream, el stream gana y se reproyecta.

---

## 16. ADR-006 — Resiliencia y outbox para integraciones ERP (2026-04-29)

**Estado:** Aceptada.

### Contexto

El módulo cloud integra con el ERP Sinco on-prem vía REST sobre VPN (ADR-001). La VPN puede tener intermitencias, el ERP puede estar en mantenimiento programado, los endpoints pueden devolver `5xx` transitorios. Sin un patrón uniforme de resiliencia, cada llamada fallida desde una saga implica:

- Dato perdido (la saga termina sin completar la integración).
- Estado del aggregate inconsistente (firmamos la inspección pero la OT no se creó).
- Imposibilidad de retomar el trabajo automáticamente — requiere intervención humana.

ADR-003 §13 ya documenta el patrón de retry para `POST /mye/ot-correctivas` (M-1). Pero el problema es general: aplica a **todo** `POST` que el módulo emita hacia el ERP. Esta ADR generaliza la solución y la convierte en regla arquitectónica del módulo.

### Decisión

**Todo `POST` del módulo hacia el ERP se ejecuta a través del outbox de Wolverine, no como llamada HTTP sincrónica desde un handler.**

#### Patrón de invocación

1. El handler / saga emite un mensaje al outbox **dentro de la misma transacción** que persiste eventos al stream del aggregate. Atomicidad por `IDocumentSession.SaveChangesAsync()` único: el evento queda guardado **si y solo si** el mensaje del outbox queda guardado. Esto se alinea con la regla dura de "atomicidad de eventos" en `CLAUDE.md`.
2. Wolverine consume el outbox de forma asíncrona y ejecuta la llamada HTTP al ERP.
3. Ante fallo (timeout, `5xx`), Wolverine reintenta con backoff exponencial: **5s → 30s → 2m → 10m**.
4. Tras agotar reintentos, el mensaje va a **dead-letter queue**. Se emite un evento de observabilidad específico al dominio (ej. `OTGeneracionFallida_v1` para M-1) y el aggregate transiciona a un estado que refleja el bloqueo (ej. `CierrePendienteOT`). Notificación a destinatarios con capability `recibir-alertas-ot-fallida` (o equivalente).

#### Tabla de respuesta del adapter (general)

| Respuesta del ERP | Acción del adapter |
|---|---|
| `200 OK` (o `201 Created`) | Marca el outbox como completado. Aggregate avanza al siguiente estado |
| `4xx` (validación, autz, recurso no existe) | NO retry — error permanente. Marca outbox como fallido. Emite evento de dominio específico (`OTGeneracionFallida_v1`, etc.). Notifica destinatarios |
| `5xx` / timeout / error de red | Retry con backoff. Tras agotar política → dead-letter + evento de fallo + notificación |

#### Implicaciones para el ERP (lado contrato)

- **Idempotencia real obligatoria** sobre `Idempotency-Key` (ver `06-contrato-apis-erp.md §1.4`). El ERP recibirá la misma key múltiples veces durante reintentos — debe responder igual a todas, sin crear duplicados.
- **Volúmenes esperados**: en operación normal ~1 llamada por evento; en degradación hasta 5 llamadas con la misma key dentro de una hora. Dimensionar capacidad y rate limiting.
- **Latencia tolerable**: hasta ~30s por llamada antes de timeout y reintentar. Si una operación requiere procesamiento más largo, considerar patrón async con polling o webhook (caso por caso).

#### Mecanismos de idempotencia aplicados (mapa con guía EDA Sinco §5)

La guía EDA Sinco §5 define tres mecanismos para garantizar idempotencia en consumo asíncrono. El módulo los usa los tres, en capas:

| Mecanismo (guía §5) | Aplicación en el módulo |
|---|---|
| **Clave de idempotencia** | Header `Idempotency-Key` en cada `POST` saliente (ver §1.4 del contrato). El ERP debe mantener el mapeo `key → respuesta` durante ≥30 días. |
| **Detección por estado** | Pre-condiciones en los métodos de decisión del aggregate (V-F1..V-F7 §15.5, I-F4 §15.7). Si el aggregate ya está en el estado esperado (p. ej. `OTSolicitada` ya emitida, inspección ya firmada), la decisión rechaza el comando antes de tocar el outbox. Esto cubre replays internos por reintento de saga, no solo retries HTTP. |
| **Concurrencia optimista** | Marten valida la versión del stream en cada `SaveChangesAsync()`. Dos handlers concurrentes sobre el mismo `InspeccionId` colisionan y solo uno persiste; el otro se reintenta o falla limpio. |

#### Excepciones (lo que NO va por outbox)

- **`GET` (lectura)**: invocados sincrónicamente desde frontend o desde sync on-app-open de catálogos (ADR-004 canonical 2026-05-05 — sin cron). Patrón distinto: stale-while-revalidate (ADR-004). Si el GET falla, el cliente reintenta en la próxima apertura o usa último cached.
- **Llamadas no transaccionales**: emails, métricas, telemetría. Tienen su propio canal y no requieren outbox del aggregate.

### Atomicidad outbox + stream del aggregate

```csharp
public sealed class CerrarInspeccionSaga
{
    public async Task Handle(InspeccionFirmada_v1 evt, IDocumentSession session, IMessageBus bus)
    {
        // ... lógica ...

        // 1. Persistir eventos al stream del aggregate
        session.Events.Append(evt.InspeccionId, /* eventos */);

        // 2. Encolar mensaje al outbox (Wolverine lo persiste en la misma transacción)
        await bus.PublishAsync(new CrearOTCorrectivaCommand(/* ... */));

        // 3. Único SaveChangesAsync — atomicidad garantizada
        await session.SaveChangesAsync();

        // Si el SaveChanges falla, ni el evento ni el mensaje del outbox quedan. Coherencia preservada.
    }
}
```

Wolverine procesa el outbox después de que la transacción committea. Aunque el handler retorne exitosamente, la llamada HTTP al ERP puede tomar minutos o horas en completarse. El cliente del frontend recibe el resultado vía SignalR push (ADR-005) cuando el outbox entrega.

### Endpoints sujetos al patrón (vigente al 2026-04-29)

| Endpoint | Slice consumidor |
|---|---|
| `POST /api/v1/preop/novedades/{id}/verificar` (P-5) | `CerrarInspeccionSaga` (paso 3.28) |
| `POST /api/v1/preop/novedades/{id}/descartar` (P-6) | `CerrarInspeccionSaga` (paso 3.29) |
| `POST /api/v1/mye/ot-correctivas` (M-1) | `CerrarInspeccionSaga` (paso 3.27) — detalle exhaustivo en ADR-003 |

**La regla es general**: cualquier `POST` futuro hacia el ERP debe seguir este patrón sin excepción.

### Observabilidad

- **Métricas por endpoint** (App Insights):
  - `outbox.attempts{endpoint}` — total de intentos
  - `outbox.success_after_n_retries{endpoint, n}` — éxito tras n reintentos
  - `outbox.dead_letter_count{endpoint}` — mensajes a dead-letter
  - `outbox.duration_ms{endpoint, p50|p95|p99}` — distribución de latencia
- **Logs estructurados**: cada intento registra `inspeccionId`, `idempotencyKey`, `attemptNumber`, `responseCode`, `durationMs`, `errorMessage` (si aplica).
- **Alertas**:
  - `outbox.dead_letter_count > 0` → notificación inmediata al equipo de operaciones.
  - `outbox.retry_rate > 10%` sostenida 1h → warning (sospecha de degradación del ERP o VPN).

### Consecuencias y trade-offs

**Pros:**
- Resiliencia uniforme: ningún `POST` se pierde por fallo transitorio.
- Atomicidad de aggregate state + intent-to-publish: el evento de dominio se emite **si y solo si** el outbox queda registrado.
- Operacional: dead-letter visible, notificación automática, replay manual posible desde panel admin.
- Desacopla latencia del cliente: el frontend ve "Inspección firmada" inmediatamente; la integración con ERP corre en background.

**Cons:**
- Latencia mayor para el técnico: ve "OT generada" minutos después de firmar, no instantáneamente. Mitigado con SignalR push (ADR-005).
- Complejidad operacional: requiere monitoreo del outbox, alertas, panel de dead-letter.
- Acoplamiento al stack Wolverine: si en el futuro se reemplaza el mediator, el patrón debe migrarse manualmente.

### JWT del usuario en el envelope del outbox (mt-3 — 2026-05-19)

Tras la decisión del sub-track multi-tenancy mt-3 (D-MT3-2), el JWT del request HTTP entrante viaja al ERP cuando el listener Wolverine ya está procesando un mensaje fuera del scope HTTP original. El patrón:

1. **Captura en publish:** un middleware Wolverine en el endpoint HTTP enriquece el envelope con `Headers["X-Forwarded-Authorization"] = <Bearer del request>` antes de que el outbox lo persista.
2. **Lectura en listener:** el listener (`SincronizarDictamenVigenteListener`, `DescartarNovedadPreopErpListener`, etc.) extrae el header del envelope vía `EnvelopeBearerExtractor.ExtraerBearerForwarded(envelope)` y lo setea en `AmbientBearerTokenAccessor` antes de invocar al adapter HTTP.
3. **Propagación al ERP:** el `BearerTokenPropagationHandler` (DelegatingHandler del `MaquinariaErpClient`) consulta la cadena `IBearerTokenAccessor` (HTTP → Ambient → ServiceAccount) y setea el header `Authorization: Bearer {token}` en cada request al ERP.

**Trade-off de expiración del JWT durante retry:**

La política de reintentos (5s → 30s → 2m → 10m) totaliza hasta ~12 min entre el primer intento y el dead-letter. El JWT del envelope se persistió en el outbox al momento del publish original y **NO se refresca durante los reintentos**. Si expira (TTL típico del host PWA Sinco MYE < 1h, pero variable), el ERP responde 401 → cae en la rama 4xx → `MoveToErrorQueue()` permanente. Comportamiento esperado del 4xx para mt-3.

Fallback `ServiceAccountBearerTokenAccessor` (`MaquinariaErpOptions.JwtToken`): si el envelope NO trae header `X-Forwarded-Authorization` (publish listener-to-listener, mensaje legacy pre-mt-3, etc.), la chain cae al token de servicio. Esto preserva la integración funcional aunque el audit del ERP atribuya la acción a un service-account.

**Decisión 2026-05-19 — NO se introduce refresh automático del JWT en retry para mt-3.** Si emerge requerimiento operativo en el piloto, abrir slice (FU-62).

**PII en outbox:** aceptado. El outbox vive en la misma DB Postgres del módulo (red privada, mismo nivel de protección que streams del dominio que ya contienen GPS y motivos de descarte). El JWT es credencial efímera, no rotated secret.

### Referencias

- `CLAUDE.md` — regla dura de atomicidad de eventos.
- ADR-001 — REST sobre VPN (`00-investigacion-mercado.md §9.11`).
- ADR-002 — auth pipeline del host PWA (`00-investigacion-mercado.md §9.14`, cerrado en mt-1).
- ADR-003 — instancia específica para M-1 (§13 de este documento).
- ADR-005 — SignalR push para feedback al cliente (§14).
- ADR-009 — multi-tenancy Marten conjoined (`00-investigacion-mercado.md §9.17`, mt-3 cerrado).
- `06-contrato-apis-erp.md §1.8` — convención del contrato que apunta a este ADR.
- `slices/mt-3-jwt-propagation-erp/spec.md` — propagación del JWT al ERP (mt-3).
- Wolverine docs — outbox + retry policy.

---

## 17. ADR-007 — Aprobación manual de OT con capability gate (2026-04-29)

**Estado:** Aceptada.

### Contexto

§15.6 (versión previa) definía que la saga `CerrarInspeccionSaga` reaccionaba a `InspeccionFirmada_v1` y, si había ≥1 hallazgo `RequiereIntervencion`, hacía POST inmediato a MYE para crear la OT correctiva. La firma del técnico equivalía a la autorización de OT.

En la reunión de diseño del 2026-04-29 (Daniel + Jaime + Sergio + David), se decidió **separar firma de aprobación de OT**:

- El técnico que ejecuta la inspección no necesariamente tiene autoridad para comprometer una OT correctiva (que implica costo: repuestos, mano de obra, tiempo de equipo fuera de operación).
- En la operación real de los clientes ERP de Sinco, esa autorización suele recaer en jefes de campo, supervisores o contralores — perfiles distintos al técnico que levanta la inspección.
- La generación automática al firmar también dejaba al técnico sin opción de "esperar mientras coordino con el supervisor antes de comprometer la OT".

Adicionalmente: la decisión confirma que **toda la gestión vive dentro de la app** — no hay camino paralelo en la web del ERP MYE para aprobar OTs. La capability gate es el único mecanismo de autorización.

### Decisión

**La firma de inspección no genera OT automáticamente.** Para inspecciones con ≥1 hallazgo `RequiereIntervencion`, el flujo se divide en dos pasos:

1. **Firma** — el técnico firma. La inspección queda en estado `Firmada`. La proyección `BandejaInspeccionesPendientesOTView` (§15.12.5) la lista como `EstadoOT=EsperandoAprobacion`.
2. **Aprobación de OT** — un usuario con capability `generar-ot` ejecuta el comando `GenerarOT(InspeccionId)`. El handler valida I-F4 (§15.7) y emite `OTSolicitada_v1`. La saga `EjecutarOTSaga` reacciona y dispara el POST a MYE vía outbox (ADR-006). En éxito → `InspeccionCerrada_v1`. En fallo → `OTGeneracionFallida_v1` + estado `CierrePendienteOT`.

Para inspecciones sin hallazgos `RequiereIntervencion`, **no cambia nada**: la saga `CerrarInspeccionSaga` emite `InspeccionCerradaSinOT_v1` automáticamente al firmar (igual que en §15.6 histórico).

#### State machine actualizada

```
EnEjecucion
   │
   ├─ FirmarInspeccion ──┬─ sin RequiereIntervencion ─→ CerradaSinOT (terminal)
   │                     │
   │                     └─ con RequiereIntervencion ─→ Firmada
   │                                                       │
   │                                                       │ (estado derivado:
   │                                                       │  EsperandoAprobacionOT
   │                                                       │  mientras !OTSolicitada)
   │                                                       │
   │                                                       ↓
   │                                              GenerarOT (capability `generar-ot`)
   │                                                       │
   │                                                       ↓
   │                                                  OTSolicitada_v1
   │                                                       │
   │                                                       ↓ saga EjecutarOTSaga → POST MYE (outbox)
   │                                                       │
   │                                                       ├─ éxito → Cerrada (terminal)
   │                                                       └─ fallo → CierrePendienteOT
   │                                                                      │
   │                                                                      └─ retry GenerarOT → ...
   │
   └─ CancelarInspeccion ─→ Cancelada (terminal)
```

#### Eventos nuevos (Δ §15.4)

```csharp
public enum ResponsableCosto
{
    Proyecto,                       // el proyecto donde está el equipo asume el costo
    DepartamentoEquipos             // el área que administra los equipos como activo asume el costo
}

public sealed record OTSolicitada_v1(
    Guid InspeccionId,
    string SolicitadaPor,           // user id del aprobador con capability generar-ot
    ResponsableCosto Responsable,   // quien asume el costo (uno por OT, no por hallazgo) — confirmado 2026-04-30
    DateTime SolicitadaEn);
```

`InspeccionCerrada_v1`, `InspeccionCerradaSinOT_v1`, `OTGeneracionFallida_v1` mantienen su shape — solo cambia **quién/cuándo** los emite.

> **Sobre `ResponsableCosto` (decisión 2026-04-30):** el campo existe del lado MYE en el DTO de `POST /api/v1/mye/ot-correctivas`. El nombre exacto del campo en el DTO está **pendiente de confirmar con David** (ver `07-preguntas-destrabar-followups.md`). El enum es **cerrado** (solo dos valores; no admite `Garantia`, `OtroProyecto`, ni "responsable por hallazgo"). Decisión por OT, no por hallazgo: una inspección con varios hallazgos `RequiereIntervencion` consolida en una OT con un único responsable. **Followup #4 cerrado 2026-04-30:** la palabra del lenguaje del módulo es **`Proyecto`** (sinónimo de "obra" del ERP). El módulo internamente usa `Proyecto`; el adapter al ERP traduce ↔ `obra` cuando llama endpoints como `/catalogos/obras`. `Proyecto` aquí en `ResponsableCosto` es el centro de costo (= el proyecto donde el equipo está asignado).

#### Comando nuevo

```csharp
public sealed record GenerarOT(
    Guid InspeccionId,
    string SolicitadaPor,
    ResponsableCosto Responsable,                 // input del aprobador en pantalla de aprobación
    IReadOnlyCollection<string> Capabilities);    // del contexto del host PWA
```

Validaciones del handler (todas bloqueantes — ver I-F4):
- `Capabilities` contiene `generar-ot`.
- Aggregate en estado `Firmada`.
- ≥1 hallazgo no eliminado con `AccionRequerida = RequiereIntervencion`.
- `!OTSolicitada` (no se aceptan dos solicitudes sobre el mismo stream).
- `!OTRechazada` (no se acepta solicitar OT que ya fue rechazada — ver `RechazarGenerarOT` abajo).
- `Responsable` ∈ {`Proyecto`, `DepartamentoEquipos`} (validación de enum cerrado).

#### Comando + evento de rechazo de OT (decisión 2026-04-30, observación Sergio)

> **Origen:** observación de Sergio el 2026-04-30 — *"se puede cancelar la generación de la orden de trabajo derivada de dicha inspección"*. Sin este comando, una inspección firmada con `RequiereIntervencion` quedaba indefinidamente en `EsperandoAprobacionOT` si nadie aprobaba.

```csharp
public sealed record RechazarGenerarOT(
    Guid InspeccionId,
    string Motivo,                                // texto libre, min 10 chars
    string RechazadoPor,
    IReadOnlyCollection<string> Capabilities);

public sealed record GeneracionOTRechazada_v1(
    Guid InspeccionId,
    string Motivo,
    string RechazadoPor,
    DateTime RechazadaEn);
```

**Validaciones del handler (todas bloqueantes — ver I-F6):**
- `Capabilities` contiene `generar-ot` (misma capability que `GenerarOT` — quien puede aprobar puede rechazar).
- Aggregate en estado `Firmada`.
- ≥1 hallazgo no eliminado con `AccionRequerida = RequiereIntervencion` (sin intervención no aplica — la saga ya cerró sin OT automáticamente).
- `!OTSolicitada` (alcance limitado: una vez solicitada y enviada al ERP, cancelar requiere coordinación cross-team — confirmado 2026-04-30, fuera de alcance MVP).
- `!OTRechazada` (no se acepta doble rechazo).
- `Motivo` no vacío y >= 10 chars.

**Atomicidad — el handler emite DOS eventos en un único `SaveChangesAsync`:**

1. `GeneracionOTRechazada_v1` (audit del rechazo).
2. `InspeccionCerradaSinOT_v1` con discriminador `MotivoCierreSinOT = RechazadaPorAprobador` (estado terminal).

```csharp
// InspeccionCerradaSinOT_v1 extendido (decisión 2026-04-30)
public enum MotivoCierreSinOT
{
    AutomaticoSinIntervencion,    // saga al firmar (no había RequiereIntervencion)
    RechazadaPorAprobador         // RechazarGenerarOT (NUEVO 2026-04-30)
}

public sealed record InspeccionCerradaSinOT_v1(
    Guid InspeccionId,
    MotivoCierreSinOT Motivo,     // NUEVO campo discriminador
    DateTime CerradaEn);
```

**Apply puro:** `Apply(GeneracionOTRechazada_v1) → OTRechazada = true; MotivoRechazo = e.Motivo`. `Apply(InspeccionCerradaSinOT_v1) → Estado = CerradaSinOT`. Sin validaciones en `Apply`.

**Equipo se libera:** la proyección `InspeccionAbiertaPorEquipoView` (§15.12.6) elimina la fila al recibir `InspeccionCerradaSinOT_v1` (no requiere cambio — ya consume ese evento). Otro técnico puede iniciar nueva inspección sobre el equipo de inmediato.

#### Sagas

| Saga | Trigger | Acción |
|---|---|---|
| `CerrarInspeccionSaga` (existente, simplificada) | `InspeccionFirmada_v1` | Si **no** hay `RequiereIntervencion` → emite `InspeccionCerradaSinOT_v1`. Si **sí** hay → no-op (espera comando humano). En **ambos casos** dispara `SincronizarDictamenVigenteSaga`. |
| `EjecutarOTSaga` (nueva) | `OTSolicitada_v1` | POST `/api/v1/mye/ot-correctivas` vía outbox (ADR-006). En éxito → `InspeccionCerrada_v1`. En fallo permanente o agotado retry → `OTGeneracionFallida_v1`. |
| `AbrirSeguimientosSaga` (existente, sin cambio) | `InspeccionFirmada_v1` | Para hallazgos `RequiereSeguimiento` abre aggregates `SeguimientoHallazgo` (§15.8). Independiente del flujo OT. |
| `SincronizarDictamenVigenteSaga` (nueva, decisión 2026-04-30) | `InspeccionFirmada_v1` | PUT `/api/v1/equipos/{equipoId}/dictamen-vigente` vía outbox con el dictamen del stream. Independiente del flujo OT — corre en toda firma. Detalle abajo. |
| `GenerarPdfInspeccionSaga` (nueva, decisión 2026-04-30) | `InspeccionFirmada_v1` | Renderiza PDF localmente con QuestPDF (datos congelados del stream), sube a Azure Blob, emite `PdfInspeccionGenerado_v1` con `BlobUri` + `Hash`. Independiente del flujo OT — el PDF queda disponible aun si la inspección cierra `SinOT`. Detalle en sub-sección "Generación de PDF de inspección" abajo. |
| `EjecutarOTSaga` (existente — extendida 2026-04-30) | (sin cambio en trigger) | Tras éxito del `POST /mye/ot-correctivas` (ya tiene `OTCorrectivaIdSinco`), invoca **adicionalmente** `POST /api/v1/mye/ot-correctivas/{otCorrectivaIdSinco}/adjuntos` con el PDF (multipart). En éxito emite `PdfAdjuntadoAOT_v1`. Si el adjunto falla, emite `AdjuntoPdfFallido_v1` para queue manual — **NO bloquea ni revierte la OT** (la OT ya existe en MYE). Si el PDF aún no está generado al momento (race), reintenta con backoff. |

#### Aggregate — estado interno

```csharp
public sealed class InspeccionTecnica
{
    // ... campos existentes ...
    public bool OTSolicitada    { get; private set; }   // setea Apply(OTSolicitada_v1) → true
    public bool OTRechazada     { get; private set; }   // setea Apply(GeneracionOTRechazada_v1) → true
    public string? MotivoRechazoOT { get; private set; }
}

public void Apply(OTSolicitada_v1 e)              => OTSolicitada    = true;
public void Apply(GeneracionOTRechazada_v1 e)
{
    OTRechazada     = true;
    MotivoRechazoOT = e.Motivo;
}
```

`Apply` es puro (consistente con regla dura de CLAUDE.md). Las precondiciones I-F4 e I-F6 viven en los métodos de decisión `GenerarOT(...)` y `RechazarGenerarOT(...)`.

#### Capabilities

| Capability | Quién la otorga | Para qué |
|---|---|---|
| `ejecutar-inspeccion` | Host PWA (mapping de perfiles ERP → capabilities, paso 2.5 roadmap) | Iniciar, registrar hallazgos, firmar |
| `generar-ot` (NUEVA) | Host PWA | Ejecutar comando `GenerarOT` o `RechazarGenerarOT` (quien aprueba puede rechazar) |
| `auditar-inspecciones` | Host PWA | Ver `AuditoriaInspeccionesView`, acceder cross-inspección |
| `recibir-alertas-ot-fallida` | Host PWA | Notificación cuando integración MYE falla |
| `recibir-alertas-ot-rechazada` (NUEVA 2026-04-30) | Host PWA | Notificación cuando un aprobador rechaza generar OT — destinatarios típicos: técnico firmante, supervisor del área. La capability es independiente de `generar-ot`: quien rechaza no se notifica a sí mismo, pero sí a otros. |

`generar-ot` es **independiente** de `ejecutar-inspeccion`: un usuario puede tener una, ambas, o ninguna. Un técnico puede firmar pero no aprobar; un supervisor puede aprobar pero (típicamente) no ejecuta inspecciones él mismo. La matriz concreta de mapping ERP → capabilities la define el host PWA, no este módulo.

#### Integración con MYE: dictamen vigente del equipo (decisión 2026-04-30)

**Origen:** observación de Sergio (consultor producto) el 2026-04-30 — *"debe existir un servicio para actualizar este campo en el ERP"*.

**Decisión:** además del dictamen embebido en `POST /api/v1/mye/ot-correctivas` (paso 4.9 del roadmap, viaja con la OT), se introduce un endpoint dedicado en MYE para mantener el **dictamen vigente del equipo** independientemente del flujo OT.

| Aspecto | Decisión |
|---|---|
| Endpoint MYE (propuesto) | `PUT /api/v1/equipos/{equipoId}/dictamen-vigente` — nombre exacto pendiente de confirmar con David (ver doc 07) |
| Body | `{ "dictamen": "PuedeOperar" \| "ConRestriccion" \| "NoPuedeOperar", "inspeccionOrigenId": "guid", "firmadaEn": "iso-8601", "tecnicoFirmante": "username" }` |
| Cuándo se invoca | En **toda firma** de inspección (con OT y sin OT) — opción (ii) confirmada 2026-04-30. Redundante con el body de OT cuando hay intervención, pero unifica la lógica del adapter. |
| Quién lo invoca | `SincronizarDictamenVigenteSaga` (nueva, ver tabla de sagas arriba) reactiva sobre `InspeccionFirmada_v1`. Wolverine outbox + retry exponencial estándar (ADR-006). |
| Idempotencia | Idempotency-Key recomendada: `{InspeccionId}` (un dictamen vigente por inspección firmante). MYE debe aceptar replay del PUT con misma key + mismo body sin efecto colateral. |
| Resiliencia | Si falla persistentemente → `DictamenVigenteSyncFallida_v1` (nuevo evento candidato; **NO bloquea cierre de inspección** — el cierre de la inspección es independiente del estado de sync con MYE). El módulo retiene el último dictamen sincronizado en proyección lateral para reintento. |
| Alternativa rechazada | Bifurcar la lógica: solo invocar PUT cuando `CerradaSinOT`. Rechazada porque obliga a saber del lado del adapter el resultado de la saga de OT — peor cohesión. |

**Eventos potenciales nuevos** (a confirmar cuando se implemente el slice; no se persisten en el aggregate `InspeccionTecnica` salvo el evento de fallo):

```csharp
// En el outbox / saga side, no en el aggregate
public sealed record DictamenVigenteSyncFallida_v1(
    Guid InspeccionId,
    int EquipoId,
    DictamenOperacion Dictamen,
    string DetalleError,
    DateTime FallidaEn);
```

**Open question pendiente con David:** ¿el campo `DictamenVigente` ya existe en la entidad `Equipo` del ERP, o es a construir? El payload propuesto y el contrato de idempotencia son tentativos. Ver `07-preguntas-destrabar-followups.md` (pregunta 2 a David).

#### Generación de PDF de inspección y adjunto a OT (decisión 2026-04-30)

**Origen:** observación de Sergio el 2026-04-30 — *"cuando se genere una OT, debe llegar como adjunto a esta, el PDF de la inspección"*.

**Decisión 1 — Cuándo se genera:** **al firmar** la inspección, no al generar OT. Razones:
- Datos congelados (post-firma el aggregate es inmutable por I-F1, ningún hallazgo cambia).
- Disponible aun cuando la inspección cierre `SinOT` (auditoría, reportes, prueba para el cliente).
- Si la generación de OT se rechaza (I-F6) o el PDF se necesita antes de aprobar OT, ya está listo.

**Decisión 2 — Quién renderiza:** el módulo, con **QuestPDF** (librería .NET de uso común, soporta layouts complejos y escala). Alternativa rechazada: pedir al ERP que genere el PDF — agrega dependencia operativa cross-team y duplica datos.

**Decisión 3 — Cómo viaja al ERP:** **endpoint separado** `POST /api/v1/mye/ot-correctivas/{otCorrectivaIdSinco}/adjuntos` con `multipart/form-data` tras la creación de la OT. Detalle del contrato en `06-contrato-apis-erp.md` (M-1b). Alternativas rechazadas:
- Base64 en el body de M-1: pesado, requiere extender contrato existente.
- URL SAS del blob: requiere que MYE tenga acceso a Azure Blob (cross-network del ERP on-prem hacia Azure) — fricción operativa.

**Eventos nuevos (Δ §15.4):**

```csharp
public sealed record PdfInspeccionGenerado_v1(
    Guid InspeccionId,
    Uri BlobUri,                    // ruta interna del blob en Azure (no SAS público)
    long TamanoBytes,
    string Sha256,                  // integridad para reconciliación
    DateTime GeneradoEn);

public sealed record PdfAdjuntadoAOT_v1(
    Guid InspeccionId,
    string OTCorrectivaIdSinco,
    string AdjuntoIdSinco,          // id que devuelve MYE al recibir el adjunto
    DateTime AdjuntadoEn);

// En outbox, no en aggregate
public sealed record AdjuntoPdfFallido_v1(
    Guid InspeccionId,
    string OTCorrectivaIdSinco,
    string DetalleError,
    DateTime FallidaEn);
```

**Layout del PDF (definido por el módulo, sin template Sinco existente):**

| Sección | Contenido |
|---|---|
| Header | Logo Sinco MYE, número correlativo de inspección, fecha de firma, código + descripción del equipo, proyecto |
| Inicio | Técnico iniciador, fecha/hora de inicio, ubicación GPS de inicio |
| Hallazgos | Por cada hallazgo no eliminado: parte, actividad, novedad técnica, `AccionRequerida`, tipo/causa de falla (si `RequiereIntervencion`), repuestos estimados (lista con cantidades), miniaturas de adjuntos (1ra página de PDFs y thumbnails de imágenes) |
| Diagnóstico final | Texto del diagnóstico |
| Dictamen | `PuedeOperar` / `ConRestriccion` / `NoPuedeOperar` con resaltado visual |
| Firma | Imagen escaneada de la firma manuscrita; técnico firmante; fecha/hora de firma; ubicación GPS de firma |
| Contribuyentes | Lista de `TecnicosContribuyentes` (todos los que aportaron eventos al stream) |
| Footer | Numeración de páginas, hash SHA-256 del documento, enlace canónico a `GET /inspecciones/{id}` |

**Almacenamiento en blob:**
- Container: `inspecciones-pdf` (separado de `adjuntos-hallazgos`).
- Path: `{InspeccionId}/inspeccion-{numeroCorrelativo}.pdf`.
- Lifecycle: retención de 7 años (cumple normativa típica de archivo de inspecciones).
- Auth: el módulo accede con managed identity. Frontend obtiene URL SAS de descarga vía endpoint del módulo (no acceso directo al blob).

**Resiliencia:**
- Generación PDF puede fallar (datos corruptos, librería) → emite evento `PdfInspeccionGeneracionFallida_v1` (candidato, a confirmar) y queue manual. **NO bloquea cierre ni OT** — el PDF se regenera al resolver.
- Adjunto a OT puede fallar (5xx, 4xx) → `AdjuntoPdfFallido_v1`, queue manual. La OT existe igual en MYE; el adjunto se reintenta o se sube manual desde MYE web.
- Race condition (PDF aún generándose cuando llega `OTSolicitada_v1`): `EjecutarOTSaga` consulta el stream antes de invocar el endpoint de adjuntos; si `PdfInspeccionGenerado_v1` aún no existe, espera con backoff (max 5 min) y luego falla con `AdjuntoPdfFallido_v1`.

**Open questions pendientes con David** (ver `07-preguntas-destrabar-followups.md`):
1. ¿MYE ya tiene endpoint de adjuntos para OT correctivas, o es a construir?
2. ¿Tamaño máximo del adjunto que MYE acepta? Sugerencia: ≥ 10 MB (el PDF estándar pesa 1-5 MB con miniaturas de fotos).
3. ¿Tipos MIME admitidos? El módulo solo enviará `application/pdf` aquí — confirmar.

### Implicaciones para read models

- **`DetalleInspeccionView` (§15.12.1)** — debe exponer:
  - `EstadoOT` derivado: `NoAplica` (sin RequiereIntervencion), `EsperandoAprobacion`, `EnProceso` (post-OTSolicitada, pre-confirmación MYE), `Generada` (post-Cerrada con OT), `Fallida` (post-OTGeneracionFallida).
  - Capability del usuario consultante (proveniente del host PWA) para que el frontend muestre/oculte botón "Generar OT".
- **`BandejaInspeccionesPendientesOTView` (§15.12.5, NUEVA)** — cola de trabajo del aprobador. Detalle en §15.12.5.

### Implicaciones para roadmap

| Paso | Cambio |
|---|---|
| 3.27 | `CerrarInspeccionSaga` — dividir en dos sagas (`CerrarInspeccionSaga` simplificada + `EjecutarOTSaga` nueva). |
| 3.27c (NUEVO 2026-04-30) | Adapter MYE: `PUT /equipos/{id}/dictamen-vigente`. Invocado por `SincronizarDictamenVigenteSaga` (nueva). Tests obligatorios: replay con misma key (idempotencia), 4xx no retry, 5xx con backoff. |
| 3.42 | `POST /inspecciones/{id}/firmar` — semántica reducida: ya no implica POST a MYE. |
| 3.42b (NUEVO) | Comando + endpoint `POST /inspecciones/{id}/generar-ot`. Capability gate `generar-ot`. |
| 3.45 | `GET /inspecciones/{id}` — exponer `EstadoOT` y capabilities del usuario consultante. |
| 3.45b (NUEVO) | Bandeja `GET /inspecciones/pendientes-ot?...` sirviendo §15.12.5. |
| 2.5 | Mapping de perfiles ERP → capabilities — agregar `generar-ot` a la matriz. |
| 4.9b (NUEVO 2026-04-30) | Endpoint MYE `PUT /api/v1/equipos/{equipoId}/dictamen-vigente` — pendiente coordinación cross-team. |

### Implicaciones para UI (frontend)

- **Pantalla 7** (post-firma): ya no muestra "Generando OT en MYE..." automáticamente. Si el firmante tiene capability `generar-ot`, ve botón "Generar OT" disponible. Si no, ve mensaje "Inspección firmada. Esperando aprobación de OT por usuario autorizado."
- **Bandeja del aprobador** (nueva pantalla, fase 5): lista de inspecciones pendientes con conteos, dictamen, antigüedad. Click → detalle → botón "Generar OT".
- **SignalR push (ADR-005)**: el aprobador ve en tiempo real el resultado del POST MYE (`OTGenerada` o `OTGeneracionFallida`). El firmante también recibe el push si está suscrito al stream.

### Trade-offs

**Pros:**
- Modelo de autorización alineado con la operación real (jefe de campo aprueba, técnico ejecuta).
- Auditoría explícita: `OTSolicitada_v1` registra quién/cuándo autorizó la OT, separado del firmante.
- Permite "firmar y coordinar antes de comprometer OT" (caso de uso real de campo).
- Mantiene la regla "una OT por inspección con intervención" — la diferencia es el momento de disparo, no la lógica.
- No introduce caminos paralelos en MYE web — toda la gestión vive en la app.

**Cons:**
- Estado nuevo (derivado `EsperandoAprobacionOT`) — requiere proyección dedicada.
- Latencia operacional: una inspección puede quedar firmada sin OT por horas/días si nadie con capability la aprueba. Sin SLA explícito en MVP — followup si emerge.
- Nueva capability a mapear en el host PWA. Coordinación con paso 2.5.
- Wolverine debe orquestar dos sagas distintas con triggers diferentes.

### Referencias

- §15.4 — catálogo de eventos (incluye `OTSolicitada_v1`).
- §15.6 — flujo previo (superseded), conservado como histórico.
- §15.7 — invariantes I-F4 e I-F5 introducidas por este ADR.
- §15.12.1 — `DetalleInspeccionView` con `EstadoOT`.
- §15.12.5 — `BandejaInspeccionesPendientesOTView` (cola del aprobador).
- ADR-006 (§16) — outbox para el POST a MYE desde `EjecutarOTSaga`.
- `roadmap.md` — pasos 3.24, 3.24b, 3.25, 3.27, 3.42, 3.42b, 3.45, 3.45b, 2.5.
- Notas de la reunión de diseño 2026-04-29 (`Inspecciones/docs/llamada diseño.mp4` — origen de la decisión).
