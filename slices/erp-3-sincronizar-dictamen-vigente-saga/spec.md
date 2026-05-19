# Slice erp-3 — SincronizarDictamenVigenteSaga

**Autor:** domain-modeler
**Fecha:** 2026-05-19
**Estado:** draft
**Agregado afectado:** ninguno — listener de integración puro. No escribe en ningún stream de dominio.
**Capa:** `Inspecciones.Infrastructure / Erp / Listeners`
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.4` — catálogo de 24 eventos; `InspeccionFirmada_v1` (evento trigger), `DictamenEstablecido_v1` (portador del dictamen). Ambos emitidos atomicamente por `FirmarInspeccion` (slice 1g).
- `01-modelo-dominio.md §16` — ADR-006: todo POST/PUT al ERP vía Wolverine outbox, backoff 5s→30s→2m→10m, dead-letter con señal de observabilidad.
- `06-contrato-apis-erp.md §3.2 M-W-1` — `PUT /api/v4/Maquinaria/api/equipos/{equipoCodigo}/dictamen-vigente`. Body: `{ Estado: int }`. 200 OK: `{ Codigo, Estado, EstadoUsuario, EstadoFecha }`. Idempotencia last-write-wins natural.
- `slices/1g-firmar-inspeccion/spec.md §3` — `InspeccionFirmada_v1` NO lleva `Dictamen` ni `EquipoCodigo`. El dictamen vive en `DictamenEstablecido_v1`; el `EquipoId` vive en el aggregate (proyectado desde `InspeccionIniciada_v1`). El listener debe reconstruir el aggregate para leer ambos.
- `slices/erp-2-descartar-novedad-preop-outbox/spec.md` — patrón de referencia: estructura del listener, INV-L1..L4, política de reintentos ADR-006, señal de observabilidad, service account JWT.
- `src/Inspecciones.Infrastructure/Erp/Dtos/ActualizarDictamenEquipoDtos.cs` — `ActualizarDictamenEquipoRequestDto { Estado: int }` (0=PuedeOperar, 1=ConRestriccion, 2=NoPuedeOperar). `ActualizarDictamenEquipoResponseDto { Codigo, Estado, EstadoUsuario, EstadoFecha }`.
- `src/Inspecciones.Infrastructure/Erp/IMaquinariaErpClient.cs` — `ActualizarDictamenEquipoAsync(int equipoCodigo, ActualizarDictamenEquipoRequestDto, CancellationToken)`.

---

## 1. Intención

Cuando una inspección técnica se firma, el dictamen de operación (`PuedeOperar | ConRestriccion | NoPuedeOperar`) representa la decisión técnica vigente sobre el equipo. El ERP Sinco MYE mantiene un campo "dictamen vigente" por equipo consultado por operaciones, mantenimiento y reportes para conocer el estado actual de la máquina.

Este slice propaga ese dictamen al ERP mediante `PUT /api/v4/Maquinaria/api/equipos/{equipoCodigo}/dictamen-vigente` (M-W-1) tan pronto como `InspeccionFirmada_v1` queda persistido en el event store. La operación es asíncrona (outbox Wolverine), de baja criticidad operacional para el cierre de la inspección, y naturalmente idempotente del lado del ERP (last-write-wins).

El listener **no emite eventos de dominio nuevos** ni modifica ningún stream de aggregate. Es un side-effect de integración de salida.

---

## 2. Comando

No hay comando nuevo. Este slice **no expone endpoint HTTP**. El trigger es el evento de dominio `InspeccionFirmada_v1` publicado por `FirmarInspeccionHandler` (slice 1g) al hacer `SaveChangesAsync`.

Wolverine suscribe el listener al mensaje `InspeccionFirmada_v1` mediante convención de handler discovery estándar del proyecto.

### Payload de entrada (evento del dominio, definido en slice 1g)

```
InspeccionFirmada_v1 {
    InspeccionId:  Guid
    FirmadoPor:    string         // TecnicoId opaco
    FirmaUri:      string         // URI del blob de firma
    UbicacionFirma: UbicacionGps
    FirmadaEn:     DateTimeOffset
}
```

> **Dato crítico:** `InspeccionFirmada_v1` NO lleva `Dictamen` ni `EquipoCodigo` ni `EquipoId`. El listener debe reconstruir el aggregate con `AggregateStreamAsync<Inspeccion>(evt.InspeccionId)` para obtener:
> - `aggregate.Dictamen` (poblado por `DictamenEstablecido_v1`, emitido antes de `InspeccionFirmada_v1` en el mismo `SaveChangesAsync` del handler 1g).
> - `aggregate.EquipoId` (int, poblado por `InspeccionIniciada_v1`).
>
> El código del equipo (`EquipoCodigo: string`) se resuelve desde el catálogo local Marten (`EquipoLocal` por `EquipoId`) — el adapter `ActualizarDictamenEquipoAsync` recibe `int equipoCodigo` según la firma actual en `IMaquinariaErpClient`, pero ver **D-1** sobre la discrepancia de tipo.

### Payload enviado a Maquinaria_V4 (M-W-1)

Construido por el listener a partir del aggregate reconstruido:

```
ActualizarDictamenEquipoRequestDto {
    Estado: int   // mapeo: PuedeOperar→0, ConRestriccion→1, NoPuedeOperar→2
}
```

**Mapeo de dominio a ERP:**

| `DictamenOperacion` (dominio) | `Estado` (ERP, `int`) | Semántica ERP |
|---|---|---|
| `PuedeOperar` | `0` | Equipo habilitado para operar sin restricciones |
| `ConRestriccion` | `1` | Equipo habilitado con restricciones operacionales |
| `NoPuedeOperar` | `2` | Equipo fuera de operación hasta corrección de fallas |

> El mapeo proviene del comentario en `ActualizarDictamenEquipoDtos.cs`: *"Estado: 0=puede operar, 1=con restricción, 2=no puede operar."*. Los valores de enum del dominio (`DictamenOperacion`) son cardinalmente equivalentes (mismo orden). Si MYE agrega un cuarto estado en el futuro, emerge `_v2` del DTO y se actualiza el mapeo sin tocar el dominio.

---

## 3. Evento(s) emitido(s)

Este slice **no emite eventos de dominio al stream del aggregate**. La decisión de dominio (firma + dictamen) ya ocurrió en el slice 1g. Este slice es exclusivamente integración de salida.

**Señal de observabilidad (sin stream de aggregate):**

| Señal | Publicado en | Cuándo |
|---|---|---|
| `DictamenVigenteErpSyncFallida_v1` | Log estructurado nivel `Error` + alerta operaciones | Si el listener agota reintentos y el mensaje pasa a dead-letter. |

`DictamenVigenteErpSyncFallida_v1` NO se persiste en el event store de Marten. Es una señal de observabilidad (métrica + alerta operacional) para el equipo de soporte. Ver §5 (INV-L2).

> **Justificación de no emitir al stream:** el aggregate `Inspeccion` ya tiene estado `Firmada` y `Dictamen` correcto. La firma es un hecho irreversible. Si la propagación al ERP falla, el estado de dominio sigue siendo correcto — solo falla la notificación a un sistema externo. Emitir un evento de "sync fallida" al stream del aggregate introduciría un estado `DictamenSyncFallida` que no existe en la máquina de estados del modelo (§2.1 del modelo de dominio). ADR-006 §16 establece este patrón de señal de observabilidad vs evento de dominio.

---

## 4. Precondiciones

Este slice opera **fuera del aggregate**. No hay precondiciones de estado del aggregate que evaluar — cuando Wolverine invoca el listener, `InspeccionFirmada_v1` ya está persistido de forma durable. El listener no puede fallar el stream principal.

Las condiciones que se verifican **dentro del listener** antes de llamar al ERP:

- **PRE-L1 (aggregate reconstruible):** `AggregateStreamAsync<Inspeccion>(evt.InspeccionId)` devuelve un aggregate no nulo con `Dictamen != null` y `EquipoId > 0`. Si el aggregate es nulo (stream no existe — indica bug en el handler 1g), el listener va a dead-letter inmediato con log `Critical`. Si `Dictamen == null` (estado corrupto — `DictamenEstablecido_v1` nunca se emitió), mismo tratamiento.
- **PRE-L2 (catálogo local disponible):** el `EquipoLocal` para `aggregate.EquipoId` existe en el catálogo Marten. Ver **D-1** — en el estado actual del adapter, `equipoCodigo` es `int` (el `EquipoId` del ERP) y no requiere resolución del catálogo. Si la firma del adapter cambia a `string`, esta precondición se vuelve relevante.
- **PRE-L3 (dictamen mapeable):** `aggregate.Dictamen` es un valor de `DictamenOperacion` mapeado en la tabla §2. Los tres valores actuales tienen mapeo definido. Si emerge un cuarto valor no mapeado: `ArgumentOutOfRangeException` → dead-letter inmediato (no reintentar — mapeo fijo en código).
- **PRE-L4 (adapter disponible):** `IMaquinariaErpClient` inyectado correctamente. Verificado por DI al arrancar — no falla en runtime normal.

> El listener **no puede** lanzar excepción que revierta el evento de dominio. La atomicidad outbox + stream garantizada por ADR-006 significa que `InspeccionFirmada_v1` y el mensaje outbox quedaron juntos en la misma transacción Marten del handler 1g. Si el listener falla, Wolverine reintenta desde el outbox; nunca revierte el evento de dominio.

---

## 5. Invariantes tocadas

No se tocan invariantes del aggregate. El listener no modifica estado del aggregate.

Invariantes de la capa de integración (extensión del patrón INV-L1..L4 de ADR-006 §16):

- **INV-L1 (atomicidad outbox):** el mensaje al outbox de Wolverine debe encolarse en la misma transacción (`SaveChangesAsync`) que persiste `InspeccionFirmada_v1` en el stream. Si el mensaje no está en el outbox, la llamada al ERP nunca ocurrirá. Garantizado por el patrón Wolverine + Marten con transacción compartida en el handler 1g. El listener erp-3 no tiene que hacer nada especial — Wolverine lo recibe del outbox.
- **INV-L2 (fallo visible, no silencioso):** si el listener agota reintentos, la situación debe ser observable. No se permite fallo silencioso. El listener emite `DictamenVigenteErpSyncFallida_v1` (log `Error` + métrica) antes de depositar en dead-letter. La inspección ya está firmada — el fallo de sync es de menor severidad que un fallo de OT, pero igual debe alertarse.
- **INV-L3 (no retry en 4xx):** respuestas `4xx` del ERP son errores permanentes. El listener NO reintenta; pasa directo a dead-letter + alerta. Un `400` indica mapeo de payload incorrecto (bug en el listener); un `404` indica equipo desconocido en MYE (anomalía operacional — si el equipo fue inspeccionado, debería existir en MYE).
- **INV-L4 (idempotencia natural del endpoint):** M-W-1 es last-write-wins. Un segundo PUT con el mismo body sobreescribe el mismo estado — no hay error, no hay efecto colateral indeseado. El listener trata cualquier `200 OK` como éxito, independientemente de si el estado ya era el mismo.
- **INV-L5 (race condition last-write-wins — tolerado):** si dos inspecciones para el mismo equipo se firman con dictámenes diferentes y el outbox entrega en orden distinto al orden de firma, el estado final en el ERP refleja la inspección entregada más tarde, no la más reciente según `FirmadaEn`. Este es el riesgo inherente de last-write-wins sin versioning en M-W-1. Ver **D-2** sobre mitigación por timestamp.

---

## 6. Escenarios Given / When / Then

> **Nota para `red`:** estos escenarios se testean con WireMock.Net (stub de M-W-1) y `AlbaHost` / `WolverineFixture` (bus de test). El aggregate se prepara en Marten real (Testcontainers Postgres) porque el listener hace `AggregateStreamAsync` — a diferencia del erp-2, aquí sí se necesita el event store. Ver §notas-red al final.

### 6.1 Happy path — dictamen PuedeOperar, ERP 200 OK

**Given**
- Stream `inspeccion-{id1}` en Marten contiene (en orden):
  - `InspeccionIniciada_v1` (EquipoId=1234)
  - `HallazgoRegistrado_v1` (h1, NoRequiereIntervencion)
  - `DiagnosticoEmitido_v1` ("Inspección sin hallazgos críticos")
  - `DictamenEstablecido_v1` (Dictamen=PuedeOperar)
  - `InspeccionFirmada_v1` (InspeccionId=id1, FirmadoPor="tecnico-01", FirmadaEn=T)
- WireMock stubbea `PUT /api/equipos/1234/dictamen-vigente` → 200 OK con body `{ "Codigo": 1234, "Estado": 0, "EstadoUsuario": 0, "EstadoFecha": "2026-05-19T15:00:00Z" }`.

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id1 }` al listener.

