# Review notes — Slice 1j — OmitirItemMonitoreo

**Autor:** reviewer
**Fecha:** 2026-05-08
**Slice auditado:** `slices/1j-omitir-item-monitoreo/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice está bien ejecutado: todos los escenarios del spec están cubiertos por tests, `Apply(ItemMonitoreoOmitido_v1)` es puro, el rebuild test está presente y explícito, el build es limpio sin warnings, y los 167 tests del suite de dominio pasan sin regresiones. Se detecta un followup (no blocker) relacionado con el registro formal de las invariantes I-M8 e I-M9 en `01-modelo-dominio.md §15.3`: la spec §5 anuncia explícitamente que esas invariantes se añaden al catálogo del modelo, pero la incorporación no ocurrió en el documento real. El slice se cierra con `approved-with-followups`; el followup se mueve a `FOLLOWUPS.md`.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. Los escenarios §6.1..§6.14 están cubiertos: §6.1 → tests 1-2, §6.2 → test 3, §6.3 → test 4 (I-M9), §6.4 → test 5 (I-M8 medido), §6.5 → test 6 (I-M8 evaluado), §6.6 → tests 7-8 (PRE-3 vacío + whitespace), §6.7 → test 9 (PRE-4 longitud), §6.8 → test 10 (I-M1), §6.9 → test 11 (I-M3), §6.10 → test 12 (I-M2), §6.11 → test 13 [Skip legítimo: PRE-2 handler], §6.12 → test 14 [Skip legítimo: Wolverine dedup], §6.13 → tests 15-16, §6.14 → tests 17-18 (rebuild).
- [x] Cada precondición tiene al menos un test que la viola. PRE-3 (vacío), PRE-3 (whitespace), PRE-4 (5 chars), PRE-5/I-M1, PRE-6/I-M2, PRE-7/I-M3, PRE-8/I-M8 (medido), PRE-8/I-M8 (evaluado), PRE-9/I-M9: todos cubiertos con tests activos.
- [x] Las invariantes tocadas tienen tests que las violan con código de invariante referenciado en el nombre del test. Ejemplos: `OmitirItemMonitoreo_item_ya_omitido_lanza_ItemYaOmitidoException_I_M9`, `OmitirItemMonitoreo_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1`.
- [x] Los nombres de los tests son frases descriptivas en español con referencia a código de invariante cuando aplica.

### 2.2 Tests como documentación

- [x] Given/When/Then está estructuralmente visible en cada test mediante comentarios explícitos.
- [x] Cero mocks del dominio. Los streams de Given se construyen con eventos reales (`InspeccionIniciada_v1`, `MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1`, `ItemMonitoreoOmitido_v1`, `HallazgoRegistrado_v1`, `InspeccionFirmada_v1`).
- [x] Las coordenadas GPS usadas en los fixtures provienen de `UbicacionColombia()` de `MonitoreoFixtures`, no de valores nonsense como `(0,0)`. Coordenadas plausibles para Colombia confirmadas en slices previos.

### 2.3 Implementación

- [x] Código de producción mínimo. El único cambio en `src/` es la implementación de `Inspeccion.OmitirItem` (reemplaza el stub NotImplementedException). Todos los miembros nuevos (3 excepciones, 1 método de decisión, 1 record de comando, 1 record de evento) tienen cobertura por al menos un test activo.
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()` ni acceso a APIs del navegador en el dominio. `OmitidoEn` se recibe como parámetro `DateTimeOffset ahora` (inyección de TimeProvider en el handler).
- [x] `ItemMonitoreoOmitido_v1` es `sealed record` inmutable. `OmitirItemMonitoreo` (comando) también es `sealed record`. Sin setters públicos en ninguno de los dos.
- [x] `UbicacionGps` usado correctamente en los fixtures (no double pelado). `ItemId` es `int` (PK del ERP, conforme §15.4). `InspeccionId` es `Guid` (ID interno). Tipos correctos.
- [x] **`Apply(ItemMonitoreoOmitido_v1)` puro**: solo ejecuta `_itemsOmitidos.Add(e.ItemId)` y `_contribuyentes.Add(e.EmitidoPor)`. Sin validaciones, sin excepciones, sin re-aplicar invariantes. El método de decisión `OmitirItem` centraliza todas las precondiciones PRE-3..PRE-9. Verificado en `Inspeccion.cs` líneas 963-967.
- [x] **Rebuild test presente**: dos tests cubren el requisito. `OmitirItemMonitoreo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones` reconstruye el aggregate con `Inspeccion.Reconstruir(stream)` y verifica el estado completo. `OmitirItemMonitoreo_Apply_puro_no_lanza_al_reproyectar_solo_eventos_del_slice` verifica el rebuild acotado de §6.14 con eventos hardcodeados.
- [x] **Atomicidad del handler**: el slice emite exactamente un evento. El refactor-notes confirma build limpio y green-notes confirma que `Apply` ya existía del slice 1i — no hay segundo `SaveChangesAsync`. La lógica de `OmitirItem` devuelve un solo array `new object[] { evento }` (línea 1159 de `Inspeccion.cs`).

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado ≥ 85 %. No hay reporte de cobertura automatizado en este slice (refactor-notes no lo incluye). Verificación visual del método `OmitirItem` (líneas 1093-1159): 9 ramas de error (PRE-3 vacío, PRE-3 longitud, PRE-4, PRE-5, PRE-6, PRE-7 snapshot null, PRE-8 medido, PRE-8 evaluado, PRE-9), 1 rama happy path. Los 16 tests activos ejercen todas esas ramas sin excepción. Por continuidad con los slices previos (97.77% en 1i', 96.29% en 1f, etc.) y por cobertura visual completa de todas las ramas del método nuevo, la cobertura estimada del aggregate supera el umbral del 85 %.
- [ ] Cobertura numérica real no fue calculada en este slice. Seguimiento en el followup #29 abierto abajo.

### 2.5 Refactor

- [x] `refactor-notes.md` presente con 4 refactors descartados con justificación explícita y detallada.
- [x] Los tests no se tocaron entre la fase green y refactor. El output de `dotnet test` es idéntico en ambas notas (167 superado, 6 omitido).
- [x] Cero warnings de compilación. Confirmado en `refactor-notes.md §Output` y en la nota del build limpio.

### 2.6 Invariantes cross-slice

- [x] Suite completo verde: 167 tests superados, 0 con error, 6 omitidos (los mismos 6 Skips documentados de slices anteriores). Ninguna regresión en los 151 tests previos. Los cambios incidentales del slice (rename `MotivoOmision → Motivo`, `OmitidoPor → EmitidoPor` en `ItemMonitoreoOmitido_v1`) fueron propagados a todos los callers en fase red y verificados.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §12.11.5 punto 5` (shape de `ItemMonitoreoOmitido_v1`) y `punto 6` (tabla de triggers de hallazgo — la omisión no aparece, confirmando que nunca genera hallazgo). El campo `EmitidoPor` y el tipo `DateTimeOffset` para `OmitidoEn` aplican la misma corrección que los slices 1i e 1i' (alineación con convención del módulo sobre el modelo histórico).
- [x] ADR-002 (claims por parámetro): `OmitirItemMonitoreo` recibe `EmitidoPor: string` y `Capabilities` por parámetro. El dominio no conoce JWTs.
- [x] ADR-008 (`X-Client-Command-Id`): `refactor-notes.md` documenta la actualización del followup #27 de 8 a 9 instancias (endpoint `omitir` añadido).
- [x] ADR-004, ADR-005, ADR-006: no aplican a este slice (sin catálogos, sin SignalR, sin llamadas al ERP). §10 y §11 del spec lo confirman explícitamente.
- [x] `Apply` puro y rebuild test — convenciones CLAUDE.md cumplidas.
- [ ] **Followup (no blocker):** la spec §5 anuncia que I-M8 e I-M9 se añaden al catálogo §15.3 de `01-modelo-dominio.md`. La sección §15.3 del modelo es "Invariantes del Hallazgo" (I-H1..I-H12) y no es el lugar semántico correcto para invariantes de monitoreo. Además, la incorporación no ocurrió: el documento no contiene las cadenas `I-M8` ni `I-M9`. Los slices 1i e 1i' tampoco registraron I-M1..I-M7 en el modelo. La deuda de documentación es acumulativa desde slice 1i. Ver hallazgo #1.

### 2.8 Integración cross-team Sinco (no aplica)

El slice no consume ni publica hacia endpoints Sinco on-prem. Ninguna llamada HTTP saliente. ADR-006 e `Idempotency-Key` no aplican. Confirmado en spec §11 y en el código — `OmitirItem` trabaja exclusivamente con el aggregate cargado desde stream.

### 2.9 SignalR / push (no aplica)

`ItemMonitoreoOmitido_v1` no genera push según el catálogo de ADR-005 (`01-modelo-dominio.md §14`). Confirmado en spec §10: "la omisión es una operación local del técnico". Sin hub nuevo, sin suscripción nueva.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | Las invariantes I-M1..I-M9 del contexto Monitoreo no están registradas como sección canónica en `01-modelo-dominio.md §15`. La spec del slice 1j (y de los slices 1i, 1i' previos) referencia `§15.3` como destino, pero §15.3 es "Invariantes del Hallazgo" (I-H*). No existe una sección §15.X dedicada a invariantes de monitoreo. La deuda es acumulativa desde slice 1i — I-M1..I-M7 tampoco están en el modelo. | `Inspecciones/docs/01-modelo-dominio.md §15.3` | Abrir una nueva subsección `§15.7.x Invariantes de Monitoreo (I-M*)` o `§15.13` con el catálogo canónico I-M1..I-M9 y sus textos exactos definidos en las specs de slices 1i, 1i' y 1j. La incorporación es documental pura, sin cambio de código. |
| 2 | nit | El fixture `StreamMonitoreoConItemId3OmitidoItemId5Libre` en `OmitirItemMonitoreoFixtures.cs` línea 289 es un alias que delega en `StreamMonitoreoConItemId3YaOmitido()`. Esto es correcto solo porque ambas funciones tienen el mismo resultado: un stream con ItemId=3 omitido e ItemId=5 en snapshot. Sin embargo, el snapshot de `StreamMonitoreoConItemId3YaOmitido` incluye ItemId=5 (via `ItemsSnapshotOmision()`) lo cual es implícito para el lector del test §6.13. Un comentario que señale "ItemId=5 está disponible porque ItemsSnapshotOmision() lo incluye" mejoraría la legibilidad. | `OmitirItemMonitoreoFixtures.cs:289` | Añadir comentario inline. No bloquea. |

---

## 4. Veredicto final

- [ ] **approved**
- [x] **approved-with-followups** — followup #29 registrado en `FOLLOWUPS.md` (documentación de I-M1..I-M9 en el modelo de dominio). El nit #2 se asume sin followup propio — cobertura de código perfecta.
- [ ] **request-changes**

---

_Veredicto `approved-with-followups`. El orquestador puede proceder al commit del slice `feat(slice-1j): OmitirItemMonitoreo` y a la fase infra-wire. El followup #29 se registra en `FOLLOWUPS.md` antes del commit._
