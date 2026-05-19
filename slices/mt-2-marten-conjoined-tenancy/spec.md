# Slice mt-2 — Marten `Conjoined` multi-tenancy por `IdEmpresa`

**Autor:** orquestador (rol domain-modeler — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-2)
**Fecha:** 2026-05-19
**Estado:** **Firmado: 2026-05-19** — autor firma: Usuario (Santiago Ramirez), autorización pre-otorgada en el ciclo mt-2.
**Agregado afectado:** ninguno (el aggregate `Inspeccion` y sus eventos `_v1` no se tocan — D3 firmada). El slice opera sobre Marten/Wolverine plumbing: políticas de tenancy, factory de sesiones, refactor de repos y handlers que abren sesión, y migración de schema.
**Decisiones previas relevantes:**
- `slices/mt-1-jwt-claims-pipeline/spec.md` §0.D2..D5 — Conjoined, ETag en envelope, reset dev/backfill staging, todos los catálogos por-empresa.
- `Inspecciones/docs/00-investigacion-mercado.md §9.17` — ADR-009 Multi-tenancy Marten conjoined (creado en mt-1, enforcement aquí).
- `Inspecciones/docs/00-investigacion-mercado.md §9.14` — ADR-002 cerrado en mt-1; `ISessionService.IdEmpresa` ya disponible.
- `CLAUDE.md` "Reglas duras de calidad" — `nullable`, `TreatWarningsAsErrors`, `Apply` puro, atomicidad de eventos, identidad nunca lee `HttpContext` desde dominio.
- `FOLLOWUPS.md #44` — propagación JWT entrante a `MaquinariaErpClient` rolla a mt-3 (no se toca en mt-2).
- `FOLLOWUPS.md #55` placeholder — caché Marten con switch de tenant mid-session: se abrirá si emerge durante green.

---

## 1. Intención

Con `ISessionService.IdEmpresa` ya disponible en cada request HTTP (cerrado en mt-1), el módulo todavía persiste y lee de Marten en modo **single-tenant**: una sola tabla por document type, sin discriminador `tenant_id`. Cualquier handler que abra `IDocumentSession.LightweightSession()` o reciba uno scoped via DI escribe contra un namespace global compartido entre empresas. Esto bloquea el piloto: una empresa puede leer streams o catálogos de otra, y no hay forma de aislar data en la misma DB Postgres.

Este slice **cablea el switch a Marten `Conjoined`** (D2 firmada): activa la política `AllDocumentsAreMultiTenanted()` y `Events.TenancyStyle = TenancyStyle.Conjoined` en `StoreOptions`, introduce un puerto `ITenantedDocumentSessionFactory` que abre sesiones con `tenantId = session.IdEmpresa.ToString(CultureInfo.InvariantCulture)` leyendo del `ISessionService`, refactoriza el `IDocumentSession` scoped registrado en DI para que sea siempre tenanted, propaga el tenant al `MartenCatalogoSyncRepository` (que recibe `IDocumentStore`) y al `MartenInspeccionReader` (que recibe `IQuerySession`). Los listeners Wolverine (`DescartarNovedadPreopErpListener`, `SincronizarDictamenVigenteListener`) heredan el tenant del envelope del evento entrante — Wolverine + Marten Conjoined garantiza que `IDocumentSession` inyectado al handler de mensaje viene con el `tenant_id` del envelope.

Con mt-2 cerrado, **cada operación de lectura/escritura Marten en código de producción está discriminada por `tenant_id`**. mt-3 puede arrancar sobre una base que enforce aislamiento por test.

---

## 2. Comando

Este slice **no es event-sourced**. No hay aggregate, no hay comando de dominio nuevo. El "comando" lógico es la apertura tenanted de cada sesión Marten — equivalente al contrato del puerto que introducimos:

```csharp
public interface ITenantedDocumentSessionFactory
{
    /// <summary>
    /// Abre una <see cref="IDocumentSession"/> lightweight con el tenant del
    /// <see cref="ISessionService"/> actual. Lanza <see cref="ClaimRequeridaException"/>
    /// si <c>IdEmpresa</c> no está disponible (en env Test usa el default del fake).
    /// </summary>
    IDocumentSession OpenSession();

    /// <summary>
    /// Abre una <see cref="IQuerySession"/> de solo lectura con el tenant actual.
    /// Útil para handlers que solo leen el aggregate (p. ej. <see cref="MartenInspeccionReader"/>).
    /// </summary>
    IQuerySession OpenQuerySession();

    /// <summary>
    /// Abre una sesión con un tenant arbitrario — solo para listeners Wolverine
    /// que ya conocen el tenant del envelope o tests E2E cross-tenant. NO se usa
    /// desde código HTTP de producción (validable por code review/regla).
    /// </summary>
    IDocumentSession OpenSessionForTenant(string tenantId);
}
```

Implementación de producción `TenantedDocumentSessionFactory(IDocumentStore store, ISessionService session)`:
- `OpenSession()` → `store.LightweightSession(session.IdEmpresa.ToString(CultureInfo.InvariantCulture))`.
- `OpenQuerySession()` → `store.QuerySession(SessionOptions { TenantId = session.IdEmpresa.ToString(...) })` (o equivalente API).
- `OpenSessionForTenant(tenantId)` → `store.LightweightSession(tenantId)` directo.

**Cambio en DI:** el `IDocumentSession` scoped que registra `AddMarten().IntegrateWithWolverine()` (today inyectado a los 15 handlers) se reemplaza por una **factory** del scoped — Marten 7 expone `services.AddMarten(...).BuildSessionsWith<ITenantedSessionFactory>()` o, alternativamente, sobrescribimos el `IDocumentSession` scoped con un proveedor custom que delega a la factory. Decisión específica de cableado en D-MT2-1 (§5).

---

## 3. Evento(s) emitido(s)

**Cero eventos de dominio.** El slice no toca aggregates ni emite mensajes nuevos.

Tabla de mutaciones de schema (no de stream):

| Mutación de schema | Cuándo |
|---|---|
| Cada tabla document type (`mt_doc_equipo_local`, `mt_doc_rutinatecnica_local`, etc.) gana columna `tenant_id varchar` con índice. | Al primer arranque post-mt-2 (Marten lo aplica automáticamente con `AutoCreateSchemaObjects = CreateOrUpdate` en Development). |
| Tabla del event store (`mt_events`, `mt_streams`) gana `tenant_id`. | Idem — Marten reconstruye según `Events.TenancyStyle = Conjoined`. |
| Schema `inspecciones` se **dropea y recrea** en dev al primer arranque. | D4: se asume que no hay data prod por-empresa que migrar. |

---

## 4. Precondiciones

No aplican PRE de aggregate. Documentamos las del wiring Marten + ISessionService:

- **`MT2-PRE-1`** — `ISessionService.IdEmpresa` resolvible en el scope HTTP cuando un handler abre sesión. Si el getter lanza `ClaimRequeridaException`, la factory propaga la excepción y el middleware global de `Program.cs` (instalado en mt-1) la mapea a `401 CLAIM-IDEMPRESA-AUSENTE`. Sin cambios al middleware — heredamos el comportamiento.
- **`MT2-PRE-2`** — listener Wolverine corre fuera de scope HTTP. El tenant **debe** venir del envelope del mensaje (`Wolverine.IMessageContext.TenantId` o equivalente). Si el envelope no trae `TenantId` (p. ej. mensaje legacy publicado antes de mt-2), el listener lanza `TenantRequeridoEnEnvelopeException` → dead-letter inmediato (consistente con la política ADR-006 §16 para errores permanentes). Esta excepción es **nueva** del slice mt-2 y vive en `Inspecciones.Infrastructure.Auth`.
- **`MT2-PRE-3`** — `MartenCatalogoSyncRepository` recibe `IDocumentStore` (singleton) y debe abrir cada `LightweightSession` con el `tenantId` del `ISessionService` actual. Se cablea via inyección de `ITenantedDocumentSessionFactory` (refactor del constructor — el repo deja de recibir `IDocumentStore` directo). Esto es **breaking change interno** pero sin impacto en endpoints HTTP.

---

## 5. Invariantes tocadas / decisiones de diseño

No aplican invariantes de aggregate. Se documentan los invariantes del wiring y las decisiones:

**MT2-INV-1 — Toda apertura de sesión Marten en código de producción pasa por `ITenantedDocumentSessionFactory`.**
Prohibido `store.LightweightSession()` directo sin tenant en `src/`. Esta regla se agrega a `CLAUDE.md` "Reglas duras de calidad" como parte del cierre de doc-writer del slice. Aplicaciones legales del bypass `OpenSessionForTenant(tenantId)`: (a) listeners Wolverine que leen el tenant del envelope, (b) tests E2E que validan cross-tenant isolation, (c) operaciones de bootstrap/admin sin contexto HTTP (no existen hoy en el módulo).

**MT2-INV-2 — Stream del aggregate `Inspeccion` de empresa A no es accesible (lectura ni escritura) desde una sesión abierta con tenant B.**
Validable por test E2E: dos `WebApplicationFactory<Program>` con tenants distintos (vía `WithSessionService(FakeSessionService(idEmpresa: 7))` y `WithSessionService(FakeSessionService(idEmpresa: 8))`) operan sobre el mismo `InspeccionId` y obtienen aislamiento — la sesión del tenant 8 no ve el stream del tenant 7. Marten Conjoined garantiza esto vía filter automático en `AggregateStreamAsync` y `Events.Append`.

**MT2-INV-3 — Catálogos por-empresa: documents (`EquipoLocal`, `RutinaTecnicaLocal`, `RutinaMonitoreoLocal`, `RepuestoLocal`, `CausaFallaCatalogo`, `TipoFallaCatalogo`, `CatalogoSyncState`) sincronizados con tenant A NO retornan registros de tenant B en lecturas.**
Aplicación D5 firmada en mt-1: todos son `Conjoined`, sin excepciones single-tenant. Validable por test que llama `POST /api/v1/catalogos/sync` con dos tenants distintos, verifica que cada uno tiene su propio ETag (`CatalogoSyncState`) y que las queries de catálogo respetan el tenant.

**MT2-INV-4 — Atomicidad cross-catálogo del sync se preserva por tenant.**
El `MartenCatalogoSyncRepository` abre `LightweightSession` propia por catálogo (sin compartirla en `Task.WhenAll` — hallazgo erp-4). Después de mt-2, cada una de esas sesiones se abre via factory con el tenant del request. Cross-catálogo y cross-tenant: sin cambios al modelo de partial-failure existente (cada catálogo es atómico por sí mismo).

**D-MT2-1 — Wiring: `IDocumentSession` scoped en DI delega al factory.**
En vez de tocar los constructores de los 15+ handlers que reciben `IDocumentSession`, registramos el `IDocumentSession` scoped como un factory delegate que abre via `ITenantedDocumentSessionFactory.OpenSession()`. Esto se hace post-`AddMarten().IntegrateWithWolverine()` con un `services.AddScoped<IDocumentSession>(sp => sp.GetRequiredService<ITenantedDocumentSessionFactory>().OpenSession())`. **Verificación pendiente en green:** Marten + Wolverine integration aún acepta este override sin romper outbox transaccional. Si no, fallback es D-MT2-1' (variante): inyectar factory a cada handler (más invasivo pero más explícito). El modelador asume D-MT2-1 hasta evidencia contraria; si emerge bloqueo, se reabre la decisión con followup.

**D-MT2-2 — Listeners Wolverine: tenant del envelope, no del evento.**
Los listeners ERP (`DescartarNovedadPreopErpListener`, `SincronizarDictamenVigenteListener`) NO reciben tenant en el shape del evento `_v1` (D3 firmada — no bumpear). El tenant llega vía `Wolverine.IMessageContext.TenantId` que Wolverine propaga automáticamente del envelope del mensaje publicado por el outbox. Marten + Wolverine integration con Conjoined: cuando el handler HTTP llama `_session.Events.Append(...)` con tenant=7, el outbox persiste el mensaje con `tenant_id=7`; cuando el listener despacha, Wolverine inyecta `IDocumentSession` ya tenanted y `IMessageContext.TenantId="7"`.

