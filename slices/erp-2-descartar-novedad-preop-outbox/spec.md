# Slice erp-2 — DescartarNovedadPreop → outbox a Maquinaria_V4 P-6

**Autor:** domain-modeler
**Fecha:** 2026-05-19
**Estado:** draft
**Agregado afectado:** ninguno — este slice no escribe en ningún stream de dominio. Es un listener de integración puro.
**Capa:** `Inspecciones.Infrastructure / Erp / Listeners`
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §16` — ADR-006: todo POST al ERP vía Wolverine outbox, backoff 5s→30s→2m→10m, dead-letter con evento de observabilidad.
- `06-contrato-apis-erp.md §3.1 P-6` — `POST /api/v4/Maquinaria/api/preoperacional-fallas/cerrar`. Body: `{ podIds: [int], observaciones: string }`. 200 OK: `{ cerradasAhora, yaCerradas, total, podIdsCerradosAhora }`. Idempotencia natural: un mismo `PODId` cerrado dos veces devuelve `yaCerradas=1`, no error. 🚧 path y contrato pendientes de firma cross-team con equipo Preop.
- `slices/1n-descartar-novedad-preop/spec.md §11` — el slice 1n declaró explícitamente que la llamada a P-6 es out-of-scope y la delegó a este slice de integración.
- `src/Inspecciones.Infrastructure/Erp/IMaquinariaErpClient.cs` — método `CerrarPreoperacionalFallasAsync(CerrarPreoperacionalFallasRequestDto, CancellationToken)` ya implementado.
- `src/Inspecciones.Infrastructure/Erp/Dtos/CerrarPreoperacionalFallasDtos.cs` — `CerrarPreoperacionalFallasRequestDto { PodIds: IReadOnlyList<int>, Observaciones: string }` y `CerrarPreoperacionalFallasResponseDto { CerradasAhora, YaCerradas, Total, PodIdsCerradosAhora }`.
- Nota de reconciliación 2026-05-13 (comentario en el DTO): "Idempotencia natural: cierre único y definitivo de cada POD. No requiere `Idempotency-Key`. Reabrir el caso unitario invocando con `PodIds=[N]` (reconciliación bilateral 2026-05-13, P-5 descartado)."

---

## 1. Intención

Cuando el técnico descarta una novedad preoperacional desde la PWA, el aggregate emite `NovedadPreopDescartada_v1` y la inspección avanza — pero el ERP Preop (Maquinaria_V4) aún tiene la novedad como pendiente para el operador. Este slice cierra esa brecha: un listener Wolverine reacciona al evento `NovedadPreopDescartada_v1` ya persistido y llama a `POST /preoperacional-fallas/cerrar` (P-6) para que la novedad salga del flujo del operador en el ERP.

El listener **no emite eventos de dominio nuevos** ni modifica ningún stream de aggregate. Su responsabilidad es exclusivamente: construir el payload para el ERP, ejecutar la llamada vía el adapter `IMaquinariaErpClient`, y manejar las respuestas con la política de resiliencia de ADR-006.

---

## 2. Comando

No hay comando nuevo. Este slice **no expone endpoint HTTP**. El trigger de entrada es el evento de dominio `NovedadPreopDescartada_v1` publicado por el handler `DescartarNovedadPreopHandler` (slice 1n) al hacer `SaveChangesAsync`.

Wolverine suscribe el listener al mensaje `NovedadPreopDescartada_v1` mediante la convención de handler discovery estándar del proyecto.

### Payload de entrada (evento del dominio, definido en slice 1n)

```
NovedadPreopDescartada_v1 {
    InspeccionId:   Guid
    NovedadId:      int          // = PODId en Maquinaria_V4
    MotivoDescarte: string       // "Cerrado por {usuario} el {fecha} UTC desde Inspecciones"
    DescartadoPor:  string       // userId opaco del técnico
    DescartadaEn:   DateTimeOffset
}
```

### Payload enviado a Maquinaria_V4 (P-6)

Construido por el listener a partir del evento:

```
CerrarPreoperacionalFallasRequestDto {
    PodIds:       [NovedadPreopDescartada_v1.NovedadId]   // array de 1 en MVP
    Observaciones: NovedadPreopDescartada_v1.MotivoDescarte
}
```

**Mapeo de campos:**
- `PodIds` = `[evento.NovedadId]` — el campo `Id` de `PreoperacionalFallaErpDto` mapea al `PODId` de `EQV4.PreoperacionalFallas`; el modelo de dominio lo nombra `NovedadId`.
- `Observaciones` = `evento.MotivoDescarte` — el motivo autogenerado ("Cerrado por {usuario} el {fecha} UTC desde Inspecciones") es el campo `observaciones` del ERP; sirve de registro de auditoría para el operador.

---

## 3. Evento(s) emitido(s)

Este slice **no emite eventos de dominio al stream del aggregate**. La decisión de dominio (descarte) ya ocurrió en el slice 1n; este slice es exclusivamente integración de salida.

**Evento de observabilidad (sin stream de aggregate):**

| Evento | Publicado en | Cuándo |
|---|---|---|
| `NovedadPreopErpCierreFallido_v1` | Log estructurado + alerta operaciones (no en stream Marten) | Si el listener agota reintentos y el mensaje pasa a dead-letter. |

`NovedadPreopErpCierreFallido_v1` NO se persiste en el event store. Es una señal de observabilidad (métrica + alerta) para el equipo de operaciones. Ver §5 (INV-L2).

> **Justificación de no emitir al stream:** el descarte ya ocurrió; el estado del aggregate es correcto. El fallo de integración es un problema de infraestructura/ERP, no un cambio de estado de dominio. Si se emitiera un evento al stream, el aggregate entraría en un estado "cierreFallidoErp" que no existe en la máquina de estados del modelo (§2.1). ADR-006 §16 confirma este patrón para `OTGeneracionFallida_v1` (que sí tiene estado porque la OT aún no existe) — para el caso del descarte preop, el estado del aggregate ya es final; solo falla la notificación al ERP.

---

## 4. Precondiciones

Este slice opera **fuera del aggregate**. No hay precondiciones de estado del aggregate que evaluar — cuando Wolverine invoca el listener, el evento `NovedadPreopDescartada_v1` ya está persistido de forma durable. El listener no puede fallar el stream principal.

Las condiciones que sí se verifican **dentro del listener** antes de llamar al ERP:

- **PRE-L1** (evento bien formado): `NovedadId > 0` y `MotivoDescarte` no nulo/vacío. Si falla → log de error + dead-letter inmediato (no reintentar mensajes corruptos). No es esperado en operación normal; indica un bug en el handler 1n.
- **PRE-L2** (adapter disponible): `IMaquinariaErpClient` inyectado correctamente. Verificado por DI al arrancar — no falla en runtime normal.

> **Importante:** el listener NO puede lanzar una excepción que haga rollback del evento de dominio. El evento ya está en el stream. La atomicidad garantizada por ADR-006 es la siguiente: evento de dominio + mensaje outbox quedan juntos en la misma transacción de Marten. Si el listener falla, Wolverine lo reintenta desde el outbox — no revierte el evento.

---

## 5. Invariantes tocadas

No se tocan invariantes del aggregate (el listener no modifica estado del aggregate).

Invariantes de la capa de integración (propuestas para documentar en §16 de ADR-006 como extensión):

- **INV-L1 (atomicidad outbox):** el mensaje al outbox de Wolverine debe encolarse en la misma transacción (`SaveChangesAsync`) que persiste `NovedadPreopDescartada_v1`. Si el mensaje no está en el outbox, la llamada al ERP nunca ocurrirá. Garantizado por el patrón de Wolverine + Marten con transacción compartida. No requiere código adicional si el handler 1n usa `IMessageBus.PublishAsync` dentro del mismo `IDocumentSession`.
- **INV-L2 (fallo visible, no silencioso):** si el listener agota reintentos, la situación debe ser observable (alerta, log estructurado, métrica). No se permite fallo silencioso. El listener emite `NovedadPreopErpCierreFallido_v1` como señal de observabilidad antes de depositar en dead-letter.
- **INV-L3 (no retry en 4xx):** respuestas `4xx` del ERP (excepto el caso especial de idempotencia tratado en §7) son errores permanentes. El listener NO reintenta; pasa directo a dead-letter + alerta. Reintentar un `400 Bad Request` es inútil y genera ruido.
- **INV-L4 (idempotencia natural del endpoint):** la respuesta 200 del ERP con `yaCerradas >= 1` para un `PODId` ya cerrado debe tratarse como éxito equivalente a `cerradasAhora >= 1`. El listener no distingue entre "cerrado ahora" y "ya estaba cerrado" — ambos son éxito. Ver §7.

---

## 6. Escenarios Given / When / Then

> **Nota para `red`:** estos escenarios se testean con WireMock (para el stub del ERP) y `WolverineFixture` / `AlbaHost` (para el bus de mensajes). No se requiere aggregate en memoria — el listener recibe el evento directamente. Ver §notas-red al final de este spec.

### 6.1 Happy path — ERP responde 200 OK (cerrada ahora)

**Given**
- Listener configurado con `IMaquinariaErpClient` que apunta a WireMock.
- WireMock stubbea `POST /api/v4/Maquinaria/api/preoperacional-fallas/cerrar` → 200 OK con body `{ cerradasAhora: 1, yaCerradas: 0, total: 1, podIdsCerradosAhora: [9001] }`.

**When**
- Wolverine entrega `NovedadPreopDescartada_v1 { InspeccionId: id1, NovedadId: 9001, MotivoDescarte: "Cerrado por ana.gomez el 2026-05-19 10:00 UTC desde Inspecciones", DescartadoPor: "ana.gomez", DescartadaEn: T }` al listener.

**Then**
- El adapter recibe exactamente 1 llamada HTTP a `/api/v4/Maquinaria/api/preoperacional-fallas/cerrar`.
- Body de la llamada: `{ "podIds": [9001], "observaciones": "Cerrado por ana.gomez el 2026-05-19 10:00 UTC desde Inspecciones" }`.
- El listener completa sin excepción.
- No se emite ningún evento de observabilidad de fallo.

---

### 6.2 Idempotencia — ERP responde 200 OK con `yaCerradas: 1` (ya estaba cerrado)

**Given**
- El `PODId=9001` ya fue cerrado en una llamada anterior (Wolverine retry o segunda entrega del outbox).
- WireMock stubbea `POST .../cerrar` → 200 OK con body `{ cerradasAhora: 0, yaCerradas: 1, total: 1, podIdsCerradosAhora: [] }`.

**When**
- Wolverine entrega el mismo `NovedadPreopDescartada_v1 { NovedadId: 9001 }` al listener por segunda vez.

**Then**
- El listener completa sin excepción (trata `yaCerradas >= 1` como éxito).
- No se emite evento de fallo.
- El estado del aggregate no cambia (ya era correcto).

> **Justificación:** El DTO `CerrarPreoperacionalFallasResponseDto.YaCerradas >= 1` con `Total == 1` indica que el endpoint es naturalmente idempotente — el ERP no falla al recibir un `PODId` ya cerrado, solo lo cuenta diferente. Nota reconciliación 2026-05-13 del DTO lo confirma.

---

### 6.3 ERP responde 5xx — reintento con backoff

**Given**
- WireMock stubbea `POST .../cerrar` → 500 Internal Server Error en los primeros 3 intentos, luego 200 OK.

**When**
- Wolverine entrega `NovedadPreopDescartada_v1 { NovedadId: 9001 }` al listener.

**Then**
- Wolverine reintenta con backoff (5s → 30s → 2m → 10m según ADR-006).
- Tras el éxito en el 4to intento, el listener completa sin excepción.
- El adapter recibió exactamente 4 llamadas HTTP.
- No se emite evento de observabilidad de fallo.

---

### 6.4 ERP responde 5xx persistente — agota reintentos, dead-letter + alerta

**Given**
- WireMock stubbea `POST .../cerrar` → 500 Internal Server Error en todos los intentos (simula ERP caído).

**When**
- Wolverine entrega `NovedadPreopDescartada_v1 { NovedadId: 9001 }` al listener y agota la política de reintentos (4 intentos).

**Then**
- El mensaje pasa a dead-letter queue de Wolverine.
- Se emite señal de observabilidad `NovedadPreopErpCierreFallido_v1` (log estructurado nivel `Error` con `InspeccionId`, `NovedadId`, `IntentosAgotados=4`, `UltimoError="500 Internal Server Error"`).
- El aggregate `Inspeccion` no se modifica (el evento de dominio ya estaba persistido).
- No se lanza excepción no controlada que crashee el proceso.

---

### 6.5 ERP responde 4xx (400 Bad Request) — no reintentar, dead-letter + alerta

**Given**
- WireMock stubbea `POST .../cerrar` → 400 Bad Request con body `{ "Codigo": "PAYLOAD_INVALIDO", "Mensaje": "observaciones no puede estar vacío" }`.

**When**
- Wolverine entrega `NovedadPreopDescartada_v1 { NovedadId: 9001, MotivoDescarte: "" }` al listener.
  > Este escenario solo ocurre si PRE-L1 no capturó el motivo vacío (defensa en profundidad).

**Then**
- El listener detecta `4xx` → NO reintenta (INV-L3).
- El mensaje pasa directo a dead-letter.
- Se emite señal de observabilidad con `Codigo="PAYLOAD_INVALIDO"`, `EsReintentable=false`.
- El adapter recibió exactamente 1 llamada HTTP.

---

### 6.6 ERP responde 404 Not Found — no reintentar, dead-letter + alerta

**Given**
- WireMock stubbea `POST .../cerrar` → 404 Not Found (el `PODId` no existe en el ERP).

**When**
- Wolverine entrega `NovedadPreopDescartada_v1 { NovedadId: 99999 }` al listener.

**Then**
- `4xx` → NO reintenta (INV-L3).
- Dead-letter + señal de observabilidad con `EsReintentable=false`, detalle del 404.
- El adapter recibió exactamente 1 llamada HTTP.

> **Nota:** un 404 en este contexto puede indicar que el técnico descartó una novedad que no existía en el ERP (opción A de la decisión D-2 del slice 1n — la novedad era "fantasma"). La alerta permite al equipo de operaciones detectar y diagnosticar el root cause.

---

### 6.7 ERP responde 409 Conflict — tratado como éxito (idempotencia)

**Given**
- WireMock stubbea `POST .../cerrar` → 409 Conflict con body `{ "Codigo": "YA_CERRADO", "Mensaje": "La novedad ya fue cerrada" }`.
  > Aunque el DTO actual indica idempotencia natural (200 con `yaCerradas`), el ERP podría devolver 409 en implementaciones alternativas o durante transición. El listener debe ser robusto.

**When**
- Wolverine entrega `NovedadPreopDescartada_v1 { NovedadId: 9001 }` al listener.

**Then**
- El listener detecta `409` con `Codigo == "YA_CERRADO"` → trata como éxito (equivalente a `yaCerradas=1`).
- No se reintenta, no se emite alerta.
- El listener completa sin excepción.

> **Decisión D-1:** el listener interpreta `409 YA_CERRADO` como idempotencia natural del ERP, no como error. Esto protege contra variaciones de implementación del lado Preop durante el período de integración. Si el 409 contiene un `Codigo` distinto (conflicto real entre inspecciones), se trata como `4xx` permanente y va a dead-letter.

---

### 6.8 Evento malformado (PRE-L1) — dead-letter inmediato

**Given**
- `NovedadPreopDescartada_v1 { NovedadId: 0, MotivoDescarte: null }` — evento con campos inválidos (indica bug en handler 1n).

**When**
- Wolverine entrega el mensaje al listener.

**Then**
- PRE-L1 falla → dead-letter inmediato (sin reintentos).
- Log de error con nivel `Critical` indicando evento malformado y stacktrace.
- No se llama al adapter HTTP.

---

## 7. Idempotencia / retries

**Idempotencia del listener (nivel Wolverine):**

Wolverine garantiza entrega "at-least-once". El mismo mensaje `NovedadPreopDescartada_v1` puede llegar al listener más de una vez (retry desde outbox, replay manual desde dead-letter). El listener debe ser idempotente:

- Si el ERP ya cerró el `PODId` (respuesta 200 con `yaCerradas >= 1` o 409 `YA_CERRADO`): el listener termina con éxito. No emite alerta. El outbox se marca como completado.
- El listener no persiste estado propio de "ya procesé este NovedadId" — confía en la idempotencia natural del endpoint P-6 del ERP (confirmada en DTO + nota 2026-05-13).

**Política de reintentos (ADR-006 §16):**

| Intento | Espera | Acción si falla |
|---|---|---|
| 1 (inmediato) | 0s | → |
| 2 | 5s | → |
| 3 | 30s | → |
| 4 | 2m | → |
| 5 | 10m | Dead-letter + señal `NovedadPreopErpCierreFallido_v1` |

> Wolverine configura esta política mediante `RetryNow()` + `PauseFor()` en el `WolverineOptions`. El rol `green` debe declarar la política explícitamente en la configuración DI — no confiar en defaults de Wolverine para dead-letter.

**Sin `Idempotency-Key` hacia el ERP:**

La nota de reconciliación 2026-05-13 en `CerrarPreoperacionalFallasDtos.cs` documenta que el endpoint es naturalmente idempotente (cierre único definitivo por POD). No se envía `Idempotency-Key` en el header — el ERP no lo requiere para este endpoint. Si en la integración real el equipo Preop solicita un header de idempotencia, la clave propuesta es `{InspeccionId}-{NovedadId}` (simple y derivable del evento sin estado adicional). Ver §12 Decisión D-2.

---

## 8. Impacto en proyecciones / read models

Este slice no emite eventos de dominio. No impacta proyecciones Marten.

La señal de observabilidad `NovedadPreopErpCierreFallido_v1` (§3) es un log estructurado — no es un evento Marten y no alimenta ninguna proyección. Si en el futuro se necesita una vista "novedades cuyo cierre ERP falló" para el panel admin, se construye como proyección separada sobre ese evento — cambio aditivo que no bloquea este slice.

---

## 9. Impacto en endpoints HTTP

**No aplica.** Este slice no expone ningún endpoint HTTP. Es un listener interno de Wolverine. La interacción con el ERP es saliente (el módulo llama al ERP, no al revés).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica.** El cierre del `PODId` en el ERP no genera notificación push al frontend. El técnico ya recibió confirmación visual de la acción de descarte al recibir el `200 OK` del endpoint HTTP del slice 1n. La integración con el ERP ocurre de fondo de forma asíncrona.

---

## 11. Impacto en adapters Sinco on-prem

**Aplica — es el núcleo de este slice.**

- **Endpoint Sinco consumido:** `POST /api/v4/Maquinaria/api/preoperacional-fallas/cerrar` (módulo: Preoperacional / Maquinaria_V4).
- **Método del adapter:** `IMaquinariaErpClient.CerrarPreoperacionalFallasAsync(CerrarPreoperacionalFallasRequestDto, CancellationToken)`.
- **Estado de disponibilidad:** 🚧 bloqueado (equipo Preop no ha implementado el endpoint). El módulo trabaja contra WireMock en tests y en entorno de desarrollo.
- **Contrato pendiente de firma cross-team:** path final (`/preoperacional-fallas/cerrar` vs `/preop/novedades/descartar`) 🚧 pendiente de confirmar con David (ver §12 Decisión D-2 abierta).
- **Payload saliente:**
  ```json
  {
    "podIds": [9001],
    "observaciones": "Cerrado por ana.gomez el 2026-05-19 10:00 UTC desde Inspecciones"
  }
  ```
- **Respuesta esperada 200 OK:**
  ```json
  {
    "cerradasAhora": 1,
    "yaCerradas": 0,
    "total": 1,
    "podIdsCerradosAhora": [9001]
  }
  ```
- **Matriz de respuestas del adapter:**

| Código ERP | Acción del listener | Reintentable |
|---|---|---|
| `200 OK` (`cerradasAhora >= 1` o `yaCerradas >= 1`) | Éxito — completa sin error | N/A |
| `409 YA_CERRADO` | Tratado como éxito (D-1) | No |
| `409` (otro código) | Dead-letter + alerta | No (permanente) |
| `404 Not Found` | Dead-letter + alerta | No (permanente) |
| `400 Bad Request` | Dead-letter + alerta | No (permanente) |
| `5xx` / timeout | Retry con backoff ADR-006 | Sí (hasta 4 reintentos) |

- **Auth:** el adapter propaga el JWT del host PWA vía `Authorization: Bearer {jwt}`. El listener lo extrae del mensaje de outbox (patrón establecido en el adapter existente). Ver §12 Decisión D-3 sobre propagación del JWT en mensajes outbox.

---

## 12. Preguntas abiertas / decisiones

Todas las preguntas están marcadas con una decisión propuesta. El orquestador debe confirmar antes de pasar a `red`.

- **D-1 (propuesta — idempotencia 409 YA_CERRADO): CONFIRMADO POR SPEC.** El listener trata `409` con `Codigo == "YA_CERRADO"` como éxito silencioso. Si el `Codigo` es diferente, va a dead-letter. Esta decisión protege contra implementaciones del ERP que retornan 409 en lugar de 200 idempotente.

- **D-2 (pendiente de confirmar con David) — Path del endpoint P-6:** el DTO actual en `CerrarPreoperacionalFallasDtos.cs` documenta el path como `POST /api/v4/Maquinaria/api/preoperacional-fallas/cerrar`, pero `06-contrato-apis-erp.md §3.1 P-6` dice `POST /api/v1/preop/novedades/descartar`. Son inconsistentes. El adapter `IMaquinariaErpClient` ya usa la forma del DTO. **Recomendación:** confiar en el DTO (`/preoperacional-fallas/cerrar`) porque fue escrito más recientemente (2026-05-13) y es el contrato implementado en el adapter real. El spec asume esta decisión hasta confirmación de David. Si el path cambia, solo se actualiza la configuración del `HttpClient` — no cambia la lógica del listener.

- **D-3 (decisión de implementación — JWT en outbox): El listener NO tiene acceso al JWT original.** Cuando Wolverine ejecuta el listener de forma asíncrona desde el outbox, el contexto HTTP del request original ya no existe. El JWT no está disponible para propagarlo al ERP. Opciones:
  - **(A — recomendada):** el adapter usa un **service account / API key** para la llamada a Maquinaria_V4 (autenticación machine-to-machine). El ERP confía en el módulo, no en el técnico individual. Para operaciones de escritura desde sagas/listeners, es el patrón correcto.
  - **(B):** incluir el JWT serializado en el mensaje outbox. Riesgo de expiración (JWTs típicos expiran en 1h; un retry de 10m puede que aún funcione, pero no uno de 30m+).
  - **(C):** el ERP no valida Auth en este endpoint (menos seguro).
  - **Recomendación:** opción A. Si el equipo Preop no tiene aún mecanismo de service account, usar opción B como fallback temporal con advertencia sobre expiración. Marcar como `🚧 pendiente de confirmar con David + equipo Seguridad/IT`.

- **D-4 (decisión de implementación — nombre del listener):** propuesto: `DescartarNovedadPreopErpListener`. Ubicación: `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs`. Si el proyecto tiene una convención diferente de naming para listeners de integración, usar esa.

- **D-5 (política de alertas — destinatarios dead-letter):** ADR-006 §16 menciona "notificación a destinatarios con capability `recibir-alertas-ot-fallida`". Para el descarte preop, la alerta debe ir a operaciones del módulo (equipo de soporte). El canal concreto (email, Teams, Application Insights alert rule) no está definido en el modelo. **Recomendación:** usar el mismo mecanismo que `OTGeneracionFallida_v1` — si no está implementado aún, un log `Critical` es suficiente para MVP. Marcar como seguimiento post-MVP.

---

## 13. Checklist pre-firma

- [x] §1 Intención: describe claramente el caso de uso del listener.
- [x] §2 Trigger de entrada (evento) y payload saliente al ERP especificados con mapeo de campos explícito.
- [x] §3 Decisión de no emitir eventos de dominio justificada con referencia a ADR-006.
- [x] §4 Precondiciones del listener (PRE-L1, PRE-L2) documentadas; separación clara de la capa de dominio.
- [x] §5 Invariantes de integración (INV-L1..INV-L4) propuestas para documentar en ADR-006.
- [x] §6 Escenarios cubiertos: happy path, idempotencia (200 `yaCerradas`, 409 `YA_CERRADO`), 5xx con retry, 5xx persistente + dead-letter, 4xx sin retry, evento malformado.
- [x] §7 Política de reintentos ADR-006 explicitada (5s→30s→2m→10m, 4 intentos, dead-letter).
- [x] §7 Idempotencia del listener ante at-least-once delivery de Wolverine documentada.
- [x] §7 Ausencia de `Idempotency-Key` justificada con referencia a reconciliación 2026-05-13.
- [x] §8 Proyecciones: no aplica, justificado.
- [x] §9 Endpoints HTTP: no aplica (listener interno).
- [x] §10 SignalR: no aplica, justificado.
- [x] §11 Adapter Sinco: aplica, detallado con matriz de respuestas completa y estado 🚧.
- [x] §12 Preguntas abiertas: D-1 auto-decidida; D-2 bloqueante potencial (path); D-3 decisión auth pendiente (recomendación A incluida); D-4 nombre del listener propuesto; D-5 alertas (no bloqueante).

---

## Notas para `red` (no forman parte del spec de dominio)

**Framework de tests recomendado:**

1. **WireMock.Net** para stubbear `POST /api/v4/Maquinaria/api/preoperacional-fallas/cerrar`. Crear una `WireMockServer` en el setup del test y configurar `IMaquinariaErpClient` para apuntarle.

2. **WolverineFixture / AlbaHost** para ejecutar el listener en un bus de test. El evento `NovedadPreopDescartada_v1` se publica con `IMessageBus.PublishAsync` y el test espera la completación antes de hacer asserts.

3. **No se necesita Marten real (Testcontainers)** para los escenarios §6.1..§6.8 — el listener no lee ni escribe en el event store. Los tests son unitarios de integración sobre el bus + adapter HTTP.

4. **Para §6.3 y §6.4 (retry):** WireMock puede configurar respuestas secuenciales (`InScenario`). El test necesita controlar el `TimeProvider` o usar un `RetryPolicy` con delay 0 en test (Wolverine permite override de la política de retries en test via `WolverineOptions`).

5. **Naming de tests:** español, frase completa, referenciando el escenario:
   - `DescartarNovedadPreopErpListener_erp_200_cierra_exitosamente`
   - `DescartarNovedadPreopErpListener_erp_200_ya_cerradas_trata_como_exito`
   - `DescartarNovedadPreopErpListener_erp_409_ya_cerrado_trata_como_exito`
   - `DescartarNovedadPreopErpListener_erp_5xx_persistente_va_a_dead_letter`
   - `DescartarNovedadPreopErpListener_erp_4xx_no_reintenta_va_a_dead_letter`
   - `DescartarNovedadPreopErpListener_evento_malformado_dead_letter_inmediato`

6. **Ubicación sugerida:** `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/DescartarNovedadPreopErpListenerTests.cs`.