**Then**
- El listener reconstruye el aggregate: `Dictamen=PuedeOperar`, `EquipoId=1234`.
- El adapter recibe exactamente 1 llamada `PUT /api/equipos/1234/dictamen-vigente` con body `{ "Estado": 0 }`.
- El listener completa sin excepción.
- No se emite señal de observabilidad de fallo.

---

### 6.2 Happy path — dictamen ConRestriccion

**Given**
- Stream con `DictamenEstablecido_v1` (Dictamen=ConRestriccion) + `InspeccionFirmada_v1` (EquipoId=5678).
- WireMock stubbea `PUT /api/equipos/5678/dictamen-vigente` → 200 OK.

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id2 }` al listener.

**Then**
- Body enviado: `{ "Estado": 1 }`.
- El listener completa sin excepción.

---

### 6.3 Happy path — dictamen NoPuedeOperar

**Given**
- Stream con `DictamenEstablecido_v1` (Dictamen=NoPuedeOperar) + `InspeccionFirmada_v1` (EquipoId=9012).
- WireMock stubbea `PUT /api/equipos/9012/dictamen-vigente` → 200 OK.

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id3 }` al listener.

**Then**
- Body enviado: `{ "Estado": 2 }`.
- El listener completa sin excepción.

---

### 6.4 Idempotencia — segundo PUT al mismo equipo con mismo dictamen (replay del outbox)

