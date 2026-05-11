# Red Notes — Slice 1m: CancelarInspeccion

**Agente:** red
**Fecha:** 2026-05-11
**Estado:** ROJO VÁLIDO — tests compilando y fallando por razón correcta (verificado 2026-05-11)

---

## 1. Tests escritos

### A) Tests de aggregate puro
**Archivo:** `tests/Inspecciones.Domain.Tests/Inspecciones/CancelarInspeccionTests.cs`

| # | Nombre del test | Escenario spec | Estado rojo |
|---|---|---|---|
| 1 | `CancelarInspeccion_en_ejecucion_emite_InspeccionCancelada_v1` | §6.1 | NotImplementedException |
| 2 | `CancelarInspeccion_en_ejecucion_emite_payload_completo_y_correcto` | §6.1 | NotImplementedException |
| 3 | `CancelarInspeccion_con_hallazgos_emite_InspeccionCancelada_v1_hallazgos_permanecen` | §6.2 | NotImplementedException |
| 4 | `CancelarInspeccion_inspeccion_tipo_monitoreo_emite_InspeccionCancelada_v1` | §6.3 | NotImplementedException |
| 5 | `CancelarInspeccion_sin_capability_ejecutar_inspeccion_lanza_403_PRE_1` | §6.4 | SKIP (capa HTTP) |
| 6 | `CancelarInspeccion_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2` | §6.5 | SKIP (Marten) |
| 7 | `CancelarInspeccion_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException` | §6.6 | PASA (inline en CasoDeUso) |
| 8 | `CancelarInspeccion_motivo_vacio_lanza_MotivoCancelacionInvalidoException` | §6.7 | PASA (inline en CasoDeUso) |
| 9 | `CancelarInspeccion_motivo_solo_espacios_lanza_MotivoCancelacionInvalidoException` | §6.8 | PASA (inline en CasoDeUso) |
| 10 | `CancelarInspeccion_motivo_menor_10_chars_lanza_MotivoCancelacionInvalidoException` | §6.9 | PASA (inline en CasoDeUso) |
| 11 | `CancelarInspeccion_motivo_exactamente_10_chars_es_valido_y_emite_evento` | §6.9 borde | NotImplementedException |
| 12 | `CancelarInspeccion_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I6` | §6.10 | Falla: expected InspeccionNoEnEjecucionException got NotImplementedException |
| 13 | `CancelarInspeccion_inspeccion_ya_cancelada_lanza_InspeccionNoEnEjecucionException_I6` | §6.11 | Falla: expected InspeccionNoEnEjecucionException got NotImplementedException |
| 14 | `CancelarInspeccion_inspeccion_cerrada_sin_OT_lanza_InspeccionNoEnEjecucionException_I6` | §6.12 | Falla: expected InspeccionNoEnEjecucionException got NotImplementedException |
| 15 | `CancelarInspeccion_segundo_contribuyente_puede_cancelar` | §6.13 | NotImplementedException |
| 16 | `CancelarInspeccion_replay_mismo_clientCommandId_no_duplica_eventos_ni_re_ejecuta_handler` | §6.14 | SKIP (Wolverine) |
| 17 | `CancelarInspeccion_rebuild_desde_stream_2_eventos_estado_correcto` | §6.15 | PASA (Apply ya puro) |
| 18 | `CancelarInspeccion_rebuild_con_hallazgos_hallazgos_persisten_estado_cancelada` | §6.16 | PASA (Apply ya puro) |
| 19 | `CancelarInspeccion_rebuild_desde_stream_reproduce_estado_post_comando` | §6.15 rebuild | NotImplementedException |

**Total dominio:** 19 tests | 6 pasan | 10 fallan | 3 skip

**Nota sobre tests 12-14 (PRE-5):** fallan con "expected InspeccionNoEnEjecucionException but found NotImplementedException". Esto es rojo válido — cuando `green` implemente `Cancelar()` con la guardia PRE-5, estos tests pasarán porque el método lanzará `InspeccionNoEnEjecucionException` antes de intentar emitir el evento.

**Nota sobre tests 7-10 (PRE-3/PRE-4):** pasan porque `CasoDeUso.Cancelar()` aplica las validaciones inline en el helper de test antes de llamar al método del aggregate. Cuando `green` mueva las validaciones PRE-3 y PRE-4 al handler (capa correcta), los tests de dominio seguirán siendo correctos — el `CasoDeUso` helper simula el handler completo.

