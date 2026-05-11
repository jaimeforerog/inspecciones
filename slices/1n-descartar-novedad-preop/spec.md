# Slice 1n — DescartarNovedadPreop

**Autor:** domain-modeler
**Fecha:** 2026-05-11
**Estado:** draft
**Agregado afectado:** `Inspeccion` (aggregate unificado — aplica a `TipoInspeccion.Tecnica`; las novedades preop no existen en el flujo de monitoreo).
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.4` — `NovedadPreopDescartada_v1` es el evento #9 del catálogo canónico de 24 eventos. Anotación explícita: "NO crea hallazgo, sólo audit; emitido por comando individual `DescartarNovedadPreop` con motivo autogenerado por el handler — ver §15.9".
- `01-modelo-dominio.md §15.9` — patrón unificado de las 3 opciones. Fila "Novedad preop (variante B), Descartar": `NovedadPreopDescartada_v1` (sin hallazgo). Contexto UX: icono "ojo tachado" tap directo, sin modal, motivo autogenerado.
- `01-modelo-dominio.md §15.5 V-F2` — "Todas las novedades preop están verificadas o descartadas" es una validación pre-firma. El presente slice implementa la mitad del descarte; V-F2 es pregunta abierta heredada de slice 1g.
- `01-modelo-dominio.md §15.12.1 DetalleInspeccionView` — "Novedades preop descartadas: por cada `NovedadPreopDescartada_v1` en el stream, exponer `NovedadPreopId` + `MotivoDescarte` + `DescartadaPor` + `DescartadaEn`. Requisito explícito — no opcional."
- `01-modelo-dominio.md §7.4.5` — "El descarte explícito requiere acción del técnico. Forma vigente (§15.4): se emite el evento dedicado `NovedadPreopDescartada_v1` (no crea hallazgo, solo audit + POST `/preop/novedades/{id}/descartar` con motivo). Sin la acción de descarte, la novedad sigue pendiente en el preop."
- `06-contrato-apis-erp.md §3.1 P-6` — `POST /api/v1/preop/novedades/descartar` (bulk-capable, 1..N). Decisión 2026-04-30: motivo autogenerado `"Cerrado por {usuario} el {fecha} UTC desde Inspecciones"`. El módulo siempre envía arrays de 1 en MVP. 🚧 path final pendiente de confirmar con David.
- `01-modelo-dominio.md §15.3 I-H2` — `NovedadPreopOrigenId` es el campo que vincula un `HallazgoRegistrado_v1` a su novedad de origen. Invariante: una novedad ya convertida en hallazgo (con `Origen=PreOperacional` y `NovedadPreopOrigenId == cmd.NovedadId`) no puede ser descartada simultáneamente.
- `slices/1c-registrar-hallazgo/spec.md` — `HallazgoRegistrado_v1` con `Origen=PreOperacional` lleva `NovedadPreopOrigenId: int`. Este es el mecanismo por el cual el aggregate sabe que una novedad fue convertida en hallazgo.
- `slices/1g-firmar-inspeccion/spec.md §12 P-1` — V-F2 (enforcement del conteo de novedades pendientes) fue marcada como pregunta abierta en slice 1g; sigue abierta aquí. Ver §12 P-2 de este spec.
- `slices/1m-cancelar-inspeccion/spec.md` — patrón de referencia para estructura del spec, `ClaimsTecnico`, manejo de capability `ejecutar-inspeccion`, ADR-008 `X-Client-Command-Id`.
- ADR-006 (`01-modelo-dominio.md §16`) — resiliencia outbox. La integración con P-6 va vía Wolverine outbox; ver §11 (out of scope).
- ADR-008 (`00-investigacion-mercado.md §9.16`) — `X-Client-Command-Id` para idempotencia end-to-end del cliente PWA.
- `roadmap.md §3.9` — paso "NovedadPreopDescartada" y `§3.49` — endpoint `POST /inspecciones/{id}/novedades-preop/{novedadId}/descartar`.

---

## 1. Intención

El técnico, al revisar la lista "Importar desde preoperacional", necesita poder descartar una novedad individual que considera inválida (falsa alarma, reporte equivocado del operador, novedad ya resuelta por otro medio). El descarte es una decisión de gobernanza: el técnico está contradiciendo al operador. Se hace con un tap único en el icono "ojo tachado" — sin modal, sin motivo manual. El sistema genera el motivo automáticamente, registra la decisión en el stream de la inspección (evento de auditoría) y notifica al ERP de preop para que la novedad salga del flujo pendiente del operador. La novedad queda descartada de forma irreversible desde el punto de vista del preop — cancelar la inspección no revierte el descarte en el ERP (decisión P-6 §06-contrato-apis-erp.md).

---

## 2. Comando

```csharp
public sealed record DescartarNovedadPreop(
    Guid   InspeccionId,
    int    NovedadId,       // PK del ERP (int, convención §15.4) — novedad a descartar
    string DescartadaPor    // userId opaco del técnico, extraído del JWT por la capa API
) : ICommand;
```

**Notas sobre el payload:**

> **`NovedadId`:** `int` (System.Int32) — PK del ERP (módulo Preoperacional). Convención §15.4: "IDs del ERP (PKs de tablas Sinco) → `int`". Igual que `NovedadPreopOrigenId` en `HallazgoRegistrado_v1`.

> **`DescartadaPor`:** userId opaco del técnico, extraído del JWT del host PWA por la capa API e inyectado como parámetro al handler. El dominio lo recibe como `string` sin conocer el mecanismo del host (ADR-002).

> **Sin `MotivoDescarte` en el payload:** el motivo es autogenerado por el handler con la plantilla `$"Cerrado por {cmd.DescartadaPor} el {descartadaEn:yyyy-MM-dd HH:mm} UTC desde Inspecciones"`. El técnico no escribe motivo (decisión 2026-04-30 — tap directo sin modal).

> **Sin `UbicacionGps`:** el descarte es una decisión cognitiva (revisar lista), no captura de campo. Sin GPS en el evento, consistente con `CancelarInspeccion` (slice 1m) que tampoco lleva GPS.

**Claims del técnico** (parámetros adicionales del handler, no del command record):

```csharp
// Misma forma que ClaimsTecnico definido en slices 1g y 1m.
public sealed record ClaimsTecnico(
    string    TecnicoId,
    ISet<int> ProyectosAsignados,
    bool      TieneCapabilityEjecutarInspeccion);
