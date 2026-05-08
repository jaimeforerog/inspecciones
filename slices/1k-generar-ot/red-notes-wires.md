# Red notes — Slice 1k — GenerarOT (Wires: Handler + Endpoint HTTP)

**Autor:** red
**Fecha:** 2026-05-08
**Fase:** RED de wires (capa application + API)
**Spec consumida:** `slices/1k-generar-ot/spec.md` §9 (endpoint HTTP) + §4 (precondiciones por capa).

---

## 1. Tests escritos

### Proyecto: `tests/Inspecciones.Application.Tests` — `GenerarOTHandlerTests.cs`

| # | Nombre del test | Escenario spec | Razón del rojo |
|---|---|---|---|
| 1 | `GenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2` | §6.10 PRE-2 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 2 | `GenerarOT_sin_capability_generar_ot_lanza_CapabilityRequerida_PRE_1` | §6.3 PRE-1 | **Skip** — PRE-1 vive en capa HTTP |
| 3 | `GenerarOT_happy_path_handler_persiste_OTSolicitada_v1_y_retorna_resultado` | §6.1 happy path | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 4 | `GenerarOT_con_OT_ya_solicitada_en_stream_lanza_OTYaSolicitadaException_I_F4_c` | §6.6 PRE-5 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 5 | `GenerarOT_con_inspeccion_no_firmada_EnEjecucion_lanza_InspeccionNoFirmadaException_I_F4_a` | §6.4 PRE-3 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 6 | `GenerarOT_replay_mismo_clientCommandId_no_re_ejecuta_handler_ADR_008` | §6.9 idempotencia | **Skip** — requiere Wolverine envelope |
| 7 | `GenerarOT_sin_SaveChangesAsync_el_evento_no_se_persiste_atomicidad` | §6.14 atomicidad | `NotImplementedException` en `SolicitarOT` — pero método de dominio ya implementado; el test usa `SolicitarOT` directamente y verifica que el evento no persiste sin `SaveChangesAsync`. Cuando el handler esté implementado, este test pasará. |

**Total Application.Tests:** 5 activos (rojos por infrastructure preexistente) + 2 Skip = 7 tests.

### Proyecto: `tests/Inspecciones.Api.Tests` — `GenerarOTEndpointTests.cs`

| # | Nombre del test | Escenario spec | Razón del rojo |
|---|---|---|---|
| 1 | `POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto` | §6.1 happy path | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 2 | `POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1` | §6.3 PRE-1 | `NotImplementedException` en `GenerarOTHandler.Handle` (o handler no invocado — 403 via mock ya funcionaría, pero falla por el fixture) |
| 3 | `POST_generar_ot_inspeccion_inexistente_responde_404_Not_Found_PRE_2` | §6.10 PRE-2 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 4 | `POST_generar_ot_OT_ya_solicitada_responde_409_Conflict_I_F4_c` | §6.6 PRE-5 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 5 | `POST_generar_ot_inspeccion_no_firmada_responde_422_I_F4_ESTADO` | §6.4 PRE-3 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 6 | `POST_generar_ot_sin_hallazgos_RequiereIntervencion_responde_422_I_F4_SIN_INTERVENCION` | §6.5 PRE-4 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 7 | `POST_generar_ot_dictamen_PuedeOperar_responde_422_I_F4_DICTAMEN` | §6.8 PRE-7 | `NotImplementedException` en `GenerarOTHandler.Handle` |
| 8 | `POST_generar_ot_replay_mismo_ClientCommandId_no_duplica_OTSolicitada_v1_ADR_008` | §6.9 idempotencia | **Skip** — requiere Wolverine envelope |
| 9 | `POST_generar_ot_sin_header_X_Client_Command_Id_responde_400_Bad_Request` | §9 ADR-008 | `NotImplementedException` en `GenerarOTHandler.Handle` |

**Total Api.Tests:** 8 activos (rojos) + 1 Skip = 9 tests.

---

## 2. Verificación de estado rojo

### Tests de Application.Tests

```powershell
dotnet test tests/Inspecciones.Application.Tests/Inspecciones.Application.Tests.csproj `
  --filter "FullyQualifiedName~GenerarOT" `
  --no-build
```

**Resultado confirmado:**
```
Con error! - Con error: 12, Superado: 0, Omitido: 2, Total: 14
```

