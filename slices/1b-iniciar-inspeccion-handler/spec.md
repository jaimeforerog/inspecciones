# Slice 1b — IniciarInspeccionHandler + InspeccionAbiertaPorEquipoView

**Autor:** domain-modeler
**Fecha:** 2026-05-06
**Estado:** draft (depende de slice 1a cerrado — ver `feat(slice-1a)` `fa1323a` + `e1c9ae0`)
**Split:** este slice depende de `slices/1a-iniciar-inspeccion-aggregate/` cerrado en green/refactor/review (cierre administrativo `e1c9ae0`). Se desarrolla como sub-slice independiente para aislar plumbing fundacional (configuración Marten del aggregate, proyección `InspeccionAbiertaPorEquipoView` con índice único Postgres, integración Wolverine + outbox + envelope dedup, endpoint HTTP `POST /api/v1/inspecciones`).
**Agregado afectado:** `Inspeccion` (consume el método `Iniciar` del slice 1a) + proyección nueva `InspeccionAbiertaPorEquipoView`.
**Decisiones previas relevantes:**
- `slices/1a-iniciar-inspeccion-aggregate/spec.md` — comando, payload del evento, precondiciones PRE-3..PRE-7, value objects, contrato HTTP en §9 (referencia preservada para este slice).
- `01-modelo-dominio.md §15.12.6` — `InspeccionAbiertaPorEquipoView` con PK `EquipoId`, eventos consumidos (`InspeccionIniciada_v1`, `InspeccionFirmada_v1`, `InspeccionCancelada_v1`), unique index Postgres sobre `data->>'EquipoId'`.
- `01-modelo-dominio.md §15.7` — invariante I-I1 (una sola inspección abierta por equipo) con defensa dual: validación blanda en handler + constraint duro en Postgres.
- `01-modelo-dominio.md §12.11.1` — resolución handler `IniciarInspeccion` lee `Equipo.RutinaTecnicaId` desde `EquipoLocal` (M-3b) sin selector — el técnico no elige.
- ADR-002 (`§9.11`) — capa HTTP recibe claims del host PWA; el handler los recibe por parámetro como `ClaimsTecnico`.
- ADR-004 (`§9.15` + refinamientos 2026-05-05) — `EquipoLocal` y `RutinaTecnicaLocal` se consultan vía `IDocumentSession` como read-only de catálogos sincronizados (M-3b, M-17).
- ADR-006 (`§16` modelo) — outbox transaccional Wolverine + Marten en una sola transacción `SaveChangesAsync()`. No aplica directo (este comando no hace `POST` a Sinco) pero sí garantiza atomicidad evento + proyección + envelope dedup.
- ADR-008 (`§9.16` + refinamientos 2026-05-05) — `clientCommandId` UUIDv7 viaja como `MessageId` Wolverine para idempotencia end-to-end; envelope TTL=30 días.

---

## 1. Intención

Exponer el comando `IniciarInspeccion` como caso de uso ejecutable end-to-end: orquestar I-I1 (validación blanda + defensa dura concurrente), resolver catálogos `EquipoLocal` y `RutinaTecnicaLocal` desde Marten, invocar el método `Inspeccion.Iniciar` del slice 1a, persistir `InspeccionIniciada_v1` atómicamente con la proyección `InspeccionAbiertaPorEquipoView` y devolver el resultado al cliente vía endpoint HTTP `POST /api/v1/inspecciones`.

Si el equipo ya tiene una inspección activa (otro técnico colaborando, sesión re-tapeada, race concurrente perdida), el handler **no falla**: retorna la `InspeccionId` existente con `redirigeAExistente=true` para que el frontend abra la inspección activa.

## 2. Comando

Mismo `IniciarInspeccion` definido en slice 1a §2. Sin cambios en el payload del comando ni en `ClaimsTecnico`. Este slice agrega:

- **DTO de entrada HTTP** (`IniciarInspeccionRequest`) que se mapea al record `IniciarInspeccion` en la capa API.
- **DTO de salida HTTP** (`IniciarInspeccionResponse`) con `InspeccionId`, `RedirigeAExistente`, `Version`, opcional `Mensaje`.
- **Resultado del handler** (`IniciarInspeccionResult`) con la misma forma del DTO de salida — el handler no toca HTTP, devuelve un record que la capa API serializa.