```

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/novedades-preop/{novedadId}/descartar
public sealed record DescartarNovedadPreopRequest(
    string DescartadaPor);   // userId del técnico; el motivo es server-generated

public sealed record DescartarNovedadPreopResult(
    Guid           InspeccionId,
    int            NovedadId,
    string         MotivoDescarte,    // devuelto para confirmación UX
    string         DescartadaPor,
    DateTimeOffset DescartadaEn);
```

---

## 3. Evento(s) emitido(s)

Este slice emite **exactamente un evento** en todos los casos de éxito, en un único `SaveChangesAsync`.

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `NovedadPreopDescartada_v1` | `InspeccionId`, `NovedadId`, `MotivoDescarte`, `DescartadaPor`, `DescartadaEn` | Al descartar exitosamente una novedad válida, no descartada previamente y no convertida en hallazgo. |

```csharp
public sealed record NovedadPreopDescartada_v1(
    Guid           InspeccionId,
    int            NovedadId,          // PK del ERP (int)
    string         MotivoDescarte,     // autogenerado: "Cerrado por {usuario} el {fecha} UTC desde Inspecciones"
    string         DescartadaPor,      // userId opaco
    DateTimeOffset DescartadaEn        // TimeProvider.GetUtcNow() — prohibido DateTime.UtcNow
) : IEvent;
```

**Causalidad y atomicidad:** un solo evento, un único `SaveChangesAsync`. No hay dependencia con otros eventos en este comando.

---

## 4. Precondiciones

Condiciones que deben cumplirse **antes** de ejecutar el comando. Evaluadas en el método de decisión del aggregate; nunca en `Apply`.

