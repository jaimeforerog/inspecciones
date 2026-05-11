# Review notes — Slice 1m — CancelarInspeccion

**Autor:** reviewer
**Fecha:** 2026-05-11
**Slice auditado:** `slices/1m-cancelar-inspeccion/`.
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

Implementación limpia y fiel al spec. Domain.Tests: 213/228 (15 skip por diseño). Build: 0 warnings, 0 errores. Cobertura de ramas `Inspecciones.Domain`: 94.9% (holgadamente sobre el mínimo del 85%). Api.Tests no corren en este entorno por Docker ausente — se abren como FU-39 (declarado en el brief del slice). Dos followups menores abiertos; sin blockers.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. Los 16 escenarios §6.1–§6.16 están cubiertos; los 3 skip (PRE-1 HTTP, PRE-2 Marten, idempotencia Wolverine) tienen justificación explícita en el `[Fact(Skip=…)]` y están cubiertos en la capa adecuada (Api.Tests / FU-39).
- [x] Cada precondición tiene un test que la viola: PRE-1 (skip domain — capa HTTP), PRE-2 (skip domain — handler), PRE-3 (`CancelarInspeccion_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException`), PRE-4 (tres tests: vacío / espacios / < 10 chars), PRE-5 (tres tests: Firmada / Cancelada / CerradaSinOT).
- [x] Cada invariante tocada tiene un test que la viola con referencia al código: I6 en `_lanza_InspeccionNoEnEjecucionException_I6` (x3 para cada estado no-EnEjecucion). I-F1 cubierta implícitamente por el test de estado Firmada (PRE-5 abarca ambas invariantes per spec §4 / §5).
- [x] Nombres de tests: frases descriptivas en español con código de invariante cuando aplica.

### 2.2 Tests como documentación

- [x] Given/When/Then estructuralmente visible en todos los tests (comentarios `// Given`, `// When`, `// Then`).
- [x] Sin mocks del dominio. `CasoDeUso.Cancelar` inlinea PRE-3/PRE-4 como helper puro de test — no es mock; reconstruye el aggregate sobre eventos reales.
- [x] Eventos usados en Given son reales: `UbicacionGps(4.711m, -74.072m, 8.5m, ...)` — coordenadas plausibles para Colombia (Bogotá). No hay `(0,0)`.

### 2.3 Implementación

- [x] Código de producción mínimo: `Inspeccion.Cancelar`, `Apply(InspeccionCancelada_v1)`, `CancelarInspeccionHandler`, command record, result record, DTO request/response, endpoint, excepción, una línea en `Program.cs`. Todo ejercido por tests.
- [x] Sin `DateTime.UtcNow`: handler usa `_time.GetUtcNow()`. `TimeProvider` inyectado por constructor.
- [x] Sin `Guid.NewGuid()` en dominio.
- [x] `InspeccionCancelada_v1` es `sealed record` inmutable, sin setters públicos.
- [x] `UbicacionGps` usado en fixtures (no double pelado). No aplica dentro del evento `InspeccionCancelada_v1` (spec confirma que no incluye GPS — Decisión D-2).
- [x] `Apply(InspeccionCancelada_v1)` puro: únicamente `Estado = Cancelada`, `MotivoCancelacion = e.Motivo`, `_contribuyentes.Add(e.CanceladaPor)`. Sin validaciones, sin throws.
- [x] Rebuild test presente: `CancelarInspeccion_rebuild_desde_stream_2_eventos_estado_correcto` (§6.15) y `CancelarInspeccion_rebuild_desde_stream_reproduce_estado_post_comando`. Ambos pasan sin necesitar infraestructura.
- [x] Atomicidad: un único `_session.Events.Append(...) + SaveChangesAsync()` en el handler (líneas 53–54 de `CancelarInspeccionHandler.cs`).
- [x] Handler recibe `ClaimsTecnico` vía claims mock por ADR-002 tentativo. El dominio no conoce JWT.

### 2.4 Cobertura

- [x] Cobertura de ramas `Inspecciones.Domain`: **94.9%** (243/256 ramas cubiertas). Bien por encima del mínimo 85%.
- [x] `Inspeccion` class: **99%**.
- [x] `InspeccionCancelada_v1` record: **100%**.
- [x] `MotivoCancelacionInvalidoException`: **100%**.
- [x] `CancelarInspeccion` (command record en Domain): **0%** — ningún test de dominio lo instancia directamente. El handler lo instancia; Application.Tests cubren ese path (FU-39). Nit: por convención del proyecto, el command record vive en Domain (alineado con `IniciarInspeccion`, `RegistrarHallazgo`, etc.), no en Application como indica erróneamente la tabla §13 del spec.

### 2.5 Refactor

