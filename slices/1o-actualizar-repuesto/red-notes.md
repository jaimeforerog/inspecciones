# red-notes — Slice 1o ActualizarRepuesto

**Agente:** red
**Fecha:** 2026-05-11
**Estado:** rojo validado

---

## 1. Tests escritos por capa

### Nivel 1 — Domain (21 tests)

Archivo: `tests/Inspecciones.Domain.Tests/Inspecciones/ActualizarRepuestoTests.cs`

| # | Nombre del test | Escenario spec | Estado rojo |
|---|---|---|---|
| 1 | `ActualizarRepuesto_solo_cantidad_emite_RepuestoActualizado_v1_con_delta_cantidad` | §6.1 | FAIL — NotImplementedException |
| 2 | `ActualizarRepuesto_solo_cantidad_preserva_justificacion_anterior_en_aggregate` | §6.1 | FAIL — NotImplementedException |
| 3 | `ActualizarRepuesto_solo_observacion_emite_RepuestoActualizado_v1_con_delta_justificacion` | §6.2 | FAIL — NotImplementedException |
| 4 | `ActualizarRepuesto_solo_observacion_preserva_cantidad_anterior_en_aggregate` | §6.2 | FAIL — NotImplementedException |
| 5 | `ActualizarRepuesto_ambos_campos_emite_RepuestoActualizado_v1_con_delta_completo` | §6.3 | FAIL — NotImplementedException |
| 6 | `ActualizarRepuesto_ambos_campos_agrega_tecnico_a_contribuyentes` | §6.3 | FAIL — NotImplementedException |
| 7 | `ActualizarRepuesto_segunda_actualizacion_emite_segundo_evento_trazabilidad` | §6.4 | FAIL — NotImplementedException |
| 8 | `ActualizarRepuesto_segunda_actualizacion_combina_ambos_deltas_en_aggregate` | §6.4 | FAIL — NotImplementedException |
| 9 | `ActualizarRepuesto_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7` | §6.5 | FAIL — NotImplementedException |
| 10 | `ActualizarRepuesto_en_inspeccion_Cancelada_lanza_InspeccionNoEnEjecucionException_I_H7` | §6.5 (variante) | FAIL — NotImplementedException |
| 11 | `ActualizarRepuesto_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE3` | §6.6 | FAIL — NotImplementedException |
| 12 | `ActualizarRepuesto_con_hallazgo_eliminado_lanza_HallazgoEliminadoException_PRE4` | §6.7 | FAIL — NotImplementedException |
| 13 | `ActualizarRepuesto_con_RepuestoId_inexistente_lanza_RepuestoNoEncontradoException_PRE5` | §6.8 | FAIL — NotImplementedException |
| 14 | `ActualizarRepuesto_con_RepuestoId_en_hallazgo_incorrecto_lanza_RepuestoNoEncontradoException_PRE5` | §6.9 | FAIL — NotImplementedException |
| 15 | `ActualizarRepuesto_con_CantidadNueva_cero_lanza_CantidadInvalidaException_PRE7` | §6.10 | FAIL — NotImplementedException |
| 16 | `ActualizarRepuesto_con_CantidadNueva_negativa_lanza_CantidadInvalidaException_PRE7` | §6.10 (variante) | FAIL — NotImplementedException |
| 17 | `ActualizarRepuesto_sin_campos_patcheables_lanza_ComandoSinCambiosException_PRE8` | §6.11 | FAIL — NotImplementedException |
| 18 | `ActualizarRepuesto_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE1` | §6.12 | SKIP — PRE-1 vive en handler |
| 19 | `ActualizarRepuesto_no_modifica_campos_inmutables_SkuId_Unidad_HallazgoId_INV_RA1` | §6.13 | FAIL — NotImplementedException |
| 20 | `ActualizarRepuesto_rebuild_desde_stream_reproduce_estado` | §6.14 | **PASS** — Apply puro ya operativo |
| 21 | `ActualizarRepuesto_rebuild_desde_stream_completo_previos_mas_emitidos` | §6.14 | FAIL — NotImplementedException (llama CasoDeUso) |

