# Baseline pre-piloto multi-empresa — Inspecciones Sinco MYE

**Fecha:** 2026-05-19
**Cierre sub-track multi-tenancy:** commit mt-4 (este slice).

## Estado de los 4 slices del sub-track

| Slice | Commit | Cierre |
|---|---|---|
| `mt-1` JWT claims pipeline + ISessionService | `2f6f96a` | ADR-002 cerrado (de tentativa a aceptada). FU-14 + FU-52 cerrados. |
| `mt-2` Marten Conjoined multi-tenancy | `4386f33` | Tenancy real end-to-end. FU-44 rolleado a mt-3. |
| `mt-3` JWT propagation al ERP | `1108426` | Cadena de identidad cross-process. FU-44 + FU-57 cerrados. |
| `mt-4` E2E isolation + observabilidad + cierre | _(este slice)_ | FU-56 + FU-59 + FU-60 cerrados. |

## Invariantes verificados end-to-end

### Pipeline de identidad (mt-1)

- **PRE-AUTH-1..4** — middleware corporativo `MiddlewareAuthorizationToken` valida JWT del host; `ClaimRequeridaException` mapea a `401 CLAIM-{NOMBRE}-AUSENTE`.
- **PRE-CAP-1** — endpoints validan capability del JWT (`session.Capabilities.Contains("...")`) antes de despachar.
- **Bypass env Test** — `TestHeaderAwareSessionService` / `FakeSessionService` (sin JWT real).

### Multi-tenancy Marten (mt-2)

- **MT2-INV-1** — toda sesión Marten en producción pasa por `ITenantedDocumentSessionFactory`. Cero bypass en `src/`.
- **MT2-INV-2** — streams aislados por `tenant_id` (Conjoined). Tenant A no ve streams de Tenant B.
- **MT2-INV-3** — catálogos aislados (D5: todos por-empresa).
- **MT2-INV-4** — sync de catálogos atómico cross-catálogo dentro de un tenant.

### JWT propagation cross-process (mt-3)

- **MT3-INV-1** — HTTP-originadas propagan Bearer del request al ERP.
- **MT3-INV-2** — listeners-originadas propagan el JWT del envelope.
- **MT3-INV-3** — fail-closed si todos los accessors vacíos.
- **MT3-INV-4** — adapter NO setea `DefaultRequestHeaders.Authorization` fijo (eliminado en mt-3).

### E2E + observabilidad (mt-4)

- **MT4-INV-1** — POST con tenant A no visible en queries del tenant B (aggregate + view + paralelismo 20 tareas).
- **MT4-INV-2** — `CaptureBearerForOutboxMiddleware` propaga el `Authorization` entrante al envelope outgoing del outbox. Listener tenant-aware lo consume y lo aplica al adapter.
- **MT4-INV-3** — logs estructurados de handlers/listeners críticos incluyen `IdEmpresa` (vía `SessionLoggingScopeFilter` global + `LogSyncFallida`/`LogCierreFallido`).
- **MT4-INV-4** — rebuild puro del aggregate es determinista (Apply no introduce side effects).

## Followups operativos abiertos para el piloto

### Bloqueantes (cerrar antes del primer deploy multi-empresa)

- **FU-58** — Ejecutar SQL backfill staging para data legacy pre-mt-2 (`slices/mt-2-marten-conjoined-tenancy/green-notes.md §"SQL backfill staging"`). Verificar con `SELECT tenant_id, COUNT(*) FROM inspecciones.mt_doc_equipo_local GROUP BY tenant_id`.

### Recomendables (cerrar en primera iteración del piloto)

- **FU-47** — Replicar el patrón `POSTGRES_TEST_CONNSTRING` de `Api.Tests` a `Application.Tests`. Bloquea iteración local en máquinas sin Docker (incluida la del PO).
- **FU-63** _(nuevo mt-4)_ — Skip determinístico de `Api.Tests` cuando Postgres no detectable. Mejora DevEx; sin Postgres todos los tests fallan en construcción del fixture.

### Defensivos (no bloqueantes pero documentados)

- **FU-61** — Re-evaluar storage estático de `AmbientBearerTokenAccessor` (y por extensión `IncomingBearerCarrier`) cuando emerja caso patológico de paralelismo cross-tenant en mismo proceso.
- **FU-62** — Refresh automático del JWT en retry del outbox si JWT expira en ventana de retry ADR-006 (5s→30s→2m→10m).
- **FU-64** _(nuevo mt-4)_ — Test del counter `inspecciones.erp.calls` con `MeterListener` para cobertura defensiva del wiring de métricas.
- **FU-55** — Caché Marten tenant-aware en sagas con context-switch (placeholder defensivo).

