# Review notes — Slice 1n — DescartarNovedadPreop

**Autor:** reviewer
**Fecha:** 2026-05-11
**Slice auditado:** `slices/1n-descartar-novedad-preop/`.
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

Slice limpio. Los 13 tests de dominio y 7 tests activos de Api pasan en verde, 0 regresiones (226/244 Domain, 46/51 Api — 5 skips ADR-008). Cobertura de ramas del agregado `Inspeccion` en 94.73%, bien por encima del umbral de 85%. `Apply(NovedadPreopDescartada_v1)` es puro, handler tiene un único `SaveChangesAsync`, 0 warnings, build limpio. Dos followups menores que no bloquean el cierre.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente.
  - §6.1 → `DescartarNovedadPreop_en_inspeccion_en_ejecucion_emite_NovedadPreopDescartada_v1` + 2 tests derivados (payload + D-5).
  - §6.2 → `DescartarNovedadPreop_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_PRE2`.
  - §6.3 → `DescartarNovedadPreop_en_inspeccion_cancelada_lanza_InspeccionNoEnEjecucionException_PRE2`.
  - §6.4 → `DescartarNovedadPreop_novedad_ya_descartada_lanza_NovedadYaDescartadaException_PRE5` + borde distinto.
  - §6.5 → `DescartarNovedadPreop_novedad_ya_importada_como_hallazgo_lanza_NovedadYaConvertidaEnHallazgoException_PRE6` + borde.
  - §6.6 → Skip correcto (D-2 opción A — `_novedadesImportadas` no implementado por decisión de spec §12 P-1).
  - §6.7 → `POST_descartar_novedad_sin_capability_responde_403` (Api.Tests).
  - §6.8 → `POST_descartar_novedad_inspeccion_inexistente_responde_404` (Api.Tests).
  - §6.9 → `DescartarNovedadPreop_rebuild_desde_stream_reproduce_estado` + variante completa.
  - §6.10 → `DescartarNovedadPreop_motivo_autogenerado_sigue_plantilla_D4_exacta`.
- [x] Cada precondición tiene test que la viola: PRE-2 (×2: firmada y cancelada), PRE-5, PRE-6. PRE-1 cubierto en Api.Tests (404). PRE-4 cubierto en Api.Tests (403). PRE-7 skipeado con justificación D-2 correcta.
- [x] Invariantes tocadas tienen tests que las violan:
  - I2 / I4 / INV-ND1 verificadas por PRE-2 (×2), PRE-5, PRE-6 respectivamente.
  - I2b verificada en `DescartarNovedadPreop_agregado_al_set_de_contribuyentes_I2b`.
- [x] Nombres de tests: frases descriptivas completas en español, referencian la invariante o la precondición donde aplica.

### 2.2 Tests como documentación

- [x] Given/When/Then estructuralmente visible en todos los tests (comentarios explícitos `// Given`, `// When`, `// Then`).
- [x] Cero mocks del dominio. `Inspeccion.Reconstruir` sobre stream de eventos reales, sin stubs.
- [x] Eventos usados en `Given` son reales y con valores plausibles: `UbicacionGps(4.711m, -74.072m, 8.5m, ...)` (coordenadas de Bogotá), `NovedadId=9001/9002` (ints del ERP), timestamps con `DateTimeOffset` explícito.

### 2.3 Implementación

- [x] Código de producción mínimo: `NovedadPreopDescartada_v1` (record), `DescartarNovedadPreop` (record), excepciones `NovedadYaDescartadaException` + `NovedadYaConvertidaEnHallazgoException`, campo `_novedadesDescartadas: HashSet<int>`, `Apply(NovedadPreopDescartada_v1)`, `Descartar(...)`. Todo ejercido por tests.
- [x] Sin `DateTime.UtcNow` ni `Guid.NewGuid()` en dominio. Handler usa `_time.GetUtcNow()` (TimeProvider inyectado).
- [x] `NovedadPreopDescartada_v1` es `sealed record` inmutable, sin setters públicos.
- [x] No hay primitivos pelados: `NovedadId: int` (PK ERP, convención §15.4 — correcto, no es VO), `DescartadaPor: string` (userId opaco — correcto, sin BlobUri ni UbicacionGps involucrados en este comando).
- [x] `Apply(NovedadPreopDescartada_v1)` es puro: solo `_novedadesDescartadas.Add(e.NovedadId)` y `_contribuyentes.Add(e.DescartadaPor)`. Sin validaciones, sin throws.
- [x] Rebuild test presente (§6.9): `DescartarNovedadPreop_rebuild_desde_stream_reproduce_estado` reproyecta `[InspeccionIniciada_v1, NovedadPreopDescartada_v1]` sobre aggregate vacío y verifica estado, set de descartadas y contribuyentes. También hay variante que incluye el round-trip completo (previos + emitidos).
- [x] Handler: un único `IDocumentSession.SaveChangesAsync(ct)`. No hay save partido.

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado `Inspeccion` (clase): **94.73 %** (reporte Coverlet `coverage.cobertura.xml`, corrida 2026-05-11). Por encima del umbral de 85 %.
- [x] No hay ramas descubiertas del slice 1n propiamente. Las ramas sin cubrir son del aggregate en paths de slices previos ya documentados.