- **PRE-1** (stream existe): el stream `inspeccion-{InspeccionId}` existe y el aggregate ha sido hidratado. Si no existe → `404 Not Found` (HTTP) / `InspeccionNotFoundException`.
- **PRE-2** (estado `EnEjecucion`): `Estado == InspeccionEstado.EnEjecucion`. Si `Estado` es `Firmada`, `Cerrada`, `CerradaSinOT`, `Cancelada` o cualquier estado terminal → `DomainException` ("La inspección no está en ejecución; estado actual: {Estado}") → HTTP 422.
- **PRE-3** (técnico contribuyente o iniciador): `DescartadaPor` es el `TecnicoIniciador` del aggregate o pertenece a `TecnicosContribuyentes` (derivado por `Apply` de cada evento de captura). Si no lo es, el handler valida capability `ejecutar-inspeccion` y pertenencia al proyecto. Cualquier técnico autenticado con la capability puede contribuir (I2b §2.1). Ver Decisión D-1.
- **PRE-4** (capability): el usuario tiene capability `ejecutar-inspeccion` en el contexto del host PWA. Verificado en la capa HTTP antes de llegar al handler.
- **PRE-5** (novedad no descartada previamente): el aggregate NO tiene un `NovedadPreopDescartada_v1` previo con el mismo `NovedadId` en el stream (evaluado desde `_novedadesDescartadas: HashSet<int>`). Si ya fue descartada → `DomainException` ("La novedad {NovedadId} ya fue descartada en esta inspección") → HTTP 422.
- **PRE-6** (novedad no convertida en hallazgo): el aggregate NO tiene un `HallazgoRegistrado_v1` con `Origen=PreOperacional` y `NovedadPreopOrigenId == cmd.NovedadId`. Si ya fue convertida en hallazgo → `DomainException` ("La novedad {NovedadId} ya fue importada como hallazgo en esta inspección; no se puede descartar") → HTTP 422.
- **PRE-7** (novedad pertenece a la inspección): la novedad debe estar registrada en el conjunto de novedades importadas de la inspección (`_novedadesImportadas: HashSet<int>` — ver §12 P-1). Si la novedad no fue importada para esta inspección → `DomainException` → HTTP 404 (novedad no encontrada en esta inspección). Ver Decisión D-2.

> **Capa donde viven:** las precondiciones se evalúan en el método de decisión `Descartar(cmd)` del aggregate, nunca en los `Apply`. Los `Apply` son mutaciones puras de estado que deben ejecutarse sin lanzar sobre cualquier historial de eventos válido — incluido el rebuild desde stream.

---

## 5. Invariantes tocadas

- **I2** (§2.1): Solo se pueden emitir eventos de captura en estado `EnEjecucion` → cubierta por PRE-2.
- **I2b** (§2.1): Cualquier técnico autenticado puede contribuir a una inspección `EnEjecucion`. El `Apply(NovedadPreopDescartada_v1)` agrega `e.DescartadaPor` al `HashSet` de contribuyentes (derivado automático) → cubierta por PRE-3 + Apply puro.
- **I4** (§2.1): "Una novedad del preop solo se puede verificar (o descartar) **una vez** dentro de la inspección" → cubierta por PRE-5 (idempotencia de descarte) y PRE-6 (novedad ya convertida en hallazgo).
- **INV-ND1** (nuevo — propuesto para agregar a §15 en este PR): Una novedad no puede estar descartada **y** convertida en hallazgo en la misma inspección. Exclusividad mutua: o `NovedadPreopDescartada_v1` o `HallazgoRegistrado_v1 con NovedadPreopOrigenId==novedadId`, nunca ambos. Cubierta por PRE-5 (bloquea doble descarte) y PRE-6 (bloquea descartar una novedad ya importada como hallazgo). Simétrico: el slice 1c (`RegistrarHallazgo`) debe verificar que la novedad no haya sido descartada antes de importarla — ver §12 P-3.
- **V-F2** (§15.5): "Todas las novedades preop están verificadas o descartadas" — validación pre-firma. Este slice implementa la mitad del descarte que contribuye al cumplimiento de V-F2; el enforcement de V-F2 en `FirmarInspeccion` es pregunta abierta heredada (§12 P-2 de este spec).

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — descartar novedad válida en inspección en ejecución

**Given**
- Stream `inspeccion-{id}` con:
  - `InspeccionIniciada_v1` (`Estado=EnEjecucion`, `TecnicoIniciador="ana.gomez"`, `ProyectoId=3`)
  - *(opcional: otros eventos de captura previos — hallazgos, mediciones; no afectan este path)*

**When**
- `DescartarNovedadPreop(InspeccionId=id, NovedadId=9001, DescartadaPor="ana.gomez")`
- Técnico tiene capability `ejecutar-inspeccion` y `ProyectoId=3` en sus claims.
- `TimeProvider` devuelve `2026-05-11T14:30:00Z`.