**Given**
- El equipo 1234 ya tiene dictamen `Estado=0` en MYE (PUT previo exitoso).
- WireMock stubbea `PUT /api/equipos/1234/dictamen-vigente` → 200 OK (last-write-wins, sin error).

**When**
- Wolverine entrega el mismo `InspeccionFirmada_v1 { InspeccionId: id1 }` al listener por segunda vez (replay del outbox).

**Then**
- El adapter recibe exactamente 1 llamada HTTP (el segundo mensaje es el replay).
- El listener completa sin excepción.
- No se emite señal de fallo.

> **Justificación:** M-W-1 es last-write-wins — el ERP sobreescribe el estado con el mismo valor. El listener no necesita estado propio de "ya procesé esta inspección". La idempotencia natural del endpoint es suficiente.

---

### 6.5 ERP responde 5xx — reintento con backoff ADR-006

**Given**
- WireMock stubbea `PUT /api/equipos/1234/dictamen-vigente` → 500 en los primeros 3 intentos, luego 200 OK.

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id1 }` al listener.

**Then**
- Wolverine reintenta con backoff (5s → 30s → 2m → 10m según ADR-006).
- Tras el éxito en el 4to intento, el listener completa sin excepción.
- El adapter recibió exactamente 4 llamadas HTTP.
- No se emite señal de observabilidad de fallo.

---

### 6.6 ERP responde 5xx persistente — agota reintentos, dead-letter + alerta

**Given**
- WireMock stubbea `PUT /api/equipos/1234/dictamen-vigente` → 500 en todos los intentos (ERP caído).

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id1 }` y agota la política de reintentos (4 intentos).

