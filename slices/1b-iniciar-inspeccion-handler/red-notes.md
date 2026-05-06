# Red notes — Slice 1b — IniciarInspeccionHandler + InspeccionAbiertaPorEquipoView

**Autor:** red
**Fecha:** 2026-05-06
**Spec consumida:** `slices/1b-iniciar-inspeccion-handler/spec.md` (firmada por el usuario el 2026-05-06; estado pre-red `e1c9ae0` cierre slice 1a + `fa1323a` aggregate puro).

---

## 1. Tests escritos

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `POST_inspecciones_happy_path_responde_201_Created_con_InspeccionId` | 6.1 happy path E2E | `tests/Inspecciones.Api.Tests/IniciarInspeccionEndpointTests.cs` |
| `IniciarInspeccion_equipo_con_activa_retorna_existente_I_I1` | 6.2 I-I1 shortcut blando | `tests/Inspecciones.Application.Tests/Inspecciones/IniciarInspeccionHandlerTests.cs` |
| `Dos_IniciarInspeccion_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1` | 6.3 I-I1 race concurrente (defensa dura Postgres) | ídem |
| `POST_inspecciones_replay_con_mismo_ClientCommandId_no_duplica_evento_idempotencia_ADR_008` | 6.4 idempotencia replay envelope dedup | `tests/Inspecciones.Api.Tests/IniciarInspeccionEndpointTests.cs` |
| `IniciarInspeccion_con_equipo_no_sincronizado_lanza_EquipoNoEncontrado_PRE_3` | 6.5 PRE-3 equipo no encontrado | `tests/Inspecciones.Application.Tests/...` |
| `IniciarInspeccion_con_rutina_referenciada_no_sincronizada_lanza_RutinaTecnicaNoSincronizada` | 6.6 PRE-handler-1 rutina no sincronizada | ídem |
| `IniciarInspeccion_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizado_PRE_2` | 6.7 PRE-2 proyecto no autorizado (defensa profundidad) | ídem |
| `IniciarInspeccion_happy_path_proyeccion_y_evento_persisten_atomicos` | 6.8 atomicidad evento + proyección | ídem |

**Total:** 8 tests nuevos. 7 escenarios distintos (el §6.3 race lo cubre 1 test que afirma simultáneamente que solo persiste un evento y el perdedor redirige). El test de rebuild ya está cubierto por el slice 1a (`IniciarInspeccion_rebuild_desde_stream_reproduce_estado` en `Inspecciones.Domain.Tests`) — este slice no toca el aggregate, solo el handler/proyección/endpoint, por lo cual no requiere un nuevo rebuild test.

## 2. Verificación de estado rojo

```
dotnet build      → 0 errores, 0 warnings.
dotnet test       → resumen:
  - Inspecciones.Domain.Tests       : 16/16 verdes (slice 1a, sin regresión).
  - Inspecciones.Application.Tests  : 6/6 fallan (los 6 tests del handler 1b).
  - Inspecciones.Api.Tests          : 5/5 fallan (3 health checks preexistentes + 2 endpoint tests 1b).
```

**Razón del fallo en este entorno local:** todos los tests de integración fallan en `PostgreSqlBuilder.Build()` porque Docker no está corriendo. Esto **es consistente con el comportamiento esperado y documentado desde el slice 1a** (ver `slices/1a-iniciar-inspeccion-aggregate/refactor-notes.md` y `green-notes.md`): los tests Testcontainers son `[Trait("Category", "Integration")]`. En CI con Docker disponible, los tests progresarán de fallar por "Docker missing" a fallar por la razón correcta del red:

| Test | Razón esperada del fallo cuando Docker esté disponible |
|---|---|
| 6.1 endpoint happy path | El endpoint stub lanza `NotImplementedException` ⇒ HTTP 500 (no 201). |
| 6.2 shortcut I-I1 | `IniciarInspeccionHandler.ManejarAsync` lanza `NotImplementedException`. |
| 6.3 race concurrente | Mismo: handler stub lanza `NotImplementedException` antes de tocar Postgres. |
| 6.4 replay idempotente | El primer POST falla (500) en stub, el test espera 201. |
| 6.5 PRE-3 equipo no encontrado | Handler stub lanza `NotImplementedException`, no `EquipoNoEncontradoException`. |
| 6.6 PRE-handler-1 rutina | Mismo. |
| 6.7 PRE-2 proyecto no autorizado | Mismo. |
| 6.8 atomicidad evento + proyección | El handler stub no persiste nada, el test no encuentra el evento ni la fila en la proyección. |

