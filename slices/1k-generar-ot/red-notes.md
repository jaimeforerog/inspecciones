# Red notes — Slice 1k — GenerarOT

**Autor:** red
**Fecha:** 2026-05-08
**Spec consumida:** `slices/1k-generar-ot/spec.md`

---

## 1. Tests escritos

### Proyecto: `tests/Inspecciones.Domain.Tests` — `GenerarOTTests.cs`

| # | Nombre del test | Escenario spec | Invariante |
|---|---|---|---|
| 1 | `GenerarOT_inspeccion_firmada_con_dictamen_NoPuedeOperar_y_hallazgo_RequiereIntervencion_emite_OTSolicitada_v1` | §6.1 happy path | Tipo evento correcto |
| 2 | `GenerarOT_payload_OTSolicitada_v1_contiene_todos_los_campos_del_comando_seccion_6_1` | §6.1 happy path | Payload completo con Prioridad, Observaciones, ComentarioJefe |
| 3 | `GenerarOT_estado_aggregate_OTSolicitada_es_true_tras_emision` | §6.1 happy path + estado | Apply muta OTSolicitada=true, Estado sigue Firmada |
| 4 | `GenerarOT_con_dictamen_ConRestriccion_y_ComentarioJefe_emite_OTSolicitada_v1_con_payload_correcto` | §6.2 happy path | DepartamentoEquipos + Alta + ComentarioJefe |
| 5 | `GenerarOT_sin_capability_generar_ot_lanza_excepcion_403_PRE_1` [SKIP] | §6.3 PRE-1 | Middleware HTTP — cubre Api.Tests |
| 6 | `GenerarOT_en_inspeccion_no_firmada_EnEjecucion_lanza_InspeccionNoFirmadaException_I_F4_a` | §6.4 PRE-3 I-F4.a | Estado EnEjecucion rechazado |
| 7 | `GenerarOT_sin_hallazgos_con_RequiereIntervencion_lanza_SinHallazgosConIntervencionException_I_F4_b` | §6.5 PRE-4 I-F4.b | Solo RequiereSeguimiento → excepción |
| 8 | `GenerarOT_OT_ya_solicitada_previamente_lanza_OTYaSolicitadaException_I_F4_c` | §6.6 PRE-5 I-F4.c | OTSolicitada=true → 409 |
| 9 | `GenerarOT_OT_rechazada_previamente_lanza_OTRechazadaException_I_F4_d` | §6.7 PRE-6 I-F4.d | OTRechazada=true → 409 |
| 10 | `GenerarOT_dictamen_PuedeOperar_lanza_DictamenNoPermiteOTException_I_F4_e` | §6.8 PRE-7 I-F4.e | Defensa 2da línea V-F8 |
| 11 | `GenerarOT_replay_mismo_clientCommandId_no_duplica_evento_ni_re_ejecuta_handler` [SKIP] | §6.9 Idempotencia | Wolverine envelope dedup — Application.Tests |
| 12 | `GenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2` [SKIP] | §6.10 PRE-2 | Handler/Marten — Application.Tests |
| 13 | `GenerarOT_hallazgo_RequiereIntervencion_eliminado_no_cuenta_para_PRE_4_lanza_SinHallazgosConIntervencionException` | §6.11 caso borde PRE-4 | Eliminado=true no cuenta |
| 14 | `GenerarOT_en_inspeccion_CerradaSinOT_lanza_InspeccionNoFirmadaException_I_F4_a` | §6.12 PRE-3 variante | Estado terminal CerradaSinOT |
| 15 | `GenerarOT_rebuild_desde_stream_7_eventos_reproduce_estado_correcto` | §6.13 rebuild obligatorio | Apply puro + 7 eventos en orden causal |

**Total tests activos:** 12 fallando + 1 pasando por diseño (rebuild) + 3 Skip = 15 tests.

