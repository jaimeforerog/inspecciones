# review-notes — fix FU-32

## 1. Resumen del slice

Fix transversal de plumbing de tests E2E. Origen: tests de `Inspecciones.Api.Tests` rotos
preexistentes desde el slice 1g, marcados como FU-32 al destrabar slice 1l. La spec inicial
se centró en `Program.cs` Oakton lifecycle; durante el green afloraron 4 amplificadores
adicionales (config precedence, EventLog, InvariantGlobalization, paralelismo) — todos
plumbing, no dominio. Scope extendido con autorización explícita del usuario el 2026-05-11.

## 2. Cumplimiento de criterios

| Criterio DoD | Estado | Nota |
|---|---|---|
| `spec.md` firmado por el usuario | OK | Extension del scope autorizada en chat 2026-05-11 |
| `red-notes.md` con razón del fallo | OK | §4 inicial + §6 con diagnóstico de los 6 fallos remanentes |
| `green-notes.md` con resultado de `dotnet test` | OK | §2 muestra 30→6 errores |
| Tests pasan tras el fix | PARCIAL | 24/32 passing. Los 6 fallos remanentes son bugs preexistentes en handlers, no del fix |
| `refactor-notes.md` presente | OK | Sin cambios estructurales — decisión justificada |
| Sin warnings | OK | `0 Advertencia(s), 0 Errores` |
| Sin regresión en otros proyectos | OK | `Inspecciones.Domain.Tests` 197/197, idéntico al estado previo |
| Cobertura ≥ 85 % del agregado afectado | N/A | Plumbing infra, no domain |
| Handler Wolverine / proyección Marten / endpoint HTTP | N/A | Plumbing infra |
| Test integración HTTP→Postgres pasa happy path | PARCIAL | 24/32 ejercitan HTTP→Postgres real; 6 fallos por bugs preexistentes |
| Commit único `fix(FU-32): ...` | Pendiente | Después de approval |

## 3. Riesgos identificados

### 3.1 Persistencia de datos entre tests (modo local)

El DROP SCHEMA solo corre en `InitializeAsync` (una vez por fixture). Si un test inserta
data y el siguiente test del mismo collection usa el mismo equipoId, colisiona. Mitigación
actual: cada test usa equipoIds únicos por archivo (16001-16004, 13001-13002, 40001-40007,
60001-60006, 15001-15002). Riesgo bajo siempre que el patrón se respete.

Followup considerable: refactor a fixture **per-test** (no per-collection) si se vuelve
necesario, a costa de ~7s overhead por test.

### 3.2 Modo Testcontainers no testeado en esta sesión

Docker no estaba disponible en el entorno del fix; el modo Testcontainers se preserva
intacto pero no ejecutado. Riesgo: si Testcontainers requiere una connection string con
otro formato, podría romper el CI. Mitigación: el código sigue exactamente el patrón
original (`_postgres.GetConnectionString()`), idéntico al pre-fix.

### 3.3 InvariantGlobalization false en tests

Cambia el comportamiento de cultura de los tests respecto a producción. Riesgo: un test
podría depender (accidentalmente) de cultura `es-CO` y pasar localmente pero fallar en CI
con cultura `en-US`. Mitigación: ningún test actual usa formateo dependiente de cultura;
el override solo destapa los mensajes de FluentAssertions.

## 4. Veredicto

**Approved with followups.**

El fix entrega valor crítico (24 tests destrabados del bloqueo Oakton) y restaura
la capacidad de TDD E2E del proyecto. Los 6 fallos remanentes son hallazgos del fix, no
defectos del fix — el reviewer los registra como FU separados que el squad TDD debe abordar
en slices posteriores con flujo regular (red→green→refactor→review).

### Followups generados

| ID | Tipo | Disparador | Tests afectados |
|---|---|---|---|
| FU-33 | bug | endpoint `POST /inspecciones/{id}/hallazgos` retorna 400 en happy path | `RegistrarHallazgoEndpointTests.POST_inspecciones_id_hallazgos_happy_path_responde_201_Created`, `..._replay_..._ADR_008` |
| FU-34 | bug | handlers `GenerarOT` / `RechazarOT` usan `DateTime.UtcNow` directo (viola regla CLAUDE.md "prohibido en dominio"); deben inyectar `TimeProvider` | `GenerarOTEndpointTests.POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto`, `RechazarGenerarOTEndpointTests.POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto` |
| FU-35 | bug | endpoints `GenerarOT` / `RechazarOT` retornan 500 en vez de 403 cuando falta capability | `..._sin_capability_generar_ot_responde_403_Forbidden_PRE_1` (×2) |

## 5. Próximos pasos del orquestador

1. Registrar FU-33, FU-34, FU-35 en `FOLLOWUPS.md`.
2. Marcar FU-32 como cerrado con SHA del commit.
3. Commit único `fix(FU-32): TestServer/Oakton lifecycle + switch Postgres local + paralelismo xUnit`.
4. Reportar al usuario y proponer próximo slice (sugerencia: abordar FU-34 primero porque
   afecta dos handlers y rompe regla CLAUDE.md de dominio).
