# CLAUDE.md — Inspecciones Sinco MYE

Este archivo orienta a Claude Code para trabajar en el repo. Las reglas duras de proceso viven en `METHODOLOGY.md`; este archivo solo apunta y fija convenciones de calidad.

## Estado del proyecto

- **Última actualización:** 2026-05-19
- **Fase actual:** Fase 1 ampliamente avanzada. **15 slices cerrados** del aggregate `Inspeccion` (1a..1o) + 4 fixes de followups (FU-32/36/37/38), más **sub-track de acople ERP a Maquinaria_V4 cerrado** (slices `erp-1..erp-4`), más **slices mt-1, mt-2 y mt-3 del sub-track multi-tenancy cerrados** (JWT claims pipeline + Marten Conjoined + propagación JWT al ERP, 2026-05-19). HEAD del sub-track aggregate: `36062e0 feat(slice-1o): ActualizarRepuesto` (2026-05-11). HEAD del sub-track ERP + multi-tenancy: ver §§"Acople ERP Maquinaria_V4" y "Multi-tenancy (sub-track 2026-05-19)" abajo.
- **Slices cerrados del aggregate (1a..1o):**
  - **Lifecycle inspección técnica:** `1a` aggregate, `1b` handler+projection, `1g` `FirmarInspeccion` (3 eventos atómicos: `DiagnosticoEmitido_v1` → `DictamenEstablecido_v1` → `InspeccionFirmada_v1`), `1m` `CancelarInspeccion`.
  - **Hallazgos:** `1c` Registrar, `1d` Actualizar, `1e` Eliminar.
  - **Repuestos (mutación del VO):** `1f` `RepuestoEstimado_v1`, `1o` `ActualizarRepuesto`. Falta `RemoverRepuesto` para cerrar la tripleta.
  - **Monitoreo (aggregate unificado `Inspeccion` con `Tipo: TipoInspeccion`):** `1h` `IniciarInspeccionMonitoreo`, `1i` `RegistrarMedicion` + `RegistrarEvaluacionCualitativa`, `1j` `OmitirItemMonitoreo`.
  - **Saga OT (capability gate manual ADR-007):** `1k` `GenerarOT` (camino feliz → `OTSolicitada_v1`), `1l` `RechazarGenerarOT` (`GeneracionOTRechazada_v1` + `InspeccionCerradaSinOT_v1`).
  - **Preop:** `1n` `DescartarNovedadPreop` (flujo "descarte rápido inline" — sin hallazgo, motivo autogenerado server-side).
- **API HTTP funcional para:** `IniciarInspeccion`, `RegistrarHallazgo`, `ActualizarHallazgo`, `EliminarHallazgo`, `AsignarRepuesto`, `ActualizarRepuesto`, `FirmarInspeccion`, `IniciarInspeccionMonitoreo`, `RegistrarMedicion`, `RegistrarEvaluacionCualitativa`, `OmitirItemMonitoreo`, `GenerarOT`, `RechazarGenerarOT`, `CancelarInspeccion`, `DescartarNovedadPreop`. Sync de 3 catálogos vía `POST /api/v1/catalogos/sync` (ETag + `If-None-Match`).
- **Próximo trabajo:** adjuntos (3.11 — pattern SAS upload), sagas `CerrarInspeccionSaga` / `EjecutarOTSaga` (3.24..3.27), aggregate `SeguimientoHallazgo` (3.C), y candidatos del aggregate `Inspeccion` aún abiertos:
  - `RemoverRepuesto` (cierra tripleta del VO `Repuesto`, par natural con 1f/1o).
  - Saga real de `OTSolicitada_v1` → `POST` al MYE on-prem (M-1) — **bloqueada por DDL DBA del slice 8 de Maquinaria_V4**.
  - `ConvertirNovedadPreopEnHallazgo` (camino largo de §15.9 que complementa 1n).
  - Adjuntos: anclaje xor `HallazgoId`/`ItemId` (§12.11.5 punto 12).

### Acople ERP Maquinaria_V4 (sub-track 2026-05-19)

Cerrado en 4 slices contra el microservicio hermano `Maquinaria_V4`:

- **slice-erp-1** (commit `4c2ef4e`): adapter HTTP tipado `IMaquinariaErpClient` en `src/Inspecciones.Infrastructure/Erp/` con 11 DTOs espejo, soporte `If-None-Match`/`ETag`/`304`, 11 endpoints admin en `CatalogosEndpoints.cs`, 14 tests WireMock.
- **slice-erp-2** (commit `63082fa`): listener Wolverine `DescartarNovedadPreopErpListener` reactivo a `NovedadPreopDescartada_v1` → `POST /api/preoperacional-fallas/cerrar` (P-6). Idempotencia natural por `PodId` (`200 yaCerradas` / `409 YA_CERRADO` = éxito silencioso). Política ADR-006: 5xx con retry 5s→30s→2m→10m, 4xx + `ArgumentException` → dead-letter inmediato. 11 tests.
- **slice-erp-3** (commit `28de25b`): listener `SincronizarDictamenVigenteListener` reactivo a `InspeccionFirmada_v1` → `PUT /equipos/{id}/dictamen-vigente` (M-W-1). Mapeo `PuedeOperar→0`, `ConRestriccion→1`, `NoPuedeOperar→2`. Nuevo puerto `IInspeccionReader` + `MartenInspeccionReader` (`AggregateStreamAsync`). 11 tests con `FakeInspeccionReader`.
- **slice-erp-4** (commit `fb44741`): endpoint `POST /api/v1/catalogos/sync` (ADR-004 canonical, sin cron). Wipe-and-replace de 3 catálogos globales puros: `causas-falla`, `tipos-falla`, `productos`. ETag por catálogo en document Marten `CatalogoSyncState`. `If-None-Match` → cache local intacto si `304`. Body vacío → `"vaciado-sospechoso"`, cache intacto. Partial-failure por catálogo. Atomicidad cross-catálogo via `LightweightSession` propia por catálogo (`MartenCatalogoSyncRepository` recibe `IDocumentStore`, no `IDocumentSession`). 23 tests con `FakeCatalogoSyncRepository`. Records dominio nuevos: `CausaFallaCatalogo`, `TipoFallaCatalogo` (§12.9.6 del modelo).
- **docs `8c3cb62`**: `Inspecciones/docs/06-contrato-apis-erp.md §0.B` espeja la reconciliación bilateral 2026-05-13 — mapa de estado real de los 21 endpoints contra Maquinaria_V4 (9 acoplables, 8 NO-aplica, 3 descartados bilateral, 1 bloqueante real M-1, 1 sintetizado M-17). Complementa la §0.A (verificación swagger 2026-05-16).

Suite tests final: `Inspecciones.Infrastructure.Tests` 59/59 verde (14 adapter + 11 erp-2 + 11 erp-3 + 23 erp-4). `Inspecciones.Domain.Tests` 246/0/19 skip (sin regresión). `Inspecciones.Application.Tests` fallos por Docker no corriendo (FU-47 preexistente, no regresión).

### Multi-tenancy (sub-track 2026-05-19)

Sub-track introducido el 2026-05-19 para introducir tenancy real por `IdEmpresa` en el módulo. 4 slices planeados:

- **slice-mt-1** (cerrado 2026-05-19, commit `feat(slice-mt-1): JWT claims pipeline + ISessionService`): pipeline de identidad del host PWA. Nuevos en `src/Inspecciones.Infrastructure/Auth/`: puerto `ISessionService` con los 5 claims canónicos (`IdEmpresa`, `IdUsuario`, `NomUsuario`, `IdSucursal`, `IdProyecto`) + `Capabilities`, `ClaimRequeridaException`, `SincoMiddlewareSessionService` (real, lee `MiddlewareAuthorizationToken.SessionVariables()` del paquete corporativo `SincoSoft.MYE.Common 1.5.1`). En `tests/Inspecciones.Api.Tests/Auth/`: `FakeSessionService` + `TestHeaderAwareSessionService` (backward-compat con tests legacy). `Program.cs` monta middleware corporativo en envs no-Test + handler global de `ClaimRequeridaException` → 401 con `codigoError = "CLAIM-{NOMBRE}-AUSENTE"`. 15 endpoints HTTP refactorizados: `tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture)`, capabilities desde `session.Capabilities`. `POST /api/v1/catalogos/sync` gana capability check (`ejecutar-inspeccion` o `administrar-catalogos`) — cierre FU-52. Headers de tests (`X-Sin-Capability-*`, `X-Tecnico-Id`) eliminados de endpoints de producción. ADR-002 cerrado (de tentativa a aceptada) + ADR-009 creado (multi-tenancy Marten conjoined). Cierra FU-14 y FU-52. Suite final Api.Tests: 65/0/7 skip (8 tests nuevos del slice + 57 legacy sin regresión).
- **slice-mt-2** (cerrado 2026-05-19, commit `feat(slice-mt-2): Marten conjoined multi-tenancy por IdEmpresa`): Marten `Conjoined` multi-tenancy activa. `StoreOptions.Policies.AllDocumentsAreMultiTenanted()` + `Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined` en `Program.cs`. Nuevos en `src/Inspecciones.Infrastructure/Auth/`: puerto `ITenantedDocumentSessionFactory` con 3 métodos (`OpenSession()`, `OpenQuerySession()`, `OpenSessionForTenant(tenantId)`) + impl `TenantedDocumentSessionFactory` que lee `ISessionService.IdEmpresa.ToString(CultureInfo.InvariantCulture)`. `IDocumentSession`/`IQuerySession` scoped en DI ahora vienen del factory — los 15 handlers existentes heredan tenancy sin tocar constructores (D-MT2-1 confirmada — Wolverine outbox preservado). `MartenInspeccionReader` recibe `IDocumentStore` + `IQuerySession` ambient; nueva overload `LeerAsync(Guid, string tenantId, ct)` para listeners Wolverine. `SincronizarDictamenVigenteListener` gana overload `HandleAsync(InspeccionFirmada_v1, Envelope, ct)` que lee `envelope.TenantId` o lanza `TenantRequeridoEnEnvelopeException` (MT2-PRE-2 → dead-letter inmediato vía ADR-006). `MartenCatalogoSyncRepository` refactor: recibe `ITenantedDocumentSessionFactory` (scoped), todas las sesiones Conjoined-aware. D5 firmada: TODOS los catálogos por-empresa (sin excepciones single-tenant). Tests nuevos: 2 (excepción) + 4 (listener tenant) + 8 (E2E cross-tenant isolation aggregate + catálogos + CatalogoSyncState). Reset schema dev confirmado en `InspeccionesAppFactory` (`DROP SCHEMA IF EXISTS inspecciones CASCADE`). SQL backfill staging documentado pero no ejecutado (FU-58). Suite final: Domain.Tests 246/265+19skip (sin regresión), Infrastructure.Tests 65/65 (+6), Api.Tests 73/80+7skip (+8). 4 followups abiertos: FU-55 (caché tenant-aware en sagas, placeholder), FU-56 (validar Wolverine 3 prefiere overload tenant-aware en mt-4), FU-57 (`DescartarNovedadPreopErpListener` propagar tenant a logs), FU-58 (SQL backfill staging post-merge), FU-59 (test rebuild cross-tenant defensivo).
- **slice-mt-3** (cerrado 2026-05-19, commit `1108426`): cadena de identidad cross-process al ERP. Nuevos en `src/Inspecciones.Infrastructure/Auth/`: puerto `IBearerTokenAccessor` + 4 implementaciones (`HttpContextBearerTokenAccessor`, `AmbientBearerTokenAccessor` con `AsyncLocal<string?>` estático y `SetForCurrentScope(string?)` que retorna `IDisposable`, `ServiceAccountBearerTokenAccessor`, `ChainedBearerTokenAccessor` orden HTTP → Ambient → ServiceAccount), excepción `BearerTokenAusenteException`, utility estática `EnvelopeBearerExtractor`. Nuevo en `src/Inspecciones.Infrastructure/Erp/`: `BearerTokenPropagationHandler` (DelegatingHandler) que reemplaza `http.DefaultRequestHeaders.Authorization` fijo — consulta el chain en cada `SendAsync` y aplica fail-closed con `BearerTokenAusenteException` si todos accessors vacíos (MT3-INV-3). Listeners `SincronizarDictamenVigenteListener` y `DescartarNovedadPreopErpListener` ganan setup del ambient con `using var _ = new AmbientBearerTokenAccessor().SetForCurrentScope(jwt)` desde `envelope.Headers["X-Forwarded-Authorization"]`. `DescartarNovedadPreopErpListener` gana overload tenant-aware + `LogCierreFallido` ahora incluye `tenantId` del envelope (cierre FU-57). `MaquinariaErpOptions.JwtToken` cambia de rol: ya no es token global, ahora es **service-account fallback** del chain. ADR-006 gana sección "JWT del usuario en el envelope del outbox (mt-3)" + ADR-009 marca mt-3 cerrado. Cierra FU-44 y FU-57. Suite final Infrastructure.Tests: 93/93 (+34 del mt-3: 13 accessor unit tests + 6 handler integration WireMock + 5 listener Descartar + 3 listener Dictamen + variantes). Domain.Tests 246/19 skip (sin regresión). 3 followups abiertos: FU-60 (`CaptureBearerForOutboxMiddleware` para captura automática del header en publish — depende FU-47), FU-61 (re-evaluar AsyncLocal estático — defensivo), FU-62 (refresh JWT en retry del outbox — condicional al piloto).
- **slice-mt-4** ⏳ pendiente: tests E2E con 2 tenants concurrentes, asserts de no-leak, métricas App Insights por `IdEmpresa`. Baseline antes del piloto. Cierra FU-56 y FU-59.

