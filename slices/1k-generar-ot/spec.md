# Slice 1k — GenerarOT

**Autor:** domain-modeler
**Fecha:** 2026-05-08
**Estado:** draft
**Agregado afectado:** `Inspeccion` (aggregate unificado — aplica a `TipoInspeccion.Tecnica` y `TipoInspeccion.Monitoreo`).
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.4` — catálogo final de 24 eventos; evento #20 `OTSolicitada_v1`; shape canónico de `ResponsableCosto`.
- `01-modelo-dominio.md §15.7` — invariantes I-F4 (precondiciones de `GenerarOT`), I-F5 (estado derivado `EsperandoAprobacionOT`), I-F6 (si hay rechazo previo no se puede solicitar — fuente de la precondición PRE-6 de este slice).
- `01-modelo-dominio.md §17` (ADR-007) — fuente de verdad del flujo de aprobación manual de OT. Define command record `GenerarOT`, evento `OTSolicitada_v1`, state machine, capabilities, sagas y aggregate state interno (`OTSolicitada`, `OTRechazada`).
- `01-modelo-dominio.md §15.6 (histórico)` — matriz dictamen × hallazgos × cierre; regla operativa: OT solo con dictamen `ConRestriccion` o `NoPuedeOperar` + ≥1 `RequiereIntervencion`.
- `01-modelo-dominio.md §15.12.5` — `BandejaInspeccionesPendientesOTView`; `EsperandoAprobacionOT` es estado **derivado** (no persistido), computado por proyección.
- ADR-007 (`§17`) — command `GenerarOT` emite un único `OTSolicitada_v1`; la saga `EjecutarOTSaga` (slice 3.24b) reacciona a ese evento para invocar M-1. Este slice **no** invoca M-1 directamente.
- ADR-006 (`§16`) — todo POST al ERP va por outbox Wolverine; el handler de este slice no hace POST a ningún ERP.
- ADR-005 (`§14`) — push SignalR `OTGenerada` es responsabilidad de la saga `EjecutarOTSaga`, no de este slice.
- ADR-008 (`00-investigacion-mercado.md §9.16`) — `X-Client-Command-Id` mapeado a `MessageId` Wolverine; idempotencia end-to-end.
- `slices/1g-firmar-inspeccion/spec.md` — patrón de referencia para `ClaimsTecnico`, idempotencia, estructura de escenarios.
- `roadmap.md §3.42` — entrada `POST /inspecciones/{id}/generar-ot` en roadmap Fase 3.

---

## 1. Intención

Un usuario con capability `generar-ot` (típicamente jefe de campo, supervisor o contralor — nunca el técnico de campo que firmó) necesita autorizar explícitamente la creación de una Orden de Trabajo correctiva para una inspección ya firmada que tiene al menos un hallazgo con `AccionRequerida = RequiereIntervencion`. El comando emite `OTSolicitada_v1` y encola el POST a MYE vía la saga `EjecutarOTSaga` (slice 3.24b). El firmante de la inspección **no** puede aprobar su propia OT si no tiene la capability correspondiente — la separación de firma y aprobación es intencional (ADR-007 §17).

---

## 2. Comando

```csharp
public sealed record GenerarOT(
    Guid         InspeccionId,
    string       SolicitadaPor,                          // userId del aprobador del host PWA — opaco para el dominio
    ResponsableCosto Responsable,                        // Proyecto | DepartamentoEquipos — enum cerrado
    string?      Observaciones,                          // texto libre opcional
    string?      ComentarioJefe,                         // comentario adicional del aprobador, opcional
    IReadOnlyCollection<string> Capabilities,            // del contexto del host PWA; debe contener "generar-ot"
    PrioridadOT  Prioridad                               // Baja | Normal | Alta | Urgente
) : ICommand;
```

**Value objects y enums referenciados:**

```csharp
public enum ResponsableCosto
{
    Proyecto,               // el proyecto donde está el equipo asume el costo
    DepartamentoEquipos     // el área que administra los equipos como activo asume el costo
}

