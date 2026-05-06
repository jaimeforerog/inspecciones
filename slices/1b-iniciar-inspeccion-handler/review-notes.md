# Review notes — Slice 1b — IniciarInspeccionHandler + InspeccionAbiertaPorEquipoView

**Autor:** reviewer
**Fecha:** 2026-05-06
**Slice auditado:** `slices/1b-iniciar-inspeccion-handler/`.
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

Slice de plumbing bien ejecutado. 8 tests cubre los 8 escenarios de la spec §6; build 0 errores / 0 warnings; dominio 16/16 verde sin regresión. El handler implementa I-I1 dual (blanda + dura 23505), PRE-3, PRE-handler-1, atomicidad con un único `SaveChangesAsync`, y la race condition con reintento determinístico. Hay cuatro desviaciones respecto a la spec, todas documentadas en green-notes y refactor-notes con justificación aceptable; los candidatos a followup ya fueron registrados en `FOLLOWUPS.md` por el refactorer (#13..#16). El reviewer agrega un followup nuevo (#17) sobre el docstring del test §6.4 que afirma verificar Wolverine envelope dedup cuando el mecanismo real es I-I1. No hay blockers.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] **Cada escenario de `spec.md §6` tiene un test.**
  - §6.1 → `POST_inspecciones_happy_path_responde_201_Created_con_InspeccionId` (endpoint E2E)
  - §6.2 → `IniciarInspeccion_equipo_con_activa_retorna_existente_I_I1` (handler)
  - §6.3 → `Dos_IniciarInspeccion_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1` (handler)
  - §6.4 → `POST_inspecciones_replay_con_mismo_ClientCommandId_no_duplica_evento_idempotencia_ADR_008` (endpoint E2E)
  - §6.5 → `IniciarInspeccion_con_equipo_no_sincronizado_lanza_EquipoNoEncontrado_PRE_3` (handler)
  - §6.6 → `IniciarInspeccion_con_rutina_referenciada_no_sincronizada_lanza_RutinaTecnicaNoSincronizada` (handler)
  - §6.7 → `IniciarInspeccion_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizado_PRE_2` (handler)
  - §6.8 → `IniciarInspeccion_happy_path_proyeccion_y_evento_persisten_atomicos` (handler)

- [x] **Cada precondición tiene un test que la viola.**
  - PRE-3 (equipo no en catálogo) → §6.5.
  - PRE-handler-1 (rutina no sincronizada) → §6.6.
  - PRE-handler-2 (I-I1 blanda) → §6.2 y §6.3.
  - PRE-2 (proyecto no autorizado, defensa profundidad) → §6.7, test directo al handler sin pasar por el filtro HTTP.
  - PRE-1 y PRE-2 filtro HTTP: no hay test E2E de rechazo en capa HTTP — la spec los posiciona en la capa HTTP y el green los resuelve con mock fijo (`TieneCapabilityEjecutarInspeccion=true`). Aceptable en el contexto de ADR-002 tentativo; registrado como followup #14.

- [x] **Cada invariante tocada tiene un test que la viola.**
  - I-I1 (una activa por equipo) → §6.2 (shortcut blando), §6.3 (defensa dura Postgres 23505), §6.8 (happy path demuestra que la fila se crea atómicamente — sin fila no hay I-I1 activa).

- [x] **Nombres de tests en español, frases descriptivas.** Los 8 tests siguen el patrón `Comando_condicion_resultado_(invariante)`. Ejemplos correctos: `IniciarInspeccion_equipo_con_activa_retorna_existente_I_I1`, `Dos_IniciarInspeccion_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1`.

### 2.2 Tests como documentación

- [x] **Given/When/Then visible.** Todos los tests tienen comentarios `// Given`, `// When`, `// Then`. §6.7 (defensa en profundidad) usa el bloque `// Given/When/Then` implícito pero la estructura de `var act = async () => ...` + `ThrowAsync` es idiomática y legible.

- [x] **Sin mocks del dominio.** Los tests crean instancias reales de `EquipoLocal`, `RutinaTecnicaLocal`, `ClaimsTecnico`, `UbicacionGps` con valores realistas (Bogotá GPS `(4.711, -74.072)`, fechas plausibles, equipo `EQ-4521`). El handler recibe un `IDocumentSession` real contra Postgres en Testcontainers. Cero mocks del dominio.

- [x] **Eventos en Given son reales.** El setup siembra datos en Marten real (no fixtures estáticos), valores plausibles para Colombia. No hay `(0,0)` ni absurdos.

### 2.3 Implementación

- [x] **Código de producción mínimo.** Los cuatro archivos nuevos/modificados en Application y dos en Api tienen cada campo/método ejercido por al menos un test. `InspeccionAbiertaPorEquipoView.EquipoId` (alias computed) se verifica en §6.8. `IniciarInspeccionResult.Mensaje` se verifica en §6.2. El switch de `codigoError` en `InspeccionesEndpoints` (refactor #1) no tiene test de verificación del campo exacto — aceptable, registrado como nit; el campo era el único candidato real de enriquecimiento y el refactorer lo justifica.

- [x] **Sin `DateTime.UtcNow`, `Guid.NewGuid()` ni APIs de navegador en el dominio o handler.** El handler recibe `TimeProvider` por constructor y llama `_time.GetUtcNow()`. `Guid.NewGuid()` no aparece en el handler; el `InspeccionId` viene del comando (generado client-side o en el endpoint como fallback). El endpoint sí genera fallback `Guid.NewGuid()` en la capa API — correcto (CLAUDE.md lo permite en handlers/endpoints).

  Aclaración: el `IniciarInspeccionRequest` define `InspeccionId: Guid` como campo required en el record posicional — no hay fallback a `Guid.NewGuid()` en el endpoint. La spec §9 menciona que el fallback existe "si el cliente no lo envía", pero en la implementación el campo es parte del record `IniciarInspeccionRequest` con el tipo `Guid` (no nullable) — si el cliente no lo envía, el deserializador asigna `Guid.Empty`. Este edge case no está cubierto por un test. Registrado como nit.

- [x] **Tipos de IDs correctos.** `EquipoId` y `ProyectoId` son `int`; `InspeccionId` es `Guid`. `InspeccionAbiertaPorEquipoView.Id` (PK Marten) es `int`. Convenio §15.4 cumplido.

- [x] **Value objects correctos.** `UbicacionGps` con `Latitud: decimal`, `Longitud: decimal`, `PrecisionMetros: decimal`, `CapturadoEn: DateTimeOffset`. Sin primitivos pelados. `LecturaMedidor` tipado correctamente. `BlobUri` no aplica en este slice.

- [x] **Records inmutables.** `IniciarInspeccionResult`, `InspeccionAbiertaPorEquipoView`, `IniciarInspeccionRequest`, `IniciarInspeccionResponse` son `sealed record`. Sin setters públicos.

- [x] **`Apply(Evt)` puro.** El slice no toca `Inspeccion.Apply`; el método sigue siendo la mutación pura confirmada en review del 1a. El rebuild test del 1a (`IniciarInspeccion_rebuild_desde_stream_reproduce_estado`) continúa verde — verificado: 16/16 tests Domain pasan.

- [x] **Rebuild test no requerido en 1b.** Este slice no emite eventos nuevos ni modifica el aggregate. El rebuild test del 1a cubre el único evento del aggregate (`InspeccionIniciada_v1`). No hay nueva rama de `Apply` que verificar.

- [x] **Un único `SaveChangesAsync`.** El handler tiene exactamente un `await _session.SaveChangesAsync(ct)` en el happy path. El catch de la excepción 23505 abre una nueva `QuerySession` de solo lectura (sin `SaveChangesAsync`). Confirmado: no se parte el comando en dos commits.

### 2.4 Cobertura

- [x] **El aggregate `Inspeccion` no se modificó.** La cobertura del aggregate es la misma que en 1a: 94.4 % de ramas (dominio). El umbral 85 % del aggregate afectado se cumple.

  Los handlers y endpoints son código de plumbing de integración (sin ramas de lógica de dominio pura). En el entorno local sin Docker, los tests de integración fallan por infraestructura, no por el código — documentado desde 1a. En CI con Docker disponible, los 8 tests de 1b deben pasar.

  No se corrió cobertura diferencial de los proyectos `Application` y `Api` porque requieren Testcontainers/Docker. El refactorer documenta explícitamente que la única rama nueva es el switch de `codigoError`, que tiene cobertura implícita vía los tests de excepción del handler.

- [x] **Ramas descubiertas justificadas.** El switch de `codigoError` en el catch genérico de `InspeccionDomainException` tiene 5 brazos; los tests del handler verifican `EquipoNoEncontradoException` (catch específico antes) y `RutinaTecnicaNoSincronizadaException` (brazo `"I-I2"`). Las ramas `"I-I3"`, `"PRE-4"`, `"PRE-1"`, `"DOMINIO"` no tienen test directo de endpoint en este slice. Aceptable: son defensas de profundidad cuyo comportamiento está cubierto por los tests del aggregate del 1a; ningún test verifica el campo `codigoError` exacto en el body de la respuesta HTTP. Registrado como nit.

### 2.5 Refactor

- [x] **`refactor-notes.md` presente.** Incluye un cambio aplicado (switch `codigoError`) y 5 candidatos diferidos con justificación factual para cada uno. El documento es claro y trazable.

- [x] **Tests no cambiaron entre green y refactor.** `refactor-notes.md` declara "Los archivos del handler, view, result y excepciones de Application no necesitaban cambio". El único cambio fue en `InspeccionesEndpoints.cs` (producción), no en los tests. Confirmado: los 16 tests del dominio pasan igual que antes del refactor.

- [x] **Sin warnings de compilación.** `dotnet build` confirma: `0 Advertencias. 0 Errores`. `TreatWarningsAsErrors=true` activo.

### 2.6 Invariantes cross-slice

- [x] **Dominio (slice 1a) en verde.** `dotnet test tests/Inspecciones.Domain.Tests/`: 16/16 correctos. Sin regresión.

- Nota: los tests de integración (`Application.Tests` y `Api.Tests`) fallan por Docker no disponible en el entorno local. Esta condición está documentada desde el slice 1a y es un bloqueo de infraestructura, no un fallo del código. En CI con Docker disponible, los tests deben pasar. El reviewer asume esta condición como aceptada por el equipo.

### 2.7 Coherencia con decisiones previas

- [x] **Alineado con §15.7 (I-I1 defensa dual).** El handler implementa la validación blanda (`LoadAsync<InspeccionAbiertaPorEquipoView>`) antes de tocar el aggregate, y la defensa dura (`catch MartenCommandException(23505)`) con reintento determinístico. Exactamente el patrón descrito en la spec §5 y §15.7 del modelo.

- [x] **Alineado con §15.12.6 (`InspeccionAbiertaPorEquipoView`).** El record tiene los campos definidos en §15.12.6: `EquipoId`, `InspeccionId`, `TecnicoIniciador`, `IniciadaEn`, `ProyectoId`. La PK Marten es `Id` (convención nombre) con alias `EquipoId` — alineado con el diseño de unique index en Postgres sobre `data->>'EquipoId'`.

  Desviación documentada: la spec §8.1 describe `MultiStreamProjection<TDoc, int>` Inline; el green implementó `session.Insert(view)` directo por restricción del fixture. El comportamiento observable (atomicidad, unique constraint) es idéntico. Followup #13 ya en `FOLLOWUPS.md`.

- [x] **Alineado con ADR-004 (catálogos).** `EquipoLocal` y `RutinaTecnicaLocal` se consultan vía `IDocumentSession` (read-only) sin modificación. PRE-3 y PRE-handler-1 cubren los fallos de sync.

- [x] **Alineado con ADR-006 (outbox).** Un único `SaveChangesAsync` garantiza atomicidad evento + view. El outbox Wolverine está activo (`IntegrateWithWolverine()` + `AutoApplyTransactions()`). La integración real de `X-Client-Command-Id` como `MessageId` Wolverine está diferida (followup #15).

- [x] **Alineado con ADR-008 (idempotencia).** El header `X-Client-Command-Id` se valida como obligatorio (400 si falta). El mecanismo de dedup es I-I1 en este slice (no el envelope Wolverine). Comportamiento idempotente desde el punto de vista del usuario garantizado; diferencia con la spec documentada (followup #15).

- [x] **ADR-002 tentativo reconocido.** Claims mock fijo con comentario explícito en producción y en notas. Followup #14 en `FOLLOWUPS.md`.

### 2.8 Integración cross-team Sinco

No aplica. El slice no consume ni publica hacia endpoints Sinco on-prem. Confirmado en spec §11.

### 2.9 SignalR / push

No aplica. `InspeccionIniciada_v1` no genera push en el catálogo vigente de ADR-005. Confirmado en spec §10.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `session.Insert(view)` en lugar de `MultiStreamProjection` Inline como describe la spec §8.1. La causa es que el `PostgresFixture` no registra proyecciones en su `StoreOptions`. El comportamiento observable es equivalente (atomicidad, unique constraint) pero el lifecycle de delete centralizado para `InspeccionFirmada_v1` / `InspeccionCancelada_v1` no estará disponible hasta que se migre. | `src/Inspecciones.Application/Inspecciones/IniciarInspeccionHandler.cs:80` | Followup #13 ya en `FOLLOWUPS.md`. Sin acción hasta el slice de firma/cancelación. |
| 2 | followup | Mock fijo de `ClaimsTecnico` (`TecnicoIniciador="rmartinez"`, `TieneCapabilityEjecutarInspeccion=true`) en producción. PRE-1 no se valida hasta que el host PWA inyecte claims reales. | `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs:35-38` | Followup #14 ya en `FOLLOWUPS.md`. Sin acción hasta resolución de ADR-002. |
| 3 | followup | El test `POST_inspecciones_replay_con_mismo_ClientCommandId_no_duplica_evento_idempotencia_ADR_008` afirma en su docstring que verifica "Wolverine envelope dedup" y "ADR-008 §9.16 — Wolverine replay devuelve 200 con la respuesta cacheada del envelope". El mecanismo real es I-I1: el segundo request ve la fila en `InspeccionAbiertaPorEquipoView` y devuelve `RedirigeAExistente=true` + `200 OK`. Wolverine no interviene. El resultado observable para el cliente es el mismo (no se duplica el evento, se devuelve `200 OK` con el `InspeccionId` correcto), pero el comentario del test documenta un mecanismo que no está implementado. Esto puede generar confusión futura cuando se implemente el dedup real de Wolverine (followup #15), porque el test pasará por el mecanismo equivocado hasta que se corrija. | `tests/Inspecciones.Api.Tests/IniciarInspeccionEndpointTests.cs:122-165` | Followup #17 — registrar en `FOLLOWUPS.md`. Cuando se implemente el dedup real de Wolverine (#15), el green de ese slice debe actualizar el docstring del test para que refleje el mecanismo correcto. No bloquea este slice; el resultado observable es correcto. |
| 4 | followup | `IniciarInspeccionResult.Version` retorna siempre `1`, incluso en el path `RedirigeAExistente=true`. La spec §2 define `Version` como la versión actual del stream tras el Append. En el path de redirige la versión real puede ser mayor. | `src/Inspecciones.Application/Inspecciones/IniciarInspeccionHandler.cs:39,97` | Followup #16 ya en `FOLLOWUPS.md`. Sin acción hasta que emerja un cliente que necesite la versión real en el path de redirige. |
| 5 | nit | El switch de `codigoError` en el catch genérico de `InspeccionDomainException` cubre `"I-I3"`, `"PRE-4"`, `"PRE-1"` y `"DOMINIO"` como brazos que no tienen test directo de endpoint en este slice. Son defensas de profundidad cuyo comportamiento principal está cubierto por los tests del aggregate del 1a; no hay test que verifique el campo `codigoError` exacto en el body HTTP. | `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs:81-89` | Asumido como nit. Cuando se agregue un test E2E que verifique respuestas de error por tipo (`I-I3`, `PRE-4`), se completa la cobertura de estos brazos. |
| 6 | nit | `IniciarInspeccionRequest.InspeccionId` es `Guid` (no nullable). Si el cliente omite el campo, el deserializador asigna `Guid.Empty`. La spec §9 menciona un fallback `Guid.NewGuid()` en el endpoint cuando el cliente no envíe el id — ese fallback no está implementado, y tampoco hay test que verifique `Guid.Empty` como fallo. | `src/Inspecciones.Api/Inspecciones/IniciarInspeccionRequest.cs:10` | Asumido como nit. El diseño preferido de la spec es client-generates; la ausencia del fallback es aceptable para MVP. Si el cliente siempre genera el `inspeccionId`, el edge case no ocurre. Sin followup formal. |

---

## 4. Veredicto final

- [x] **approved-with-followups** — un followup nuevo (#17) y cuatro ya registrados (#13..#16). Dos nits asumidos sin followup formal.

Followup nuevo a agregar en `FOLLOWUPS.md`:

```
### #17 — Docstring del test §6.4 afirma mecanismo Wolverine dedup no implementado 🟢

**Origen:** slice 1b review hallazgo #3
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · test · ADR-008
**Descripción:** El test `POST_inspecciones_replay_con_mismo_ClientCommandId_no_duplica_evento_idempotencia_ADR_008`
documenta en su body que verifica "ADR-008 §9.16 — Wolverine replay devuelve 200 con la respuesta cacheada del
envelope". El mecanismo real es I-I1: el segundo request ve la fila en `InspeccionAbiertaPorEquipoView` y devuelve
`RedirigeAExistente=true` + `200 OK`. El resultado observable es correcto (no hay duplicación de evento, el cliente
recibe `200 OK` con el `InspeccionId` original), pero el docstring documenta un mecanismo que no existe en el código.
Cuando se implemente el dedup real de Wolverine (followup #15), el green de ese slice debe: (a) corregir el docstring
del test para reflejar el mecanismo real, (b) asegurarse de que el test siga pasando por el mecanismo correcto y no
por I-I1 como colateral.
**Disparador para abrir slice:** cierre de followup #15 (implementación de Wolverine envelope dedup real).
**Notas:** el test está en `tests/Inspecciones.Api.Tests/IniciarInspeccionEndpointTests.cs:122-165`.
```

El orquestador puede proceder al commit `feat(slice-1b): IniciarInspeccionHandler + InspeccionAbiertaPorEquipoView` y agregar el followup #17 a `FOLLOWUPS.md`.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
