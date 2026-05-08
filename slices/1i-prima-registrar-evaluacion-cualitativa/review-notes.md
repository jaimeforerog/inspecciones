# Review notes — Slice 1i' — RegistrarEvaluacionCualitativa

**Autor:** reviewer
**Fecha:** 2026-05-08
**Slice auditado:** `slices/1i-prima-registrar-evaluacion-cualitativa/`.
**Veredicto:** `approved`

---

## 1. Resumen ejecutivo

El slice implementa correctamente las ocho precondiciones del aggregate (PRE-3..PRE-8 + guard I-H1 + capas HTTP/handler), la emisión atómica de 1 o 2 eventos en orden causal, el rebuild desde stream, y el mapeo de errores HTTP. Build 0 warnings, 151 tests verdes, 4 skips por diseño (infra/Docker). Cobertura de ramas `Inspeccion`: **95.1 %** (umbral 85 %). No se encontraron blockers. La lección aprendida del slice 1i —la rama `ParteEquipoIdAusenteEnSnapshotException` sin test— está corregida: el escenario §6.11 la cubre con tests activos #20 y #21 (positivo y negativo).

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Todos los escenarios §6.1..§6.16 tienen al menos un test activo. §6.10 (PRE-2 handler), §6.12 (idempotencia Wolverine) y §6.15 (atomicidad Testcontainers) marcados Skip con razón documentada en `red-notes.md`. Justificación consistente con el patrón de todos los slices anteriores.
- [x] Cada precondición PRE-3..PRE-8 viola en un test. PRE-1 (capability) no implementada en el endpoint (claims mock hardcodeado — deuda pre-existente del módulo, documentada como followup #15 en slices anteriores). PRE-2 marcada Skip correctamente.
- [x] Guard I-H1 (`ParteEquipoIdAusenteEnSnapshotException`) tiene dos tests: test #20 verifica que se lanza cuando `Calificacion=Malo` y `ParteEquipoId=null`; test #21 verifica que NO se lanza cuando `Calificacion=Bueno` y `ParteEquipoId=null`. Rama negativa del guard cubierta. Este era el blocker del slice gemelo 1i — aquí está resuelto.
- [x] Invariantes I-M1, I-M2, I-M3, I-M4, I-M5b, I-M7 referenciadas en nombres de tests. Naming en español con código de invariante. PASS.
- [x] Escenario §6.13 (múltiples ítems Malo — I-H6) cubierto con tests #23 y #24. PASS.
- [x] Escenario §6.14 (sets `_itemsMedidos`/`_itemsEvaluados` independientes) cubierto con test #25. PASS.
- [x] Escenario §6.16 (rebuild desde stream obligatorio) cubierto con 3 tests: rebuild completo, rebuild sin hallazgo, y orden causal explícito (tests #27, #28, #29). PASS.

### 2.2 Tests como documentación

- [x] Given/When/Then estructuralmente visible con comentarios en todos los tests. PASS.
- [x] Cero mocks del dominio. Streams de Given construidos con eventos reales. PASS.
- [x] `UbicacionGps` con coordenadas plausibles para Colombia (`Latitud=4.711, Longitud=-74.072`) heredadas de `MonitoreoFixtures.UbicacionColombia()`. Coordenadas (0,0) ausentes. PASS.
- [x] Snapshots de ítems con datos realistas (partes, actividades, valores de rango). PASS.

### 2.3 Implementación

- [x] Código de producción mínimo: todos los miembros públicos nuevos son ejercidos por tests. `_itemsEvaluados`/`ItemsEvaluados`: cubiertos en tests §6.1, §6.3, §6.9. `ItemNoEsCualitativoException`: test #16. `ItemYaEvaluadoException`: tests #17, #18. `Apply(EvaluacionCualitativaRegistrada_v1)`: rebuild tests. PASS.
- [x] Sin `DateTime.UtcNow` en dominio. Grep confirmado: 0 ocurrencias en `src/Inspecciones.Domain/`. `ahora` proviene del handler vía `_time.GetUtcNow()`. PASS.
- [x] Sin `Guid.NewGuid()` en dominio. `HallazgoId` viene del comando (generado client-side). PASS.
- [x] IDs de tipo correcto: `ItemId: int`, `ParteEquipoId: int?`, `EvaluacionOrigenId: int?`, `InspeccionId: Guid`, `HallazgoId: Guid`. PASS.
- [x] `EvaluacionCualitativaRegistrada_v1` y extensión de `HallazgoRegistrado_v1` son `sealed record` inmutables sin setters públicos. PASS.
- [x] `Apply(EvaluacionCualitativaRegistrada_v1)` puro: solo `_itemsEvaluados.Add(e.ItemId)` + `_contribuyentes.Add(e.EmitidoPor)`. Sin validaciones. PASS.
- [x] `Apply(HallazgoRegistrado_v1)` extendido: propaga `EvaluacionOrigenId: e.EvaluacionOrigenId` en la construcción del record `Hallazgo`. Puro — sin lógica condicional ni excepciones. PASS.
- [x] Rebuild test presente (§6.16): tres tests. Estado verificado campo a campo incluyendo `EvaluacionOrigenId`, `MedicionOrigenId`, `Contribuyentes`. PASS.
- [x] Un único `SaveChangesAsync` en el handler (`RegistrarEvaluacionCualitativaHandler.cs`). PASS.
- [x] Orden causal correcto: `EvaluacionCualitativaRegistrada_v1` primero, `HallazgoRegistrado_v1` segundo. Verificado explícitamente en tests §6.3 y §6.16.
- [x] Guard I-H1 evalúa solo dentro del bloque `if (cmd.Calificacion == CalificacionCualitativa.Malo)`. No aplica para Bueno/Regular. Cubierto por test #21. PASS.

### 2.4 Cobertura

- [x] Cobertura de ramas del aggregate medida: **95.1 %** (`Inspeccion`). Supera el umbral de 85 %.
- [x] Ramas no cubiertas (4.9 %) son deuda de slices anteriores (1d, 1e, 1g — métodos `ActualizarHallazgo`, `EliminarHallazgo`, `Firmar` con ramas límite heredadas). Ninguna rama nueva introducida en este slice queda descubierta. PASS.
- [x] `ItemNoEsCualitativoException`: 100 % branches. `ItemYaEvaluadoException`: 100 % branches. PASS.

### 2.5 Refactor

- [x] `refactor-notes.md` presente y completo. Lista 3 cleanups aplicados + 5 refactors descartados con justificaciones.
- [x] Los 3 cambios del refactor son exclusivamente en archivos de producción (cleanups de docstrings). Los tests no se tocaron en lógica — solo cambios backward-compat de `EvaluacionOrigenId: null` en fixtures de slices anteriores, hechos en la fase red.
- [x] 0 warnings de compilación (`dotnet build` confirmado).
- [x] La decisión de no extraer helpers DRY está justificada: mensajes de error distintos en los guards compartidos, y la construcción de `HallazgoRegistrado_v1` difiere en 2 campos mutuamente excluyentes (`MedicionOrigenId` vs `EvaluacionOrigenId`). Justificación factual y aplicable. PASS.

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests/` completo: 151 pass, 4 skip, 0 fail. Los tests de slices 1a..1i siguen en verde. PASS.
- [n/a] `Application.Tests` y `Api.Tests` requieren Docker (Testcontainers) — comportamiento consistente con todos los slices anteriores; verificación en CI.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §12.11.5 puntos 3, 5, 6, 8`: enum `CalificacionCualitativa { Bueno, Regular, Malo }` correcto; solo `Malo` dispara `HallazgoRegistrado_v1`; `AccionRequerida=RequiereSeguimiento` hardcodeado; `EvaluacionOrigenId=ItemId` para trazabilidad.
- [x] P-3 (campo `EvaluacionOrigenId: int?` separado de `MedicionOrigenId: int?`) implementado correctamente. Backward compat: Marten deserializa como `null` para eventos históricos. PASS.
- [x] P-5 (`_itemsEvaluados` separado de `_itemsMedidos`) implementado y documentado en spec §3.4. PASS.
- [x] ADR-002: claims recibidos como parámetro del comando (`EmitidoPor`, `Capabilities`). El dominio no accede al HTTP context. PASS.
- [x] ADR-006 (outbox): un único `SaveChangesAsync` — atomicidad garantizada. PASS.
- [x] ADR-008: header `X-Client-Command-Id` validado en el endpoint. PASS.
- [x] Followup #20 (`ObservacionCampo` en record `Hallazgo`): el record `Hallazgo` sigue sin el campo (followup sigue abierto). La green-notes §4 dice "el red ya lo propagó" — esto es impreciso: `ObservacionCampo` está en el evento `HallazgoRegistrado_v1` y en la construcción del evento en `Inspeccion.cs:1064`, pero no en el record `Hallazgo` del state. La spec lo marcó explícitamente como opcional ("si el implementador lo decide"). El followup #20 permanece abierto. No es blocker.
- [x] Extensión backward-compat de `HallazgoRegistrado_v1` con `EvaluacionOrigenId: int?`: callers existentes (slices 1c, 1d, 1e, 1f, 1g, 1i) pasan `null` explícito. Fixtures de tests anteriores actualizados incidentalmente en la fase red. PASS.

### 2.8 Integración cross-team Sinco (si aplica)

- [n/a] El slice no consume ni publica hacia endpoints Sinco on-prem. Spec §11 lo confirma explícitamente.

### 2.9 SignalR / push (si aplica)

- [n/a] Spec §10 confirma que `EvaluacionCualitativaRegistrada_v1` y `HallazgoRegistrado_v1` derivado no generan push SignalR en este slice.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | nit | Green-notes §4 afirma "el red ya propagó `ObservacionCampo` desde el evento `HallazgoRegistrado_v1` al record `Hallazgo`". El campo sigue ausente de `Hallazgo.cs`. El spec lo marcó como opcional y el followup #20 permanece abierto — no hay bug, pero la nota es imprecisa para lectores futuros. | `slices/1i-prima-registrar-evaluacion-cualitativa/green-notes.md §4` | No acción requerida en este slice. El followup #20 en `FOLLOWUPS.md` es la referencia correcta. |

---

## 4. Veredicto final

- [x] **approved** — sin hallazgos bloqueantes. El único nit (#1) es cosmético en una nota interna. La lección del slice 1i (guard sin test) fue absorbida: tests #20 y #21 cubren la rama `ParteEquipoIdAusenteEnSnapshotException` con escenario positivo y negativo. Cobertura 95.1 % confirmada. 151 tests verdes. 0 warnings. Commit ready.
- [ ] approved-with-followups
- [ ] request-changes

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