public enum PrioridadOT
{
    Baja,
    Normal,
    Alta,
    Urgente
}
```

> **Nota sobre campos opcionales del dominio vs §17:** el record canónico en ADR-007 §17 tiene solo `{ InspeccionId, SolicitadaPor, Responsable, Capabilities }`. Este spec añade `Prioridad`, `Observaciones` y `ComentarioJefe` como campos requeridos/opcionales del comando, por ser necesarios para el payload que la saga `EjecutarOTSaga` necesita entregar a M-1 (ver `06-contrato-apis-erp.md` — el POST a MYE incluye prioridad). Si emergen como preguntas abiertas, ver §12.

> **Nota sobre `SolicitadaPor`:** el handler extrae el userId del JWT del host PWA y lo pasa como `SolicitadaPor`; el dominio lo recibe como string opaco. No es el mismo que el técnico que firmó (aunque un usuario podría tener ambas capabilities en configuraciones locales del host).

> **Nota sobre `Prioridad`:** el §17 modelo muestra que la prioridad se deriva automáticamente del dictamen (NoPuedeOperar → Urgente, ConRestriccion → Alta). En este spec se modela como campo explícito del comando para que el aprobador pueda sobreescribir la sugerencia de la UI. Ver §12 P-1 sobre esta ambigüedad.

**Claims del aprobador** (parámetros del handler, no del record de comando):

```csharp
// Integrado directamente como Capabilities: IReadOnlyCollection<string> en el comando.
// El handler verifica que Capabilities.Contains("generar-ot") antes de invocar el
// método de decisión del aggregate.
```

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/generar-ot
public sealed record GenerarOTRequest(
    string          Responsable,     // "Proyecto" | "DepartamentoEquipos"
    string          Prioridad,       // "Baja" | "Normal" | "Alta" | "Urgente"
    string?         Observaciones,
    string?         ComentarioJefe);

public sealed record GenerarOTResult(
    Guid            InspeccionId,
    DateTimeOffset  SolicitadaEn,
    string          SolicitadaPor,
    string          Responsable,
    string          Prioridad);
```

---

## 3. Evento(s) emitido(s)

Este slice emite **exactamente un evento** en todos los casos de éxito, en un único `SaveChangesAsync`.

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `OTSolicitada_v1` | Ver shape a continuación | Siempre que las precondiciones I-F4 se cumplan |

### 3.1 `OTSolicitada_v1` — shape canónico

El evento está definido en `01-modelo-dominio.md §17`. Se aplica la misma corrección de tipo que en slices anteriores: `DateTimeOffset` en lugar de `DateTime` (convención del módulo — CLAUDE.md).

```csharp
public sealed record OTSolicitada_v1(
    Guid            InspeccionId,
    string          SolicitadaPor,       // userId opaco del aprobador
    ResponsableCosto Responsable,
    PrioridadOT     Prioridad,           // campo añadido respecto al mínimo del §17 — ver §12 P-1
    string?         Observaciones,       // texto libre opcional — para la saga EjecutarOTSaga
    string?         ComentarioJefe,      // texto libre opcional — para la saga EjecutarOTSaga
    DateTimeOffset  SolicitadaEn);       // DateTimeOffset — TimeProvider.GetUtcNow() en el handler
```

> **Campo `DateTimeOffset` vs `DateTime`:** el shape en §17 usa `DateTime SolicitadaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset`. Se aplica la misma corrección que en slices anteriores. Se propone corrección al modelo §17 en el PR de este slice.

> **Campos `Prioridad`, `Observaciones`, `ComentarioJefe`:** no aparecen en el shape mínimo del §17, pero son necesarios para que la saga `EjecutarOTSaga` construya el payload de M-1 sin hacer una segunda consulta al stream. Se incluyen en el evento para captura única at-command-time. Ver §12 P-1 si hay objeción.

### 3.2 Impacto en estado interno del aggregate

El `Apply(OTSolicitada_v1)` es una mutación pura:

```csharp
// Apply puro — sin validaciones:
// OTSolicitada = true;
// SolicitadaEn = e.SolicitadaEn;
// SolicitadaPor = e.SolicitadaPor;
```

La precondición `!OTSolicitada` vive en el método de decisión, no en `Apply`.

---

## 4. Precondiciones

Las precondiciones se clasifican por la capa donde viven. Los `Apply` son puros — nunca re-validan.

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability `generar-ot`):** `Capabilities.Contains("generar-ot")`. Sin ella, el endpoint devuelve `403 Forbidden` sin invocar el aggregate. Excepción: `CapabilityRequeridaException` (o middleware 403 directo). `generar-ot` es **independiente** de `ejecutar-inspeccion` — quien firmó no tiene automáticamente esta capability.

### Capa handler (antes de invocar el método de decisión)

- **PRE-2 (inspección existe):** el handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).

### Método de decisión del aggregate (`Inspeccion.SolicitarOT`) — invariantes I-F4

