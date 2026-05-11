# Review notes — fix-FU-38 — Results.Forbid() reemplazado por Forbidden403 helper

**Autor:** reviewer
**Fecha:** 2026-05-11
**Slice auditado:** `slices/fix-FU-38/`
**Veredicto:** `approved`

---

## 1. Resumen ejecutivo

Fix puramente de capa HTTP: reemplaza los 6 callsites de `Results.Forbid()` por un helper estático `Forbidden403` que construye `Results.Json(..., statusCode: 403)` sin depender de `IAuthenticationService`. Los tests corren en 28/32 passing (2 failing FU-36 preexistente, 2 skipped ADR-008) — exactamente el conteo predicho por spec §4.1. Un solo archivo modificado (`InspeccionesEndpoints.cs`), 0 warnings de compilación, domain tests 197/197 sin regresión. Veredicto: aprobado sin followups.

---

## 2. Checklist de auditoría

### 2.1 Spec vs. tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. El spec no define §6 propio (es un fix, no un comando nuevo); los 2 tests target están identificados en spec §2 y preexistían en el repo. Ambos pasaron a verde con el fix.
- [x] Precondiciones: el fix no introduce nuevas pre-condiciones. Los 4 callsites latentes (IniciarInspeccion, IniciarInspeccionMonitoreo, FirmarInspeccion x2) no tienen test rojo en este slice — decisión explícita y justificada en spec §1 y §3.
- [x] No aplica verificar invariantes de dominio I-H*/I-F*/V-F*: el fix no toca el dominio.
- [x] Los 2 tests target tienen nombres en español completos: `POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1` y `POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1`. Ambos referencian el código de error PRE-1.

### 2.2 Tests como documentación

- [x] Los tests existentes tienen estructura Given/When/Then visible (siembra con `SembrarInspeccionFirmada*`, construcción de request con headers, aserción sobre `StatusCode`).
- [x] Sin mocks del dominio.
- [x] No aplica rebuild test: el fix no emite eventos ni modifica el aggregate.

### 2.3 Implementación

- [x] Código añadido es mínimo: 1 helper privado `Forbidden403(string, string)` + 1 constante privada `MensajeCapabilityGenerarOT`. Ambos ejercidos por los 6 callsites (helper) y los 2 callsites de capability de OT (constante).
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()`, ni acceso a APIs del navegador. No aplica a este fix.
- [x] No se añaden eventos ni records. No aplica.
- [x] No se usan primitivos pelados para value objects. No aplica.
- [x] No hay `Apply`. No aplica.
- [x] No hay rebuild test requerido (0 eventos emitidos en el fix).
- [x] No hay handler modificado. No aplica `SaveChangesAsync`.
- [x] Los 6 callsites de `Results.Forbid()` han sido eliminados del archivo — verificado con grep; 0 ocurrencias residuales.
- [x] Helper posicionado fuera del método `MapInspeccionesEndpoints`, como método privado de la clase estática, después del cierre del builder (`return app;`). Constante declarada en el mismo scope.

### 2.4 Cobertura

No aplica: el fix no modifica el aggregate `Inspeccion` ni ningún handler de dominio. La cobertura de ramas de `InspeccionesEndpoints.cs` no es el objetivo de medición (es capa HTTP, no dominio). Los 28 tests verdes cubren los 6 callsites reparados (2 con test explícito, 4 cubiertos por la no-regresión general).

### 2.5 Refactor

- [x] `refactor-notes.md` presente con 7 refactors evaluados y descartados o aplicados.
- [x] Único cambio aplicado: extracción de constante `MensajeCapabilityGenerarOT` para eliminar duplicación del literal en los callsites de GenerarOT y RechazarGenerarOT. Cambio no toca lógica.
- [x] Los tests no fueron modificados entre green y refactor.
- [x] 0 warnings de compilación — verificado con `dotnet build --no-incremental`:

```
0 Advertencia(s)
0 Errores
```

### 2.6 Invariantes cross-slice

- [x] `dotnet test` completo de `Inspecciones.Api.Tests`: **28 passing, 2 failing (FU-36 preexistente), 2 skipped (ADR-008)**.
- [x] `dotnet test` completo de `Inspecciones.Domain.Tests`: **197 passing, 0 failing, 12 skipped**.

Output real de `dotnet test Inspecciones.Api.Tests`:

```
Pruebas totales: 32
     Correcto: 28
     Incorrecto: 2
    Omitido: 2
 Tiempo total: 10,2514 Segundos
```

Los 2 failing son `RegistrarHallazgoEndpointTests` (FU-36, bug de deserialización preexistente, independiente de este fix).

Output real de `dotnet test Inspecciones.Domain.Tests`:

```
Con error:     0, Superado:   197, Omitido:    12, Total:   209
```

### 2.7 Coherencia con decisiones previas

- [x] Alineado con ADR-002: el fix es consecuencia directa de ADR-002 (identidad 100% del host PWA — el módulo no registra `AddAuthentication()`). El helper `Forbidden403` construye la respuesta sin depender de `IAuthenticationService`.
- [x] No toca ADR-001, ADR-003, ADR-004, ADR-005, ADR-006, ADR-007.
- [x] Working tree tras el fix: exactamente 1 archivo modificado (`src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs`), confirmado con `git diff --stat HEAD`.
- [x] `codigoError` de los 6 callsites son consistentes con los specs de origen: `"PRE-3-PROYECTO"` para `ProyectoNoAutorizadoException` (spec FU-38 §4.3, alineado con convención del archivo), `"PRE-1"` para `CapabilityRequeridaException` (spec 1g §9 y switch existente en el endpoint), `"PRE-F3"` para `TecnicoNoContribuyenteException` (spec FU-38 §4.3), `"PRE-1"` para el header `X-Sin-Capability-Generar-OT` (specs 1k y 1l).

Nota sobre `"PRE-3-PROYECTO"` en los callsites latentes: los specs 1b y 1h no definen un `codigoError` string explícito para `ProyectoNoAutorizadoException` (solo declaran `403 Forbidden`). El valor `"PRE-3-PROYECTO"` es el asignado por el spec del propio fix-FU-38 §4.3. Cuando se escriban los tests de esos callsites, si hubiera discrepancia con los specs de origen, deberá corregirse en ese momento. El spec FU-38 §12 documenta esta pregunta abierta explícitamente; se considera aceptada al firmarse el spec.

### 2.8 Integración cross-team Sinco

No aplica. El fix está en la capa de validación HTTP previa a cualquier llamada al ERP.

### 2.9 SignalR / push

No aplica.

---

## 3. Hallazgos

No hay hallazgos bloqueantes ni followups. El fix cumple exactamente el scope del spec firmado.

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| — | — | Sin hallazgos. | — | — |

---

## 4. Veredicto final

- [x] **approved** — sin hallazgos, o solo nits asumidos.

El slice está listo para commit: `fix(FU-38): reemplazar Results.Forbid() por Forbidden403 helper — IAuthenticationService no registrado`.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