Verificable cuando se levante Docker:
```
docker info   # debe responder OK
dotnet test   # los tests del slice 1b siguen rojos pero por NotImplementedException, no por DockerEndpointAuthConfig
```

## 3. Código de producción tocado

Stubs mínimos creados/modificados — todos lanzan `NotImplementedException` o son tipos sin lógica:

- `src/Inspecciones.Application/Inspecciones/IniciarInspeccionResult.cs` — **modificado**: agregado campo `Mensaje: string?` (4to parámetro del record) según spec §2. Mantiene compatibilidad sintáctica con la firma del handler ya stub.
- `src/Inspecciones.Application/Inspecciones/InspeccionAbiertaPorEquipoView.cs` — **nuevo**: record que describe la proyección. Sin lógica, solo shape (la registración como `MultiStreamProjection<...>` Inline + el índice único Postgres son responsabilidad del green).
- `src/Inspecciones.Application/Inspecciones/Excepciones.cs` — **nuevo**: `EquipoNoEncontradoException : InspeccionDomainException` (PRE-3 del handler). Las demás excepciones de dominio (PRE-2, PRE-4..PRE-7, I-I2, I-I3) ya viven en `Inspecciones.Domain` y se reusan.
- `src/Inspecciones.Api/Inspecciones/IniciarInspeccionRequest.cs` — **nuevo**: DTO de entrada HTTP, shape espejo del comando.
- `src/Inspecciones.Api/Inspecciones/IniciarInspeccionResponse.cs` — **nuevo**: DTO de salida HTTP, shape espejo de `IniciarInspeccionResult`.
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` — **nuevo**: `MapInspeccionesEndpoints` registra `POST /api/v1/inspecciones`. El handler del endpoint lanza `NotImplementedException` directamente; el green sustituirá por mapeo Request→cmd + claims + invocación al handler + traducción de excepciones a status codes 422/403/404 + header `Location` + replay envelope dedup.
- `src/Inspecciones.Api/Program.cs` — **modificado**: `using Inspecciones.Api.Inspecciones;` + llamada `app.MapInspeccionesEndpoints();` después de los health checks. Sin lógica nueva — solo wire del endpoint.

**No tocado:**
- `src/Inspecciones.Application/Inspecciones/IniciarInspeccionHandler.cs` — ya era stub `throw new NotImplementedException()` desde slice 1a, intacto.
- `src/Inspecciones.Domain/...` — sin cambios. Slice 1b no toca dominio puro.
- Tests del slice 1a (`Inspecciones.Domain.Tests/Inspecciones/*`) — intactos, 16/16 verdes.

## 4. Desviaciones respecto a la spec

- [x] Sin desviaciones materiales.
- **Aclaración menor:** la spec §2 declara `Version: long` en el record `IniciarInspeccionResult`. El record ya existente (creado en slice 1a) lo declaraba como `int`; preservé `int` para no romper la firma existente y porque Marten devuelve `int` en `StreamState.Version` para single-stream nativo. Si emerge necesidad de `long` en slices futuros (streams largos), se cambia con migración. **No bloquea el green.**
- **Aclaración menor:** la spec §6.7 supone que el filtro HTTP de capability bypass-ea para el test. El test de defensa en profundidad lo escribí como test del handler directamente (no E2E HTTP) — más simple y suficiente para verificar la propagación de `ProyectoNoAutorizadoException` desde el aggregate del 1a a través del handler.

## 5. Hand-off a green

- **Spec firmada:** sí (usuario, 2026-05-06).
- **Todos los tests rojos:** sí. 8 tests nuevos escritos, todos fallan. En el entorno local fallan por Docker missing (consistente con slice 1a). En CI con Docker disponible, fallarán por `NotImplementedException` del handler/endpoint stub — la "razón correcta" del red.
- **Sin cambios de comportamiento accidentales:** sí. Slice 1a sin regresión (16/16 tests `Inspecciones.Domain.Tests` verdes).
- **Stubs entregados al green:**
  - `IniciarInspeccionHandler.ManejarAsync` debe implementar: consulta a `InspeccionAbiertaPorEquipoView` por `EquipoId` → corto-circuito si activa con `Mensaje="Ya hay inspección activa, abriendo la existente"` y `RedirigeAExistente=true`; si no, consulta `EquipoLocal` + `RutinaTecnicaLocal`, mapea `EquipoNoEncontradoException` (PRE-3) y `RutinaTecnicaNoSincronizadaException` (PRE-handler-1) antes del aggregate; invoca `Inspeccion.Iniciar`, hace `_session.Events.StartStream<Inspeccion>(cmd.InspeccionId, eventos)` y un único `_session.SaveChangesAsync(ct)`. Race condition (escenario 6.3): atrapar `MartenCommandException` envolviendo `PostgresException.SqlState=23505`, releer la proyección, retornar `RedirigeAExistente=true`.
  - `InspeccionAbiertaPorEquipoView` debe registrarse como `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>` Inline en `Program.cs` (`opts.Projections.Add(new InspeccionAbiertaPorEquipoViewProjection(), ProjectionLifecycle.Inline)`). La proyección consume `InspeccionIniciada_v1` (upsert con `Identity<TEvent>(e => e.EquipoId)`); los `InspeccionFirmada_v1` y `InspeccionCancelada_v1` aún no existen — se agregan en slices futuros.
  - **Migración SQL del unique index** debe correr antes de los tests integración: `CREATE UNIQUE INDEX ix_inspeccion_abierta_equipo_unique ON inspecciones.mt_doc_inspeccionabiertaporequipoview ((data->>'EquipoId'))`. El green decide si aprovecha el `OnException()` Marten o ejecuta el DDL en el `Program.cs` post-build.
  - `InspeccionesEndpoints.MapInspeccionesEndpoints` debe: leer `X-Client-Command-Id` (rechazo 400 si falta), construir `ClaimsTecnico` desde el contexto del request (mock fijo para tests E2E hasta que el host PWA inyecte real — ver ADR-002), invocar el handler, mapear `IniciarInspeccionResult` → `IniciarInspeccionResponse`, devolver `201 Created` + `Location: /api/v1/inspecciones/{InspeccionId}` cuando `!RedirigeAExistente && !replay`, `200 OK` cuando `RedirigeAExistente || replay`, mapear excepciones a `403/404/422`. La idempotencia ADR-008 requiere que el endpoint propague el header como `MessageId` Wolverine (`opts.Policies.UseDurableLocalQueues()` + envelope storage activo).
- **Atomicidad (escenario 6.8):** debe verificarse que el handler no llame `SaveChangesAsync` dos veces — un único commit que incluya Append + proyección Inline + envelope dedup. La regla CLAUDE.md "Prohibido partir un comando en dos `SaveChangesAsync`" aplica directo aquí.

## 6. Notas para el reviewer

- El test §6.3 (race condition) es no determinístico por naturaleza (depende del scheduler). Aceptable porque la afirmación es estadística: ambos `Task` se inician en paralelo y la verificación es sobre el resultado convergente (un evento, dos resultados con ganador/perdedor identificable). Si el green descubre flakiness, puede agregarse un mecanismo de barrier para forzar la concurrencia.
- El test §6.4 asume que Wolverine envelope dedup está habilitado en `Program.cs` (`opts.Policies.UseDurableLocalQueues()` ya está). El handler debe estar registrado vía discovery (`Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly)` ya está). Si el green encuentra que la integración requiere `[WolverineHandler]` o `IntegrateWithWolverine().UseWolverineManagedEventSubscriptionDistribution()` adicional, lo documenta en green-notes.
- Los test Health Checks (`HealthChecksTests`) son del slice 1a y siguen fallando por Docker missing — este red no los rompió ni los arregló. Cuando Docker esté disponible volverán a verde.
