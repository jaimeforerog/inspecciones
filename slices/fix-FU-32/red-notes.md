# red-notes — fix FU-32

## 1. Tests destrabados

### RechazarGenerarOTEndpointTests.cs — 7 tests destrabados

| # | Método | Escenario spec |
|---|---|---|
| 1 | `POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto` | §6.1 |
| 2 | `POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1` | §6.3 |
| 3 | `POST_rechazar_generar_ot_inspeccion_inexistente_responde_404_Not_Found_PRE_2` | §6.12 |
| 4 | `POST_rechazar_generar_ot_motivo_corto_responde_422_I_F6_MOTIVO` | §6.4 |
| 5 | `POST_rechazar_generar_ot_inspeccion_no_firmada_responde_422_I_F6_ESTADO` | §6.6 |
| 6 | `POST_rechazar_generar_ot_OT_ya_solicitada_responde_409_Conflict_I_F6_OT_YA_SOLICITADA` | §6.10 |
| 7 | `POST_rechazar_generar_ot_sin_header_X_Client_Command_Id_responde_400_Bad_Request` | Header |

Quedó en skip (razón Wolverine, no FU-32):
- `POST_rechazar_generar_ot_replay_mismo_ClientCommandId_no_duplica_eventos_ADR_008` — §6.13

### Otros tests sin cambio de skip (ya no tenían SkipReasonFu32)

- `HealthChecksTests.cs` — 3 tests, sin skip, ya fallaban por FU-32
- `IniciarInspeccionEndpointTests.cs` — 2 tests, sin skip, ya fallaban por FU-32
- `RegistrarHallazgoEndpointTests.cs` — 2 tests, sin skip, ya fallaban por FU-32
- `EliminarHallazgoEndpointTests.cs` — 3 tests, sin skip, ya fallaban por FU-32
- `AsignarRepuestoEndpointTests.cs` — 5 tests, sin skip, ya fallaban por FU-32 o DocumentAlreadyExists
- `GenerarOTEndpointTests.cs` — 7 tests activos, sin skip, ya fallaban por FU-32 o DocumentAlreadyExists

## 2. Comando para correr los tests destrabados

```
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj --filter "ClassName~RechazarGenerarOTEndpointTests"
```

Para correr todos los Api.Tests:

```
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj
```

## 3. Resultado de dotnet test

```
Con error! - Con error: 30, Superado: 0, Omitido: 2, Total: 32, Duración: ~7 s
```

Los 2 skip son:
- `GenerarOTEndpointTests.POST_generar_ot_replay_mismo_ClientCommandId...` — razón Wolverine
- `RechazarGenerarOTEndpointTests.POST_rechazar_generar_ot_replay_mismo_ClientCommandId...` — razón Wolverine

## 4. Razón del fallo de cada test destrabado

Todos los 7 tests de `RechazarGenerarOTEndpointTests` recién destrabados fallan con:

```
System.InvalidOperationException : The server has not been started or no web application was configured.
   at Microsoft.AspNetCore.TestHost.TestServer.get_Application()
   at Microsoft.AspNetCore.TestHost.TestServer.CreateHandler()
   at Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1.CreateDefaultClient(...)
   at Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1.CreateClient()
```

Esta es exactamente la excepción esperada descrita en FU-32 y en la spec del fix. El bug raíz (`RunOaktonCommands(args)` consume el lifecycle del host antes de que `TestServer` pueda capturar el pipeline HTTP) está confirmado en estado rojo.

**Nota sobre tests con DocumentAlreadyExistsException:** varios tests de otros archivos fallan con `DocumentAlreadyExistsException` en la siembra de datos (antes de llegar a `factory.CreateClient()`). Esto es un artefacto de que el Postgres de Testcontainers conserva datos de corridas anteriores — los equipoIds están hardcodeados y colisionan. Este es un problema preexistente separado de FU-32 que el fix transversal no aborda. Tras el fix de Program.cs, estos tests pasarán a fallar por `InvalidOperationException` (o pasarán a verde si la siembra no colisiona en una corrida limpia). No es cambio de scope.