```csharp
public sealed record IniciarInspeccionResult(
    Guid InspeccionId,
    bool RedirigeAExistente,
    long Version,                          // versión del stream tras Append (1 si nuevo, N si redirige)
    string? Mensaje);                      // null en happy path; "Ya hay inspección activa..." si redirige
```

## 3. Evento(s) emitido(s)

`InspeccionIniciada_v1` (definido en slice 1a) cuando el handler **no** corto-circuita por I-I1. Cuando sí corto-circuita (fila ya existe en `InspeccionAbiertaPorEquipoView`), **ningún evento** se emite y el handler devuelve la `InspeccionId` existente con `RedirigeAExistente=true`.

## 4. Precondiciones (adicionales a las del slice 1a)

Las precondiciones PRE-3..PRE-7 del slice 1a se evalúan en el método de decisión del aggregate (no se duplican aquí). Este slice agrega las que viven **antes** de invocar el aggregate:

- **PRE-1 (capa HTTP):** capability `ejecutar-inspeccion` heredada del host PWA (ADR-002 tentativo). Excepción: `403 Forbidden` antes de invocar el handler. La capa HTTP lee claims del contexto inyectado por el host; el handler recibe `ClaimsTecnico` por parámetro.
- **PRE-2 (capa HTTP):** `ProyectoId ∈ claims.ProyectosAsignados`. Excepción: `ProyectoNoAutorizadoException` (`403 Forbidden`).
- **PRE-3 (handler):** `EquipoLocal` con `EquipoId` debe existir tras sync (ADR-004). El handler hace `session.Query<EquipoLocal>().Where(e => e.EquipoId == cmd.EquipoId).SingleOrDefaultAsync()`. Cache stale extrema (>7 días) bloquea según ADR-004 punto 3 (refinamientos 2026-05-05) — la lógica de staleness vive en un middleware/decorator del catálogo, no en este handler. Excepción: `EquipoNoEncontradoException` (`404 Not Found`).
- **PRE-handler-1:** `RutinaTecnicaLocal[EquipoLocal.RutinaTecnicaId]` resuelve (consultado por el handler antes de invocar el aggregate; si no existe, propaga `RutinaTecnicaNoSincronizadaException` antes de tocar el stream). Esta resolución alimenta los campos `RutinaId` y `RutinaCodigo` que el handler pasa al método de decisión del aggregate.
- **PRE-handler-2 (I-I1 blanda):** consultar `InspeccionAbiertaPorEquipoView` por `EquipoId`. Si hay fila → corto-circuito (no emite evento, devuelve `RedirigeAExistente=true`).

> **Capa donde viven:** PRE-1 y PRE-2 en la capa HTTP (autorización endpoint); PRE-3 + PRE-handler-1 + PRE-handler-2 en el handler (`IniciarInspeccionHandler.ManejarAsync`); PRE-4..PRE-7 (del slice 1a) en el método de decisión del aggregate (`Inspeccion.Iniciar`). PRE-1 y PRE-2 ya las cubre el aggregate del 1a también como defensa en profundidad — no se quita esa cobertura.

## 5. Invariantes tocadas

- **I-I1** Una sola inspección abierta por equipo. **Defensa dual (decisión 2026-04-30, refrendada 2026-05-05):**
  - **Validación blanda** en handler — lee `InspeccionAbiertaPorEquipoView` por `EquipoId` antes de invocar el aggregate; corto-circuita si hay fila.
  - **Defensa dura** en Postgres — `CREATE UNIQUE INDEX ix_inspeccion_abierta_equipo_unique ON mt_doc_inspeccionabiertaporequipoview (data->>'EquipoId')`. Race condition: el segundo `SaveChangesAsync` falla con unique violation; el handler reintenta, ahora ve la fila y devuelve `RedirigeAExistente=true`.

> I-I2, I-I3 ya están cubiertas por el aggregate del 1a — este slice no las re-evalúa. La proyección `InspeccionAbiertaPorEquipoView` no es un agregado y no tiene invariantes propios; es read model + constraint Postgres.

## 6. Escenarios Given / When / Then

### 6.1 Happy path end-to-end

