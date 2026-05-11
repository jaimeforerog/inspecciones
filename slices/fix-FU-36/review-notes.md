# Review notes — fix-FU-36 — JsonStringEnumConverter ausente en Minimal APIs

**Autor:** reviewer
**Fecha:** 2026-05-11
**Slice auditado:** `slices/fix-FU-36/`
**Veredicto:** `approved`

---

## 1. Resumen ejecutivo

El fix aplica `ConfigureHttpJsonOptions` con `JsonStringEnumConverter` en `Program.cs`, corrigiendo el 400 BadRequest que bloqueaba el endpoint `POST /inspecciones/{id}/hallazgos` desde que FU-32 destrabó `WebApplicationFactory<Program>`. Los 2 tests rojos del slice 1c pasan a 1 verde + 1 skip con justificación correcta (Wolverine envelope dedup no disponible en Testcontainers). El comentario anticipatorio de `Program.cs:28-30` fue eliminado conforme a la spec. Sin blockers ni followups nuevos.

---

## 2. Checklist de auditoría

### 2.1 Spec vs tests

- [x] Cada escenario de `spec.md §2` tiene correspondencia: Test 1 (happy path) ahora verde; Test 2 (ADR-008) correctamente skipped con el mecanismo real documentado en el skip message.
- [x] No hay precondiciones de dominio en este fix (puramente configuracion HTTP) — criterio N/A.
- [x] No hay invariantes del agregado tocadas — criterio N/A.
- [x] Los nombres de los tests son frases descriptivas en español completo: `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created`, `POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008`. Pasan.

### 2.2 Tests como documentacion

- [x] Given/When/Then visible estructuralmente en `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` (comentarios en el cuerpo del test).
- [x] Cero mocks del dominio — el test usa `WebApplicationFactory<Program>` con Postgres real en Testcontainers.
- [x] `UbicacionGps` usada correctamente con coordenadas plausibles para Colombia (`4.711m, -74.072m`) en `SembrarInspeccionConEquipo`.

### 2.3 Implementacion

- [x] `using System.Text.Json.Serialization;` presente en linea 1 de `Program.cs`.
- [x] Bloque `ConfigureHttpJsonOptions` con `JsonStringEnumConverter` registrado antes de `builder.Build()` (lineas 114-119 de `Program.cs`).
- [x] Comentario de las lineas 28-30 originales eliminado — verificado: la seccion AddMarten no contiene el comentario historico.
- [x] El nuevo banner del bloque (`// JSON serializer — Minimal APIs: enums como string en request y response bodies.`) es documentacion de comportamiento activo, no historia de desarrollo. La linea referencial `// FU-36: cierra el comentario...` fue eliminada en refactor — correcto.
- [x] Sin `DateTime.UtcNow` introducido — ninguna modificacion toca logica de dominio o tiempo.
- [x] Sin primitivos pelados introducidos.
- [x] Sin setters publicos en eventos nuevos — no hay eventos nuevos en este fix.
- [x] `Apply` puro — no hay cambios en el agregado.
- [x] Rebuild test — no aplica (ningun evento emitido en este fix).
- [x] Un unico `SaveChangesAsync` por comando — no aplica (no hay handler nuevo).
- [x] `CapturadoEn` armonizado a `new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero)` en `RegistrarHallazgoEndpointTests.cs:20` — coincide con el `FakeTimeProvider` canonico registrado en `InspeccionesAppFactory.cs` (patron establecido en FU-37).
- [x] Skip ADR-008 con `[Fact(Skip = ...)]` en formato identico al patron canonico del repo (`GenerarOTEndpointTests.cs §6.9`, `RechazarGenerarOTEndpointTests.cs §6.13`).

### 2.4 Cobertura

- [x] Este fix no modifica codigo de dominio ni logica de negocio. La cobertura del agregado `Inspeccion` es invariante respecto al slice anterior. El criterio >= 85 % se mantiene por construction (ningun branch del agregado fue tocado).
- [x] El unico codigo nuevo es la llamada `ConfigureHttpJsonOptions` en `Program.cs` — ejercida directamente por el test `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` que ahora pasa.

### 2.5 Refactor

- [x] `refactor-notes.md` presente con 1 cambio documentado (eliminacion de la linea referencial al comentario previo) y 3 refactors descartados con justificacion.
- [x] Los tests no cambiaron de logica entre green y refactor — el unico cambio de refactor fue en `Program.cs` (eliminar 1 linea de comentario historico), no en tests.
- [x] `dotnet build` — 0 Advertencias, 0 Errores (confirmado en refactor-notes §Output).

### 2.6 Invariantes cross-slice

- [x] `dotnet test Inspecciones.Api.Tests` completo: **29 passing, 3 skip, 0 failing**. Sin regresion en los 28 tests que estaban verdes antes del fix.
- [x] `dotnet test Inspecciones.Domain.Tests` completo: **197 passing, 12 skip, 0 failing**. Sin regresion.

Output real de ejecucion:

```
# Inspecciones.Api.Tests
Pruebas totales: 32
     Correcto: 29
    Omitido: 3
 Tiempo total: 8,9789 Segundos

# Inspecciones.Domain.Tests
Correctas! - Con error: 0, Superado: 197, Omitido: 12, Total: 209, Duracion: 105 ms
```

### 2.7 Coherencia con decisiones previas

- [x] Alineado con `01-modelo-dominio.md §15` — el fix no toca modelo de dominio.
- [x] Alineado con ADR-001 (REST/VPN), ADR-002 (auth), ADR-003, ADR-004, ADR-005, ADR-006 — ninguno afectado.
- [x] El spec documenta explicitamente por que no se agrega `JsonNamingPolicy` ahora (riesgo de romper 28 tests verdes con asserts PascalCase) — decision arquitectonica justificada en spec §0 y §5.
- [x] El spec documenta por que los DTOs con `string + Enum.TryParse` (`GenerarOTRequest`, `RegistrarEvaluacionCualitativaRequest`) quedan como deuda separada — spec §1.2 lo excluye explicitamente del scope con razon valida (no hay test rojo que lo pida).
- [x] FU-36 marcado como abierto en `FOLLOWUPS.md` antes de este fix y fue la motivacion del slice. El cierre de FU-36 se documenta aqui: el fix fue aplicado.

### 2.8 Integracion cross-team Sinco

- [x] No aplica. El fix es de configuracion del serializer HTTP, anterior a cualquier interaccion con el ERP.

### 2.9 SignalR / push

- [x] No aplica. El fix opera en la deserializacion del request body, antes de que el handler se ejecute y antes de cualquier flujo SignalR.

---

## 3. Hallazgos

| # | Tipo | Descripcion | Ubicacion | Accion sugerida |
|---|---|---|---|---|
| — | — | Sin hallazgos | — | — |

No se identificaron blockers, followups ni nits. El scope del fix es el minimo necesario, esta respaldado por evidencia (stack trace, ruta `$.origen`), y los desvios de scope (armonizacion `CapturadoEn`, skip ADR-008) tienen precedente documentado en FU-37 y patron canonico del repo.

---

## 4. Veredicto final

- [x] **approved** — sin hallazgos. Los 2 tests rojos del slice 1c quedan en estado correcto (1 verde + 1 skip con justificacion valida). La suite completa esta en verde. FU-36 cerrado.

---

_El orquestador puede proceder al commit `fix(FU-36): JsonStringEnumConverter en Minimal APIs — enums deserializados como string` y actualizar FU-36 en `FOLLOWUPS.md` como cerrado._