### 🟡 Salud del repo (revisión 2026-05-19, vigente al cierre de mt-3)

Estado factual al cierre del slice mt-3 (Infrastructure.Tests verificado local sin Docker; Api.Tests/Application.Tests requieren Postgres/Docker no disponible en esta sesión — FU-32/FU-47 abiertos):

- **Build:** limpio en los 8 proyectos. `TreatWarningsAsErrors=true` vigente. 0 warnings, 0 errors.
- **Tests dominio:** `Domain.Tests` 246/265 pass + 19 skip esperados. Cobertura ramas aggregate `Inspeccion`: 94.44% (regla CLAUDE.md ≥ 85%). Sin regresión post-mt-2 (D-MT2-8: cero cambios al dominio).
- **Tests API HTTP:** `Api.Tests` 73/80 pass + 7 skip — los 7 skip son tests de header `X-Client-Command-Id` (ADR-008) o `Authorization` en env Development (mt-1) que requieren setup específico. Suite creció +8 con mt-2 (`MartenConjoinedTenancyTests`) sobre +8 de mt-1. Requiere `POSTGRES_TEST_CONNSTRING` exportada (local 5432 confirmado en sesión 2026-05-19).
- **Tests Application:** `Application.Tests` requiere Docker (Testcontainers). `FU-47` preexistente — no es regresión de mt-2.
- **Tests Infrastructure:** `Infrastructure.Tests` 93/93 verde (slices erp-1..erp-4 + mt-2 + mt-3). mt-3 añadió 34 tests: 13 `BearerTokenAccessorTests` (unidad de cada accessor + chain order) + 6 `BearerTokenPropagationHandlerTests` (WireMock — HTTP propaga, listener envelope propaga, fallback service-account, fail-closed, DelegatingHandler reescribe header default) + 5 `DescartarNovedadPreopErpListenerTenantTests` (envelope propaga JWT, fallback service-account, overload legacy compat, log con tenant FU-57, ambient cleanup) + 3 `SincronizarDictamenVigenteBearerPropagationTests` (envelope propaga JWT al PUT dictamen, fallback, overload legacy). Patrón "puerto + fake" preservado.
- **Bugs preexistentes detectados durante 1g..1l y cerrados:** `FU-36` (`RegistrarHallazgo` retornaba 400 — faltaba `JsonStringEnumConverter` en Minimal APIs), `FU-37` (`GenerarOT`/`RechazarGenerarOT` usaban `DateTime.UtcNow` violando regla CLAUDE.md — reemplazados por `FakeTimeProvider` en factory), `FU-38` (`Results.Forbid` devolvía 500 en vez de 403 — reemplazado por helper `Forbidden403`).
- **Followups vivos relevantes:** `FU-39`/`FU-43` (colisión de `EquipoIds` hardcoded entre slices en tests de integración), `FU-13` (migrar `InspeccionAbiertaPorEquipoView` a `MultiStreamProjection` puro — bloqueado en decisión de añadir `EquipoId` a `InspeccionFirmada_v1`/`InspeccionCancelada_v1`), `FU-14` (claims reales del JWT del host pendiente de ADR-002), `FU-22` (confirmar con David que M-16 expone `Activo`/`Orden`/`ParteEquipoId`), `FU-44..FU-52` (nuevos del sub-track ERP — ver `FOLLOWUPS.md`).
- **DevEx local:** `docker compose up -d` falla silenciosamente cuando hay un PostgreSQL nativo en el puerto 5432 (caso real de la máquina del PO — dos instalaciones nativas, 5432 y 5433). Para arrancar portable: container en puerto alto (p. ej. 55432) y env var `ConnectionStrings__Postgres` override.

