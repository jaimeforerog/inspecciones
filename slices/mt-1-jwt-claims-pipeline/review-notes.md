# mt-1 — Review phase notes

**Fecha:** 2026-05-19
**Rol:** reviewer (asumido por orquestador — autorización explícita)
**Predecesores:** spec firmada (2026-05-19), red-notes (9 tests rojos), green-notes (todos verdes), refactor-notes (sin cambios)

---

## 1. Auditoría contra reglas duras CLAUDE.md

### 1.1 Nullable + TreatWarningsAsErrors

✅ `dotnet build Inspecciones.sln -p:NuGetAudit=false --no-restore` → **0 advertencia(s), 0 errores** en los 8 proyectos. Todos los archivos nuevos respetan nullable annotations (`dynamic?` en `SincoMiddlewareSessionService` fue el único punto donde emergió un CS8600 y se corrigió en green).

### 1.2 Naming español dominio / inglés plumbing

✅ Cumple:
- Dominio: ningún cambio (mt-1 es plumbing-only).
- Plumbing: `ISessionService`, `SincoMiddlewareSessionService`, `FakeSessionService`, `TestHeaderAwareSessionService`, `ClaimRequeridaException`. Naming inglés correcto para Infrastructure.
- Claims del JWT: `IdEmpresa`, `IdUsuario`, `NomUsuario`, `IdSucursal`, `IdProyecto` — naming es español por paridad con `MiddlewareAuthorizationToken` del paquete corporativo Sinco (D-MT1-1). Aceptable porque es contrato externo, no dominio interno.

### 1.3 Records vs classes

✅ Cumple. `ISessionService` es interface (no record). Implementaciones son `sealed class` (`SincoMiddlewareSessionService`, `FakeSessionService`, `TestHeaderAwareSessionService`) — apropiado para servicios stateful con DI. `ClaimRequeridaException` es `sealed class` (excepciones siempre son class).

### 1.4 TimeProvider — prohibido `DateTime.UtcNow` en dominio

✅ Cumple. Grep no encontró ninguna ocurrencia de `DateTime.UtcNow` ni `DateTimeOffset.UtcNow` en `src/` (excepto el comentario en `Program.cs` que documenta la regla). mt-1 no toca dominio.

### 1.5 `Guid.NewGuid()` solo en handlers, no en dominio

✅ Cumple. Grep en `src/Inspecciones.Domain/` → 0 matches. mt-1 no toca dominio.

### 1.6 Tipos de IDs

✅ Cumple. `IdEmpresa`, `IdUsuario`, `IdSucursal`, `IdProyecto` son `int` (System.Int32) — paridad 1:1 con el ERP y con Attachment (D-MT1-1). `NomUsuario` es `string`. `Capabilities` es `IReadOnlyCollection<string>`. No se introdujeron `Guid` para identidades del JWT.

### 1.7 `UbicacionGps` / `BlobUri` / VOs

✅ Cumple. mt-1 no toca VOs de ubicación ni adjuntos.

### 1.8 Identidad: handler recibe claims por parámetro, dominio nunca conoce JWTs

✅ Cumple. Los handlers reciben `ClaimsTecnico` por parámetro (sin cambios desde slice 1b). El nuevo `ISessionService` vive en Infrastructure, no en Domain. Ningún archivo de `src/Inspecciones.Domain/` se modificó.

### 1.9 Cobertura ramas ≥ 85% del agregado afectado

✅ N/A. mt-1 no toca aggregate (spec §1: "Agregado afectado: ninguno"). La cobertura del aggregate `Inspeccion` se preserva en 94.44% (slice 1o baseline).

### 1.10 Eventos `_v1` no modificados

✅ Cumple. mt-1 no toca eventos. Cero archivos de `src/Inspecciones.Domain/Inspecciones/Events/` modificados.

### 1.11 Soft delete con `*Eliminado`

✅ N/A. mt-1 no toca soft-delete.

### 1.12 `Apply` puro

✅ N/A. mt-1 no toca aggregates.

### 1.13 Rebuild test obligatorio