- **PRE-3 (estado Firmada — I-F4.a):** `Estado == EstadoInspeccion.Firmada`. Si el estado es `EnEjecucion`, `CierradaSinOT`, `Cerrada`, `CierrePendienteOT` o `Cancelada` → `InspeccionNoFirmadaException` (`422 Unprocessable Entity`). Nota: en `EnEjecucion` la inspección aún no tiene firma — no se puede generar OT.
- **PRE-4 (≥1 hallazgo activo con RequiereIntervencion — I-F4.b):** `Hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == AccionRequerida.RequiereIntervencion)`. Si ningún hallazgo cumple → `SinHallazgosConIntervencionException` (`422 Unprocessable Entity`). Esta condición cierra automáticamente el caso `PuedeOperar` sin intervención (por V-F8 ya está garantizado al firmar, pero la defensa en I-F4 es explícita).
- **PRE-5 (OT no solicitada previamente — I-F4.c):** `!OTSolicitada`. Si ya existe un `OTSolicitada_v1` en el stream → `OTYaSolicitadaException` (`409 Conflict`). No se aceptan dos solicitudes sobre el mismo stream.
- **PRE-6 (OT no rechazada previamente — I-F4.d):** `!OTRechazada`. Si ya existe un `GeneracionOTRechazada_v1` en el stream → `OTRechazadaException` (`409 Conflict`). Una vez rechazada no se puede re-solicitar en el MVP (cross-ref I-F6).
- **PRE-7 (dictamen no es PuedeOperar — I-F4.e, defensa):** `Dictamen != DictamenOperacion.PuedeOperar`. Defensa explícita contra inconsistencias previas al deploy de V-F8. Si el dictamen es `PuedeOperar` → `DictamenNoPermiteOTException` (`422 Unprocessable Entity`).

> **Capa de validación:** PRE-1 en capa HTTP; PRE-2 en el handler; PRE-3 a PRE-7 en el método de decisión `Inspeccion.SolicitarOT`. Ningún `Apply` re-valida. Todas las condiciones de I-F4 (§15.7) están cubiertas.

---

## 5. Invariantes tocadas

- **I-F4 (GenerarOT — precondiciones):** cubierta íntegramente por PRE-3 (estado Firmada), PRE-4 (≥1 RequiereIntervencion activo), PRE-5 (!OTSolicitada), PRE-6 (!OTRechazada), PRE-7 (dictamen no PuedeOperar). Todas las condiciones de I-F4 mapean a precondiciones de método de decisión.
- **I-F5 (estado derivado EsperandoAprobacionOT):** no es una precondición sino la **descripción del estado** previo al comando. Este slice es la transición que saca al aggregate del estado derivado `EsperandoAprobacionOT`. Tras emitir `OTSolicitada_v1`, la proyección `BandejaInspeccionesPendientesOTView` cambia el registro a `EstadoOT=EnProceso`.
- **I-F1 (inmutabilidad post-firma):** el aggregate está en estado `Firmada`; los hallazgos son inmutables. PRE-3 confirma el estado Firmada. Las validaciones de PRE-4 se hacen sobre el estado congelado del stream.
- **I-F6 (no solicitar tras rechazo):** cubierta por PRE-6. La invariante I-F6 describe precondiciones del comando `RechazarGenerarOT` (slice futuro); la simetría que le aplica a este slice es "no se puede solicitar OT que ya fue rechazada".

### Invariantes existentes no tocadas

- I-H*, I-I*, V-F*, I-F2, I-F3: no aplican directamente — el aggregate ya está firmado y los hallazgos son inmutables.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — OT solicitada con dictamen NoPuedeOperar y hallazgo RequiereIntervencion

**Given**
- Stream con eventos:
  - `InspeccionIniciada_v1` (EquipoId=42, TecnicoId="carlos.ruiz", Tipo=Tecnica)
  - `HallazgoRegistrado_v1` (HallazgoId=`h1`, AccionRequerida=`RequiereIntervencion`, Eliminado=false, TipoFallaId=1, CausaFallaId=2)
  - `AdjuntoSubido_v1` (AdjuntoId=`adj1`, HallazgoId=`h1`)
  - `DiagnosticoEmitido_v1` (DiagnosticoFinal="Falla estructural en brazo hidráulico")
  - `DictamenEstablecido_v1` (Dictamen=NoPuedeOperar)
  - `InspeccionFirmada_v1` (FirmadoPor="carlos.ruiz", Estado=Firmada)
