# Slice 1j — OmitirItemMonitoreo

**Autor:** domain-modeler
**Fecha:** 2026-05-08
**Estado:** listo para firma
**Agregado afectado:** `Inspeccion` (aggregate unificado — discriminador `TipoInspeccion.Monitoreo`).
**Decisiones previas relevantes:**
- `slices/1i-registrar-medicion/spec.md` — establece invariantes I-M1..I-M6, sets `_itemsMedidos` / `_itemsOmitidos`, excepciones de dominio del contexto monitoreo, patrón precondición en método de decisión.
- `slices/1i-prima-registrar-evaluacion-cualitativa/spec.md` — establece I-M5b, I-M7, `_itemsEvaluados`. Diferencia clave: ese slice SÍ emite hallazgo automático cuando `Calificacion=Malo`. Este slice NO emite hallazgo en ningún caso.
- `01-modelo-dominio.md §12.11.5 puntos 5, 6, 11` — shape canónico de `ItemMonitoreoOmitido_v1` (punto 5), tabla de trigger de hallazgo explicitando que omisión NO dispara hallazgo (punto 6, fila ausente de la tabla), comando hermano `IniciarInspeccionMonitoreo` (punto 11).
- `01-modelo-dominio.md §15.3` — invariantes I-H*, I-I*, I-F*, I-M*. Este slice confirma las invariantes I-M1..I-M4 ya establecidas y añade I-M8 (ítem ya medido/evaluado no puede omitirse) e I-M9 (doble omisión rechazada).
- `01-modelo-dominio.md §15.4` — catálogo MVP de eventos; `ItemMonitoreoOmitido_v1` listado.
- `roadmap.md §3.B' paso 3.16h` — `⏳ Pendiente`. Endpoint 3.36e.
- ADR-002 (`00-investigacion-mercado.md §9.11`) — claims del host PWA recibidos como parámetro.
- ADR-008 (`00-investigacion-mercado.md §9.16`) — `clientCommandId` UUIDv7 como `MessageId` Wolverine; idempotencia end-to-end.
- CLAUDE.md — `Apply` puro, rebuild test obligatorio, IDs `int` ERP + `Guid` internos, `DateTimeOffset` para timestamps.

---

## 1. Intención

El técnico de campo necesita registrar que un ítem de la rutina de monitoreo **no pudo ser ejecutado** durante la inspección, indicando el motivo (p. ej. "multímetro descargado, no pude medir", "sensor inaccesible por barro"). La omisión es una declaración explícita de que el ítem existe en el snapshot pero el técnico no puede registrar valor para él en esta ejecución.

A diferencia de `RegistrarMedicion` y `RegistrarEvaluacionCualitativa`, la omisión **nunca genera hallazgo automático** — el motivo de omisión es evidencia documental, no una anomalía técnica medida. Si el técnico detecta un problema al intentar el ítem (p. ej. el multímetro está descargado porque se usa para medir una batería dañada), puede registrar un hallazgo manual separado mediante `RegistrarHallazgo`.

Un ítem omitido queda bloqueado para medición o evaluación posterior en la misma inspección (I-M4). La firma valida que todos los ítems del snapshot tienen al menos un registro (medición, evaluación u omisión) antes de permitir cerrar la inspección.

**Motivación de negocio:** sin el comando de omisión, el checklist de la rutina queda con ítems en estado indeterminado ("¿no se midió o se olvidó?"). La omisión explícita con motivo preserva la integridad del historial de ejecución y permite al supervisor distinguir entre un ítem intencionalmente saltado (con justificación) y uno olvidado.

---

## 2. Comando

> **Decisión de diseño — método nuevo `Inspeccion.OmitirItem`:** sigue el mismo patrón de `RegistrarMedicion` e `RegistrarEvaluacionCualitativa`. No se sobrecarga ningún comando existente. El método de decisión es el que centraliza las precondiciones; el `Apply(ItemMonitoreoOmitido_v1)` es puramente aditivo (`_itemsOmitidos.Add(ItemId)`). A diferencia de los slices 1i e 1i', este comando emite **exactamente un evento** en todos los casos — no existe rama de dos eventos.

```csharp
public sealed record OmitirItemMonitoreo(
    Guid   InspeccionId,          // stream del aggregate
    int    ItemId,                // PK ERP del ítem de la rutina (int — convención §15.4)
    string Motivo,                // texto libre del técnico — mínimo 10 chars, no vacío, no solo whitespace
    string EmitidoPor,            // tecnicoId opaco del JWT
    IReadOnlyCollection<string> Capabilities  // claims del host PWA — nunca toca HTTP
);
```

