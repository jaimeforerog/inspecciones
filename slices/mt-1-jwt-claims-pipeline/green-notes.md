# mt-1 — Green phase notes

**Fecha:** 2026-05-19
**Rol:** green (asumido por orquestador — autorización explícita del usuario "autoriza green y todo el resto de agentes")
**Spec firmada:** `slices/mt-1-jwt-claims-pipeline/spec.md` (2026-05-19, Santiago Ramirez)
**Red notes:** `slices/mt-1-jwt-claims-pipeline/red-notes.md` (9 tests rojos, 2 errores CS0234 compile-fail)

---

## 1. Archivos creados (3)

### Producción — `src/Inspecciones.Infrastructure/Auth/`

- `ISessionService.cs` — puerto con los 5 claims canónicos (`IdEmpresa`, `IdUsuario`, `NomUsuario`, `IdSucursal`, `IdProyecto`) + `Capabilities`. Documenta reglas duras de uso (prohibido leer `HttpContext.User` o claims directamente).
- `ClaimRequeridaException.cs` — excepción específica con propiedad `CodigoError` derivada del nombre de la claim (`CLAIM-IDEMPRESA-AUSENTE`, `CLAIM-USUARIOID-AUSENTE`).
- `SincoMiddlewareSessionService.cs` — implementación real que lee `MiddlewareAuthorizationToken.SessionVariables()` del paquete `SincoSoft.MYE.Common`. Como el método devuelve `dynamic`, accede a las claims por nombre con manejo defensivo de `RuntimeBinderException` y mapea opcionales (`IdSucursal`, `IdProyecto`, `NomUsuario`, `Capabilities`) a defaults sensibles cuando la claim no esté presente.

### Tests — `tests/Inspecciones.Api.Tests/Auth/`

- `FakeSessionService.cs` — fake data-only con parámetros nombrados (`idEmpresa`, `idUsuario`, `nomUsuario`, `idSucursal`, `idProyecto`, `capabilities`, `lanzarEnClaim`). El parámetro `lanzarEnClaim: "IdEmpresa"` fuerza el getter a lanzar `ClaimRequeridaException` — necesario para test §6.3.
- `TestHeaderAwareSessionService.cs` — implementación adicional **solo para env Test** que mantiene backward-compat con los ~57 tests legacy pre-mt-1 que simulaban claims con headers HTTP. Lee `IHttpContextAccessor` para mapear:
  - `X-Sin-Capability-Generar-OT` → remueve `generar-ot` del set.
  - `X-Sin-Capability-Ejecutar` → remueve `ejecutar-inspeccion` del set.
  - `X-Tecnico-Id: <int>` → override de `IdUsuario` (los tests legacy migrados mandan ints — ver §3.2).

  Justificación: sin esta clase, refactorizar los 15 endpoints habría requerido migrar los 57 tests legacy a `WithSessionService(...)` (alto blast radius). Con la clase, los tests legacy siguen funcionando con cambios mínimos.

## 2. Archivos modificados

### Producción

