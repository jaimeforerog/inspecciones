# mt-3 — Refactor notes

**Autor:** orquestador (rol refactorer — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario)
**Fecha:** 2026-05-19
**Estado:** verde — 93/93 Infrastructure.Tests pass post-refactor. Build 0/0.

---

## Cambios aplicados

### 1. Extracción de `EnvelopeBearerExtractor` (utility estática)

**Antes:** `private static string? ExtraerBearerForwarded(Envelope envelope)` duplicado en:
- `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs`
- `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs`

15 LoC idénticos por sitio = 30 LoC duplicados.

**Después:** `src/Inspecciones.Infrastructure/Auth/EnvelopeBearerExtractor.cs` con método público estático `ExtraerBearerForwarded(Envelope)`. Documentación cita los 4 casos de retorno (null por: header ausente, scheme distinto, token post-trim vacío; valor: bearer extraído).

Ganancia:
- Mismo punto de cambio si emerge una variante (p. ej. `X-Forwarded-Authorization-V2`).
- Disponible para listeners futuros que necesiten el mismo extract.
- Constantes (`X-Forwarded-Authorization`, `Bearer `) en un solo sitio.

### 2. `using Wolverine` removido del listener `DescartarNovedadPreopErpListener`

Al extraer el helper, el listener ya no necesita usar el tipo `Envelope` en la signature de un método privado (solo en el overload público). El `using Wolverine;` queda — sigue siendo necesario para el parámetro del overload `HandleAsync(NovedadPreopDescartada_v1, Envelope, ct)`.

No-cambio neto en imports — verificado.

---

## NO refactorizado (con justificación)

### 1. Listener no recibe `AmbientBearerTokenAccessor` por DI

Cada listener construye `new AmbientBearerTokenAccessor()` localmente. Cosmético — el storage es `AsyncLocal<string?>` estático, así que cualquier instancia funciona. Inyectarlo por DI no aporta testabilidad (todos los tests del slice ya pasan sin override) y agregaría boilerplate al constructor.

Si emerge necesidad de fake/mock del accessor (improbable — el `AsyncLocal` ya da el aislamiento por contexto), abrir followup.

### 2. `CaptureBearerForOutboxMiddleware` (spec §2) sigue pendiente

Ya documentado como FU-60 en green-notes. No es refactor — es feature pendiente para que MT3-INV-1 funcione end-to-end con outbox (en lugar de solo inline HTTP).

### 3. `HttpContextBearerTokenAccessor` no se hizo singleton

Es `Scoped` para alinear con el lifetime de `IHttpContextAccessor`. Aunque podría ser singleton (no tiene state mutable propio), respetar el lifetime esperado del dependency reduce sorpresas en debug.

### 4. `ChainedBearerTokenAccessor` no parametrizado vía colección

El constructor recibe los 3 accessors por tipo concreto. Podría recibir `IEnumerable<IBearerTokenAccessor>` para orden configurable. Decisión: **prematuro**. El orden HTTP → Ambient → ServiceAccount es semánticamente fijo (más específico → menos específico). Si emerge una cuarta fuente (p. ej. cache de tokens refreshed), abrir followup y re-evaluar.

### 5. Tests de listeners NO refactorizan setup compartido

Los tres archivos de test (`BearerTokenPropagationHandlerTests`, `DescartarNovedadPreopErpListenerTenantTests`, `SincronizarDictamenVigenteBearerPropagationTests`) tienen helpers similares (`ConErpClientConAmbient`). Refactor candidato: extraer a fixture compartida. Decisión: **no aplicado** — los helpers son <15 LoC cada uno y la fixture compartida agregaría dependencia entre archivos de test. Las pruebas leen mejor "self-contained".

---

## Métricas post-refactor

| Métrica | Pre-refactor (green) | Post-refactor |
|---|---|---|
| LoC duplicados en listeners | 30 | 0 |
| Tipos públicos nuevos en `Auth/` | 6 | 7 (+ `EnvelopeBearerExtractor`) |
| Tests pass | 93/93 | 93/93 (idéntico) |
| Build warnings | 0 | 0 |
| Tiempo de ejecución de tests | ~2s | ~2s |

---

## Comando de verificación

```
dotnet build --no-restore                              # 0/0
dotnet test tests/Inspecciones.Infrastructure.Tests/.. # 93/0/0
```

---

## Criterio de paso a review

- [x] Build verde (0 errors, 0 warnings).
- [x] 93/93 Infrastructure.Tests pass — idéntico al green.
- [x] Duplicación eliminada con utility estática (`EnvelopeBearerExtractor`).
- [x] Decisiones de NO-refactor documentadas con justificación.
- [x] Listo para reviewer.
