# Slice 1m — CancelarInspeccion

**Autor:** domain-modeler
**Fecha:** 2026-05-11
**Estado:** draft
**Agregado afectado:** `Inspeccion` (aggregate unificado — aplica a `TipoInspeccion.Tecnica` y `TipoInspeccion.Monitoreo`).
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §2.1` — diagrama de estados: `CancelarInspeccion` es la transición `EnEjecucion → Cancelada`. Cita explícita: "Cancelable desde EnEjecucion → Cancelada".
- `01-modelo-dominio.md §2.1 (invariante I6)` — "Solo se puede cancelar en `EnEjecucion`". Fuente canónica de la precondición de estado.
- `01-modelo-dominio.md §2.1 (invariantes I-F1)` — "Una vez en estado Firmada → no se puede [...] cancelar la inspección." La cancelación post-firma está explícitamente prohibida.
- `01-modelo-dominio.md §2.1 (eventos canónicos)` — `CancelarInspeccion` command record: `(InspeccionId, Motivo, CanceladaPor)`. `InspeccionCancelada_v1`: `(InspeccionId, Motivo, CanceladaPor, CanceladaEn)`.
- `01-modelo-dominio.md §7.2` — "Flujo de cancelación: `EnEjecucion → CancelarInspeccion → InspeccionCancelada_v1`. La saga NO ejecuta posts a Sinco. Estado terminal: `Cancelada`."
- `01-modelo-dominio.md §15.4` — `InspeccionCancelada_v1` está en el catálogo canónico de 24 eventos (evento #16).
- `01-modelo-dominio.md §15.7` — invariante I-F1 explícita: cancelar está prohibido post-firma. I6 fija la única ventana válida para cancelar.
- `01-modelo-dominio.md §15.12.6` — `InspeccionAbiertaPorEquipoView` consume `InspeccionCancelada_v1` → delete fila; el equipo queda libre para nueva inspección.
- `slices/1g-firmar-inspeccion/spec.md` — establece la frontera post-firma (I-F1); PRE-2 de firma exige `EnEjecucion`, igual que la cancelación. Este slice no puede ir sobre estados post-firma.
- `slices/1l-rechazar-generar-ot/spec.md` — diferencia semántica: `RechazarGenerarOT` cierra una inspección **firmada** con motivo de negocio ("no creo la OT"). `CancelarInspeccion` abandona la inspección **antes de firmar** — no llega a existir ni dictamen ni OT. Son rutas de salida ortogonales.
- ADR-006 (`§16`) — no aplica: no hay POST al ERP en cancelación (§7.2 del modelo).
- ADR-005 (`§14`) — SignalR: no aplica a este slice.
- ADR-008 (`00-investigacion-mercado.md §9.16`) — `X-Client-Command-Id` mapeado a `MessageId` Wolverine; idempotencia end-to-end.
- `roadmap.md §3.7` (lifecycle) y `§3.43` (endpoint `POST /inspecciones/{id}/cancelar`).

---

## 1. Intención

El técnico necesita poder abandonar una inspección técnica o de monitoreo que ya inició pero que no puede o no debe continuar (equipo trasladado a otra obra, emergencia operativa, error de selección de equipo, etc.). El sistema registra el motivo del abandono, transiciona la inspección al estado terminal `Cancelada` y libera el equipo para que otro técnico pueda iniciar una nueva inspección. La cancelación solo es válida mientras la inspección está en ejecución — una vez firmada, el modelo prohíbe explícitamente cancelar (I-F1); el único camino es `RechazarGenerarOT` (para el ramal de OT) o crear una nueva inspección.

---

## 2. Comando

```csharp
public sealed record CancelarInspeccion(
    Guid         InspeccionId,
    string       Motivo,           // texto libre obligatorio; mínimo 10 chars (trimmed); max 500 chars (D-1)
    string       CanceladaPor      // userId opaco del técnico, extraído del JWT por la capa API
) : ICommand;
```

**Notas sobre el payload:**

> **`Motivo`:** mínimo 10 caracteres después de trim. Máximo 500 chars propuesto (operativo para campo móvil — ver Decisión D-1). La validación del mínimo vive en el handler o al inicio del método de decisión; el máximo puede vivir en validación de DTO HTTP.
>
> **`CanceladaPor`:** el handler extrae el userId del JWT del host PWA y lo inyecta como parámetro; el dominio lo recibe como string opaco. Mismo patrón que `FirmadoPor` (slice 1g) y `RechazadoPor` (slice 1l).
>
> **Sin `UbicacionGps` en el payload:** el modelo canónico `InspeccionCancelada_v1` (§2.1) no incluye `UbicacionGps`. El `CancelarInspeccion` command canónico tampoco la incluye. La cancelación puede producirse en cualquier contexto (pérdida de señal, emergencia) — exigir GPS sería una fricción injustificada. Ver Decisión D-2.

**Claims del técnico** (parámetros adicionales del handler, no del command record):

```csharp
// Misma forma que ClaimsTecnico definido en slice 1g / 1a.
public sealed record ClaimsTecnico(
    string       TecnicoId,
    ISet<int>    ProyectosAsignados,
    bool         TieneCapabilityEjecutarInspeccion);