- `aggregate.OTSolicitada == false`, `aggregate.OTRechazada == false`.
- `TimeProvider` retorna `2026-05-08T14:00:00Z`.

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Urgente, Observaciones="Equipo fuera de operación — prioridad máxima", ComentarioJefe=null, Capabilities=["generar-ot"])`.

**Then**
- Se emite exactamente **un** evento: `OTSolicitada_v1(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Urgente, Observaciones="Equipo fuera de operación — prioridad máxima", ComentarioJefe=null, SolicitadaEn=2026-05-08T14:00:00Z)`.
- `aggregate.OTSolicitada == true`.
- `aggregate.Estado` sigue siendo `Firmada` (el estado no cambia a `Cerrada` en este slice — eso ocurre cuando la saga `EjecutarOTSaga` recibe confirmación de MYE).
- Handler retorna `GenerarOTResult(InspeccionId=X, SolicitadaEn=2026-05-08T14:00:00Z, SolicitadaPor="jefe.campo.01", Responsable="Proyecto", Prioridad="Urgente")`.
- Capa API devuelve `202 Accepted` con header `Location: /api/v1/inspecciones/{X}`.

### 6.2 Happy path — OT solicitada con dictamen ConRestriccion y campo ComentarioJefe poblado

**Given**
- Mismo setup base que 6.1 excepto:
  - `DictamenEstablecido_v1` (Dictamen=ConRestriccion)
  - `InspeccionFirmada_v1` (Estado=Firmada)

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="supervisor.01", Responsable=DepartamentoEquipos, Prioridad=Alta, Observaciones=null, ComentarioJefe="Coordinar con David antes de iniciar", Capabilities=["generar-ot"])`.

**Then**
- Se emite `OTSolicitada_v1(InspeccionId=X, SolicitadaPor="supervisor.01", Responsable=DepartamentoEquipos, Prioridad=Alta, Observaciones=null, ComentarioJefe="Coordinar con David antes de iniciar", SolicitadaEn=...)`.
- `aggregate.OTSolicitada == true`.
- Capa API devuelve `202 Accepted`.

### 6.3 Violación PRE-1 — capability `generar-ot` ausente (403)

**Given**
- Aggregate en estado `Firmada` con ≥1 hallazgo `RequiereIntervencion`.
- Claims del usuario: `Capabilities=["ejecutar-inspeccion"]` (sin `generar-ot`).

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="carlos.ruiz", Responsable=Proyecto, Prioridad=Normal, Capabilities=["ejecutar-inspeccion"])`.

**Then**
- Middleware de autorización lanza excepción 403 antes de llegar al handler.
- Sin evento. `aggregate.OTSolicitada` permanece `false`.
- Capa API devuelve `403 Forbidden` con `{ "codigoError": "PRE-1", "mensaje": "Capability 'generar-ot' requerida para este comando." }`.

### 6.4 Violación PRE-3 / I-F4.a — inspección no firmada (EnEjecucion) (422)

**Given**
- Stream con `InspeccionIniciada_v1` (Estado=EnEjecucion). Inspección sin firmar.
- Claims del usuario: `Capabilities=["generar-ot"]`.

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `InspeccionNoFirmadaException("La inspección X está en estado 'EnEjecucion'. GenerarOT solo aplica a inspecciones en estado 'Firmada'.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F4-ESTADO" }`.

### 6.5 Violación PRE-4 / I-F4.b — sin hallazgos con RequiereIntervencion (422)

**Given**
- Stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereSeguimiento`, Eliminado=false) + `DiagnosticoEmitido_v1` + `DictamenEstablecido_v1` (ConRestriccion) + `InspeccionFirmada_v1` (Estado=Firmada).
- `aggregate.Hallazgos`: solo hallazgos con `RequiereSeguimiento` — ninguno con `RequiereIntervencion`.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `SinHallazgosConIntervencionException("La inspección X no tiene hallazgos activos con AccionRequerida=RequiereIntervencion. GenerarOT requiere al menos uno.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F4-SIN-INTERVENCION" }`.

### 6.6 Violación PRE-5 / I-F4.c — OT ya solicitada previamente (409)

**Given**
- Stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, RequiereIntervencion) + firma completa + `OTSolicitada_v1` previo (aggregate.OTSolicitada == true).

**When**
- Segundo comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.02", Responsable=Proyecto, Prioridad=Alta, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `OTYaSolicitadaException("La inspección X ya tiene una OT solicitada. No se aceptan dos solicitudes de OT sobre el mismo stream.")`.
- Sin evento. `aggregate.OTSolicitada` permanece `true`.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I-F4-OT-DUPLICADA" }`.

### 6.7 Violación PRE-6 / I-F4.d — OT rechazada previamente (409)

**Given**
- Stream con firma completa + `GeneracionOTRechazada_v1` previo (aggregate.OTRechazada == true).

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `OTRechazadaException("La inspección X ya tiene la generación de OT rechazada. No se puede solicitar OT una vez rechazada.")`.
- Sin evento.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I-F4-OT-RECHAZADA" }`.

### 6.8 Violación PRE-7 / I-F4.e — dictamen PuedeOperar (422)

