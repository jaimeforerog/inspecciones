# green-notes — fix FU-32 consolidado

## 1. Implementación

Fix transversal en plumbing de tests. **No toca código de dominio** — los 6 cambios viven
en archivos de infraestructura (`Program.cs` para el bug Oakton, fixture / csproj / config
para los amplificadores).

### 1.1 Cambio núcleo (red original): `Program.cs` Oakton lifecycle

`Program.cs:138-149` reemplaza la rama única `await app.RunOaktonCommands(args)` por:

```csharp
var oaktonCommand = args.Length > 0 && !args[0].StartsWith("--");
if (oaktonCommand)
{
    await app.RunOaktonCommands(args);
}
else
{
    await app.RunAsync();
}
```

- **CLI Oakton (`dotnet run -- check-env`)**: `args[0] = "check-env"`, no empieza con `--`,
  toma rama Oakton → comportamiento original preservado.
- **`dotnet run`**: `args.Length == 0`, toma rama `RunAsync` → host arranca normal.
- **`WebApplicationFactory<Program>`**: pasa flags como `--environment=Development`. Ahora
  el check `!args[0].StartsWith("--")` evita falsos positivos de Oakton y entra por `RunAsync`,
  lo cual permite a `TestServer` capturar el pipeline HTTP.

### 1.2 Switch local Postgres / Testcontainers (ampliación)

`InspeccionesAppFactory.cs` — el constructor lee `POSTGRES_TEST_CONNSTRING`:

- **Modo local** (var definida): no instancia `PostgreSqlContainer`. `InitializeAsync`:
  1. `EnsureDatabaseExistsAsync` — conecta a la DB admin `postgres`, hace
     `SELECT 1 FROM pg_database WHERE datname = ...`, si no existe ejecuta `CREATE DATABASE`.
  2. `DropMartenSchemasAsync` — `DROP SCHEMA IF EXISTS inspecciones CASCADE` + loop dinámico
     que dropea `public.wolverine_%`.
- **Modo Testcontainers** (var ausente): comportamiento original preservado para CI.

### 1.3 Override de connection string (ampliación)

`ConfigureWebHost` ahora hace **dos** registros:

```csharp
builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
builder.ConfigureAppConfiguration((_, config) => { config.AddInMemoryCollection(...); });
```

Causa raíz: `appsettings.Development.json` define
`ConnectionStrings:Postgres = "...Database=inspecciones..."` (DB de desarrollo del usuario).
Cuando WAF arranca con `UseEnvironment("Development")`, ese archivo se carga y sobrescribía
la connection string de tests si solo usábamos `AddInMemoryCollection` (problema de orden
de providers no determinístico). `UseSetting` se aplica al `IConfiguration` final con prioridad
absoluta sobre cualquier source.

### 1.4 Supresión EventLogLoggerProvider (ampliación)

```csharp
builder.ConfigureServices(services =>
{
    var eventLogDescriptors = services
        .Where(d => d.ServiceType == typeof(ILoggerProvider) &&
                    (d.ImplementationType?.FullName?.Contains("EventLog", StringComparison.Ordinal) ?? false))
        .ToList();
    foreach (var d in eventLogDescriptors) services.Remove(d);
});
```

Wolverine drena colas durables en `DisposeAsync`. Si hay mensajes pendientes, intenta
loguear error de "Failed to drain outstanding messages" y el logger de Windows Event Log
trata de escribir, lo cual requiere `SeSecurityPrivilege`. Sin ese permiso lanza
`SecurityException`, que VSTest contabiliza como fallo del test recién terminado.

Quitando el provider del DI elimina el ruido. Los logs siguen disponibles via consola en debug.

### 1.5 Override `InvariantGlobalization` (ampliación)

`tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj`:

```xml
<PropertyGroup>
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

`Directory.Build.props` define `<InvariantGlobalization>true</InvariantGlobalization>` a nivel
de repo (correcto para producción — reduce tamaño de imagen + arranque más rápido). Pero
FluentAssertions, al formatear un mensaje de fallo, llama
`FindExceptionAssembly()` que enumera `AppDomain.CurrentDomain.GetAssemblies()` y, para cada
uno, llama `Assembly.GetLocale()`, que internamente intenta resolver una `CultureInfo`. En
invariant mode esto lanza `CultureNotFoundException` y el mensaje real del assert nunca se
imprime — solo el stack de FluentAssertions, lo cual hace imposible diagnosticar fallos.

Override solo aplica al proyecto de tests; producción mantiene invariant mode.

### 1.6 `xunit.runner.json` (defensa en profundidad)

```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false,
  "maxParallelThreads": 1,
  "preEnumerateTheories": false
}
```

`maxParallelThreads: 1` evita paralelismo dentro de la collection. La fixture
`InspeccionesAppFactory` es `ICollectionFixture` (compartida entre todos los tests del
collection); aunque xUnit dentro de un `[Collection(...)]` serializa por default, fijarlo
explícitamente protege contra futuros tests que extiendan los seeds compartidos.

## 2. Resultado de `dotnet test`

```
POSTGRES_TEST_CONNSTRING="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj
```

**Antes del fix (estado red)**: `Con error 30, Superado 0, Omitido 2, Total 32`.

**Después del fix**: `Con error 6, Superado 24, Omitido 2, Total 32, Duración 7s`.

Los 6 tests que siguen rojos son bugs preexistentes en handlers/endpoints, no FU-32:
- `RegistrarHallazgo.happy_path` / `replay`: 400 BadRequest (bug en endpoint).
- `GenerarOT.happy_path` / `RechazarOT.happy_path`: `*En` con today's date (handler usa `DateTime.UtcNow` viola CLAUDE.md).
- `GenerarOT.sin_capability` / `RechazarOT.sin_capability`: 500 en vez de 403 (bug en handling).

Registrados como FU separados (ver `FOLLOWUPS.md`).

**Domain tests**: `Correctas! Con error 0, Superado 197, Omitido 12, Total 209`. Sin regresión.

## 3. Cómo correr los tests localmente

PowerShell:

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj
```

Bash:

```bash
POSTGRES_TEST_CONNSTRING="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test" \
  dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj
```

Sin la env var, la fixture usa Testcontainers — requiere Docker corriendo (caso CI).

La DB `inspecciones_test` se crea automáticamente si no existe. El schema `inspecciones`
se dropea entre corridas para aislar siembras. Los datos persisten **dentro** de una
corrida (entre tests del mismo collection), por lo que cada test debe usar IDs únicos.

## 4. Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `src/Inspecciones.Api/Program.cs` | Condicional Oakton lifecycle (líneas 138-149) |
| `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` | Switch local/Testcontainers; `EnsureDatabaseExistsAsync`; `DropMartenSchemasAsync`; `UseSetting`; supresión EventLog |
| `tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj` | `<InvariantGlobalization>false</InvariantGlobalization>` |
| `tests/Inspecciones.Api.Tests/xunit.runner.json` | `maxParallelThreads: 1`, `preEnumerateTheories: false` |
| `tests/Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs` | (red phase) 7 skip → fact, constante eliminada |
| `slices/fix-FU-32/spec.md` | Scope extension 2026-05-11 |
| `slices/fix-FU-32/red-notes.md` | Sección §6 scope extension + diagnóstico de los 6 fallos remanentes |
| `slices/fix-FU-32/green-notes.md` | Este archivo |
