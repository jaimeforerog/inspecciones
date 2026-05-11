# Fix FU-32 — RunOaktonCommands bloquea WebApplicationFactory

> **Scope extension 2026-05-11** (decisión orquestador con autorización del usuario):
> el alcance original (`Program.cs` Oakton lifecycle) resolvió la `InvalidOperationException`
> pero al destrabar los tests E2E afloraron tres bloqueos independientes del mismo síntoma
> "Api.Tests rotos preexistentes" — todos plumbing de la fixture, no código de dominio.
> El fix consolidado incluye:
>
> 1. **Switch local Postgres / Testcontainers** en `InspeccionesAppFactory` controlado por
>    env var `POSTGRES_TEST_CONNSTRING` (desbloquea dev offline / sin Docker).
> 2. **Override de connection string vía `UseSetting`** para que `appsettings.Development.json`
>    (que apunta a la DB dev `inspecciones`) no sobrescriba la DB de tests `inspecciones_test`.
> 3. **Eliminación del `EventLogLoggerProvider`** del host de test — Wolverine intenta loguear
>    al Windows EventLog en `DisposeAsync` y falla por permisos, contaminando los resultados.
> 4. **Override `InvariantGlobalization=false`** en `Api.Tests.csproj` — FluentAssertions
>    enumera assemblies al formatear mensajes de fallo, lo cual choca con el invariant mode
>    activado a nivel `Directory.Build.props` y enmascaraba el assert real.
> 5. **DROP SCHEMA + tablas wolverine_*** entre corridas en modo local — aislamiento.
> 6. **`xunit.runner.json` con `maxParallelThreads: 1`** — defensa en profundidad por si
>    futuros tests amplían el set de seeds compartidos.
>
> El fix no toca código de dominio. Es 100% plumbing de tests (rol infra-wire). Los 6 tests
> que aún fallan tras el fix son bugs reales preexistentes en handlers (datetime hardcodeado,
> capability handling) — registrados como FU separados, NO bloquean el cierre de FU-32.

## Diagnóstico

`Program.cs:136` termina con `await app.RunOaktonCommands(args);` como única rama de arranque. Oakton captura el lifecycle del host cuando `args` está vacío (caso test): devuelve el control solo después de cerrar el proceso, antes de que el pipeline HTTP escuche. `WebApplicationFactory<Program>` necesita que el entry point solo configure el `IHostBuilder` y arranque a través del `IHostBuilder` interno; cuando el factory sustituye el servidor por `TestServer`, el pipeline HTTP nunca se construye. Resultado: `TestServer.get_Application()` lanza `InvalidOperationException: "The server has not been started or no web application was configured."` en cualquier test que llame `factory.CreateClient()`.

## Estrategia del fix

Condicionar el modo de arranque a si existen argumentos CLI reales:

```csharp
if (args.Length > 0)
    await app.RunOaktonCommands(args);   // modo CLI — Oakton toma el control
else
    await app.RunAsync();                // modo servidor — WebApplicationFactory puede interceptar
```

Cuando `dotnet run` arranca la app real (sin args Oakton), `args` es vacío y el servidor arranca con `RunAsync()`. Cuando se invoca con `-- check-env` u otro subcomando Oakton, el flujo CLI sigue intacto. `WebApplicationFactory` nunca pasa args, por lo que siempre entra por `RunAsync()`, y el `TestServer` puede capturar el pipeline correctamente.

## Tests que deben pasar a verde tras el fix

### RechazarGenerarOTEndpointTests.cs — 7 tests destrabados de skip FU-32

| Escenario | Método |
|---|---|
| §6.1 Happy path 200 OK | `POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto` |
| §6.3 PRE-1 capability ausente 403 | `POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1` |
| §6.12 PRE-2 inexistente 404 | `POST_rechazar_generar_ot_inspeccion_inexistente_responde_404_Not_Found_PRE_2` |
| §6.4 PRE-3 motivo corto 422 | `POST_rechazar_generar_ot_motivo_corto_responde_422_I_F6_MOTIVO` |
| §6.6 PRE-4 no firmada 422 | `POST_rechazar_generar_ot_inspeccion_no_firmada_responde_422_I_F6_ESTADO` |
| §6.10 PRE-6 OT ya solicitada 409 | `POST_rechazar_generar_ot_OT_ya_solicitada_responde_409_Conflict_I_F6_OT_YA_SOLICITADA` |
| Header ausente 400 | `POST_rechazar_generar_ot_sin_header_X_Client_Command_Id_responde_400_Bad_Request` |

Queda en skip (razón Wolverine, no FU-32): `POST_rechazar_generar_ot_replay_mismo_ClientCommandId_no_duplica_eventos_ADR_008`

### HealthChecksTests.cs — 3 tests (ya sin skip, actualmente fallan por FU-32)

- `GET_health_live_responde_200`
- `GET_health_ready_responde_200`
- `GET_root_responde_200_con_metadata`

### Otros archivos — sin skip FU-32, pero fallan por el mismo bug

- `IniciarInspeccionEndpointTests.cs` — todos sus tests activos
- `RegistrarHallazgoEndpointTests.cs` — todos sus tests activos
- `EliminarHallazgoEndpointTests.cs` — todos sus tests activos
- `AsignarRepuestoEndpointTests.cs` — todos sus tests activos
- `GenerarOTEndpointTests.cs` — 7 tests activos (1 skip por Wolverine)