Para el listener `SincronizarDictamenVigenteListener` específicamente: el `IInspeccionReader.LeerAsync(inspeccionId)` debe usar la sesión tenanted del envelope. Cambiamos `MartenInspeccionReader` para que reciba `ITenantedDocumentSessionFactory` y abra `OpenQuerySession()` por invocación, **o** lo dejamos recibir `IQuerySession` y confiamos en que Wolverine inyecte uno tenanted via Marten integration. **Decisión modelador:** dejarlo recibir `IQuerySession` (status quo) — Wolverine + Marten Conjoined garantiza que la sesión inyectada es tenanted del envelope. Si en green emerge que Wolverine no provee `IQuerySession` tenanted por default, fallback es inyectar la factory. Followup latente.

**D-MT2-3 — Migración: reset dev + backfill staging.**
D4 firmada: reset del schema `inspecciones` en dev. La fixture `InspeccionesAppFactory` ya hace `DROP SCHEMA IF EXISTS inspecciones CASCADE` entre corridas — sin cambios. En dev local, el desarrollador corre `docker compose down -v` o equivalente para wipe — documentado en green-notes. Backfill staging: scripted con `UPDATE inspecciones.<tabla> SET tenant_id = '0' WHERE tenant_id IS NULL` (se prepara como SQL guardado, pero no se ejecuta en este slice — staging es responsabilidad post-merge). Producción: no aplica, el módulo no está en prod.

**D-MT2-4 — Tenant default `"0"` en bypass extremos.**
Si por bug `ISessionService.IdEmpresa` retorna 0 (en env Test el fake default es `IdEmpresa=1`; si alguien lo construye con `idEmpresa: 0`, será `"0"`), se acepta — el comportamiento es determinista (todo el data va a tenant "0", sin leak cross-tenant). NO se mapea a un default mágico. La excepción se reserva para el caso "claim ausente" (manejado por `ClaimRequeridaException`).

**D-MT2-5 — Tests `Infrastructure.Tests`: patrón "puerto + fake" se preserva.**
Los tests del listener `DescartarNovedadPreopErpListener` y `SincronizarDictamenVigenteListener` usan `FakeInspeccionReader` / `FakeCatalogoSyncRepository`. mt-2 introduce `FakeTenantedDocumentSessionFactory` (en `tests/Inspecciones.Infrastructure.Tests/`) que retorna sesiones contra un Marten embebido o lo que ya hagan los tests. **Riesgo:** los tests de `MartenCatalogoSyncRepository` y `MartenInspeccionReader` que tocan Marten real (si existen — verificar en green) requieren Postgres. Si están bajo skip por FU-47 (Docker), se mantienen así. Si no, se ajustan.

**D-MT2-6 — Migración de proyección `InspeccionAbiertaPorEquipoView`.**
La proyección `InspeccionAbiertaPorEquipoProjection` (registrada inline en `Program.cs`) ya opera sobre la sesión Marten del slice 1g — al volverse tenanted, el view también queda particionado por `tenant_id` automáticamente. **Marten Conjoined aplica el tenant a inline projections** sin intervención del developer (es el comportamiento esperado del feature). Validable por test cross-tenant: dos inspecciones del mismo `EquipoId` en tenants distintos abren ambas, y cada `WithSessionService(idEmpresa=N)` ve solo la suya.

**D-MT2-7 — `TestHeaderAwareSessionService` debe pasar el `idEmpresa` al header opcional.**
Para tests legacy que necesitan tenants distintos sin reescribir el setup, agregamos soporte a un header `X-Sin-IdEmpresa: <int>` análogo al `X-Tecnico-Id` ya existente. Tests del slice mt-2 prefieren `WithSessionService(FakeSessionService(idEmpresa: N))` puro. Default de `TestHeaderAwareSessionService.IdEmpresa` permanece en `1` (compat con tests pre-mt-2 que asumen tenant único).

**D-MT2-8 — Sin cambios al dominio ni a los eventos.**
Cero cambios a `Inspecciones.Domain.*`. Cero cambios a eventos `*_v1`. Cero re-emisiones. Cero tests de dominio nuevos o tocados. Cobertura del aggregate `Inspeccion` se mantiene en 94.44% (no se mueve).

**D-MT2-9 — `SincoMiddlewareSessionService` no se toca.**
La implementación de producción del puerto sigue como está. mt-2 solo agrega `TenantedDocumentSessionFactory` que la consume.

