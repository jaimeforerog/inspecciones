# Review notes — Slice erp-4 — SincronizarCatalogos

**Autor:** reviewer
**Fecha:** 2026-05-19
**Slice auditado:** `slices/erp-4-sync-catalogos-on-app-open/`
**Veredicto:** `request-changes` — se devuelve a **green** por un blocker de producción en atomicidad transaccional.

---

## 1. Resumen ejecutivo

El handler `SincronizarCatalogosHandler` y sus tests son estructuralmente correctos. Los 23 tests cubren los 8 escenarios del spec §6, pasan en verde y la suite completa (59/59) no tiene regresiones. Sin embargo, existe un **bug serio en producción**: `MartenCatalogoSyncRepository` recibe `IDocumentSession` por DI como `AddScoped` (singleton dentro del request HTTP), y el handler ejecuta los 3 catálogos con `Task.WhenAll` compartiendo esa sesión. `IDocumentSession` de Marten **no es thread-safe** — los tres tasks acceden concurrentemente al mismo objeto, produciendo condiciones de carrera. Además, en el escenario de partial-failure (D5), el `SaveChangesAsync` del catálogo exitoso compromete en la misma transacción los `Store` acumulados de **todos** los catálogos, incluido el que falló — lo que puede producir un wipe del catálogo fallido sin el replace correspondiente (inconsistencia). Los tests pasan porque `FakeCatalogoSyncRepository` es thread-safe y no replica este comportamiento. Esto es un blocker.

---

## 2. Checklist de auditoría

### 2.1 Spec <-> tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. Los 8 escenarios (§6.1-§6.8) están cubiertos con granularidad adecuada: §6.1 x3, §6.2 x3, §6.3 x3, §6.4 x4, §6.5 x3, §6.6 x4, §6.7 x1, §6.8 x2.
- [x] Cada precondición tiene un test que la viola. PRE-1 (auth) está delegada al pipeline HTTP y documentada explícitamente en el spec como fuera de scope de tests de dominio. PRE-2 (Marten caído) es infraestructura. Ambas justificadas.
- [x] No hay invariantes I-H*, I-F*, V-F* — slice no-aggregate, correcto no referenciarlos.
- [x] Los nombres de tests son frases descriptivas en español con referencia a escenario. Conformes.

### 2.2 Tests como documentación

- [x] Given/When/Then visible en cada test con comentarios explícitos.
- [x] Sin mocks del dominio. `FakeCatalogoSyncRepository` implementa el puerto `ICatalogoSyncRepository` — no es un mock del dominio, es un fake de infraestructura. Correcto.
- [x] WireMock para el ERP. `MaquinariaErpClient` real apuntando a WireMock — correcto (igual que erp-1, erp-2, erp-3).

Una observacion sobre §6.6: el test `SincronizarCatalogos_causas_falla_200_vacio_respuesta_indica_error_con_mensaje_D4` verifica `cat.Status.Should().BeOneOf("error", "vaciado-sospechoso")`. El spec §6.6 dice explícitamente `status: "error"` en la respuesta y `UltimoEstado = "vaciado-sospechoso"` en el state. El handler retorna `"vaciado-sospechoso"` en el `ResultadoCatalogo.Status` (linea 102 del handler), no `"error"`. El test deberia verificar `"vaciado-sospechoso"` exacto para ser fiel al spec — `BeOneOf` es mas permisivo que necesario. Nit: no bloquea, pero la asercion laxa puede ocultar una futura regresion si el handler cambia a `"error"`.

### 2.3 Implementación