**Implicación metodológica:** la safety-net de integración existe y está en verde para `Api.Tests` e `Infrastructure.Tests`. Antes de abrir slices grandes (sagas reales contra ERP con Postgres real), tratar `FU-39` para que `Application.Tests` también corra sin Docker — sin eso, cualquier handler nuevo con dependencia Marten queda con cobertura de integración huérfana.

### Embargo de docs — LEVANTADO 2026-05-07

**Estado:** **levantado** por Jaime el 2026-05-07 (disparador implícito ya cumplido — 6 slices cerrados al momento de la decisión: 1a..1f; hoy son 15).

**Histórico (vigente entre 2026-05-05 y 2026-05-07):** se prohibieron edits no-triviales a `Inspecciones/docs/*` para evitar amplificar el costo de cambios mientras los slices iniciales evidenciaban qué partes del modelo/ADRs eran load-bearing. Razón documentada en commit `30a0e71 docs(metodología): embargo de docs hasta cerrar 4 slices`.

**Reconciliación 2026-05-07:** al levantarse, se aplicó la propagación de `EquipoLocal.GrupoMantenimientoId` al modelo §12.7 (campo añadido al record canonical, alineado con M-3b decisión 2026-05-05).

**Próximo embargo:** si se necesita uno nuevo, registrarlo aquí con fecha y disparador de levantamiento.
- **Roadmap:** `Inspecciones/docs/roadmap.md` (fases 0..10).
- **Modelo de dominio:** `Inspecciones/docs/01-modelo-dominio.md` §15 (fuente de verdad).
- **Contrato de APIs ERP:** `Inspecciones/docs/06-contrato-apis-erp.md` (16 obligatorios MVP + 1 condicional + 8 diferidos — M-16 promovido a MVP el 2026-05-05 por inclusión de monitoreo; U-1/U-2 eliminados el 2026-05-05 — identidad 100% del host PWA).
- **Diagramas de flujo:** `02f` técnica · `02g` monitoreo · `02h` seguimientos (narrativos) + `02i/02j/02k` workflows basados en nodos (Markdown + HTML interactivos con Mermaid).
- **ADRs:** ADR-001 a ADR-005 en `00-investigacion-mercado.md §9`; ADR-003 ampliado en `01-modelo-dominio.md §13`; ADR-005 en `§14`; **ADR-006 (resiliencia outbox para integraciones ERP) en `§16`**; **ADR-007 (OT manual con capability gate) en `§17`**; **ADR-008 (cola de comandos offline cliente PWA) en `00-investigacion-mercado.md §9.16` + diagrama interactivo `09-adr-008-offline-cliente.html`**.

### Refinamientos vigentes (sesión 2026-05-04 + decisiones 2026-05-05)