```

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/cancelar
public sealed record CancelarInspeccionRequest(
    string Motivo);

public sealed record CancelarInspeccionResult(
    Guid           InspeccionId,
    string         Estado,        // "Cancelada"
    DateTimeOffset CanceladaEn,
    string         CanceladaPor,
    string         Motivo);
```

---

## 3. Evento(s) emitido(s)

Este slice emite **exactamente un evento** en todos los casos de éxito, en un único `SaveChangesAsync`.

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `InspeccionCancelada_v1` | Ver shape a continuación | Cuando todas las precondiciones I6 + PRE-* se cumplen — registra el abandono y transiciona a estado terminal |

### 3.1 `InspeccionCancelada_v1` — shape canónico

El modelo histórico §2.1 define el shape con `DateTime CanceladaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset`. Se aplica la misma corrección aplicada en todos los slices anteriores (ver Decisión D-3).

```csharp
public sealed record InspeccionCancelada_v1(
    Guid           InspeccionId,
    string         Motivo,         // texto auditado del motivo de cancelación
    string         CanceladaPor,   // userId opaco del técnico
    DateTimeOffset CanceladaEn);   // DateTimeOffset — TimeProvider.GetUtcNow() en el handler
```

> **`DateTimeOffset` vs `DateTime`:** el modelo §2.1 históricamente usa `DateTime CanceladaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset`. Se propone corrección en el PR de este slice.

### 3.2 Impacto en estado interno del aggregate

```csharp
// Apply(InspeccionCancelada_v1) — mutación pura (ya existe en el aggregate):
// Estado            = InspeccionEstado.Cancelada;
// MotivoCancelacion = e.Motivo;
```

El modelo §2.1 ya define `MotivoCancelacion: string?` en la estructura interna del aggregate. El `Apply` existente ya setea ambos campos. Este slice no requiere nuevos campos en el aggregate — solo eleva el `Apply` a `DateTimeOffset` y agrega el método de decisión `Cancelar(...)`.

Las precondiciones (I6, PRE-*) viven exclusivamente en el método de decisión `Cancelar(...)`. Ningún `Apply` lanza excepción.

---

## 4. Precondiciones

Evaluadas en capas jerárquicas. Los `Apply` son puros y nunca las re-evalúan.

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability `ejecutar-inspeccion`):** `claims.TieneCapabilityEjecutarInspeccion == true`. Sin ella, el endpoint devuelve `403 Forbidden` sin invocar el aggregate. Misma capability que los demás comandos de captura y firma — quien puede ejecutar una inspección puede cancelarla (ver Decisión D-4).

### Capa handler (antes de invocar el método de decisión)

- **PRE-2 (inspección existe):** el handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).
- **PRE-3 (técnico contribuyente):** `aggregate.TecnicosContribuyentes.Contains(cmd.CanceladaPor)`. Solo un técnico que haya contribuido al stream puede cancelar. Motivo: la cancelación es un acto de responsabilidad sobre la inspección propia; un técnico externo no tiene contexto. Excepción: `TecnicoNoContribuyenteException` (`403 Forbidden`) — ver Decisión D-5.

### Validación de input (antes de invocar el método de decisión)

- **PRE-4 (motivo no vacío y ≥10 chars):** `cmd.Motivo.Trim().Length >= 10`. Si no cumple → `MotivoCancelacionInvalidoException` (`422 Unprocessable Entity`). La validación se aplica sobre el valor trimmed.

### Método de decisión del aggregate (`Inspeccion.Cancelar`) — invariante I6

- **PRE-5 (estado EnEjecucion — I6):** `Estado == InspeccionEstado.EnEjecucion`. Si el estado es `Firmada`, `Cerrada`, `CerradaSinOT`, `CierrePendienteOT` o `Cancelada` → `InspeccionNoEnEjecucionException` (`409 Conflict` para estados terminales esperados — ver §9 tabla de códigos HTTP). La invariante I6 del modelo es explícita: "Solo se puede cancelar en `EnEjecucion`". La invariante I-F1 prohíbe cancelar en `Firmada` — ambas invariantes quedan cubiertas por PRE-5.

> **Capa donde viven:** PRE-1 en capa HTTP; PRE-2, PRE-3 y PRE-4 en el handler; PRE-5 en el método de decisión `Inspeccion.Cancelar`. Ningún `Apply` re-valida. Las invariantes I6 e I-F1 están ambas cubiertas por PRE-5.

