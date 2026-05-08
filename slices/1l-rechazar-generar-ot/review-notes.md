# Review notes — Slice 1l — RechazarGenerarOT

**Autor:** reviewer
**Fecha:** 2026-05-08
**Slice auditado:** `slices/1l-rechazar-generar-ot/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice está completo en su capa de dominio: método de decisión `RechazarOT`, dos eventos canónicos, enum `MotivoCierreSinOT`, excepciones nuevas, fixtures, 18 tests en verde, 3 skips justificados, cobertura de ramas 94.92% (sobre el umbral de 85%), 0 warnings, 0 errores de compilación, y 197/209 tests en verde en el suite completo. Los `Apply` son puros, el rebuild test cubre los 7 eventos en orden causal, y los refactors cross-slice (D-2, D-3) no introdujeron regresiones. Se emiten dos followups nuevos: FU-34 (FU-30 no cerrado en FOLLOWUPS.md) y FU-35 (spec §8.1 afirma que la proyección consume `InspeccionCerradaSinOT_v1`, pero no lo hace). Ambos son deuda de documentación, no blockers funcionales.

---

## 2. Checklist de auditoría

### 2.1 Spec vs tests

| Escenario spec §6 | Test correspondiente | Estado |
|---|---|---|
| §6.1 — happy path NoPuedeOperar | `emite_dos_eventos_en_orden_causal`, `GeneracionOTRechazada_v1_emitido_antes`, `payload_GeneracionOTRechazada_v1`, `payload_InspeccionCerradaSinOT_v1` | PASA |
| §6.2 — happy path ConRestriccion | `con_dictamen_ConRestriccion_y_hallazgo_intervencion_emite_dos_eventos` | PASA |
| §6.3 — PRE-1 capability ausente | Skip justificado (middleware HTTP) | PASA |
| §6.4 — PRE-3 motivo corto / borde | `motivo_menor_10_chars`, `motivo_9_chars_borde_inferior`, `motivo_exactamente_10_chars_es_valido` | PASA |
| §6.5 — PRE-3 motivo solo espacios | `motivo_vacio_o_solo_espacios` | PASA |
| §6.6 — PRE-4 EnEjecucion | `inspeccion_no_firmada_EnEjecucion_lanza_InspeccionNoFirmadaException_I_F6` | PASA |
| §6.7 — PRE-4 CerradaSinOT | `inspeccion_cerrada_sin_OT_lanza_InspeccionNoFirmadaException` | PASA |
| §6.8 — PRE-5 sin RequiereIntervencion | `sin_hallazgos_con_intervencion_lanza_SinHallazgosConIntervencionException_I_F6` | PASA |
| §6.9 — PRE-5 hallazgo eliminado | `hallazgo_intervencion_eliminado_no_cuenta` | PASA |
| §6.10 — PRE-6 OT ya solicitada | `OT_ya_solicitada_lanza_OTYaSolicitadaException_I_F6` | PASA |
| §6.11 — PRE-7 aislado + precedencia PRE-4 | `OT_ya_rechazada_estado_firmada_lanza_OTYaRechazadaException_I_F6`, `doble_rechazo_completo_PRE4_intercepta_antes_que_PRE7` | PASA |
| §6.12 — PRE-2 InspeccionId inexistente | Skip justificado (handler/Marten) | PASA |
| §6.13 — idempotencia Wolverine | Skip justificado (Wolverine infra) | PASA |
| §6.14 — rebuild 7 eventos | `rebuild_desde_stream_7_eventos_estado_correcto` | PASA |
| Estado post-comando (complemento §6.1) | `estado_post_comando_OTRechazada_true_MotivoRechazoOT_seteado` | PASA |

Cada precondición (PRE-3 a PRE-7) tiene al menos un test que la viola. PRE-1 y PRE-2 están en skip con justificación explícita de capa. Las invariantes I-F6.a (PRE-4), I-F6.b (PRE-5), I-F6.c (PRE-6) e I-F6.d (PRE-7) tienen test dedicado referenciando el código del invariante en el nombre del test. PASA.

Los nombres de tests son frases completas en español con referencia a invariantes donde aplica. PASA.

### 2.2 Tests como documentación

- Given/When/Then estructuralmente visible por comentarios en cada test. PASA.
- Cero mocks del dominio — `CasoDeUso.RechazarOT` reconstruye el aggregate sobre stream real. PASA.
- Los eventos usados en `Given` son reales: `StreamFirmadoNoPuedeOperar()`, `StreamFirmadoConRestriccion()` reutilizan fixtures de slice 1k. Las coordenadas `UbicacionTipo()` heredadas son plausibles para Colombia. PASA.

### 2.3 Implementación

| Criterio | Resultado |
|---|---|
| Código mínimo: todo miembro público nuevo ejercido por tests | PASA — `MotivoRechazoOT`, `RechazarOT`, `MotivoCierreSinOT`, `MotivoRechazoInvalidoException`, `OTYaRechazadaException` tienen cobertura directa |
| Sin `DateTime.UtcNow` en dominio | PASA — `ahora: DateTimeOffset` inyectado desde el handler |
| Sin `Guid.NewGuid()` en dominio | PASA — no hay new Guid en el aggregate |
| Eventos son `record sealed` inmutables | PASA — `GeneracionOTRechazada_v1` e `InspeccionCerradaSinOT_v1` son sealed records sin setters |
| `Apply(Evt)` puro | PASA — `Apply(GeneracionOTRechazada_v1)`: solo `OTRechazada = true; MotivoRechazoOT = e.Motivo`. `Apply(InspeccionCerradaSinOT_v1)`: solo `Estado = EstadoInspeccion.CerradaSinOT`. Ninguno valida ni lanza. |
| Rebuild test presente | PASA — `RechazarGenerarOT_rebuild_desde_stream_7_eventos_estado_correcto` reproyecta 7 eventos en orden causal sobre `Inspeccion.Reconstruir(stream)` y verifica estado completo |
| Atomicidad handler (un único `SaveChangesAsync`) | N/A para este slice — el handler no fue implementado en el dominio (es responsabilidad de `infra-wire`). El spec §3 especifica la atomicidad; el `refactor-notes.md` la confirma como requerimiento. |
| PRE-3 antes de PRE-4 en `RechazarOT` | PASA — coincide con spec §4: validación de input antes de estado. Los tests confirman que `motivo_menor_10_chars` lanza `MotivoRechazoInvalidoException` sobre stream válido (Firmada). |
| D-2: campo `Motivo` en `GeneracionOTRechazada_v1` | PASA — field renombrado de `MotivoRechazo` a `Motivo`; `GenerarOTFixtures.cs` actualizado. |
| D-3: `CerradoPor` eliminado de `InspeccionCerradaSinOT_v1` | PASA — record sin `CerradoPor`; `GenerarOTFixtures.cs` actualizado con `MotivoCierre: MotivoCierreSinOT.AutomaticoSinIntervencion`. |

### 2.4 Cobertura

Cobertura de ramas de `Inspeccion.cs` medida con `dotnet test --collect:"XPlat Code Coverage"`:

**Resultado: branch-rate = 0.9492 → 94.92%**

Supera el umbral de 85%. PASA.

Las ramas no cubiertas (aprox 5.08% sobre todo el aggregate) corresponden a rutas de slices anteriores no ejercidas por los tests de dominio puro (Docker skip). No hay ramas nuevas introducidas por el slice 1l sin test.

### 2.5 Refactor

- `refactor-notes.md` presente con tres cambios documentados: fix naming en mensajes de excepción, corrección de comentario FU-30, extracción de `TieneHallazgosConIntervencionActivos()`. PASA.
- Los tests no cambiaron de lógica entre green y refactor — solo se aplicaron los tres refactors a `Inspeccion.cs`. PASA.
- `dotnet build` reporta `0 Advertencia(s) / 0 Errores`. PASA.

### 2.6 Invariantes cross-slice

`dotnet test tests/Inspecciones.Domain.Tests` al momento de la auditoría:

```
Correctas! - Con error: 0, Superado: 197, Omitido: 12, Total: 209, Duración: 343 ms
```

Sin regresión fuera del slice. Los tests del slice 1k (`GenerarOTTests`) 12 pass / 3 skip — sin ruptura por D-2 y D-3. PASA.

### 2.7 Coherencia con decisiones previas

| Decisión | Alineamiento |
|---|---|
| `01-modelo-dominio.md §17` (ADR-007) | PASA — `RechazarGenerarOT`, `GeneracionOTRechazada_v1`, `InspeccionCerradaSinOT_v1`, `MotivoCierreSinOT` alineados con §17 salvo D-1 (máx 500 chars asunción del modelador) y D-2 (`Motivo` vs `MotivoRechazo`, justificado) |
| ADR-006 (outbox + retry ERP) | N/A — no hay POST a Sinco |
| ADR-008 (X-Client-Command-Id) | N/A para dominio puro — spec §7 lo documenta; el test de idempotencia está en Skip con razón correcta |
| ADR-003 (OT correctiva) | PASA — cierre `CerradaSinOT` es la alternativa a `GenerarOT`; el slice implementa la rama de rechazo del ADR-007 §17 |
| `DateTimeOffset` sobre `DateTime` | PASA — ambos eventos usan `DateTimeOffset RechazadaEn` / `CerradaEn` (corrección de FU-31 aplicada) |
| `bool OTRechazada` ya existía en 1k | PASA — elevado con `MotivoRechazoOT` sin romper el estado anterior |

### 2.8 Integración cross-team Sinco

No aplica. El slice no invoca endpoints Sinco on-prem ni publica hacia el ERP.

### 2.9 SignalR / push

No aplica para este slice. Spec §10 documenta explícitamente que el push `OTRechazada` es responsabilidad de un proyector lateral en slice futuro (3.45c).

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | FU-30 ("comentario slice incorrecto en switch `AplicarEvento`") fue cerrado en código por el refactor #2 de este slice (`ItemMonitoreoOmitido_v1` ahora bajo `// Slice 1j — OmitirItemMonitoreo`), pero `FOLLOWUPS.md` sigue mostrando FU-30 en sección `## Abiertos` sin marca ✅. Gap de documentación. | `FOLLOWUPS.md` — entrada FU-30 | Mover FU-30 a sección `## Cerrados` con fecha de cierre 2026-05-08 y resolución por refactor #2 del slice 1l. |
| 2 | followup | `spec.md §8.1` afirma: "el stub de 1k incluyó el case para `InspeccionCerradaSinOT_v1`" en `InspeccionAbiertaPorEquipoProjection`. La proyección real no tiene ese case: solo consume `InspeccionIniciada_v1`, `InspeccionFirmada_v1` e `InspeccionCancelada_v1`. La afirmación de la spec es incorrecta. Funcionalmente no es un bug: `InspeccionFirmada_v1` siempre precede a `InspeccionCerradaSinOT_v1` en el stream (el equipo ya queda libre al firmar), por lo que la proyección no necesita un case adicional para `InspeccionCerradaSinOT_v1`. Sin embargo, la afirmación falsa del spec puede inducir a error en slices futuros. | `slices/1l-rechazar-generar-ot/spec.md §8.1` y `src/Inspecciones.Application/Inspecciones/InspeccionAbiertaPorEquipoProjection.cs` | Corregir §8.1 de la spec para reflejar que la proyección ya libera el equipo en `InspeccionFirmada_v1`, y que no hay ni se necesita un case para `InspeccionCerradaSinOT_v1`. Agregar comentario en la proyección aclarando por qué `InspeccionCerradaSinOT_v1` no necesita handler. |

---

## 4. Veredicto final

- [ ] **approved**
- [x] **approved-with-followups** — followups FU-34 y FU-35 registrados en `FOLLOWUPS.md`.
- [ ] **request-changes**

**Razón:** El slice cumple todos los criterios bloqueantes: Apply puro, rebuild test, cobertura 94.92%, 0 warnings, cross-slice sin regresión, eventos `record sealed`, sin `DateTime.UtcNow` en dominio, orden causal correcto (rechazo antes de cierre). Los dos hallazgos son deuda de documentación sin impacto funcional en código o tests.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