**Given**
- Stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=NoRequiereIntervencion) + `DiagnosticoEmitido_v1` + `DictamenEstablecido_v1` (Dictamen=PuedeOperar) + `InspeccionFirmada_v1` (Estado=Firmada).
- (Caso de defensa: V-F8 debería haber bloqueado esto al firmar; esta prueba verifica la defensa de segunda línea en I-F4.)

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `DictamenNoPermiteOTException("El dictamen 'PuedeOperar' no permite generar OT. Solo ConRestriccion o NoPuedeOperar son válidos.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F4-DICTAMEN" }`.

### 6.9 Idempotencia — replay con mismo `X-Client-Command-Id`

**Given**
- Comando `GenerarOT` con `MessageId=Y` ya ejecutado exitosamente. Wolverine envelope storage tiene respuesta original (`OTSolicitada_v1` emitido, `aggregate.OTSolicitada == true`).

**When**
- Cliente reenvía mismo `MessageId=Y` tras timeout de red (mismo header `X-Client-Command-Id`).

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- `aggregate.OTSolicitada` permanece `true` con un solo evento `OTSolicitada_v1` en el stream.
- Capa API devuelve `202 Accepted` con body original.

### 6.10 PRE-2 — InspeccionId no existe (404)

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando `GenerarOT(InspeccionId=Z, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Capa API devuelve `404 Not Found`.

### 6.11 Caso borde — hallazgo RequiereIntervencion eliminado no cuenta para PRE-4

**Given**
- Stream con firma completa donde el único hallazgo con `RequiereIntervencion` fue eliminado: `HallazgoEliminado_v1` (h1, Eliminado=true). No quedan hallazgos activos con `RequiereIntervencion`.

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `SinHallazgosConIntervencionException` (el hallazgo eliminado no cuenta — misma lógica que PRE-4 en 6.5).
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F4-SIN-INTERVENCION" }`.

### 6.12 Violación PRE-3 — inspección ya cerrada (409)

**Given**
- Stream con `InspeccionCerradaSinOT_v1` (Estado=CerradaSinOT — estado terminal).

**When**
- Comando `GenerarOT(InspeccionId=X, SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Normal, Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `InspeccionNoFirmadaException("La inspección X está en estado 'CerradaSinOT'. GenerarOT solo aplica a inspecciones en estado 'Firmada'.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F4-ESTADO" }`.

### 6.13 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos).
- Lista de eventos en orden causal para reproducir el happy path 6.1:
  1. `InspeccionIniciada_v1(EquipoId=42, TecnicoId="carlos.ruiz", Tipo=Tecnica, Estado=EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=h1, AccionRequerida=RequiereIntervencion, Eliminado=false, TipoFallaId=1, CausaFallaId=2)`
  3. `AdjuntoSubido_v1(AdjuntoId=adj1, HallazgoId=h1)`
  4. `DiagnosticoEmitido_v1(DiagnosticoFinal="Falla estructural")`
  5. `DictamenEstablecido_v1(Dictamen=NoPuedeOperar)`
  6. `InspeccionFirmada_v1(FirmadoPor="carlos.ruiz", Estado=Firmada)`
  7. `OTSolicitada_v1(SolicitadaPor="jefe.campo.01", Responsable=Proyecto, Prioridad=Urgente, SolicitadaEn=2026-05-08T14:00:00Z)`

**When**
- Se reproyectan los siete eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- Estado resultante:
  - `Estado == EstadoInspeccion.Firmada` (estado no cambia al emitir OTSolicitada — cambia cuando la saga confirma éxito de M-1).
  - `Dictamen == DictamenOperacion.NoPuedeOperar`.
  - `OTSolicitada == true`.
  - `OTRechazada == false`.
  - `Hallazgos.Count == 1` (h1 activo, RequiereIntervencion).
  - `SolicitadaEn == 2026-05-08T14:00:00Z`.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al obtenido tras ejecutar el comando en 6.1.

> **Justificación del rebuild:** garantiza que `Apply(OTSolicitada_v1)` es puro (solo actualiza `OTSolicitada = true`), que no hay validaciones intrusas en `Apply`, y que el evento es auto-contenido y reproducible desde el stream sin efectos colaterales.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente PWA genera `clientCommandId: UUIDv7` cuando el aprobador confirma la solicitud de OT. Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler (escenario 6.9).

**Idempotencia natural por PRE-5 (I-F4.c):**

Si el aprobador reenvía con un `clientCommandId` distinto (nuevo retry humano) sobre una inspección que ya tiene `OTSolicitada_v1`, el aggregate lanza `OTYaSolicitadaException` (`409 Conflict`). El `409` es intencional: señala que la OT ya fue solicitada, no un error de red. El cliente no debe reintentar automáticamente en `409`.

**Sin POST a Sinco en este slice:**

Este comando no cruza al ERP. El POST a MYE (M-1) lo realiza la saga `EjecutarOTSaga` (slice 3.24b) al reaccionar a `OTSolicitada_v1` via outbox Wolverine (ADR-006). El handler de este slice emite el evento y retorna inmediatamente — sin llamadas HTTP síncronas al ERP.

**Idempotency-Key para Sinco:**

La saga `EjecutarOTSaga` usará `Idempotency-Key=InspeccionId` al invocar M-1 (ADR-003 + ADR-006) — responsabilidad de ese slice, no de este.

---

## 8. Impacto en proyecciones / read models

### 8.1 `BandejaInspeccionesPendientesOTView` (§15.12.5) — principal impactada

Esta proyección consume `OTSolicitada_v1` para actualizar el `EstadoOT` de la fila:

- `OTSolicitada_v1` → `EstadoOT = EnProceso` (el aprobador actúa; la saga ya está en camino).

La fila **no desaparece** de la bandeja al emitir `OTSolicitada_v1` — permanece con `EstadoOT=EnProceso` para que el aprobador vea que su solicitud está en camino. Sale cuando llegue `InspeccionCerrada_v1` (éxito de M-1) u `OTGeneracionFallida_v1` (saga en `CierrePendienteOT`).

**Esta proyección es responsabilidad de un slice separado (roadmap 3.25 / 3.45b).** El agente `infra-wire` de este slice debe dejar un comentario `// TODO: actualizar BandejaInspeccionesPendientesOTView con OTSolicitada_v1 en slice de proyecciones`.