---

## 5. Invariantes tocadas

- **I6 (solo cancelar en `EnEjecucion`):** fuente canónica `§2.1`. Cubierta íntegramente por PRE-5. Estado origen permitido: únicamente `EnEjecucion`. Todo otro estado → excepción.
- **I-F1 (inmutabilidad post-firma — no se puede cancelar post-firma):** fuente canónica `§15.7`. Cubierta por PRE-5: al verificar `Estado == EnEjecucion`, el estado `Firmada` queda automáticamente excluido sin necesidad de condición adicional.
- **I-I1 (una sola inspección abierta por equipo):** la proyección `InspeccionAbiertaPorEquipoView` consume `InspeccionCancelada_v1` → delete fila. Tras la cancelación el equipo queda libre. No es una precondición del comando sino un postcondition observacional gestionado por la proyección.

### Invariantes no tocadas

- I-H*, I-H9 (hallazgos): no aplican — la cancelación no valida hallazgos. Los hallazgos registrados hasta el momento permanecen en el stream como histórico auditado (soft delete implícito por estado terminal del aggregate).
- V-F1..V-F8 (validaciones pre-firma): no aplican — la cancelación es anterior a la firma.
- I-F2, I-F3, I-F4, I-F5, I-F6 (post-firma, OT): no aplican.
- I-I2, I-I3 (inicio): no aplican.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — cancelar inspección en ejecución (técnica, sin hallazgos)

**Given**
- Stream con eventos:
  1. `InspeccionIniciada_v1` (EquipoId=42, TecnicoId="carlos.ruiz", Tipo=Tecnica, Estado=EnEjecucion, TecnicosContribuyentes={"carlos.ruiz"})
- `aggregate.Estado == EnEjecucion`.
- `aggregate.TecnicosContribuyentes.Contains("carlos.ruiz") == true`.
- `TimeProvider` retorna `2026-05-11T10:00:00Z`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Equipo trasladado a otra obra sin previo aviso", CanceladaPor="carlos.ruiz")`.
- Claims: `TieneCapabilityEjecutarInspeccion=true`.

**Then**
- Se emite exactamente **un** evento:
  1. `InspeccionCancelada_v1(InspeccionId=X, Motivo="Equipo trasladado a otra obra sin previo aviso", CanceladaPor="carlos.ruiz", CanceladaEn=2026-05-11T10:00:00Z)`.
- Estado post-comando: `aggregate.Estado == Cancelada`, `aggregate.MotivoCancelacion == "Equipo trasladado a otra obra sin previo aviso"`.
- Handler retorna `CancelarInspeccionResult(InspeccionId=X, Estado="Cancelada", CanceladaEn=2026-05-11T10:00:00Z, CanceladaPor="carlos.ruiz", Motivo="Equipo trasladado a otra obra sin previo aviso")`.
- Capa API devuelve `200 OK`.

### 6.2 Happy path — cancelar inspección en ejecución con hallazgos registrados (técnica)

**Given**
- Stream con eventos:
  1. `InspeccionIniciada_v1` (EquipoId=42, TecnicoId="carlos.ruiz", TecnicosContribuyentes={"carlos.ruiz"})
  2. `HallazgoRegistrado_v1` (HallazgoId=h1, AccionRequerida=RequiereIntervencion, Eliminado=false)
  3. `HallazgoRegistrado_v1` (HallazgoId=h2, AccionRequerida=RequiereSeguimiento, Eliminado=false)
- `aggregate.Estado == EnEjecucion`, `Hallazgos.Count == 2`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Error de selección de equipo, se reinspeccionará mañana", CanceladaPor="carlos.ruiz")`.

**Then**
- Se emite exactamente **un** evento: `InspeccionCancelada_v1`.
- `aggregate.Estado == Cancelada`.
- Los hallazgos h1 y h2 permanecen en el stream como histórico auditado — no se eliminan.
- Capa API devuelve `200 OK`.

### 6.3 Happy path — cancelar inspección de monitoreo en ejecución

**Given**
- Stream con eventos:
  1. `InspeccionIniciada_v1` (Tipo=Monitoreo, TecnicoId="juan.perez", TecnicosContribuyentes={"juan.perez"})
  2. `ItemMonitoreoOmitido_v1` (ItemId=5, EmitidoPor="juan.perez")
- `aggregate.Estado == EnEjecucion`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Equipo fuera de operación por falla eléctrica", CanceladaPor="juan.perez")`.

**Then**
- Se emite exactamente **un** evento: `InspeccionCancelada_v1`.
- `aggregate.Estado == Cancelada`.
- Capa API devuelve `200 OK`.

### 6.4 Violación PRE-1 — capability `ejecutar-inspeccion` ausente (403)

