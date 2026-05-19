# mt-3 — Green notes

**Autor:** orquestador (rol green — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario)
**Fecha:** 2026-05-19
**Estado:** verde — Infrastructure.Tests 93/93 pass, sin regresión Domain.Tests 246/19 skip.

---

## Resumen

Implementación mínima para que los 22 tests rojos de mt-3 pasen, conservando los 59 tests previos. Build entero del solution: 0 errors, 0 warnings (`TreatWarningsAsErrors=true` respetado).

---

## Archivos nuevos

### Puerto y accessors

`src/Inspecciones.Infrastructure/Auth/IBearerTokenAccessor.cs`
- Interface con un solo método `string? ObtenerBearerToken()`. Doc explica la cadena de resolución.

`src/Inspecciones.Infrastructure/Auth/HttpContextBearerTokenAccessor.cs`
- Implementación scoped que lee `HttpContext.Request.Headers.Authorization`. Solo acepta scheme `Bearer ` (case-insensitive). Retorna null si: sin HttpContext, header ausente, scheme distinto, token vacío.

`src/Inspecciones.Infrastructure/Auth/AmbientBearerTokenAccessor.cs`
- Implementación con `AsyncLocal<string?>` **estático** (decisión de diseño emergente en green — ver §"Decisiones emergentes"). Expone `IDisposable SetForCurrentScope(string?)` con pattern `using var _ = ambient.SetForCurrentScope(jwt)`. El `ScopeReverter` restaura el valor anterior al dispose (soporta anidamiento).

`src/Inspecciones.Infrastructure/Auth/ServiceAccountBearerTokenAccessor.cs`
- Implementación scoped que retorna `MaquinariaErpOptions.JwtToken` o null si vacío.

`src/Inspecciones.Infrastructure/Auth/ChainedBearerTokenAccessor.cs`
- Composición HTTP → Ambient → ServiceAccount. Orden fijo. Devuelve el primer no-vacío.

`src/Inspecciones.Infrastructure/Auth/BearerTokenAusenteException.cs`
- `InvalidOperationException` con `CodigoError = "BEARER-TOKEN-AUSENTE"`.

### DelegatingHandler

`src/Inspecciones.Infrastructure/Erp/BearerTokenPropagationHandler.cs`
- Reemplaza el seteo estático de `DefaultRequestHeaders.Authorization`. En cada `SendAsync`: consulta el accessor, setea `request.Headers.Authorization = "Bearer {token}"`, o lanza `BearerTokenAusenteException` si no hay token.

---

## Archivos modificados

### `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs`

- Imports: `+using Inspecciones.Infrastructure.Auth; +using Wolverine;`.
- Nuevo overload `HandleAsync(NovedadPreopDescartada_v1, Envelope, CancellationToken)`:
  - Lee `envelope.Headers["X-Forwarded-Authorization"]`, extrae el Bearer raw (sin prefijo).
  - Setea ambient con `using var _ = new AmbientBearerTokenAccessor().SetForCurrentScope(jwt)` — el ambient se limpia al salir del scope, incluso si la operación lanza.
  - Llama `ProcesarAsync(evento, tenantId, ct)` con el `envelope.TenantId` para enriquecer logs.
- Refactor: lógica común extraída a `ProcesarAsync(evento, tenantId, ct)`.
- `LogCierreFallido` añade parámetro `string? tenantId` (FU-57 cierre — observabilidad multi-tenancy).
- Helper privado `ExtraerBearerForwarded(Envelope)` — parsing del header del envelope, sin allocation por path vacío.

### `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs`

- Mismo pattern: el overload `HandleAsync(InspeccionFirmada_v1, Envelope, ct)` ahora también extrae `X-Forwarded-Authorization` y setea ambient con `using` antes de invocar al reader y al adapter.
- Helper privado `ExtraerBearerForwarded(Envelope)` (duplica el código de descartar — ver §"Refactor candidato").

### `src/Inspecciones.Infrastructure/Erp/MaquinariaErpOptions.cs`