## 5. Archivos modificados

| Archivo | Tipo de cambio |
|---|---|
| `tests/Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs` | 7 `[Fact(Skip = SkipReasonFu32)]` → `[Fact]`; constante `SkipReasonFu32` eliminada; comentario de clase actualizado; skip Wolverine limpiado de referencia mixta FU-32 |
| `slices/fix-FU-32/spec.md` | Creado (diagnóstico + estrategia + inventario de tests) |
| `slices/fix-FU-32/red-notes.md` | Creado (este archivo) |

## 6. Scope extension 2026-05-11

Tras el fix del Program.cs, el rojo evolucionó. El usuario autorizó al orquestador ampliar
el scope para resolver los bloqueos transversales que aparecieron — todos plumbing de tests.

**Capa "fix Oakton lifecycle" (red original)**:
- 7 tests de `RechazarGenerarOTEndpointTests` destrabados con `--no-skip`.
- Antes del fix: 0/32 passing, todos `InvalidOperationException`.

**Capa "switch local Postgres" (ampliación)**:
- `InspeccionesAppFactory` recibe env var `POSTGRES_TEST_CONNSTRING`. Si está definida,
  conecta a Postgres local; si no, fallback a Testcontainers (CI / Docker).
- `EnsureDatabaseExistsAsync` crea `inspecciones_test` si no existe.
- `DropMartenSchemasAsync` limpia schema `inspecciones` + tablas `wolverine_%` entre corridas.

**Capa "config precedence" (ampliación)**:
- `UseSetting("ConnectionStrings:Postgres", ...)` se aplica además de `AddInMemoryCollection`
  porque `appsettings.Development.json` apunta a la DB de dev `inspecciones` (no `_test`)
  y la sobrescribía.

**Capa "EventLog noise" (ampliación)**:
- `ConfigureServices` elimina cualquier `ILoggerProvider` cuyo nombre contenga "EventLog".
- Wolverine intenta loguear errores de drain durante `DisposeAsync` al Windows EventLog;
  sin permisos lanza `SecurityException` que VSTest contabiliza como fallo de test.

**Capa "InvariantGlobalization" (ampliación)**:
- `Api.Tests.csproj` override `<InvariantGlobalization>false</InvariantGlobalization>`.
- `Directory.Build.props` tiene `true` (válido para producción).
- FluentAssertions enumera assemblies cargados al formatear el mensaje de un assert fallido;
  llama `Assembly.GetLocale()` que lanza `CultureNotFoundException` en invariant mode,
  enmascarando el assert real y mostrando solo el stack de FluentAssertions.

**Resultado final tras todo el fix consolidado**:
- `Inspecciones.Api.Tests`: **24/32 passing, 6 fail, 2 skip** (los 6 fallos son bugs
  preexistentes en handlers, no FU-32).
- `Inspecciones.Domain.Tests`: **197/197 passing**, sin regresión.

**Los 6 fallos remanentes (registrar como FU separados, no bloquean FU-32)**:

| Test | Falla | Diagnóstico inicial |
|---|---|---|
| `RegistrarHallazgo.happy_path` | 400 BadRequest en vez de 201 | endpoint requiere algo del request que el test no envía — probable validación de body o header |
| `RegistrarHallazgo.replay_ADR_008` | 400 BadRequest en vez de 201 | mismo bug que arriba |
| `GenerarOT.happy_path` | `SolicitadaEn` = `2026-05-11` (today) en vez de `2026-05-08` (test) | handler usa `DateTimeOffset.UtcNow` directo, viola regla CLAUDE.md "prohibido DateTime.UtcNow en dominio" — debe usar `TimeProvider` inyectado |
| `RechazarOT.happy_path` | `RechazadaEn` = today | mismo bug que arriba |
| `GenerarOT.sin_capability` | 500 InternalServerError en vez de 403 | endpoint no maneja capability ausente — null reference o exception |
| `RechazarOT.sin_capability` | 500 InternalServerError en vez de 403 | mismo bug que arriba |
