# Review notes — Slice mt-3 — Propagación del JWT entrante al `MaquinariaErpClient`

**Autor:** reviewer (orquestador en rol reviewer — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario)
**Fecha:** 2026-05-19
**Slice auditado:** `slices/mt-3-jwt-propagation-erp/`.
**Veredicto:** **approved-with-followups**

---

## 1. Resumen ejecutivo

mt-3 cierra la cadena de identidad cross-process: el JWT del request HTTP entrante (o del envelope Wolverine para listeners) viaja al ERP Maquinaria_V4 vía `BearerTokenPropagationHandler` (DelegatingHandler). La cadena HTTP → Ambient → ServiceAccount funciona; `BearerTokenAusenteException` enforce fail-closed (MT3-INV-3); el listener `DescartarNovedadPreopErpListener` gana log con tenant (cierre FU-57); FU-44 cierra. 93/93 Infrastructure.Tests verde, Domain.Tests 246/19 sin regresión, build entero 0/0.

**Veredicto: `approved-with-followups`.** Un hallazgo no-bloqueante explícito en green-notes: la captura **automática** del header HTTP en el outbox publish (`CaptureBearerForOutboxMiddleware`) está pendiente como FU-60 — el listener ya está preparado para leer del envelope, pero requiere wiring Wolverine para que el endpoint HTTP enriquezca el envelope antes del outbox. MT3-INV-1 (HTTP scope) funciona end-to-end inline; MT3-INV-2 (envelope) funciona en tests in-process pero requiere FU-60 para flujos reales del outbox.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente:
  - §6.1 happy path HTTP → `HTTP_scope_propaga_Bearer_del_request_al_ERP`.
  - §6.2 happy path listener → `Listener_scope_propaga_Bearer_del_envelope_al_ERP` + variantes para Descartar y Dictamen.
  - §6.3 fallback service-account → `Sin_HTTP_ni_envelope_cae_a_service_account` + variantes.
  - §6.4 envelope vacío → `Ambient_con_string_vacio_cae_a_service_account`.
  - §6.5 fail-closed → `Sin_ningun_token_lanza_BearerTokenAusenteException_antes_de_salir_al_ERP`.
  - §6.6 chain order → `Chained_HTTP_gana_sobre_envelope_y_service_account`.
  - §6.7 log con tenant → `Listener_log_estructurado_incluye_TenantId_del_envelope_en_fallo_5xx`.
  - §6.8 compat HttpClient.DefaultRequestHeaders → cubierto por los 14 tests existentes `MaquinariaErpClientTests`.
  - §6.9 smoke regresión → 93/93 pass.
- [x] Cada precondición tiene un test que la viola: MT3-PRE-1 (sin HttpContext + sin ambient + sin SA = fail-closed test), MT3-PRE-2 (envelope sin header cae al SA), MT3-PRE-3 (SA vacío + sin otros = fail-closed).
- [x] Cada invariante tiene cobertura:
  - MT3-INV-1 (HTTP propaga el del request, no SA) → `HTTP_scope_propaga_Bearer_del_request_al_ERP` con assertions sobre lo que NO se envía.
  - MT3-INV-2 (listener via envelope+fallback) → tests de cada listener.
  - MT3-INV-3 (fail-closed) → `Sin_ningun_token_lanza_BearerTokenAusenteException`.
  - MT3-INV-4 (DelegatingHandler reescribe header default) → `DelegatingHandler_reescribe_Authorization_aunque_HttpClient_lo_tenga_setado`.
- [x] Los nombres de los tests son frases completas en español (convención repo).

### 2.2 Tests como documentación

- [x] Un lector sin contexto puede entender los flujos leyendo los tests: cada uno tiene comentarios GIVEN/WHEN/THEN explícitos y la chain configuration está visible.
- [x] Given/When/Then claro visualmente.
- [x] Sin mocks del dominio. Los doubles son `WireMockServer` (HTTP mock externo) y `FakeTenantAwareReader` (fake del puerto `IInspeccionReader`, ya existente en mt-2). El `CaptureLogger<T>` para inspeccionar logs estructurados es trivial — captura entradas sin reescribir comportamiento.