**Then**
- Emite `NovedadPreopDescartada_v1` con:
  - `InspeccionId = id`
  - `NovedadId = 9001`
  - `MotivoDescarte = "Cerrado por ana.gomez el 2026-05-11 14:30 UTC desde Inspecciones"`
  - `DescartadaPor = "ana.gomez"`
  - `DescartadaEn = 2026-05-11T14:30:00Z`
- `_novedadesDescartadas` contiene `9001`.
- `TecnicosContribuyentes` contiene `"ana.gomez"`.
- Estado sigue siendo `EnEjecucion` (el descarte no transiciona el estado del aggregate).

---

### 6.2 Violación de PRE-2 — descarte sobre inspección firmada

**Given**
- Stream con `InspeccionIniciada_v1` + `DiagnosticoEmitido_v1` + `DictamenEstablecido_v1` + `InspeccionFirmada_v1` → `Estado=Firmada`.

**When**
- `DescartarNovedadPreop(InspeccionId=id, NovedadId=9001, DescartadaPor="ana.gomez")`

**Then**
- Lanza `DomainException` con mensaje que contiene "La inspección no está en ejecución" y el estado actual.
- HTTP 422 Unprocessable Entity.
- Ningún evento emitido.

---

### 6.3 Violación de PRE-2 — descarte sobre inspección cancelada

**Given**
- Stream con `InspeccionIniciada_v1` + `InspeccionCancelada_v1` → `Estado=Cancelada`.

**When**
- `DescartarNovedadPreop(InspeccionId=id, NovedadId=9001, DescartadaPor="ana.gomez")`

**Then**
- Lanza `DomainException` (estado terminal).
- HTTP 422.
- Ningún evento emitido.

---

### 6.4 Violación de PRE-5 — novedad ya descartada previamente (idempotencia de error)

**Given**
- Stream con `InspeccionIniciada_v1` + `NovedadPreopDescartada_v1(NovedadId=9001, DescartadaPor="ana.gomez", DescartadaEn=T1)`.

**When**
- Segundo intento: `DescartarNovedadPreop(InspeccionId=id, NovedadId=9001, DescartadaPor="ana.gomez")`

**Then**
- Lanza `DomainException` con mensaje que contiene "La novedad 9001 ya fue descartada en esta inspección".
- HTTP 422.
- Ningún `NovedadPreopDescartada_v1` adicional emitido.

> **Nota de idempotencia:** este escenario captura el caso de **reintento humano intencional** (el técnico toca el botón dos veces). El reintento de red (mismo `X-Client-Command-Id`) está cubierto por ADR-008 en §7.

---

### 6.5 Violación de PRE-6 — novedad ya convertida en hallazgo