- [x] `TimeProvider` inyectado — cero `DateTime.UtcNow` en el handler. Verificado: `_time.GetUtcNow()` en las tres ubicaciones del handler.
- [x] `Guid.NewGuid()` no aplica. `CatalogoSyncState.Id` es string natural. Correcto.
- [x] Records inmutables: `SincronizarCatalogosResult` y `ResultadoCatalogo` son `sealed record` con constructor posicional. Correcto.
- [x] `CatalogoSyncState` es `sealed class` con setters públicos — correcto para documento Marten (requiere deserializacion). No es un evento de dominio.
- [x] Sin `Apply()` — no hay aggregate event-sourced. Correcto.
- [x] Rebuild test: §6.8 cubre el analogo de rebuild para el documento Marten (recarga el state desde el repo tras el sync y verifica coherencia). Correcto dado que no hay stream de eventos.
- [x] Naming: handler y repo en ingles (plumbing), registros de dominio en español. Correcto.
- [x] `nullable` habilitado, 0 warnings de build. Verificado: `dotnet build` → `0 Advertencia(s), 0 Errores`.

- **[BLOCKER]** `IDocumentSession` scoped compartida en `Task.WhenAll` — ver hallazgo #1 abajo.
- [x] Endpoint `POST /api/v1/catalogos/sync` retorna `200 OK` con el response DTO del spec §9.3 (campos `catalogos[].nombre/status/actualizadosEn/error` + `sincronizadoEn`). Estructura conforme. Sin auth verificada en el endpoint — ver hallazgo #2.

### 2.4 Cobertura

- [x] Cobertura de ramas del handler: todas las ramas ejercitadas por los 23 tests (304, 200+items, 200+vacio, MaquinariaErpException, HttpRequestException mapeada como MaquinariaErpException via 503). El refactor-notes afirma "≥85% — todas las ramas ejercitadas". Verificado por inspeccion del handler: `SincronizarCatalogoAsync` tiene 5 caminos (304, items>0 OK, items==0 D4, excepcion cualquiera) — los 4 cubiertos. `GuardarErrorYRetornarAsync` tiene segunda lectura del state (state previo null o no null) — cubierta en tests §6.4 con state previo y §6.5 sin state previo para productos.
- [x] `MartenCatalogoSyncRepository` no tiene test de integracion con Postgres real. Registrado como FU-51 en refactor-notes. Riesgo documentado. Aceptado como followup.
- [x] Ramas defensivas: `GuardarErrorYRetornarAsync` tiene segunda lectura del state — su rama null (estado de error sin state previo) es ejercitada en el test `SincronizarCatalogos_productos_error_guarda_state_error` (no hay SeedState para productos antes del error en el primer caso, aunque hay en el segundo). Confirmo que el test `SincronizarCatalogos_causas_falla_ok_productos_error_ambos_estados_en_respuesta` no siembra state para productos — la rama null de `previo?.EtagActual` se ejercita.

Cobertura de ramas del handler: **efectivamente ≥85%** — todas las ramas vivas ejercitadas. Ramas no ejercitadas son las del `MartenCatalogoSyncRepository` con Postgres real (FU-51 abierto).

### 2.5 Refactor