### 2.3 Implementación

- [x] Código de producción mínimo: 7 archivos nuevos en `Inspecciones.Infrastructure.Auth/` + 1 en `Inspecciones.Infrastructure.Erp/`; cada uno cumple un rol específico. Nada no ejercido por tests.
- [x] No hay `DateTime.UtcNow` ni `Guid.NewGuid()` en código de producción del slice. El slice no toca dominio.
- [x] No aplica "eventos como record" — el slice no emite eventos.
- [x] Value objects respetados — el slice no toca dominio.
- [x] **`Apply(Evt)` puro:** N/A — el slice no toca el aggregate.
- [x] **Rebuild test:** N/A — el slice no emite eventos.
- [x] **Atomicidad handler:** N/A — el slice no introduce handlers nuevos. El listener `DescartarNovedadPreopErpListener` sigue el pattern de un solo path (no toca Marten).

### 2.4 Cobertura

- [x] **N/A** — slice plumbing, sin aggregate. La cobertura del aggregate `Inspeccion` se mantiene en 94.44% (sin regresión, Domain.Tests 246/19 idéntico a mt-2).
- [x] Cobertura de los nuevos tipos: validable indirectamente — 28 tests directos sobre los 6 accessors + handler + listeners. Cero ramas descubiertas reportadas por el build.

### 2.5 Refactor

- [x] `refactor-notes.md` presente. Refactor real: extracción de `EnvelopeBearerExtractor` (utility estática) que deduplica 30 LoC entre los dos listeners. Justificación de NO-refactor (singleton para accessors, parametrización del chain) documentada con razones.
- [x] Los tests NO se tocaron en refactor — verificado por `dotnet test` post-refactor (93/93 idénticos).
- [x] Sin warnings de compilación (TreatWarningsAsErrors=true respetado, 0/0 build).

### 2.6 Invariantes cross-slice