**Given**
- Stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1(HallazgoId=h1, Origen=PreOperacional, NovedadPreopOrigenId=9001, AccionRequerida=RequiereIntervencion, ...)`.

**When**
- `DescartarNovedadPreop(InspeccionId=id, NovedadId=9001, DescartadaPor="ana.gomez")`

**Then**
- Lanza `DomainException` con mensaje que contiene "La novedad 9001 ya fue importada como hallazgo en esta inspección".
- HTTP 422.
- Ningún `NovedadPreopDescartada_v1` emitido.
- El hallazgo existente no se modifica.

---

### 6.6 Violación de PRE-7 — novedad no pertenece a la inspección (D-2 asunción)

> **Aplica solo si se implementa `_novedadesImportadas` en el aggregate** (ver Decisión D-2 en §12). Si la decisión es no trackear, este escenario se elimina o se convierte en HTTP 422 pass-through.

**Given**
- Stream con `InspeccionIniciada_v1` (sin ninguna novedad importada registrada en el aggregate).

**When**
- `DescartarNovedadPreop(InspeccionId=id, NovedadId=9999, DescartadaPor="ana.gomez")`
- `NovedadId=9999` nunca fue presentada como opción en la lista de novedades de esta inspección.

**Then**
- Lanza `DomainException` / HTTP 404 (novedad no encontrada en el contexto de esta inspección).
- Ningún evento emitido.

---

### 6.7 Técnico sin capability `ejecutar-inspeccion`

**Given**
- Inspección en `EnEjecucion`, válida.
- Usuario `"juan.auditoria"` con capability `auditar-inspecciones` pero NO `ejecutar-inspeccion`.

**When**
- Request `POST /inspecciones/{id}/novedades-preop/9001/descartar` con claims de `"juan.auditoria"`.

**Then**
- Capa HTTP rechaza con HTTP 403 Forbidden antes de llegar al handler.
- Ningún evento emitido.

---

### 6.8 Inspección no encontrada (PRE-1)

**Given**
- No existe stream `inspeccion-{id-desconocido}`.

**When**
- `DescartarNovedadPreop(InspeccionId=id-desconocido, NovedadId=9001, DescartadaPor="ana.gomez")`

**Then**
- HTTP 404 Not Found.
- Ningún evento emitido.

---

### 6.9 Rebuild desde stream (obligatorio — el comando emite ≥1 evento)

**Given**
- Aggregate en estado inicial (sin eventos).

**When**
- Se reproyectan en orden causal:
  1. `InspeccionIniciada_v1` (con `TecnicoIniciador="ana.gomez"`, `Estado=EnEjecucion`)
  2. `NovedadPreopDescartada_v1` (`NovedadId=9001`, `DescartadaPor="ana.gomez"`, `DescartadaEn=T`)

**Then**
- El estado resultante es idéntico al obtenido tras ejecutar el happy path (§6.1):
  - `Estado = EnEjecucion`
  - `_novedadesDescartadas` contiene `9001`
  - `TecnicosContribuyentes` contiene `"ana.gomez"`
- Ningún `Apply` lanza excepción.

> **Justificación:** garantiza que `Apply(NovedadPreopDescartada_v1)` es puro (solo muta `_novedadesDescartadas` y `_contribuyentes`), que no hace validaciones, y que los eventos están en orden causal correcto. Sin este test, una validación intrusa en `Apply` pasaría desapercibida hasta el primer rebuild en producción.

---

### 6.10 Motivo autogenerado — verificar plantilla exacta

**Given**
- Inspección en `EnEjecucion`.
- `TimeProvider` devuelve `2026-11-03T09:05:07Z`.

**When**
- `DescartarNovedadPreop(InspeccionId=id, NovedadId=9002, DescartadaPor="r.martinez")`

**Then**
- `NovedadPreopDescartada_v1.MotivoDescarte == "Cerrado por r.martinez el 2026-11-03 09:05 UTC desde Inspecciones"`
- El motivo es consistente con la plantilla documentada en P-6 del contrato.

---

## 7. Idempotencia / retries

**Reintento de red (cliente PWA / ADR-008):** el endpoint acepta el header `X-Client-Command-Id: <uuid>`, mapeado por Wolverine a `MessageId`. Ante un replay con el mismo `MessageId` (sin cambio de `NovedadId`), Wolverine devuelve la respuesta almacenada sin re-ejecutar el handler. El stream no recibe un segundo `NovedadPreopDescartada_v1`. Este es el escenario esperado en condiciones de conectividad inestable (el técnico está en campo).

**Reintento humano (doble tap):** cubierto por PRE-5 (§6.4) — lanza `DomainException`, HTTP 422. No es idempotente naturalmente porque el segundo intento viola la invariante I4/INV-ND1; el cliente debe mostrar el error y suprimir el ícono.

**Integración con ERP (saga P-6):** el POST a `/preop/novedades/descartar` se ejecuta vía Wolverine outbox (ADR-006). `Idempotency-Key` propuesta: `{InspeccionId}-{NovedadId}` (simple, derivada de los dos campos únicos del evento). Ver §11 para detalle de la saga (out of scope de este slice).

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` — **impacto explícito y obligatorio**

Consume el evento `NovedadPreopDescartada_v1`. Según §15.12.1 del modelo (requisito explícito, no opcional), debe agregar al array `novedadesDescartadas` de la vista:

```json
{
  "novedadId": 9001,
  "motivoDescarte": "Cerrado por ana.gomez el 2026-05-11 14:30 UTC desde Inspecciones",
  "descartadaPor": "ana.gomez",
  "descartadaEn": "2026-05-11T14:30:00Z"
}
```