**D-MT2-10 — Wolverine outbox + Conjoined: integration intact.**
Marten 7 + Wolverine 3 integran outbox conjoined-aware desde la versión actual del repo (`Wolverine.Marten` paquete). El outbox persiste `tenant_id` en `wolverine_outgoing_envelopes` y lo propaga a los listeners. **Verificable en green** corriendo un test que: (a) llama `POST /api/v1/inspecciones/{id}/firmar` con tenant 7, (b) verifica que `InspeccionFirmada_v1` queda en el outbox con `tenant_id=7`, (c) verifica que el listener `SincronizarDictamenVigenteListener` lo procesa con `IMessageContext.TenantId="7"` y llama `MartenInspeccionReader.LeerAsync(inspeccionId)` que retorna el aggregate del tenant 7. Si Wolverine no propaga el tenant, este es el bloqueo que requiere paro y reporte (consigna del usuario).

---

## 6. Escenarios Given / When / Then

Como mt-2 no tiene aggregate, los escenarios son **HTTP end-to-end + tests de unidad del factory + tests de listener con bus en memoria**. Mínimo siete escenarios.

### 6.1 Happy path — factory abre sesión con `tenantId` del `ISessionService`

**Given**
- `FakeSessionService(idEmpresa: 7)` registrado en DI.
- `ITenantedDocumentSessionFactory` registrado con la impl de producción.

**When**
- Algún consumidor llama `factory.OpenSession()`.

**Then**
- La sesión retornada expone `TenantId == "7"`.
- Cualquier `Store()` o `Events.Append()` sobre esa sesión persiste con `tenant_id='7'`.

> Test unitario contra un Marten embebido (Postgres real vía Testcontainers o local) en `Infrastructure.Tests`.

### 6.2 Cross-tenant isolation del aggregate — `InspeccionIniciada_v1` de tenant 7 no visible en tenant 8

**Given**
- `FakeSessionService(idEmpresa: 7)` activo.
- Setup canónico de slice 1b (equipo `100020` sembrado **en el tenant 7** vía sesión tenanted).
- `POST /api/v1/inspecciones` ejecutado, retorna `InspeccionId = X`, status `201`.

**When**
- Se reemplaza el `ISessionService` por `FakeSessionService(idEmpresa: 8)` vía `WithSessionService(...)`.
- Se llama `GET /api/v1/inspecciones/{X}` (o equivalente — alternativa: leer el aggregate via factory tenant=8).

**Then**
- La lectura retorna `null` / 404 / aggregate vacío (depende del endpoint). El stream `X` no existe en tenant 8.
- En tenant 7 sigue existiendo con todos sus eventos.

> Test E2E contra `WebApplicationFactory<Program>` real. Si no hay endpoint GET para leer el aggregate, se valida abriendo una sesión tenanted directa contra el `IDocumentStore` del fixture (sin pasar por endpoint).

### 6.3 Cross-tenant isolation de catálogos — `CatalogoSyncState` por tenant

**Given**
- `FakeSessionService(idEmpresa: 7)` activo. Se llama `POST /api/v1/catalogos/sync` con un payload de 2 `causas-falla`. Retorna `200`, ETag `"v1-tenant7"`.

**When**
- Se reemplaza por `FakeSessionService(idEmpresa: 8)`.
- Se llama `POST /api/v1/catalogos/sync` con un payload de 3 `causas-falla` (distintas). Retorna `200`, ETag `"v1-tenant8"`.

**Then**
- Una query `session.OpenSessionForTenant("7").Query<CausaFallaCatalogo>().CountAsync()` retorna 2.
- `session.OpenSessionForTenant("8").Query<CausaFallaCatalogo>().CountAsync()` retorna 3.
- `LeerSyncStateAsync("causas-falla")` retorna `CatalogoSyncState { Etag = "v1-tenant7" }` con tenant 7 y `CatalogoSyncState { Etag = "v1-tenant8" }` con tenant 8.

### 6.4 Lectura sin tenant lanza — `factory.OpenSession()` cuando `IdEmpresa` ausente

**Given**
- `FakeSessionService(lanzarEnClaim: "IdEmpresa")` registrado.

**When**
- Cualquier endpoint que toque Marten (p. ej. `POST /api/v1/inspecciones`).

**Then**
- Status `401 Unauthorized` con body `{ codigoError: "CLAIM-IDEMPRESA-AUSENTE", ... }` (heredado del middleware global mt-1 sin cambios).
- Cero sesiones Marten creadas (verificable indirectamente por ausencia de stream para el `InspeccionId` propuesto).