**Given**
- `EquipoLocal` con `EquipoId=4521`, `ProyectoId=3`, `RutinaTecnicaId=18` poblado (sync M-3b).
- `RutinaTecnicaLocal[18]` poblado con `Codigo="INSP. BULL.MOTOR"`, `Tipo=TipoRutina.Tecnica` (sync M-17).
- `InspeccionAbiertaPorEquipoView` vacía para `EquipoId=4521`.
- `claims` con `TieneCapabilityEjecutarInspeccion=true`, `ProyectosAsignados={1,2,3}`, `TecnicoIniciador="ana.gomez"`.
- `MessageId=X` único (no procesado antes por Wolverine envelope dedup).

**When**
- Capa API recibe `POST /api/v1/inspecciones` con header `X-Client-Command-Id: X` y body válido. Mapea a `IniciarInspeccion(cmd)` + `ClaimsTecnico(claims)` y llama `IniciarInspeccionHandler.ManejarAsync(cmd, claims, ct)`.

**Then**
- Un `InspeccionIniciada_v1` queda persistido en `mt_events` con `Tipo=Tecnica`, `EquipoId=4521`, `RutinaId=18`, `RutinaCodigo="INSP. BULL.MOTOR"`, `TecnicoIniciador="ana.gomez"`, `ProyectoId=3`, `Ubicacion`, `IniciadaEn` desde `TimeProvider`, `FechaReportada` del comando, lecturas tal cual.
- `InspeccionAbiertaPorEquipoView` tiene una fila con `EquipoId=4521`, `InspeccionId=cmd.InspeccionId`, `ProyectoId=3`, `TecnicoIniciador="ana.gomez"`, `IniciadaEn` consistente con el evento.
- Wolverine `wolverine.envelope_storage` tiene una entrada con `MessageId=X` y la respuesta serializada.
- Handler retorna `IniciarInspeccionResult(InspeccionId=cmd.InspeccionId, RedirigeAExistente=false, Version=1, Mensaje=null)`.
- Capa API devuelve `201 Created`, header `Location: /api/v1/inspecciones/{InspeccionId}`, body con el result.

### 6.2 I-I1 shortcut — equipo con activa retorna existente

**Given**
- `InspeccionAbiertaPorEquipoView` ya tiene fila para `EquipoId=4521` con `InspeccionId=Y` (preexistente, otra sesión o intento previo del mismo técnico que ya commiteó).
- `claims` válidos. `MessageId=Z` distinto del que originó la activa.

**When**
- Handler ejecuta `ManejarAsync(cmd, claims, ct)` con `cmd.EquipoId=4521` (`cmd.InspeccionId=W`, ignorado en este path).

**Then**
- Ningún evento nuevo se emite (`mt_events` para el stream `W` permanece vacío; el stream `Y` tampoco se modifica).
- Handler retorna `IniciarInspeccionResult(InspeccionId=Y, RedirigeAExistente=true, Version=Nactual, Mensaje="Ya hay inspección activa, abriendo la existente")`.
- Capa API devuelve `200 OK` (no `201`) con el body del result.

### 6.3 I-I1 race condition concurrente — pierde el segundo en unique violation

**Given**
- `InspeccionAbiertaPorEquipoView` aún sin fila para `EquipoId=4521`.
- Dos comandos simultáneos `cmd_A(InspeccionId=Wa, EquipoId=4521, MessageId=Xa)` y `cmd_B(InspeccionId=Wb, EquipoId=4521, MessageId=Xb)` invocan el handler en paralelo.
- Ambos pasan la validación blanda (read model stale para B).

**When**
- Ambos `SaveChangesAsync()` se ejecutan en paralelo.

**Then**
- Uno gana (digamos A): persiste `InspeccionIniciada_v1` en stream `Wa` + fila en `InspeccionAbiertaPorEquipoView` con `InspeccionId=Wa`.
- El otro (B) recibe `Marten.Exceptions.MartenCommandException` envolviendo `Npgsql.PostgresException` con `SqlState=23505` (unique violation sobre `ix_inspeccion_abierta_equipo_unique`).
- Handler de B atrapa la excepción específica, **reintenta una vez** consultando la proyección — ahora ve la fila con `InspeccionId=Wa`, retorna `IniciarInspeccionResult(InspeccionId=Wa, RedirigeAExistente=true, Version=1, Mensaje="Ya hay inspección activa, abriendo la existente")`.
- Verificación: exactamente un `InspeccionIniciada_v1` persistido en el event store para `EquipoId=4521`. Stream `Wb` queda vacío.
- Capa API devuelve `200 OK` para B (no `201`, no `409`).