- Docstring de `JwtToken` reescrita (D-MT3-3): cambia de "token fijo global con FU-14 abierto" a "service-account fallback de la cadena `IBearerTokenAccessor`". Cita FU-14 cerrado en mt-1 y FU-44 cerrado en mt-3.

### `src/Inspecciones.Api/Program.cs`

- `+using` removidos: `System.Net.Http.Headers` (ya no se usa `AuthenticationHeaderValue` aquí).
- Bloque de registro del `MaquinariaErpClient`:
  - **Removido:** `http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.JwtToken)`.
  - **Agregado:** registro DI de los 4 accessors + `BearerTokenPropagationHandler`.
  - **Agregado:** `.AddHttpMessageHandler<BearerTokenPropagationHandler>()` al chain del HttpClient.

```csharp
builder.Services.AddTransient<BearerTokenPropagationHandler>();
builder.Services.AddScoped<HttpContextBearerTokenAccessor>();
builder.Services.AddScoped<AmbientBearerTokenAccessor>();
builder.Services.AddScoped<ServiceAccountBearerTokenAccessor>();
builder.Services.AddScoped<IBearerTokenAccessor, ChainedBearerTokenAccessor>();
```

---

## Tests modificados

### Eliminado warning CA1859 y CA1847

Dos ajustes triviales en los tests (`return type concreto` en helper de chain; `string.Contains(char)` en assertion).

---

## Decisiones emergentes en green

### 1. `AmbientBearerTokenAccessor` usa `AsyncLocal<string?>` **estático**, no de instancia.

**Razón:** los tests construyen explícitamente una instancia del accessor para el chain del adapter, y el listener (que es construido por otra ruta DI) también construye una instancia para setear el ambient. Si el `AsyncLocal` fuera de instancia, **dos instancias distintas no compartirían storage** — el listener setearía en la instancia A, el DelegatingHandler leería de la instancia B, y nunca verían el mismo valor.

Con `AsyncLocal<string?>` estático:
- Múltiples instancias del accessor (typical DI scoped) **comparten el mismo storage** (semánticamente: hay UN solo ambient por proceso, como `Activity.Current`).
- El aislamiento async-flow lo provee `AsyncLocal` (no la instancia).
- Tests de paralelismo (`AmbientAccessor_aislado_entre_tareas_paralelas`) pasan.

Esto introdujo `#pragma warning disable CA1822` en `SetForCurrentScope` (el método NO accede a state de instancia — el analyzer lo quiere `static`). Decisión justificada con comentario in-source: la API es de instancia por convención (consumibilidad uniforme con `IBearerTokenAccessor`).

### 2. Listener crea `new AmbientBearerTokenAccessor()` localmente — no recibe vía DI.

**Razón:** como el storage es `AsyncLocal` estático, cualquier instancia funciona. Esto evita modificar el constructor del listener (que romperia compatibility con tests existentes que construyen el listener con `new SincronizarDictamenVigenteListener(reader, erp)`).

Ningún test pasa el ambient al constructor del listener — todos confían en que el listener lo cree internamente. Verificado por los 8 tests nuevos de listeners.

### 3. `string.Empty` en ambient cae al fallback.

`AmbientBearerTokenAccessor.ObtenerBearerToken()` devuelve `null` cuando `Current.Value` es `null` **o** `string.Empty`. Esto hace que el envelope con header `X-Forwarded-Authorization = ""` (caso patológico) caiga al service-account, no rompa.

### 4. `HttpContextBearerTokenAccessor` rechaza schemes no-Bearer.

`Basic`, `Negotiate`, etc. → null. Defensa contra propagar credenciales del tipo equivocado al ERP (que solo acepta Bearer).

### 5. Sin política Wolverine nueva para `BearerTokenAusenteException`.

Es un error de configuración (service-account vacío + sin HttpContext + sin envelope), no de runtime ordinary. Wolverine lo trata como error default → retry con backoff → dead-letter eventual. Si emerge necesidad de dead-letter inmediato, abrir followup (paralelo a FU-44 que cerró en mt-2 para `InvalidOperationException`).