### 6.5 Listener Wolverine recibe tenant del envelope — happy path

**Given**
- Tenant `"7"` activo. `POST /api/v1/inspecciones/{id}/firmar` ejecutado para una inspección abierta del tenant 7, status `200`. `InspeccionFirmada_v1` persistida en el outbox con `tenant_id="7"`.
- `FakeMaquinariaErpClient` registra calls (en lugar del cliente HTTP real).

**When**
- Wolverine despacha `InspeccionFirmada_v1` al `SincronizarDictamenVigenteListener`.

**Then**
- El listener invoca `_inspeccionReader.LeerAsync(inspeccionId)` y la sesión Marten subyacente está tenanted con `"7"` → retorna el aggregate correcto.
- `FakeMaquinariaErpClient.ActualizarDictamenEquipoAsync` se invoca con el `equipoId` del aggregate del tenant 7.
- Verificado con asserts: 1 call al fake client, parámetros esperados.

> Test integración con bus en memoria de Wolverine. Marten embebido. Sin HTTP real al ERP — `FakeMaquinariaErpClient` (nuevo doble interno del slice o uno existente).

### 6.6 Listener Wolverine sin tenant en envelope → dead-letter inmediato

**Given**
- Mensaje `InspeccionFirmada_v1` publicado manualmente sin tenant (`Envelope.TenantId == null`) — simula caso patológico de mensaje legacy o bug.

**When**
- Wolverine despacha al listener.

**Then**
- El listener (o el wiring del factory) lanza `TenantRequeridoEnEnvelopeException` → política ADR-006 lo mueve a dead-letter inmediato (no reintenta).
- `FakeMaquinariaErpClient.ActualizarDictamenEquipoAsync` NO se invoca.

> Documenta la semántica D-MT2-2 con un test explícito. Defensa en profundidad contra publicación accidental sin tenant.

### 6.7 Rebuild desde stream — N/A

El slice no emite eventos. §6.X omitido por convención template §6.X (idéntico a mt-1).

### 6.8 Wolverine outbox preserva el tenant — atomicidad transaccional

**Given**
- Tenant `"7"` activo.

**When**
- `POST /api/v1/inspecciones/{id}/firmar` ejecutado en tenant 7.

**Then**
- `_session.Events.Append(streamId, evento)` + `_session.SaveChangesAsync()` del handler `FirmarInspeccionHandler` persiste 3 eventos en el stream con `tenant_id="7"`.
- El envelope outbound a Wolverine outbox lleva `tenant_id="7"` (verificable inspeccionando `wolverine_outgoing_envelopes` post-commit, o vía test bus con assertion sobre envelope publicado).
- Listener (cuando despachado) usa tenant `"7"`.

> Test E2E con `WebApplicationFactory<Program>`. Garantiza que la integración Marten + Wolverine + Conjoined funciona end-to-end (D-MT2-10).

### 6.9 Smoke regresión — los 65 tests `Api.Tests` existentes siguen verde

**Given**
- Suite actual `Api.Tests` (65 pass + 7 skip al cierre de mt-1).

**When**
- Se corre `dotnet test tests/Inspecciones.Api.Tests/` post-cambios mt-2.

**Then**
- 65 pass + 7 skip (igual o mejor). No regresión.

> Validable solo en green, pero el spec lo lista como criterio de DoD.

---

## 7. Idempotencia / retries

- **Apertura de sesión tenanted es idempotente** — `factory.OpenSession()` N veces produce sesiones distintas pero todas con el mismo `TenantId`. No hay estado mutable compartido.
- **Sync de catálogos cross-tenant es idempotente** — el wipe-and-replace por catálogo dentro de `MartenCatalogoSyncRepository` ya lo era (erp-4); tenant solo es el discriminador que aísla.
- **Listeners Wolverine reintentan respetando el tenant** — la política ADR-006 (5xx retry con backoff) sigue funcionando idéntica; el outbox preserva el `tenant_id` en cada reintento.

---

## 8. Impacto en proyecciones / read models

