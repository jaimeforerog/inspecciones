# mt-1 — Red phase notes

**Fecha:** 2026-05-19
**Rol:** red (asumido por orquestador — autorización explícita del usuario "sí, autoriza red")
**Spec firmada:** `slices/mt-1-jwt-claims-pipeline/spec.md` (2026-05-19, Santiago Ramirez)

---

## 1. Tests creados (9 total, #9 con Skip)

| # | Archivo | Línea | Escenario spec | Naturaleza del rojo |
|---|---|---|---|---|
| 1 | `tests/Inspecciones.Api.Tests/Auth/FakeSessionServiceTests.cs` | 24 | spec §2 + D-MT1-2 + D-MT1-4 | compile-fail (`FakeSessionService` no existe) |
| 2 | `tests/Inspecciones.Api.Tests/Auth/FakeSessionServiceTests.cs` | 56 | spec §2 — constructor con `capabilities = []` | compile-fail (`FakeSessionService(capabilities:)` no existe) |
| 3 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 90 | §6.1 happy path Test env | compile-fail (`factory.WithSessionService(...)`, `FakeSessionService` no existen) |
| 4 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 131 | §6.3 `IdEmpresa` ausente → 401 `CLAIM-IDEMPRESA-AUSENTE` | compile-fail (`lanzarEnClaim:` en `FakeSessionService` no existe; middleware de error global no mapea `ClaimRequeridaException` aún) |
| 5 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 169 | §6.4 capability `ejecutar-inspeccion` ausente → 403 `PRE-1` | behavior-fail (capability hoy se hardcodea `true` en el endpoint; verde introduce lectura desde `ISessionService.Capabilities`) |
| 6 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 202 | §6.5 `/catalogos/sync` sin capability → 403 (cierre FU-52) | behavior-fail (endpoint hoy no valida capability) |
| 7 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 226 | §6.6 `/catalogos/sync` con capability → no-403 | compile-fail (mismas dependencias de namespace) |
| 8 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 250 | §6.7 `IdUsuario=42` propaga al evento | behavior-fail (endpoint hoy hardcodea `TecnicoIniciador="rmartinez"`) |
| 9 | `tests/Inspecciones.Api.Tests/Auth/SessionServicePipelineTests.cs` | 290 | §6.2 JWT ausente env Development → 401 (SKIP — diferido a mt-2) | skip-by-design (atributo `[Fact(Skip=...)]` con razón documentada en línea) |

