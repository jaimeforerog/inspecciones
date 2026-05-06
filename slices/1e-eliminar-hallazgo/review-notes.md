# Review notes — Slice 1e — EliminarHallazgo

**Autor:** reviewer
**Fecha:** 2026-05-06
**Slice auditado:** `slices/1e-eliminar-hallazgo/`
**Veredicto:** `approved`

---

## 1. Resumen ejecutivo

El slice está limpio. Nueve de diez escenarios de la spec tienen test activo; el único skip (§6.7 PRE-D/I-H9) es estructuralmente correcto y está documentado conforme a la decisión de firma. El followup #21 está cerrado: `StreamConHallazgoEliminado()` usa `HallazgoEliminado_v1` real y el test `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` pasa en verde. `Apply(HallazgoEliminado_v1)` es puro, el rebuild test pasa, no hay setters en eventos, no hay `DateTime.UtcNow` ni primitivos pelados en el dominio. El refactor extrae `ObtenerHallazgoActivo` eliminando duplicación real. Cobertura de ramas del agregado `Inspeccion`: **97.77 %** — supera el umbral del 85 % con holgura. Sin hallazgos que bloqueen ni followups nuevos que abrir.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. §6.1 → test §6.1 activo; §6.2 → test §6.2 activo; §6.3 → §6.3 activo; §6.4 → §6.4 activo; §6.5 → §6.5 activo; §6.6 → §6.6 activo; §6.7 → `[Fact(Skip="I-H9: requiere slices de repuestos/adjuntos")]` conforme a decisión de firma; §6.8 → omitido correctamente como integración (handler + Marten) documentado en red-notes §6.8; §6.9 → test rebuild activo; §6.10 → skip levantado en `ActualizarHallazgoTests.cs` y pasando en verde (followup #21 cerrado).
- [x] Cada precondición tiene un test que la viola: PRE-A (§6.3, `*Firmada*`), PRE-B1 (§6.4), PRE-B2 (§6.5, `*{HallazgoG1}*`), PRE-C (§6.6, `*obligatorio*`), PRE-D (§6.7 skip con justificación estructural), PRE-F (§6.8 integración justificada).
- [x] Cada invariante tocada tiene test que la viola con referencia al código del modelo: I-H7 cubierto por §6.3 (nombre del test incluye `_I_H7`); I-F1 cubierta por el mismo §6.3 (inspección en estado `Firmada` es post-firma, condición descrita en spec §5); I-H9 cubierta por §6.7 `[Skip]` con referencia explícita en el nombre del test (`_I_H9`).
- [x] Nombres de tests son frases descriptivas en español con referencia a la precondición o invariante correspondiente. `EliminarHallazgo_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7`, `EliminarHallazgo_con_HallazgoId_ya_eliminado_lanza_HallazgoEliminadoException_PRE_B2`, etc.

### 2.2 Tests como documentación

- [x] Given/When/Then está estructuralmente visible en cada test mediante comentarios de sección y separación de bloques. El test §6.9 (rebuild) usa doble bloque `When` con comentario explícito — patrón correcto para tests de reproyección.
- [x] Cero mocks del dominio. Los streams de Given se construyen con eventos reales aplicados vía `Inspeccion.Reconstruir` a través de `CasoDeUso.EliminarHallazgo`. `StreamConHallazgoEliminado()` usa `HallazgoEliminado_v1` real desde este slice.
- [x] Eventos usados en `Given` son reales y plausibles. `HallazgoRegistradoEjemplo` usa `parteEquipoId=77`, coordenadas GPS no presentes en el stream base pero el fixture `UbicacionTipo()` (de slices anteriores) usa `(4.711m, -74.072m, 3.0m)` — coordenadas plausibles para Colombia (Bogotá). Sin valores `(0,0)` ni nonsense.

### 2.3 Implementación