Esta información es visible para el técnico (para saber qué ya procesó) y para el auditor (para rastrear decisiones de gobernanza donde el técnico contradice al operador). El campo `DecisionContradiceReporteOperador` de `AuditoriaInspeccionesView` (§15.12.2) se deriva de la presencia de al menos un `NovedadPreopDescartada_v1` en el stream.

**Estrategia de proyección:** inline `SingleStreamProjection` que consume todos los eventos del aggregate `Inspeccion`. No requiere nuevo stream ni proyección separada.

### 8.2 `BandejaTecnicoView` — sin cambio

`NovedadPreopDescartada_v1` no cambia el estado de la inspección ni el dictamen. La fila de la bandeja no se modifica por este evento. No aplica.

### 8.3 `InspeccionAbiertaPorEquipoView` — sin cambio

El descarte no cierra ni cancela la inspección. El equipo sigue ocupado. No aplica.

### 8.4 `AuditoriaInspeccionesView` (§15.12.2) — impacto indirecto

El campo `DecisionContradiceReporteOperador` de la vista de auditoría se calcula como `true` si el stream tiene ≥1 `NovedadPreopDescartada_v1`. Este campo ya era parte del diseño de la proyección; el presente slice lo materializa. No requiere cambio en la definición de la proyección, solo que se consuma el evento.

---

## 9. Impacto en endpoints HTTP

### Método y ruta

```
POST /api/v1/inspecciones/{inspeccionId}/novedades-preop/{novedadId}/descartar
```

- `inspeccionId`: `Guid` (ID interno del módulo).
- `novedadId`: `int` (PK del ERP). Va en el path (convención RESTful para recurso específico).

### Request body

```json
{
  "descartadaPor": "ana.gomez"
}
```

El `motivoDescarte` es server-generated. El cliente **no** lo envía.

### Respuestas

| Código | Cuándo | Body |
|---|---|---|
| `200 OK` | Happy path | `{ "inspeccionId": "...", "novedadId": 9001, "motivoDescarte": "Cerrado por...", "descartadaPor": "ana.gomez", "descartadaEn": "2026-05-11T14:30:00Z" }` |
| `200 OK` | Replay con mismo `X-Client-Command-Id` (ADR-008 / Wolverine dedup) | Mismo body de la primera ejecución exitosa |
| `400 Bad Request` | Body malformado (campo `descartadaPor` ausente o vacío) | Error envelope |
| `403 Forbidden` | Técnico sin capability `ejecutar-inspeccion` | Error envelope |
| `404 Not Found` | Inspección no encontrada (PRE-1) o novedad no pertenece a la inspección (PRE-7, si implementada) | Error envelope |
| `422 Unprocessable Entity` | Estado no `EnEjecucion` (PRE-2), novedad ya descartada (PRE-5), novedad ya convertida en hallazgo (PRE-6) | Error envelope con `code` específico |

### Rol / permiso requerido

Capability `ejecutar-inspeccion` (declarada, no validada por el módulo — ADR-002 tentativo). El host PWA Sinco MYE inyecta el claim; la capa HTTP del módulo lo verifica antes de despachar al handler.

### Headers

- `X-Client-Command-Id: <uuid>` — requerido para idempotencia ADR-008. Wolverine lo mapea a `MessageId`. Si el cliente no lo envía, el endpoint acepta el request pero sin garantía de idempotencia de red.
- `Authorization: Bearer {jwt}` — token del host PWA, validado por el módulo contra el IdP del host (ADR-002).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `NovedadPreopDescartada_v1` no está en el catálogo de eventos que disparan push en ADR-005 (`§14`). El descarte es una acción del técnico que ya está en la pantalla — no necesita notificación en tiempo real hacia el mismo usuario, ni hacia otros (el descarte es individual y no colaborativo en el modelo actual).

Si en el futuro emerge la necesidad de notificar a un supervisor que una novedad fue descartada (gobernanza), se agrega el evento al catálogo SignalR como cambio aditivo. Followup posible, no bloquea este slice.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**La saga de integración con el ERP (`DescartarNovedadPreopSaga`) está fuera del scope de este slice.**

**Patrón:**
Al persistir `NovedadPreopDescartada_v1` vía `SaveChangesAsync`, Wolverine outbox encola un mensaje `DescartarNovedadPreopErpCommand` que la saga procesa de forma asíncrona (ADR-006). La saga llama `POST /api/v1/preop/novedades/descartar` con array de 1 elemento.

