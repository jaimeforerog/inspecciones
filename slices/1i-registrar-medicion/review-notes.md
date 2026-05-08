# Review notes — Slice 1i — RegistrarMedicion

**Autor:** reviewer
**Fecha:** 2026-05-08
**Slice auditado:** `slices/1i-registrar-medicion/`.
**Veredicto:** `request-changes` — devuelto a **green** por un blocker de cobertura (rama de producción sin test).

---

## 1. Resumen ejecutivo

El slice implementa correctamente las seis precondiciones del aggregate (I-M1..I-M6), el cálculo de rango cerrado (P-2), la emisión atómica de 1 o 2 eventos en orden causal, el rebuild desde stream, y el mapeo de errores HTTP. Build en verde, 0 warnings. 124 tests pasan, 1 skip por diseño. Cobertura de ramas medida: **94.21 %**. Sin embargo, la excepción `ParteEquipoIdAusenteEnSnapshotException` introducida en este slice tiene su rama de lanzamiento (líneas 889-893 de `Inspeccion.cs`) **sin ningún test que la ejerza**: es código de producción en el camino de decisión del aggregate, no un helper ni un `Apply`. Esa omisión es un blocker bajo los criterios de la persona reviewer.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Todos los escenarios §6.1..§6.15 tienen al menos un test. §6.12 marcado Skip con razón documentada (requiere Wolverine). §6.14 en `Domain.Tests` Skip + test real en `Application.Tests`.
- [x] Cada precondición PRE-3..PRE-8 tiene un test que la viola (tests 11..16). PRE-2 en `Application.Tests` test 22. PRE-1 no implementada en la capa HTTP (consistent con patrón anterior, documentado en green-notes).
- [x] Invariantes I-M1..I-M6 referenciadas en nombres de tests. I-H6 cubierto en test 18/19. I-H1 cubierto implícitamente en tests §6.2/§6.6 pero la rama de violación (snapshot sin `ParteEquipoId`) **no tiene test** — ver hallazgo #1.
- [x] Nombres de tests son frases descriptivas en español con código de invariante. PASS.

### 2.2 Tests como documentación

- [x] Given/When/Then estructuralmente visible en todos los tests (comentarios explícitos).
- [x] Cero mocks del dominio. Los streams de Given se construyen con eventos reales de fixtures.
- [x] `UbicacionGps` con coordenadas plausibles para Colombia (`Latitud=4.711, Longitud=-74.072`) en `MedicionFixtures`. PASS.
- [x] `ItemMonitoreoOmitido_v1` en `StreamMonitoreoConItemOmitido` construido con datos realistas (`MotivoOmision` no vacío, timestamp posterior al inicio). PASS.

### 2.3 Implementación

- [x] Código de producción mínimo: todos los miembros públicos nuevos son ejercidos por tests. Excepción: rama `snapshot.ParteEquipoId is null` — ver hallazgo #1.
- [x] Sin `DateTime.UtcNow` en dominio. El aggregate recibe `ahora` como parámetro desde el handler (que usa `_time.GetUtcNow()`). PASS.
- [x] Sin `Guid.NewGuid()` en dominio. `HallazgoId` viene del comando. PASS.
- [x] IDs de tipo correcto: `ItemId: int`, `ParteEquipoId: int?`, `InspeccionId: Guid`, `HallazgoId: Guid`. PASS.
- [x] `MedicionRegistrada_v1` y `HallazgoRegistrado_v1` son `sealed record` inmutables sin setters públicos. PASS.
- [x] `Apply(MedicionRegistrada_v1)` puro: solo `_itemsMedidos.Add` + `_contribuyentes.Add`. Sin validaciones. PASS.
- [x] `Apply(HallazgoRegistrado_v1)` extendido con `MedicionOrigenId`: puro, solo mutación de state. PASS.
- [x] `Apply(ItemMonitoreoOmitido_v1)` puro: solo `_itemsOmitidos.Add` + `_contribuyentes.Add`. PASS.
- [x] Rebuild test presente (§6.15): dos tests, uno fuera de rango y uno dentro. Estado verificado campo a campo. PASS.
- [x] Un único `SaveChangesAsync` en el handler. PASS.
- [x] Orden causal correcto: `MedicionRegistrada_v1` primero, `HallazgoRegistrado_v1` segundo. Verificado en tests §6.2 y §6.15.
- [ ] **FAIL — hallazgo #1:** rama `ParteEquipoId is null` introducida en `RegistrarMedicion` (Inspeccion.cs:889-893) sin test.
- [nit] Docstring de `Apply(MedicionRegistrada_v1)` en `Inspeccion.cs:939` dice "Stub mínimo fase red — el green completa la mutación de estado" — el refactorer limpió el docstring en `MedicionRegistrada_v1.cs` y en `RegistrarMedicionHandler.cs` pero dejó este comentario obsoleto en el `Apply`. No es blocker pero es inconsistente con el cleanup del refactor.

