# Slice erp-4 — SincronizarCatalogos

**Autor:** domain-modeler
**Fecha:** 2026-05-19
**Estado:** draft
**Agregado afectado:** ninguno (no hay aggregate event-sourced). Este slice opera sobre documentos Marten del bounded context Catálogo (supporting). El "aggregate" relevante a efectos de test de rebuild es `CatalogoSyncState` como documento mutable.
**Decisiones previas relevantes:**
- `00-investigacion-mercado.md §9.15` — ADR-004 canonical 2026-05-05: sync on-app-open, sin cron, ETag/`If-None-Match`, stale-while-revalidate.
- `01-modelo-dominio.md §12.7` — shapes de `EquipoLocal`, `RutinaTecnicaLocal`.
- `01-modelo-dominio.md §12.9.6` — shapes de `CausaFallaCatalogo`, `TipoFallaCatalogo`.
- `01-modelo-dominio.md §12.11.5` — `RutinaMonitoreoLocal`, por-equipo vs por-grupo.
- `06-contrato-apis-erp.md §3.4` — M-10 causas-falla, M-11 tipos-falla, M-13 obras/proyectos, M-16 rutinas-monitoreo (🚧), M-17 rutinas técnicas (🚧). M-16 per-equipo.
- `src/Inspecciones.Infrastructure/Erp/IMaquinariaErpClient.cs` — métodos disponibles con soporte `EtagResult<T>`.
- ADR-006 (`01-modelo-dominio.md §16`) — outbox para POSTs al ERP; este slice solo hace GETs, por lo que el outbox no aplica.
- ADR-002 (tentativo) — identidad heredada del host PWA Sinco MYE.

---

## 1. Intención

La PWA Sinco MYE necesita mantener sincronizados sus catálogos locales (IndexedDB cliente + proyecciones Marten servidor) sin scheduler nocturno. Al abrir la aplicación — o cuando el administrador presiona "Refrescar ahora" — se dispara un único endpoint que verifica catálogo por catálogo si hubo cambios en el ERP, descarga solo los que cambiaron (aprovechando `304 Not Modified`), y responde con el detalle del resultado por catálogo para que la PWA pueda informar al usuario.

El endpoint también sirve de backend para el botón admin "refrescar ahora" mencionado en ADR-004 §9.15 punto 5 (promovido a v1.0).

---

## 2. Comando

Este slice no es event-sourced: no hay aggregate con stream de eventos. El mecanismo de entrada es un `POST` HTTP que el endpoint traduce a un handler de aplicación. Por convención de la metodología (spec §2 para slices no-aggregate), el "comando" es el DTO de entrada del handler:

```
SincronizarCatalogos {
    // Sin payload. La decisión de qué catálogos sincronizar es interna al handler.
    // La identidad del llamante viaja en el JWT (propagado por el host PWA).
}
```

No hay campos de entrada porque el sync-all no admite filtro client-selectable en MVP; el handler decide el conjunto fijo de catálogos sincronizables (ver §1). Si en el futuro se requiere sincronizar solo un subconjunto, se añade `IReadOnlySet<string>? Catalogos` de forma aditiva.

---

## 3. Evento(s) emitido(s)

Este slice no emite eventos de dominio (no hay aggregate event-sourced). Las mutaciones de estado ocurren en documentos Marten:

| Documento mutado | Cuándo |
|---|---|
| `CatalogoSyncState` (upsert por catálogo) | Siempre: al inicio del intento se actualiza `UltimaSyncIntento`; al finalizar cada catálogo se persiste `UltimoEstado`, `UltimaSyncExitosa` (si ok) y `EtagActual` (si 200). |
| `EquipoLocal` (wipe-and-replace) | Solo si Maquinaria responde `200` con cuerpo no-vacío para el catálogo de equipos. |
| `CausaFallaCatalogo` / `TipoFallaCatalogo` / `RepuestoLocal` | Solo si Maquinaria responde `200` con cuerpo no-vacío para el catálogo correspondiente. |
| `RutinaTecnicaLocal` | Solo si Maquinaria responde `200` con cuerpo no-vacío para rutinas técnicas (M-17). |

`RutinaMonitoreoLocal` y `ParteEquipoLocal` **no se tocan** en este endpoint (ver decisión D1 y D2 en §5).

---

## 4. Precondiciones

Como no hay aggregate event-sourced, las "precondiciones" son validaciones del handler antes de llamar al ERP:

- `PRE-1`: el JWT del llamante es válido y contiene la capability `ejecutar-inspeccion` o `administrar-catalogos`. Sin ella, `401 Unauthorized`. (Mecanismo concreto: ADR-002 tentativo — el módulo valida el token recibido del host PWA.)
- `PRE-2`: el handler puede leer de Marten (sesión disponible). Si Marten está caído, el endpoint falla con `503`. Este caso no se cubre con GWT de dominio (es infraestructura); el healthcheck de Application Insights lo detecta.

No hay precondiciones de estado del catálogo: el sync se puede disparar en cualquier momento, independientemente del estado previo (incluso si `UltimoEstado = "error"` — el handler reintenta todo).

> **Capa donde viven:** las precondiciones se evalúan en el handler de aplicación antes de invocar `IMaquinariaErpClient`. No hay `Apply()` porque no hay aggregate.

---

## 5. Invariantes tocadas / decisiones de diseño

Como no aplican invariantes de aggregate (I-H*, I-F*, V-F*), se documentan las **decisiones de diseño** que dan forma a las reglas del slice:

**D1 — Rutinas de monitoreo omitidas del sync-all (decisión del modelador).**
`ListarRutinasMonitoreoPorEquipoAsync` requiere `equipoId` — no existe un endpoint M-16 global sin filtro. El sync-all no conoce la lista de equipos activos en tiempo de ejecución sin una consulta adicional cara. Se omite del sync-all y se sincroniza on-demand al iniciar `IniciarInspeccionMonitoreo`. Documentado como decisión explícita; si el ERP expone en el futuro un `GET /catalogos/rutinas-monitoreo` sin filtro (M-16 completo), se añade como catálogo D al sync-all en un slice posterior.

**D2 — Partes-equipo omitidas del sync-all (decisión del modelador).**
`ListarPartesEquipoAsync` requiere `idEquipo` (-1 trae todas, pero el comportamiento con -1 es "todas las partes visibles" — no el mismo agregado per-equipo que `ParteEquipoLocal` necesita). El shape de `ParteEquipoLocal` es per-equipo (tiene `ParteEquipoId` de la asignación), no un catálogo global plano. Se omite del sync-all; las partes se sincronizan por `SincronizarEquipoDesdeErpHandler` (existente) en el flujo de administración per-equipo.

**D3 — Estrategia de actualización: wipe-and-replace (Opción A).**
Al recibir `200 OK` con body de Maquinaria para un catálogo, el handler borra todos los documentos existentes del tipo y persiste los nuevos en una misma `IDocumentSession.SaveChangesAsync()`. Razón: es atómica por construcción (Marten garantiza que el wipe y el insert van en la misma transacción de PostgreSQL), más simple de razonar, sin riesgo de docs huérfanos por id renombrado. Trade-off aceptado: ventana de inconsistencia de lectura cero (transacción atómica), a costa de que lectura concurrente durante el sync ve el catálogo vacío por ~ms. En la práctica, Marten con `IsolationLevel.ReadCommitted` (default) y el bajo volumen de sync hacen que esto sea imperceptible.

**D4 — Cuerpo vacío `[]` con `200`: no borrar el cache (política conservadora).**
Si Maquinaria devuelve `200 OK` con array vacío (`items: []` o `Causas: []`, etc.), el handler interpreta eso como posible error operacional (regla: un catálogo vacío es anomalía — ningún cliente tiene cero causas de falla activas) y **no toca el cache local**. El `CatalogoSyncState` marca `UltimoEstado = "vaciado-sospechoso"` y no actualiza el ETag. La PWA puede mostrar aviso al admin. Si en el futuro el negocio confirma que un catálogo vacío es legítimo (improbable para causas/tipos de falla), se ajusta la política por catálogo.
Umbral MVP: cualquier respuesta con `items.Count == 0` (o lista equivalente vacía según el DTO) activa esta política. El handler no borra el cache local.

**D5 — Partial-failure: un catálogo falla, los demás continúan.**
Si el `IMaquinariaErpClient` lanza `MaquinariaErpException` o `HttpRequestException` para un catálogo, el handler captura la excepción, registra `UltimoEstado = "error"` con `UltimoErrorMensaje` en `CatalogoSyncState`, y continúa con el resto de catálogos. La respuesta HTTP del endpoint es siempre `200 OK` (el sync fue ejecutado), con el detalle de estado por catálogo en el body.