**Endpoint:** P-6 `POST /api/v1/preop/novedades/descartar` — 🚧 bloqueado (equipo del preop no lo ha implementado). El módulo trabaja contra mock WireMock.

**Forma del body hacia el ERP** (para referencia, no modelada en este slice):
```json
{
  "inspeccionId": "{InspeccionId}",
  "novedadIds": [{NovedadId}],
  "motivo": "{MotivoDescarte}",
  "descartadaPor": "{DescartadaPor}"
}
```

**Idempotency-Key hacia el ERP:** `{InspeccionId}-{NovedadId}`. Ventana ≥30 días (§1.4 del contrato). Pendiente confirmar forma exacta con David (`07-preguntas-destrabar-followups.md`).

**Irreversibilidad:** una vez que el POST llega al ERP, la novedad queda descartada en el preop aunque la inspección se cancele posteriormente (decisión documentada en P-6 del contrato — "La asignación es vinculante").

**Followup:** la saga `DescartarNovedadPreopSaga` se planifica como slice 1n-b o como task de integración de Fase 4 (paso 3.29 del roadmap + adapter P-6). No bloquea el cierre del presente slice.

---

## 12. Preguntas abiertas

- **P-1 (no bloqueante — asunción con default razonable): Tracking de novedades en el aggregate (`_novedadesImportadas`).** PRE-7 requiere que el aggregate sepa qué novedades fueron importadas para esta inspección. El modelo actual del aggregate (`01-modelo-dominio.md §2.1`) **no expone** un `HashSet<int>` de novedades importadas. Opciones:
  - **(A — asunción default de este spec):** no se implementa `_novedadesImportadas` en el aggregate. PRE-7 no se enforcea en el backend. La validación de "novedad pertenece a esta inspección" queda como responsabilidad de la UI (el frontend solo muestra las novedades que trajo del preop para esta inspección). El backend solo valida PRE-5 (no ya descartada) y PRE-6 (no ya convertida en hallazgo). Técnica de defensa: si un cliente envía un `NovedadId` arbitrario, el handler lo acepta y emite el evento — el ERP luego rechaza el POST P-6 con 404 (la novedad no existe o no pertenece al contexto). Riesgo: event store queda con `NovedadPreopDescartada_v1` para una novedad que el ERP rechazó — la saga entra en dead-letter.
  - **(B):** agregar `_novedadesImportadas: HashSet<int>` al aggregate, poblado desde `InspeccionIniciada_v1` (si el comando de inicio snapshotea la lista de novedades). Requiere que `IniciarInspeccion` traiga la lista de `NovedadIds` pendientes al momento de iniciar. Implica cambio en slice 1a/1b y en el evento `InspeccionIniciada_v1` — invasivo. No recomendado para MVP sin más evidencia operativa de que el riesgo de la opción A se materializa.
  - **(C):** el handler consulta el ERP sincrónicamente (P-1/P-2 `GET /preop/novedades/{id}`) para validar que la novedad existe y está pendiente. Viola el principio de no llamadas síncronas al ERP desde handlers (ADR-006 §1.8). Descartada.
  - **Recomendación:** usar opción A para MVP. Si el dead-letter de la saga por novedades fantasma se materializa en producción, implementar opción B como cambio aditivo en un slice posterior.

- **P-2 (heredada de slice 1g — no bloqueante para este slice): V-F2 enforcement en FirmarInspeccion.** La validación pre-firma "todas las novedades preop están verificadas o descartadas" (V-F2 §15.5) requiere que el aggregate sepa cuántas novedades pendientes hay. Este slice agrega `NovedadPreopDescartada_v1` al stream, que es uno de los dos eventos que contribuyen al cumplimiento de V-F2 (el otro es `HallazgoRegistrado_v1 con Origen=PreOperacional`). Sin embargo, el aggregate no sabe el total de novedades pendientes (están en el ERP vivo). El enforcement de V-F2 sigue siendo UX-only en MVP (botón "Firmar" se deshabilita en el frontend mientras la pantalla muestre pendientes). Si el usuario decide implementar enforcement backend de V-F2, ese cambio va en el slice de `FirmarInspeccion` (1g), no aquí.