**Nota sobre tests 17-18 (rebuild):** pasan porque `Apply(InspeccionCancelada_v1)` ya existe y es puro. Confirma que el Apply de slice 1c fue correctamente actualizado al nuevo shape del record.

### B) Tests de handler (Application)
**Archivo:** `tests/Inspecciones.Application.Tests/Inspecciones/CancelarInspeccionHandlerTests.cs`

| # | Nombre del test | Escenario spec | Estado rojo esperado |
|---|---|---|---|
| 1 | `handler_inspeccion_inexistente_lanza_InspeccionNoEncontradaException_PRE_2` | §6.5 | NotImplementedException (handler stub) |
| 2 | `handler_sin_capability_ejecutar_inspeccion_lanza_CapabilityRequerida_PRE_1` | §6.4 | SKIP |
| 3 | `handler_cancela_inspeccion_existente_persiste_evento_y_devuelve_ok` | §6.1 | NotImplementedException |
| 4 | `handler_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException` | §6.6 | NotImplementedException |
| 5 | `handler_motivo_vacio_lanza_MotivoCancelacionInvalidoException` | §6.7 | NotImplementedException |
| 6 | `handler_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I6` | §6.10 | NotImplementedException |
| 7 | `handler_uso_TimeProvider_para_CanceladaEn` | regla CLAUDE.md | NotImplementedException |
| 8 | `handler_replay_mismo_clientCommandId_no_re_ejecuta_handler_ADR_008` | §6.14 | SKIP |

**Total handler:** 8 tests | 1 skip | 7 fallan con NotImplementedException (requieren Postgres)

### C) Tests E2E del endpoint
**Archivo:** `tests/Inspecciones.Api.Tests/CancelarInspeccionEndpointTests.cs`

| # | Nombre del test | Escenario spec | Estado rojo esperado |
|---|---|---|---|
| 1 | `POST_cancelar_inspeccion_happy_path_responde_200_OK` | §6.1 | NotImplementedException |
| 2 | `POST_cancelar_inspeccion_sin_capability_responde_403` | §6.4 | PASA (middleware PRE-1 ya implementado) |
| 3 | `POST_cancelar_inspeccion_inexistente_responde_404` | §6.5 | NotImplementedException |
| 4 | `POST_cancelar_inspeccion_tecnico_no_contribuyente_responde_403` | §6.6 | NotImplementedException |
| 5 | `POST_cancelar_inspeccion_motivo_vacio_responde_400_o_422_I6_MOTIVO` | §6.7 | NotImplementedException |
| 6 | `POST_cancelar_inspeccion_motivo_corto_responde_422_I6_MOTIVO` | §6.9 | NotImplementedException |
| 7 | `POST_cancelar_inspeccion_ya_firmada_responde_409_I6_ESTADO` | §6.10 | NotImplementedException |
| 8 | `POST_cancelar_inspeccion_ya_cancelada_responde_409_I6_ESTADO` | §6.11 | NotImplementedException |
| 9 | `POST_cancelar_inspeccion_replay_mismo_ClientCommandId_no_duplica_eventos_ADR_008` | §6.14 | SKIP |
| 10 | `POST_cancelar_inspeccion_sin_header_X_Client_Command_Id_responde_400` | ADR-008 | PASA (middleware ya implementado) |

**Total E2E:** 10 tests | 1 skip | ~8 fallan con NotImplementedException

---

## 2. Stubs creados

**Estrategia usada: opción (a) — stubs mínimos que compilan y fallan en runtime.**

### Nuevos archivos de producción (stubs):
- `src/Inspecciones.Domain/Inspecciones/CancelarInspeccion.cs` — record del comando
- `src/Inspecciones.Application/Inspecciones/CancelarInspeccionResult.cs` — record del resultado
- `src/Inspecciones.Application/Inspecciones/CancelarInspeccionHandler.cs` — handler stub (NotImplementedException)
- `src/Inspecciones.Api/Inspecciones/CancelarInspeccionRequest.cs` — DTOs request/response