✅ N/A. mt-1 no emite eventos. La spec §6.8 lo documenta explícitamente.

### 1.14 Atomicidad de eventos / un único `SaveChangesAsync`

✅ N/A. mt-1 no toca handlers que persisten.

### 1.15 Nueva regla mt-1: "Todo endpoint HTTP lee identidad vía `ISessionService`; prohibido `HttpContext.User`"

✅ Cumple. Grep `HttpContext.User|ctx.User|context.User` en `src/` → solo el comentario en docstring de `ISessionService`. Ningún endpoint lee claims directamente.

## 2. Auditoría contra convenciones de tests

### 2.1 xUnit + FluentAssertions, cero mocks del dominio

✅ Cumple. Los tests nuevos (`FakeSessionServiceTests`, `SessionServicePipelineTests`) usan xUnit + FluentAssertions. Cero mocks del dominio — el `FakeSessionService` es un fake de un puerto de Infrastructure (no de dominio), `TestHeaderAwareSessionService` igual.

### 2.2 Marten embebido para integración

✅ Cumple. La fixture sigue usando Postgres local (via `POSTGRES_TEST_CONNSTRING`) o Testcontainers. Sin cambios desde slice 1o.

### 2.3 `WebApplicationFactory<Program>` para E2E

✅ Cumple. `InspeccionesAppFactory : WebApplicationFactory<Program>` se preserva, añadiendo el método `WithSessionService` que devuelve otro `WebApplicationFactory<Program>` (patrón estándar).

### 2.4 Naming en español, frase completa, referenciando código de invariante

✅ Cumple. Los 9 tests nuevos siguen la convención:
- `FakeSessionService_constructor_default_expone_los_5_claims_canonical_y_set_completo_de_capabilities`
- `POST_inspecciones_con_ISessionService_que_lanza_ClaimRequeridaException_en_IdEmpresa_responde_401_Unauthorized_PRE_AUTH_3`
- `POST_catalogos_sync_sin_capability_responde_403_Forbidden_cierre_FU_52`
- etc.

Todos los nombres referencian código de precondición (`PRE_AUTH_3`, `PRE_CAP_1`, `FU_52`, `regression_erp_4`).

## 3. Auditoría contra spec firmada

### 3.1 Símbolos del §3 (red-notes)

✅ Todos los 11 símbolos enumerados en red-notes §3 introducidos y verificados (ver green-notes §6).

### 3.2 Decisiones firmadas

- D-MT1-1 (`IdEmpresa` int paridad Attachment): ✅ implementado.
- D-MT1-2 (bypass env Test): ✅ Program.cs registra condicional, fixture inyecta default + permite override.
- D-MT1-3 (capabilities desde JWT, headers eliminados de prod): ✅ los endpoints no leen headers `X-Sin-Capability-*` ni `X-Tecnico-Id`. Esos headers solo viven en `TestHeaderAwareSessionService` (capa de tests).
- D-MT1-4 (`Forbidden403` mantenido): ✅ helper conservado, `Results.Forbid()` no se reintrodujo.
- D-MT1-5 (`ClaimsTecnico` construido desde `ISessionService`): ✅ implementado.
- D-MT1-6 (`TecnicoId = IdUsuario.ToString()`): ✅ implementado. Causó modificación de tests legacy (documentado en green-notes §3.3).
- D-MT1-7 (NuGets corporativos): ✅ ya estaban en spec pre-firma; el slice mt-1 los consume en `Inspecciones.Api.csproj` y `Inspecciones.Infrastructure.csproj`.
- D-MT1-8 (sin cambios al dominio): ✅ cumplido literalmente.
- D-MT1-9 (`/catalogos/sync` gana capability check): ✅ cierre FU-52.
- D-MT1-10 (`MaquinariaErpClient` no se toca): ✅ adapter intacto; FU-44 sigue rolando a mt-3.

### 3.3 Preguntas abiertas resueltas

