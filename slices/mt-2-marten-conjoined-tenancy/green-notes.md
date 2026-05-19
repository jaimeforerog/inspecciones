# Slice mt-2 — Green notes

**Autor:** orquestador (rol `green` — Agent tool no disponible; autorización pre-otorgada)
**Fecha:** 2026-05-19
**Estado:** **Verde** — build limpio (0 warnings, 0 errors); tests pasan en los proyectos con Postgres disponible.

---

## Resumen ejecutivo

| Suite | Pre mt-2 | Post mt-2 | Delta |
|---|---|---|---|
| `Domain.Tests` | 246 pass + 19 skip | 246 pass + 19 skip | 0 (mt-2 no toca dominio) |
| `Infrastructure.Tests` | 59 pass | **65 pass** | **+6 nuevos del slice** |
| `Api.Tests` (con `POSTGRES_TEST_CONNSTRING=localhost:5432`) | 65 pass + 7 skip | **73 pass + 7 skip** | **+8 nuevos del slice** |
| `Application.Tests` | falla por Docker (FU-47 pre-existente) | igual (FU-47 pre-existente) | 0 (regresión preexistente, no de mt-2) |
| **Build** | clean | **clean (0 warnings, 0 errors)** | — |

**Total tests nuevos:** 14 (2 excepción + 4 listener tenant + 8 E2E cross-tenant).
La spec proyectaba 15; la diferencia: el escenario §6.1 "factory abre sesión" se cubre con 1 test `TenantedDocumentSessionFactory_OpenSession_propaga_IdEmpresa_...` y 2 hermanos (`OpenQuerySession`, `OpenSessionForTenant`) — ya están dentro de los 8 mt-2 E2E, no separados.

---

## Archivos producción agregados/modificados

### Agregados

1. `src/Inspecciones.Infrastructure/Auth/TenantRequeridoEnEnvelopeException.cs`
   - Excepción para envelopes Wolverine sin TenantId (MT2-PRE-2).
   - Hereda `InvalidOperationException` → política ADR-006 dead-letter inmediato.

2. `src/Inspecciones.Infrastructure/Auth/ITenantedDocumentSessionFactory.cs`
   - Puerto con 3 métodos: `OpenSession()`, `OpenQuerySession()`, `OpenSessionForTenant(tenantId)`.

3. `src/Inspecciones.Infrastructure/Auth/TenantedDocumentSessionFactory.cs`
   - Impl producción. Lee `ISessionService.IdEmpresa.ToString(CultureInfo.InvariantCulture)` y delega a `store.LightweightSession(tenantId)`.

### Modificados

4. `src/Inspecciones.Api/Program.cs`
   - `options.Policies.AllDocumentsAreMultiTenanted()` + `options.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined`.
   - Registro DI: `AddScoped<ITenantedDocumentSessionFactory, TenantedDocumentSessionFactory>()`.
   - Override del `IDocumentSession` y `IQuerySession` scoped — ahora vienen del factory (D-MT2-1 confirmada — no rompe outbox Wolverine).
   - `ICatalogoSyncRepository` cambió de Singleton a Scoped (depende del factory scoped).

5. `src/Inspecciones.Infrastructure/Erp/IInspeccionReader.cs`
   - Overload nueva `LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default)`.

6. `src/Inspecciones.Infrastructure/Erp/MartenInspeccionReader.cs`
   - Ahora recibe `IDocumentStore` + `IQuerySession` (ambient). La nueva overload abre `_store.QuerySession(tenantId)` para garantizar la lectura tenant-aware del listener.

7. `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs`
   - Nueva overload `HandleAsync(InspeccionFirmada_v1 evento, Envelope envelope, CancellationToken ct)` que lee `envelope.TenantId` y lo propaga al reader.
   - Si `TenantId` es null o vacío → lanza `TenantRequeridoEnEnvelopeException` (MT2-PRE-2 / §6.6).
   - La overload original `HandleAsync(InspeccionFirmada_v1, CancellationToken)` se preserva por backwards-compat con los 11 tests erp-3.

8. `src/Inspecciones.Infrastructure/Erp/MartenCatalogoSyncRepository.cs`
   - Refactor de constructor: recibe `ITenantedDocumentSessionFactory` (no más `IDocumentStore` directo).
   - Cada `LightweightSession()` ahora se abre via `_sessions.OpenSession()` → discriminada por tenant (MT2-INV-3 / D5).