- [x] `refactor-notes.md` presente. Documentación completa: 2 cambios aplicados + 3 refactors descartados con justificación.
- [x] Los tests no cambiaron de lógica entre green y refactor: el único cambio fue extraer `motivoTrimmed = cmd.Motivo.Trim()` en el handler (eliminación de triple evaluación) y actualizar un comentario en el endpoint. Conteos: 213 pass / 15 skip pre y post.
- [x] Cero warnings de compilación: `dotnet build Inspecciones.sln` reportó `0 Advertencia(s), 0 Errores`.

### 2.6 Invariantes cross-slice

- [x] `dotnet test Inspecciones.Domain.Tests`: 213/228 — ningún test de slices previos roto. El cambio de shape de `InspeccionCancelada_v1` (reorden de parámetros + renombre `CanceladoPor` → `CanceladaPor`) fue correctamente propagado a `HallazgoFixtures.cs` (único uso previo).

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §2.1`: transición `EnEjecucion → Cancelada`, evento `InspeccionCancelada_v1`, saga no ejecuta posts a Sinco (§7.2).
- [x] Alineado con §15.4 catálogo canónico de 24 eventos (evento #16 `InspeccionCancelada_v1`).
- [x] Alineado con §15.12.6 `InspeccionAbiertaPorEquipoView` ya consume el evento (sin cambio).
- [x] ADR-006: no aplica — sin POST al ERP.
- [x] ADR-005 (SignalR): no aplica — spec §10 confirma explícitamente.
- [x] ADR-008: `X-Client-Command-Id` verificado en endpoint. Idempotencia natural via PRE-5 documentada en spec §7.
- [x] Decisión P-1 firmada por el usuario: solo contribuyentes pueden cancelar — implementado como PRE-3.

### 2.8 Integración cross-team Sinco (no aplica)

No hay llamada a endpoints Sinco on-prem. Sin outbox, sin `Idempotency-Key` para ERP. Confirmado en spec §11 y modelo §7.2.

### 2.9 SignalR / push (no aplica)

No aplica para este slice. Confirmado en spec §10.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `CancelarInspeccion` command record ubicado en `Inspecciones.Domain` pero la tabla §13 del spec lo lista en `Inspecciones.Application`. Inconsistencia entre spec y convención real del proyecto (todos los command records viven en Domain). El código es correcto; es el spec el que tiene error tipográfico. | `spec.md §13`, `src/Inspecciones.Domain/Inspecciones/CancelarInspeccion.cs` | Corregir tabla §13 del spec en PR de documentación. |
| 2 | followup | `Application.Tests` (`CancelarInspeccionHandlerTests.cs`) y `Api.Tests` (`CancelarInspeccionEndpointTests.cs`) no se ejecutaron en este entorno por Docker ausente. La cobertura end-to-end (PRE-2, PRE-3 vía Marten, PRE-5 en handler, respuestas HTTP) queda pendiente de validación en CI. | `tests/Inspecciones.Api.Tests/CancelarInspeccionEndpointTests.cs`, `tests/Inspecciones.Application.Tests/Inspecciones/CancelarInspeccionHandlerTests.cs` | Registrar como FU-39: habilitar ejecución de Api.Tests y Application.Tests en CI o en entorno con Docker. |
| 3 | nit | `xunit.runner.json` sin versionar en `tests/Inspecciones.Api.Tests/`. El archivo deshabilita paralelismo, lo cual puede enmascarar race conditions en otros slices. No pertenece a este slice pero apareció como untracked. | `tests/Inspecciones.Api.Tests/xunit.runner.json` | Evaluar si debe commitearse en un PR de infra. No impacta este slice. |

---

## 4. Veredicto final

- [ ] **approved**
- [x] **approved-with-followups** — followups #39 (Application.Tests + Api.Tests en CI con Docker) y nit de spec §13 registrados. El slice se cierra.
- [ ] **request-changes**

---

## 5. Output real de dotnet test

### Domain.Tests (sin Docker)

```
Correctas! - Con error: 0, Superado: 213, Omitido: 15, Total: 228, Duración: 84 ms
```

Detalle del slice 1m: 16/19 pasan; 3 skip permanentes por diseño (PRE-1 capa HTTP, PRE-2 requiere Marten, idempotencia Wolverine).

### Api.Tests

```
Con error! - Con error: 38, Superado: 0, Omitido: 4, Total: 42, Duración: 49 ms
```

Todos los 38 fallos son `System.ArgumentException: Docker is either not running or misconfigured` — error de infraestructura, no de lógica. Los 4 skip son los ADR-008 permanentes. Condición pre-existente en este entorno; no introducida por este slice. Queda pendiente en FU-39.

### Build

```
dotnet build Inspecciones.sln → 0 Advertencia(s), 0 Errores
```

### Cobertura de ramas (Inspecciones.Domain, Domain.Tests únicamente)

```
Branch coverage: 94.9% (243 de 256)
Inspeccion: 99%
InspeccionCancelada_v1: 100%
MotivoCancelacionInvalidoException: 100%
```

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