### 6.4 Idempotencia del cliente — replay con mismo `clientCommandId`

**Given**
- Ya se ejecutó exitosamente el comando con `MessageId=X`. Wolverine envelope storage tiene la entrada con la respuesta original (`InspeccionId=W`, `RedirigeAExistente=false`, `Version=1`).
- El cliente reintenta tras timeout: reenvía el mismo `MessageId=X`.

**When**
- Handler recibe el mensaje (Wolverine lo procesa como reentry).

**Then**
- Wolverine envelope dedup detecta `MessageId=X` ya procesado y devuelve la respuesta original sin re-aplicar el handler.
- El stream `W` mantiene un solo `InspeccionIniciada_v1` (no se duplicó).
- `InspeccionAbiertaPorEquipoView` no se reescribe.
- Capa API devuelve `200 OK` (Wolverine documenta `200` en replay; no `201` porque ya no es creación nueva) con el mismo body original.

### 6.5 PRE-3 equipo no encontrado en catálogo

**Given**
- `EquipoLocal` no tiene fila para `EquipoId=99999`.

**When**
- Handler ejecuta `ManejarAsync(cmd, claims, ct)` con `cmd.EquipoId=99999`.

**Then**
- Lanza `EquipoNoEncontradoException` con mensaje "Equipo {EquipoId=99999} no encontrado en catálogo local. Refresca catálogos."
- Sin evento emitido, sin fila en proyección.
- Capa API devuelve `404 Not Found` con body `{ "codigoError": "PRE-3", "mensaje": "..." }`.

### 6.6 PRE-handler-1 rutina referenciada no sincronizada

**Given**
- `EquipoLocal[4521].RutinaTecnicaId=18`.
- `RutinaTecnicaLocal[18]` no existe (cache stale o admin del ERP eliminó la rutina sin propagar).

**When**
- Handler ejecuta `ManejarAsync(cmd, claims, ct)`.

**Then**
- Lanza `RutinaTecnicaNoSincronizadaException` con mensaje "Rutina técnica referenciada por el equipo no está sincronizada — refresca catálogos."
- Sin evento, sin fila en proyección.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-I2", "mensaje": "..." }`.

> **Por qué la valida el handler y no el aggregate:** el aggregate del slice 1a recibe `RutinaId` y `RutinaCodigo` ya resueltos como parámetros del método `Iniciar`. La consulta al catálogo `RutinaTecnicaLocal` es responsabilidad del handler (capa de orquestación, acceso a `IDocumentSession`). Si la rutina falta, el handler no puede construir los argumentos del aggregate — falla antes.

### 6.7 PRE-2 proyecto no autorizado (defensa en profundidad)

**Given**
- `claims.ProyectosAsignados = {1, 2}`, `cmd.ProyectoId = 99`.

**When**
- Capa HTTP llama al handler (asumiendo que el filtro de capability dejó pasar el request).

**Then**
- Handler delega al aggregate del 1a, que lanza `ProyectoNoAutorizadoException` (PRE-2 del 1a §6.4).
- Sin evento, sin fila.
- Capa API devuelve `403 Forbidden`.

> Cubierto principalmente por filtro HTTP antes de llegar al handler; este escenario verifica la defensa en profundidad cuando el filtro está bypass por test.

### 6.8 Wolverine outbox + Marten — atomicidad evento + proyección + envelope

**Given**
- Mismo contexto que 6.1.

**When**
- El handler ejecuta `Iniciar` y termina con un único `SaveChangesAsync()` que comprende: (a) Append de `InspeccionIniciada_v1` al stream, (b) upsert en `InspeccionAbiertaPorEquipoView` (proyección inline), (c) write en `wolverine.envelope_storage` con la respuesta.

**Then**
- Las tres escrituras son atómicas: si cualquiera falla, ninguna persiste.
- Verificación: forzar fallo en el upsert de la proyección (mockeando una unique violation manual) → `mt_events` queda sin el evento, `wolverine.envelope_storage` queda sin la respuesta, handler propaga la excepción.

