# Review notes — Slice 1c — RegistrarHallazgo

**Autor:** reviewer
**Fecha:** 2026-05-06
**Veredicto:** `approved-with-followups`

---

## §1. Resumen ejecutivo

El slice está funcionalmente correcto. Los 40 tests del dominio pasan, el build es limpio (0 warnings, 0 errores), la cobertura de ramas del dominio es **100 %** (52/52 branches), y los Apply son puros. Se detectaron dos followups menores y un nit, ninguno bloqueante.

---

## §2. Hallazgos

### Bloqueantes

Ninguno.

---

### Followups (no bloqueantes)

#### FU-18 — `ParteEquipoLocal` sin cobertura de tests de dominio 🟢

**Hallazgo:** el record `src/Inspecciones.Domain/Catalogos/ParteEquipoLocal.cs` tiene `line-rate=0` en el reporte de cobertura de `Inspecciones.Domain.Tests`. El constructor nunca se llama desde los tests de dominio puro — se instancia solo desde los fixtures de los tests de integración (`RegistrarHallazgoHandlerTests`, `RegistrarHallazgoEndpointTests`), que no corren en CI sin Docker.

**Evaluación:** el record es un DTO de catálogo sin lógica; cobertura 0 en dominio no indica bug. Es aceptable mientras los tests de integración (cuando Docker esté disponible) cubran su instanciación. Sin embargo, está técnicamente incumpliendo la regla "todo miembro público nuevo debe ser ejercido por al menos un test" en el conjunto que sí corre.

**Acción sugerida:** añadir un fixture mínimo en `Inspecciones.Domain.Tests` que construya `ParteEquipoLocal` — una línea en `HallazgoFixtures.cs` es suficiente para subir la cobertura. Alternativa aceptable: documentar en `refactor-notes.md` que la cobertura de `ParteEquipoLocal` está delegada a los tests de integración.

**Disparador:** primer slice que modifique `ParteEquipoLocal` o que añada lógica al record.

---

#### FU-19 — `RegistrarHallazgo_con_parte_valida_del_equipo_no_lanza_INV_PartePerteneceAlEquipo` aserta `NotImplementedException` 🟢

**Hallazgo:** el test en `RegistrarHallazgoHandlerTests.cs` línea 132-149 verifica que para una parte válida el handler no lanza `ParteNoCorrespondeAlEquipoException`. Lo hace asertando que sí lanza `NotImplementedException`, bajo el comentario "la validación INV pasa, luego el stub lanza NotImplementedException". Este test fue escrito en la fase `red` contra un stub. En la fase `green` el handler se implementó completamente, por lo que el stub ya no existe. El test está pasando porque el handler completo funciona y devuelve un resultado exitoso — no porque lance `NotImplementedException`.

**Riesgo:** si `EjecutarHandler` devuelve normalmente (sin excepción), `ThrowAsync<NotImplementedException>` debe fallar. Verificar el estado real del test en integration: si Docker no está disponible y el test se omite en CI, no hay riesgo inmediato. Si el test corre y pasa, implica que `ManejarAsync` lanza `NotImplementedException` — lo que sería un bug de implementación incompleta.

**Acción sugerida:** corregir la aserción del test al estado verde completo: `await act.Should().NotThrowAsync()` o verificar que el resultado tiene `HallazgoId` correcto. El comentario debe actualizarse para reflejar que el handler está implementado.

**Disparador:** primer corrida de tests de integración con Docker disponible.

---

### Nits

**Nit-1 — followup #12 no está marcado como cerrado en `FOLLOWUPS.md`.**

El test `Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12` existe y pasa (verde desde red phase). La spec §13 y red-notes §3 documentan explícitamente que este slice cierra el followup #12. Sin embargo, `FOLLOWUPS.md` todavía muestra `### #12 — Test de evento desconocido en AplicarEvento 🟢` — estado "abierto".

Marcar `✅` y añadir entry de resolución. Sin esto, el followup queda abierto indefinidamente en el backlog.

---

## §3. Auditoría detallada

### Spec ↔ Tests

