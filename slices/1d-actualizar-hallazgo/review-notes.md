# Review notes — Slice 1d — ActualizarHallazgo

**Autor:** reviewer
**Fecha:** 2026-05-06
**Slice auditado:** `slices/1d-actualizar-hallazgo/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice está bien ejecutado: 15 de 16 escenarios tienen test activo, el único skip (§6.7) es estructuralmente correcto y está debidamente documentado como prerrequisito de `EliminarHallazgo`. `Apply(HallazgoActualizado_v1)` es puro, el rebuild test pasa, no hay setters en eventos, no hay `DateTime.UtcNow` ni primitivos pelados en el dominio, y la cobertura de ramas del agregado es 97.56 %. Se identifican dos followups de baja criticidad que no bloquean el cierre del slice.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. §6.7 marcado `[Fact(Skip="...")]` con razón estructural documentada; §6.14 omitido correctamente como integración — ambos aceptados.
- [x] Cada precondición tiene un test que la viola: PRE-A (§6.5), PRE-B1 (§6.6), PRE-B2 (§6.7 skip), PRE-C (§6.8), PRE-D1 (§6.9, §6.10), PRE-D2 (§6.11), PRE-E (§6.12, §6.13). PRE-F (§6.14) integración justificada.
- [x] Cada invariante tocada tiene test que la viola: I-H7 cubierto por §6.5, I-H4 por §6.9/§6.10, I-H5 por §6.12/§6.13, I-H8 cubierto por §6.15 con reflexión sobre el tipo del evento.
- [x] Nombres de tests son frases descriptivas en español con referencia a la precondición o invariante correspondiente.

### 2.2 Tests como documentación

- [x] Given/When/Then está estructuralmente visible en cada test mediante comentarios de sección y separación de bloques.
- [x] Cero mocks del dominio. Los streams de Given se construyen con eventos reales aplicados vía `Inspeccion.Reconstruir`.
- [x] Coordenadas GPS usadas en los tests son plausibles para Colombia: `(4.711, -74.072)` (Bogotá aproximado) en `UbicacionTipo()` y `(4.800, -74.100)` en §6.3. Sin coordenadas `(0,0)`.

### 2.3 Implementación

- [x] Código de producción mínimo: todos los métodos y tipos públicos nuevos ejercidos por tests activos. `HallazgoEliminadoException` tiene cobertura line=0 por el skip §6.7 — aceptado como consecuencia documentada del prerrequisito de `EliminarHallazgo`.
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()` ni acceso a APIs del navegador en el dominio. `ahora` se inyecta como parámetro; `EmitidoPor` llega desde fuera.
- [x] `HallazgoActualizado_v1` es `sealed record` — sin setters públicos.
- [x] `UbicacionGps` usado en su tipo propio; `TipoFallaId`/`CausaFallaId` son `int?` (ERP IDs conforme a §15.4); sin primitivos pelados para coordenadas.
- [x] `Apply(HallazgoActualizado_v1)` puro: no lanza, no valida, no re-aplica invariantes. El caso `idx < 0` retorna silenciosamente con comentario explicativo correcto.
- [x] Rebuild test presente y activo (§6.16): reproyecta tres eventos sobre `Inspeccion.Reconstruir` y verifica estado resultante incluyendo campos inmutables.
- [x] No hay handler implementado en este slice — atomicidad del `SaveChangesAsync` no aplica en esta fase; la infra-wire la garantizará conforme a la spec §7.

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado `Inspeccion`: **97.56 %** — supera el umbral del 85 %. Ramas descubiertas (`HallazgoEliminadoException`, `CapabilityRequeridaException`, `ParteEquipoLocal`) están justificadas en followups previos (#11, #18).

### 2.5 Refactor

- [x] `refactor-notes.md` presente, cero cambios aplicados, con tres candidatos explícitamente descartados con justificación factual.
- [x] Los tests no se tocaron en la fase refactor.
- [x] `dotnet build` → 0 advertencias, 0 errores.

### 2.6 Invariantes cross-slice

- [x] `dotnet test` del proyecto de dominio: **54 pass, 1 skip, 0 errors** — sin regresiones en los 39 tests de slices previos.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15.3`: `HallazgoActualizado_v1` no contiene `Origen`, `ParteEquipoId`, `NovedadPreopOrigenId`, `SeguimientoOrigenId` — verificado contra el record de producción.
- [x] Alineado con `§15.4`: PKs del ERP (`TipoFallaId`, `CausaFallaId`) son `int?`; IDs internos (`InspeccionId`, `HallazgoId`) son `Guid`.
- [x] Alineado con ADR-002 (tentativo): `EmitidoPor` llega como parámetro desde fuera del dominio; mock `"ana.gomez"` consistente con slices anteriores hasta resolución de followup #14.
- [x] Alineado con ADR-008: spec §7 documenta el patrón `X-Client-Command-Id` con el mismo estado de followup #15 vigente desde slice 1c. Sin regresión ni avance no autorizado.
- [x] `ObservacionCampo` presente en el evento pero ausente del record `Hallazgo` del agregado — decisión deliberada documentada, aceptable para MVP (ver followup #20).

### 2.8 Integración cross-team Sinco (si aplica)

No aplica. `ActualizarHallazgo` es operación local. Documentado en spec §11.

### 2.9 SignalR / push (si aplica)

No aplica. Documentado en spec §10 con justificación.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `ObservacionCampo` existe en `HallazgoActualizado_v1` y en `ActualizarHallazgo` pero no en el record `Hallazgo` del agregado. `Apply(HallazgoActualizado_v1)` lo ignora silenciosamente. No es bug para el MVP (ninguna proyección activa lo consulta), pero el gap entre el evento y el state debe cerrarse antes del primer slice de proyecciones de detalle. | `Inspeccion.Apply(HallazgoActualizado_v1)`, `Hallazgo.cs` | Followup #20 — añadir `ObservacionCampo` al record `Hallazgo` y al `with { ... }` de `Apply` en el slice que implemente `DetalleInspeccionView`. |
| 2 | followup | El test §6.7 (`PRE-B2 — HallazgoId eliminado`) está en skip con el fixture `StreamConHallazgoRegistrado` (hallazgo activo). Al implementar `EliminarHallazgo`, el orquestador debe verificar activamente que el fixture se reemplace y el skip se levante como parte del DoD de ese slice. | `ActualizarHallazgoTests.cs:184-223` | Followup #21 — vinculado al DoD del slice `EliminarHallazgo`: verificar que §6.7 se activa con `StreamConHallazgoEliminado()` y pasa. |

---

## 4. Veredicto final

- [ ] **approved**
- [x] **approved-with-followups** — followups #20 y #21 registrados en `FOLLOWUPS.md`. El slice se cierra.
- [ ] **request-changes**

---

_El orquestador puede proceder al commit del slice y a la fase de infra-wire._
