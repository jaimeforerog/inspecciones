# Red notes — Slice 1j — OmitirItemMonitoreo

**Autor:** red
**Fecha:** 2026-05-08
**Spec consumida:** `slices/1j-omitir-item-monitoreo/spec.md`

---

## 1. Tests escritos

### Proyecto: `tests/Inspecciones.Domain.Tests` — `OmitirItemMonitoreoTests.cs`

| # | Nombre del test | Escenario spec | Invariante |
|---|---|---|---|
| 1 | `OmitirItemMonitoreo_en_inspeccion_monitoreo_enEjecucion_emite_ItemMonitoreoOmitido_v1` | §6.1 happy path | Happy path — tipo evento correcto |
| 2 | `OmitirItemMonitoreo_happy_path_payload_correcto_en_evento_emitido` | §6.1 happy path | Payload completo del evento |
| 3 | `OmitirItemMonitoreo_motivo_con_exactamente_10_chars_emite_ItemMonitoreoOmitido_v1` | §6.2 límite inferior | PRE-4 borde válido exactamente 10 chars |
| 4 | `OmitirItemMonitoreo_item_ya_omitido_lanza_ItemYaOmitidoException_I_M9` | §6.3 I-M9 | PRE-9 — doble omisión rechazada |
| 5 | `OmitirItemMonitoreo_item_ya_medido_lanza_ItemYaProcesadoException_I_M8` | §6.4 I-M8 | PRE-8 — ítem ya medido no puede omitirse |
| 6 | `OmitirItemMonitoreo_item_ya_evaluado_cualitativamente_lanza_ItemYaProcesadoException_I_M8` | §6.5 I-M8 | PRE-8 — ítem ya evaluado no puede omitirse |
| 7 | `OmitirItemMonitoreo_motivo_vacio_lanza_MotivoOmisionInvalidoException` | §6.6 PRE-3 | PRE-3 — motivo vacío |
| 8 | `OmitirItemMonitoreo_motivo_solo_whitespace_lanza_MotivoOmisionInvalidoException` | §6.6 PRE-3 | PRE-3 — motivo solo whitespace |
| 9 | `OmitirItemMonitoreo_motivo_menor_a_10_chars_lanza_MotivoOmisionInvalidoException` | §6.7 PRE-4 | PRE-4 — motivo con 5 chars |
| 10 | `OmitirItemMonitoreo_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1` | §6.8 I-M1 | PRE-5 / I-M1 — Tipo=Tecnica |
| 11 | `OmitirItemMonitoreo_con_ItemId_inexistente_en_snapshot_lanza_ItemNoEncontradoEnSnapshotException_I_M3` | §6.9 I-M3 | PRE-7 / I-M3 — ItemId 999 no existe |
| 12 | `OmitirItemMonitoreo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I_M2` | §6.10 I-M2 | PRE-6 / I-M2 — Estado=Firmada |
| 13 | `OmitirItemMonitoreo_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException` [SKIP] | §6.11 PRE-2 | Handler — Application.Tests |
| 14 | `OmitirItemMonitoreo_replay_mismo_clientCommandId_no_re_ejecuta_handler` [SKIP] | §6.12 Idempotencia | Wolverine — Application.Tests |
| 15 | `OmitirItemMonitoreo_segundo_item_coexiste_con_omision_previa_emite_ItemMonitoreoOmitido_v1` | §6.13 coexistencia | ItemId=5 libre con ItemId=3 omitido |
| 16 | `OmitirItemMonitoreo_segundo_item_agrega_ItemId5_a_itemsOmitidos_sin_perder_ItemId3` | §6.13 coexistencia | Estado _itemsOmitidos={3,5} |
| 17 | `OmitirItemMonitoreo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones` | §6.14 rebuild obligatorio | Apply puro + estado coherente |
| 18 | `OmitirItemMonitoreo_Apply_puro_no_lanza_al_reproyectar_solo_eventos_del_slice` | §6.14 rebuild acotado | Apply directo sin pasar por decisión |