- **Monitoreo entra al MVP (decisión Jaime 2026-05-05):** antes era Fase 2 / roadmap 10.4. Aggregate **unificado** `Inspeccion` con discriminador `Tipo: TipoInspeccion ∈ {Tecnica, Monitoreo}`. Reusa firma, dictamen, sagas OT, seguimientos. Eventos nuevos: `MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1`, `ItemMonitoreoOmitido_v1`. Comando hermano `IniciarInspeccionMonitoreo`. Endpoints `POST /inspecciones/monitoreo` + `POST /inspecciones/{id}/items/{itemId}/{medicion|evaluacion|omitir}`. Ver roadmap §3.B' + §5.B' + §12.11.5 del modelo.
- **Identidad 100% del host PWA (decisión Jaime 2026-05-05):** el módulo NO maneja usuarios. Sin sync de usuarios, sin catálogo local, sin app registration propio, sin endpoints `/admin/usuarios`. Endpoints U-1/U-2 eliminados del contrato. Equipo Seguridad/IT sale del cross-team. El módulo solo: (a) valida JWT cloud-side (issuer/JWKS del host), (b) autoriza por capability (no por perfil), (c) usa `tecnicoId` opaco del JWT. Ver roadmap Fase 2 actualizada + §4.D (NO APLICA).

- **Tipos de IDs (1b):** PKs del ERP son `int` (System.Int32) acompañados de `<X>Codigo: string` para UI/URLs. IDs internos del módulo (`InspeccionId`, `HallazgoId`, etc.) siguen siendo `Guid` (v7 preferido). Ver §15.4 del modelo para la convención formal.
- **Rutina técnica per-equipo (β):** cardinalidad **1 rutina técnica por equipo** (única). La asignación es explícita en el ERP — `M-3b` trae `rutinaTecnicaId: int` (singular). El handler `IniciarInspeccion` la resuelve auto, técnico no elige (UX MVP histórica preservada). Ver §12.11.1 del modelo.
- **Rutinas monitoreo por grupo de mantenimiento (decisión 2026-05-05):** asignación **derivada por grupo**, no per-equipo. `M-3b` trae `grupoMantenimientoId` del equipo; `M-16` trae cada rutina con su `grupoMantenimientoId`. Cliente filtra `r.GrupoMantenimientoId == equipo.GrupoMantenimientoId`. Sin tabla intermedia en el ERP. Técnico elige entre las rutinas activas del grupo. Ver §12.11.5.
- **M-3b consolidado:** detalle del equipo trae partes + asignaciones de rutinas en una sola llamada. **M-4 eliminado** (absorbido).
- **M-17 nuevo (MVP crítico):** `GET /catalogos/rutinas` — sync on-app-open de rutinas técnicas (decisión 2026-05-05 ADR-004 canonical — sin cron nocturno). Cierra gap detectado en revisión por flujos.
- **Sync de catálogos on-app-open (decisión Jaime 2026-05-05 — ADR-004 canonical):** **sin cron nocturno**. Cada apertura de la PWA dispara `GET /api/v1/catalogos/<X>` en paralelo con `If-None-Match: "{etag-cliente}"`; respuesta típica = `304 Not Modified`. Persistencia en IndexedDB cliente. Si la app abre sin red → usa último cached (modo degradado, no bloquea). Bloqueo solo por staleness extrema (>7 días sin sync). Botón admin "refrescar ahora" promovido a v1.0. Ver ADR-004 §9.15 actualizado.
- **Adjuntos:** anclaje xor `HallazgoId` (técnica) o `ItemId` (monitoreo). Siempre opcional. Límite 5 por entidad. Ver §12.11.5 punto 12.
- **Backends ERP:** preop = SQL Server relacional on-prem (confirmado). MYE núcleo / inventario = SQL Server (asumido — confirmar con David). Solo el módulo Azure usa Marten + PostgreSQL.

## Metodología (resumen — ver `METHODOLOGY.md` para detalle)