**Given**
- Aggregate en estado `EnEjecucion`.
- Claims: `TieneCapabilityEjecutarInspeccion=false`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Motivo de cancelación válido completo", CanceladaPor="tecnico.01")`.

**Then**
- Middleware de autorización lanza excepción 403 antes de llegar al handler.
- Sin evento.
- `aggregate.Estado` permanece `EnEjecucion`.
- Capa API devuelve `403 Forbidden`.

### 6.5 Violación PRE-2 — inspección no existe (404)

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando `CancelarInspeccion(InspeccionId=Z, Motivo="Motivo de cancelación válido completo", CanceladaPor="tecnico.01")`.
- Claims: `TieneCapabilityEjecutarInspeccion=true`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Sin evento.
- Capa API devuelve `404 Not Found`.

### 6.6 Violación PRE-3 — técnico no contribuyente (403)

**Given**
- Aggregate con `InspeccionIniciada_v1` (TecnicoId="carlos.ruiz", TecnicosContribuyentes={"carlos.ruiz"}).
- `aggregate.Estado == EnEjecucion`.
- Claims: `TieneCapabilityEjecutarInspeccion=true`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Motivo de cancelación suficientemente largo", CanceladaPor="tecnico.externo.99")` (no contribuyente).

**Then**
- Handler lanza `TecnicoNoContribuyenteException("El técnico 'tecnico.externo.99' no ha contribuido a la inspección X. Solo un técnico contribuyente puede cancelarla.")`.
- Sin evento.
- Capa API devuelve `403 Forbidden`.

### 6.7 Violación PRE-4 — motivo vacío (422)

**Given**
- Aggregate en estado `EnEjecucion`, `TecnicosContribuyentes={"carlos.ruiz"}`.
- Claims: `TieneCapabilityEjecutarInspeccion=true`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="", CanceladaPor="carlos.ruiz")`.

**Then**
- Handler lanza `MotivoCancelacionInvalidoException("El motivo de cancelación no puede estar vacío.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I6-MOTIVO" }`.

### 6.8 Violación PRE-4 — motivo solo espacios (422)

**Given**
- Aggregate en estado `EnEjecucion`, contribuyente `"carlos.ruiz"`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="   ", CanceladaPor="carlos.ruiz")`.

**Then**
- Handler lanza `MotivoCancelacionInvalidoException` (trim da cadena vacía → length = 0 < 10).
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I6-MOTIVO" }`.

### 6.9 Violación PRE-4 — motivo con menos de 10 chars (422)

**Given**
- Aggregate en estado `EnEjecucion`, contribuyente `"carlos.ruiz"`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Corto", CanceladaPor="carlos.ruiz")` (5 chars < 10).

**Then**
- Handler lanza `MotivoCancelacionInvalidoException("El motivo de cancelación debe tener al menos 10 caracteres.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I6-MOTIVO" }`.

### 6.10 Violación PRE-5 / I6 — inspección ya firmada (I-F1) (409)

**Given**
- Stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` + `DiagnosticoEmitido_v1` + `DictamenEstablecido_v1` + `InspeccionFirmada_v1` (Estado=Firmada).
- `aggregate.Estado == Firmada`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Intento de cancelar inspección ya firmada", CanceladaPor="carlos.ruiz")`.
- Claims: `TieneCapabilityEjecutarInspeccion=true`.

**Then**
- Aggregate lanza `InspeccionNoEnEjecucionException("La inspección X está en estado 'Firmada'. CancelarInspeccion solo aplica a inspecciones en estado 'EnEjecucion'.")`.
- Sin evento.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I6-ESTADO" }`.

### 6.11 Violación PRE-5 / I6 — inspección ya cancelada (409, idempotencia natural)

**Given**
- Stream con `InspeccionIniciada_v1` + `InspeccionCancelada_v1` (Estado=Cancelada).
- `aggregate.Estado == Cancelada`.

**When**
- Segundo intento de cancelación: `CancelarInspeccion(InspeccionId=X, Motivo="Segundo intento de cancelar", CanceladaPor="carlos.ruiz")`.

**Then**
- Aggregate lanza `InspeccionNoEnEjecucionException("La inspección X está en estado 'Cancelada'. CancelarInspeccion solo aplica a inspecciones en estado 'EnEjecucion'.")`.
- Sin evento — el stream permanece con un único `InspeccionCancelada_v1`.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I6-ESTADO" }`.

### 6.12 Violación PRE-5 / I6 — inspección ya cerrada (CerradaSinOT) (409)

**Given**
- Stream con `InspeccionFirmada_v1` + `InspeccionCerradaSinOT_v1` (Estado=CerradaSinOT).

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="Intentando cancelar una inspección cerrada", CanceladaPor="carlos.ruiz")`.

**Then**
- Aggregate lanza `InspeccionNoEnEjecucionException("La inspección X está en estado 'CerradaSinOT'. CancelarInspeccion solo aplica a inspecciones en estado 'EnEjecucion'.")`.
- Sin evento.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I6-ESTADO" }`.