- §12.A (A.3 — diferir enforcement cross-proyecto): ✅ implementado. `ProyectosAsignados: new HashSet<int> { request.ProyectoId }` preserva comportamiento mock.
- §12.B (bypass via `ISessionService`): ✅ implementado. `MiddlewareAuthorizationToken` no se monta en env Test.
- §12.C (caché NuGet caliente para local; FU-53 para CI): ✅ verificado durante green (build verde con `--source nuget.org --source $USERPROFILE/.nuget/packages`).
- §12.D (D.2 — capabilities always-allow en mt-1; FU-54): ✅ `SincoMiddlewareSessionService.Capabilities` retorna set completo cuando la claim falta.

## 4. Hallazgos y veredicto

### 4.1 Hallazgos menores (no bloqueantes)

1. **`TestHeaderAwareSessionService` es deuda técnica**: la clase existe solo para no romper ~57 tests legacy. Su retiro es un slice futuro (no urgente). Documentado en docstring y green-notes §3.2.
2. **`_ = session.IdEmpresa;` solo en `POST /inspecciones`**: enforcement parcial del claim crítico. mt-2 lo extenderá a todos los endpoints como parte del `tenant_id` Marten. Documentado en refactor-notes §1.2.
3. **`SincronizarCatalogosHandler` se construye via DI aunque el endpoint retorne 403 antes**: minor — los tests #6/#7 requirieron añadir `Maquinaria:BaseUrl` placeholder al factory. La construcción no realiza llamada al ERP, así que no hay impacto funcional, pero un middleware temprano de auth (o convertir el handler en factory lazy) lo evitaría. No vale el esfuerzo en mt-1.

### 4.2 Followups que el slice abre

- **Mantener FU-44** (propagación JWT entrante a `MaquinariaErpClient`) abierto. Rola a mt-3 según spec D-MT1-10.
- **Mantener FU-53** (CI credentials para feeds Azure DevOps) abierto. Documentado en spec §12.C.
- **Mantener FU-54** (cross-team con Sergio/David para claim `capabilities`) abierto. Documentado en spec §12.D.

### 4.3 Followups que el slice cierra

- **FU-14** (claims reales desde JWT) — cerrado por D-MT1-5/6.
- **FU-52** (capability check en `/catalogos/sync`) — cerrado por D-MT1-9.

### 4.4 Veredicto

**APROBADO** (con followups FU-53, FU-54, FU-44 abiertos por diseño).

Razones:
- Todas las reglas duras de CLAUDE.md cumplen.
- Las 10 decisiones D-MT1-1..D-MT1-10 implementadas literalmente.
- Las 4 preguntas abiertas (§12.A..§12.D) resueltas con la decisión firmada.
- Cero regresión en Domain.Tests (246/0/19) ni Infrastructure.Tests (59/0/0).
- Suite Api.Tests pasa de 57 pass a 65 pass (8 tests nuevos del slice activos + 1 skip diferido a mt-2).
- Build limpio en los 8 proyectos (0 warnings, 0 errores).

mt-1 desbloquea mt-2 (Marten conjoined por `IdEmpresa`) y mt-3 (propagación del JWT entrante al ERP). El plumbing está listo para que mt-2 introduzca el `tenant_id` sin tocar más HTTP.

## 5. Recomendaciones para los siguientes slices

1. **mt-2 (Marten conjoined)**: agregar `_ = session.IdEmpresa;` a los 14 endpoints restantes O introducir un middleware temprano que valide el claim antes de despachar al handler. El segundo es más limpio.
2. **mt-2/mt-3**: cuando se confirme con Sergio/David el contrato real de `capabilities` (FU-54), apretar el default de `SincoMiddlewareSessionService.Capabilities` de "always-allow" a "vacío" — esto convierte el "default permisivo" en "default deniegues" y obliga al host a propagar la claim explícitamente.
3. **Slice de modernización de tests**: si emerge ancho de banda, migrar los ~10 tests legacy de `CancelarInspeccion`/`DescartarNovedadPreop`/etc a `WithSessionService(...)` puro y retirar `TestHeaderAwareSessionService`. No urgente — la clase es estable.