> **Nota:** todos los rojos colapsan a "compile-fail" en una primera pasada porque el `using Inspecciones.Infrastructure.Auth;` falla resolviendo el namespace. Una vez que `green` cree el namespace y el `ISessionService`/`FakeSessionService`, los rojos por compile se convierten en behavior-fails (#5, #6, #8) hasta que `green` refactorice los 15 endpoints y agregue el handler de excepción global.

## 2. Verificación del rojo

```powershell
dotnet build tests\Inspecciones.Api.Tests\Inspecciones.Api.Tests.csproj --no-restore
```

**Resultado factual (2026-05-19, ejecutado por el orquestador asumiendo rol `red`):**

```
Inspecciones.Api -> ...\Inspecciones.Api.dll
Auth\FakeSessionServiceTests.cs(2,35): error CS0234: El tipo o el nombre del espacio
  de nombres 'Auth' no existe en el espacio de nombres 'Inspecciones.Infrastructure'
Auth\SessionServicePipelineTests.cs(7,35): error CS0234: El tipo o el nombre del
  espacio de nombres 'Auth' no existe en el espacio de nombres 'Inspecciones.Infrastructure'

ERROR al compilar.
    0 Advertencia(s)
    2 Errores

Tiempo transcurrido 00:00:08.75
```

El compilador se detiene en los `using` antes de avanzar a los otros símbolos faltantes (`WithSessionService`, `ClaimRequeridaException`, etc.). Una vez que `green` cree el namespace, emergerán los demás errores de símbolos — esto es esperado y forma parte del incremento de `green`.

**Restore-friendly:** los proyectos `Inspecciones.Domain`, `Inspecciones.Application`, `Inspecciones.Infrastructure`, `Inspecciones.Api` compilaron limpio (0 advertencias). Solo el proyecto de tests falla por los 2 archivos nuevos — comportamiento esperado de la fase red.

## 3. Símbolos que `green` debe introducir

Lista exhaustiva (importante para que `green` no se desvíe):

### 3.1 Producción — `src/Inspecciones.Infrastructure/Auth/`

- **`namespace Inspecciones.Infrastructure.Auth`** (nuevo)
- **`public interface ISessionService`** con miembros (spec §2):
  - `int IdEmpresa { get; }`
  - `int IdUsuario { get; }`
  - `string NomUsuario { get; }`
  - `int IdSucursal { get; }`
  - `int IdProyecto { get; }`
  - `IReadOnlyCollection<string> Capabilities { get; }`
- **`public sealed class ClaimRequeridaException : Exception`**
  - Constructor: `ClaimRequeridaException(string nombreClaim)` (mensaje canónico: `"La claim '{nombreClaim}' es requerida en el JWT del host."`)
  - Propiedad: `string NombreClaim { get; }`
  - **Mapeo HTTP (en `Program.cs` o middleware dedicado):**
    `ClaimRequeridaException("IdEmpresa")` → `401 Unauthorized` con body
    `{ codigoError: "CLAIM-IDEMPRESA-AUSENTE", mensaje: "..." }`. Mismo patrón para `"UsuarioId"` → `CLAIM-USUARIOID-AUSENTE`.
    Implementación sugerida: `app.UseExceptionHandler(...)` o un wrapper en cada catch del endpoint. El detalle de cuál de las dos es decisión de `green` y se documentará en `green-notes.md`.
- **`public sealed class SincoMiddlewareSessionService : ISessionService`**
  - Lee `MiddlewareAuthorizationToken.SessionVariables()` del paquete `SincoSoft.MYE.Common 1.5.1` (D-MT1-1).
  - Si una claim falta o no parsea, lanza `ClaimRequeridaException(...)`.
  - `Capabilities` devuelve el set completo `["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]` cuando el JWT no expone la claim `capabilities` (D-MT1-4 + FU-54). Si la expone, la prefiere.

### 3.2 Tests — `tests/Inspecciones.Api.Tests/Auth/`

- **`public sealed class FakeSessionService : ISessionService`** con constructores:
  - **Default**: `FakeSessionService()` → defaults del spec §2 (`IdEmpresa=1, IdUsuario=1, NomUsuario="TestUser", IdSucursal=0, IdProyecto=0, Capabilities=["ejecutar-inspeccion","generar-ot","administrar-catalogos"]`).
  - **Con overrides nombrados** (named arguments — todos los tests usan `capabilities:`, `idEmpresa:`, `idUsuario:`, `nomUsuario:`, `lanzarEnClaim:`):
    ```csharp
    public FakeSessionService(
        int idEmpresa = 1,
        int idUsuario = 1,
        string nomUsuario = "TestUser",
        int idSucursal = 0,
        int idProyecto = 0,
        IReadOnlyCollection<string>? capabilities = null,
        string? lanzarEnClaim = null)
    ```
  - Si `lanzarEnClaim == "IdEmpresa"`, el getter `IdEmpresa` lanza `ClaimRequeridaException("IdEmpresa")` (necesario para Test #4 §6.3). Mismo para otras claims si se extiende.

### 3.3 Tests fixture — `InspeccionesAppFactory.cs`

- **Método nuevo** `public InspeccionesAppFactory WithSessionService(ISessionService fake)` que registra el fake en DI antes de crear el cliente (override por test). Patrón sugerido: usar `WithWebHostBuilder(builder => builder.ConfigureServices(...))` y devolver un wrapper `WebApplicationFactory<Program>`-castable a `InspeccionesAppFactory` — o adaptar la firma a `WebApplicationFactory<Program>` y exponer `Services` igualmente.
  - **Alternativa válida:** que `WithSessionService` devuelva `HttpClient` directamente (`WithSessionService(fake).CreateClient()` se convertiría en `factory.CreateClientWithSession(fake)`). Si `green` elige esta variante, los tests se ajustan en consecuencia. Doy preferencia al patrón fluent del spec §12.B porque mantiene la legibilidad.
- **Registro por default en env `Test`**: `Program.cs` (o un extension method) debe registrar `FakeSessionService` cuando `Environment == "Test"` y `SincoMiddlewareSessionService` en el resto (D-MT1-2).
  - **Detalle de cableado:** `InspeccionesAppFactory.ConfigureWebHost` hoy usa `builder.UseEnvironment("Development")` (línea 83 actual). Para activar el bypass por env, **green debe** cambiar el factory a `"Test"` y agregar en `Program.cs` la rama condicional `if (builder.Environment.IsEnvironment("Test")) services.AddScoped<ISessionService, FakeSessionService>(); else services.AddScoped<ISessionService, SincoMiddlewareSessionService>();` (paridad con Attachment).
- **Reemplazo de capability headers:** los tests existentes (`GenerarOTEndpointTests.cs:308` `X-Sin-Capability-Generar-OT`, etc.) seguirán funcionando porque por default el `FakeSessionService` expone las 3 capabilities — los headers `X-Sin-Capability-*` quedan ignorados (no rompen, no aplican). `refactorer` o `green` (a discreción) puede limpiarlos de los tests existentes; por ahora la regla "red no toca tests de otros slices" me obliga a dejarlos quietos.

### 3.4 Refactor de endpoints (15 endpoints, `InspeccionesEndpoints.cs` + `CatalogosEndpoints.cs`)

Cada endpoint hoy con `// Claims mock — ADR-002 tentativo` cambia a:
- Inyectar `ISessionService session` en la lambda.
- Validar capability con `if (!session.Capabilities.Contains("...")) return Forbidden403("PRE-1", ...);` (cuando aplique — ver tabla §9.5 del spec).
- Construir `ClaimsTecnico` desde `session.IdUsuario.ToString(CultureInfo.InvariantCulture)` + `session.Capabilities.Contains("ejecutar-inspeccion")`.
- Eliminar los `const string tecnicoId = "rmartinez";` (13 ocurrencias) y los `const string aprobadorId = "jefe.campo.01";` (2 ocurrencias) — todos reemplazados por la lectura desde `ISessionService`.
- `CatalogosEndpoints.cs:300` (`POST /catalogos/sync`) suma validación capability `ejecutar-inspeccion` o `administrar-catalogos` (D-MT1-9, cierre FU-52).

### 3.5 NuGets corporativos

Spec §12.C/D-MT1-7 confirma:
- `SincoSoft.MYE.Common 1.5.1` y `SincoSoft.MYE.Middleware 1.1.6` a `Directory.Packages.props` + `Inspecciones.Api.csproj` (verificado pre-firma: caché caliente local).
- `Microsoft.Extensions.Logging` bumped a `9.0.3` (alineación con dep transitive).

Estos paquetes los necesita `SincoMiddlewareSessionService` real. Para que **los tests verdes corran sin auth a feeds Azure DevOps**, basta con que la caché global esté caliente (validado por el spec §12.C). FU-53 cubre CI.

## 4. Desviaciones de la spec encontradas al escribir los tests

Ninguna desviación silenciosa. Tres puntos a flagear para la fase verde:

### 4.1 Hook `factory.WithSessionService(fake)` no está en el spec literal

El spec §12.B firmada habla de "tests específicos que necesiten denegar capability construyen un `FakeSessionService`". No define el API exacto del hook en el factory. Asumí el patrón fluent `factory.WithSessionService(fake).CreateClient()` porque es el más legible. Si `green` prefiere otra forma (ej. `factory.CreateClientWithSession(fake)`), los tests se ajustan en un solo lugar — está acotado.

**Recomendación:** que `green` resuelva esto con el método más simple posible (1-2 líneas) y lo documente en `green-notes.md`.

### 4.2 El test #4 (§6.3) no exige forma exacta del middleware de error global

Spec §6.3 nota técnica dice: "el test verifica el comportamiento del **handler de excepción** (middleware de error en `Program.cs` que mapea `ClaimRequeridaException` → 401)". No prescribe `app.UseExceptionHandler(...)` vs catch explícito por endpoint. El test verifica solo el outcome (status 401 + body con `codigoError`). `green` elige.

### 4.3 Test #6 (`/catalogos/sync` con capability) verifica "NO es 403", no un status específico

Por dos razones:
1. El endpoint actual de `erp-4` puede devolver 200, 304, o 207 dependiendo del estado de ETag y los catálogos remotos (que no están mockeados en este test).
2. El objetivo del test es probar que **la admisión funciona** (capability presente ⇒ no rechaza por PRE-1). Verificar el status concreto del catalog sync es responsabilidad de los tests existentes de `SincronizarCatalogosEndpointTests` (que deben seguir verde como regression).

Si `green` agrega un fake de `ICatalogoSyncRepository` que devuelva un 200 determinístico, el test se puede endurecer; por ahora "NO 403" es suficiente para probar el cableado del puerto.

## 5. Conteo final

- **Tests creados:** 9 (#1-#2 unitarios del fake, #3-#9 E2E del pipeline).
- **Tests con `[Fact(Skip=...)]`:** 1 (#9, razón firmada en spec §6.7/§12.B).
- **Tests E2E activos:** 6 (#3, #4, #5, #6, #7, #8) — los 4 que dependen de Marten (#3, #4, #5, #8) requieren `POSTGRES_TEST_CONNSTRING` o Testcontainers.
- **Tests unitarios activos:** 2 (#1, #2) — no requieren Postgres.

## 6. Naming de tests (frase completa en español)

Todos los nombres siguen la convención CLAUDE.md "frase completa, referenciando código de invariante / precondición cuando aplique":

- `FakeSessionService_constructor_default_expone_los_5_claims_canonical_y_set_completo_de_capabilities`
- `FakeSessionService_constructor_con_capabilities_vacias_devuelve_lista_vacia_PRE_CAP_1`
- `POST_inspecciones_con_FakeSessionService_default_responde_201_Created_y_emite_evento_con_TecnicoIniciador_desde_IdUsuario`
- `POST_inspecciones_con_ISessionService_que_lanza_ClaimRequeridaException_en_IdEmpresa_responde_401_Unauthorized_PRE_AUTH_3`
- `POST_inspecciones_con_FakeSessionService_sin_capability_ejecutar_inspeccion_responde_403_Forbidden_PRE_CAP_1`
- `POST_catalogos_sync_sin_capability_responde_403_Forbidden_cierre_FU_52`
- `POST_catalogos_sync_con_capability_administrar_catalogos_no_devuelve_403_regression_erp_4`
- `POST_inspecciones_con_FakeSessionService_IdUsuario_42_emite_evento_con_TecnicoIniciador_42_regression_mt_2`
- `POST_inspecciones_sin_Authorization_en_env_Development_responde_401_Unauthorized_PRE_AUTH_1` (skip)