> `HallazgoId` **no viaja en este comando** — a diferencia de los slices 1i e 1i', la omisión no puede disparar hallazgo automático en ningún caso. El técnico que detecte un problema lo registra como hallazgo manual separado.

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/omitir
public sealed record OmitirItemMonitoreoRequest(
    string Motivo);

public sealed record OmitirItemMonitoreoResult(
    Guid   InspeccionId,
    int    ItemId,
    string Motivo,
    DateTimeOffset OmitidoEn);
```

---

## 3. Evento(s) emitido(s)

Este slice emite **exactamente un evento** en todos los casos de éxito, en un único `SaveChangesAsync`.

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `ItemMonitoreoOmitido_v1` | Ver shape a continuación | Siempre que las precondiciones se cumplan |

**No existe rama multi-evento.** La omisión no deriva en `HallazgoRegistrado_v1` bajo ninguna circunstancia (§12.11.5 punto 6 — la tabla de trigger de hallazgo no incluye fila para omisión).

### 3.1 `ItemMonitoreoOmitido_v1` — shape canónico

El evento está definido en `01-modelo-dominio.md §12.11.5 punto 5`. El campo `OmitidoEn` se usa con `DateTimeOffset` (coherente con la convención del módulo — todos los timestamps son `DateTimeOffset`; el modelo histórico usa `DateTime`).

```csharp
public sealed record ItemMonitoreoOmitido_v1(
    Guid          InspeccionId,
    int           ItemId,        // PK ERP del ítem de la rutina (int — §15.4)
    string        Motivo,        // texto libre; mínimo 10 chars (validado en método de decisión)
    string        EmitidoPor,    // tecnicoId opaco del JWT — patrón idéntico a MedicionRegistrada_v1 y EvaluacionCualitativaRegistrada_v1
    DateTimeOffset OmitidoEn);   // DateTimeOffset — TimeProvider.GetUtcNow() en el handler
```

> **Campo `EmitidoPor` confirmado (decisión 2026-05-08, P-2):** se añade por coherencia con todos los demás eventos del módulo que registran quién realizó la acción (`MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1`, `HallazgoRegistrado_v1`). Necesario para proyectar `Contribuyentes` en `Apply(ItemMonitoreoOmitido_v1)` de la misma forma que los comandos hermanos. Se propone corrección al modelo §12.11.5 punto 5 en el PR de este slice.

> **Campo `DateTimeOffset` vs. `DateTime`:** el shape original del modelo usa `DateTime OmitidoEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset`. Se aplica la misma corrección que en `MedicionRegistrada_v1` (slice 1i) y `EvaluacionCualitativaRegistrada_v1` (slice 1i'). Se propone corrección al modelo en el PR de este slice.

### 3.2 Sin extensión de `HallazgoRegistrado_v1`

A diferencia de los slices 1i e 1i', este slice **no extiende** `HallazgoRegistrado_v1` — no se emite hallazgo.

### 3.3 Impacto en estado interno del aggregate

El `Apply(ItemMonitoreoOmitido_v1)` actualiza el `HashSet<int> _itemsOmitidos` que ya fue establecido en el slice 1i como parte del state del aggregate. Si el set no existe aún en la implementación, el `green` lo crea vacío al implementar este slice.

```csharp
// El Apply puro agrega el ítem al set:
// _itemsOmitidos.Add(evt.ItemId)
// _contribuyentes.Add(evt.EmitidoPor)
```

---

## 4. Precondiciones

Las precondiciones se clasifican por la capa donde viven. Los `Apply` son puros — nunca re-validan.

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability):** el usuario tiene capability `ejecutar-inspeccion` en los claims del host PWA (ADR-002 tentativo). Excepción: `403 Forbidden`.

### Capa handler (antes de invocar el método de decisión del aggregate)

- **PRE-2 (inspección existe):** el handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`). Excepción reutilizada de slices anteriores.

### Validación del DTO de entrada (capa HTTP / binding)

- **PRE-3 (motivo no vacío — validación de entrada):** `cmd.Motivo` no es `null`, no es cadena vacía, no es solo whitespace. Si falla → `400 Bad Request`.
- **PRE-4 (motivo longitud mínima — validación de entrada):** `cmd.Motivo.Trim().Length >= 10`. Si falla → `400 Bad Request`. El mínimo de 10 caracteres evita motivos triviales ("ok", "x") que no aportan información al supervisor.