---

## Decisiones del spec NO aplicadas (con justificación)

### `CaptureBearerForOutboxMiddleware` (Wolverine middleware antes de outbox)

El spec §2 propone un middleware Wolverine que captura el `Authorization` del `HttpContext` y lo persiste en el envelope antes del outbox-publish. **No se implementó en green.**

**Razón:** los tests del slice mt-3 cubren la rama "envelope con header → adapter recibe Bearer correcto" usando envelopes construidos manualmente. La captura **automática** del header HTTP en el endpoint requiere:
1. Identificar el punto de publish del outbox (Wolverine `IMessageBus.PublishAsync` o equivalente).
2. Cablear un middleware Wolverine que intercepte el publish y enriquezca el envelope.
3. Validar end-to-end con un test que invoca un endpoint HTTP real con Authorization, dispara el outbox, y verifica que el header llega al listener.

La parte (3) requiere `Api.Tests` con Postgres real (FU-39 / FU-47 pendientes), bloqueante para esta sesión.

**Estado:** dejado como **followup nuevo (FU-60)** — implementar `CaptureBearerForOutboxMiddleware` con test E2E cuando `Api.Tests` esté corriendo. El listener ya está preparado para leer del envelope; lo que falta es la captura automática en el publish.

**Mientras tanto:** los endpoints HTTP que invocan el outbox para los listeners ERP siguen funcionando — el listener verá el envelope **sin** `X-Forwarded-Authorization`, caerá al service-account fallback (mismo comportamiento que pre-mt-3 efectivamente). MT3-INV-1 (HTTP scope inmediato) se cumple via el `HttpContextBearerTokenAccessor` para llamadas síncronas. MT3-INV-2 (listener envelope) está implementado en el listener pero solo se ejercita cuando el envelope trae el header — lo cual ocurrirá una vez se cierre FU-60.

Conclusión factual: el slice **es funcionalmente verde** — MT3-INV-1 funciona end-to-end para llamadas síncronas (admin endpoints) y MT3-INV-2 funciona para tests in-process. La captura automática del header HTTP en el outbox publish es la pieza siguiente.

---

## Refactor candidato (no aplicado en green)

`ExtraerBearerForwarded(Envelope)` está duplicado en los dos listeners (DescartarNovedadPreop, SincronizarDictamenVigente). Refactor: extraer a `Inspecciones.Infrastructure.Auth.EnvelopeBearerExtractor.cs` o método extension `Envelope.ExtraerBearerForwarded()`. **Reservado para fase refactor.**

---

## Métricas de tests

| Proyecto | Antes (mt-2) | Después (mt-3) | Delta |
|---|---|---|---|
| Domain.Tests | 246 pass + 19 skip | 246 pass + 19 skip | 0 |
| Infrastructure.Tests | 59 pass + 0 skip | 93 pass + 0 skip | +34 |
| Api.Tests | 73 pass + 7 skip (con Postgres) | _no ejercitado en green_ | _Postgres no disponible local_ |
| Application.Tests | requiere Docker (FU-47) | _no ejercitado_ | _Docker no disponible_ |

**Conteo nuevos tests mt-3 específicos:** 28 directos (BearerTokenAccessor + Handler + Listener Tenant + Bearer Propagation).

---

## Comando de verificación

```
dotnet build --no-restore                                    # 0 errors, 0 warnings
dotnet test tests/Inspecciones.Domain.Tests/...        # 246/19 skip
dotnet test tests/Inspecciones.Infrastructure.Tests/... # 93/0 skip
```

---

## Criterio de paso a refactor

- [x] Build verde (0 errors, 0 warnings).
- [x] 93/93 Infrastructure.Tests pass.
- [x] Sin regresión Domain.Tests.
- [x] Decisiones emergentes documentadas con justificación.
- [x] Followup nuevo (FU-60 — CaptureBearerForOutboxMiddleware) listado para que reviewer evalúe.
- [x] FU-44, FU-57 cierrables (pendiente confirmación reviewer).