### 6.13 Caso borde — segundo técnico contribuyente puede cancelar

**Given**
- Stream con:
  1. `InspeccionIniciada_v1` (TecnicoId="carlos.ruiz", TecnicosContribuyentes={"carlos.ruiz"})
  2. `HallazgoRegistrado_v1` (EmitidoPor="juan.perez") — incorpora a juan.perez como contribuyente automáticamente (I-I2b)
- `aggregate.TecnicosContribuyentes` = {"carlos.ruiz", "juan.perez"}.
- `aggregate.Estado == EnEjecucion`.

**When**
- Comando `CancelarInspeccion(InspeccionId=X, Motivo="El técnico iniciador no puede continuar por emergencia", CanceladaPor="juan.perez")`.

**Then**
- Se emite `InspeccionCancelada_v1(CanceladaPor="juan.perez", Motivo="El técnico iniciador no puede continuar por emergencia")`.
- `aggregate.Estado == Cancelada`.
- No lanza excepción. PRE-3 se cumple porque juan.perez es contribuyente.

### 6.14 Idempotencia — replay con mismo `X-Client-Command-Id`

**Given**
- Comando `CancelarInspeccion` con `MessageId=Y` ya ejecutado exitosamente. Wolverine envelope storage tiene respuesta original (`aggregate.Estado == Cancelada`, evento emitido).

**When**
- Cliente reenvía mismo `MessageId=Y` tras timeout de red.

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- El stream sigue con exactamente un `InspeccionCancelada_v1` — sin duplicación.
- Capa API devuelve `200 OK` con body original.

### 6.15 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos).
- Lista de eventos en orden causal para reproducir el happy path §6.1:
  1. `InspeccionIniciada_v1(EquipoId=42, TecnicoId="carlos.ruiz", Tipo=Tecnica, Estado=EnEjecucion)`
  2. `InspeccionCancelada_v1(InspeccionId=X, Motivo="Equipo trasladado a otra obra sin previo aviso", CanceladaPor="carlos.ruiz", CanceladaEn=2026-05-11T10:00:00Z)`

**When**
- Se reproyectan los dos eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- Estado resultante:
  - `Estado == InspeccionEstado.Cancelada`.
  - `MotivoCancelacion == "Equipo trasladado a otra obra sin previo aviso"`.
  - `TecnicosContribuyentes.Contains("carlos.ruiz") == true`.
  - `Hallazgos.Count == 0`.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al obtenido tras ejecutar el comando en §6.1.

> **Justificación:** garantiza que `Apply(InspeccionCancelada_v1)` es puro (no valida, no lanza), que el timestamp correcto se almacena en `CanceladaEn`, y que el `MotivoCancelacion` queda seteado tras el fold. También confirma que el orden causal de los dos eventos (`InspeccionIniciada_v1` → `InspeccionCancelada_v1`) es coherente — no puede cancelarse lo que no existe.

### 6.16 Rebuild con hallazgos — hallazgos persisten en stream tras cancelación

**Given**
- Aggregate vacío.
- Lista de eventos:
  1. `InspeccionIniciada_v1` (TecnicoId="carlos.ruiz")
  2. `HallazgoRegistrado_v1` (HallazgoId=h1, AccionRequerida=RequiereIntervencion, Eliminado=false)
  3. `HallazgoRegistrado_v1` (HallazgoId=h2, AccionRequerida=RequiereSeguimiento, Eliminado=false)
  4. `InspeccionCancelada_v1` (Motivo="Error de selección de equipo, se reinspeccionará mañana", CanceladaPor="carlos.ruiz")

**When**
- Se reproyectan los cuatro eventos en orden.

**Then**
- `aggregate.Estado == Cancelada`.
- `aggregate.Hallazgos.Count == 2` (h1 y h2 permanecen; el aggregate cancelado conserva la historia).
- `aggregate.MotivoCancelacion == "Error de selección de equipo, se reinspeccionará mañana"`.
- Ningún `Apply` lanza excepción.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico confirma la cancelación. Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler (escenario §6.14).

**Idempotencia natural por PRE-5 (I6):**

Si el técnico reenvía con un `clientCommandId` distinto (nuevo retry humano) sobre una inspección que ya tiene `InspeccionCancelada_v1`, el aggregate detectará en PRE-5 que el estado es `Cancelada` y lanzará `InspeccionNoEnEjecucionException` (`409 Conflict`). El `409` es intencional: señala que la inspección ya está en estado terminal. El cliente no debe reintentar automáticamente en `409`.

**Sin POST a Sinco en este slice:**

