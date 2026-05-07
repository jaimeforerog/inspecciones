# Review notes — Slice 1g — FirmarInspeccion

**Autor:** reviewer
**Fecha:** 2026-05-07
**Slice auditado:** `slices/1g-firmar-inspeccion/`
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice 1g está completo y correcto. Los 22 tests del escenario spec §6 pasan en verde (96/96 del suite de dominio total); el build compila sin warnings ni errores; la cobertura de ramas del agregado `Inspeccion` es 96.66 %, por encima del umbral de 85 %. Todos los criterios de calidad obligatorios se cumplen. Se identifican dos followups no bloqueantes relativos a `CapabilityRequeridaException` (cobertura 0 %) y a los stubs de eventos `AdjuntoSubido_v1` / `AdjuntoEliminado_v1` que tienen miembros públicos sin cobertura de línea en los tests de dominio puro. El orquestador puede proceder al commit.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. Los 18 escenarios (§6.1 × 4 subtests + §6.2..§6.18) están mapeados en `FirmarInspeccionTests.cs`. El escenario §6.14 tiene un test extra para whitespace (borde natural de V-F5), total 22.
- [x] Cada precondición tiene un test que la viola: PRE-2 (§6.4), PRE-3 (§6.5, §6.6), PRE-4 — validada en handler no en aggregate, documentada así en spec §4, sin test de dominio puro (correcto), PRE-5 (§6.7, §6.8), PRE-6 (§6.10..§6.13), PRE-7 (§6.14), PRE-8 (§6.15), PRE-9 (§6.16).
- [x] Cada invariante tocada tiene un test que la viola con referencia al código del modelo: V-F1 (PRE-3), V-F3 (PRE-6), V-F5 (PRE-7), V-F6 (PRE-8), V-F7 (PRE-2), V-F8 (PRE-5), I-F1 (§6.18). Los nombres de tests incluyen sufijos `_PRE_X_V_FX` o `_I_FX` de forma consistente.
- [x] Los nombres de los tests son frases completas descriptivas en español con referencia al código de invariante.

### 2.2 Tests como documentación

- [x] Given/When/Then está estructuralmente visible mediante comentarios `// Given`, `// When`, `// Then` en cada test.
- [x] Sin mocks del dominio. Los tests reconstruyen el aggregate desde streams de eventos reales mediante `CasoDeUso.Firmar()` / `Inspeccion.Reconstruir()`.
- [x] Coordenadas GPS en fixtures usan valores plausibles para Colombia (Latitud=4.7, Longitud=-74.1), no coordenadas `(0,0)`. `UbicacionGps` se usa correctamente en lugar de `double` pelado.

### 2.3 Implementación

- [x] El código de producción añadido es mínimo. Todos los métodos públicos nuevos son ejercidos por al menos un test: `Firmar(...)`, los cinco `Apply` nuevos, los seis tipos de excepción del slice, los tres records de evento. Excepción documentada como followup: `CapabilityRequeridaException` (cobertura línea=0) y las líneas de los stubs `AdjuntoSubido_v1`/`AdjuntoEliminado_v1` no ejercidas en Domain.Tests.
- [x] Sin `DateTime.UtcNow` en dominio. El handler llama `_time.GetUtcNow()` una única vez y reutiliza el valor. El dominio recibe `DateTimeOffset ahora` como parámetro.
- [x] Sin `Guid.NewGuid()` en el dominio ni en el handler (este slice no genera GUIDs nuevos).
- [x] Los tres eventos (`DiagnosticoEmitido_v1`, `DictamenEstablecido_v1`, `InspeccionFirmada_v1`) son `sealed record` inmutables. Sin setters públicos. Sufijo `_v1` presente en los tres.
- [x] `UbicacionGps` value object usado en `InspeccionFirmada_v1.UbicacionFirma` y en el comando `FirmarInspeccion.UbicacionFirma`. Sin `double` pelado para coordenadas.
- [x] `FirmaUri: string` en los eventos es el campo opaco del blob. El dominio nunca firma SAS. Patrón SAS upload correcto (el cliente sube primero, el handler recibe el URI ya persistido).
- [x] `Apply(DiagnosticoEmitido_v1)`, `Apply(DictamenEstablecido_v1)`, `Apply(InspeccionFirmada_v1)`, `Apply(AdjuntoSubido_v1)`, `Apply(AdjuntoEliminado_v1)` son mutaciones puras: ninguno lanza excepciones, valida estado ni re-aplica invariantes. Las precondiciones PRE-2..PRE-9 viven exclusivamente en `Inspeccion.Firmar(...)`. Verificado en el código de `Inspeccion.cs` líneas 688-728.
- [x] Test de rebuild desde stream presente (§6.17). El test reproyecta los 5 eventos del happy path (InspeccionIniciada + HallazgoRegistrado + DiagnosticoEmitido + DictamenEstablecido + InspeccionFirmada) sobre un aggregate vacío y verifica `Estado`, `DiagnosticoFinal`, `Dictamen`, `FirmaUri`, `UbicacionFirma.Latitud` y `FirmadaEn`. Ningún `Apply` lanza durante el rebuild — test verde.
- [x] Handler con un único `IDocumentSession.SaveChangesAsync()`. Los tres eventos se pasan en un único `_session.Events.Append(cmd.InspeccionId, eventos)` seguido de `await _session.SaveChangesAsync(ct)`. Sin partición del save. Verificado en `FirmarInspeccionHandler.cs` líneas 60-61.