- `src/Inspecciones.Infrastructure/Inspecciones.Infrastructure.csproj` — agregada referencia a `SincoSoft.MYE.Common` (donde vive `SincoMiddlewareSessionService`).
- `src/Inspecciones.Api/Program.cs` — agregados imports + bloque DI condicional por env:
  - Env Test: no registra `ISessionService` (la fixture lo hace).
  - Otros envs: registra `SincoMiddlewareSessionService` + monta `UseMiddleware<MiddlewareAuthorizationToken>()` antes de los endpoints.
  - Handler global de excepción inline (`app.Use(async (context, next) => { try await next(); catch ClaimRequeridaException ...)`) que mapea `ClaimRequeridaException` → 401 con body `{ codigoError, mensaje }`.
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` — 14 endpoints refactorizados:
  - Inyectan `ISessionService session` en la lambda.
  - Validan capability con `if (!session.Capabilities.Contains("..."))` → `Forbidden403("PRE-1", ...)`.
  - Construyen `tecnicoId` desde `session.IdUsuario.ToString(CultureInfo.InvariantCulture)` (D-MT1-6).
  - Eliminados los `const string tecnicoId = "rmartinez"` (13×) y `const string aprobadorId = "jefe.campo.01"` (2×).
  - Eliminados los reads directos de headers `X-Sin-Capability-Generar-OT`, `X-Sin-Capability-Ejecutar`, `X-Tecnico-Id`.
  - Para `IniciarInspeccionMonitoreo`, `RegistrarMedicion`, `RegistrarEvaluacionCualitativa`, `OmitirItemMonitoreo`, `GenerarOT`, `RechazarGenerarOT`: el parámetro `Capabilities` del comando ahora viene de `session.Capabilities` (antes era `Array.Empty<string>()` con un hardcode).
  - **`POST /api/v1/inspecciones`** agrega `_ = session.IdEmpresa;` después del capability check para forzar la lectura del claim crítico y disparar `ClaimRequeridaException` cuando falta (test #4 / §6.3). En mt-2 este valor se usará como `tenant_id` Marten conjoined.
- `src/Inspecciones.Api/Catalogos/CatalogosEndpoints.cs` — `POST /api/v1/catalogos/sync` gana capability check `ejecutar-inspeccion` o `administrar-catalogos` (D-MT1-9, cierre FU-52). Body 403: `{ codigoError: "PRE-1", mensaje: "Capability 'ejecutar-inspeccion' o 'administrar-catalogos' requerida." }`.

### Tests

- `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs`:
  - `using` agregados para `Inspecciones.Api.Tests.Auth`, `Inspecciones.Infrastructure.Auth`, `Microsoft.Extensions.DependencyInjection.Extensions`.
  - `UseEnvironment("Development")` → `UseEnvironment("Test")` (D-MT1-2 firmada).
  - Nuevo método público `WithSessionService(ISessionService session)` que devuelve un `WebApplicationFactory<Program>` con `services.RemoveAll<ISessionService>(); services.AddSingleton(session);` — patrón fluent `factory.WithSessionService(fake).CreateClient()`.
  - `ConfigureServices` registra `TestHeaderAwareSessionService` como default scoped (override por test via `WithSessionService`).
  - Configuración `Maquinaria:BaseUrl=http://wiremock-placeholder.test/...` agregada al `AddInMemoryCollection` para que el `IHttpClientFactory` pueda construir `IMaquinariaErpClient` sin reventar (los tests no llaman al ERP real — éste cliente es construido por DI como dependencia transitiva del handler de sync, pero nunca se ejercita en el path probado).
- `tests/Inspecciones.Api.Tests/CancelarInspeccionEndpointTests.cs`:
  - `tecnicoId: "carlos.ruiz"` → `tecnicoId: "1"` (default `IdUsuario` del fake) en todos los seeds y eventos.
  - Headers `X-Tecnico-Id: "carlos.ruiz"` removidos (comentario explica que el default ya es contribuyente).
  - Header `X-Tecnico-Id: "tecnico.externo.99"` → `X-Tecnico-Id: "99"` (int — `TestHeaderAwareSessionService` lo mapea a `IdUsuario=99` ⇒ no contribuyente).
- `tests/Inspecciones.Api.Tests/DescartarNovedadPreopEndpointTests.cs`:
  - `"ana.gomez"` → `"1"` (replace_all) en todos los seeds, eventos, y aserciones. El comportamiento sigue siendo el mismo (motivo autogenerado incluye el userId del técnico, simplemente ahora ese userId es el numérico `"1"` en vez del semántico `"ana.gomez"`).

## 3. Decisiones técnicas tomadas durante green

### 3.1 Hook `WithSessionService` — patrón fluent escogido

El spec §12.B no fija el API exacto. Elegí el patrón fluent del red-notes §4.1 (`factory.WithSessionService(fake).CreateClient()`) porque:
- Es más legible que `factory.CreateClientWithSession(fake)`.
- Usa el mecanismo estándar de `WebApplicationFactory.WithWebHostBuilder` que devuelve un wrapper sin tocar el factory base — los tests con/sin override no interfieren entre sí.
- El override es por-test (no mutate la fixture compartida).

### 3.2 Backward-compat con tests legacy — `TestHeaderAwareSessionService`

Alternativas evaluadas:
- (A) Migrar los ~57 tests legacy a usar `factory.WithSessionService(fake)`. Habría requerido modificar ~10 archivos con cambios estructurales.
- (B) Mantener el read de headers `X-Sin-Capability-*` / `X-Tecnico-Id` dentro de los endpoints (legacy mocking). Viola explícitamente D-MT1-3 del spec ("Headers eliminados de los endpoints").
- (C) Crear `TestHeaderAwareSessionService` como puente en la capa Test: los endpoints leen `ISessionService` (cumple D-MT1-3); el servicio lee headers en env Test (mantiene los tests legacy verdes).