> Este escenario lo cubre un test de integración con Marten + Postgres real (Testcontainers); no es un test puro del handler.

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

- El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico tap "Iniciar". Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine.
- Wolverine envelope dedup detecta replays: el mismo `clientCommandId` reenviado tras timeout devuelve `200 OK` con la respuesta original sin reaplicar.
- TTL del envelope = 30 días en MVP.

**Idempotencia natural por I-I1:**

- Independiente del envelope dedup. Si el cliente reenvía con un `clientCommandId` distinto pero el equipo ya tiene activa, el handler corto-circuita (escenario 6.2) y devuelve la `InspeccionId` existente. Comportamiento idempotente desde el punto de vista del usuario aunque no desde el envelope.

**Defensa concurrente:** índice único parcial Postgres sobre `data->>'EquipoId'` en `mt_doc_inspeccionabiertaporequipoview` (ver §15.12.6 modelo). Race con dos comandos sobre el mismo equipo: solo uno persiste (escenario 6.3); el perdedor reintenta y devuelve `RedirigeAExistente=true`.

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica directo. El outbox transaccional de Wolverine sí está activo para garantizar atomicidad evento + proyección + envelope dedup en un único commit (escenario 6.8).

## 8. Impacto en proyecciones / read models

### 8.1 `InspeccionAbiertaPorEquipoView` — proyección inline NUEVA

**Forma de la proyección (resolución de la pregunta §12 que estaba abierta en el draft previo):**

Se materializa como **`MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>`** registrada en `Marten.Events.Projections` como `ProjectionLifecycle.Inline`. La PK del documento es `EquipoId` (`int`), distinta del stream-id (`InspeccionId: Guid`), por lo cual `SingleStreamProjection<TDoc>` no aplica (correlaciona stream-id ↔ doc-id 1:1).

Ventajas de `MultiStreamProjection`:
- Soporta PK distinta del stream-id vía `Identity<TEvent>(e => e.EquipoId)`.
- Marten orquesta el lifecycle completo cuando lleguen los eventos terminales en slices futuros (`InspeccionFirmada_v1`, `InspeccionCancelada_v1` → delete) sin que cada handler tenga que recordar borrar la fila — lógica centralizada en la proyección.
- Registrable como `Inline`: ejecuta en la misma transacción del Append. Atomicidad evento + proyección + envelope (escenario 6.8).
- Marten v7 maneja delete vía `IEvent` API (`projection.Identity<TEvent>(...)` + `projection.DeleteEvent<TEvent>()`).

Descartado:
- **`SingleStreamProjection<TDoc>`** — requiere que el doc-id sea el stream-id; aquí no se cumple.
- **`session.Store(view)` directo en el handler** — atómico también, pero descentraliza el lifecycle: cada handler que produzca evento terminal (firma, cancelación) tendría que recordar `session.Delete<InspeccionAbiertaPorEquipoView>(equipoId)`. Esa duplicación es exactamente lo que `MultiStreamProjection` evita. Además rompe la convención §15.12.7 ("cada proyección declara explícitamente eventos consumidos").

**Esquema y eventos consumidos** (§15.12.6):

```csharp
public sealed record InspeccionAbiertaPorEquipoView(
    int EquipoId,                  // PK Marten (Identity en MultiStreamProjection)
    Guid InspeccionId,
    string TecnicoIniciador,
    DateTime IniciadaEn,
    int ProyectoId);
```

| Evento | Acción |
|---|---|
| `InspeccionIniciada_v1` | Upsert con `EquipoId` como PK |
| `InspeccionFirmada_v1` | Delete (no aplica en este slice — slice futuro) |
| `InspeccionCancelada_v1` | Delete (no aplica en este slice — slice futuro) |

**Constraint Postgres** (declarado en migración Marten):

```sql
CREATE UNIQUE INDEX ix_inspeccion_abierta_equipo_unique
    ON mt_doc_inspeccionabiertaporequipoview (data->>'EquipoId');
```

(Cubre toda la tabla — la fila solo existe mientras la inspección está `EnEjecucion`; no requiere filtrado parcial. Aclaración respecto al draft previo: `WHERE Estado='EnEjecucion'` no aplica porque la fila no tiene campo `Estado` — su existencia en la tabla **es** la afirmación de "EnEjecucion".)