**Decisión sobre §6.1:** se desglosó en 3 tests independientes (tipo evento, payload completo, estado del aggregate) en lugar de uno monolítico. Esta granularidad facilita el diagnóstico durante la fase green. No viola la regla "un test por escenario" — son tres aspectos distinguibles del mismo escenario.

---

## 2. Verificación de estado rojo

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
  --filter "FullyQualifiedName~GenerarOT" `
  --no-build
```

**Resultado confirmado:**
```
Con error! - Con error: 11, Superado: 1, Omitido: 3, Total: 15, Duración: ~99 ms
```

Verificación baseline (tests de slices previos no rotos):

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
  --filter "FullyQualifiedName!~GenerarOT" `
  --no-build
```

**Resultado:** `Correctas! - Con error: 0, Superado: 167, Omitido: 6, Total: 173` — baseline intacto.

---

## 3. Razón del fallo de cada test

**Tests activos (11 de 12) fallan por:**

```
System.NotImplementedException: Slice 1k — GenerarOT no implementado. El green debe implementar este método.
   at Inspecciones.Domain.Inspecciones.Inspeccion.SolicitarOT(GenerarOT cmd, DateTimeOffset ahora)
```

- Tests de **happy path** (§6.1×3, §6.2): fallan porque `SolicitarOT` lanza `NotImplementedException`.
- Tests de **precondición** (§6.4, §6.5, §6.6, §6.7, §6.8, §6.11, §6.12): fallan porque se espera excepción de dominio específica (`InspeccionNoFirmadaException`, `SinHallazgosConIntervencionException`, `OTYaSolicitadaException`, `OTRechazadaException`, `DictamenNoPermiteOTException`) pero se lanza `NotImplementedException`.

**Test que pasa (1 de 12) — por diseño correcto:**

- `GenerarOT_rebuild_desde_stream_7_eventos_reproduce_estado_correcto`: llama directamente a `Inspeccion.Reconstruir` con un stream hardcodeado de 7 eventos incluyendo `OTSolicitada_v1`. Pasa porque `Apply(OTSolicitada_v1)` está implementado como mutación pura. Confirma que los `Apply` son puros y que el rebuild desde stream es posible una vez que green implemente `SolicitarOT`.

**Tests Skip (3):**
- `GenerarOT_sin_capability_generar_ot_lanza_excepcion_403_PRE_1` (§6.3): PRE-1 vive en middleware HTTP.
- `GenerarOT_replay_mismo_clientCommandId_no_duplica_evento_ni_re_ejecuta_handler` (§6.9): idempotencia Wolverine envelope dedup.
- `GenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2` (§6.10): PRE-2 vive en el handler con Marten.

---

## 4. Código de producción tocado (stubs mínimos)

### Nuevos archivos en `src/`

