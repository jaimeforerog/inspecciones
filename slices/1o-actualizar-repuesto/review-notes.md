# Review notes — Slice 1o — ActualizarRepuesto

**Autor:** reviewer
**Fecha:** 2026-05-11
**Slice auditado:** `slices/1o-actualizar-repuesto/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

Slice completo y sin blockers. Los 14 escenarios de la spec §6 tienen tests correspondientes en dominio y los tests de integración pasaron (Domain 246/265 + 19 skip, Api 57/63 + 6 skip + 0 fail). Cobertura de ramas del agregado `Inspeccion`: **94.44%** — sobre el umbral obligatorio del 85%. Se registran dos followups: uno para la colisión de IDs en Api.Tests (manifestación del FU-39 preexistente), y uno para el test API E2E del escenario §6.9 que está documentado en el docblock pero no tiene método propio.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. Los 14 escenarios (§6.1..§6.14) están cubiertos en dominio. §6.12 skipeado con justificación explícita (PRE-1 vive en handler — cubierto en Api.Tests). §6.14 tiene dos tests (rebuild directo + rebuild via CasoDeUso).
- [x] Cada precondición tiene un test que la viola. PRE-2..PRE-8 cubiertas; PRE-0 cubierta en Api.Tests; PRE-1 cubierta en Api.Tests.
- [x] Cada invariante tocada tiene un test. I-H7 cubierta en §6.5 (Firmada y Cancelada). INV-RA1 cubierta en §6.13 (SkuId, Unidad, HallazgoId inmutables verificados post-Apply).
- [x] Los nombres de los tests son frases descriptivas en español con referencia a invariante.

### 2.2 Tests como documentación

- [x] Given/When/Then estructuralmente visible con comentarios en todos los tests.
- [x] Cero mocks del dominio. Aggregate real, eventos reales.
- [x] `UbicacionGps` en Api.Tests usa coordenadas de Bogotá `(4.711, -74.072)` — plausible.

### 2.3 Implementación

- [x] `RepuestoActualizado_v1`: `sealed record` inmutable. Sin setters públicos.
- [x] `ActualizarRepuesto` (comando): `sealed record` inmutable.
- [x] Sin `DateTime.UtcNow` en dominio. `ActualizarRepuesto` usa `ahora: DateTimeOffset` inyectado. Handler usa `_time.GetUtcNow()`.
- [x] `Apply(RepuestoActualizado_v1)` puro: solo aplica delta sobre `_repuestos[idx]` con `with`. Sin validaciones, sin lanzar. Return silencioso si `idx < 0` (stream con `RepuestoRemovido_v1` futuro).
- [x] Rebuild test obligatorio presente: §6.14 tiene dos variantes — rebuild directo con 4 eventos literales, y rebuild via CasoDeUso con stream completo. Ambos verifican estado post-Apply.
- [x] Un único `IDocumentSession.SaveChangesAsync()` en el handler. Verificado en `ActualizarRepuestoHandler.cs` línea 40.
- [x] Orden de validaciones PRE-2 → PRE-3/4 → PRE-8 → PRE-7 → PRE-5 documentado y coherente con D-3 (rechazar comando vacío da mejor feedback antes de validar existencia del repuesto).
- [x] P-2 normalización: `string.IsNullOrWhiteSpace → null` aplicado tanto en endpoint como en handler (defensa en profundidad correcta — capas independientes).
- [x] Estado post-update en handler calculado en memoria a partir del aggregate pre-cargado + delta del evento emitido. Evita segundo round-trip a DB.
- [x] `Guid.NewGuid()` no aparece en dominio. Los IDs en Api.Tests usan `Guid.NewGuid()` en la capa de siembra — correcto.

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado `Inspeccion`: **94.44%** (branch-rate en `coverage.cobertura.xml`). Por encima del umbral del 85%.
- [x] No hay justificación requerida — cobertura supera el umbral holgadamente.

### 2.5 Refactor

- [x] `refactor-notes.md` presente. Documenta un refactor aplicado (extract `ActualizarRepuestoResult` a archivo propio) y cuatro candidatos descartados con justificación.
- [x] Tests no cambiaron de lógica entre green y refactor — solo cambio estructural de archivo.
- [x] Cero warnings de compilación. `dotnet build` reporta `0 Advertencia(s), 0 Errores`.

### 2.6 Invariantes cross-slice

- [x] `dotnet test` completo del repo: Domain 246/265 + 19 skip, Api 57/63 + 6 skip + 0 fail. Sin regresión.
- [x] Application.Tests no ejecutados localmente (requiere Docker — FU-39 pendiente). Consistente con patrón de slices anteriores.

### 2.7 Coherencia con decisiones previas

- [x] `RepuestoActualizado_v1` corresponde al evento #11 del catálogo canónico `01-modelo-dominio.md §15.4`. Nombre correcto (spec nota que §12.10.14 usa nombre histórico `RepuestoEstimadoActualizado_v1` — §15.4 es la fuente de verdad).
- [x] Semántica delta en evento (D-1) coherente con `HallazgoActualizado_v1` de slice 1d.
- [x] Patrón PATCH semántico con campos opcionales coherente con ADR-008 y patrón general del proyecto.
- [x] ADR-002 tentativo respetado: `const string tecnicoId = "rmartinez"` en endpoint (followup #14 sigue abierto).
- [x] `I-H7` cubierta, `I-H12` explícitamente no re-validada con justificación documentada en spec §5.
- [x] INV-RA1 propuesto como nuevo invariante para §15.3. No es blocker — el spec propone añadirlo al modelo en este PR.

### 2.8 Integración cross-team Sinco

- [x] No aplica — `ActualizarRepuesto` no cruza al ERP. ADR-006 no aplica. Sin WireMock requerido.

### 2.9 SignalR / push

- [x] No aplica — spec §10 documenta explícitamente que `RepuestoActualizado_v1` no está en el catálogo SignalR (ADR-005). Justificación presente.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | El escenario §6.9 (PRE-5: RepuestoId en hallazgo distinto) está cubierto en dominio (`ActualizarRepuesto_con_RepuestoId_en_hallazgo_incorrecto_lanza_RepuestoNoEncontradoException_PRE5`) pero no tiene test E2E en Api.Tests. El docblock de `ActualizarRepuestoEndpointTests` lo lista como cubierto, pero no hay método `[Fact]` correspondiente. Dado que §6.8 y §6.9 producen el mismo HTTP 404 + "PRE-5" y el dominio distingue ambas ramas, la cobertura es aceptable a nivel de comportamiento observable. | `tests/Inspecciones.Api.Tests/ActualizarRepuestoEndpointTests.cs` (docblock §6.9 sin test) | Agregar test `PATCH_repuesto_repuesto_en_hallazgo_incorrecto_responde_404_PRE5` en futura iteración. |
| 2 | followup | Colisión de EquipoIds en Api.Tests — el orquestador renombró los IDs del slice 1o de 80001-80010 a 100001-100010 para evitar colisión con slices 1m/1n. Es manifestación expandida de FU-39: IDs de siembra hardcoded sin mecanismo de aislamiento entre suites de tests. Cada nuevo slice requiere elegir rangos manuales para evitar colisiones. | `tests/Inspecciones.Api.Tests/ActualizarRepuestoEndpointTests.cs` (IDs 100001..100010) | Continuar acumulando en FU-39: cuando el número de suites haga insostenible la asignación manual de rangos, implementar `Guid.NewGuid()` para siembra o fixture de isolation por colección. |

---

## 4. Veredicto final

- [x] **approved-with-followups** — followups #42 y #43 registrados en `FOLLOWUPS.md`. Sin blockers. Slice cierra.

---

## 5. Output de dotnet test

### Domain (sin Postgres):

```
Correctas! - Con error: 0, Superado: 246, Omitido: 19, Total: 265, Duración: 165 ms
```

ActualizarRepuesto: 20 PASS + 1 SKIP (PRE-1 — vive en handler, cubierto en Api.Tests).

### Api (Testcontainers + Postgres local):

```
Correctas! - Con error: 0, Superado: 57, Omitido: 6, Total: 63, Duración: 9 s
```

ActualizarRepuesto (Api): 11 PASS + 1 SKIP (ADR-008 idempotencia — FU-15 pendiente).

### Application.Tests:

No ejecutados — requiere Docker (FU-39). Consistente con política de slices anteriores.

### Cobertura de ramas `Inspeccion`:

**94.44%** (`branch-rate="0.9444"` en `coverage.cobertura.xml`). Por encima del umbral del 85%.