### 8.2 Proyecciones async fuera de alcance

`BandejaTecnicoView` (§15.12.3) y `DetalleInspeccionView` (§15.12.1) son proyecciones async (no requieren atomicidad con el commit del evento). Slices independientes posteriores. No se tocan en 1b.

### 8.3 Catálogos consumidos (read-only)

`EquipoLocal`, `RutinaTecnicaLocal`. Sincronizados via ADR-004 (sync on-app-open + ETag canonical, refinamientos 2026-05-05). El handler los consulta vía `IDocumentSession`; no los modifica. Si están vacíos por sync no ejecutado, los escenarios 6.5 y 6.6 cubren el comportamiento.

## 9. Impacto en endpoints HTTP

`POST /api/v1/inspecciones` — endpoint expuesto por este slice (definición íntegra; reemplaza la nota "fuera del alcance de 1a" del slice anterior).

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido, cualquier UUID válido aceptado).
- `Authorization` heredado del host PWA (ADR-002 tentativo; el host inyecta claims).

**Request DTO (`IniciarInspeccionRequest`):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "equipoId": 4521,
  "proyectoId": 3,
  "ubicacionInicio": {
    "latitud": 4.711,
    "longitud": -74.072,
    "precisionMetros": 8.5,
    "capturadoEn": "2026-05-06T08:30:12-05:00"
  },
  "fechaReportada": "2026-05-06",
  "lecturaMedidorPrimario": { "tipo": "Hr", "valor": 4523.5, "capturadoEn": "2026-05-06T08:30:10-05:00" },
  "lecturaMedidorSecundario": null
}
```

> **Decisión adicional:** `inspeccionId` viaja en el body desde el cliente (UUIDv7 generado client-side). La regla CLAUDE.md ("`Guid.NewGuid()` solo en handlers; el dominio recibe el id desde fuera") sigue cumplida — el handler **acepta** el `inspeccionId` del comando. Si el cliente no lo envía, el handler genera con `Guid.NewGuid()` (fallback). El diseño preferido es client-generates para que el cliente conozca la URL del recurso desde antes del request (UX offline + retries).

**Response 201 Created (inicio nuevo, escenario 6.1):**
- Header `Location: /api/v1/inspecciones/{inspeccionId}`.
- Body:
  ```json
  {
    "inspeccionId": "0193a4f7-...",
    "redirigeAExistente": false,
    "version": 1,
    "mensaje": null
  }
  ```

**Response 200 OK (redirige a existente, escenarios 6.2 y 6.3):**
- Body:
  ```json
  {
    "inspeccionId": "<existente>",
    "redirigeAExistente": true,
    "version": 1,
    "mensaje": "Ya hay inspección activa, abriendo la existente"
  }
  ```

**Response 200 OK (replay idempotente, escenario 6.4):**
- Body idéntico al original (Wolverine replay).

**Response 422 Unprocessable Entity (escenarios 6.6 + violaciones PRE-4..PRE-7 del 1a):**
- Body: `{ "codigoError": "I-I2" | "I-I3" | "PRE-4" | "PRE-5" | "PRE-6" | "PRE-7", "mensaje": "..." }`.

**Response 403 Forbidden (PRE-1 + PRE-2, escenario 6.7):**
- Body: `{ "codigoError": "PRE-1" | "PRE-2", "mensaje": "..." }`.

**Response 404 Not Found (PRE-3, escenario 6.5):**
- Body: `{ "codigoError": "PRE-3", "mensaje": "Equipo {id} no encontrado en catálogo local. Refresca catálogos." }`.

**Rol/permiso requerido:** capability `ejecutar-inspeccion` con asignación al `proyectoId` del request (heredado del host PWA).

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `InspeccionIniciada_v1` no genera push hacia el frontend en el catálogo vigente de ADR-005 (`§14`, post refinamiento 2026-05-05). El push está reservado para eventos de cierre y fallo (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`).

> Cuando emerja la necesidad multi-técnico colaborando ("ver inspecciones nuevas en bandeja del proyecto en tiempo real"), se agrega un evento push `InspeccionEstadoCambiado` con audiencia `Group=proyectoId`. Followup posible para slice posterior; no bloquea este slice.

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `IniciarInspeccion` no consume ni publica hacia el ERP. Trabaja exclusivamente con read models locales (`EquipoLocal`, `RutinaTecnicaLocal`, `InspeccionAbiertaPorEquipoView`). Los catálogos se llenan vía sync (ADR-004) gestionado por slices independientes.