- **`InspeccionAbiertaPorEquipoView`** — proyección inline existente. Marten Conjoined la particiona automáticamente: cada empresa tiene su propia vista de "qué equipos tienen inspección activa". Sin cambios al código de la proyección.
- **`CatalogoSyncState`** — document state existente. Por-empresa con tenant_id; ETag por catálogo por empresa.
- **Tablas Marten internas (`mt_events`, `mt_streams`)** — ganan `tenant_id` automáticamente al activar Conjoined.
- **Tablas Wolverine (`wolverine_*`)** — Wolverine + Marten integration con Conjoined propaga `tenant_id` al outbox. **Verificable en green.**

---

## 9. Impacto en endpoints HTTP

**Cero endpoints nuevos.** Cero endpoints removidos. Cero cambios al request/response shape de los 15 endpoints HTTP existentes.

**Cambios internos:** cada endpoint que despacha a un handler que abre `IDocumentSession` ahora obtiene una sesión tenanted (transparente vía DI).

Códigos HTTP nuevos: ninguno. Los `401 CLAIM-IDEMPRESA-AUSENTE` y `403 PRE-1` ya existen de mt-1.

OpenAPI: sin cambios.

---

## 10. Impacto en SignalR / push (si aplica)

- **Cero impacto** en SignalR. mt-2 no toca el hub.
- **Diferido a mt-3 / piloto:** la audiencia del hub debería filtrarse por tenant (`Group = $"{idEmpresa}-{tecnicoId}"`) — followup latente si emerge.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

- **`MaquinariaErpClient` no se toca en mt-2** (decisión D-MT1-10 / FU-44 rolla a mt-3).
- Los listeners `DescartarNovedadPreopErpListener` y `SincronizarDictamenVigenteListener` siguen llamando al adapter con el `JwtToken` de config (token fijo).
- **Tenant propagation cross-process al ERP rolla a mt-3.**
- Estado de disponibilidad: 🟢 disponible (sin cambios al adapter).

---

## 12. Preguntas abiertas

Todas las decisiones del sub-track multi-tenancy (D1..D7) ya están firmadas en mt-1. Las preguntas operativas de mt-2 se resuelven por defaults razonables (autorización pre-otorgada por el usuario):

### A. ¿`IDocumentSession` scoped delegado al factory funciona con Wolverine outbox?

**Recomendación del modelador:** D-MT2-1 (delegate). Si en green falla integration con outbox, fallback a D-MT2-1' (inyectar factory a cada handler).
**Decisión firmada 2026-05-19 — D-MT2-1 con escape hatch a D-MT2-1' si emerge bloqueo técnico.** Reportable como sorpresa al cierre.

### B. ¿`MartenInspeccionReader` debe recibir factory o seguir con `IQuerySession`?

**Recomendación del modelador:** mantener `IQuerySession` (status quo). Wolverine + Marten Conjoined debería inyectar uno tenanted por contexto del envelope.
**Decisión firmada 2026-05-19 — mantener IQuerySession.** Si Wolverine no inyecta tenanted automáticamente, refactorizar a factory en green (anotable como deviation en green-notes).

### C. ¿Listener test cross-tenant requiere Postgres real?

**Recomendación del modelador:** los listeners siguen testeables con `FakeInspeccionReader` + `FakeMaquinariaErpClient` para los escenarios de ADR-006. El test de **tenant del envelope** (§6.5 y §6.6) sí requiere Marten + Wolverine reales — vive en `Infrastructure.Tests` con un puerto fake del Inspeccion reader que verifica el `IMessageContext.TenantId` recibido.
**Decisión firmada 2026-05-19 — fake reader con assert sobre tenant.** El listener real recibe `IMessageContext` cuyo `TenantId` es asertable. No requiere Marten real para los tests del listener. Los tests de **integración Marten + Wolverine + Conjoined** (§6.8) sí requieren Postgres y viven en `Api.Tests` (que ya está configurado con `InspeccionesAppFactory` y switch local/Testcontainers).

### D. Backfill de staging — ¿se ejecuta en mt-2?

**Recomendación del modelador:** NO. mt-2 prepara el SQL (commit del archivo `migrations/mt-2-backfill-tenant-default.sql`) pero no lo ejecuta. La ejecución es responsabilidad de ops/post-merge.
**Decisión firmada 2026-05-19 — preparar SQL, no ejecutar.**

