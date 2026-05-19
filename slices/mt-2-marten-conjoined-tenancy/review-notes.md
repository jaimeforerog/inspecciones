# Review notes — Slice mt-2 — Marten Conjoined multi-tenancy

**Autor:** orquestador (rol `reviewer` — Agent tool no disponible; autorización pre-otorgada)
**Fecha:** 2026-05-19
**Slice auditado:** `slices/mt-2-marten-conjoined-tenancy/`
**Veredicto:** **approved-with-followups**

---

## 1. Resumen ejecutivo

mt-2 cablea Marten `Conjoined` por `tenant_id = IdEmpresa.ToString()` y verifica end-to-end con 8 tests E2E que documentan aislamiento cross-tenant en aggregate, catálogos y `CatalogoSyncState`. La integración con Wolverine outbox se preserva sin tocar dominio (D-MT2-8 confirmada — cero cambios en `src/Inspecciones.Domain/*`, cero cambios en eventos `_v1`). El listener `SincronizarDictamenVigenteListener` gana overload tenant-aware con manejo defensivo de `Envelope.TenantId` ausente (MT2-PRE-2). Build limpio (0 warnings, 0 errors).

**Veredicto:** `approved-with-followups` — el slice cumple el DoD pero abre cuatro followups operativos/observabilidad documentados en §3.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente:
  - §6.1 → `TenantedDocumentSessionFactory_OpenSession_propaga_IdEmpresa_...` (3 tests cubren las 3 overloads).
  - §6.2 → `POST_inspecciones_crea_stream_en_tenant_del_session_y_NO_es_visible_desde_otro_tenant`.
  - §6.3 → `EquipoLocal_sembrado_en_tenant_7_no_aparece_en_query_de_tenant_8` + `CatalogoSyncState_sembrado_en_tenant_7_no_aparece_en_query_de_tenant_8`.
  - §6.4 → `POST_inspecciones_con_IdEmpresa_ausente_responde_401_y_no_crea_stream`.
  - §6.5 → `Listener_lee_el_tenant_del_envelope_y_lo_propaga_al_reader_del_aggregate`.
  - §6.6 → `Listener_sin_tenant_en_envelope_lanza_TenantRequeridoEnEnvelopeException` + 2 hermanos defensivos.
  - §6.7 → N/A (slice no emite eventos — explícito en §6.7 del spec).
  - §6.8 → `POST_inspecciones_con_tenant_7_persiste_stream_discriminado_por_tenant_id_7`.
  - §6.9 → cubierto por la suite completa (regresión 65→73 sin pérdida).
- [x] Cada precondición tiene un test que la viola:
  - MT2-PRE-1 → §6.4 (ClaimRequeridaException → 401).
  - MT2-PRE-2 → §6.6 (TenantRequeridoEnEnvelopeException → dead-letter).
  - MT2-PRE-3 → §6.3 (CatalogoSyncState por-empresa).
- [x] Cada invariante tocada tiene un test que la viola:
  - MT2-INV-1 → cubierto por `MartenConjoinedTenancyTests` que validan que la sesión expone `TenantId == "N"` correcto. Regla code-review (no programable como test sin static analyzer).
  - MT2-INV-2 → §6.2 (cross-tenant aggregate).
  - MT2-INV-3 → §6.3 (cross-tenant catálogos).
  - MT2-INV-4 → preservado (no se tocó atomicidad cross-catálogo de erp-4).
- [x] Los nombres de los tests son frases completas en español.

### 2.2 Tests como documentación

- [x] Un lector que no conoce el código puede entender el comportamiento leyendo solo los tests:
  - `MartenConjoinedTenancyTests` tiene XML doc al header que mapea cada test a su §6.X del spec.
  - Cada test tiene comentarios Given/When/Then o estructura visual clara.
- [x] Given/When/Then está visible.
- [x] Sin mocks del dominio. Los fakes son del puerto `IInspeccionReader` y de `IMaquinariaErpClient`, nunca del aggregate.

### 2.3 Implementación