**D6 — Concurrencia optimista vía `IDocumentSession`.**
Si dos PWAs abren simultáneamente y disparan el sync-all en paralelo, ambos ejecutan el mismo flujo. En el peor caso, ambas llaman al ERP con el mismo `ETag`, reciben `304` (si no hubo cambios entre ambas llamadas) o `200` (si hubo cambios), y ambas persisten en Marten. Marten sin versioning explícito en documentos Marten (`Store()` es upsert — no usa `UpdateExpected()`) significa que la última escritura gana, lo cual es idempotente para catálogos estáticos. No hay riesgo de corrupción porque ambas escrituras producen el mismo contenido (vinieron de la misma respuesta del ERP). Se acepta la doble lectura al ERP como gasto menor. No se implementa mutex distribuido en MVP.

**D7 — ETagActual persiste el valor con comillas tal como lo devuelve HTTP (`"v42"`).**
El `EntityTagHeaderValue.ToString()` de .NET incluye las comillas. El handler guarda el valor crudo del header en `CatalogoSyncState.EtagActual` y lo pasa directamente a `IMaquinariaErpClient` (que ya maneja el parsing en `GetWithEtagAsync`). No se limpian las comillas en ningún punto del flujo.

---

## 6. Escenarios Given / When / Then

> Nota: como este slice no tiene aggregate event-sourced, no hay stream que reproyectar. El escenario "rebuild" aplica a `CatalogoSyncState` como documento Marten (se verifica que el estado del documento tras el sync coincide con lo esperado). Los escenarios usan Marten en modo documento, no event store.

### 6.1 Happy path — sync inicial sin ETag previo, Maquinaria devuelve 200

**Given**
- No existe `CatalogoSyncState` para el catálogo "causas-falla" en Marten (primer sync).
- `IMaquinariaErpClient.ListarCausasFallaAsync("-1", ifNoneMatch: null)` devuelve `EtagResult.Modified(body, "\"v1\"")` con body que tiene 3 causas.

**When**
- Se ejecuta `SincronizarCatalogos` (POST /api/v1/catalogos/sync).

**Then**
- Se persisten 3 documentos `CausaFallaCatalogo` en Marten.
- `CatalogoSyncState { Id="causas-falla", EtagActual="\"v1\"", UltimoEstado="actualizado" }` existe en Marten con `UltimaSyncExitosa != null`.
- La respuesta del endpoint contiene `{ nombre: "causas-falla", status: "actualizado", actualizadosEn: <non-null> }`.

### 6.2 Happy path — sync con ETag previo, Maquinaria devuelve 304

**Given**
- Existe `CatalogoSyncState { Id="tipos-falla", EtagActual="\"v5\"", UltimoEstado="actualizado" }`.
- `IMaquinariaErpClient.ListarTiposFallaAsync("-1", ifNoneMatch: "\"v5\"")` devuelve `EtagResult.NotModified<ListarTiposFallaResponseDto>("\"v5\"")`.

**When**
- Se ejecuta `SincronizarCatalogos`.

**Then**
- No se borran ni crean documentos `TipoFallaCatalogo`.
- `CatalogoSyncState.UltimoEstado` queda `"no-change"`.
- `CatalogoSyncState.UltimaSyncExitosa` se actualiza al timestamp actual.
- La respuesta del endpoint contiene `{ nombre: "tipos-falla", status: "no-change" }`.

### 6.3 Happy path — sync con ETag previo, Maquinaria devuelve 200 con nuevo cuerpo (cambio detectado)

**Given**
- Existe `CatalogoSyncState { Id="causas-falla", EtagActual="\"v10\"", UltimoEstado="actualizado" }`.
- Existen 3 documentos `CausaFallaCatalogo` previos en Marten.
- `IMaquinariaErpClient.ListarCausasFallaAsync("-1", ifNoneMatch: "\"v10\"")` devuelve `EtagResult.Modified(bodyConCuatroItems, "\"v11\"")`.

**When**
- Se ejecuta `SincronizarCatalogos`.

**Then**
- Los 3 documentos previos son reemplazados por los 4 nuevos (wipe-and-replace atómico).
- `CatalogoSyncState.EtagActual` queda `"\"v11\""`.
- `CatalogoSyncState.UltimoEstado` queda `"actualizado"`.
- La respuesta del endpoint contiene `{ nombre: "causas-falla", status: "actualizado" }`.