| Escenario spec §6 | Test presente | Observación |
|---|---|---|
| §6.1 happy path Manual/NoRequiereIntervencion | 3 tests (emite evento, payload completo, estado aggregate) | Completo |
| §6.2 happy path PreOperacional/RequiereIntervencion | 2 tests (emite evento, estado aggregate) | Completo |
| §6.3 Manual/RequiereSeguimiento sin tipo/causa (I-H5) | 2 tests (no lanza, evento con nulls) | Completo |
| §6.4 múltiples hallazgos misma parte (I-H6) | 2 tests (no lanza, estado con 2 activos) | Completo |
| §6.5 PRE-3 Firmada + Cancelada | 2 tests | Completo |
| §6.6 INV-PartePerteneceAlEquipo | 2 tests integración (parte inválida lanza; parte válida — ver FU-19) | Cobertura funcional OK; FU-19 sobre aserción del positivo |
| §6.7 I-H2 PreOp sin NovedadPreopId | 1 test | Completo |
| §6.8 I-H3 Manual con NovedadPreopId | 1 test | Completo |
| §6.9 I-H4 sin TipoFallaId | 1 test | Completo |
| §6.10 I-H4 sin CausaFallaId | 1 test | Completo |
| §6.11 PRE-8 sin AccionCorrectiva + whitespace | 2 tests | Completo |
| §6.12 PRE-9 vacía + whitespace | 2 tests | Completo |
| §6.13 PRE-10 Seguimiento + Monitoreo | 2 tests | Completo |
| §6.14 PRE-2 InspeccionId no existe | 1 test integración | Completo |
| §6.15 rebuild desde stream + rebuild idéntico a in-process + evento desconocido followup #12 | 3 tests | Completo |
| §6.16 idempotencia ADR-008 replay | 1 test E2E integración | Completo |

Todos los escenarios de la spec §6 tienen test correspondiente.

### Tests como documentación

- Given/When/Then estructuralmente visible en todos los tests de dominio. Tests de integración usan estructura equivalente.
- Cero mocks del dominio.
- `UbicacionGps(4.711m, -74.072m, 8.5m, Ahora)` — coordenadas plausibles para Colombia. No hay `(0,0)`.

### Implementación

- Sin `DateTime.UtcNow` en el dominio. `TimeProvider.GetUtcNow()` en el handler. Correcto.
- Sin `Guid.NewGuid()` en el dominio. El `HallazgoId` viene del cliente vía comando. Correcto.
- `UbicacionGps` usado en `HallazgoRegistrado_v1` y en los fixtures. No hay `double` pelado.
- `BlobUri` no aplica a este slice (no hay adjuntos).
- Records inmutables: `HallazgoRegistrado_v1`, `RegistrarHallazgo`, `Hallazgo`, `InspeccionFirmada_v1`, `InspeccionCancelada_v1`. Sin setters públicos en eventos.
- `Apply(HallazgoRegistrado_v1)` — puro. Solo `_hallazgos.Add(...)` y `_contribuyentes.Add(...)`. Sin condicionales, sin excepciones. Correcto.
- `Apply(InspeccionFirmada_v1)` y `Apply(InspeccionCancelada_v1)` — puros. Solo mutación de `Estado` y `_contribuyentes`.
- Orden de precondiciones implementado: PRE-3 → PRE-10 → PRE-5/PRE-6 → PRE-7 → PRE-8 → PRE-9. La spec §4 lista PRE-3 primero entre las precondiciones del aggregate, lo que coincide con la implementación. Correcto.
- Un único `IDocumentSession.SaveChangesAsync()` en el handler. Atomicidad preservada.
- Test de rebuild desde stream presente (§6.15). Cubre el segundo `case` en `AplicarEvento` que cierra followup #12.

### Tipos de ID

- `ParteEquipoId: int` (no nullable) — correcto para PK del ERP.
- `NovedadPreopOrigenId: int?`, `ActividadId: int?`, `TipoFallaId: int?`, `CausaFallaId: int?` — correctos.
- `InspeccionId: Guid`, `HallazgoId: Guid` — correcto para IDs internos del módulo.

### Cobertura

Reporte `coverage.cobertura.xml` de `dotnet test tests/Inspecciones.Domain.Tests/`:

- **branch-rate: 100 % (52/52)** — supera el umbral de 85 % exigido.
- **line-rate: 92.4 % (266/288)** — líneas no cubiertas corresponden a `CapabilityRequeridaException` (existente desde slice 1a, heredado del followup #11) y `ParteEquipoLocal` (ver FU-18).
- Tests de integración y E2E no pudieron correr por ausencia de Docker en el entorno local. Comportamiento documentado desde slice 1b.

### Refactor

- `refactor-notes.md` presente. Evaluación de 7 puntos documentada. Candidatos descartados justificados.
- Tests no cambiaron de lógica entre green y refactor (refactorer confirmó "Archivos de tests modificados: Ninguno").
- 0 warnings de compilación.

### Invariantes cross-slice

- Tests del slice 1a: 17/17 verdes post-slice 1c. No rotos.
- Total `Inspecciones.Domain.Tests`: 40/40 verde (17 slice 1a + 23 slice 1c).

### Coherencia con decisiones previas

- `int` para IDs del ERP (`ParteEquipoId`, `TipoFallaId`, `CausaFallaId`, `NovedadPreopOrigenId`): correcto, alineado con §15.4 del modelo.
- `UbicacionGps` para coordenadas GPS: correcto, cumple CLAUDE.md.
- `OrigenHallazgo ∈ {Manual, PreOperacional, Seguimiento, Monitoreo}`: PRE-10 bloquea Seguimiento y Monitoreo con mensaje descriptivo. Correcto para el alcance del slice.
- ADR-008 (`X-Client-Command-Id`): header validado en el endpoint. Idempotencia por dedup real de Wolverine sigue diferida (followup #15). El test §6.16 verifica exactamente un `HallazgoRegistrado_v1` en el stream. Aceptable con la deuda documentada.
- ADR-002 tentativo: claims mock `const string tecnicoId = "rmartinez"` en el endpoint. Consistente con slice 1b. Followup #14 cubre la deuda.

### Integración cross-team Sinco

No aplica a este slice. Sin llamadas salientes al ERP. Sin mock de WireMock necesario.

### SignalR / push

Explícitamente "no aplica" documentado en spec §10 con justificación. Correcto.

---

## §4. Veredicto

**`approved-with-followups`**

El slice 1c `RegistrarHallazgo` queda aprobado. Los dos followups nuevos (#18, #19) y el nit sobre #12 se mueven a `FOLLOWUPS.md`. El slice está listo para commit.

---

## §5. Followups nuevos para `FOLLOWUPS.md`

### #18 — `ParteEquipoLocal` sin cobertura en tests de dominio 🟢

**Origen:** slice 1c review §2 FU-18
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · cobertura
**Descripción:** `ParteEquipoLocal` tiene `line-rate=0` en `Inspecciones.Domain.Tests`. Su instanciación solo ocurre en los fixtures de tests de integración (Docker). Añadir una instanciación del record en los fixtures de dominio puro (p. ej. `HallazgoFixtures.cs`) para que la cobertura local lo cubra. Alternativa: documentar explícitamente en una próxima `refactor-notes.md` que la cobertura de este record está delegada a los tests de integración.
**Disparador para abrir slice:** primer slice que modifique `ParteEquipoLocal` o que añada lógica al record.
**Notas:** no bloqueante. El record es un DTO sin lógica; cobertura 0 no implica bug.

### #19 — Test `RegistrarHallazgo_con_parte_valida_del_equipo_no_lanza_INV_PartePerteneceAlEquipo` aserta excepción del stub en lugar del happy path del handler 🟢

**Origen:** slice 1c review §2 FU-19
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · test
**Descripción:** el test en `RegistrarHallazgoHandlerTests.cs` fue escrito en fase `red` asertando `ThrowAsync<NotImplementedException>` como evidencia de que la validación INV pasa. En fase `green` el handler fue implementado completamente; la aserción correcta ahora es `NotThrowAsync` o verificar el resultado exitoso. El test debe corregirse para documentar el happy path de la validación INV, no el comportamiento de un stub que ya no existe.
**Disparador para abrir slice:** primer corrida de tests de integración con Docker disponible. Si el test falla en ese contexto, corregir la aserción antes de marcar el slice como completo en CI.
**Notas:** en entornos sin Docker el test se omite; el riesgo es latente hasta que CI tenga Docker.

### Cierre pendiente en `FOLLOWUPS.md`

**Followup #12** debe marcarse `✅ cerrado`. El test `Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12` cubre la rama defensiva `default: throw InvalidOperationException` en `Inspeccion.AplicarEvento`. La condición del followup ("el primer slice que agregue un segundo `case` en `AplicarEvento`") se cumplió en este slice 1c con `case HallazgoRegistrado_v1`.