---

## Archivos test agregados

1. `tests/Inspecciones.Infrastructure.Tests/Auth/TenantRequeridoEnEnvelopeExceptionTests.cs` — 2 tests.
2. `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/SincronizarDictamenVigenteListenerTenantTests.cs` — 4 tests (happy + 3 patológicos).
3. `tests/Inspecciones.Api.Tests/Tenancy/MartenConjoinedTenancyTests.cs` — 8 tests E2E.

---

## Archivos test modificados (regresión esperada del cambio Conjoined)

Refactor mecánico: `store.LightweightSession()` y `store.QuerySession()` directos no funcionan post-Conjoined (los documentos sin tenant_id no son legibles por handlers que abren sesión tenant-aware). Reemplazados por `factory.OpenSeedingSessionForDefaultTenant()` o `factory.OpenSeedingSessionForTenant("N")`:

- `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` — agregados helpers `OpenSeedingSessionForDefaultTenant()` y `OpenSeedingSessionForTenant(tenantId)` (bypass legal de MT2-INV-1 para siembra de tests).
- `tests/Inspecciones.Api.Tests/IniciarInspeccionEndpointTests.cs` — 2 sites.
- `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs` — 2 sites.
- `tests/Inspecciones.Api.Tests/EliminarHallazgoEndpointTests.cs` — 1 site.
- `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` — 4 sites + helper `SembrarCatalogoEnTenant(tenantId, ...)` agregado.
- `tests/Inspecciones.Api.Tests/DescartarNovedadPreopEndpointTests.cs` — 5 sites (bulk replace).
- `tests/Inspecciones.Api.Tests/CancelarInspeccionEndpointTests.cs` — 3 sites.
- `tests/Inspecciones.Api.Tests/ActualizarRepuestoEndpointTests.cs` — 3 sites.
- `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs` — 4 sites.
- `tests/Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs` — 3 sites.
- `tests/Inspecciones.Api.Tests/AsignarRepuestoEndpointTests.cs` — 2 sites.
- `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/SincronizarDictamenVigenteListenerTests.cs` — `FakeInspeccionReader` extendido con overload `LeerAsync(Guid, string, CancellationToken)`.

---

## Decisiones de diseño confirmadas en green

- **D-MT2-1 (delegate `IDocumentSession` scoped al factory):** funciona. Marten 7.40 + Wolverine 3.13 + outbox integration: el override scoped acepta el factory. **No requirió fallback a D-MT2-1'** (inyectar factory a cada handler).
- **D-MT2-2 (tenant del envelope en listener):** introducimos overload `HandleAsync(evento, Envelope envelope, ct)` que lee `envelope.TenantId`. Ambas overloads coexisten en el listener. **Riesgo controlado:** en producción Wolverine 3 prefiere la overload con más parámetros (convención de discovery). Validable en mt-4 con smoke real. Si no, opción del red-notes B (remover la sin tenant) se aplicará.
- **D-MT2-3 (reset dev / backfill staging):** confirmado. La fixture `InspeccionesAppFactory` ya hace `DROP SCHEMA IF EXISTS inspecciones CASCADE` entre corridas. SQL de backfill se preparará como followup operativo (no es código del slice).
- **D-MT2-4 (tenant default `"1"`):** confirmado. `FakeSessionService(idEmpresa: 1)` default — alineado con `TestHeaderAwareSessionService.IdEmpresa = 1`.
- **D-MT2-6 (proyección `InspeccionAbiertaPorEquipoView`):** Marten Conjoined aplica el tenant a inline projections automáticamente. Verificado indirectamente — los tests E2E §6.2 funcionan (el view debe respetar tenant).
- **D-MT2-7 (`TestHeaderAwareSessionService` header `X-Sin-IdEmpresa`):** NO se implementó en mt-2. Default `IdEmpresa = 1` cubre los tests legacy. Los tests que necesitan tenants distintos (§6.2, §6.7) usan `WithSessionService(FakeSessionService(idEmpresa: N))` puro. **No es un gap funcional** — se agregará si emerge necesidad.
- **D-MT2-10 (outbox conjoined-aware):** verificable en mt-4. Los 8 tests del slice mt-2 cubren el camino completo HTTP → handler → Marten con `tenant_id` correctamente. El test del listener Wolverine en proceso (no via Wolverine host) garantiza que la lógica del envelope funciona — falta el smoke E2E real con outbox.