### 6.4 Fallo de Maquinaria 5xx en un catálogo — partial-failure

**Given**
- Existen `CatalogoSyncState` previos para todos los catálogos.
- Para "causas-falla": `ListarCausasFallaAsync` lanza `MaquinariaErpException` (5xx).
- Para "tipos-falla": `ListarTiposFallaAsync` devuelve `EtagResult.NotModified(...)`.

**When**
- Se ejecuta `SincronizarCatalogos`.

**Then**
- El cache local de "causas-falla" no se toca (intacto).
- `CatalogoSyncState { Id="causas-falla", UltimoEstado="error", UltimoErrorMensaje=<mensaje del 5xx> }` queda en Marten.
- "tipos-falla" se procesa correctamente con estado `"no-change"`.
- La respuesta HTTP del endpoint es `200 OK` (el endpoint no falla aunque un catálogo falle).
- El body incluye: `[{ nombre: "causas-falla", status: "error", error: <mensaje> }, { nombre: "tipos-falla", status: "no-change" }, ...]`.

### 6.5 Sync con un catálogo OK y otro en error — ambos reflejados en respuesta

**Given**
- Para "causas-falla": Maquinaria devuelve `200` con 3 items. ETag pasa de `"\"v1\""` a `"\"v2\""`.
- Para "productos": `ListarProductosAsync` lanza `HttpRequestException` (timeout VPN).

**When**
- Se ejecuta `SincronizarCatalogos`.

**Then**
- "causas-falla" se actualiza en Marten con los 3 nuevos docs.
- "productos": el cache local de `RepuestoLocal` queda intacto.
- `CatalogoSyncState { Id="productos", UltimoEstado="error" }` persiste.
- Respuesta `200 OK` con ambos estados en el array de catálogos.

### 6.6 Maquinaria devuelve 200 con array vacío — política conservadora (D4)

**Given**
- Existen 5 documentos `CausaFallaCatalogo` en Marten.
- `CatalogoSyncState { Id="causas-falla", EtagActual="\"v3\"" }`.
- `ListarCausasFallaAsync("-1", ifNoneMatch: "\"v3\"")` devuelve `EtagResult.Modified(bodyConCausasVacio, "\"v4\"")` donde `body.Causas.Count == 0`.

**When**
- Se ejecuta `SincronizarCatalogos`.

**Then**
- Los 5 documentos `CausaFallaCatalogo` previos permanecen intactos en Marten.
- `CatalogoSyncState.EtagActual` **no** se actualiza (sigue siendo `"\"v3\""`).
- `CatalogoSyncState.UltimoEstado` queda `"vaciado-sospechoso"`.
- La respuesta del endpoint indica `{ nombre: "causas-falla", status: "error", error: "Maquinaria devolvió catálogo vacío — cache local preservado" }`.

### 6.7 Sync concurrente — dos llamadas simultáneas con mismo ETag (D6)

**Given**
- Existe `CatalogoSyncState { Id="causas-falla", EtagActual="\"v10\"" }`.
- Dos instancias del handler se ejecutan concurrentemente.
- Ambas llaman a `ListarCausasFallaAsync("-1", ifNoneMatch: "\"v10\"")`.
- Maquinaria devuelve `304 Not Modified` a ambas (no hubo cambios).

**When**
- Ambas instancias completan su ejecución.

**Then**
- `CatalogoSyncState.UltimoEstado` es `"no-change"` (ambas escriben el mismo valor — idempotente).
- Los documentos `CausaFallaCatalogo` no se tocaron.
- No hay excepción ni corrupción de datos.

### 6.8 Sync con ETag previo, Maquinaria devuelve 200 con nuevo cuerpo — verificación de estado final del documento (análogo a rebuild)

**Given**
- `CatalogoSyncState { Id="tipos-falla", EtagActual="\"v2\"", UltimoEstado="no-change" }` en Marten.
- `TipoFallaCatalogo` con 2 items en Marten (estado anterior).
- `ListarTiposFallaAsync("-1", ifNoneMatch: "\"v2\"")` devuelve `EtagResult.Modified(bodyConTresItems, "\"v3\"")`.

**When**
- Se ejecuta `SincronizarCatalogos`.