- **TDD estricto** sobre Event Sourcing: Given/When/Then sobre eventos.
- **Squad de 5 agentes** secuencial: `domain-modeler` → `red` → `green` → `refactorer` → `reviewer`.
- **Unidad de trabajo:** un comando = un slice = una carpeta `slices/{N}-{slug}/{spec, red-notes, green-notes, refactor-notes, review-notes}.md` = un commit `feat(slice-{N}): {comando}`.
- **Plantillas:** `templates/slice-spec.md`, `templates/test-red.md`, `templates/review-notes.md`.
- **Personas de agente:** `templates/agent-personas/`.
- **Followups:** `FOLLOWUPS.md` en raíz.

## Stack (decidido en Fase 0)

| Capa | Tecnología | Notas |
|---|---|---|
| Event store / CQRS | Marten 7 sobre PostgreSQL 16 | |
| Mediator + outbox | Wolverine 3 | |
| Runtime | .NET 8+ | (aceptable .NET 9 si se valida en este repo) |
| Compute Azure | Azure Container Apps | scale-to-zero |
| DB Azure | Azure Database for PostgreSQL Flexible | |
| Identidad | **Heredada de la PWA Sinco MYE móvil** (host) | El módulo no se autentica solo; recibe el contexto del usuario del host. Mecanismo concreto a confirmar — ver ADR-002 (estado tentativo). |
| Push frontend | Azure SignalR (Standard tier) | ADR-005 |
| Integración Sinco on-prem | REST sobre VPN site-to-site | ADR-001 |
| Frontend | PWA React + MUI v6 (heredada de Sinco MYE) | módulo nuevo dentro de la PWA existente |

## Reglas duras de calidad (no negociables)

- `nullable` habilitado, `TreatWarningsAsErrors=true` en todos los proyectos.
- **Naming:** español para dominio (`InspeccionTecnica`, `Hallazgo`, `Repuesto`, `Seguimiento`), inglés para plumbing (`Program`, `Handler`, `Projection`, `Adapter`).
- Records para eventos y comandos; clases para agregados.
- `TimeProvider` inyectado — **prohibido `DateTime.UtcNow` en dominio**.
- `Guid.NewGuid()` solo en handlers; el dominio recibe el id desde fuera.
- **Tipos de IDs:** `int` (System.Int32) para PKs del ERP (`EquipoId`, `RutinaId`, `ParteId`, `ActividadId`, `CausaFallaId`, `TipoFallaId`, `NovedadPreopId`, `InsumoId`/`SkuId`, `ProyectoId`/`ObraId`, `OTCorrectivaIdSinco`, `ItemId`, etc.) acompañados de `<X>Codigo: string` para UI/URLs. `Guid` solo para IDs internos del módulo (`InspeccionId`, `HallazgoId`, `RepuestoId`, `AdjuntoId` Azure Blob, `SeguimientoHallazgoId`). Ver §15.4 del modelo.
- `UbicacionGps(Latitud, Longitud, PrecisionMetros, CapturadoEn)` para coordenadas — prohibido `double` pelado.
- `BlobUri` para adjuntos — el dominio nunca firma SAS (ADR-005, pattern SAS upload).
- Identidad: el handler recibe claims por parámetro; el dominio nunca conoce JWTs.
- **Identidad HTTP (regla nueva mt-1):** todo endpoint HTTP lee identidad vía `ISessionService` (`Inspecciones.Infrastructure.Auth`). **Prohibido leer `HttpContext.User` o claims directamente en endpoints o handlers.** El `TecnicoId` del comando se construye como `session.IdUsuario.ToString(CultureInfo.InvariantCulture)` — string opaco que el dominio recibe sin conocer su origen. Las capabilities se validan en el endpoint con `if (!session.Capabilities.Contains("xxx")) return Forbidden403("PRE-1", ...);` antes de despachar al handler. Ver ADR-002 §9.14 + ADR-009 §9.17.
- **Multi-tenancy Marten (regla nueva mt-2 — MT2-INV-1):** toda apertura de sesión Marten en código de producción pasa por `ITenantedDocumentSessionFactory` (`Inspecciones.Infrastructure.Auth`). **Prohibido `store.LightweightSession()` o `store.QuerySession()` directo sin tenant en `src/`.** El `IDocumentSession` y `IQuerySession` scoped registrados en DI ya vienen tenant-aware vía el factory — los handlers que los reciben por DI heredan tenancy sin tocar nada. Bypass legal `OpenSessionForTenant(tenantId)`: listeners Wolverine que leen el tenant del `Envelope.TenantId`, tests E2E cross-tenant, y operaciones de bootstrap/admin sin contexto HTTP. Para listeners Wolverine que escriben/leen Marten: aceptar `Wolverine.Envelope` como parámetro de `HandleAsync` y propagar el tenant explícitamente al puerto (ej. `IInspeccionReader.LeerAsync(id, tenantId, ct)`). Ver ADR-009 §9.17.
- Cobertura de ramas del agregado afectado **≥ 85 %** por slice.
- Eventos versionados con sufijo `_v1`, `_v2` cuando emerja segunda versión.
- Soft delete: hallazgos y repuestos emiten `*Eliminado`; nunca borran del stream.
- **`Apply` puro:** los métodos `Apply(Evt)` del agregado son mutaciones puras de estado — sin validaciones, sin lanzar excepciones. Las pre-condiciones (estado actual, "ya firmado", invariantes I-*) viven en los métodos de decisión que producen los eventos. Re-validar en `Apply` rompe el rebuild desde stream.
- **Rebuild test obligatorio:** todo slice que toque comportamiento del agregado incluye un test que reproyecta los eventos emitidos sobre un agregado vacío y verifica que el estado resultante es el mismo que tras la decisión original. Atrapa validaciones intrusas en `Apply` y eventos fuera de orden causal.
- **Atomicidad de eventos:** múltiples eventos al mismo stream en el mismo handler son atómicos por construcción (un único `IDocumentSession.SaveChangesAsync()`). Prohibido partir un comando en dos `SaveChangesAsync`. Orden de los eventos = orden causal (p. ej. `Diagnostico → Dictamen → Firmada`).