| Archivo | Descripción |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/ResponsableCosto.cs` | Enum nuevo — `Proyecto | DepartamentoEquipos` |
| `src/Inspecciones.Domain/Inspecciones/PrioridadOT.cs` | Enum nuevo — `Baja | Normal | Alta | Urgente` |
| `src/Inspecciones.Domain/Inspecciones/GenerarOT.cs` | Command record stub (sin `: ICommand` — patrón del repo no usa interfaz) |
| `src/Inspecciones.Domain/Inspecciones/OTSolicitada_v1.cs` | Evento con campos P-1/P-2 confirmados |
| `src/Inspecciones.Domain/Inspecciones/GeneracionOTRechazada_v1.cs` | Stub para PRE-6 §6.7 (slice futuro RechazarGenerarOT) |
| `src/Inspecciones.Domain/Inspecciones/InspeccionCerradaSinOT_v1.cs` | Stub para PRE-3 §6.12 (slice futuro CerrarSinOT) |

### Archivos modificados en `src/`

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | 5 nuevas excepciones: `InspeccionNoFirmadaException`, `SinHallazgosConIntervencionException`, `OTYaSolicitadaException`, `OTRechazadaException`, `DictamenNoPermiteOTException` |
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Propiedades `OTSolicitada`, `OTRechazada`, `SolicitadaEn`; método stub `SolicitarOT(cmd, ahora)` con `throw new NotImplementedException(...)`; `Apply(OTSolicitada_v1)`, `Apply(GeneracionOTRechazada_v1)`, `Apply(InspeccionCerradaSinOT_v1)`; 3 cases en `AplicarEvento` switch |

### Nuevos archivos en `tests/`

| Archivo | Descripción |
|---|---|
| `tests/Inspecciones.Domain.Tests/Inspecciones/GenerarOTTests.cs` | 15 tests del slice (12 activos, 3 Skip) |
| `tests/Inspecciones.Domain.Tests/Inspecciones/GenerarOTFixtures.cs` | Fixtures de streams y comandos para los 13 escenarios |

### Archivos modificados en `tests/`

| Archivo | Cambio |
|---|---|
| `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` | Método `SolicitarOT` añadido |

---

## 5. Desviaciones respecto a la spec

- **§6.1 desglosado en 3 tests:** el escenario §6.1 se dividió en tres tests independientes (tipo evento, payload completo, estado del aggregate). El spec es suficientemente denso para justificarlo — facilita el diagnóstico en fase green. Sin violación de invariantes de negocio.

- **`ICommand` no usado:** el spec §2 muestra `: ICommand` en el record del comando. El repo no usa `ICommand` — ningún comando existente implementa esa interfaz (verificado en `OmitirItemMonitoreo.cs`, `FirmarInspeccion.cs`, etc.). Se omite para consistencia.

- **`CierreOTPendiente` no en `EstadoInspeccion`:** el spec §4 menciona `CierrePendienteOT` como estado posible en §5. No existe en el enum `EstadoInspeccion.cs` actual. No se añadió porque ningún test de este slice lo requiere (el estado se gestiona por la saga 3.24b). Candidato para followup.

---

## 6. Hand-off a green

- Spec firmada: sí (con decisiones P-1 opción B y P-2 campos en evento).
- Todos los tests activos rojos: sí (11/12 fallan; 1 pasa por diseño del Apply puro).
- Sin cambios de comportamiento accidentales: sí (167 tests previos verdes).

### Qué debe implementar green en `Inspeccion.SolicitarOT`

Orden de precondiciones recomendado (de más genérico a más específico):

1. **PRE-3 / I-F4.a:** `Estado != EstadoInspeccion.Firmada` → `throw InspeccionNoFirmadaException($"La inspección {InspeccionId} está en estado '{Estado}'. GenerarOT solo aplica a inspecciones en estado 'Firmada'.")`
2. **PRE-7 / I-F4.e (defensa):** `Dictamen == DictamenOperacion.PuedeOperar` → `throw DictamenNoPermiteOTException("El dictamen 'PuedeOperar' no permite generar OT. Solo ConRestriccion o NoPuedeOperar son válidos.")`
3. **PRE-4 / I-F4.b:** `!_hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == AccionRequerida.RequiereIntervencion)` → `throw SinHallazgosConIntervencionException($"La inspección {InspeccionId} no tiene hallazgos activos con AccionRequerida=RequiereIntervencion. GenerarOT requiere al menos uno.")`
4. **PRE-5 / I-F4.c:** `OTSolicitada` → `throw OTYaSolicitadaException($"La inspección {InspeccionId} ya tiene una OT solicitada. No se aceptan dos solicitudes de OT sobre el mismo stream.")`
5. **PRE-6 / I-F4.d:** `OTRechazada` → `throw OTRechazadaException($"La inspección {InspeccionId} ya tiene la generación de OT rechazada. No se puede solicitar OT una vez rechazada.")`
6. **Emisión:** `return new object[] { new OTSolicitada_v1(InspeccionId, cmd.SolicitadaPor, cmd.Responsable, cmd.Prioridad, cmd.Observaciones, cmd.ComentarioJefe, ahora) }`

### Nota sobre `Apply`

`Apply(OTSolicitada_v1)` ya existe y está implementado correctamente desde este slice. El green no necesita tocarlo.