**Then**
- Marten contiene exactamente 3 documentos `TipoFallaCatalogo` (los 2 anteriores fueron reemplazados).
- `CatalogoSyncState { Id="tipos-falla", EtagActual="\"v3\"", UltimoEstado="actualizado", UltimaSyncExitosa != null }`.
- Si se recarga el documento `CatalogoSyncState` desde Marten (equivale al rebuild check para este slice no-aggregate), los campos son idénticos a los escritos por el handler.

---

## 7. Idempotencia / retries

El endpoint `POST /api/v1/catalogos/sync` es **naturalmente idempotente**:

- Un segundo POST disparado inmediatamente después del primero encuentra el mismo `EtagActual` en `CatalogoSyncState` y Maquinaria responde `304 Not Modified` → no se toca el cache local, no se duplican documentos.
- En el caso extremo de concurrencia (D6), ambas escrituras producen el mismo resultado (wipe-and-replace del mismo body).

No se requiere `Idempotency-Key` en el header. El ETag del catálogo es el mecanismo natural de deduplicación.

El endpoint **no cruza a Sinco on-prem via outbox** (solo GETs síncronos): no aplica ADR-006. El handler es un `GET` encadenado — si el ERP está caído, el handler lo registra como error en `CatalogoSyncState` y responde al cliente, sin reintento automático. La PWA reintenta en la próxima apertura (stale-while-revalidate, ADR-004).

---

## 8. Impacto en proyecciones / read models

Este slice **es** la materialización de las proyecciones de catálogo en Marten. Los documentos afectados y sus shapes:

### 8.1 `CatalogoSyncState` (nuevo document — id natural string)

```
CatalogoSyncState {
    Id: string,                          // nombre del catálogo: "equipos" | "causas-falla" | "tipos-falla" | "productos" | "rutinas-tecnicas"
    EtagActual: string?,                 // valor crudo del header ETag incluyendo comillas: '"v42"'
    UltimaSyncExitosa: DateTimeOffset?,  // timestamp de la última sync con resultado "actualizado" o "no-change"
    UltimaSyncIntento: DateTimeOffset,   // timestamp del último intento (exitoso o no)
    UltimoEstado: string,                // "no-change" | "actualizado" | "error" | "vaciado-sospechoso"
    UltimoErrorMensaje: string?          // solo poblado cuando UltimoEstado = "error"
}
```

Clave primaria `Id` = nombre del catálogo (string natural — no Guid). Marten lo persiste como documento con Id tipo `string`.

### 8.2 Catálogos locales afectados

| Documento Marten | Catálogo ERP | Endpoint ERP | Método adapter | Estrategia update |
|---|---|---|---|---|
| `EquipoLocal` | Equipos | Equivalente a M-3 (ListarEquipos) | `ListarEquiposAsync(filtro: null, ...)` | Wipe-and-replace |
| `CausaFallaCatalogo` | Causas de falla (M-10) | `api/causas-falla?texto=-1` | `ListarCausasFallaAsync("-1", ...)` | Wipe-and-replace |
| `TipoFallaCatalogo` | Tipos de falla (M-11) | `api/tipos-falla?texto=-1` | `ListarTiposFallaAsync("-1", ...)` | Wipe-and-replace |
| `RepuestoLocal` | Productos/insumos | `api/productos?texto=-1` | `ListarProductosAsync("-1", ...)` | Wipe-and-replace |
| `RutinaTecnicaLocal` | Rutinas técnicas (M-17) | `api/catalogos/rutinas` | **No existe aún en `IMaquinariaErpClient`** — ver §12 P-1 |

### 8.3 Documentos omitidos del sync-all

| Documento | Razón de exclusión | Sync alternativo |
|---|---|---|
| `RutinaMonitoreoLocal` | `ListarRutinasMonitoreoPorEquipoAsync` requiere `equipoId` — no hay endpoint global M-16 sin filtro (D1) | On-demand al iniciar `IniciarInspeccionMonitoreo` |
| `ParteEquipoLocal` | Shape per-equipo, requiere `idEquipo` explícito (D2) | `SincronizarEquipoDesdeErpHandler` (existente) |

### 8.4 Nota sobre `EquipoLocal` en el wipe