> Las validaciones PRE-3 y PRE-4 pueden vivir en el binding del DTO de entrada (Data Annotations / FluentValidation en la capa API) o en el método de decisión del aggregate. Recomendación: validar en el aggregate para mantener las reglas de negocio cerca del dominio y cubierlas con tests de dominio puros.

### Método de decisión del aggregate (`Inspeccion.OmitirItem`)

- **PRE-5 (tipo Monitoreo — I-M1):** `Tipo == TipoInspeccion.Monitoreo`. Si la inspección es `Tecnica` → `InspeccionNoEsMonitoreoException` (`422 Unprocessable Entity`, código `I-M1`). Reutiliza excepción existente (slice 1i).
- **PRE-6 (estado EnEjecucion — I-M2):** `Estado == EstadoInspeccion.EnEjecucion`. Si `Estado ∈ {Firmada, CierrePendienteOT, Cerrada, CerradaSinOT, Cancelada}` → `InspeccionNoEnEjecucionException` (`422 Unprocessable Entity`, código `I-M2`). Reutiliza excepción existente.
- **PRE-7 (ítem existe en snapshot — I-M3):** `ItemId ∈ ItemsSnapshot.Select(i => i.ItemId)`. Si no existe → `ItemNoEncontradoEnSnapshotException` (`404 Not Found`, código `I-M3`). Reutiliza excepción existente (slice 1i). Código HTTP `404` — el ítem no es parte de esta inspección.
- **PRE-8 (ítem no medido ni evaluado — I-M8):** `ItemId ∉ _itemsMedidos` y `ItemId ∉ _itemsEvaluados`. Si el ítem ya tiene medición → `ItemYaProcesadoException` (`422 Unprocessable Entity`, código `I-M8`). Si el ítem ya tiene evaluación cualitativa → `ItemYaProcesadoException` (`422 Unprocessable Entity`, código `I-M8`). **Nueva excepción** (no existe en slices previos — los slices 1i/1i' tienen `ItemYaMedidoException` e `ItemYaEvaluadoException` para el caso de doble registro del mismo tipo; aquí el conflicto es cruzado: no se puede omitir algo ya procesado). Ver nota sobre semántica en §5.
- **PRE-9 (ítem no omitido previamente — I-M9):** `ItemId ∉ _itemsOmitidos`. Si el ítem ya fue omitido → `ItemYaOmitidoException` (`409 Conflict`, código `I-M9`). **Nueva excepción.** La doble omisión del mismo ítem es el equivalente a la doble medición (I-M6) o doble evaluación (I-M7): `409` señala idempotencia natural rota, no un error de validación de negocio.

> **Capa de validación:** PRE-1 en capa HTTP; PRE-2 en el handler; PRE-3/PRE-4 en capa HTTP/binding o en método de decisión; PRE-5 a PRE-9 en el método de decisión `Inspeccion.OmitirItem`. Ningún `Apply` re-valida.

> **Separación PRE-8 (I-M8) vs PRE-9 (I-M9):** PRE-8 captura el conflicto semántico "ya procesado con valor real → no tiene sentido omitir" (errror de dominio → `422`). PRE-9 captura la doble omisión (ya fue omitido antes) (conflicto de idempotencia → `409`). Los códigos HTTP son distintos por la naturaleza distinta del conflicto.

---

## 5. Invariantes tocadas

### Invariantes I-M* reutilizadas de contexto Monitoreo

- **I-M1 (tipo Monitoreo):** `OmitirItemMonitoreo` solo es válido sobre inspecciones con `Tipo=Monitoreo`. Cubierta por PRE-5. Reutiliza la invariante establecida en slice 1i: _"I-M1: El comando solo aplica a inspecciones de `Tipo=Monitoreo`."_
- **I-M2 (estado EnEjecucion):** `OmitirItemMonitoreo` solo es válido en estado `EnEjecucion`. Cubierta por PRE-6. Reutiliza la invariante: _"I-M2: El aggregate debe estar en estado `EnEjecucion`."_
- **I-M3 (ítem pertenece al snapshot):** el `ItemId` debe existir en `ItemsSnapshot`. Cubierta por PRE-7. Reutiliza: _"I-M3: ItemId debe existir en el snapshot de ítems capturado al iniciar la inspección."_
- **I-M4 (ítem no omitido → no puede medirse ni evaluarse):** este slice es el productor del estado `_itemsOmitidos` que I-M4 protege en los slices 1i e 1i'. La invariante I-M4 se leía desde esos slices como "si ya está en `_itemsOmitidos`, rechaza medición/evaluación". El slice 1j es el que escribe en `_itemsOmitidos`. No hay conflicto — los tres slices conviven en el mismo aggregate.

### Nuevas invariantes añadidas a §15.3 (decisión 2026-05-08)

- **I-M8 (ítem ya procesado no puede omitirse):** un ítem que ya recibió medición (`_itemsMedidos`) o evaluación cualitativa (`_itemsEvaluados`) no puede omitirse. Cubierta por PRE-8. Texto canónico para §15.3: _"I-M8: Un ítem con medición (I-M6) o evaluación cualitativa (I-M7) registrada no puede omitirse en la misma inspección. La omisión es un sustituto del registro, no un estado adicional."_

  > **Relación con I-M5b del slice 1i':** `I-M5b` cubre "ítem numérico rechaza evaluación cualitativa" (discriminación por tipo de ítem en el snapshot). `I-M8` cubre el cruce opuesto: "ítem ya procesado (cualquiera que sea el tipo) no acepta omisión tardía". Semánticas distintas — no hay colisión. `I-M5b` del slice 1i' queda sin cambios.

- **I-M9 (doble omisión rechazada):** un ítem ya omitido (`_itemsOmitidos`) no puede omitirse de nuevo. Cubierta por PRE-9. Texto canónico para §15.3: _"I-M9: Cada ítem del snapshot admite exactamente una omisión por inspección (`409 Conflict`). La doble omisión del mismo ítem no está permitida — simétrica a I-M6 (doble medición) e I-M7 (doble evaluación)."_

- **Invariante de no-hallazgo automático en omisión:** `OmitirItemMonitoreo` nunca emite `HallazgoRegistrado_v1`. Hardcodeado en el método de decisión — no existe camino condicional. Fuente: §12.11.5 punto 6 (tabla de trigger de hallazgo; la omisión no aparece). No se codifica como invariante numerada — es una propiedad estructural del método de decisión.

### Invariantes existentes no tocadas

- I-H* (invariantes de hallazgo): no aplican — este slice no emite `HallazgoRegistrado_v1`.
- I-F* / V-F* (firma): no aplican — el estado no avanza a `Firmada` en este slice.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — ítem válido, motivo válido (un evento)

**Given**
- Stream con `InspeccionIniciada_v1` donde `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `ItemsSnapshot` contiene `ItemId=3` con `Evaluacion=MedicionEsperada(...)`, `Parte="Sensor de presión"`, `Actividad="Medir presión hidráulica"`.
- `_itemsMedidos = {}`, `_itemsEvaluados = {}`, `_itemsOmitidos = {}`.
- `TimeProvider` retorna `2026-05-08T09:00:00Z`.
- Claims: `EmitidoPor="carlos.ruiz"`, capability `ejecutar-inspeccion`.

**When**
- Comando `OmitirItemMonitoreo(InspeccionId=X, ItemId=3, Motivo="Sensor inaccesible por barro acumulado en el compartimento", EmitidoPor="carlos.ruiz")`.

**Then**
- Se emite exactamente **un** evento: `ItemMonitoreoOmitido_v1(InspeccionId=X, ItemId=3, Motivo="Sensor inaccesible por barro acumulado en el compartimento", EmitidoPor="carlos.ruiz", OmitidoEn=2026-05-08T09:00:00Z)`.
- `_itemsOmitidos` contiene `3`.
- `_itemsMedidos` permanece vacío. `_itemsEvaluados` permanece vacío.
- `Hallazgos.Count` no cambia (cero nuevos hallazgos).
- Handler retorna `OmitirItemMonitoreoResult(InspeccionId=X, ItemId=3, Motivo="Sensor inaccesible por barro acumulado en el compartimento", OmitidoEn=2026-05-08T09:00:00Z)`.
- Capa API devuelve `200 OK`.

### 6.2 Motivo con exactamente 10 caracteres (límite inferior — válido)

**Given** — mismo aggregate que 6.1.

**When**
- Comando `OmitirItemMonitoreo(ItemId=3, Motivo="Sin acceso", ...)`. (`"Sin acceso"` = 10 chars).

**Then**
- Se emite `ItemMonitoreoOmitido_v1` con `Motivo="Sin acceso"`.
- Capa API devuelve `200 OK`.

### 6.3 Violación PRE-9 / I-M9 — ítem ya omitido previamente (409)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `_itemsOmitidos = {3}` (ítem ya omitido por `ItemMonitoreoOmitido_v1` previo).

**When**
- Segundo comando `OmitirItemMonitoreo(ItemId=3, Motivo="Mismo problema persiste", ...)`.

**Then**
- Aggregate lanza `ItemYaOmitidoException("El ítem 3 ya fue omitido en esta inspección. La doble omisión del mismo ítem no está permitida.")`.
- Sin evento. `_itemsOmitidos` no cambia.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I-M9" }`.

### 6.4 Violación PRE-8 / I-M8 — ítem ya medido no puede omitirse (422)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `_itemsMedidos = {3}` (ítem medido por `MedicionRegistrada_v1` previo).

**When**
- Comando `OmitirItemMonitoreo(ItemId=3, Motivo="Multímetro descargado ahora", ...)`.

**Then**
- Aggregate lanza `ItemYaProcesadoException("El ítem 3 ya tiene una medición registrada en esta inspección. Un ítem procesado no puede omitirse.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M8" }`.

### 6.5 Violación PRE-8 / I-M8 — ítem ya evaluado cualitativamente no puede omitirse (422)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `_itemsEvaluados = {4}` (ítem evaluado por `EvaluacionCualitativaRegistrada_v1` previo).

**When**
- Comando `OmitirItemMonitoreo(ItemId=4, Motivo="No pude acceder al componente", ...)`.

**Then**
- Aggregate lanza `ItemYaProcesadoException("El ítem 4 ya tiene una evaluación registrada en esta inspección. Un ítem procesado no puede omitirse.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M8" }`.

### 6.6 Violación PRE-3 / PRE-4 — motivo vacío (400)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemId=3` en snapshot.

**When**
- Comando `OmitirItemMonitoreo(ItemId=3, Motivo="", ...)`.

**Then**
- Lanza `MotivoOmisionInvalidoException("El motivo de omisión es obligatorio.")` (o equivalente de validación de entrada).
- Sin evento.
- Capa API devuelve `400 Bad Request` con `{ "codigoError": "MOTIVO-VACIO" }`.

### 6.7 Violación PRE-4 — motivo con menos de 10 caracteres (400)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemId=3` en snapshot.

**When**
- Comando `OmitirItemMonitoreo(ItemId=3, Motivo="corto", ...)`. (`"corto"` = 5 chars).

**Then**
- Lanza `MotivoOmisionInvalidoException("El motivo de omisión debe tener al menos 10 caracteres. Recibido: 5.")`.
- Sin evento.
- Capa API devuelve `400 Bad Request` con `{ "codigoError": "MOTIVO-LONGITUD" }`.

### 6.8 Violación PRE-5 / I-M1 — inspección técnica rechaza omisión (422)

**Given**
- Stream con `InspeccionIniciada_v1` donde `Tipo=Tecnica`, `Estado=EnEjecucion`.

**When**
- Comando `OmitirItemMonitoreo(InspeccionId=X, ItemId=3, Motivo="No pude acceder al componente", ...)`.

**Then**
- Aggregate lanza `InspeccionNoEsMonitoreoException("La inspección X es de tipo Tecnica. OmitirItemMonitoreo solo aplica a inspecciones de Tipo=Monitoreo.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M1" }`.

### 6.9 Violación PRE-7 / I-M3 — ítem inexistente en snapshot (404)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemsSnapshot` contiene solo `ItemId=3`.

**When**
- Comando `OmitirItemMonitoreo(ItemId=999, Motivo="Sensor no encontrado en la máquina", ...)`.

**Then**
- Aggregate lanza `ItemNoEncontradoEnSnapshotException("El ítem 999 no forma parte del snapshot de esta inspección. Solo pueden omitirse ítems del snapshot: [3].")`.
- Sin evento.
- Capa API devuelve `404 Not Found` con `{ "codigoError": "I-M3" }`.

### 6.10 Violación PRE-6 / I-M2 — inspección Firmada rechaza omisión (422)

**Given**
- Stream con `[InspeccionIniciada_v1(Tipo=Monitoreo), InspeccionFirmada_v1]` → `Estado=Firmada`.

**When**
- Comando `OmitirItemMonitoreo(InspeccionId=X, ItemId=3, Motivo="Sensor inaccesible por barro", ...)`.

**Then**
- Aggregate lanza `InspeccionNoEnEjecucionException("La inspección está en estado 'Firmada'. Solo se pueden omitir ítems en estado EnEjecucion.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M2" }`.

### 6.11 PRE-2 — InspeccionId no existe (404)

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando `OmitirItemMonitoreo(InspeccionId=Z, ItemId=3, Motivo="Sensor inaccesible por barro", ...)`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Capa API devuelve `404 Not Found`.

### 6.12 Idempotencia — replay con mismo `clientCommandId`

**Given**
- Comando con `MessageId=X` ya ejecutado exitosamente (`ItemId=3`, motivo válido). Wolverine envelope storage tiene respuesta original.

**When**
- Cliente reenvía mismo `MessageId=X` tras timeout de red.

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- `_itemsOmitidos` permanece con `{3}` (un solo registro).
- Un solo `ItemMonitoreoOmitido_v1` en el stream.
- Capa API devuelve `200 OK` con body original.

### 6.13 Coexistencia — múltiples ítems omitidos (no interfieren entre sí)

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `ItemsSnapshot` contiene `ItemId=3` y `ItemId=5`.
- `_itemsOmitidos = {3}` (ítem 3 ya omitido).

**When**
- Comando `OmitirItemMonitoreo(ItemId=5, Motivo="Mangera hidráulica con aceite, no toqué el sensor", ...)`.

**Then**
- Se emite `ItemMonitoreoOmitido_v1(ItemId=5, Motivo="Mangera hidráulica con aceite, no toqué el sensor", ...)`.
- `_itemsOmitidos = {3, 5}`.
- `_itemsMedidos` permanece sin cambio. `_itemsEvaluados` permanece sin cambio.
- `Hallazgos.Count` no cambia.

### 6.14 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos).
- Lista de eventos en orden causal para reproducir el happy path 6.1:
  1. `InspeccionIniciada_v1(Tipo=Monitoreo, Estado=EnEjecucion, ItemsSnapshot=[{ItemId=3, Parte="Sensor de presión", Actividad="Medir presión hidráulica", MedicionEsperada(...)}, {ItemId=4, Parte="Conectores", EvaluacionCualitativaEsperada()}])`.
  2. `ItemMonitoreoOmitido_v1(InspeccionId=X, ItemId=3, Motivo="Sensor inaccesible por barro acumulado en el compartimento", EmitidoPor="carlos.ruiz", OmitidoEn=2026-05-08T09:00:00Z)`.

**When**
- Se reproyectan los dos eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- Estado resultante:
  - `Tipo = TipoInspeccion.Monitoreo`.
  - `Estado = EstadoInspeccion.EnEjecucion`.
  - `ItemsSnapshot.Count = 2`.
  - `_itemsMedidos = {}`.
  - `_itemsEvaluados = {}`.
  - `_itemsOmitidos = {3}`.
  - `Hallazgos.Count = 0`.
  - `Contribuyentes = {"carlos.ruiz"}` (propagado desde `InspeccionIniciada_v1` e `ItemMonitoreoOmitido_v1`).
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al obtenido tras ejecutar el comando en 6.1.

> **Justificación del rebuild acotado:** la decisión del usuario es que el rebuild end-to-end del flujo monitoreo completo (iniciar → medir → evaluar → omitir → firmar) queda para el slice 1k de cierre (roadmap 3.16j). Este test cubre exclusivamente los eventos emitidos por el happy path de `OmitirItemMonitoreo` sobre un aggregate previamente iniciado, garantizando que `Apply(ItemMonitoreoOmitido_v1)` es puro y que el estado `_itemsOmitidos` se proyecta correctamente desde el stream.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico confirma la omisión del ítem. Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler (escenario 6.12).

**Idempotencia natural por PRE-9 (I-M9):**

Si el cliente reenvía con un `clientCommandId` distinto (nuevo retry) sobre un ítem ya omitido, el aggregate lanza `ItemYaOmitidoException` (`409 Conflict`). El `409` es intencional y no es retryable automáticamente: señala que el ítem ya fue omitido, no un error de red. El cliente no debe reintentar en `409`.

**Sin POST a Sinco:**

Este comando no cruza al ERP en ningún caso. ADR-006 (outbox para integraciones ERP) no aplica directamente — no hay llamada HTTP saliente en el handler. El outbox transaccional de Wolverine garantiza atomicidad evento + proyección + envelope en un único `SaveChangesAsync`.

**`Idempotency-Key` para Sinco:**

No aplica en este slice (sin llamadas al ERP).

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` — consumirá `ItemMonitoreoOmitido_v1`

La proyección `DetalleInspeccionView` (§15.12.1) no existe aún (roadmap 3.45). Cuando se implemente, debe consumir `ItemMonitoreoOmitido_v1` para mostrar el motivo de omisión por ítem. Este slice no la crea; documenta qué evento consume.

Campos proyectados: `ItemId`, `Motivo`, `EmitidoPor`, `OmitidoEn`.

### 8.2 `BandejaTecnicoView` — no impactada

Solo reacciona a eventos de lifecycle de la inspección (inicio, firma, cierre). Sin cambio.

### 8.3 `InspeccionAbiertaPorEquipoView` — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio.

### 8.4 `ItemsMonitoreoView` (futura)

La proyección dedicada al checklist de ítems (estado por ítem: pendiente / medido / evaluado / omitido) ya fue especificada como futura en slices 1i e 1i'. Cuando se implemente, consumirá `MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1` e `ItemMonitoreoOmitido_v1`. El evento de este slice completa el catálogo de estados del ítem que esa proyección necesita.

Campos proyectados por `ItemMonitoreoOmitido_v1`: `ItemId → estado = Omitido`, `Motivo`, `OmitidoEn`.

---

## 9. Impacto en endpoints HTTP

**Endpoint:** `POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/omitir`

> Ruta conforme a `roadmap.md §3.36e`. `inspeccionId` e `itemId` viajan en el path. El body contiene únicamente el motivo.

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO:**

```json
{
  "motivo": "Sensor inaccesible por barro acumulado en el compartimento"
}
```

**Response 200 OK (happy path):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "itemId": 3,
  "motivo": "Sensor inaccesible por barro acumulado en el compartimento",
  "omitidoEn": "2026-05-08T09:00:00Z"
}
```

**Códigos de error:**

| Código HTTP | `codigoError` | Escenario |
|---|---|---|
| `400 Bad Request` | `"MOTIVO-VACIO"` | Motivo vacío o solo whitespace (PRE-3) |
| `400 Bad Request` | `"MOTIVO-LONGITUD"` | Motivo con menos de 10 chars (PRE-4) |
| `403 Forbidden` | `"PRE-1"` | Capability ausente |
| `404 Not Found` | — | InspeccionId no existe (PRE-2) |
| `404 Not Found` | `"I-M3"` | ItemId no en snapshot (PRE-7) |
| `409 Conflict` | `"I-M9"` | Ítem ya omitido previamente (PRE-9) |
| `422 Unprocessable Entity` | `"I-M1"` | Inspección es Tecnica (PRE-5) |
| `422 Unprocessable Entity` | `"I-M2"` | Inspección no en EnEjecucion (PRE-6) |
| `422 Unprocessable Entity` | `"I-M8"` | Ítem ya medido o ya evaluado (PRE-8) |

**Rol/permiso requerido:** capability `ejecutar-inspeccion`. Heredado del host PWA.

**Nota sobre HTTP status para PRE-7 (ítem no en snapshot):** se usa `404 Not Found` por coherencia con la semántica REST — el ítem no existe como recurso en el contexto de esta inspección. Los slices 1i e 1i' usaron `422` para esta condición. Se deja la decisión de HTTP status a `red`/`green` si prefieren alinear con los slices previos (`422`). Documentado aquí como `404` siguiendo la semántica más estricta.

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `ItemMonitoreoOmitido_v1` no genera push hacia el frontend según el catálogo vigente de ADR-005 (`01-modelo-dominio.md §14`). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). La omisión de un ítem es una operación local del técnico en su dispositivo — no hay otras partes que necesiten notificación en tiempo real.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `OmitirItemMonitoreo` no consume ni publica hacia el ERP. Trabaja exclusivamente con el aggregate cargado desde el stream de Marten (`ItemsSnapshot` fue persistido en `InspeccionIniciada_v1`). No hay llamadas a M-3b, M-16 ni a ningún otro endpoint ERP en este handler.