## Convenciones de tests

- xUnit + FluentAssertions.
- Cero mocks del dominio.
- Marten embebido (Testcontainers Postgres) para tests de integración.
- `WebApplicationFactory<Program>` para tests HTTP end-to-end.
- WireMock (o equivalente) para tests de adapters Sinco on-prem cuando los endpoints reales no estén disponibles.
- Naming en español, frase completa, referenciando código de invariante cuando aplique:
  - ✅ `FirmarInspeccion_sin_GPS_lanza_GpsRequeridoException` (V-F3)
  - ❌ `Test1`, `ShouldWork`

## Convención de commits

- Un commit por slice cerrado: `feat(slice-{N}): {comando}`.
- Refinamientos mantienen sufijo: `feat(slice-{N}b): {refinamiento}`.
- Fixes transversales: `fix(slice-{N}): {descripción}`.
- Docs/ADRs aislados: `docs: ...`.

## Arranque del trabajo

**Persona del orquestador:** `templates/agent-personas/orchestrator.md` define el contrato completo (catálogo de comandos, criterios de paso entre fases, manejo de veredictos, roles propios infra-wire/azure-ops/doc-writer, DoD, prohibiciones). Resumen del flujo:

1. Cuando el usuario diga "vamos con `XComando`":
2. Invocar `domain-modeler` con `templates/agent-personas/domain-modeler.md` y la referencia a `01-modelo-dominio.md §15` correspondiente.
3. Esperar firma del usuario en `spec.md`.
4. Invocar `red` → `green` → `refactorer` → `reviewer` en orden, validando criterios de paso entre fases (METHODOLOGY.md §2.2 + orchestrator.md "Criterios de paso").
5. Como orquestador (`infra-wire`): registrar handler en Wolverine, proyección en Marten, endpoint HTTP, hub SignalR si aplica.
6. Commit único `feat(slice-{N}): {comando}` con referencia al `spec.md`.

## Memoria persistente del proyecto

- `Proyecto Inspecciones Sinco` — contexto del módulo y stack.
- `Proyecto hermano sinco-presupuesto` — referencia metodológica (mismo stack, 52 slices probados).

Ver `~/.claude/projects/.../memory/MEMORY.md` para el índice.