**Total:** 16 tests activos (15 fallando + 1 pasando por diseño) + 2 Skip.

---

## 2. Verificación de estado rojo

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
  --filter "FullyQualifiedName~OmitirItemMonitoreo" `
  --no-build
```

**Resultado confirmado:**
```
Con error! - Con error: 15, Superado: 1, Omitido: 2, Total: 18, Duración: ~270 ms
```

Para verificar que los tests previos siguen verdes:

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
  --filter "FullyQualifiedName!~OmitirItemMonitoreo" `
  --no-build
```

**Resultado:** `Correctas! - Con error: 0, Superado: 151, Omitido: 4, Total: 155` — baseline intacto.

---

## 3. Razón del fallo de cada test

**Tests activos (15 de 16) fallan por:**

```
System.NotImplementedException: Slice 1j — OmitirItemMonitoreo no implementado.
El green debe implementar este método.
   at Inspecciones.Domain.Inspecciones.Inspeccion.OmitirItem(OmitirItemMonitoreo cmd, DateTimeOffset ahora)
```

- Tests de **happy path** (§6.1, §6.2, §6.13, §6.14 rebuild): fallan porque `OmitirItem` lanza `NotImplementedException` en lugar de emitir `ItemMonitoreoOmitido_v1`.
- Tests de **precondición** (§6.3, §6.4, §6.5, §6.6, §6.7, §6.8, §6.9, §6.10): fallan porque se espera excepción de dominio específica (`ItemYaOmitidoException`, `ItemYaProcesadoException`, `MotivoOmisionInvalidoException`, `InspeccionNoEsMonitoreoException`, `ItemNoEncontradoEnSnapshotException`, `InspeccionNoEnEjecucionException`) pero se lanza `NotImplementedException`.

**Test que pasa (1 de 16) — por diseño correcto:**

- `OmitirItemMonitoreo_Apply_puro_no_lanza_al_reproyectar_solo_eventos_del_slice`: llama directamente a `Inspeccion.Reconstruir` con un stream hardcodeado que incluye `ItemMonitoreoOmitido_v1`. Pasa porque `Apply(ItemMonitoreoOmitido_v1)` ya existía como stub en el slice 1i y está implementado correctamente. Esto es coherente con el estado rojo: el `Apply` es puro y ya funciona; el test que falla es el del método de decisión `OmitirItem`. Este test que pasa tiene valor diagnóstico: confirma que `Apply` no tiene validaciones intrusas y que el rebuild desde stream es posible una vez que el green implemente el método de decisión.

**Tests Skip (2):**

- `OmitirItemMonitoreo_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException`: PRE-2 vive en el handler, requiere Marten/Testcontainers.
- `OmitirItemMonitoreo_replay_mismo_clientCommandId_no_re_ejecuta_handler`: idempotencia Wolverine envelope dedup, requiere infra.

---

## 4. Código de producción tocado (stubs mínimos)

### Nuevos archivos

- `src/Inspecciones.Domain/Inspecciones/OmitirItemMonitoreo.cs` — command record (stub mínimo con firma completa).
- `tests/Inspecciones.Domain.Tests/Inspecciones/OmitirItemMonitoreoFixtures.cs` — fixtures del slice.
- `tests/Inspecciones.Domain.Tests/Inspecciones/OmitirItemMonitoreoTests.cs` — tests del slice.

### Archivos modificados

- `src/Inspecciones.Domain/Inspecciones/ItemMonitoreoOmitido_v1.cs` — renombrados campos `MotivoOmision → Motivo` y `OmitidoPor → EmitidoPor` para alinear con la spec firmada (§3.1, P-2 resuelta). Esto es un cambio de la firma del stub creado en el slice 1i.
- `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` — 3 nuevas excepciones: `ItemYaProcesadoException` (I-M8, `422`), `ItemYaOmitidoException` (I-M9, `409`), `MotivoOmisionInvalidoException` (PRE-3/PRE-4, `400`).
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — método stub `OmitirItem(cmd, ahora)` con `throw new NotImplementedException(...)`.
- `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` — método `OmitirItem` añadido.

