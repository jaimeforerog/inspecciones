# Review notes — Slice 1h — IniciarInspeccionMonitoreo

**Autor:** reviewer
**Fecha:** 2026-05-07
**Slice auditado:** `slices/1h-iniciar-inspeccion-monitoreo/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

Slice limpio. El aggregate `Inspeccion` se extiende sin romper streams del 1b; `Apply(InspeccionIniciada_v1)` es puro y tolerante a nulls. Los 104 tests de dominio pasan; la cobertura de ramas del aggregate es **96.79 %** (por encima del umbral de 85 %). Se identificaron dos followups (#25 y #26) de severidad baja que no bloquean el cierre. Veredicto: `approved-with-followups`.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente — 14 escenarios, 17 tests, mapeo documentado en `red-notes.md §2`. §6.4 (idempotencia Wolverine envelope) no tiene test dedicado; justificado en `red-notes.md §5 punto 1` como responsabilidad del middleware Wolverine, no del handler. Aceptado.
- [x] Cada precondición tiene un test que la viola. PRE-3..PRE-9 cubiertos. PRE-1 y PRE-2 viven en capa HTTP (fuera del alcance de este slice, consistente con el patrón establecido desde slice 1b).
- [x] Cada invariante tocada (I-I1, I-I2 adaptada, I-I3, I-I-Mon-1, I-I-Mon-2) tiene un test que la viola con nombre que incluye la referencia al código.
- [x] Los nombres de los tests son frases completas en español con sufijo `_I_I3`, `_PRE_8`, etc.

### 2.2 Tests como documentación

- [x] Given/When/Then visible estructuralmente en todos los tests.
- [x] Cero mocks del dominio. Tests de dominio usan `CasoDeUso.IniciarMonitoreo` sobre el aggregate directamente; tests de integración usan Marten real.
- [x] GPS Colombia plausible (`Latitud=4.711, Longitud=-74.072, PrecisionMetros=8.5`) en `MonitoreoFixtures.UbicacionColombia()`. Sin coordenadas `(0,0)`.
- [x] `EquipoId=4521`, `RutinaMonitoreoId=42`, `GrupoMantenimientoId=7`, técnico `"ana.gomez"` — datos realistas para Colombia.

### 2.3 Implementación

- [x] Código de producción mínimo: todos los tipos y métodos públicos nuevos están ejercidos por al menos un test.
- [x] Sin `DateTime.UtcNow` en dominio — `IniciarMonitoreo` recibe `DateTimeOffset ahora` como parámetro; el handler usa `_time.GetUtcNow()` con `TimeProvider` inyectado.
- [x] Sin `Guid.NewGuid()` en dominio.
- [x] Records inmutables: `InspeccionIniciada_v1`, `ItemRutinaMonitoreoSnapshot`, `EvaluacionEsperada`, `MedicionEsperada`, `EvaluacionCualitativaEsperada`, `RutinaMonitoreoLocal`, `ItemRutinaMonitoreoLocal`, `IniciarInspeccionMonitoreo`, `IniciarInspeccionMonitoreoResult` — todos `sealed record`. Sin setters públicos.
- [x] `UbicacionGps` en `cmd.Ubicacion` — sin `double` pelado para coordenadas. Tipos de IDs correctos: `int` para `EquipoId`, `RutinaMonitoreoId`, `GrupoMantenimientoId`; `Guid` para `InspeccionId`. Conforme §15.4.
- [x] **`Apply(InspeccionIniciada_v1)` puro**: líneas 334-352 de `Inspeccion.cs` — solo asignaciones de campos, sin `if`, sin `throw`, sin re-validaciones. La tolerancia a null (`RutinaMonitoreoSeleccionadaId = e.RutinaMonitoreoSeleccionadaId`) es asignación pura.
- [x] **Rebuild test presente**: `IniciarMonitoreo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones` reproyecta el evento emitido por el happy path y verifica 7 campos del estado resultante. El test backward-compat `Apply_InspeccionIniciada_v1_con_campos_monitoreo_null_Tecnica_no_lanza` verifica que streams del 1b siguen funcionando.
- [x] **Atomicidad del handler**: un único `_session.Events.StartStream` seguido de un único `await _session.SaveChangesAsync(ct)` — líneas 93-97 de `IniciarInspeccionMonitoreoHandler.cs`.

### 2.4 Cobertura

- [x] Cobertura de ramas del aggregate `Inspeccion`: **96.79 %** (medida con `dotnet test --collect:"XPlat Code Coverage"` sobre `Inspecciones.Domain.Tests`, 104/104 verde). Por encima del umbral de 85 %.
- [x] Las ~3.21 % de ramas no cubiertas corresponden a ramas de métodos de slices anteriores (`Firmar`, `ActualizarHallazgo`) que ya tenían cobertura declarada en sus propios `refactor-notes.md`. No hay ramas nuevas del slice 1h sin cubrir.

### 2.5 Refactor

- [x] `refactor-notes.md` presente y claro: 1 fix aplicado (`Version: long → int`), 4 candidatos descartados con justificación, followups #23 y #24 abiertos con disparadores explícitos.
- [x] Los tests no cambiaron de lógica entre green y refactor — el único cambio es `Version: long → int` en el record de resultado, que no altera ningún test (el campo no tenía aserción de tipo).
- [x] Cero warnings de compilación (`0 Advertencia(s)` con `TreatWarningsAsErrors=true` activo).

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests` completo: 104/104 verde. Los 23 tests de `IniciarInspeccion` (slices 1a-1b) siguen pasando con el evento extendido.
- [x] `dotnet test tests/Inspecciones.Api.Tests` y `tests/Inspecciones.Application.Tests`: fallan por Docker no disponible localmente — comportamiento preexistente e idéntico desde slice 1b. No atribuible a este slice.