### 8.2 `BandejaTecnicoView` (§15.12.3)

La proyección de bandeja del técnico no reacciona a `OTSolicitada_v1` directamente — el estado relevante para el técnico es `InspeccionCerrada_v1` (slash la OT fue creada exitosamente en MYE). Sin cambio en este slice.

### 8.3 `InspeccionAbiertaPorEquipoView` (§15.12.6)

No reacciona a `OTSolicitada_v1`. El equipo solo queda libre cuando la inspección se cierra (terminal). Sin cambio.

---

## 9. Impacto en endpoints HTTP

### Endpoint principal

| Campo | Valor |
|---|---|
| Método + ruta | `POST /api/v1/inspecciones/{id}/generar-ot` |
| Path param | `{id}` = `InspeccionId` (Guid) |
| Content-Type | `application/json` |
| Authorization | JWT del host PWA; el middleware extrae `userId` y capabilities del token |

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization: Bearer <JWT>` heredado del host PWA (ADR-002 tentativo).

**DTO de request (body JSON):**

```json
{
  "responsable": "Proyecto",
  "prioridad": "Urgente",
  "observaciones": "Equipo fuera de operación — prioridad máxima",
  "comentarioJefe": null
}
```

**DTO de response (202 Accepted, body JSON):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "solicitadaEn": "2026-05-08T14:00:00Z",
  "solicitadaPor": "jefe.campo.01",
  "responsable": "Proyecto",
  "prioridad": "Urgente"
}
```

**Códigos HTTP:**

| Escenario | Código | `codigoError` | Notas |
|---|---|---|---|
| Happy path | `202 Accepted` | — | Location header apunta a `/api/v1/inspecciones/{id}`. 202 porque el cierre real (InspeccionCerrada_v1) ocurre asíncronamente via saga + outbox |
| Capability ausente (`generar-ot`) | `403 Forbidden` | `"PRE-1"` | PRE-1 — middleware de auth |
| Inspección no existe | `404 Not Found` | — | PRE-2 |
| Estado != Firmada | `422 Unprocessable Entity` | `"I-F4-ESTADO"` | PRE-3 |
| Sin hallazgos RequiereIntervencion | `422 Unprocessable Entity` | `"I-F4-SIN-INTERVENCION"` | PRE-4 |
| OT ya solicitada | `409 Conflict` | `"I-F4-OT-DUPLICADA"` | PRE-5 |
| OT ya rechazada | `409 Conflict` | `"I-F4-OT-RECHAZADA"` | PRE-6 |
| Dictamen PuedeOperar | `422 Unprocessable Entity` | `"I-F4-DICTAMEN"` | PRE-7 — defensa de segunda línea |

**Permiso requerido:** capability `generar-ot` extraída del JWT del host PWA Sinco MYE. Independiente de `ejecutar-inspeccion`. El handler recibe las claims como `IReadOnlyCollection<string> Capabilities` por parámetro; el dominio nunca conoce JWTs directamente.

**Nota sobre código HTTP 202 vs 200:** se usa `202 Accepted` porque la solicitud desencadena un proceso asíncrono (saga `EjecutarOTSaga` → POST M-1 → push SignalR `OTGenerada`). El aprobador no recibe el número de OT inmediatamente — lo recibe via push SignalR cuando la saga complete (ADR-005). El `202` alinea la semántica con este patrón async.