## 12. Preguntas abiertas

- [x] **Forma de la proyección `InspeccionAbiertaPorEquipoView`** — resuelto en §8.1: `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>` registrada `Inline`. Razón: PK distinta del stream-id, lifecycle multi-evento centralizado, atomicidad con el Append.
- [x] **`inspeccionId` lo genera cliente o servidor** — resuelto en §9: cliente preferido (UUIDv7 client-side); fallback `Guid.NewGuid()` en handler si el body no lo trae. Compatible con CLAUDE.md ("`Guid.NewGuid()` solo en handlers; el dominio recibe el id desde fuera").
- [x] **`X-Client-Command-Id` requerido vs opcional** — resuelto: requerido (ya lo cerró slice 1a §12; ADR-008 lo establece como contrato vinculante).
- [x] **Reintento ante unique violation Postgres** — resuelto en escenario 6.3: el handler atrapa `MartenCommandException` envolviendo `PostgresException` con `SqlState=23505` y reintenta una vez (consulta proyección, retorna `RedirigeAExistente=true`). No hay loop ni backoff — un solo reintento determinístico.
- [x] **Aclaración sobre `WHERE Estado='EnEjecucion'` en el índice** — resuelto en §8.1: el índice cubre toda la tabla; la fila solo existe mientras la inspección está activa, no requiere filtrado parcial. Se corrige la mención del draft previo.

## 13. Checklist pre-firma

- [x] Slice 1a cerrado (commit `e1c9ae0` cierre administrativo + `fa1323a` aggregate puro). Verificable con `git log --oneline | grep slice-1a`.
- [x] Decisión sobre forma de la proyección tomada (§8.1: `MultiStreamProjection<TDoc, int>` inline).
- [x] Cada precondición agregada por este slice (PRE-1, PRE-2, PRE-3, PRE-handler-1, PRE-handler-2) tiene escenario Then en §6 (PRE-1+PRE-2 → 6.7; PRE-3 → 6.5; PRE-handler-1 → 6.6; PRE-handler-2 → 6.2 + 6.3).
- [x] La invariante I-I1 con defensa dual mapea a escenarios 6.2 (shortcut blando) y 6.3 (defensa dura concurrente).
- [x] Happy path end-to-end presente (6.1).
- [x] Atomicidad evento + proyección + envelope cubierta (6.8).
- [x] Idempotencia de cliente cubierta (6.4).
- [x] Endpoint HTTP descrito íntegro en §9 (request/response, todos los códigos de error).
- [x] §10 SignalR y §11 Sinco on-prem resueltos explícitamente como "no aplica".
- [x] Preguntas abiertas todas respondidas.
- [ ] **Firma del usuario pendiente** — al firmar, el slice pasa a `red`.

---

## Notas de cierre para revisión humana

**Lo que este slice añade respecto al 1a:**
- Handler `IniciarInspeccionHandler` (en `Inspecciones.Application`).
- Proyección `InspeccionAbiertaPorEquipoView` (en `Inspecciones.Application` o `Inspecciones.Infrastructure`, según convención del repo — el `green` decide).
- Endpoint HTTP `POST /api/v1/inspecciones` (en `Inspecciones.Api`).
- Configuración Marten: registro del aggregate `Inspeccion` como event-sourced + registro de la proyección `MultiStreamProjection` inline + migración del unique index.
- Configuración Wolverine: handler discovery + outbox transaccional + envelope dedup TTL=30 días.
- Tests de integración: handler con Marten embebido (Testcontainers Postgres), endpoint con `WebApplicationFactory<Program>`, escenario de race condition concurrente (6.3) requiere infra real.

**Lo que NO hace este slice:**
- Endpoint `GET /api/v1/inspecciones/{id}` (slice independiente de read models).
- Proyecciones async `BandejaTecnicoView` y `DetalleInspeccionView`.
- Lógica de staleness de catálogo >7 días (decorator/middleware del catálogo, slice independiente).
- Comandos hermanos (`RegistrarLecturaMedidor`, etc.).