### Cross-team (resolver fuera del módulo)

- **FU-54** — Confirmar con Sergio/David si el JWT del host emite la claim `capabilities`. Hoy el módulo usa default `["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]` cuando el claim está ausente (D-MT1-4 "always-allow").
- **FU-53** — Auth feeds NuGet corporativos en CI (CI requerirá PAT en secret para acceder a los feeds Azure DevOps de `SincoSoft.MYE.*`).

## Checklist "qué validar antes del primer deploy multi-empresa"

### 1. Data layer

- [ ] **SQL backfill staging ejecutado** (FU-58). Toda fila legacy de tablas `mt_doc_*` tiene `tenant_id IS NOT NULL`.
- [ ] **Inspeccionar `wolverine_outgoing_envelopes`** — esquema ya creado por Wolverine con columna `headers` (JSON) lista para `X-Forwarded-Authorization`.
- [ ] **Verificar PostgreSQL `pg_stat_statements`** no muestra queries cross-tenant (cualquier `WHERE tenant_id = 'X'` debe coincidir con el tenant del scope HTTP).

### 2. Autenticación

- [ ] **JWT del host PWA entrega los 5 claims canónicos:** `UsuarioId`, `NomUsuario`, `IdEmpresa`, `IdSucursal`, `IdProyecto`. Confirmado vía Postman/curl + smoke E2E.
- [ ] **JWT emite claim `capabilities`** (FU-54 cierre cross-team) o se decide explícitamente que el default "always-allow" de mt-1 es aceptable para piloto.
- [ ] **Maquinaria_V4 valida el JWT propagado** — un técnico de empresa 7 firmando una inspección causa que el ERP audite la acción al usuario, NO al service-account.

### 3. Observabilidad

- [ ] **App Insights / Azure Monitor configurado** con connection string en `appsettings.Production.json`.
- [ ] **Dashboard App Insights filtrable por `id_empresa`** — al menos:
  - Logs estructurados de los 15 endpoints (scope `IdEmpresa` propagado vía `SessionLoggingScopeFilter`).
  - Counter `inspecciones.erp.calls` taggeado por `id_empresa`, `endpoint`, `resultado`.
  - Distributed tracing tag `id_empresa` en cada `Activity` HTTP.
- [ ] **Alerta operativa**: spike de `inspecciones.erp.calls{resultado=fallo}` por tenant > umbral (definir en operativa).

### 4. Aislamiento

- [ ] **Smoke E2E manual con 2 tenants reales** — empresa A inicia inspección; empresa B no la ve en su listado activo (`InspeccionAbiertaPorEquipoView`).
- [ ] **Verificar al menos un retry del outbox** post-deploy — `5xx` simulado del ERP causa retry con `X-Forwarded-Authorization` preservado, dead-letter eventual si JWT expira (comportamiento documentado en mt-3 §7).
- [ ] **Confirmar SignalR audience por tenant** (decisión post-piloto si emerge — out of scope mt-4).

### 5. Rollout

- [ ] **Deploy gradual por empresa** — habilitar Inspecciones para empresa piloto primero (p. ej. empresa 1); luego empresa 2; etc. Cada habilitación es independiente.
- [ ] **Plan de rollback** — si emerge leak cross-tenant en producción, plan de respuesta documentado (alertar, suspender el deploy, postmortem).

### 6. Tests

- [ ] **Tests del sub-track en CI verde:** mt-1 + mt-2 + mt-3 + mt-4. Suite final con Postgres:
  - `Domain.Tests`: 248 / 19 skip
  - `Infrastructure.Tests`: 103 / 0 skip
  - `Api.Tests`: ~77 / 7 skip (con Postgres)
- [ ] **CI con `POSTGRES_TEST_CONNSTRING`** configurado para `Api.Tests`. Auth feeds NuGet resueltos (FU-53).

## Lo que NO está en mt-4 — slices futuros

- **OpenTelemetry full** (exporter, sampling, AppInsights connection string explícita). Hoy: `Meter` BCL + `Activity.AddTag` son scrappable por App Insights nativo. Slice operativo separado si emerge necesidad.
- **Histogram `inspecciones.command.duration`** para los 15 endpoints. ApplicationInsights.AspNetCore ya mide latencia HTTP por endpoint nativamente. Slice futuro si emerge requerimiento.
- **SignalR audience filter por tenant** — diferido a piloto si emerge uso real del hub.
- **CaptureBearer para listener-to-listener publish** — hoy MT4-PRE-2 documenta que no aplica; abrir followup cuando emerja un listener que publica más mensajes.

Status: **sub-track multi-tenancy ✅ cerrado. Listo para piloto multi-empresa**, sujeto al checklist de §6.