### Incidentales — rename de campos de `ItemMonitoreoOmitido_v1`

El rename `MotivoOmision → Motivo` y `OmitidoPor → EmitidoPor` en el record del evento rompe los callers existentes en tests de slices anteriores. Se actualizaron:

- `tests/Inspecciones.Domain.Tests/Inspecciones/MedicionFixtures.cs` — `StreamMonitoreoConItemOmitido`: `MotivoOmision: → Motivo:`, `OmitidoPor: → EmitidoPor:`.
- `tests/Inspecciones.Domain.Tests/Inspecciones/EvaluacionCualitativaFixtures.cs` — `StreamMonitoreoConItemOmitido`: ídem.
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — `Apply(ItemMonitoreoOmitido_v1 e)`: `e.OmitidoPor → e.EmitidoPor`.

Verificado: los 151 tests previos siguen verdes tras estos cambios incidentales.

---

## 5. Desviaciones respecto a la spec

- **Rename de campos en `ItemMonitoreoOmitido_v1`:** el stub del slice 1i usaba `MotivoOmision`/`OmitidoPor`. La spec 1j (P-2 resuelta 2026-05-08) confirma `Motivo`/`EmitidoPor` por coherencia con los eventos hermanos del módulo. Se aplicó el rename en este slice. Documentado aquí como cambio incidental de renombre de stub — no afecta tests de dominio (ningún test de slices 1i o 1i' afirma directamente sobre los nombres de campo de `ItemMonitoreoOmitido_v1`; solo construyen instancias que compilaron con los nombres nuevos tras el rename).

---

## 6. Hand-off a green

- Spec firmada: sí.
- Todos los tests activos rojos: sí (15 de 16 activos fallan; 1 pasa por diseño correcto del `Apply` previo).
- Sin cambios de comportamiento accidentales: sí (151 tests previos verdes).

### Qué debe implementar el green en `Inspeccion.OmitirItem`

1. **PRE-3 / PRE-4:** validar `cmd.Motivo` — no vacío, no solo whitespace, `Trim().Length >= 10`. Lanzar `MotivoOmisionInvalidoException`.
2. **PRE-5 / I-M1:** `if (Tipo != TipoInspeccion.Monitoreo) throw new InspeccionNoEsMonitoreoException(...)`.
3. **PRE-6 / I-M2:** `if (Estado != EstadoInspeccion.EnEjecucion) throw new InspeccionNoEnEjecucionException(...)`.
4. **PRE-7 / I-M3:** buscar `ItemsSnapshot.FirstOrDefault(i => i.ItemId == cmd.ItemId)`. Si `null` → `ItemNoEncontradoEnSnapshotException`.
5. **PRE-8 / I-M8:** `if (_itemsMedidos.Contains(cmd.ItemId) || _itemsEvaluados.Contains(cmd.ItemId)) throw new ItemYaProcesadoException(...)`. El mensaje debe distinguir medido vs evaluado.
6. **PRE-9 / I-M9:** `if (_itemsOmitidos.Contains(cmd.ItemId)) throw new ItemYaOmitidoException(...)`.
7. **Emisión:** siempre un único `ItemMonitoreoOmitido_v1(InspeccionId, ItemId, Motivo=cmd.Motivo, EmitidoPor=cmd.EmitidoPor, OmitidoEn=ahora)`. Sin segundo evento — la omisión nunca genera hallazgo.
8. **Orden de precondiciones recomendado:** PRE-3/PRE-4 primero (validación de entrada), luego PRE-5 (tipo), PRE-6 (estado), PRE-7 (snapshot), PRE-8 (ya procesado), PRE-9 (ya omitido). Así los errores de entrada se reportan antes de cualquier acceso al estado del aggregate.

### Nota sobre `Apply`

`Apply(ItemMonitoreoOmitido_v1)` ya existe y está implementado correctamente desde el slice 1i (actualizado con `EmitidoPor` en este slice). El green no necesita tocarlo.