- **P-3 (nueva — no bloqueante, asunción D-1): Simetría INV-ND1 en RegistrarHallazgo.** La invariante INV-ND1 (novedad no puede estar descartada Y convertida en hallazgo) tiene dos lados: este slice bloquea descartar una novedad ya importada como hallazgo (PRE-6). El lado opuesto — bloquear `RegistrarHallazgo con Origen=PreOperacional y NovedadPreopOrigenId=X` cuando X ya fue descartada — no está implementado en el slice 1c (`RegistrarHallazgo`). Opciones: (a) asumir que la UI previene esta situación (el icono "ojo tachado" al descartar desaparece o se deshabilita en la lista); (b) agregar PRE-X al método de decisión de `RegistrarHallazgo` que consulte `_novedadesDescartadas`. Recomendación: implementar opción (b) como parte del rollout de este slice — requiere un fix trivial en el handler de 1c al agregar `_novedadesDescartadas: HashSet<int>` al aggregate y consultar en el método de decisión de `RegistrarHallazgo`. Si se acepta esta recomendación, el fix puede ir en el mismo PR de este slice o en un fix-FU separado. **Marcar como seguimiento para el rol `red` de este slice.**

---

## 13. Decisiones documentadas

| # | Decisión | Valor elegido | Justificación |
|---|---|---|---|
| D-1 | Contribuyente requerido vs capability | Cualquier técnico con capability `ejecutar-inspeccion` puede descartar (no exige ser contribuyente previo) | I2b §2.1: "Cualquier técnico autenticado puede contribuir a una inspección EnEjecucion". El descarte es un acto de captura que agrega a `TecnicosContribuyentes` vía `Apply`. Exigir contribuyente previo sería contradecir I2b. |
| D-2 | PRE-7 enforcement (novedad pertenece a la inspección) | Opción A: no se trackea `_novedadesImportadas`, validación UX-only | Ver §12 P-1. MVP prioriza simplicidad; el riesgo de dead-letter en saga se acepta como improbable en operación normal. |
| D-3 | `NovedadId` tipo | `int` (System.Int32) | Convención §15.4: "IDs del ERP (PKs de tablas Sinco) → `int`". Consistente con `NovedadPreopOrigenId: int?` en `HallazgoRegistrado_v1` y en el request body P-5/P-6 del contrato. |
| D-4 | Plantilla del motivo autogenerado | `"Cerrado por {usuario} el {fecha:yyyy-MM-dd HH:mm} UTC desde Inspecciones"` | Documentada en P-6 §06-contrato-apis-erp.md. El técnico no la escribe (tap directo sin modal — decisión 2026-04-30). |
| D-5 | Estado post-descarte | `EnEjecucion` (sin transición) | El descarte es un evento de captura, no de lifecycle. La inspección sigue en ejecución para que el técnico continúe descartando o importando otras novedades. |
| D-6 | GPS en evento | No incluido | El descarte es cognitivo (revisar lista en pantalla). Sin GPS, consistente con `CancelarInspeccion` (slice 1m). |
| D-7 | Integración P-6 en scope del slice | Fuera del scope | Patrón consistente con `GenerarOT` (slice 1k) y `RechazarGenerarOT` (slice 1l): el comando emite el evento de dominio, la saga de integración es un slice separado. Reduce complejidad del slice y testabilidad. |

---

## 14. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-7) mapean a al menos un escenario Then en §6.
- [x] Todas las invariantes tocadas (I2, I2b, I4, INV-ND1) mapean a un escenario Then.
- [x] El happy path (§6.1) está presente.
- [x] El escenario de rebuild desde stream (§6.9) está presente.
- [x] §7 (idempotencia) está decidido: ADR-008 cubre replay de red; doble tap humano recibe 422; no es naturalmente idempotente.
- [x] §10 (SignalR) marcado explícitamente como "no aplica para este slice".
- [x] §11 (adapters Sinco on-prem) marcado explícitamente como out-of-scope con followup documentado.
- [x] §12 (preguntas abiertas): 3 preguntas, todas no bloqueantes con asunción default documentada (P-1 opción A, P-2 heredada de 1g, P-3 con recomendación de fix en mismo PR). El spec puede firmarse con las asunciones actuales.
- [x] INV-ND1 (nuevo invariante) propuesto para agregar a §15.3 del modelo en el mismo PR.
- [x] Decisiones D-1..D-7 documentadas en §13.