---

## 10. Impacto en SignalR / push (si aplica)

**Este slice no emite eventos SignalR directamente.** El evento `OTSolicitada_v1` dispara la saga `EjecutarOTSaga` (slice 3.24b) que, en éxito, emite `InspeccionCerrada_v1`. Un proyector lateral sobre ese evento envía el push `OTGenerada` a través de `InspeccionesHub` (ADR-005 §14):

| Push SignalR | Emitido por | Audiencia | Cuándo |
|---|---|---|---|
| `OTGenerada` | Proyector sobre `InspeccionCerrada_v1` (slice 3.24b) | `Group("inspeccion-{InspeccionId}")` | Cuando M-1 confirma éxito y la saga cierra la inspección |
| `OTGeneracionFallida` | Proyector sobre `OTGeneracionFallida_v1` (slice 3.24b) | `Group("inspeccion-{InspeccionId}")` | Cuando M-1 agota reintentos |

El hub `InspeccionesHub` existe por ADR-005 pero **este slice no lo usa ni lo instancia**. El aprobador y el técnico reciben el push cuando la saga completa — responsabilidad de los slices de sagas.

**No aplica directamente para este slice.**

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**Este slice no invoca ningún endpoint Sinco on-prem.** La emisión de `OTSolicitada_v1` es una operación puramente de dominio local — solo persiste el evento en el stream de Marten y retorna `202 Accepted`. El POST a MYE (`M-1: POST /api/v1/mye/ot-correctivas`) es responsabilidad de la saga `EjecutarOTSaga` (slice 3.24b), que reacciona al evento via outbox Wolverine (ADR-006).

**No aplica para este slice.**

---

## 12. Preguntas abiertas

### P-1 — Prioridad: derivada automática vs campo explícito del comando (DECISIÓN REQUERIDA)

El modelo §17 (ADR-007) muestra en la sección "Flujo de error" una regla de derivación automática de prioridad:
- Dictamen `NoPuedeOperar` → `Prioridad = Urgente`.
- Dictamen `ConRestriccion` con ≥1 `RequiereIntervencion` → `Prioridad = Alta`.

Pero en la misma sección §17 también muestra `PrioridadOT Prioridad` como campo del `CrearOTCorrectivaRequest_v1` enviado a MYE — campo que la saga construye.

**Ambigüedad:** ¿la prioridad la calcula automáticamente el sistema (derivada del dictamen y hallazgos), o el aprobador la elige explícitamente en la pantalla de aprobación (con la sugerencia pre-llenada)?

**Opciones:**
- **(A) Calculada automáticamente:** el método de decisión del aggregate (o la saga) deriva la prioridad del dictamen. El comando `GenerarOT` no incluye `Prioridad`; se omite el campo del evento. Pro: sin riesgo de entrada incorrecta. Con: el aprobador no puede sobreescribir.
- **(B) Campo explícito del aprobador (opción de este spec):** la UI pre-llena la prioridad sugerida según el dictamen, pero el aprobador puede cambiarla antes de confirmar. El comando incluye `Prioridad`. Pro: flexibilidad operativa. Con: complejidad mínima adicional.

**Asunción de este spec:** opción (B). `Prioridad` es campo explícito del comando y del evento. Si el usuario confirma la opción (A), los campos `Prioridad`, `Observaciones` y `ComentarioJefe` del evento `OTSolicitada_v1` deben revisarse — `Prioridad` se eliminaría del evento (la saga la deriva al construir el payload de M-1); `Observaciones` y `ComentarioJefe` pueden mantenerse como campos opcionales del evento para auditoría.

**Impacto si se cambia a opción (A):** modificar el record `GenerarOT`, modificar el shape de `OTSolicitada_v1`, actualizar los escenarios 6.1 y 6.2, ajustar el spec antes de que `red` lo lea.

### P-2 — Campos `Observaciones` y `ComentarioJefe` en el evento vs en el handler (menor, aclarar)

Los campos `Observaciones` y `ComentarioJefe` son opcionales del comando. Se incluyen en `OTSolicitada_v1` para que la saga `EjecutarOTSaga` los tenga disponibles al construir el payload de M-1 sin necesidad de re-leer el stream o mantener estado de saga. Si el usuario prefiere no contaminar el evento del dominio con campos que son puramente de presentación (comentarios del aprobador), se pueden omitir del evento y persistir solo en la capa de handler/saga como contexto opaco. Confirmación del usuario requerida.