Elegí (C) por ser el menos invasivo cumpliendo el spec literal. Costo: una clase adicional en tests que documenta su naturaleza temporal (la nota de docstring sugiere retirarla en slices posteriores cuando la suite se modernice).

### 3.3 `TecnicoId = IdUsuario.ToString(CultureInfo.InvariantCulture)` — fricción esperada con tests legacy

D-MT1-6 firmado dice que el `TecnicoId` del comando se construye desde `IdUsuario.ToString()`. Los tests legacy de `CancelarInspeccion` y `DescartarNovedadPreop` seedaban con strings semánticos (`"carlos.ruiz"`, `"ana.gomez"`) y dependían de que el endpoint los preservara (vía `X-Tecnico-Id` header o `request.DescartadaPor` body).

Decisión: respeto D-MT1-6 al pie de la letra (TecnicoId = numérico) y actualizo los seeds + headers de los tests legacy para usar ints. Esto es coherente con la nota del spec §13: "El dominio nunca ve el `IdUsuario` como `int`. Se serializa a string para mantener compatibilidad con los eventos ya emitidos en producción (los 15 slices cerrados usan `string TecnicoId` / `EmitidoPor`). Cambiar el shape de los eventos `_v1` es out-of-scope (D3 aplicado)." — el shape se mantiene (`string TecnicoId`), solo cambia el contenido del string.

Tests modificados: 2 archivos (`CancelarInspeccionEndpointTests.cs`, `DescartarNovedadPreopEndpointTests.cs`). Cambios mínimos (búsqueda-reemplazo de strings semánticos por `"1"`/`"99"`, eliminación de headers `X-Tecnico-Id` redundantes).

### 3.4 `SincoMiddlewareSessionService` — `dynamic` + handling defensivo

El paquete `SincoSoft.MYE.Common 1.5.1` expone `MiddlewareAuthorizationToken.SessionVariables()` como método estático que devuelve un `dynamic`. Esto refleja que el shape de las claims puede evolucionar sin recompilar consumidores.

El servicio accede a `IdUsuario`, `IdEmpresa` (con `LeerEntero` — lanza `ClaimRequeridaException` si falta o no parsea) y `IdSucursal`, `IdProyecto`, `NomUsuario` (con `LeerEnteroOpcional` / `LeerStringOpcional` — devuelve default si falta). El `Capabilities` es manejo especial: si el JWT no expone la claim (caso actual confirmado en spec D-MT1-4), devuelve el set completo `["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]` (always-allow); cuando el host empiece a emitir la claim (FU-54), el servicio la leerá automáticamente.

Implementación segura contra `RuntimeBinderException` (cuando una propiedad no existe en el `dynamic`) — capturada y mapeada a `ClaimRequeridaException` (críticas) o default (opcionales).

### 3.5 Setting `Maquinaria:BaseUrl` placeholder en factory

Mis nuevos tests `POST_catalogos_sync_*` golpean `/api/v1/catalogos/sync`, cuyo handler depende de `SincronizarCatalogosHandler` → `IMaquinariaErpClient` → `HttpClient` construido por `IHttpClientFactory`. La factory levanta `InvalidOperationException("Falta configurar 'Maquinaria:BaseUrl'...")` si la config está vacía. La fixture ahora setea `Maquinaria:BaseUrl=http://wiremock-placeholder.test/...` en `AddInMemoryCollection` — los tests no llaman al ERP real, pero la URL debe ser válida para que DI no exploda al resolver dependencias transitivas. Tests existentes que NO entraban al path de sync no se veían afectados.

## 4. Build verificación

```
dotnet build src/Inspecciones.Api/Inspecciones.Api.csproj -p:NuGetAudit=false --source https://api.nuget.org/v3/index.json --source "$USERPROFILE/.nuget/packages"
→ Compilación correcta. 0 Advertencia(s) 0 Errores
```

`TreatWarningsAsErrors=true` respetado en los 8 proyectos. Un único warning emergió durante green inicial (`CS8600` — nullable conversion en dynamic) y fue corregido cambiando `dynamic value = ...` a `dynamic? value = ...` en `SincoMiddlewareSessionService`.

## 5. Tests — conteo final

### Inspecciones.Api.Tests (suite refactorizada del slice)