### 2.5 Refactor

- [x] `refactor-notes.md` presente. Documenta un cambio aplicado (eliminación de `<remarks>` falso residual del red) y tres refactors descartados con justificación.
- [x] Tests no cambiaron de lógica entre green y refactor. Solo se tocó código de producción.
- [x] Cero warnings de compilación. `TreatWarningsAsErrors=true` en todos los proyectos, build limpio.

### 2.6 Invariantes cross-slice

- [x] `dotnet test` completo del repo en verde: Domain 226/244 (18 skip), Api 46/51 (5 skip ADR-008). Cero regresiones en 213 tests previos.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15.4`: `NovedadPreopDescartada_v1` es exactamente el evento #9 del catálogo canónico. Shape idéntico al documentado.
- [x] Alineado con §15.9 (patrón de las 3 opciones): descarte sin hallazgo, motivo autogenerado, tap directo.
- [x] Alineado con ADR-002 (auth tentativo): handler recibe `DescartadaPor` como parámetro del cmd; el dominio no conoce JWT.
- [x] Alineado con ADR-006: la saga de integración P-6 es out-of-scope, documentada en §11 del spec.
- [x] Alineado con ADR-008: header `X-Client-Command-Id` verificado en endpoint, skip del test de replay correctamente justificado.
- [x] Decisiones D-1..D-7 documentadas y consistentes con el modelo. D-2 (opción A para PRE-7) tomada en el spec y respetada en la implementación.

### 2.8 Integración cross-team Sinco

- [x] La saga P-6 (`POST /preop/novedades/descartar`) está explícitamente fuera del scope (§11 del spec). No corresponde test contra WireMock en este slice. El patrón es consistente con slices 1k y 1l (GenerarOT, RechazarGenerarOT) donde la saga es slice separado.
- [x] `Idempotency-Key` para el POST hacia ERP no aplica en este slice (saga out-of-scope). La propuesta `{InspeccionId}-{NovedadId}` está documentada en §7 del spec para cuando se implemente.

### 2.9 SignalR / push

- [x] No aplica. §10 del spec marcado explícitamente "No aplica". `NovedadPreopDescartada_v1` no está en el catálogo ADR-005. Sin cambio en hub.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | P-3 del spec (simetría INV-ND1 en `RegistrarHallazgo`): el método de decisión `RegistrarHallazgo` no verifica que la novedad no haya sido descartada previamente antes de aceptar `Origen=PreOperacional`. El aggregate ahora tiene `_novedadesDescartadas: HashSet<int>` listo para consultarse, pero no se añadió el check en `RegistrarHallazgo` porque ningún test lo ejercía (regla anti-scope-creep correctamente aplicada). La invariante INV-ND1 queda asimétrica: el descarte bloquea importar-como-hallazgo, pero importar-como-hallazgo no bloquea un descarte posterior de la misma novedad (ese bloqueo sí existe vía PRE-6). La asimetría no introduce corrupción de estado en producción porque la UI previene el flujo, pero el backend no rechazaría una secuencia `HallazgoRegistrado_v1(NovedadId=X) → DescartarNovedadPreop(NovedadId=X)` si llegara directamente. Candidato a fix en slice 1c refactor o fix-FU separado. | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`, método `RegistrarHallazgo` | Añadir check `_novedadesDescartadas.Contains(cmd.NovedadPreopOrigenId)` en `RegistrarHallazgo` cuando `Origen=PreOperacional`, con test correspondiente en un fix-FU. No bloquea el cierre del slice 1n. |
| 2 | followup | INV-ND1 no está documentada formalmente en `01-modelo-dominio.md §15.3`. El spec §5 la propone para agregar en el mismo PR. Se recomienda agregar la entrada canónica antes del cierre de Fase 1 para que sea referenciable desde futuros tests. | `Inspecciones/docs/01-modelo-dominio.md §15.3` | Agregar entrada `INV-ND1` al catálogo de invariantes §15.3 del modelo. No bloquea el cierre del slice. |

---

## 4. Resultados de test

### Domain

```
Correctas! - Con error: 0, Superado: 226, Omitido: 18, Total: 244, Duración: 136 ms
```

Tests del slice 1n: 13 superados, 3 omitidos (PRE-7 D-2, PRE-4 capability capa HTTP, PRE-1 Marten).

### Api / E2E

```
Correctas! - Con error: 0, Superado: 46, Omitido: 5, Total: 51, Duración: 9 s
```

5 omitidos: 1 de slice 1n (idempotencia ADR-008) + 4 de slices previos (idempotencia ADR-008). Todos justificados.

### Build

```
Compilación correcta. 0 Advertencia(s), 0 Errores.
```

### Cobertura de ramas — aggregate `Inspeccion`

Rama: **94.73 %** (Coverlet XPlat, corrida 2026-05-11).

---

## 5. Veredicto final

- [ ] approved
- [x] **approved-with-followups** — followups #40 y #41 registrados en `FOLLOWUPS.md`.
- [ ] request-changes

Los dos followups son deuda de documentación y simetría de invariante; ninguno introduce corrupción de datos en producción. El slice puede cerrarse y hacer commit.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
