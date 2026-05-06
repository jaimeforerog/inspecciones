# Review notes — Slice 2 — ActualizarHallazgo

**Autor:** reviewer (ejecutado por orchestrator)
**Fecha:** 2026-05-06
**Slice auditado:** `slices/2-actualizar-hallazgo/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice implementa `ActualizarHallazgo` correctamente sobre el agregado `Inspeccion`. Todos los escenarios de la spec están cubiertos, los `Apply` son puros, la cobertura de ramas del agregado es 97 %, y los refactors aplicados eliminan duplicación real (helper `ValidarRequiereIntervencion`). Se identifican tres followups menores que no bloquean el cierre: cobertura de línea en stubs de eventos futuros, handler + endpoint HTTP pendiente, y test de integración HTTP→Postgres pendiente de Docker.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. §6.1 → 2 tests; §6.2 → 1; §6.3 → 1; §6.4 → 1; §6.5 → 1; §6.6 → 1; §6.7 → 2; §6.8 → 2; §6.9 → 2. Total: 13 tests.
- [x] Cada precondición tiene un test que la viola. PRE-2 (§6.3), PRE-3 (§6.4), PRE-4 (§6.5), PRE-5 (§6.6), PRE-6 (§6.7 ×2), PRE-7 (§6.8 ×2). PRE-1 vive en el handler (404) — correcto por spec §4.
- [x] Cada invariante tocada tiene un test: I-H7 → §6.3; I-H8 → garantizado por diseño del payload (no hay test de violación porque el campo no existe en el comando — correcto); I-H4 → §6.6.
- [x] Nombres de tests: frases descriptivas en español con referencia a invariante cuando aplica.

### 2.2 Tests como documentación

- [x] Given/When/Then visible en todos los tests.
- [x] Sin mocks del dominio.
- [x] Coordenadas en `UbicacionGps` plausibles para Colombia (4.711, -74.072 — Bogotá). En el escenario de rebuild se usan datos de fixture heredados de slice 1a.

### 2.3 Implementación

- [x] Código mínimo: todos los miembros públicos nuevos son ejercidos por tests. `HallazgoActualizado_v1`, `HallazgoEliminado_v1` (stub), `ActualizarHallazgo` (comando), `HallazgoNoEncontradoException`, `HallazgoEliminadoException`, `Apply(HallazgoActualizado_v1)`, `Apply(HallazgoEliminado_v1)` (stub).
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()` ni acceso a APIs del navegador en el dominio.
- [x] `HallazgoActualizado_v1` y `HallazgoEliminado_v1` son `record` inmutables.
- [x] `UbicacionGps` en su campo `Ubicacion` (no double pelado). `Hallazgo` extendido correctamente.
- [x] `Apply(HallazgoActualizado_v1)` puro: no valida, no lanza excepciones — solo muta con `with`. Verificado.
- [x] `Apply(HallazgoEliminado_v1)` puro: solo marca `Eliminado=true`. Stub correcto para el rebuild de PRE-4.
- [x] Rebuild test presente: `ActualizarHallazgo_rebuild_desde_stream_reproduce_estado` (§6.9) y `ActualizarHallazgo_rebuild_estado_identico_al_de_decision_in_process`.
- [x] Handler: este slice no tiene handler aún — es trabajo de infra-wire post-review. No es blocker.

### 2.4 Cobertura

- [x] `Inspeccion` (agregado afectado): **branch=97 %, line=98.4 %**. Supera el umbral de 85 %.
- Nota: la rama sin cubrir en `Inspeccion` (3%) corresponde al `if (idx < 0) { return; }` dentro de `Apply(HallazgoActualizado_v1)` y `Apply(HallazgoEliminado_v1)` — ramas defensivas para eventos fuera de orden causal. No ejercitarlas en tests positivos es correcto por diseño; no hay test de stream corrupto previsto en la spec.

### 2.5 Refactor

- [x] `refactor-notes.md` presente y documentado.
- [x] Los tests no se tocaron en la fase refactor (auditado: `ActualizarHallazgoTests.cs` idéntico desde la fase red).
- [x] `dotnet build`: 0 warnings, 0 errores.

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests/`: 53 pass, 0 fail. Los 41 tests de slices previos siguen en verde.
- Application.Tests y Api.Tests fallan por Docker no disponible en el entorno — condición pre-existente, no es regresión de este slice.

### 2.7 Coherencia con decisiones previas

- [x] Consistente con §15.2 (shape de `Hallazgo`), §15.3 (I-H7, I-H8, I-H4) y §15.4 (IDs: `int?` para `ActividadId`, `TipoFallaId`, `CausaFallaId` — alineado con convención ERP).
- [x] ADR-008: spec menciona `X-Client-Command-Id` — se implementará en infra-wire (handler HTTP). No aplica en dominio.
- [x] No aplica ADR-003/ADR-006 (sin integración ERP), ADR-005 (sin SignalR), ADR-004 (sin catálogo).

### 2.8 Integración cross-team Sinco

No aplica en este slice.

### 2.9 SignalR / push

No aplica en este slice (spec §10 confirma: `HallazgoActualizado_v1` no está en el catálogo push de ADR-005).

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | Handler `ActualizarHallazgoHandler` + endpoint `PATCH /api/v1/inspecciones/{id}/hallazgos/{hid}` + test de integración HTTP→Postgres pendientes. Trabajo de infra-wire post-review. | `slices/2-actualizar-hallazgo/` — infra-wire | Orquestador completa infra-wire antes del commit. |
| 2 | followup | Cobertura de línea de `HallazgoEliminado_v1` es 60 % y `InspeccionFirmada_v1`/`InspeccionCancelada_v1` es 50-60 %. Son stubs de eventos cuya lógica completa llega en slices futuros (3 y FirmarInspeccion). Cobertura subirá orgánicamente. | `Inspecciones.Domain` | Registrar como deuda a revisar al cerrar slice 3 y FirmarInspeccion. |
| 3 | followup | `CapabilityRequeridaException` tiene line-rate 0 % — la excepción existe pero ningún test la ejerce. El handler `IniciarInspeccion` debería validar `TieneCapabilityEjecutarInspeccion` pero esa validación no está en el método de decisión del dominio (está diferida a ADR-002 tentativo). | `Excepciones.cs` | Registrar en FOLLOWUPS: cuando se resuelva ADR-002 (auth del host), agregar test que cubra CapabilityRequeridaException en IniciarInspeccion. |

---

## 4. Veredicto final

- [ ] approved
- [x] **approved-with-followups** — los tres followups se registran en `FOLLOWUPS.md`. El slice puede cerrarse y commitearse.
- [ ] request-changes