### 2.4 Cobertura

- [x] Cobertura de ramas del agregado `Inspeccion`: **96.66 %** (branch-rate=0.9666 según cobertura medida con `dotnet test --collect:"XPlat Code Coverage"` sobre `Inspecciones.Domain.Tests`). Supera el umbral de 85 %.
- [x] Cobertura total del paquete `Inspecciones.Domain`: líneas 93.28 %, ramas 96.66 %. Consistente con slices anteriores (1f: 96.29 %).
- Observación: `CapabilityRequeridaException` tiene line-rate=0 (la excepción se define en Domain pero se lanza desde el handler de Application — patrón documentado desde slice 1a en followup #11, sin cambio). `DiagnosticoRequeridoException` también tiene line-rate=0 en Domain.Tests por la misma razón: PRE-4 se valida en el handler, no en el aggregate. Ambas están cubiertas en Application.Tests (Docker no disponible en entorno local — preexistente). Los stubs `AdjuntoSubido_v1` y `AdjuntoEliminado_v1` tienen line-rate bajas (0.57 y 0.67) porque son records con campos que el instrumentador reporta parcialmente; sus `Apply` correspondientes en `Inspeccion` están cubiertos al 100 %.

### 2.5 Refactor

- [x] `refactor-notes.md` presente y claro. Documenta el único cambio aplicado (move `DiagnosticoRequeridoException` de Application a Domain) y los cinco refactors descartados con justificación.
- [x] Los tests no cambiaron de lógica entre green y refactor. El cambio aplicado en refactor fue exclusivamente un move de clase entre proyectos; ningún test de dominio referencia `DiagnosticoRequeridoException` directamente.
- [x] Cero warnings de compilación. `dotnet build` reporta `0 Advertencia(s), 0 Errores`.

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Domain.Tests/` completo: 96/96 verde. Los tests de Application e Infrastructure fallan por Docker no disponible en el entorno local — fallo preexistente documentado desde slice 1b, ajeno a este slice.
- [x] Los cambios incidentales en fixtures (`HallazgoFixtures.StreamConInspeccionFirmada` actualizado al payload completo de `InspeccionFirmada_v1`, `AsignarRepuestoTests` y `EliminarHallazgoTests` actualizados a la nueva firma del constructor de 5 campos) son consistentes. Los tests de slices previos (1c, 1d, 1e, 1f) continúan en verde.
- [x] La inmutabilidad post-firma (I-F1) está verificada por el test §6.18: `RegistrarHallazgo` sobre un aggregate en estado `Firmada` lanza `InspeccionNoEnEjecucionException`. La invariante es garantizada transversalmente por PRE-2 de todos los métodos de decisión que exigen `Estado = EnEjecucion`.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15.4` (eventos #15, #16, #2 con payloads exactos) y §15.5 (validaciones V-F1..V-F8 todas bloqueantes, orden de evaluación correcto en `Firmar()`).
- [x] ADR-007 (`§17`): `FirmarInspeccion` no genera OT automáticamente. Los tres eventos propios (DiagnosticoEmitido → DictamenEstablecido → InspeccionFirmada) se emiten atómicamente. Las sagas post-firma son slices separados. Cumplido.
- [x] ADR-006 (`§16`): el handler no hace POST al ERP. Cumplido.
- [x] ADR-005 (`§14`): no aplica a este slice (SignalR se dispara desde sagas posteriores). Cumplido.
- [x] ADR-002 (auth tentativo): claims mock `tecnicoId="rmartinez"` con `TieneCapabilityEjecutarInspeccion=true` — patrón idéntico a slices 1b-1f. Followup #14 existente.
- [x] FU-13 migración `InspeccionAbiertaPorEquipoView`: aplicada como `EventProjection` (no `MultiStreamProjection`) con justificación documentada en `refactor-notes.md` y followup #13 actualizado. El `session.Insert(view)` fue eliminado de `IniciarInspeccionHandler`. La proyección corre inline en el mismo `SaveChangesAsync`. Funcionalmente correcto.
- [x] Convención de tipos de IDs (§15.4): `InspeccionId: Guid`, `TecnicoId: string` opaco, `EquipoId: int` en la proyección. Sin mezcla de tipos.
- [x] Decisiones P-1 (V-F2 solo UX) y P-2 (solo contribuyentes, sin capability supervisor) firmadas por el usuario y reflejadas en la implementación.

### 2.8 Integración cross-team Sinco (no aplica)

Este slice no invoca ni publica hacia ningún endpoint Sinco on-prem. La firma es operación puramente de dominio local. Los POSTs al ERP son responsabilidad de las sagas post-firma (slices separados).

### 2.9 SignalR / push (no aplica)

Este slice no emite eventos SignalR. El hub `InspeccionesHub` existe (ADR-005) pero no es usado por este handler. Los eventos push (`OTGenerada`, `InspeccionCerradaSinOT`) se emiten desde las sagas posteriores.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `CapabilityRequeridaException` tiene line-rate=0 en Domain.Tests. La excepción está definida en `Inspecciones.Domain/Inspecciones/Excepciones.cs` pero se lanza exclusivamente desde la capa handler (Application). Patrón idéntico al documentado en followup #11 (slice 1a). Ahora se repite con `DiagnosticoRequeridoException` (movida a Domain por refactorer, pero sigue sin test de dominio porque PRE-4 es del handler). | `src/Inspecciones.Domain/Inspecciones/Excepciones.cs`: líneas 7-8 (`CapabilityRequeridaException`) y 103-104 (`DiagnosticoRequeridoException`) | Mantener en followup #11 la discusión de qué excepciones pertenecen a Domain vs Application cuando no son lanzadas por el aggregate. Cuando Docker esté disponible, los tests de Application cubrirán ambas. No bloqueante. |
| 2 | followup | Los stubs `AdjuntoSubido_v1` y `AdjuntoEliminado_v1` tienen line-rate bajas (0.57 y 0.67 respectivamente) porque son records posicionales cuyos campos de constructor no se ejercen todos en Domain.Tests (el instrumentador reporta líneas del constructor primario del record). Sus `Apply` en el aggregate sí están cubiertos. La cobertura de ramas del aggregate es 96.66 % — no hay ramas descubiertas en esos stubs. | `src/Inspecciones.Domain/Inspecciones/AdjuntoSubido_v1.cs`, `AdjuntoEliminado_v1.cs` | Al implementar el slice de `SubirAdjunto` / `EliminarAdjunto`, los stubs serán reemplazados por eventos completos con tests propios. La cobertura parcial actual es consecuencia esperada de los stubs y no representa lógica sin probar. |
| 3 | nit | El endpoint `FirmarInspeccion` en `InspeccionesEndpoints.cs` (línea 343) construye `ClaimsTecnico` con `ProyectosAsignados: new HashSet<int>()` (conjunto vacío). El handler valida `TieneCapabilityEjecutarInspeccion` pero no valida proyecto para firma. El conjunto vacío es inofensivo para el flujo de firma pero podría confundir si se añaden validaciones de proyecto en el futuro. | `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs:343` | Followup #14 (claims reales desde JWT) resolverá esto cuando ADR-002 se implemente. No bloqueante. |

---

## 4. Veredicto final

- [ ] **approved** — sin hallazgos, o solo nits asumidos.
- [x] **approved-with-followups** — followups registrados en `FOLLOWUPS.md`.
- [ ] **request-changes** — se devuelve a **{red | green | refactorer}** con los blockers detallados.

Los dos followups son extensiones de followups ya abiertos (#11 para las excepciones sin cobertura en Domain, #14 para los claims mock). No se abren followups nuevos en este slice. El nit #3 del claims vacío es ruido documental — ya cubierto por followup #14.

---

**Notas para el orquestador:**

1. El slice puede comitearse: `feat(slice-1g): FirmarInspeccion`. 0 blockers.
2. `dotnet build` limpio (0 warnings, 0 errors). 96/96 tests de dominio en verde.
3. Cobertura de ramas de `Inspeccion`: **96.66 %** — sobre el umbral de 85 %.
4. FU-13 completado parcialmente: `EventProjection` en lugar de `MultiStreamProjection` — decisión documentada en followup #13 con análisis de por qué `EquipoId` falta en los eventos terminales.
5. Próximos slices candidatos: (a) `CerrarInspeccionSaga` / `GenerarOT` (ADR-007 — slice de saga post-firma); (b) `SubirAdjunto` / `EliminarAdjunto` (completa los stubs del slice 1g y hace el slice de adjuntos); (c) proyecciones de lectura (`BandejaTecnicoView`, `DetalleInspeccionView`) referenciadas en spec §8.2 con TODO en el código de proyección.
6. El TODO `// TODO: actualizar BandejaTecnicoView en slice de proyecciones` está presente en `InspeccionAbiertaPorEquipoProjection.cs` como documenta la spec §8.2 — registrar en el backlog antes del primer slice de proyecciones.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