**Resumen Domain:** 19 FAIL + 1 PASS (rebuild directo) + 1 SKIP

### Nivel 2 — Application (3 tests)

Archivo: `tests/Inspecciones.Application.Tests/Inspecciones/ActualizarRepuestoHandlerTests.cs`

| # | Nombre del test | Escenario spec | Estado rojo |
|---|---|---|---|
| 1 | `ActualizarRepuesto_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE1` | §6.12 | FAIL — NotImplementedException |
| 2 | `ActualizarRepuesto_handler_happy_path_persiste_evento_y_devuelve_resultado` | §6.1 handler | FAIL — NotImplementedException |
| 3 | `ActualizarRepuesto_handler_persiste_evento_en_stream_de_Marten` | §6.1 handler | FAIL — NotImplementedException |
| 4 | `ActualizarRepuesto_observacion_vacia_se_normaliza_a_null_P2` | P-2 normalización | FAIL — NotImplementedException |

**Resumen Application:** 4 FAIL (todos por NotImplementedException en handler)

### Nivel 3 — API (12 tests)

Archivo: `tests/Inspecciones.Api.Tests/ActualizarRepuestoEndpointTests.cs`

| # | Nombre del test | Escenario spec | Estado rojo |
|---|---|---|---|
| 1 | `PATCH_repuesto_sin_header_ClientCommandId_responde_400_HEADER_REQUERIDO` | ADR-008 header | FAIL — NotImplementedException (handler) |
| 2 | `PATCH_repuesto_sin_capability_ejecutar_inspeccion_responde_403_PRE0` | PRE-0 | FAIL (capability gate en endpoint, pasa; handler falla) |
| 3 | `PATCH_repuesto_happy_path_solo_cantidad_responde_200_OK` | §6.1 | FAIL — NotImplementedException |
| 4 | `PATCH_repuesto_happy_path_ambos_campos_responde_200_OK` | §6.3 | FAIL — NotImplementedException |
| 5 | `PATCH_repuesto_inspeccion_firmada_responde_422_I_H7` | §6.5 | FAIL — NotImplementedException |
| 6 | `PATCH_repuesto_hallazgo_inexistente_responde_404_PRE3` | §6.6 | FAIL — NotImplementedException |
| 7 | `PATCH_repuesto_hallazgo_eliminado_responde_422_PRE4_ELIMINADO` | §6.7 | FAIL — NotImplementedException |
| 8 | `PATCH_repuesto_repuesto_inexistente_responde_404_PRE5` | §6.8 | FAIL — NotImplementedException |
| 9 | `PATCH_repuesto_cantidad_cero_responde_422_PRE7` | §6.10 | FAIL — NotImplementedException |
| 10 | `PATCH_repuesto_ambos_campos_null_responde_400_PRE8` | §6.11 | FAIL — NotImplementedException |
| 11 | `PATCH_repuesto_inspeccion_inexistente_responde_404_PRE1` | §6.12 | FAIL — NotImplementedException |
| 12 | `PATCH_repuesto_retry_con_mismo_ClientCommandId_no_emite_segundo_evento_ADR008` | ADR-008 idempotencia | SKIP — followup #15 |

**Resumen API:** 11 FAIL + 1 SKIP

---