> El motivo de omisión es un dato de auditoría interno del módulo — no se sincroniza hacia el ERP en el MVP.

---

## 12. Preguntas abiertas

Cero items abiertos. Todas las preguntas previas fueron resueltas el 2026-05-08.

### P-1 — Numeración de invariantes (RESUELTA 2026-05-08)

**Decisión:** opción B. Se asignan números nuevos `I-M8` e `I-M9` para las invariantes de omisión de este slice. `I-M5b` del slice 1i' queda sin cambios (semánticas distintas: I-M5b = ítem numérico rechaza evaluación cualitativa; I-M8 = ítem ya procesado no puede omitirse; I-M9 = doble omisión rechazada). Sin impacto retroactivo sobre slices anteriores.

### P-2 — `EmitidoPor` en `ItemMonitoreoOmitido_v1` (RESUELTA 2026-05-08)

**Decisión:** confirmar. `EmitidoPor: string` añadido al evento `ItemMonitoreoOmitido_v1`. Patrón idéntico a `MedicionRegistrada_v1` (slice 1i) y `EvaluacionCualitativaRegistrada_v1` (slice 1i'). Se propone corrección al modelo §12.11.5 punto 5 en el PR de este slice.

---

## 13. Checklist pre-firma

- [x] **P-1 resuelta** — numeración invariantes: `I-M8` (ítem ya procesado, PRE-8, `422`) e `I-M9` (doble omisión, PRE-9, `409`). `I-M5b` del slice 1i' sin cambios. Decisión 2026-05-08.
- [x] **P-2 resuelta** — `EmitidoPor: string` confirmado en `ItemMonitoreoOmitido_v1`. Decisión 2026-05-08.
- [x] Todas las precondiciones (PRE-1..PRE-9) mapean a un escenario Given/When/Then en §6 (6.11→PRE-2, 6.6→PRE-3, 6.7→PRE-4, 6.8→PRE-5/I-M1, 6.10→PRE-6/I-M2, 6.9→PRE-7/I-M3, 6.4→PRE-8/I-M8 medido, 6.5→PRE-8/I-M8 evaluado, 6.3→PRE-9/I-M9).
- [x] Happy path presente (6.1 — un único evento, sin hallazgo).
- [x] Escenario de límite mínimo de motivo presente (6.2 — 10 chars exactos, válido).
- [x] Escenario de rebuild desde stream presente (6.14) — acotado al comando 1j por decisión del usuario.
- [x] Idempotencia decidida (§7): envelope dedup ADR-008 + PRE-9/I-M9 natural (`409 Conflict`).
- [x] §10 SignalR resuelto explícitamente ("no aplica").
- [x] §11 adapters Sinco resuelto explícitamente ("no aplica").
- [x] Preguntas abiertas: cero items pendientes. P-1 y P-2 resueltas.

---

## Notas de cierre para revisión humana

**Lo que este slice añade respecto al 1i':**

- Nuevo evento `ItemMonitoreoOmitido_v1(InspeccionId, ItemId, Motivo, EmitidoPor, OmitidoEn)` — shape definitivo con `EmitidoPor` confirmado y `DateTimeOffset OmitidoEn`.
- Nuevo método de decisión `Inspeccion.OmitirItem` — emite siempre un único evento, sin rama multi-evento.
- `Apply(ItemMonitoreoOmitido_v1)` puro: actualiza `_itemsOmitidos` + `Contribuyentes`.
- `HashSet<int> _itemsOmitidos` — el slice 1i ya lo referenciaba; este slice lo produce.
- Nueva excepción `ItemYaOmitidoException` (`409 Conflict`, código `I-M9`).
- Nueva excepción `ItemYaProcesadoException` (`422 Unprocessable Entity`, código `I-M8`).
- Nueva excepción `MotivoOmisionInvalidoException` (`400`, códigos `MOTIVO-VACIO` / `MOTIVO-LONGITUD`).
- Nuevas invariantes `I-M8` e `I-M9` añadidas al catálogo §15.3.
- Handler `OmitirItemMonitoreoHandler` en `Inspecciones.Application`.
- Endpoint `POST /api/v1/inspecciones/{id}/items/{itemId}/omitir` en `Inspecciones.Api`.

**Diferencia clave respecto a 1i e 1i':**

- Este slice **no emite hallazgo automático en ningún caso** — el método de decisión no tiene rama condicional hacia `HallazgoRegistrado_v1`.
- No extiende `HallazgoRegistrado_v1`.
- No requiere `HallazgoId` en el comando.

**Lo que NO hace este slice:**

- `FirmarInspeccion` con validación de completitud del checklist (¿todos los ítems deben tener registro antes de firmar?) — se modelará en el slice de `FirmarInspeccion` extendido para monitoreo si aplica.
- `AdjuntarArchivo` anclado a `ItemId` (roadmap 3.16i) — fuera de alcance.
- Tests end-to-end del flujo monitoreo completo (roadmap 3.16j = slice 1k).
- Proyecciones async `DetalleInspeccionView` e `ItemsMonitoreoView`.