### E. `RutinaMonitoreoLocal`, `RepuestoLocal`, `RutinaTecnicaLocal`, `ParteEquipoLocal` — ¿conjoined o single-tenant?

**Recomendación del modelador:** D5 firmada — TODOS conjoined. Sin excepciones.
**Decisión firmada 2026-05-19 — todos conjoined (D5).**

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (MT2-PRE-1..3) mapean a un escenario Then (§6.4, §6.6, §6.3).
- [x] Todas las invariantes (MT2-INV-1..4) están explícitas en §5 con escenarios asociados.
- [x] Happy path presente (§6.1, §6.2, §6.5, §6.8).
- [x] Rebuild desde stream **no aplica** (§6.7 explícito).
- [x] Preguntas abiertas (§12.A..E) firmadas con defaults pre-autorizados.
- [x] Slice no toca endpoints Sinco on-prem nuevos (§11). FU-44 explícitamente rolleado a mt-3.
- [x] DoD de infra plumbing identificado:
  - [ ] Puerto `ITenantedDocumentSessionFactory` declarado en `Inspecciones.Infrastructure/Auth/` (pendiente — green).
  - [ ] Impl `TenantedDocumentSessionFactory` (pendiente — green).
  - [ ] `Program.cs` activa `Policies.AllDocumentsAreMultiTenanted()` + `Events.TenancyStyle = Conjoined` (pendiente — green).
  - [ ] `IDocumentSession` scoped delegado al factory (pendiente — green; verificación D-MT2-1 incluida).
  - [ ] `MartenCatalogoSyncRepository` refactor para tomar factory (pendiente — green).
  - [ ] `MartenInspeccionReader` con `IQuerySession` validado tenanted por envelope (pendiente — green).
  - [ ] `TestHeaderAwareSessionService` extendido con header `X-Sin-IdEmpresa` opcional (pendiente — green).
  - [ ] `TenantRequeridoEnEnvelopeException` declarada en `Inspecciones.Infrastructure/Auth/` (pendiente — green).
  - [ ] SQL backfill `migrations/mt-2-backfill-tenant-default.sql` creado (pendiente — green).
  - [ ] ADR-009 actualizado en `00-investigacion-mercado.md §9.17` (marcado mt-2 cerrado) (pendiente — doc-writer post-aprobación).
  - [ ] Regla `MT2-INV-1` agregada a `CLAUDE.md` "Reglas duras de calidad" (pendiente — doc-writer post-aprobación).
  - [ ] `FOLLOWUPS.md`: FU-55 abierto si emerge caché tenant-aware; otros followups cierran (pendiente — review).
  - [ ] Sub-track multi-tenancy en `CLAUDE.md` marcado mt-2 ✅ cerrado con commit hash (pendiente — doc-writer post-merge).

---

## Nota final del modelador

mt-2 es plumbing-heavy pero más arriesgado que mt-1 porque toca la integración Marten + Wolverine en el corazón del module. Los riesgos vivos:

1. **D-MT2-1 (delegate IDocumentSession scoped):** si Marten 7 / Wolverine 3 no aceptan el override sin romper outbox, refactor a factory directo en handlers. Detectable en green con los tests §6.5 + §6.8. Si emerge → reportable y posible ajuste de la spec.
2. **D-MT2-2 (tenant del envelope en listeners):** dependemos de que `Wolverine.IMessageContext.TenantId` esté poblado por la integración con Marten Conjoined. Si no — bloqueo serio reportable; requiere ADR adicional sobre cómo capturar el tenant en eventos publicados.
3. **D-MT2-10 (outbox conjoined-aware):** el outbox debe persistir `tenant_id` y propagarlo. Marten + Wolverine 3 lo soportan según docs, pero hay que verificarlo con el test §6.8.

mt-2 cierra la base sólida para mt-3 (JWT al ERP) y mt-4 (smoke E2E + observabilidad). Después del commit de mt-2, **el módulo está listo para piloto multi-empresa con aislamiento por tenant verificado por tests**.

Status: **firmado 2026-05-19** — autor firma: Usuario (Santiago Ramirez), autorización pre-otorgada en el ciclo mt-2. Próxima fase: `red` (tests rojos del factory + listeners + cross-tenant isolation).