El modelo §7.2 es explícito: "La saga NO ejecuta posts a Sinco." No hay `Idempotency-Key` para ERP porque no hay llamada al ERP. El cierre es puramente local — solo persiste `InspeccionCancelada_v1` en el stream de Marten. El equipo se libera automáticamente por la proyección `InspeccionAbiertaPorEquipoView` que consume el evento.

---

## 8. Impacto en proyecciones / read models

### 8.1 `InspeccionAbiertaPorEquipoView` (§15.12.6) — equipo liberado

La proyección ya consume `InspeccionCancelada_v1` → delete fila (documentado en §15.12.6 del modelo: "Eventos consumidos: [...] `InspeccionCancelada_v1` → delete fila"). Este comportamiento **ya está definido en el modelo canónico** y debe estar implementado o stub-implementado desde el slice 1g (FU-13 incluye este case). No requiere nuevo trabajo en la proyección — el evento ya es el tipo correcto.

Tras la cancelación: el equipo queda disponible de inmediato para nueva inspección (otro técnico puede ejecutar `IniciarInspeccion`).

### 8.2 `BandejaTecnicoView` (§15.12.3)

La inspección aparecía en la bandeja del técnico como `EnEjecucion`. Al emitir `InspeccionCancelada_v1`, la fila debe transicionar a estado `Cancelada` (o desaparecer de la bandeja activa si el filtro por defecto excluye estados terminales). Esta proyección es responsabilidad de un slice separado. El agente `infra-wire` de este slice debe dejar un comentario `// TODO: actualizar BandejaTecnicoView con InspeccionCancelada_v1 en slice de proyecciones`.

### 8.3 `HistorialInspeccionesEquipoView` (si existe — §15.12 futuro)

Una proyección de historial de equipo debería incluir inspecciones canceladas con su motivo para trazabilidad. Sin impacto de implementación en este slice — el evento queda en el stream y cualquier proyección futura puede consumirlo. El agente `infra-wire` puede dejar TODO.

---

## 9. Impacto en endpoints HTTP

### Endpoint principal

| Campo | Valor |
|---|---|
| Método + ruta | `POST /api/v1/inspecciones/{id}/cancelar` |
| Path param | `{id}` = `InspeccionId` (Guid) |
| Content-Type | `application/json` |
| Authorization | JWT del host PWA; el middleware extrae `TecnicoId` y capability `ejecutar-inspeccion` del token |

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization: Bearer <JWT>` heredado del host PWA (ADR-002 tentativo).

**DTO de request (body JSON):**

```json
{
  "motivo": "Equipo trasladado a otra obra sin previo aviso"
}
```

**DTO de response (200 OK, body JSON):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "estado": "Cancelada",
  "canceladaEn": "2026-05-11T10:00:00Z",
  "canceladaPor": "carlos.ruiz",
  "motivo": "Equipo trasladado a otra obra sin previo aviso"
}
```

**Códigos HTTP:**

| Escenario | Código | `codigoError` | Notas |
|---|---|---|---|
| Happy path | `200 OK` | — | La inspección se cancela síncronamente — sin saga asíncrona |
| Capability `ejecutar-inspeccion` ausente | `403 Forbidden` | — | PRE-1 — middleware de auth |
| Inspección no existe | `404 Not Found` | — | PRE-2 |
| Técnico no contribuyente | `403 Forbidden` | `"I6-NO-CONTRIBUYENTE"` | PRE-3 |
| Motivo vacío / < 10 chars | `422 Unprocessable Entity` | `"I6-MOTIVO"` | PRE-4 |
| Estado != EnEjecucion (firmada, cerrada, cancelada) | `409 Conflict` | `"I6-ESTADO"` | PRE-5 — I6 + I-F1 |

**Nota sobre 200 OK:** se usa `200 OK` (no `202 Accepted`) porque la cancelación es síncrona — el único evento se persiste en `SaveChangesAsync` y el aggregate ya está en `Cancelada` cuando el handler retorna. Sin saga asíncrona pendiente.

**Permiso requerido:** capability `ejecutar-inspeccion` extraída del JWT del host PWA. Misma capability que los comandos de captura (slice 1c, 1d, 1e, 1f). El handler recibe `ClaimsTecnico` como parámetro; el dominio nunca conoce JWTs directamente.

---

## 10. Impacto en SignalR / push (si aplica)

**Este slice no emite eventos SignalR.** La cancelación de una inspección es un evento local — solo el técnico que canceló y potencialmente su supervisor necesitan conocerlo, pero en MVP no se prioriza notificación push para cancelaciones. El modelo §7.2 no menciona push SignalR para la cancelación.

Si en el futuro se requiere notificación push para cancelaciones (p. ej. alertar al supervisor de campo), un proyector lateral sobre `InspeccionCancelada_v1` puede emitir el push sin cambios en este slice.

El hub `InspeccionesHub` existe por ADR-005 pero **este slice no lo instancia**.