**Then**
- El mensaje pasa a dead-letter queue de Wolverine.
- Se emite señal de observabilidad `DictamenVigenteErpSyncFallida_v1` (log estructurado nivel `Error` con `InspeccionId`, `EquipoId`, `Dictamen`, `IntentosAgotados=4`, `UltimoError="500 Internal Server Error"`).
- El aggregate `Inspeccion` no se modifica (ya tenía estado `Firmada` + `Dictamen` correcto).
- La inspección queda usable para otras sagas (`CerrarInspeccionSaga`, `GenerarOTSaga`).

---

### 6.7 ERP responde 4xx (400 Bad Request) — no reintentar, dead-letter + alerta

**Given**
- WireMock stubbea `PUT /api/equipos/1234/dictamen-vigente` → 400 Bad Request con body `{ "Codigo": "ESTADO_INVALIDO", "Mensaje": "El valor de Estado no es admitido" }`.

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id1 }` al listener.

**Then**
- El listener detecta `4xx` → NO reintenta (INV-L3).
- Dead-letter inmediato.
- Señal de observabilidad con `CodigoErp="ESTADO_INVALIDO"`, `EsReintentable=false`.
- El adapter recibió exactamente 1 llamada HTTP.

---

### 6.8 ERP responde 404 Not Found — equipo desconocido en MYE, dead-letter + alerta

**Given**
- WireMock stubbea `PUT /api/equipos/9999/dictamen-vigente` → 404 Not Found (equipo no existe en MYE).

**When**
- Wolverine entrega `InspeccionFirmada_v1 { InspeccionId: id4, EquipoId: 9999 }` al listener.

**Then**
- `4xx` → NO reintenta (INV-L3).
- Dead-letter + señal de observabilidad con `EquipoId=9999`, `EsReintentable=false`.
- El adapter recibió exactamente 1 llamada HTTP.

> **Nota operacional:** un 404 en este contexto es anómalo. Si el equipo fue inspeccionado, debe existir en el catálogo MYE local (de lo contrario `IniciarInspeccion` habría fallado en PRE del handler 1b). Un 404 de MYE indica desincronización entre el catálogo local y el ERP, o un equipo dado de baja en MYE después de la inspección. Requiere atención manual del equipo de soporte — la alerta es suficiente para MVP.

---

### 6.9 Aggregate no reconstruible (PRE-L1) — dead-letter inmediato

**Given**
- `InspeccionFirmada_v1 { InspeccionId: id-inexistente }` — el stream no existe en Marten (indica bug en el handler 1g o mensaje corrupto).

**When**
- Wolverine entrega el evento al listener.

**Then**
- PRE-L1 falla: `AggregateStreamAsync` devuelve `null` → dead-letter inmediato sin reintentos.
- Log nivel `Critical`: "Listener erp-3: stream no encontrado para InspeccionId {id}. Posible bug en handler 1g."
- No se llama al adapter HTTP.

---

### 6.10 Dictamen nulo en aggregate (PRE-L1 — estado corrupto)

**Given**
- Stream con `InspeccionIniciada_v1` + `InspeccionFirmada_v1` sin `DictamenEstablecido_v1` intermedio (estado corrupto — no debería ocurrir si slice 1g emite los 3 eventos atómicamente).

**When**
- Wolverine entrega `InspeccionFirmada_v1` al listener.

**Then**
- PRE-L1 falla: `aggregate.Dictamen == null` → dead-letter inmediato.
- Log nivel `Critical`: "Listener erp-3: Dictamen nulo en aggregate {InspeccionId}. El stream está corrupto (faltan eventos de slice 1g)."
- No se llama al adapter HTTP.

---

### 6.11 Dictamen no mapeable (PRE-L3) — dead-letter inmediato

**Given**
- El aggregate tiene un valor de `DictamenOperacion` que no cubre la tabla de mapeo (solo posible si se agrega un cuarto valor al enum sin actualizar el listener — escenario de bug de evolución).

**When**
- El listener intenta mapear el dictamen al entero ERP.

**Then**
- `ArgumentOutOfRangeException` (o equivalente) → dead-letter inmediato sin reintentos.
- No se llama al adapter HTTP.

---

## 7. Idempotencia / retries

**Idempotencia del listener (nivel Wolverine):**

Wolverine garantiza entrega "at-least-once". El mismo `InspeccionFirmada_v1` puede llegar al listener más de una vez (retry desde outbox, replay manual). El listener es naturalmente idempotente porque M-W-1 es last-write-wins:

- Un segundo PUT con el mismo `Estado` sobreescribe con el mismo valor — inocuo.
- El listener no necesita estado propio de "ya procesé esta inspección". No persiste ninguna marca de idempotencia.

**Race condition last-write-wins (INV-L5 — tolerado en MVP):**

Si dos inspecciones del mismo equipo se firman concurrentemente y el outbox las entrega en orden invertido respecto a `FirmadaEn`, el estado final en MYE reflejará el dictamen de la última entrega, no de la última firma. En operación normal esto es improbable (dos inspecciones del mismo equipo en paralelo están prohibidas por la invariante I1b del modelo — no puede haber dos `EnEjecucion` para el mismo equipo). El riesgo es mínimo; la mitigación formal (agregar `FirmadaEn` al body para que MYE descarte writes obsoletos) se marca como D-2 y se evalúa según respuesta del equipo MYE.

**Sin `Idempotency-Key` formal hacia el ERP:**

El DTO actual `ActualizarDictamenEquipoRequestDto` solo contiene `Estado: int`. No hay campo `Idempotency-Key` en el body ni en headers documentado por M-W-1. Dado que el endpoint es last-write-wins, múltiples PUTs con el mismo body son inocuos. Si el equipo MYE solicita un header de idempotencia, la clave propuesta es `{InspeccionId}` (ADR-003 + §3.2 del contrato). Ver D-2.

**Política de reintentos (ADR-006 §16):**

| Intento | Espera | Acción si falla |
|---|---|---|
| 1 (inmediato) | 0s | → |
| 2 | 5s | → |
| 3 | 30s | → |
| 4 | 2m | → |
| 5 | 10m | Dead-letter + señal `DictamenVigenteErpSyncFallida_v1` |

La política se configura mediante `RetryNow()` + `PauseFor()` en `WolverineOptions`. El rol `green` debe declarar la política explícitamente — no confiar en defaults de Wolverine para dead-letter.

---

## 8. Impacto en proyecciones / read models

Este slice no emite eventos de dominio. No impacta proyecciones Marten.

La señal de observabilidad `DictamenVigenteErpSyncFallida_v1` es un log estructurado — no es un evento Marten y no alimenta proyecciones. Si en el futuro se necesita una vista "equipos cuyo sync de dictamen falló" para el panel admin, se construye como proyección separada sobre ese evento de observabilidad — cambio aditivo que no bloquea este slice.

**No impacta ninguna proyección existente.**

---

## 9. Impacto en endpoints HTTP

**No aplica.** Este slice no expone ningún endpoint HTTP. Es un listener interno de Wolverine. La interacción con el ERP es exclusivamente saliente (el módulo llama a MYE, no al revés).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica.** La actualización del dictamen vigente en el ERP es un efecto de integración de fondo — no genera notificación push al frontend. El técnico ya recibió confirmación visual de la firma mediante la respuesta `200 OK` del endpoint `POST /api/v1/inspecciones/{id}/firmar` (slice 1g). Las notificaciones push relevantes (`OTGenerada`, `InspeccionCerradaSinOT`) son responsabilidad de `CerrarInspeccionSaga` y `GenerarOTSaga`, no de este listener.

---

## 11. Impacto en adapters Sinco on-prem

**Aplica — es el núcleo de este slice.**

- **Endpoint Sinco consumido:** `PUT /api/v4/Maquinaria/api/equipos/{equipoCodigo}/dictamen-vigente` (módulo: MYE núcleo / Maquinaria_V4, slice 11 de Maquinaria_V4).
- **Método del adapter:** `IMaquinariaErpClient.ActualizarDictamenEquipoAsync(int equipoCodigo, ActualizarDictamenEquipoRequestDto, CancellationToken)`.
- **Estado de disponibilidad:** 🚧 bloqueado (endpoint no existe aún en Maquinaria_V4 / equipo MYE no se ha comprometido). El módulo trabaja contra WireMock en tests y en entorno de desarrollo.
- **Discrepancia de tipo del path param — D-1:** la firma del adapter usa `int equipoCodigo` pero el contrato en `06-contrato-apis-erp.md §3.2` dice `equipoCodigo` es `string` del catálogo MYE. La implementación actual en `MaquinariaErpClient` formatea el `int` como string en la URL (`equipoCodigo.ToString(CultureInfo.InvariantCulture)`). En práctica, si el catálogo MYE usa enteros como código de equipo, esto es consistente. Ver D-1.
- **Payload saliente:**
  ```json
  { "Estado": 0 }
  ```
- **Respuesta esperada 200 OK:**
  ```json
  {
    "Codigo": 1234,
    "Estado": 0,
    "EstadoUsuario": 0,
    "EstadoFecha": "2026-05-19T15:00:00Z"
  }
  ```
- **Matriz de respuestas del adapter:**

| Código ERP | Acción del listener | Reintentable |
|---|---|---|
| `200 OK` | Éxito — completa sin error | N/A |
| `404 Not Found` | Dead-letter + alerta (anomalía operacional — equipo desconocido) | No (permanente) |
| `400 Bad Request` | Dead-letter + alerta (bug en mapeo de payload) | No (permanente) |
| `409 Conflict` | Dead-letter + alerta (no esperado — M-W-1 es last-write-wins) | No (permanente) |
| `5xx` / timeout | Retry con backoff ADR-006 | Sí (hasta 4 reintentos) |

- **Auth (service account — decisión D-3, por analogía con erp-2):** cuando Wolverine ejecuta el listener de forma asíncrona desde el outbox, el contexto HTTP del request original ya no existe y el JWT del técnico no está disponible. El adapter usa service account / API key para la llamada a Maquinaria_V4 (autenticación machine-to-machine). Ver D-3.

---

## 12. Preguntas abiertas / decisiones

- **D-1 (pendiente de confirmar con David) — Tipo del `equipoCodigo` en M-W-1:** el contrato `06-contrato-apis-erp.md §3.2` dice `equipoCodigo` es `string`. La firma del adapter en `IMaquinariaErpClient` y la implementación en `MaquinariaErpClient` usan `int equipoCodigo` y lo formatean como `{int}.ToString()` en la URL. Esto asume que el "código" del equipo en MYE es numéricamente equivalente al `EquipoId` (int, PK del ERP) que guarda el aggregate.
  - **Si `equipoCodigo` es `int` (el `EquipoId`):** no hay problema — el adapter funciona como está. El listener lee `aggregate.EquipoId` directamente.
  - **Si `equipoCodigo` es un string alfanumérico (como "EXC-320D-014"):** el listener debe resolver `EquipoLocal.Codigo` desde el catálogo local por `aggregate.EquipoId`. La firma del adapter debe cambiar de `int` a `string`. Esto implica un cambio en `IMaquinariaErpClient` y `MaquinariaErpClient` — work para el rol `green`.
  - **Recomendación:** confirmar con David si el path param de M-W-1 es el id numérico del equipo o el código alfanumérico. Mientras se confirma, el spec asume `int` (el estado actual del adapter). Si cambia a `string`, es un cambio de interface no bloqueante para el spec.

- **D-2 (pendiente de confirmar con David) — Versioning/timestamp en M-W-1 para mitigar race condition:** el DTO actual `ActualizarDictamenEquipoRequestDto { Estado: int }` no incluye `FirmadaEn` ni `Version`. El contrato en `06-contrato-apis-erp.md §3.2` propone un body más rico (`dictamen: string`, `inspeccionOrigenId`, `firmadaEn`, `tecnicoFirmante`) que permitiría a MYE descartar writes obsoletos por timestamp.
  - **Si MYE implementa el body rico:** el listener debe incluir `FirmadaEn = evento.FirmadaEn` y `TecnicoFirmante = evento.FirmadoPor` en el request. Requiere actualizar `ActualizarDictamenEquipoRequestDto`.
  - **Si MYE mantiene solo `Estado: int` (estado actual del DTO):** la race condition last-write-wins queda tolerada (probabilidad mínima por I1b del dominio — no puede haber dos inspecciones `EnEjecucion` del mismo equipo simultáneamente).
  - **Recomendación:** solicitar a David que MYE acepte al menos `FirmadaEn` para permitir lógica de "ignorar si ya tengo un dictamen más reciente". Mientras se confirma, el spec y el DTO funcionan con `Estado: int` solo.

- **D-3 (service account JWT) — Confirmado por analogía con erp-2:** el adapter usa service account / API key para la llamada a Maquinaria_V4 desde el listener asíncrono (mismo razonamiento que D-3 del slice erp-2). El JWT del técnico no está disponible en el contexto del outbox. Si el equipo MYE no tiene mecanismo de service account aún, usar el JWT serializado en el mensaje outbox como fallback temporal con advertencia de expiración (JWTs ≤1h; retry de 10m puede que funcione; retry de 30m puede que no). **Marcar 🚧 pendiente de confirmar con David + equipo Seguridad/IT.**

---

## 13. Checklist pre-firma

- [x] §1 Intención: describe claramente el propósito del listener y su alcance (solo side-effect de integración de salida).
- [x] §2 Trigger de entrada (`InspeccionFirmada_v1`) especificado con payload; necesidad de `AggregateStreamAsync` documentada y justificada.
- [x] §2 Payload saliente al ERP (`ActualizarDictamenEquipoRequestDto { Estado: int }`) con mapeo completo `DictamenOperacion → int`.
- [x] §3 Decisión de no emitir eventos de dominio justificada con referencia a ADR-006 y máquina de estados del aggregate.
- [x] §4 Precondiciones del listener (PRE-L1..L4) documentadas; separación clara de capa de dominio.
- [x] §5 Invariantes de integración (INV-L1..L5) propuestas; INV-L5 documenta la race condition y su tolerancia.
- [x] §6 Escenarios cubiertos: happy path x3 dictámenes, idempotencia (replay), 5xx con retry, 5xx persistente + dead-letter, 4xx 400 + 404 sin retry, aggregate no reconstruible, dictamen nulo, dictamen no mapeable.
- [x] §7 Política de reintentos ADR-006 explicitada (5s→30s→2m→10m, 4 intentos, dead-letter).
- [x] §7 Idempotencia ante at-least-once delivery de Wolverine documentada.
- [x] §7 Race condition last-write-wins documentada con evaluación de probabilidad.
- [x] §8 Proyecciones: no aplica, justificado.
- [x] §9 Endpoints HTTP: no aplica (listener interno).
- [x] §10 SignalR: no aplica, justificado.
- [x] §11 Adapter Sinco: aplica, detallado con matriz de respuestas completa y estado 🚧.
- [ ] §12 Preguntas abiertas: D-1 (tipo de `equipoCodigo`) y D-2 (versioning timestamp) requieren confirmación de David antes de que `red` escriba los tests de integración. D-3 por analogía con erp-2, pero pendiente confirmación Seguridad/IT.

**El spec puede firmarse con D-1, D-2 y D-3 como pendientes.** Los tests de `red` se pueden escribir con el comportamiento actual del adapter (`int equipoCodigo`, `Estado: int` solo). Si David confirma cambios en D-1 o D-2, `red`/`green` actualizan antes del commit del slice.

---

## Notas para `red` (no forman parte del spec de dominio)

**Framework de tests recomendado:**

1. **WireMock.Net** para stubbear `PUT /api/v4/Maquinaria/api/equipos/{equipoCodigo}/dictamen-vigente`. Crear una `WireMockServer` en el setup del test y configurar `IMaquinariaErpClient` para apuntarle.

2. **Testcontainers Postgres + Marten** — a diferencia de erp-2, este listener hace `AggregateStreamAsync`. Los tests deben escribir el stream en Marten real (o usar un doble de `IQuerySession` que devuelva el aggregate preconstruido). La opción más robusta es Testcontainers para evitar acoplamiento a la implementación de `AggregateStreamAsync`.

3. **AlbaHost / WolverineFixture** para ejecutar el listener en un bus de test. El evento `InspeccionFirmada_v1` se publica con `IMessageBus.PublishAsync` y el test espera la completación antes de hacer asserts.

4. **Para §6.5 y §6.6 (retry):** WireMock puede configurar respuestas secuenciales (`InScenario`). El test necesita controlar el `TimeProvider` o usar `RetryPolicy` con delay 0 en test (Wolverine permite override en `WolverineOptions`).

5. **Naming de tests en español, frase completa:**
   - `SincronizarDictamenVigenteSaga_dictamen_PuedeOperar_envia_Estado_0`
   - `SincronizarDictamenVigenteSaga_dictamen_ConRestriccion_envia_Estado_1`
   - `SincronizarDictamenVigenteSaga_dictamen_NoPuedeOperar_envia_Estado_2`
   - `SincronizarDictamenVigenteSaga_replay_outbox_es_inocuo_last_write_wins`
   - `SincronizarDictamenVigenteSaga_erp_5xx_persistente_va_a_dead_letter`
   - `SincronizarDictamenVigenteSaga_erp_4xx_no_reintenta_va_a_dead_letter`
   - `SincronizarDictamenVigenteSaga_erp_404_equipo_desconocido_dead_letter`
   - `SincronizarDictamenVigenteSaga_aggregate_no_encontrado_dead_letter_inmediato`
   - `SincronizarDictamenVigenteSaga_dictamen_nulo_dead_letter_inmediato`

6. **Ubicación sugerida:** `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/SincronizarDictamenVigenteSagaTests.cs`.

7. **Nombre del listener propuesto:** `SincronizarDictamenVigenteSagaListener`. Ubicación: `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteSagaListener.cs`.