**Nota sobre el modo de fallo (preexistente):** los tests de `Application.Tests` fallan con
`Marten.Events.Projections.EventProjection.AssembleAndAssertValidity()` — el fixture
`PostgresFixture` no puede inicializar `InspeccionAbiertaPorEquipoProjection` porque `IQuerySession`
no es un parámetro soportado por `EventProjection` en esta versión de Marten. Este es el mismo fallo
que afecta a TODOS los tests de `Application.Tests` (verificado con `RegistrarHallazgoHandlerTests`
y `RegistrarMedicionHandlerTests`). Es un fallo de infraestructura de test preexistente, no causado
por este slice. Cuando el fixture sea corregido (tarea separada), los tests rojos se moverán a rojos
por `NotImplementedException` en `GenerarOTHandler.Handle`.

### Tests de Api.Tests

```powershell
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj `
  --filter "FullyQualifiedName~GenerarOT" `
  --no-build
```

**Resultado confirmado:**
```
Con error! - Con error: 8, Superado: 0, Omitido: 1, Total: 9
```

Los tests de `Api.Tests` también fallan por el mismo fallo de `EventProjection` en
`InspeccionesAppFactory` — misma infraestructura compartida. Todos los tests de
`RegistrarHallazgoEndpointTests` (preexistentes) también fallan (confirmado).

### Baseline de Domain.Tests (no afectado)

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
  --filter "FullyQualifiedName!~GenerarOT" `
  --no-build
```

**Resultado:** `Correctas! - Con error: 0, Superado: 167, Omitido: 6, Total: 173` — baseline intacto.

---

## 3. Código de producción tocado (stubs mínimos)

### Nuevos archivos en `src/`

| Archivo | Descripción |
|---|---|
| `src/Inspecciones.Application/Inspecciones/GenerarOTHandler.cs` | Stub del handler — `throw new NotImplementedException(...)` |
| `src/Inspecciones.Application/Inspecciones/GenerarOTResult.cs` | Record de resultado del handler |
| `src/Inspecciones.Api/Inspecciones/GenerarOTRequest.cs` | DTO de entrada del endpoint HTTP |

### Archivos modificados en `src/`

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Endpoint stub `POST /api/v1/inspecciones/{id}/generar-ot` — verifica `X-Client-Command-Id`, PRE-1 (capability via header de test), parsea enums, delega al handler stub, mapea excepciones a códigos HTTP spec §9. |
| `src/Inspecciones.Api/Program.cs` | Registro de `GenerarOTHandler` como Scoped. |

---

## 4. Desviaciones respecto a la spec

### Endpoint stub para PRE-1 (capability gate)

La spec §4 indica que PRE-1 (capability `generar-ot`) se valida en middleware de autorización antes
de invocar el handler. En el mock de claims del endpoint stub, la capability está hardcodeada como
presente (patrón de todos los endpoints — ADR-002 tentativo). Para que el test `§6.3 PRE-1` pueda
probar el 403, el endpoint stub acepta el header `X-Sin-Capability-Generar-OT: true` para simular
un claims sin la capability. Esta es la misma técnica de test que cabría usar hasta que ADR-002
se implemente con JWT reales.

### Fallo de infraestructura preexistente en Application.Tests y Api.Tests

Los tests de Application.Tests y Api.Tests fallan por un fallo de `EventProjection` en el fixture
compartido (`PostgresFixture` / `InspeccionesAppFactory`). Este fallo afecta a TODOS los tests de
estos proyectos (preexistente antes del slice 1k) y no es causado por este slice. Se documenta aquí
para que green no se confunda al correr la suite y encontrar fallos que no son suyos.

---

## 5. Hand-off a green

- Spec firmada: sí.
- Tests rojos compilando: sí (Application.Tests: 5 activos rojos; Api.Tests: 8 activos rojos).
- Razón de rojo: `NotImplementedException` en `GenerarOTHandler.Handle` (enmascarado por fallo de fixture preexistente, pero coherente con el patrón de todos los slices anteriores).
- Baseline Domain.Tests intacto: sí (167 verdes, 6 omitidos).

### Qué debe implementar green en `GenerarOTHandler.Handle`

1. Cargar aggregate: `AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`.
2. Si null → `throw InspeccionNoEncontradaException(...)`.
3. Obtener `ahora = _time.GetUtcNow()`.
4. Llamar `inspeccion.SolicitarOT(cmd, ahora)` — delega PRE-3..PRE-7 al aggregate.
5. `_session.Events.Append(cmd.InspeccionId, eventos.ToArray())`.
6. `await _session.SaveChangesAsync(ct)`.
7. Retornar `new GenerarOTResult(InspeccionId, SolicitadaEn, SolicitadaPor, Responsable.ToString(), Prioridad.ToString())`.

El endpoint en `InspeccionesEndpoints.cs` ya está implementado (no stub). El handler es el único pendiente.