`EquipoLocal` actualmente se carga per-equipo (un equipo a la vez) vía `SincronizarEquipoDesdeErpHandler`. En este slice se agrega la capacidad de cargarlos en masa desde `ListarEquiposAsync`. El shape que devuelve `ListarEquiposResponseDto` puede no tener todos los campos del record canonical `EquipoLocal` (§12.7 del modelo — `Placa`, `Descripcion`, medidores, `AtributosExtra` están en el subset extendido que se completa con M-3b). El mapper del sync-all rellena los campos disponibles y deja el resto en su valor default/null. Los equipos sincronizados per-equipo (con M-3b via `SincronizarEquipoDesdeErpHandler`) tienen campos más ricos — el wipe-and-replace del sync-all puede degradar esos campos si borra y reescribe con el shape liviano. **Mitigación**: el sync-all no toca equipos cuya `ParteEquipoLocal` ya fue sincronizada (alternativa más conservadora), o bien se acepta que el sync-all escribe el shape liviano y el sync per-equipo se re-ejecuta cuando el técnico selecciona el equipo para inspeccionar (el handler `IniciarInspeccion` lo hidrata bajo demanda). Decisión MVP: el sync-all escribe el shape liviano; el detalle rico lo aporta M-3b on-demand. Esto es coherente con ADR-004 stale-while-revalidate y el patrón de hidratación bajo demanda (§12.7 nota 2026-05-07).

---

## 9. Impacto en endpoints HTTP

### 9.1 Endpoint principal

- **Método + ruta:** `POST /api/v1/catalogos/sync`
- **Auth requerida:** capability `ejecutar-inspeccion` o `administrar-catalogos` en el JWT del host PWA (ADR-002 tentativo). El endpoint es invocado tanto por la PWA al abrirse (cualquier técnico) como por el admin desde el botón "Refrescar ahora".

### 9.2 Request DTO

```json
{}
```

Sin body. El Content-Type puede ser `application/json` o estar ausente. El handler ignora el body.

### 9.3 Response DTO

HTTP `200 OK` siempre (incluso en partial-failure). Body:

```json
{
  "catalogos": [
    {
      "nombre": "equipos",
      "status": "no-change | actualizado | error | vaciado-sospechoso",
      "actualizadosEn": "2026-05-19T14:30:00Z",
      "error": "mensaje de error si status = error o vaciado-sospechoso"
    },
    {
      "nombre": "causas-falla",
      "status": "actualizado",
      "actualizadosEn": "2026-05-19T14:30:05Z",
      "error": null
    },
    {
      "nombre": "tipos-falla",
      "status": "no-change",
      "actualizadosEn": null,
      "error": null
    },
    {
      "nombre": "productos",
      "status": "error",
      "actualizadosEn": null,
      "error": "Maquinaria_V4 respondió 503 en api/productos?texto=-1."
    }
  ],
  "sincronizadoEn": "2026-05-19T14:30:10Z"
}
```

Campos de cada item:
- `nombre`: string identificador del catálogo (coincide con `CatalogoSyncState.Id`).
- `status`: uno de `"no-change"` | `"actualizado"` | `"error"` | `"vaciado-sospechoso"`.
- `actualizadosEn`: `DateTimeOffset?` — timestamp de fin del sync exitoso. `null` si `status != "actualizado"`.
- `error`: `string?` — mensaje de error. `null` si no hubo error.

### 9.4 Códigos HTTP

| Escenario | Código |
|---|---|
| Sync ejecutado (incluso con partial-failure) | `200 OK` |
| JWT inválido o capability ausente | `401 Unauthorized` |
| Marten no disponible (infraestructura) | `503 Service Unavailable` (no cubre dominio) |

### 9.5 Orden de ejecución

El handler ejecuta las llamadas al ERP en **paralelo** (Task.WhenAll) para minimizar la latencia total. Cada catálogo es independiente. Los `SaveChangesAsync` por catálogo se ejecutan de forma independiente (una sesión por catálogo o una sesión con múltiples stores — a decisión del implementador `green`; la spec no lo impone). La respuesta se construye al completar todos.

---

## 10. Impacto en SignalR / push (si aplica)

No aplica. El sync de catálogos no genera notificaciones push en tiempo real. La respuesta HTTP síncrona del `POST /catalogos/sync` es suficiente para que la PWA actualice su UI de estado de sincronización.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

Este slice consume los siguientes endpoints del ERP (solo GETs — no hay POSTs outbox):