- [x] Código de producción mínimo: todos los métodos y tipos públicos nuevos ejercidos por tests activos. `EliminarHallazgo.cs` (record comando), `HallazgoEliminado_v1.cs` (record evento), dos excepciones nuevas (`MotivoEliminacionVacioException`, `HallazgoTieneHijosActivosException`), y el método de decisión `EliminarHallazgo` + `Apply(HallazgoEliminado_v1)` + helper privado `ObtenerHallazgoActivo` — todos ejercidos por los 8 tests activos. `HallazgoTieneHijosActivosException` tiene `line-rate=0` por el skip §6.7 — aceptado como consecuencia documentada del prerrequisito de slices de repuestos/adjuntos.
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()` ni acceso a APIs del navegador en el dominio. `ahora` se inyecta como `DateTimeOffset` por parámetro; `TecnicoId` llega desde fuera del dominio como `string` opaco.
- [x] `HallazgoEliminado_v1` es `sealed record` — sin setters públicos. `EliminarHallazgo` es `sealed record` — conforme.
- [x] `UbicacionGps` usado en su tipo propio; `TipoFallaId`/`CausaFallaId` son `int?` (PKs del ERP conforme a §15.4); `MotivoEliminacion` es `string?` en el record `Hallazgo` — sin primitivos pelados para campos de dominio.
- [x] `Apply(HallazgoEliminado_v1)` puro: no lanza, no valida, no re-aplica invariantes. El `idx < 0` guard retorna silenciosamente — patrón establecido desde `Apply(HallazgoActualizado_v1)`, coherente y correcto para rebuild con gaps.
- [x] Rebuild test presente y activo (§6.9): reproyecta tres eventos sobre `Inspeccion.Reconstruir`, verifica `Eliminado=true`, `MotivoEliminacion="Registrado por error"`, `Origen`, `AccionRequerida`, `TipoFallaId`, `CausaFallaId` inalterados, `Estado=EnEjecucion`, y contribuyente `"rmartinez"`. Ningún `Apply` lanza durante la reproyección.
- [x] No hay handler implementado en este slice — atomicidad del `SaveChangesAsync` no aplica en esta fase; la infra-wire la garantizará conforme a spec §7.

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado `Inspeccion`: **97.77 %** — supera el umbral del 85 %. La única rama descubierta visible es el caso `idx < 0` en `Apply(HallazgoEliminado_v1)` (rebuild con gaps, intencional) y el constructor de `HallazgoTieneHijosActivosException` (skip §6.7). Ambas están justificadas.

### 2.5 Refactor

- [x] `refactor-notes.md` presente. Documenta un cambio aplicado (extracción de `ObtenerHallazgoActivo`) con tabla Before/After, justificación factual (duplicación real en dos métodos del mismo archivo), y consecuencia sobre el mensaje de `HallazgoEliminadoException` unificado a `"El hallazgo {id} está eliminado."` — verificado que ningún test aserda el texto exacto, solo `*{HallazgoG1}*` (GUID presente). Un candidato descartado justificado (`TieneHijosActivos` — abstracción especulativa sin implementación concreta).
- [x] Los tests no cambiaron de lógica entre green y refactor. La única modificación observable es que `ActualizarHallazgo` y `EliminarHallazgo` llaman a `ObtenerHallazgoActivo(...)` en lugar del bloque inline — comportamiento observable idéntico. Conteo final: 62 pass, 0 fail, 1 skip — igual que tras green.
- [x] `dotnet build --no-incremental -warnaserror` → 0 advertencias, 0 errores. Confirmado en refactor-notes §Verificación final.

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests` completo: **62 pass, 0 fail, 1 skip (§6.7 documentado)**. Sin regresiones en los 54 tests de slices 1a..1d. El test del followup #21 (`ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException`) pasa en verde — la implementación de `Apply(HallazgoEliminado_v1)` habilita el fixture `StreamConHallazgoEliminado()` correctamente.

### 2.7 Coherencia con decisiones previas

- [x] `HallazgoEliminado_v1` usa `DateTimeOffset EliminadoEn` — conforme a la convención vigente del CLAUDE.md y coherente con `HallazgoActualizado_v1.ActualizadoEn` e `InspeccionIniciada_v1.IniciadaEn`. La decisión de migrar de `DateTime` (modelo histórico §12.10.6) a `DateTimeOffset` está documentada en spec §3 nota sobre `DateTimeOffset`.
- [x] `MotivoEliminacion: string?` añadido al record `Hallazgo` — conforme a §15.2 del modelo. `Apply(HallazgoActualizado_v1)` actualizado para preservar el valor (`MotivoEliminacion = _hallazgos[idx].MotivoEliminacion` en el `with { ... }`).
- [x] Alineado con §15.3 (I-H7, I-H9) y §15.7 (I-F1): PRE-A cubre los tres con un único chequeo `Estado == EnEjecucion`.
- [x] Alineado con §15.4: evento `HallazgoEliminado_v1` es el evento #8 del catálogo MVP.
- [x] Alineado con ADR-002 (tentativo): `TecnicoId` llega como parámetro opaco del JWT, mock `"rmartinez"` consistente con slices anteriores hasta resolución del followup #14.
- [x] Alineado con ADR-008: spec §7 documenta el patrón `X-Client-Command-Id` con el mismo estado de followup #15 vigente. Sin regresión ni avance no autorizado.
- [x] §10 SignalR marcado "no aplica" con justificación — conforme.
- [x] §11 Adapters Sinco marcado "no aplica" con justificación — conforme.

### 2.8 Integración cross-team Sinco (si aplica)

No aplica. `EliminarHallazgo` es operación local al módulo. Documentado en spec §11.

### 2.9 SignalR / push (si aplica)

No aplica. Documentado en spec §10 con justificación: `HallazgoEliminado_v1` no está en el catálogo de eventos SignalR (ADR-005).

---

## 3. Hallazgos

No hay hallazgos bloqueantes ni followups nuevos. El followup #21 (cerrado) fue el único item pendiente de este slice y queda resuelto.

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| — | — | Sin hallazgos | — | — |

---

## 4. Veredicto final

- [x] **approved** — sin hallazgos. Followup #21 cerrado. El slice se cierra limpio.
- [ ] **approved-with-followups**
- [ ] **request-changes**

---

_El orquestador puede proceder al commit del slice y a la fase de infra-wire._