## 2. Comando para verificar estado rojo

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj --filter "FullyQualifiedName~ActualizarRepuesto"
```

Solo domain tests (sin Postgres):
```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --filter "FullyQualifiedName~ActualizarRepuesto"
```

---

## 3. Razón del fallo por test

**Tests de decisión (domain):** todos fallan con `System.NotImplementedException: Slice 1o — ActualizarRepuesto pendiente de implementación por el agente green.` desde `Inspeccion.ActualizarRepuesto(ActualizarRepuesto cmd, DateTimeOffset ahora)`.

**Rebuild directo §6.14 (PASA):** `Inspeccion.Reconstruir(stream)` con 4 eventos pre-construidos. `Apply(RepuestoActualizado_v1)` ya es puro y operativo. Confirma que el Apply aplica el delta correctamente y no tiene validaciones intrusas.

**Tests Application:** fallan con `NotImplementedException` desde `ActualizarRepuestoHandler.Handle`.

**Tests API:** todos fallan con `NotImplementedException` propagada desde el handler stub.

---

## 4. Archivos nuevos/modificados

### Producción (stubs)
- `src/Inspecciones.Domain/Inspecciones/ActualizarRepuesto.cs` — record comando
- `src/Inspecciones.Domain/Inspecciones/RepuestoActualizado_v1.cs` — record evento
- `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` — `RepuestoNoEncontradoException` + `ComandoSinCambiosException`
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — método `ActualizarRepuesto` (stub NotImplementedException) + `Apply(RepuestoActualizado_v1)` (operativo) + entrada en `AplicarEvento` switch
- `src/Inspecciones.Application/Inspecciones/ActualizarRepuestoHandler.cs` — handler stub + `ActualizarRepuestoResult`
- `src/Inspecciones.Api/Inspecciones/ActualizarRepuestoRequest.cs` — DTO request
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` — endpoint PATCH con capability gate y mapeo de errores
- `src/Inspecciones.Api/Program.cs` — registro DI de `ActualizarRepuestoHandler`

### Tests
- `tests/Inspecciones.Domain.Tests/Inspecciones/ActualizarRepuestoTests.cs` — 21 tests (19 FAIL + 1 PASS + 1 SKIP)
- `tests/Inspecciones.Domain.Tests/Inspecciones/ActualizarRepuestoFixtures.cs` — fixtures reusables
- `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` — método `ActualizarRepuesto` añadido
- `tests/Inspecciones.Application.Tests/Inspecciones/ActualizarRepuestoHandlerTests.cs` — 4 tests (4 FAIL)
- `tests/Inspecciones.Api.Tests/ActualizarRepuestoEndpointTests.cs` — 12 tests (11 FAIL + 1 SKIP)

---

## 5. Invariantes cubiertas

| Invariante | Test(s) |
|---|---|
| I-H7 (editable solo EnEjecucion) | §6.5 tests 9 y 10 |
| INV-RA1 (SkuId/Unidad/HallazgoId inmutables) | §6.13 test 19 |
| PRE-2 (estado EnEjecucion) | §6.5 |
| PRE-3 (HallazgoId existe) | §6.6 |
| PRE-4 (hallazgo no eliminado) | §6.7 |
| PRE-5 (RepuestoId existe y pertenece al HallazgoId) | §6.8 y §6.9 |
| PRE-7 (Cantidad > 0) | §6.10 x2 |
| PRE-8 (al menos un campo) | §6.11 |
| D-1 (delta en evento) | §6.1 y §6.2 — Justificacion/Cantidad=null en evento |
| D-5 (normalización P-2) | handler test P-2 |

---

## 6. Notas al agente green

1. Implementar `Inspeccion.ActualizarRepuesto` en `Inspeccion.cs` siguiendo el patrón de `AsignarRepuesto`: validar PRE-2→PRE-4→PRE-5→PRE-8→PRE-7 en ese orden, emitir `RepuestoActualizado_v1` con el delta.
2. Implementar `ActualizarRepuestoHandler.Handle` en `ActualizarRepuestoHandler.cs`: cargar aggregate (PRE-1), normalizar `ObservacionNueva` vacía a null (P-2), delegar al aggregate, append+SaveChangesAsync, retornar `ActualizarRepuestoResult` con el estado post-update.
3. PRE-5: `_repuestos.Any(r => r.RepuestoId == cmd.RepuestoId && r.HallazgoId == cmd.HallazgoId)`. Si el RepuestoId existe pero en otro hallazgo → `RepuestoNoEncontradoException` (D-2).
4. PRE-8 debe evaluarse antes de PRE-7 (primero verificar que hay algo que cambiar, luego que el valor es válido). O después de la normalización P-2.
5. `ActualizarRepuestoResult` debe devolver el estado vigente completo del repuesto post-update (incluye campos no cambiados), no solo el delta. El handler lee el estado del aggregate post-`Apply` o del evento para los campos que cambiaron.