| Endpoint ERP | Método adapter | Estado |
|---|---|---|
| `api/equipos` (equivalente M-3) | `ListarEquiposAsync(filtro: null, ifNoneMatch)` | 🟡 mock-only — el endpoint existe en Maquinaria_V4 y ya tiene implementación en el adapter; los tests usan WireMock. |
| `api/causas-falla?texto=-1` (M-10) | `ListarCausasFallaAsync("-1", ifNoneMatch)` | 🟡 mock-only — ídem. |
| `api/tipos-falla?texto=-1` (M-11) | `ListarTiposFallaAsync("-1", ifNoneMatch)` | 🟡 mock-only — ídem. |
| `api/productos?texto=-1` (M-4 / I-2 insumos) | `ListarProductosAsync("-1", ifNoneMatch)` | 🟡 mock-only — ídem. |
| `api/catalogos/rutinas` (M-17) | **método nuevo a agregar a `IMaquinariaErpClient`** | 🚧 bloqueado — requiere extensión del interface (ver §12 P-1). |

**Nota importante sobre M-17 y `ListarRutinasMonitoreoPorEquipoAsync`:** el adapter existente tiene `ListarRutinasMonitoreoPorEquipoAsync` que requiere `equipoId`. Para M-17 (rutinas técnicas sin filtro), se necesita un método nuevo. Para M-16 global (si emergiera), también requeriría un método nuevo. No se deben usar los métodos existentes con parámetros fake (-1 u 0) para simular "sin filtro" — eso cambiaría la semántica de la URL.

---

## 12. Preguntas abiertas

- [ ] **P-1 (bloqueante para `red`):** `IMaquinariaErpClient` no tiene método `ListarRutinasTecnicasAsync(string? ifNoneMatch)` para M-17 (`api/catalogos/rutinas`). Tampoco existe el DTO `ListarRutinasTecnicasResponseDto`. Hay dos opciones: (a) el slice erp-4 incluye la extensión del interface + DTO + implementación HTTP + tests WireMock del adapter; (b) la extensión se hace en un slice erp-4b previo. **Recomendación del modelador:** incluirlo en erp-4 mismo (es parte del contrato del sync-all, no merece un slice separado). El `green` puede agregar el método al interface en el mismo PR. Confirmar con el usuario antes de arrancar `red`.

- [ ] **P-2 (decisión de producto):** para `EquipoLocal`, el sync-all escribe el shape liviano (solo campos de `ListarEquiposResponseDto`: `EquipoId`, `EquipoCodigo`, `ProyectoId`, `RutinaTecnicaId`, `GrupoMantenimientoId` — los que estén disponibles en el DTO). Si un equipo ya fue sincronizado per-equipo con campos ricos (medidores, `Placa`, etc.), el wipe-and-replace del sync-all lo borra con el shape liviano. ¿Es aceptable esta degradación, o el sync-all de equipos debe omitir equipos ya sincronizados per-equipo? **Recomendación del modelador:** aceptar la degradación (shape liviano del sync-all); el técnico que inicia inspección dispara `SincronizarEquipoDesdeErpHandler` on-demand que restituye el shape rico. Confirmar con Jaime si la ventana de degradación es aceptable en el flujo de uso real.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones mapean a un escenario Then (PRE-1 → §6 implícito en scope de tests de auth; PRE-2 es infraestructura).
- [x] Todas las decisiones de diseño (D1..D7) tienen escenario correspondiente o están justificadas sin escenario (D3, D5, D6, D7 — son políticas del handler, no variantes de comportamiento de negocio observables externamente, salvo D4 cubierta en §6.6 y D6 cubierta en §6.7).
- [x] El happy path está presente (§6.1, §6.2, §6.3).
- [x] No hay aggregate event-sourced: el escenario de rebuild desde stream no aplica. En su lugar, §6.8 verifica consistencia del estado del documento Marten tras el sync (análogo funcional al rebuild check).
- [x] §7 idempotencia decidida: naturalmente idempotente via ETag; no requiere `Idempotency-Key`.
- [x] §10 SignalR: no aplica — marcado explícitamente.
- [x] §11 adapters Sinco: todos los endpoints consumidos son GETs; todos marcados 🟡 mock-only o 🚧 bloqueado con justificación. No hay POSTs outbox (ADR-006 no aplica a este slice).
- [x] §12 preguntas abiertas: 2 ítems — P-1 bloqueante para `red` (requiere confirmación de scope del slice); P-2 requiere validación con Jaime. Ambas tienen recomendación del modelador.