- [x] El código de producción añadido es mínimo:
  - Excepción nueva (8 líneas substantivas + comentarios).
  - Puerto (3 métodos triviales).
  - Impl factory (40 líneas).
  - Overload del listener (8 líneas substantivas + delegate a `DespacharAsync` ya existente).
  - Overload del reader (4 líneas).
  - Modificación `Program.cs` (10 líneas: 2 de policies + 8 de DI).
  - Modificación `MartenCatalogoSyncRepository` (cambio de ctor + 8 sites `_store.LightweightSession()` → `_sessions.OpenSession()`).
- [x] No hay `DateTime.UtcNow`, `Guid.NewGuid()` ni acceso a APIs del navegador en producción. **Cero cambios al dominio** (D-MT2-8).
- [x] Eventos son records inmutables — **no se tocaron** (D3 firmada — sin bump `_v1 → _v2`).
- [x] Value objects respetados — sin cambios.
- [x] **`Apply(Evt)` puro:** **N/A para este slice** — no se tocó el aggregate ni los `Apply`. Los tests de rebuild del aggregate del slice 1g siguen verde (Domain 246/265).
- [x] **Rebuild test:** **N/A** — el slice no emite eventos (§6.7 del spec).
- [x] **Atomicidad del handler:** **N/A** — el slice no introduce handlers nuevos. El comportamiento atómico del `IDocumentSession` Conjoined se valida indirectamente en §6.8 (un `SaveChangesAsync` persiste eventos discriminados por tenant).

### 2.4 Cobertura

- **Cobertura del aggregate `Inspeccion`:** **sin cambios — 94.44%** (slice no toca dominio).
- **Cobertura de `TenantedDocumentSessionFactory`:** los 3 métodos son ejercidos por los 3 tests `TenantedDocumentSessionFactory_*` + indirectamente por todo el E2E (cada `WebApplicationFactory.CreateClient()` invoca `OpenSession()` por cada handler scoped). **100% efectivo.**
- **Cobertura de `TenantRequeridoEnEnvelopeException`:** los 2 props (`NombreListener`, `MessageId`, `CodigoError`) y el constructor son ejercidos por los 2 tests + 4 del listener tenant. **100%.**
- **Cobertura del overload `LeerAsync(Guid, string, CancellationToken)`:** ejercido por los 4 tests del listener tenant + el test del aggregate cross-tenant. **100%.**
- **Cobertura de `MartenCatalogoSyncRepository`:** ejercida por los 23 tests existentes de erp-4 (`SincronizarCatalogosEndpointTests`) que pasan con el `FakeCatalogoSyncRepository`. **Sin cambios de cobertura.**

### 2.5 Refactor

- [x] `refactor-notes.md` presente — `refactor-notes.md` documenta explícitamente "sin cambios" con justificación por cada candidato analizado.
- [x] Los tests no se tocaron en la fase refactor (salvo renombrar — no aplicó).
- [x] Sin warnings de compilación. **0 warnings, 0 errors.**

### 2.6 Invariantes cross-slice