**Asunción de este spec:** se incluyen en el evento por simplicidad y para facilitar auditabilidad.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-7) mapean a al menos un escenario Given/When/Then en §6 (6.3→PRE-1, 6.10→PRE-2, 6.4+6.12→PRE-3, 6.5+6.11→PRE-4, 6.6→PRE-5, 6.7→PRE-6, 6.8→PRE-7).
- [x] Todas las invariantes tocadas (I-F4 completa con sus 5 sub-condiciones, I-F5 como estado previo, I-F1) mapean a escenarios Then.
- [x] El happy path (§6.1) está presente.
- [x] El escenario de rebuild desde stream (§6.13) está presente con los 7 eventos en orden causal.
- [x] §7 (idempotencia) está decidido: ADR-008 `X-Client-Command-Id` cubre replay de red; PRE-5 I-F4.c cubre segundo intento humano (`409`). Sin POST a Sinco en este slice.
- [x] §10 (SignalR) resuelto explícitamente: no aplica en este slice; responsabilidad de la saga `EjecutarOTSaga` (slice 3.24b).
- [x] §11 (adapters Sinco on-prem) resuelto explícitamente: no aplica; responsabilidad de slice 3.24b.
- [x] §8 (proyecciones) documentadas: `BandejaInspeccionesPendientesOTView` impactada; `BandejaTecnicoView` e `InspeccionAbiertaPorEquipoView` no impactadas.
- [ ] §12 Preguntas abiertas: P-1 (prioridad derivada vs explícita) y P-2 (campos opcionales en evento) requieren decisión del usuario. **El spec puede firmarse con la asunción de opción (B) para P-1 y con inclusión de campos opcionales en el evento para P-2, si el usuario acepta esas asunciones explícitamente.**

---

## Notas para el agente `red`

**Archivos a crear/modificar en este slice:**

| Tipo | Archivo (ruta relativa a `src/`) | Operación |
|---|---|---|
| Evento | `Inspecciones.Domain/Inspecciones/Eventos/OTSolicitada_v1.cs` | Crear |
| Enums (si no existen) | `Inspecciones.Domain/Inspecciones/Enums/ResponsableCosto.cs` | Crear o verificar |
| Enums (si no existen) | `Inspecciones.Domain/Inspecciones/Enums/PrioridadOT.cs` | Crear o verificar |
| Excepción nueva | `Inspecciones.Domain/Inspecciones/Excepciones.cs` | Añadir `OTYaSolicitadaException`, `OTRechazadaException`, `SinHallazgosConIntervencionException`, `InspeccionNoFirmadaException`, `DictamenNoPermiteOTException` |
| Aggregate state | `Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Añadir `bool OTSolicitada`, `bool OTRechazada`, método de decisión `SolicitarOT(...)`, `Apply(OTSolicitada_v1)` |
| Comando | `Inspecciones.Application/Inspecciones/Comandos/GenerarOT.cs` | Crear |
| Handler | `Inspecciones.Application/Inspecciones/Handlers/GenerarOTHandler.cs` | Crear |
| Endpoint | `Inspecciones.Api/Inspecciones/Endpoints/GenerarOTEndpoint.cs` | Crear |
| Tests dominio | `Inspecciones.Domain.Tests/Inspecciones/GenerarOTTests.cs` | Crear — cobertura ≥85 % ramas del aggregate afectadas |
| Tests integración | `Inspecciones.Application.Tests/Inspecciones/GenerarOTHandlerTests.cs` | Crear |
| Tests HTTP | `Inspecciones.Api.Tests/Inspecciones/GenerarOTEndpointTests.cs` | Crear |

**Cobertura mínima:** ≥85 % de ramas del aggregate para las rutas que toca este slice (PRE-3..PRE-7 + happy path + rebuild).

**Convención de nombres de tests (español, referenciando código de invariante):**
- `GenerarOT_inspeccion_firmada_con_hallazgo_intervencion_emite_OTSolicitada_v1`
- `GenerarOT_inspeccion_no_firmada_lanza_InspeccionNoFirmadaException_I_F4`
- `GenerarOT_sin_hallazgos_con_intervencion_lanza_SinHallazgosConIntervencionException_I_F4`
- `GenerarOT_OT_ya_solicitada_lanza_OTYaSolicitadaException_I_F4`
- `GenerarOT_OT_rechazada_previamente_lanza_OTRechazadaException_I_F4`
- `GenerarOT_dictamen_PuedeOperar_lanza_DictamenNoPermiteOTException_I_F4`
- `GenerarOT_hallazgo_intervencion_eliminado_no_cuenta_lanza_SinHallazgos`
- `GenerarOT_inspeccion_cerrada_SinOT_lanza_InspeccionNoFirmadaException`
- `GenerarOT_replay_mismo_MessageId_no_duplica_evento`
- `GenerarOT_rebuild_desde_stream_7_eventos_estado_correcto`
