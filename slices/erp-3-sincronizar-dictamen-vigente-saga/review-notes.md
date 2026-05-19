# Review notes — Slice erp-3 — SincronizarDictamenVigenteSaga

**Autor:** reviewer
**Fecha:** 2026-05-19
**Slice auditado:** `slices/erp-3-sincronizar-dictamen-vigente-saga/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El listener `SincronizarDictamenVigenteListener` cubre los 11 escenarios del spec (9 de la spec base + los 3 happy paths de mapeo de dictamen como tests distintos), la implementación es mínima y correcta, y no toca ningún aggregate. Se detecta un gap entre la spec (PRE-L1 → dead-letter inmediato) y la política de Wolverine registrada en `Program.cs`: `InvalidOperationException` no tiene `OnException<T>().MoveToErrorQueue()`, por lo que en producción Wolverine reintentaría PRE-L1 con backoff hasta agotar intentos antes de dead-letter — comportamiento distinto al "inmediato" especificado. Este gap es followup (FU-44), no blocker, porque: (a) el efecto final es el mismo (dead-letter), solo el camino difiere; (b) la separación listener/política es intencional en ADR-006 y el spec documenta que la política correcta se declara en `WolverineOptions`. Sin otros blockers. Suite Infrastructure 36/36 verde; Application.Tests falla por Docker ausente (preexistente, FU-39).

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. 11 tests cubren los 11 escenarios (§6.1–§6.11). La fusión de §6.5/§6.6 en dos tests separados pero coherentes con la arquitectura de test directa (sin Wolverine host) está documentada en red-notes §4 y es válida.
- [x] Cada precondición tiene un test que la viola: PRE-L1 (aggregate nulo) → §6.9; PRE-L1 (Dictamen nulo) → §6.10; PRE-L3 (dictamen no mapeable) → §6.11.
- [x] Cada invariante de integración (INV-L3, INV-L4) referenciada en los nombres de test con código preciso.
- [x] Los nombres de los tests son frases descriptivas en español con código de invariante/precondición cuando aplica.

### 2.2 Tests como documentación

- [x] Given/When/Then visible estructuralmente en cada test (comentarios y bloques separados).
- [x] `FakeInspeccionReader` usa `Inspeccion.Reconstruir(stream)` con eventos reales — sin bypasear lógica de dominio. El fake no es un mock del dominio; es un doble de infraestructura.
- [x] `AgregateCon` construye el stream con eventos reales en orden causal correcto (`InspeccionIniciada_v1` → `DiagnosticoEmitido_v1` → `DictamenEstablecido_v1` → `InspeccionFirmada_v1`). El `DiagnosticoEmitido_v1` en `AgregateCon` no es un requisito del aggregate para el listener (el listener solo necesita `Dictamen` y `EquipoId`), pero su presencia en el fixture no viola ninguna regla.
- [x] `UbicacionGps` con coordenadas plausibles para Colombia (Lat=4.711, Lon=-74.072) — no `(0,0)`.
- [x] Sin mocks del dominio.

### 2.3 Implementación

- [x] Código de producción mínimo: `SincronizarDictamenVigenteListener` (HandleAsync + MapearDictamen + LogSyncFallida), `IInspeccionReader` (interfaz), `MartenInspeccionReader` (adapter). Todo ejercido por al menos 1 test excepto `MartenInspeccionReader` (ver §2.3 nota abajo).
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()`, ni acceso a APIs del navegador en el listener.
- [x] `DictamenVigenteErpSyncFallida_v1` declarado como `sealed record` inmutable. Correcto.
- [x] `Apply` no aplica — slice no toca el aggregate.
- [x] Rebuild test no aplica — slice no emite eventos de dominio.
- [x] Atomicidad no aplica — slice no tiene `SaveChangesAsync`.
- [x] `LogLevel.Error` en `LogSyncFallida` (corregido en refactor #2 desde `Critical`). Consistente con spec §5 INV-L2 y patrón erp-2.

**Nota sobre `MartenInspeccionReader`:** el adapter de producción se implementó en refactor (decisión documentada en green-notes §2 y refactor-notes #4). No tiene tests propios en este slice — la delegación a `IQuerySession.Events.AggregateStreamAsync<Inspeccion>` es un wrapper de una línea sin ramas propias, y el comportamiento de null cuando el stream no existe lo maneja el listener (PRE-L1). Registrado como FU-45.

### 2.4 Cobertura

- [x] Ramas del listener documentadas en refactor-notes:

| Rama | Cubierta |
|---|---|
| `aggregate is null` (PRE-L1) | Sí — §6.9 |
| `aggregate.Dictamen is null` (PRE-L1 corrupto) | Sí — §6.10 |
| `MapearDictamen` PuedeOperar→0 | Sí — §6.1 |
| `MapearDictamen` ConRestriccion→1 | Sí — §6.2 |
| `MapearDictamen` NoPuedeOperar→2 | Sí — §6.3 |
| `MapearDictamen` valor no mapeado (PRE-L3) | Sí — §6.11 |
| 200 OK (éxito) | Sí — §6.1, §6.2, §6.3 |
| `MaquinariaErpException` con StatusCode | Sí — §6.5, §6.6, §6.7, §6.8 |
| `MaquinariaErpException` sin StatusCode (`!ex.StatusCode.HasValue`) | Rama defensiva muerta — no testeada, documentada en refactor-notes §ramas. |

- [x] Cobertura efectiva: 8/9 ramas vivas (la rama `!ex.StatusCode.HasValue` es defensiva muerta, idéntica al caso erp-2). Porcentaje efectivo ≥ 88 % — supera el umbral 85 %.
- [x] Rama defensiva muerta documentada en `refactor-notes.md` con justificación.

### 2.5 Refactor

- [x] `refactor-notes.md` presente con 4 cambios documentados (rename, log level fix, cleanup comments, nuevo adapter + DI).
- [x] Los tests no cambiaron de lógica entre green y refactor (solo 3 referencias de nombre ajustadas por el rename del listener — cambio cosmético documentado en refactor #1).
- [x] Cero warnings de compilación (confirmado en refactor-notes: "Compilación correcta. 0 Advertencia(s), 0 Errores.").

### 2.6 Invariantes cross-slice

- [x] Infrastructure.Tests 36/36 verde (confirmado — `dotnet test tests/Inspecciones.Infrastructure.Tests/`).
- [x] Application.Tests falla con Docker ausente — preexistente (FU-39), no causado por este slice. Las 40 fallas son idénticas al patrón de slices 1m, 1n, 1o.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15.4` — `InspeccionFirmada_v1` y `DictamenEstablecido_v1` son eventos del catálogo. El listener los consume correctamente.
- [x] ADR-006 §16 — outbox + retry para POSTs hacia ERP. El listener propaga excepciones para que Wolverine gestione el backoff. Política 5xx declarada en `Program.cs` (4 reintentos con backoff ADR-006). Política 4xx declarada (dead-letter inmediato INV-L3).
- [x] ADR-001 (REST/VPN) — M-W-1 es PUT REST hacia Maquinaria_V4. Correcto.
- [x] Naming en español para dominio, inglés para plumbing. Correcto (`SincronizarDictamenVigenteListener` — inglés para el listener de infraestructura; `DictamenOperacion`, `MapearDictamen` — dominio en español).
- [x] Rename "Saga" → sin "Saga" documentado y justificado en refactor-notes §decisión. Correcto: Saga en DDD implica estado compensable; este listener no tiene estado.

### 2.8 Integración cross-team Sinco

- [x] Endpoint M-W-1 stubbado con WireMock.Net. Slice marcado con estado 🚧 (endpoint no existe aún en Maquinaria_V4) — consistente con criterio `🟡 mock-only` del checklist.
- [x] Sin `Idempotency-Key` formal en el body — justificado por naturaleza last-write-wins de M-W-1. D-2 (versioning/timestamp) documentado como pendiente de confirmar con David. Aceptable para MVP.

### 2.9 SignalR / push

No aplica — slice no emite notificaciones push. Justificado en spec §10.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `InvalidOperationException` (PRE-L1: aggregate nulo o Dictamen nulo) no tiene política `OnException<InvalidOperationException>().MoveToErrorQueue()` en `Program.cs`. La política existente para `ArgumentException` cubre `ArgumentOutOfRangeException` (PRE-L3) correctamente por herencia, pero `InvalidOperationException` no es subclase de `ArgumentException`. En producción, Wolverine aplicará comportamiento default (posiblemente retry con backoff hasta agotar intentos antes de dead-letter), no "dead-letter inmediato" como especifica PRE-L1. El efecto final es el mismo (dead-letter), pero el camino difiere de la spec. | `src/Inspecciones.Api/Program.cs:105-109` | Añadir `opts.Policies.OnException<InvalidOperationException>().MoveToErrorQueue()` en el bloque de políticas de Wolverine. Verificar que no colisiona con otros handlers que lanzan `InvalidOperationException` por razones retriables. Registrado como FU-44. |
| 2 | followup | `MartenInspeccionReader` no tiene tests propios — ejercicio del adapter de producción depende de tests de integración con Marten real (no incluidos en este slice, decisión Opción B). El adapter es un wrapper de una línea; el riesgo de bug es mínimo, pero la cobertura del path producción→Marten queda a cero en este slice. | `src/Inspecciones.Infrastructure/Erp/MartenInspeccionReader.cs` | Añadir un test de integración (Testcontainers Postgres + Marten) en un slice de infraestructura o test E2E que verifique que `AggregateStreamAsync<Inspeccion>` devuelve null para stream inexistente y el aggregate correcto para stream existente. Registrado como FU-45. |
| 3 | nit | `intentosAgotados: 1` hardcodeado en `LogSyncFallida` — documentado como decisión deliberada en green-notes §1 y refactor-notes §descartados. El valor es incorrecto para el escenario de dead-letter real donde habrán ocurrido 4+ intentos. El log estructurado en producción reportará siempre `IntentosAgotados=1` independientemente del intento real. | `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs:73` | Nit asumido. Implementar un dead-letter handler de Wolverine con acceso al contexto de envelope (que sí tiene el contador de intentos) en un slice futuro. Referenciado en refactor-notes §descartados #2. |
| 4 | nit | `DictamenVigenteErpSyncFallida_v1` declarado en el mismo archivo que `SincronizarDictamenVigenteListener` — la señal de observabilidad cohabita con el listener en el mismo namespace. No es un problema funcional, pero si en el futuro se añaden proyecciones o handlers que consuman esta señal, la ubicación puede ser confusa. | `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs:116-123` | Nit asumido. Si emerge un consumidor de la señal, mover a `Erp/Observabilidad/` o similar. |

---

## 4. Veredicto final

- [ ] **approved**
- [x] **approved-with-followups** — followups FU-44 y FU-45 registrados en `FOLLOWUPS.md`. Nits #3 y #4 asumidos sin followup.
- [ ] **request-changes**

---

_El orquestador puede proceder al commit del slice y a la fase de infra-wire._