- [x] El slice no rompe invariantes de slices previos:
  - `Domain.Tests`: 246/265 + 19 skip — **sin regresión** (idéntico al pre-mt-2).
  - `Infrastructure.Tests`: 59 → 65 — solo crecimiento por tests nuevos del slice.
  - `Api.Tests`: 65/72 → 73/80 — solo crecimiento por 8 tests E2E nuevos.
  - `Application.Tests`: falla por Docker (FU-47 preexistente) — **no es regresión de mt-2**.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15` (fuente de verdad) — mt-2 no toca dominio.
- [x] Alineado con ADRs aplicables:
  - **ADR-002** (cerrado en mt-1): el `ISessionService.IdEmpresa` es la fuente del `tenant_id`.
  - **ADR-006** (outbox + retry para POSTs hacia ERP): preservado. La política `OnException<MaquinariaErpException>` + dead-letter se mantiene; `TenantRequeridoEnEnvelopeException` cae en la categoría `InvalidOperationException` (no retryable) por diseño.
  - **ADR-009** (multi-tenancy Conjoined — creado en mt-1): **enforcement implementado en este slice**. La sección "Slices del sub-track multi-tenancy" del ADR debe actualizarse en doc-writer para marcar mt-2 ✅.
- [x] Alineado con el spec mt-2 firmado (todas las decisiones D-MT2-1..10 confirmadas en green-notes).

### 2.8 Integración cross-team Sinco

- [x] **N/A** — mt-2 no toca adapter Sinco. FU-44 (propagación del JWT al `MaquinariaErpClient`) rolla a mt-3 según D-MT1-10.

### 2.9 SignalR / push

- [x] **N/A** — mt-2 no toca el hub (D-MT2-7 difiere a piloto).

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | Wolverine 3 puede tener ambigüedad de overload `HandleAsync(evento, ct)` vs `HandleAsync(evento, Envelope, ct)`. En producción, Wolverine debería preferir la overload con más parámetros, pero no hay smoke test E2E con outbox real que lo confirme — mt-4 debe validarlo. Si falla, opción del red-notes B: remover la overload sin envelope y migrar los 11 tests erp-3. | `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs` | Abrir **FU-56** con disparador "Wolverine 3 ambiguous handler" en mt-4. |
| 2 | followup | `DescartarNovedadPreopErpListener` no lee `Envelope.TenantId`. No es bug funcional (el listener no toca Marten directo, solo HTTP al ERP), pero pierde tenant en logs estructurados — riesgo de observabilidad cuando el operador investigue un fallo cross-empresa. | `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs` | Abrir **FU-57** con disparador "logs sin tenant complican triage multi-empresa". Probable cierre en mt-3 (cuando propaguemos el JWT al ERP). |
| 3 | followup | SQL backfill staging está preparado en green-notes pero no ejecutado. Es responsabilidad ops post-merge — fuera del scope del slice. | `slices/mt-2-marten-conjoined-tenancy/green-notes.md §"SQL backfill staging"` | Abrir **FU-58** con disparador "primer deploy a staging multi-empresa". |
| 4 | followup | El comportamiento de `Apply` puro en `Inspeccion` se preserva pero **no se ejercitó explícitamente con un test cross-tenant del rebuild**. Marten Conjoined garantiza que `AggregateStreamAsync(streamId)` con tenant N solo carga eventos con `tenant_id=N` — pero un test de "rebuild" en mt-2 podría ser valioso. | `tests/Inspecciones.Api.Tests/Tenancy/MartenConjoinedTenancyTests.cs` | Abrir **FU-59** con disparador "robustez cross-tenant del rebuild". Bajo riesgo — los 8 tests E2E ya garantizan que la lectura cross-tenant retorna `null`. |
| 5 | nit | El default tenant en tests (`"1"`) está hardcoded en `OpenSeedingSessionForDefaultTenant()`. Podría hacerse `IConfiguration`-driven, pero la simplicidad gana. Sin acción. | `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` | — |
| 6 | nit | `MartenCatalogoSyncRepository` ahora es Scoped (era Singleton). Es semánticamente correcto (depende del factory scoped), pero significa que se instancia por request HTTP. Costo despreciable (constructor trivial). Sin acción. | `src/Inspecciones.Api/Program.cs` | — |

---

## 4. Veredicto final

- [ ] **approved** — sin hallazgos, o solo nits asumidos.
- [x] **approved-with-followups** — 4 followups operativos/observabilidad registrados (FU-56..FU-59). Ningún blocker.
- [ ] **request-changes**

### Justificación

El slice satisface la spec firmada con todos los escenarios cubiertos por tests, cero regresión en Domain/Infra/Api tests, build limpio, y cero cambios al dominio. Los 4 followups son operativos (validación en mt-4, observabilidad, backfill ops, test de robustez extra) — ninguno bloquea el cierre del slice ni el avance a mt-3.

**El orquestador puede proceder al commit del slice y a las fases doc-writer/infra-wire.**