- [x] `refactor-notes.md` presente con 4 items documentados.
- [x] Tests no cambiaron de logica entre green y refactor (el refactor solo extrae el helper generico y agrega infra-wire). Verificado por inspeccion de los tests — identicos en nombres y aserciones.
- [x] 0 warnings de compilacion. Verificado.
- [x] El helper generico `SincronizarCatalogoAsync<TDto, TItem, TLocal>` es una mejora de claridad real: los 3 call-sites son concisos y el flujo ETag/304/vacio/wipe/error no se repite. La indirection con 4 delegates es justificada — es la unica forma de generalizar DTOs sin interfaz comun. La firma nombra cada delegate con precision.

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Infrastructure.Tests` → 59/59 pass. Sin regresiones en erp-1, erp-2, erp-3.
- [x] Domain.Tests no disponible para correr sin Docker (FU-47). Inferido de la nota del refactorer y de la historia previa del proyecto: el suite de dominio no toca el codigo de este slice.

### 2.7 Coherencia con decisiones previas

- [x] ADR-004 (sync on-app-open, ETag/If-None-Match, sin cron): implementado exactamente como especificado.
- [x] ADR-006 (outbox para POSTs ERP): no aplica — solo GETs. Documentado.
- [x] ADR-001 (REST/VPN): los endpoints consumidos son los acordados (M-10 causas-falla, M-11 tipos-falla, M-4 productos). M-17 (rutinas tecnicas) excluido intencionalmente — la decision del orquestador de reducir el scope a 3 catalogos esta reflejada en el handler. Sin violation de ADRs.
- [x] Sincronizacion reducida a 3 catalogos (causas-falla, tipos-falla, productos): conforme a la decision del orquestador. Equipos excluidos (P-2 abierta en spec). Rutinas tecnicas excluidas (requieren endpoint M-17 no implementado aun — P-1 documentada).
- [x] NO se intento sincronizar M-17 ni equipos en masa. Correcto.
- [x] Tipos de IDs: `CausaFallaCatalogo.Id` y `TipoFallaCatalogo.Id` son `int` (PK del ERP). `CatalogoSyncState.Id` es string natural (nombre del catalogo). Conforme a convencion §15.4.

### 2.8 Integración cross-team Sinco

- [x] Todos los endpoints ERP son GETs → no aplica `Idempotency-Key`.
- [x] WireMock usado para los 3 endpoints de catálogos. Marcados 🟡 mock-only en spec §11. Conforme.

### 2.9 SignalR / push

- [x] No aplica. Documentado explícitamente en spec §10.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | **blocker** | `IDocumentSession` de Marten es `AddScoped` — una instancia por request. `Task.WhenAll` ejecuta los 3 `SincronizarCatalogoAsync` concurrentemente, los 3 acceden a la misma `IDocumentSession`. Marten no garantiza thread-safety de `IDocumentSession`. En el camino feliz, los `DeleteWhere`+`Store` de los 3 catálogos se acumulan en la misma sesión y el primer `GuardarSyncStateAsync` que llega a `SaveChangesAsync` compromete los cambios de los 3 catálogos juntos (no partial-failure por catalogo). En el camino de error (D5): si causas-falla lanza exception, su `DeleteWhere` pudo haber sido ejecutado ya en la sesion compartida antes de que la excepcion llegara al catch — un `SaveChangesAsync` posterior de otro catalogo exitoso puede commitear el wipe-sin-replace de causas-falla, corrompiendo el catalogo. Los tests pasan porque `FakeCatalogoSyncRepository` usa listas en memoria (thread-safe por casualidad, no por diseño). **Fix requerido:** cada catálogo debe usar su propia `IDocumentSession` independiente. Opcion A: `MartenCatalogoSyncRepository` recibe `IDocumentStore` y abre una session propia por operacion (`store.LightweightSession()`). Opcion B: el handler crea tres `MartenCatalogoSyncRepository` con sesiones distintas (requiere factory). Opcion C: eliminar `Task.WhenAll` y ejecutar los 3 catálogos secuencialmente — se pierde el paralelismo pero se evita la sesion compartida. La opcion A es la mas limpia: `MartenCatalogoSyncRepository` recibe `IDocumentStore` en lugar de `IDocumentSession`, y cada metodo abre su propia `LightweightSession`. El DI cambia de `AddScoped<IDocumentSession>` a usar el `IDocumentStore` ya registrado por Marten. | `src/Inspecciones.Infrastructure/Erp/MartenCatalogoSyncRepository.cs:21` + `src/Inspecciones.Api/Program.cs:186` | **green** debe cambiar `MartenCatalogoSyncRepository` para recibir `IDocumentStore` (o similar) y abrir sesiones independientes. Los tests no cambian — usan `FakeCatalogoSyncRepository`. |
| 2 | followup | El endpoint `POST /api/v1/catalogos/sync` no verifica la capability `ejecutar-inspeccion` o `administrar-catalogos` (PRE-1 del spec §4). El spec lo marca como `PRE-1` pero reconoce que ADR-002 es tentativo. El endpoint es publico (cualquier request sin token puede dispararlo). Consistente con la situacion de otros endpoints del modulo (FU-14). | `src/Inspecciones.Api/Catalogos/CatalogosEndpoints.cs:300-319` | Registrar como FU-52 (consistente con FU-14). Bloqueante cuando se integre al host PWA. |
| 3 | nit | `SincronizarCatalogos_causas_falla_200_vacio_respuesta_indica_error_con_mensaje_D4` usa `BeOneOf("error", "vaciado-sospechoso")`. El handler retorna `"vaciado-sospechoso"` en `ResultadoCatalogo.Status` — el test deberia verificar exactamente ese valor para detectar regresiones futuras. | `SincronizarCatalogosHandlerTests.cs:705` | Cambiar `BeOneOf` a `Be("vaciado-sospechoso")`. No bloquea. |

---

## 4. Veredicto final

- [ ] **approved**
- [ ] **approved-with-followups**
- [x] **request-changes** — se devuelve a **green** con el blocker #1.

**Blocker #1** es la unica razon del rechazo. El resto del slice es solido. Una vez que `green` cambie `MartenCatalogoSyncRepository` para usar `IDocumentStore` (o sesiones independientes), el slice puede ser aprobado sin nueva ronda de review completa — solo verificar el fix.

**Aclaracion sobre los tests:** el blocker es un bug de produccion que los tests actuales no detectan porque `FakeCatalogoSyncRepository` no replica el comportamiento de sesion compartida de Marten. No se requiere cambiar los tests para el fix — el fake sigue siendo correcto como verificacion del contrato del puerto. El test E2E con Postgres real (FU-51) es el que eventualmente detectaria este tipo de bug.

**Seguimiento post-fix:** el orquestador puede re-invocar al reviewer con solo los archivos modificados (`MartenCatalogoSyncRepository.cs` + `Program.cs`). No se necesita re-auditar los 23 tests.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._

---

## 5. Iteración 2 — Post-review (verificación del fix)

**Fecha:** 2026-05-19
**Archivos verificados:** `ICatalogoSyncRepository.cs`, `MartenCatalogoSyncRepository.cs`, `SincronizarCatalogosHandler.cs`, `FakeCatalogoSyncRepository.cs`, `Program.cs`.

### 5.1 Atomicidad cross-catálogo

Confirmado. Cada `PersistirSync*Async` abre su propia `LightweightSession` con `_store.LightweightSession()` y llama `SaveChangesAsync` antes de salir (`await using var session`). Las tres sesiones son completamente independientes. Una excepción en causas-falla no puede afectar la sesión de tipos-falla ni la de productos.

### 5.2 Atomicidad dentro-de-catálogo

Confirmado. Dentro de cada `PersistirSync*Async`: (1) `DeleteWhere<T>(_ => true)` si `wipeAndReplace != null`, (2) `Store(items)`, (3) `Store(state)`, (4) un único `SaveChangesAsync`. Un solo commit por catálogo — wipe + replace + state en la misma transacción de Marten.

### 5.3 Thread-safety

Confirmado. `IDocumentStore` es singleton y thread-safe por diseño de Marten. `LightweightSession` es local a cada invocación (`await using var session` dentro del método). No hay sesión compartida entre los tres tasks del `Task.WhenAll`. La clase se registra como `AddSingleton<ICatalogoSyncRepository, MartenCatalogoSyncRepository>()` — correcto porque no tiene estado mutable (ningún campo que mute entre requests).

### 5.4 Partial-failure (D5)

Confirmado por construcción. Sesiones independientes significa que la excepción de un catálogo no puede comprometer la sesión de otro. El test `SincronizarCatalogos_causas_falla_5xx_tipos_falla_304_procesado_correctamente` verifica esto con el fake; el comportamiento del adapter Marten reproduce el mismo aislamiento por el diseño de sesiones independientes. FU-51 (test E2E con Postgres real) sigue abierto para cobertura completa del adapter — sin cambio de estado.

### 5.5 Tests

59/59 pass verificado en ejecución real. Las assertions no se debilitaron — los 23 escenarios son idénticos en estructura a la iteración 1 (refactorer lo confirma y la inspección de los tests corrobora). El nit de la iteración 1 (#3 — `BeOneOf` en la línea 705 del test) sigue presente: el test `SincronizarCatalogos_causas_falla_200_vacio_respuesta_indica_error_con_mensaje_D4` usa `BeOneOf("error", "vaciado-sospechoso")` cuando el handler retorna `"vaciado-sospechoso"` siempre. Este nit no bloquea — la ruta 304 → `PersistirSyncXxx(state, null)` también está cubierta.

### 5.6 Calidad de la firma del puerto

`wipeAndReplace == null` es una API suficientemente clara dado el docstring explícito en la interfaz. La alternativa `PersistirSinWipeAsync` añadiría seis métodos más (3 catálogos × 2 variantes) sin ganancia real de claridad. La semántica nullable es idiomática en C# para "opcional". Sin objeción.

Un punto menor: el `switch` en `PersistirErrorStateAsync` del handler asigna el catálogo por nombre string (`"causas-falla"`, `"tipos-falla"`, `"productos"`). La rama `_` lanza `ArgumentOutOfRangeException`, que es correcta. El acoplamiento nombre→método es aceptable dado que el handler define los nombres en los call-sites `SincronizarCatalogoAsync`.

### 5.7 Cobertura de ramas

Handler: todas las ramas del helper genérico `SincronizarCatalogoAsync` ejercitadas (304, 200+items, 200+vacío, excepción). `GuardarErrorYRetornarAsync` cubre la rama `previo?.EtagActual` tanto nula como no-nula. `PersistirErrorStateAsync` cubre las tres ramas del switch por los tests de error de los tres catálogos. Cobertura efectiva del handler: 100% de ramas vivas.

`MartenCatalogoSyncRepository`: tres métodos `PersistirSync*Async` con dos ramas cada uno (`wipeAndReplace == null` y `!= null`). Sin tests de integración con Postgres — FU-51 abierto. Ramas del adapter: no cubiertas por suite actual (solo el fake). Aceptado con FU-51 como en la iteración 1.

Cobertura de ramas del handler: ≥85% verificada por inspección. Criterio cumplido.

### 5.8 Reglas duras

- `TimeProvider` inyectado: 3 usos de `_time.GetUtcNow()`. Sin `DateTime.UtcNow`. Correcto.
- `nullable` habilitado, 0 warnings: verificado con `dotnet build` → `0 Advertencia(s), 0 Errores`.
- Naming: handler/repo en inglés (plumbing), documentos de catálogo en español. Correcto.
- Records inmutables: `SincronizarCatalogosResult` y `ResultadoCatalogo` son `sealed record`. Correcto.
- `IDocumentStore` (singleton) → `LightweightSession` local: correcto. Sin sesiones scoped en DI.

### 5.9 Regresiones

59/59 Infrastructure.Tests en verde. Domain.Tests no disponible sin Docker (FU-47 — sin cambio de estado). El código de producción de este slice no toca el dominio (`Inspecciones.Domain` — solo lee tipos de catálogo), por lo que no hay riesgo de regresión en Domain.Tests.

---

## 6. Veredicto final — Iteración 2

- [ ] **approved**
- [x] **approved-with-followups** — followups pre-existentes #51 (test E2E adapter Marten) y #52 (auth endpoint PRE-1). Sin followups nuevos.
- [ ] **request-changes**

**El blocker #1 de la iteración 1 está resuelto.** La corrección aplicada (Opción A) es la más limpia: `IDocumentStore` singleton → `LightweightSession` local por método → atomicidad y thread-safety por construcción. Sin overhead de fábrica ni exposición de sesión al caller.

**Followups que permanecen abiertos:**
- FU-51: test E2E de `POST /api/v1/catalogos/sync` con Postgres real (adapter Marten sin cobertura directa). Sin cambio.
- FU-52: endpoint sin verificación de capability PRE-1 (ADR-002). Sin cambio.

El orquestador puede proceder al commit del slice y a la fase de infra-wire.
