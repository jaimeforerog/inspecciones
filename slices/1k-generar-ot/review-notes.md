# Review notes — Slice 1k — GenerarOT

**Autor:** reviewer
**Fecha:** 2026-05-08
**Slice auditado:** `slices/1k-generar-ot/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice 1k implementa `GenerarOT` / `SolicitarOT` correctamente. Los 13 escenarios del spec tienen cobertura de tests activos (12) o skip justificado (3); todos los `Apply` son puros; cobertura de ramas del aggregate `Inspeccion` es 94.71 %, por encima del umbral del 85 %. Se registra un followup (#31) por desfase de documentación entre el shape implementado y el shape canónico del ADR-007 §17 — no es bloqueante. El slice está listo para wires de infraestructura.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] **§6.1** happy path NoPuedeOperar: cubierto por tests #1, #2, #3 (desglose justificado en red-notes §5).
- [x] **§6.2** happy path ConRestriccion + ComentarioJefe: test #4.
- [x] **§6.3** PRE-1 capability ausente: Skip justificado — vive en middleware HTTP. Razón documentada en `[Fact(Skip=...)]`.
- [x] **§6.4** PRE-3/I-F4.a EnEjecucion: test #6, aserción `*EnEjecucion*` en mensaje.
- [x] **§6.5** PRE-4/I-F4.b sin RequiereIntervencion: test #7, aserción `*RequiereIntervencion*`.
- [x] **§6.6** PRE-5/I-F4.c OT ya solicitada: test #8.
- [x] **§6.7** PRE-6/I-F4.d OT rechazada: test #9.
- [x] **§6.8** PRE-7/I-F4.e dictamen PuedeOperar: test #10.
- [x] **§6.9** idempotencia Wolverine: Skip justificado — Wolverine envelope dedup, vive en Application.Tests.
- [x] **§6.10** PRE-2 InspeccionId inexistente: Skip justificado — handler+Marten, vive en Application.Tests.
- [x] **§6.11** hallazgo RequiereIntervencion eliminado no cuenta: test #13.
- [x] **§6.12** PRE-3 variante CerradaSinOT: test #14, aserción `*CerradaSinOT*`.
- [x] **§6.13** rebuild desde stream 7 eventos: test #15 — presente, verificado, pasa.
- [x] Cada precondición (PRE-3..PRE-7) viola en un test separado.
- [x] Invariantes I-F4.a..e, I-F5, I-F1 referenciadas en nombres de tests y comentarios.
- [x] Naming en español, frase completa, referencia a invariante. Cumple las convenciones de CLAUDE.md.

### 2.2 Tests como documentación

- [x] Given/When/Then visible con comentarios de sección en cada test.
- [x] Cero mocks del dominio. `CasoDeUso.SolicitarOT` reconstruye el aggregate desde eventos; no hay interfaces mockeadas.
- [x] Streams de `Given` realistas: coordenadas GPS vía `UbicacionGps` (value object), timestamps plausibles `2026-05-08T14:00:00Z`, `TecnicoIniciador="carlos.ruiz"`, `SolicitadaPor="jefe.campo.01"`. No hay valores nonsense tipo `(0,0)` para GPS.

### 2.3 Implementación

- [x] Código mínimo: todos los miembros públicos nuevos (`SolicitarOT`, `OTSolicitada`, `OTRechazada`, `SolicitadaEn`, los tres `Apply`, los dos enums, las 5 excepciones, los 3 eventos) ejercidos por al menos un test.
- [x] **Cero `DateTime.UtcNow`** en dominio — confirmado. `OTSolicitada_v1.SolicitadaEn` se recibe como parámetro `ahora: DateTimeOffset` en `SolicitarOT`; el handler inyectará `TimeProvider.GetUtcNow()`.
- [x] **Cero `Guid.NewGuid()`** en dominio — confirmado. No hay IDs generados dentro de `SolicitarOT`.
- [x] **`Apply(OTSolicitada_v1)` puro** — solo `OTSolicitada = true; SolicitadaEn = e.SolicitadaEn`. Sin throws, sin validaciones.
- [x] **`Apply(GeneracionOTRechazada_v1)` puro** — solo `OTRechazada = true`.
- [x] **`Apply(InspeccionCerradaSinOT_v1)` puro** — solo `Estado = EstadoInspeccion.CerradaSinOT`.
- [x] **Test de rebuild desde stream presente** (`§6.13` — 7 eventos). Pasa. Confirma que ningún `Apply` lanza excepción y que el estado es consistente post-replay.
- [x] Eventos versionados con sufijo `_v1`: `OTSolicitada_v1`, `GeneracionOTRechazada_v1`, `InspeccionCerradaSinOT_v1`.
- [x] Soft delete: no aplica directamente (ningún hallazgo se borra en este slice). El filtro `!h.Eliminado` en PRE-4 respeta la semántica de soft delete de hallazgos.
- [x] Records inmutables para los 3 eventos y el comando. Sin setters públicos.
- [x] `AplicarEvento` switch incluye los 3 nuevos cases (`OTSolicitada_v1`, `GeneracionOTRechazada_v1`, `InspeccionCerradaSinOT_v1`) — líneas 361-368 de `Inspeccion.cs`.

### 2.4 Cobertura

- [x] Cobertura de ramas del aggregate `Inspeccion`: **94.71 %** (medido con `dotnet test --collect:"XPlat Code Coverage"`). Por encima del umbral del 85 %.
- Ramas no cubiertas (~5.3 %): las ramas de estados intermedios no alcanzados por ningún test de este slice (p. ej. estados `Cancelada`, `CierrePendienteOT`) pertenecen a slices futuros. No constituyen deuda de este slice.

### 2.5 Refactor

- [x] `refactor-notes.md` presente — documenta 4 refactors descartados con justificación.
- [x] Tests no cambiaron de lógica entre green y refactor (refactor-notes indica cero cambios, runner confirma mismo conteo 179/9/188).
- [x] Cero warnings de compilación — build del proyecto `Inspecciones.Domain` devuelve `0 Advertencia(s) 0 Errores`.

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests/` completo: 179 superados / 9 omitidos / 0 errores. Cero regresiones.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15.7` invariantes I-F4.a..e (5 precondiciones implementadas en `SolicitarOT` en el orden correcto), I-F5 (estado derivado), I-F6 (no solicitar si rechazada = PRE-6).
- [x] Alineado con ADR-007 (§17): el slice emite exactamente `OTSolicitada_v1`; el POST a MYE (M-1) queda para la saga `EjecutarOTSaga` (slice 3.24b). `Idempotency-Key=InspeccionId` es responsabilidad de ese slice (documentado en spec §7).
- [x] Alineado con ADR-006: handler no hace POST a ERP en este slice; el outbox es responsabilidad de la saga.
- [x] Alineado con ADR-008: `X-Client-Command-Id` / `MessageId` documentado en spec §7; test de idempotencia en Skip con justificación (Application.Tests, fuera del dominio puro).
- [x] Alineado con ADR-002: el handler recibe `Capabilities` como parámetro del comando; el dominio no conoce JWT. `SolicitarOT` no verifica `Capabilities` internamente — PRE-1 vive en capa HTTP (documentado en `GenerarOT.cs` y en spec §4).
- [x] Alineado con ADR-001 (REST/VPN): no aplica, no hay endpoints Sinco en este slice.
- [x] Alineado con ADR-005: SignalR no aplica en este slice (responsabilidad de saga slice 3.24b — documentado en spec §10).
- [~] **Desfase menor con §17:** el shape canónico de `OTSolicitada_v1` en §17 usa `DateTime SolicitadaEn` (en lugar de `DateTimeOffset`) y no incluye `Prioridad`, `Observaciones`, `ComentarioJefe`. La implementación es correcta y el spec documenta explícitamente la desviación en §3.1 y §12 P-1/P-2. Registrado como **followup #31** para corrección del documento.

### 2.8 Integración cross-team Sinco

- [x] No aplica. Este slice no invoca ningún endpoint Sinco on-prem (documentado en spec §11).

### 2.9 SignalR / push

- [x] No aplica en este slice (documentado en spec §10). El push `OTGenerada` es responsabilidad de la saga `EjecutarOTSaga` (slice 3.24b).

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | Shape canónico de `OTSolicitada_v1` en `01-modelo-dominio.md §17` usa `DateTime` en lugar de `DateTimeOffset` y no incluye `Prioridad`, `Observaciones`, `ComentarioJefe`. El `GenerarOT` canónico tampoco incluye esos campos. El código es correcto; el doc quedó desfasado. | `Inspecciones/docs/01-modelo-dominio.md` línea ~4596-4614 | Registrado como **FU-31** — doc-writer actualiza §17 previo al slice `EjecutarOTSaga`. |
| 2 | nit | `StreamFirmadoConHallazgoIntervencion()` en `GenerarOTFixtures.cs` (línea 199) es un alias de `StreamFirmadoNoPuedeOperar()` definido para el escenario §6.3 (Skip). Al ser un Skip permanente en tests de dominio, el fixture no lo consume ningún test activo. No es muerto funcional — el comentario aclara que es para tests HTTP. Sin acción requerida. | `tests/Inspecciones.Domain.Tests/Inspecciones/GenerarOTFixtures.cs:199` | Nit — sin acción. |
| 3 | nit | El campo `bool OTSolicitada` en `Inspeccion.cs` tiene el mismo nombre que el campo homónimo en el tipo del evento. No hay ambigüedad gracias al calificador de tipo, pero podría confundir a un lector nuevo. El naming sigue el patrón de slices anteriores (`OTRechazada`, `Firmada`, etc.) — consistente con el aggregate. | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs:59` | Nit — sin acción. |

---

## 4. Veredicto final

- [ ] **approved**
- [x] **approved-with-followups** — followup #31 registrado en `FOLLOWUPS.md`. El slice está listo para wires de infraestructura y commit.
- [ ] **request-changes**

**Followups creados en esta review:**

| FU | Descripción | Disparador |
|---|---|---|
| #31 | Shape `OTSolicitada_v1` y `GenerarOT` en §17 desactualizados (`DateTime` vs `DateTimeOffset`, campos faltantes) | Previo al slice `EjecutarOTSaga` (3.24b) |

---

_Veredicto `approved-with-followups`. El orquestador puede proceder al commit `feat(slice-1k): GenerarOT` y a la fase infra-wire (handler Wolverine, endpoint HTTP `POST /api/v1/inspecciones/{id}/generar-ot`, capability gate)._