**No aplica para este slice.**

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**Este slice no invoca ningún endpoint Sinco on-prem.** El modelo §7.2 es explícito: "La saga NO ejecuta posts a Sinco." La cancelación es una operación puramente de dominio local — solo persiste `InspeccionCancelada_v1` en el stream de Marten y retorna `200 OK`. Sin outbox, sin `Idempotency-Key` para ERP.

**No aplica para este slice.**

---

## 12. Decisiones del modelador y preguntas abiertas

### Decisiones tomadas (el usuario debe confirmar o revertir antes de firmar)

**D-1 — Longitud máxima de `Motivo`: 500 chars (asunción del modelador)**

El modelo canónico §2.1 define solo que `Motivo` es texto libre obligatorio; no especifica mínimo ni máximo. El mínimo de 10 chars se toma por consistencia con `RechazarGenerarOT` (slice 1l). El máximo de 500 chars es propuesto por este spec como límite operativo razonable para un campo móvil de texto libre de auditoría. Puede eliminarse sin impacto en el dominio (solo validación DTO HTTP).

**D-2 — Sin `UbicacionGps` en el payload de cancelación (asunción del modelador)**

El comando canónico `CancelarInspeccion` en §2.1 no incluye `UbicacionGps`. A diferencia de `IniciarInspeccion` y `FirmarInspeccion` (donde el GPS es evidencia de presencia física), la cancelación puede producirse en cualquier contexto operativo (pérdida de señal, urgencia, regreso a oficina). Agregar GPS obligatorio a la cancelación crearía fricción innecesaria. El GPS de inicio ya está en el stream (`InspeccionIniciada_v1.Ubicacion`) para trazabilidad.

Si el usuario considera que la ubicación de cancelación es relevante para auditoría (p. ej. verificar que el técnico seguía en planta al cancelar), puede agregarse como campo opcional `UbicacionGps? UbicacionCancelacion` sin romper el dominio — pero no es load-bearing para MVP.

**D-3 — `DateTimeOffset` en lugar de `DateTime` para `CanceladaEn` (corrección estándar)**

El model §2.1 históricamente usa `DateTime CanceladaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset`. Se aplica la misma corrección aplicada en todos los slices anteriores. Se propone corrección al modelo §2.1 en el PR de este slice.

**D-4 — Capability requerida: `ejecutar-inspeccion` (asunción del modelador)**

El modelo §2.1 no especifica explícitamente la capability para `CancelarInspeccion`. La cancelación es parte del ciclo de vida de la inspección: quien puede ejecutar, puede cancelar. La capability `ejecutar-inspeccion` es la más natural (misma que todos los comandos de captura y firma). Si el negocio requiere que solo el iniciador o un supervisor pueda cancelar, la restricción se cubre por PRE-3 (técnico contribuyente) sin agregar capability separada.

Alternativa descartada: capability `cancelar-inspeccion` separada — agrega complejidad de administración sin beneficio aparente para MVP.

**D-5 — Solo técnicos contribuyentes pueden cancelar (asunción del modelador)**

El modelo §2.1 no especifica explícitamente quién puede cancelar (solo que debe estar en `EnEjecucion`). Por analogía con PRE-9 de `FirmarInspeccion` (slice 1g), se propone que solo un contribuyente del stream pueda cancelar. Un técnico externo que no ha interactuado con la inspección no debería poder cancelarla — podría interferir con el trabajo en curso.

Caso límite: ¿puede un supervisor con capability distinta cancelar una inspección aunque no sea contribuyente? El modelo no lo especifica. Este spec lo deja como **pregunta abierta P-1**.

### Preguntas abiertas

**P-1 — ¿Puede un supervisor cancelar una inspección aunque no sea contribuyente? (decisión requerida)**

El modelo §2.1 y §15.7 no definen una capability de "cancelar ajena". PRE-3 de este spec restringe la cancelación a contribuyentes del stream.

Opciones:
- (a) Solo contribuyentes pueden cancelar (D-5 actual). Simple y coherente con la filosofía de responsabilidad del contribuyente.
- (b) Contribuyentes O usuario con capability `administrar-inspecciones` o similar (aún no definida en ADR-007). Útil para operaciones de back-office o bloqueos operativos.

