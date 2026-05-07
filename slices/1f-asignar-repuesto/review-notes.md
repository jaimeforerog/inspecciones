# Review notes — Slice 1f — AsignarRepuesto

**Autor:** reviewer
**Fecha:** 2026-05-07
**Slice auditado:** `slices/1f-asignar-repuesto/`
**Veredicto:** `approved`

---

## 1. Resumen ejecutivo

El slice 1f cierra limpio: 74/74 tests en verde, 0 warnings de compilación, cobertura de ramas del agregado `Inspeccion` en 96.29 % (umbral: 85 %), `Apply(RepuestoEstimado_v1)` puro, skip de EliminarHallazgoTests §6.7 levantado y verificado. Los 4 escenarios de handler (PRE-F, PRE-H1, PRE-H2, PRE-0) están correctamente delegados a tests de integración con justificación documentada en red-notes. No hay blockers. Los dos hallazgos son nits asumidos.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente.
  - §6.1 → `AsignarRepuesto_en_inspeccion_en_ejecucion_con_RequiereIntervencion_emite_RepuestoEstimado_v1`
  - §6.2 → `AsignarRepuesto_con_cantidad_fraccionaria_emite_RepuestoEstimado_v1_con_fraccion`
  - §6.3 → `AsignarRepuesto_con_RepuestoId_ya_existente_devuelve_lista_vacia_sin_lanzar_PRE_D`
  - §6.4 → `AsignarRepuesto_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7`
  - §6.5 → `AsignarRepuesto_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE_B1`
  - §6.6 → `AsignarRepuesto_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException_PRE_B2`
  - §6.7 → `AsignarRepuesto_en_hallazgo_NoRequiereIntervencion_lanza_HallazgoNoRequiereIntervencionException_I_H12`
  - §6.8 → `AsignarRepuesto_con_Cantidad_cero_lanza_CantidadInvalidaException_PRE_E`
  - §6.9 → `AsignarRepuesto_con_SkuId_duplicado_en_hallazgo_distinto_RepuestoId_lanza_SkuDuplicadoEnHallazgoException_PRE_G`
  - §6.10, §6.11, §6.12 → delegados a integración (handler), justificado en red-notes §3.
  - §6.13 → `EliminarHallazgo_con_repuesto_activo_lanza_HallazgoTieneHijosActivosException_I_H9` en `AsignarRepuestoTests` + `EliminarHallazgo_con_hijos_activos_lanza_HallazgoTieneHijosActivosException_I_H9` en `EliminarHallazgoTests` (skip levantado).
  - §6.14 → `AsignarRepuesto_rebuild_desde_stream_reproduce_estado_con_repuesto`
- [x] Cada precondición tiene un test que la viola: PRE-A (§6.4), PRE-B1 (§6.5), PRE-B2 (§6.6), PRE-C/I-H12 (§6.7), PRE-E (§6.8), PRE-D retorno silencioso (§6.3), PRE-G (§6.9). PRE-F/PRE-H1/PRE-H2 en tests de integración del handler (aceptado por red-notes §3).
- [x] Cada invariante tocada tiene test que la viola: I-H7 (§6.4), I-H9 (§6.13 — skip levantado), I-H12/PRE-C (§6.7). Referencias al código del invariante presentes en comentarios de los tests.
- [x] Nombres de tests son frases descriptivas en español con código de invariante o PRE cuando aplica.

### 2.2 Tests como documentación

- [x] Given/When/Then estructuralmente visible en cada test mediante comentarios de sección.
- [x] Sin mocks del dominio. Los streams de Given son arrays de eventos reales; `CasoDeUso` usa `Inspeccion.Reconstruir`.
- [x] Eventos usados en Given son plausibles: `RepuestoEstimadoEjemplo` usa `SkuId=501`, `Cantidad=2m`, `Unidad="unidad"` — valores de negocio reales, no `(0,0)` ni nonsense. `UbicacionGps` en el stream de inicio usa `UbicacionTipo()` del fixture de Fixtures.cs (verificado en slice 1a — coordenadas de Colombia).