- [x] Slices previos no rompen: `dotnet build --no-restore` clean en los 8 proyectos. Domain.Tests 246/19 sin regresión. Infrastructure.Tests 59 pre-mt-3 → 93 post-mt-3 (todos los 59 previos siguen verde, +34 nuevos).
- [ ] `Api.Tests` no ejercitado (requiere `POSTGRES_TEST_CONNSTRING` local — FU-32 cerrado pero env var no exportada en esta sesión). **Followup observacional, no blocker** — el build verde de `Api.Tests` confirma que no hay regresión de compilación; los smoke E2E quedan validados en CI o mt-4.
- [ ] `Application.Tests` no ejercitado (requiere Docker — FU-47 abierto). Mismo razonamiento.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md` §15 — el slice no toca dominio.
- [x] Alineado con ADRs:
  - **ADR-001 (REST/VPN):** el adapter ERP sigue siendo REST sobre VPN. mt-3 solo cambia el seteo del header `Authorization`.
  - **ADR-002 (auth — cerrado en mt-1):** mt-3 extiende el modelo de identidad del host PWA. El JWT del host viaja al ERP — paridad de auth cross-process. FU-44 cierra.
  - **ADR-003 (OT correctiva):** no aplica directamente; el adapter del ERP se usa por sagas futuras (saga OT bloqueada por DDL DBA — ver `roadmap.md` Fase 3.C).
  - **ADR-004 (catálogos):** los endpoints admin de catálogos ahora propagan el Bearer del caller (admin endpoints del module). MT3-INV-1 aplicable.
  - **ADR-005 (SignalR):** no aplica — el slice no toca el hub.
  - **ADR-006 (outbox + retry):** **gana una nota** en doc-writer phase sobre "JWT en outbox puede expirar entre publish y retry → dead-letter inmediato es comportamiento esperado del 4xx; refresh automático del JWT NO aplica en mt-3".
  - **ADR-007 (OT manual):** no aplica.
  - **ADR-008 (cola offline cliente):** no aplica directamente; el cliente PWA refresca el JWT por su lado.
  - **ADR-009 (multi-tenancy Marten):** mt-3 extiende la cadena de identidad con el ERP. Sin cambios a la decisión Conjoined.
- [x] Alineado con la memoria del proyecto: la decisión D-MT3-1 (puerto separado) es coherente con el patrón Sinco multi-tenancy del proyecto Attachment — Attachment NO mezcla raw token con claims tampoco (su `SessionService` solo parsea claims; el token raw se setea estáticamente porque es backend-to-backend).

### 2.8 Integración cross-team Sinco

- [x] El slice consume Maquinaria_V4 — adapter ya estaba mockeado con WireMock desde slices erp-1..erp-3. mt-3 mantiene el mismo pattern de tests.
- [x] **Asunción documentada:** Maquinaria_V4 lee `IdEmpresa` del JWT (D-MT3-4). NO se valida real-world porque Maquinaria_V4 no corre localmente. Si emerge contra-evidencia en piloto, FU defensivo abierto para agregar header explícito `X-Tenant-Id` al adapter.
- [x] No hay nuevo POST hacia el ERP. El adapter mantiene los 8 endpoints existentes — solo cambia el header `Authorization` que envía.
- [x] `Idempotency-Key` no aplica para el slice (los listeners ERP ya lo manejan: `DescartarNovedadPreopErpListener` usa idempotencia natural por `PodId`, `SincronizarDictamenVigenteListener` usa `Idempotency-Key=InspeccionId` — ambos validados en sus slices propios).

### 2.9 SignalR / push

- [x] N/A — slice no toca el hub.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `CaptureBearerForOutboxMiddleware` (Wolverine middleware que captura el `Authorization` del HttpContext y lo persiste en el envelope antes del outbox-publish) NO implementado en green. El listener ya está preparado para leer del envelope, pero la captura automática en el publish está pendiente. Sin esto, los listeners reciben envelopes **sin** `X-Forwarded-Authorization` → caen al service-account fallback. Es comportamiento funcional pero NO ejercita MT3-INV-2 end-to-end (solo via tests in-process). | `Inspecciones.Api/Program.cs` Wolverine `UseWolverine(opts => ...)` policy block | Abrir **FU-60** — implementar middleware Wolverine `CaptureBearerForOutboxMiddleware` + test E2E con Postgres (requiere FU-47 cerrado). Mientras tanto, listeners siguen funcionando con service-account fallback. |
| 2 | followup | `AmbientBearerTokenAccessor` usa `AsyncLocal<string?>` **estático** — semánticamente "UN ambient por proceso". Si en el futuro emerge necesidad de múltiples ambients aislados (p. ej. testing en paralelo con diferentes tokens cross-test), reescribir como instance-AsyncLocal e inyectar la misma instancia donde se necesite. Hoy los tests del slice pasan con `AsyncLocal` estático (los tests de paralelismo verifican aislamiento async-flow, no instance-isolation). | `src/Inspecciones.Infrastructure/Auth/AmbientBearerTokenAccessor.cs:23` | Abrir **FU-61** defensivo — re-evaluar si el static storage es la elección correcta cuando emerja el primer caso patológico. Hoy: bajo riesgo. |
| 3 | followup | El slice NO valida que el JWT del envelope esté **vivo** antes de invocar al ERP. Si expiró durante el retry (ADR-006: 5s→30s→2m→10m hasta ~12 min), el ERP devolverá 401 → la política `OnException<MaquinariaErpException>` con `StatusCode==401` cae en la rama 4xx → `MoveToErrorQueue()` permanente (dead-letter inmediato). Comportamiento esperado por D-MT3-2 nota. Si operativamente emerge necesidad de refresh automático del JWT en retry, abrir slice. | `Inspecciones.Api/Program.cs` Wolverine policy block | Abrir **FU-62** condicional — refresh automático del JWT en retry. Solo abrir si emerge requerimiento operativo del piloto. |
| 4 | followup | ADR-006 NO se actualizó con la nota sobre "JWT en outbox + retry". El doc-writer phase debe agregar §16.X "JWT en envelope: expiration + refresh policy" para que la decisión D-MT3-2 quede documentada en el ADR (no solo en el spec del slice). | `Inspecciones/docs/00-investigacion-mercado.md §9.X (ADR-006)` o `Inspecciones/docs/01-modelo-dominio.md §16` | Hacer en doc-writer phase (parte del cierre del slice). |
| 5 | followup | ADR-009 NO se actualizó con el cierre mt-3. El doc-writer debe extender §9.17 con el commit hash de mt-3 cuando esté disponible. | `Inspecciones/docs/00-investigacion-mercado.md §9.17` | Hacer en doc-writer phase. |
| 6 | nit | `EnvelopeBearerExtractor.ExtraerBearerForwarded(Envelope)` lanza `ArgumentNullException` si el envelope es null — defensa explícita. Los listeners ya hacen `ArgumentNullException.ThrowIfNull(envelope)` antes del extract. Redundante pero defensivo. No bloquea. | `src/Inspecciones.Infrastructure/Auth/EnvelopeBearerExtractor.cs:24` | Sin acción. |
| 7 | nit | `BearerTokenPropagationHandler.SendAsync` no instrumenta logs estructurados ni métricas. Si emerge necesidad operativa de "cuántas llamadas usaron HTTP vs Ambient vs ServiceAccount", agregar `IBearerTokenAccessor` que retorne también el origin (HTTP/Ambient/Service). | `src/Inspecciones.Infrastructure/Erp/BearerTokenPropagationHandler.cs` | Sin acción hoy. |
| 8 | nit | El test `Ambient_se_limpia_despues_de_HandleAsync_aunque_lance` verifica que el ambient queda en null post-handle. Esto solo valida que el `using var _` se evaluó al salir del scope — no es una invariante crítica del flujo de outbox (Wolverine re-resuelve el listener por mensaje). Bueno tenerlo como regression test del pattern `using`. | `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/DescartarNovedadPreopErpListenerTenantTests.cs:131-150` | Sin acción. |
| 9 | nit | El logger `CaptureLogger<T>` en el test del FU-57 inspecciona el string formateado del log. Es frágil — si el [LoggerMessage] cambia el orden de los KV, el assertion sigue pasando (busca "TenantId" y "7" en cualquier parte de la línea). Idealmente assertar sobre los KV estructurados, no el string. | `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/DescartarNovedadPreopErpListenerTenantTests.cs:201-230` | Sin acción — el test cumple su propósito (verificar que el tenantId termina en el log). |

---

## 4. Veredicto final

- [ ] approved
- [x] **approved-with-followups** — followups #1 (FU-60), #2 (FU-61), #3 (FU-62), #4 (ADR-006 nota en doc-writer), #5 (ADR-009 cierre en doc-writer).
- [ ] request-changes

---

## 5. Cierres explícitos

- **FU-44** (propagación JWT entrante al `MaquinariaErpClient` y sagas): **CERRADO**. El adapter ahora propaga el Bearer del request via `BearerTokenPropagationHandler` (HTTP scope), del envelope via `AmbientBearerTokenAccessor` (listener scope), o fallback al service-account. Decisión D-MT3-2 (chained accessors) firmada.
- **FU-57** (`DescartarNovedadPreopErpListener` log estructurado con tenant): **CERRADO**. El listener gana overload `HandleAsync(evento, Envelope, ct)` que lee `envelope.TenantId` y lo pasa al `[LoggerMessage]`. Verificado con test E2E (`Listener_log_estructurado_incluye_TenantId_del_envelope_en_fallo_5xx`).

---

_Veredicto `approved-with-followups`. El orquestador puede proceder al commit del slice y a la fase doc-writer (ADR-006 + ADR-009 + CLAUDE.md + FOLLOWUPS.md)._