### 2.7 Coherencia con decisiones previas

- [x] P-1 (D1): `EvaluacionEsperada` abstract record + `MedicionEsperada` + `EvaluacionCualitativaEsperada` — sin enum `ItemTipo`. Conforme.
- [x] P-2 (D2): `ItemRutinaMonitoreoSnapshot` en `Inspecciones.Domain/Inspecciones/` — namespace `Inspecciones.Domain.Inspecciones`. Conforme.
- [x] P-3 (D3): PRE-6 lanza `EquipoSinRutinasMonitoreoException` antes de delegar al aggregate. Escenario §6.8 cubierto.
- [x] P-4 (D4): filtro `i.Activo` en handler (`IniciarInspeccionMonitoreoHandler.cs:72`), no en dominio. Conforme.
- [x] P-5 (D5): I-I1 aplica sin distinción de tipo — `InspeccionAbiertaPorEquipoView` tiene PK `EquipoId`; cualquier inspección activa (Tecnica o Monitoreo) bloquea. Escenario §6.2 cubierto en integración.
- [x] P-6 (D6): `InspeccionIniciada_v1` extendido (no `_v2`); nuevos campos con `default null`; backward compat verificado por test.
- [x] `RutinaMonitoreoSeleccionadaId: int?` e `ItemsSnapshot: IReadOnlyList<ItemRutinaMonitoreoSnapshot>?` con tipos correctos (§15.4).
- [x] ADR-006: no aplica — el slice no invoca ERP.
- [x] ADR-005: no aplica — no hay push SignalR en `IniciarInspeccion`.
- [x] ADR-008: `clientCommandId` viaja como `Capabilities` en el comando (campo presente) — la idempotencia real de Wolverine es followup #15 preexistente.

### 2.8 Integración cross-team Sinco (si aplica)

No aplica: este slice no consume ni publica hacia endpoints ERP on-prem. Catálogos `EquipoLocal` y `RutinaMonitoreoLocal` se pueblan directamente vía `session.Store(...)` en tests. Sin endpoints Sinco que mockear con WireMock.

### 2.9 SignalR / push (si aplica)

No aplica explícitamente documentado en spec §10: "No aplica en este slice."

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | El test de integración §6.1 no verifica `fila.Tipo == TipoInspeccion.Monitoreo` en `InspeccionAbiertaPorEquipoView`. La spec §6.1 lo requiere ("InspeccionAbiertaPorEquipoView tiene fila con ... Tipo=Monitoreo"). La proyección es correcta (`InspeccionAbiertaPorEquipoProjection.cs:42` proyecta `Tipo: e.Tipo`), pero el test no lo aserta. | `IniciarInspeccionMonitoreoHandlerTests.cs:134-137` (§6.1 Then bloque b) | Añadir `fila.Tipo.Should().Be(TipoInspeccion.Monitoreo)` al verificar la proyección en el test §6.1. Registrado como #25. |
| 2 | followup | `IniciarMonitoreo` en el aggregate usa `cmd.IniciadaPor` para `TecnicoIniciador` (línea 179), mientras `Iniciar` (slice 1b) usa `claims.TecnicoIniciador` (línea 126). Son semánticamente equivalentes (el handler construye `ClaimsTecnico.TecnicoIniciador = cmd.IniciadaPor`), pero la asimetría rompe la coherencia interna del aggregate: los métodos hermanos extraen el identificador del técnico de fuentes distintas. Si se cambia la forma en que el handler construye `ClaimsTecnico`, podría divergir silenciosamente. | `Inspeccion.cs:126` vs `Inspeccion.cs:179` | Unificar a `claims.TecnicoIniciador` en `IniciarMonitoreo` para mantener la invariante de que el aggregate siempre lee el técnico desde los claims. Registrado como #26. |
| 3 | nit | Docstring de `ItemRutinaMonitoreoSnapshot.cs` dice "Stub mínimo fase red" — el stub fue implementado en green. Comentario residual. | `ItemRutinaMonitoreoSnapshot.cs:8` | Eliminar la mención "Stub mínimo fase red" del docstring (igual al cleanup de `IniciarInspeccionMonitoreoResult.cs` hecho en refactor). No abre followup. |
| 4 | nit | El record `EvaluacionCualitativaEsperada` no tiene parámetros ni docstring — correcto por diseño, pero el docstring dice "Stub mínimo fase red". | `EvaluacionEsperada.cs:24` | Eliminar la mención "Stub mínimo fase red". No abre followup. |

---

## 4. Veredicto final

- [ ] **approved** — sin hallazgos, o solo nits asumidos.
- [x] **approved-with-followups** — followups #25 y #26 registrados en `FOLLOWUPS.md`.
- [ ] **request-changes** — se devuelve a **{red | green | refactorer}** con los blockers detallados.

Followups nuevos: **#25** (aserción `fila.Tipo` en test §6.1 de integración) y **#26** (unificar fuente de `TecnicoIniciador` en `IniciarMonitoreo`). Ambos son deuda de baja criticidad que no afectan la corrección del slice.

Datos de auditoría:
- Build: limpio — 0 warnings, 0 errors (`TreatWarningsAsErrors=true`)
- Tests dominio puro: 104/104 verde
- Tests Docker-dependientes: preexistente sin Docker — idéntico a slices 1b..1g
- Cobertura de ramas `Inspeccion`: **96.79 %** (umbral 85 %)

---

_El orquestador puede proceder al commit del slice `feat(slice-1h): IniciarInspeccionMonitoreo` y a la fase de infra-wire._