### 2.4 Cobertura

- [x] Cobertura de ramas del aggregate medida: **94.21 %** (124 branch lines en `Inspeccion.cs`).
- [x] Supera el umbral mínimo de 85 % globalmente.
- [ ] **Rama no cubierta introducida por este slice:** `snapshot.ParteEquipoId is null` (Inspeccion.cs:889). Esta rama es nueva en el 1i (no es deuda heredada) y representa una excepción de dominio (`ParteEquipoIdAusenteEnSnapshotException`) que no tiene escenario de test.
- [nit] Rama `string.IsNullOrEmpty(texto)` en `CapitalizarPrimera` (Inspeccion.cs:929-930) no cubierta. Es edge case de helper privado con input que el aggregate nunca produciría (magnitud vacía vendría de `MedicionEsperada` que no lo permite). Riesgo mínimo — documentar como nit.
- Las restantes líneas no cubiertas (502-503, 571-572, 728) son deuda de slices anteriores (1d, 1e, 1g) — no imputable al 1i.

### 2.5 Refactor

- [x] `refactor-notes.md` presente y completo. Lista 4 cambios aplicados + 5 refactors descartados con justificaciones.
- [x] Los tests no cambiaron de lógica entre green y refactor (solo cleanups de comentarios de infraestructura de desarrollo). Los 4 cambios del refactor son en archivos de producción exclusivamente.
- [x] 0 warnings de compilación (`dotnet build` confirmado).

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests/` completo: 124 pass, 1 skip, 0 fail. Los 103 tests de slices anteriores siguen en verde. PASS.
- [n/a] `Application.Tests` y `Api.Tests` requieren Docker (Testcontainers) — comportamiento consistente con todos los slices anteriores; verificación en CI.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §12.11.5 puntos 5, 6, 8`: `MedicionRegistrada_v1` con `DateTimeOffset` (P-3 aprobada), `HallazgoRegistrado_v1` con `Origen=Monitoreo` y `AccionRequerida=RequiereSeguimiento`, `MedicionOrigenId=ItemId`.
- [x] ADR-002: claims recibidos como parámetro del comando (`EmitidoPor`, `Capabilities`). El dominio no accede a HTTP context. PASS.
- [x] ADR-006 (outbox): un único `SaveChangesAsync` — atomicidad garantizada. PASS.
- [x] ADR-008: header `X-Client-Command-Id` validado en el endpoint. PASS.
- [x] PRE-10 de `RegistrarHallazgo` no modificada: `RegistrarMedicion` emite `HallazgoRegistrado_v1` directamente desde el aggregate sin invocar `RegistrarHallazgo`. Coherente con decisión de diseño §2 del spec. PASS.
- [x] Extensión backward-compat de `HallazgoRegistrado_v1` con `MedicionOrigenId: int?`: callers existentes (slices 1c, 1d, 1e, 1f, 1g) pasan `null` explícito. Tests de slices anteriores actualizados incidentalmente. PASS.
- [x] Followups #27 y #28 creados en `FOLLOWUPS.md` con descripción adecuada. Razonables como followups (no blockers). PASS.

### 2.8 Integración cross-team Sinco (si aplica)

- [n/a] El slice no consume ni publica hacia endpoints Sinco on-prem. Spec §11 lo confirma explícitamente. Sin llamadas al ERP en el handler.

### 2.9 SignalR / push (si aplica)