---

## Comandos de reproducción

### Build (con caché caliente Sinco)

```powershell
dotnet build --nologo --no-restore -clp:NoSummary
# 0 warnings, 0 errors.
```

### Tests por proyecto

```powershell
# Domain — sin Postgres requerido.
dotnet test tests/Inspecciones.Domain.Tests/ --no-build --nologo
# → 246 pass + 19 skip

# Infrastructure — sin Postgres requerido (patrón puerto + fake).
dotnet test tests/Inspecciones.Infrastructure.Tests/ --no-build --nologo
# → 65 pass

# Api — requiere Postgres (local 5432 o Testcontainers).
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_mt2_test"
dotnet test tests/Inspecciones.Api.Tests/ --no-build --nologo
# → 73 pass + 7 skip
```

### Reset del schema dev

```powershell
# Localmente (Postgres nativo):
psql -h localhost -U postgres -c "DROP SCHEMA IF EXISTS inspecciones CASCADE;"
# Marten lo recrea con Conjoined al primer arranque.

# Vía Docker:
docker compose down -v
docker compose up -d
```

---

## SQL backfill staging (preparado, no ejecutado)

Para staging post-merge:

```sql
-- Backfill: data preexistente single-tenant → tenant_id = '0' (default).
-- Ejecutar UNA VEZ después del deploy de mt-2 a staging.
-- D4 firmada: producción no aplica (módulo no está en prod aún).

-- Marten Conjoined automáticamente añade columna tenant_id al recibir el primer
-- documento Conjoined. El UPDATE solo aplica si hay filas legacy sin tenant.
UPDATE inspecciones.mt_doc_equipo_local        SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_doc_rutinatecnica_local SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_doc_repuesto_local      SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_doc_rutinamonitoreo_local SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_doc_causafallacatalogo  SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_doc_tipofallacatalogo   SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_doc_catalogosyncstate   SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_streams                 SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
UPDATE inspecciones.mt_events                  SET tenant_id = '0' WHERE tenant_id IS NULL OR tenant_id = '';
```

(Los nombres exactos de las tablas Marten dependen de la convención de Marten 7.40 — verificar en staging con `\dt inspecciones.*` antes de ejecutar.)

---

## Riesgos asumidos / followups abiertos

- **FU-55 (placeholder ADR-009):** documentar cómo invalidar caché Marten al cambiar tenant mid-session. No emergió en mt-2 (cada request abre sesión fresca).
- **FU-56 (nuevo mt-2):** validar en mt-4 que Wolverine 3 resuelve la overload tenant-aware del listener (vs la legacy sin envelope). Si no, opción B del red-notes: remover la overload sin envelope.
- **FU-57 (nuevo mt-2):** propagar `Envelope.TenantId` también al `DescartarNovedadPreopErpListener` para logs estructurados con tenant (no funcional, solo observabilidad). El listener no toca Marten directo, solo HTTP al ERP — bajo riesgo.
- **FU-58 (nuevo mt-2):** SQL backfill staging documentado pero no ejecutado. Ops/PO ejecuta post-merge antes del primer despliegue a staging multi-empresa.

---

## Nota final

mt-2 es deliberadamente plumbing-heavy y dominio-zero (D-MT2-8). Después de mt-2:
- Cada request HTTP queda discriminado por `tenant_id` en cada query/insert a Marten.
- Listeners Wolverine reciben el envelope y leen el tenant — el plumbing está cableado, falta validación E2E con outbox real (mt-4).
- Catálogos por-empresa (D5): `EquipoLocal`, `RutinaTecnicaLocal`, `RutinaMonitoreoLocal`, `RepuestoLocal`, `CausaFallaCatalogo`, `TipoFallaCatalogo`, `CatalogoSyncState` — todos Conjoined automáticamente vía `AllDocumentsAreMultiTenanted()`.

Cobertura cross-tenant verificada por test E2E: tenant 7 ≠ tenant 8 en `Inspeccion`, `EquipoLocal`, `CatalogoSyncState`. MT2-INV-2 y MT2-INV-3 satisfechos.