### 2.3 Implementación

- [x] Código de producción mínimo: `AsignarRepuesto.cs`, `RepuestoEstimado_v1.cs`, `Repuesto.cs`, tres excepciones nuevas, `_repuestos`/`Repuestos` en `Inspeccion.cs`, `AsignarRepuesto(...)` y `Apply(RepuestoEstimado_v1)`. Todos los miembros públicos nuevos están ejercidos por al menos un test.
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()`, ni acceso a APIs del navegador en el dominio. `AsignadoEn` viene del parámetro `ahora: DateTimeOffset`; `RepuestoId` llega en el comando.
- [x] `RepuestoEstimado_v1` es `sealed record` inmutable — sin setters públicos.
- [x] `Repuesto` es `sealed record` — value object de estado, sin lógica, sin setters públicos. El XML doc confirma que es "Solo para fold — no es evento ni comando."
- [x] `Apply(RepuestoEstimado_v1)` puro: solo `_repuestos.Add(...)` y `_contribuyentes.Add(...)`. Sin validaciones, sin throw. Verificado en `Inspeccion.cs` líneas 539-550.
- [x] Rebuild test presente (§6.14): reproyecta stream completo sobre `Inspeccion.Reconstruir(...)` y verifica `Estado`, `Repuestos.Count`, campos individuales del repuesto y `Contribuyentes`.
- [x] Atomicidad del handler: no hay handler HTTP implementado en este slice (solo dominio puro). El spec §7 documenta un único `IDocumentSession.SaveChangesAsync()` para cuando se implemente. No aplicable como blocker en este slice de dominio puro.
- [x] `UbicacionGps` en su campo respectivo; `SkuId: int` y no `string`; `Cantidad: decimal` y no `double`. Tipos alineados con §15.4 y CLAUDE.md.
- [x] Orden de PRE-D antes de PRE-A (idempotencia primero): correctamente documentado en green-notes §2 y en el comentario del código (línea 478-483 de `Inspeccion.cs`).

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado `Inspeccion` >= 85 %. Actual: **96.29 %** (104/108 ramas).
- Las 4 ramas descubiertas son carry-over de slices anteriores, no introducidas por este slice:
  - Línea 387 (`Apply(HallazgoActualizado_v1)`) — rama `idx < 0` (rebuild con gap): no ejercida porque no hay test de HallazgoActualizado_v1 con hallazgo ausente del stream. Heredada del slice 1d.
  - Línea 456 (`Apply(HallazgoEliminado_v1)`) — rama `idx < 0` (rebuild con gap): análoga. Heredada del slice 1e.
  - Línea 511 (`AsignarRepuesto` — PRE-G lambda) — 2 ramas: el predicado `r.HallazgoId == cmd.HallazgoId && r.SkuId == cmd.SkuId && r.RepuestoId != cmd.RepuestoId` tiene 6 ramas de evaluación corta; el test §6.9 cubre el camino "todo verdadero → excepción" y §6.3 cubre "RepuestoId igual → retorno silencioso antes de PRE-G". Las 2 ramas no cubiertas corresponden a los paths de cortocircuito de `HallazgoId != cmd.HallazgoId` o `SkuId != cmd.SkuId` dentro de la lista existente — no se prueban explícitamente porque §6.9 solo establece el caso positivo (mismo HallazgoId + mismo SkuId + distinto RepuestoId). No son ramas de negocio críticas.
- Ninguna de las 4 brechas requiere justificación adicional en refactor-notes dado el umbral cumplido. Las dos de Apply carry-over ya eran conocidas (slice 1d/1e).

### 2.5 Refactor

- [x] `refactor-notes.md` presente con tabla de cambios aplicados y tabla de candidatos descartados con justificación.
- [x] Los tests no cambiaron de lógica entre green y refactor: el único cambio fue actualizar el XML doc de `ObtenerHallazgoActivo` — sin modificaciones a lógica de tests.
- [x] 0 warnings de compilación — confirmado en green-notes §4 y refactor-notes §"Build".

### 2.6 Invariantes cross-slice

- [x] `dotnet test` completo del repo: **74 Superado, 0 Con error, 0 Omitido**. Los 62 tests de slices 1a..1e siguen en verde. El skip levantado de EliminarHallazgoTests §6.7 pasa.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con §15.4 del modelo: evento #10 `RepuestoEstimado_v1` con los campos correctos (`SkuId: int`, `SkuCodigo: string`, `Cantidad: decimal`, `DateTimeOffset AsignadoEn`).
- [x] Alineado con §15.3: I-H7 (PRE-A), I-H9 (PRE-D en EliminarHallazgo levantado). I-H12 propuesta como nueva invariante pendiente de actualizar en el modelo (embargo de docs vigente — anotado en spec §5 para FOLLOWUPS.md cuando se levante el embargo).
- [x] Alineado con §12.10.12: `AccionRequerida=RequiereIntervencion` requerido (PRE-C), `Cantidad > 0` (PRE-E), `Unidad` derivada del catálogo en handler (no en aggregate).
- [x] ADR-002 (auth tentativo): `TecnicoId` llega como parámetro del comando — el aggregate no conoce JWT. Consistente con slices anteriores.
- [x] ADR-004 (catálogos): `RepuestoLocal` vive en el catálogo local de Marten — la lectura es del document store, no una llamada HTTP en tiempo real. Correctamente separada en PRE-H1/PRE-H2 del handler.
- [x] ADR-006 (outbox): no aplica — no hay llamada saliente al ERP en este slice. Documentado en spec §11.
- [x] ADR-008 (idempotencia): PRE-D en aggregate como segunda línea de defensa, correctamente documentado en spec §7.
- [x] `Guid.NewGuid()` solo en handlers/tests — `RepuestoId` generado fuera del aggregate. CLAUDE.md cumplido.
- [x] `DateTimeOffset` en lugar de `DateTime` para `AsignadoEn` — CLAUDE.md cumplido (modelo histórico §12.10.12 usaba `DateTime` pero la convención vigente es `DateTimeOffset`).

### 2.8 Integración cross-team Sinco

No aplica en este slice. `AsignarRepuesto` es puramente local; la integración ocurre en `CerrarInspeccionSaga` (slice posterior). Documentado en spec §11.

### 2.9 SignalR / push

No aplica. Documentado en spec §10 con justificación: `RepuestoEstimado_v1` no está en el catálogo de eventos SignalR (ADR-005). La asignación de repuesto es operación local del técnico.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | nit | El test §6.1 (happy path principal) no verifica `_contribuyentes.Contains("rmartinez")` aunque la spec §6.1 §Then lo requiere explícitamente. El test §6.14 (rebuild) sí lo verifica. El comportamiento está cubierto indirectamente, pero el happy path primario debería ser autocontenido. | `AsignarRepuestoTests.cs:22-52` | Añadir `resultado.Contribuyentes.Should().Contain("rmartinez")` en §6.1 — o verificarlo después del rebuild del aggregate. No bloquea. |
| 2 | nit | El test §6.8 usa `Cantidad=0` para PRE-E pero el código verifica `<= 0`. El caso `Cantidad=-1` no está cubierto. El guard `<= 0` cubre ambos por construcción, pero un segundo test con valor negativo haría la intención del guard explícita para el lector. | `AsignarRepuestoTests.cs:214-231` | Agregar test `AsignarRepuesto_con_Cantidad_negativa_lanza_CantidadInvalidaException_PRE_E` con `Cantidad=-1m`. Nit — la rama ya está cubierta lógicamente. |

---

## 4. Veredicto final

- [x] **approved** — sin hallazgos bloqueantes. Los dos nits (§6.1 sin aserción de `Contribuyentes`, §6.8 sin test de cantidad negativa) no bloquean el cierre del slice. El orquestador puede proceder al commit y a la fase `infra-wire`.

---

_El orquestador puede proceder al commit del slice y a la fase de infra-wire._