- [n/a] Spec §10 confirma que `MedicionRegistrada_v1` y `HallazgoRegistrado_v1` derivado no generan push SignalR en este slice.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | **blocker** | La rama `if (snapshot.ParteEquipoId is null)` en `RegistrarMedicion` lanza `ParteEquipoIdAusenteEnSnapshotException` (guard I-H1). Esta excepción es nueva en el slice 1i y no tiene ningún test que la ejerza. Es código de producción en el método de decisión — no un `Apply`, no un helper trivial. La spec §12 P-1 describe exactamente el escenario (`snapshot.ParteEquipoId is null` para streams del slice 1h sin el campo), y los tests del slice deben cubrir ese path. | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs:889-893` | Green agrega el escenario: `RegistrarMedicion_fuera_de_rango_con_snapshot_sin_ParteEquipoId_lanza_ParteEquipoIdAusenteEnSnapshotException` usando un `StreamMonitoreoConItems` donde `ItemsSnapshotSinParteEquipoId()` pone `ParteEquipoId: null` en el ítem 1 y el comando tiene `ValorMedido` fuera del rango. |
| 2 | followup | Docstring obsoleto en `Apply(MedicionRegistrada_v1)` (Inspeccion.cs:939): "Stub mínimo fase red — el green completa la mutación de estado". El refactor limpió los equivalentes en `MedicionRegistrada_v1.cs` y `RegistrarMedicionHandler.cs` pero olvidó este. | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs:939` | Eliminar la segunda oración del summary. Puede hacerse al mismo tiempo que la corrección del blocker #1. |
| 3 | nit | Rama vacía `string.IsNullOrEmpty(texto)` en `CapitalizarPrimera` (Inspeccion.cs:929-930) no cubierta. El método nunca recibe `""` en el flujo de producción (magnitud viene de `MedicionEsperada` que no acepta vacío). El riesgo es mínimo dado que es un helper privado estático. | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs:929-930` | No acción requerida. Si en un slice futuro `MedicionEsperada` permite magnitud vacía, agregar el test en ese momento. |

---

## 4. Veredicto final

- [ ] approved
- [x] **approved-with-followups**
- [ ] request-changes

**Followups existentes:** #27 y #28 en `FOLLOWUPS.md` (sin cambios respecto a la review anterior).

---

## 5. Re-review tras iteración correctiva — 2026-05-08

**Revisado por:** reviewer
**Fecha:** 2026-05-08

### Correcciones verificadas

**Blocker #1 cerrado.** Test `RegistrarMedicion_fuera_de_rango_con_ParteEquipoId_nulo_en_snapshot_lanza_ParteEquipoIdAusenteEnSnapshotException` añadido en `RegistrarMedicionTests.cs` (líneas 487-532). El test:
- Construye el stream Given inline con `InspeccionIniciada_v1` y `ItemsSnapshot` donde `ParteEquipoId: null` — escenario real, no fabricado.
- Ejecuta `RegistrarMedicion` con `ValorMedido=10.2m` (fuera del rango `[12.3, 12.5]`) — activa la rama del guard.
- Verifica `ParteEquipoIdAusenteEnSnapshotException` con mensaje que contiene `"*ParteEquipoId*"`. PASS.
- Given/When/Then estructuralmente visible. Sin mocks. Naming en español. PASS.

**Followup #2 (docstring) cerrado en la misma iteración.** `Inspeccion.cs:937-939` contiene ahora el docstring correcto: "Aplicación pura de `MedicionRegistrada_v1`: añade el ítem a `_itemsMedidos` y registra el contribuyente. Sin validaciones." Sin rastro de "Stub mínimo fase red". PASS.

### Cobertura re-medida

`dotnet test tests/Inspecciones.Domain.Tests/ --collect:"XPlat Code Coverage"` — 2026-05-08:

| Clase | Branch coverage |
|---|---|
| `Inspeccion` | **95.16 %** (subió de 94.21 %) |

La rama `Inspeccion.cs:889-893` ahora aparece cubierta. Todas las ramas introducidas en el slice 1i están ejercidas. Umbral ≥ 85 % cumplido con margen.

### Estado de tests

`dotnet test tests/Inspecciones.Domain.Tests/` — 125 pass, 1 skip (§6.14 — Testcontainers, por diseño), 0 fail. +1 test respecto a la baseline del review anterior (124 pass). Sin regresiones en los 103 tests de slices anteriores.

### Deuda nueva introducida

Ninguna. El test nuevo no modifica lógica de tests existentes. El cleanup del docstring es cosmético y no afecta compilación ni comportamiento.

### Veredicto

**approved-with-followups.** El único blocker de la review anterior está cerrado. Los followups #27 y #28 permanecen en `FOLLOWUPS.md` sin cambios (no son blockers). El slice queda cerrado.