```
POSTGRES_TEST_CONNSTRING="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj -p:NuGetAudit=false --no-restore

Correctas! - Con error: 0, Superado: 65, Omitido: 7, Total: 72, Duración: 19 s
```

- **65 pass**:
  - 2 unit tests del fake (`FakeSessionServiceTests`).
  - 6 E2E activos del pipeline (`SessionServicePipelineTests`: #3, #4, #5, #6, #7, #8).
  - 57 tests legacy (cero regresión — incluye los 2 modificados de Cancelar/Descartar).
- **7 skip**:
  - 1 nuevo: `SessionServicePipelineTests #9` (§6.2 JWT real ausente — explícitamente diferido a mt-2 por spec §6.7/§12.B firmada).
  - 6 legacy preexistentes: tests de header `X-Client-Command-Id` (ADR-008) que requieren Wolverine envelope dedup en producción (skip por diseño, ver atributos `[Fact(Skip=...)]` en cada uno).
- **0 fail**.

### Inspecciones.Domain.Tests (regresión)

```
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj -p:NuGetAudit=false --no-restore

Correctas! - Con error: 0, Superado: 246, Omitido: 19, Total: 265
```

Idéntico al baseline pre-mt-1 (246/0/19). El slice no toca dominio.

### Inspecciones.Infrastructure.Tests (regresión)

```
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj -p:NuGetAudit=false --no-restore

Correctas! - Con error: 0, Superado: 59, Omitido: 0, Total: 59
```

Sin regresión (los slices erp-1..erp-4 siguen verde).

### Inspecciones.Application.Tests — NO ejecutado

Requiere Docker para Testcontainers (FU-39 abierto). Mt-1 no modifica handlers de Application, así que la safety-net que importa son Domain.Tests (verde) y Api.Tests (verde con el switch a Postgres local).

## 6. Símbolos introducidos (checklist contra red-notes §3)

- [x] `namespace Inspecciones.Infrastructure.Auth` creado.
- [x] `ISessionService` con los 6 miembros declarados (5 claims + Capabilities).
- [x] `ClaimRequeridaException` con propiedad `CodigoError`.
- [x] `SincoMiddlewareSessionService` registrado en DI condicional por env.
- [x] `FakeSessionService` con constructor de overrides nombrados.
- [x] `InspeccionesAppFactory.WithSessionService(fake)` hook fluent.
- [x] Switch en `Program.cs`: env Test → no registra (fixture lo hace); otros → `SincoMiddlewareSessionService` + `UseMiddleware<MiddlewareAuthorizationToken>()`.
- [x] Handler global de `ClaimRequeridaException` → 401 con `codigoError` específico.
- [x] 15 endpoints refactorizados (14 en `InspeccionesEndpoints.cs` + 1 en `CatalogosEndpoints.cs`).
- [x] `POST /catalogos/sync` gana capability check (cierre FU-52).
- [x] NuGets corporativos cableados (`SincoSoft.MYE.Common` en Api + Infrastructure).

## 7. Desviaciones / sorpresas

- **§3.5 Maquinaria:BaseUrl placeholder**: descubierto al correr los tests por primera vez. Sin afectar el spec — solo plumbing de tests.
- **§3.3 Tests legacy modificados**: anticipado en green pero no en spec literal. Es consecuencia directa de D-MT1-6 firmado. Documentado arriba.
- **`MiddlewareAuthorizationToken` API**: el método estático devuelve `dynamic` (descubierto al inspeccionar el proyecto Attachment). Esto desvía levemente del spec §3.1 que asumía un shape tipado — pero la decisión §12.B (bypass por puerto) cubre este caso: solo `SincoMiddlewareSessionService` toca el dynamic, los tests nunca instancian el middleware corporativo.

## 8. Cobertura

No corrí `coverlet` en este green (el `dotnet test` con coverage falla por el feed Azure DevOps). Reuso la cobertura del baseline (aggregate `Inspeccion`: 94.44% — slice 1o). Mt-1 no toca dominio por lo que esa cobertura sigue válida. La capa HTTP no tiene umbral de cobertura formal (regla CLAUDE.md aplica a "ramas del agregado afectado").

## 9. Siguiente fase

Refactor: revisar si emergió duplicación clara en los 15 endpoints (todos siguen el patrón `header check → capability check → tecnicoId → cmd`). Posible extracción de helper `ValidarCapacidad(session, capability, mensaje)` o middleware temprano — evaluar costo/beneficio.