Si el usuario elige (b), PRE-3 debe extenderse para aceptar también la capability adicional, y `ClaimsTecnico` debe incluir ese flag. **Este spec avanza con la opción (a) como asunción; el `red` puede comenzar con ella.**

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-5) mapean a al menos un escenario Given/When/Then en §6 (6.4→PRE-1, 6.5→PRE-2, 6.6→PRE-3, 6.7+6.8+6.9→PRE-4, 6.10+6.11+6.12→PRE-5).
- [x] Todas las invariantes tocadas (I6, I-F1) mapean a escenarios Then.
- [x] El happy path (§6.1) está presente con inspección sin hallazgos.
- [x] Happy path secundario (§6.2) con hallazgos presentes; happy path terciario (§6.3) con tipo Monitoreo.
- [x] Caso borde multi-contribuyente (§6.13) presente.
- [x] El escenario de rebuild desde stream (§6.15) está presente con los 2 eventos en orden causal.
- [x] Rebuild con hallazgos (§6.16) presente para verificar que hallazgos persisten en stream tras cancelación.
- [x] §7 (idempotencia) está decidido: ADR-008 `X-Client-Command-Id` cubre replay de red; PRE-5 (estado Cancelada) cubre segundo intento humano (`409`). Sin POST a Sinco en este slice.
- [x] §10 (SignalR) resuelto explícitamente: no aplica para este slice.
- [x] §11 (adapters Sinco on-prem) resuelto explícitamente: no aplica.
- [x] §8 (proyecciones) documentadas: `InspeccionAbiertaPorEquipoView` ya consume `InspeccionCancelada_v1` (sin cambio nuevo); `BandejaTecnicoView` es responsabilidad de slice futuro.
- [x] §12 Decisiones del modelador: D-1..D-5 documentadas con justificación. Una pregunta abierta (P-1: capability supervisor para cancelar ajena) marcada explícitamente; el `red` puede comenzar con la asunción D-5 (solo contribuyentes).

---

## Notas para el agente `red`

**Archivos a crear/modificar en este slice:**

| Tipo | Archivo (ruta relativa a `src/`) | Operación |
|---|---|---|
| Excepción nueva | `Inspecciones.Domain/Inspecciones/Excepciones.cs` | Añadir `MotivoCancelacionInvalidoException` |
| Aggregate — método de decisión | `Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Añadir método de decisión `Cancelar(string motivo, string canceladaPor, DateTimeOffset canceladaEn)` con PRE-5 (I6 + I-F1). El `Apply(InspeccionCancelada_v1)` ya existe — verificar que usa `DateTimeOffset` |
| Evento (corregir tipo timestamp) | `Inspecciones.Domain/Inspecciones/InspeccionCancelada_v1.cs` | Si `CanceladaEn` es `DateTime` → cambiar a `DateTimeOffset` (D-3) |
| Comando | `Inspecciones.Application/Inspecciones/CancelarInspeccion.cs` | Crear |
| Handler | `Inspecciones.Application/Inspecciones/CancelarInspeccionHandler.cs` | Crear — carga aggregate, verifica PRE-2, PRE-3, PRE-4, llama `Cancelar(...)`, persiste |
| Request/Result | `Inspecciones.Api/Inspecciones/CancelarInspeccionRequest.cs` | Crear — `CancelarInspeccionRequest` + `CancelarInspeccionResult` |
| Endpoint | `Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Añadir `POST .../cancelar` |
| Tests dominio | `Inspecciones.Domain.Tests/Inspecciones/CancelarInspeccionTests.cs` | Crear — cobertura ≥85 % ramas del aggregate afectadas |
| Tests integración | `Inspecciones.Application.Tests/Inspecciones/CancelarInspeccionHandlerTests.cs` | Crear |
| Tests HTTP | `Inspecciones.Api.Tests/CancelarInspeccionEndpointTests.cs` | Crear |

**Convención de nombres de tests (español, referenciando código de invariante):**
- `CancelarInspeccion_en_ejecucion_emite_InspeccionCancelada_v1`
- `CancelarInspeccion_con_hallazgos_emite_InspeccionCancelada_v1_hallazgos_permanecen`
- `CancelarInspeccion_inspeccion_tipo_monitoreo_emite_InspeccionCancelada_v1`
- `CancelarInspeccion_segundo_contribuyente_puede_cancelar`
- `CancelarInspeccion_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I6`
- `CancelarInspeccion_inspeccion_ya_cancelada_lanza_InspeccionNoEnEjecucionException_I6`
- `CancelarInspeccion_inspeccion_cerrada_lanza_InspeccionNoEnEjecucionException_I6`
- `CancelarInspeccion_motivo_vacio_lanza_MotivoCancelacionInvalidoException`
- `CancelarInspeccion_motivo_solo_espacios_lanza_MotivoCancelacionInvalidoException`
- `CancelarInspeccion_motivo_menor_10_chars_lanza_MotivoCancelacionInvalidoException`
- `CancelarInspeccion_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException`
- `CancelarInspeccion_rebuild_desde_stream_2_eventos_estado_correcto`
- `CancelarInspeccion_rebuild_con_hallazgos_hallazgos_persisten_estado_cancelada`

**Cobertura mínima:** ≥85 % de ramas del aggregate para las rutas que toca este slice (PRE-5 + validación motivo + happy path + rebuild).