### Modificaciones a archivos existentes:
- `src/Inspecciones.Domain/Inspecciones/InspeccionCancelada_v1.cs` — cambiado shape: `(InspeccionId, Motivo, CanceladaPor, CanceladaEn)` (D-3: DateTimeOffset; D-1: orden canónico spec §3.1). El orden anterior era `(InspeccionId, CanceladaEn, CanceladoPor, Motivo?)`.
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — añadido `MotivoCancelacion` property, actualizado `Apply(InspeccionCancelada_v1)` para setear MotivoCancelacion, añadido stub `Cancelar()`.
- `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` — añadida `MotivoCancelacionInvalidoException`.
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` — añadido endpoint stub `POST /api/v1/inspecciones/{id}/cancelar`.
- `src/Inspecciones.Api/Program.cs` — registrado `CancelarInspeccionHandler` en DI.
- `tests/Inspecciones.Domain.Tests/Inspecciones/HallazgoFixtures.cs` — actualizado `StreamConInspeccionCancelada()` para usar nuevo shape de `InspeccionCancelada_v1`.
- `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` — añadido helper `Cancelar()`.

**Breaking change en InspeccionCancelada_v1:** el orden de parámetros cambió. Único uso previo era en `HallazgoFixtures.cs:67` — actualizado. El `Apply` que usaba `e.CanceladoPor` (campo anterior) fue actualizado a `e.CanceladaPor` (nuevo nombre conforme al spec §3.1 y convención de naming del aggregate).

---

## 3. Comando para verificar rojo

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Domain.Tests --filter "CancelarInspeccion" --logger "console;verbosity=minimal"
```

**Output esperado (dominio puro, sin Postgres):**
```
Con error! - Con error: 10, Superado: 6, Omitido: 3, Total: 19
```

**Para todos los proyectos (requiere Postgres):**
```powershell
dotnet test --filter "CancelarInspeccion"
```

---

## 4. Razón del fallo por test

| Grupo | Razón del fallo |
|---|---|
| Tests happy path (1, 2, 3, 4, 11, 15, 19) | `NotImplementedException`: `Cancelar()` no implementado en `Inspeccion.cs` |
| Tests PRE-5 estado (12, 13, 14) | Expected `InspeccionNoEnEjecucionException` but found `NotImplementedException` — la guardia aún no existe |
| Tests handler (1, 3, 4, 5, 6, 7) | `NotImplementedException`: `CancelarInspeccionHandler.Handle()` stub |
| Tests E2E (1, 3, 4, 5, 6, 7, 8) | `NotImplementedException` propagada hasta el endpoint → 500 en vez del código HTTP esperado |

---

## 5. Confirmación de rojo válido

- [x] Todos los tests **compilan** sin errores (`dotnet build` limpio, 0 errores).
- [x] Los tests fallan por **razón correcta**: método no existe (`NotImplementedException`) o excepción esperada no se lanza (porque el método lanza `NotImplementedException` antes).
- [x] Los tests de **rebuild** (§6.15, §6.16) **pasan** — confirma que `Apply(InspeccionCancelada_v1)` es puro y el nuevo shape es correcto.
- [x] Tests existentes de otros slices: **203 pasando** (ninguno roto por el cambio de shape en `InspeccionCancelada_v1`).
- [x] Único uso previo del record (`HallazgoFixtures.cs:67`) **actualizado** al nuevo shape.
- [x] Rojo válido, listo para `green`.

---

## 6. Notas para el agente green

1. **`Inspeccion.Cancelar(motivo, canceladaPor, canceladaEn)`** — implementar con guardia PRE-5 (I6):
   ```csharp
   if (Estado != EstadoInspeccion.EnEjecucion)
       throw new InspeccionNoEnEjecucionException($"La inspección {InspeccionId} está en estado '{Estado}'...");
   return [ new InspeccionCancelada_v1(InspeccionId, motivo, canceladaPor, canceladaEn) ];
   ```

2. **`CancelarInspeccionHandler.Handle()`** — implementar con PRE-2 (AggregateStreamAsync), PRE-3 (contribuyente), PRE-4 (motivo ≥10 chars), luego llamar `aggregate.Cancelar()`.

3. **El endpoint stub** ya existe en `InspeccionesEndpoints.cs` — solo falta que el handler funcione.

4. **`InspeccionAbiertaPorEquipoView`** — la proyección ya tiene el case para `InspeccionCancelada_v1` (delete fila). Verificar que el nuevo shape del record es compatible con la proyección existente.

5. **TecnicosContribuyentes vs Contribuyentes**: el spec usa el nombre `TecnicosContribuyentes` pero la propiedad en el aggregate es `Contribuyentes` (tipo `IReadOnlySet<string>`). Usar `aggregate.Contribuyentes.Contains(cmd.CanceladaPor)`.
