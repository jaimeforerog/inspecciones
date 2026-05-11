# Fix FU-38 — Results.Forbid() lanza 500 porque IAuthenticationService no está registrado

**Autor:** domain-modeler
**Fecha:** 2026-05-11
**Estado:** draft
**Agregado afectado:** ninguno — fix puramente de capa HTTP (endpoints)
**Decisiones previas relevantes:** ADR-002 (identidad 100 % del host PWA — el módulo no registra `AddAuthentication()`); ADR-007 (capability gate en capa HTTP, no en dominio).

---

## 0. Contexto del bug

### Causa raíz

`Results.Forbid()` de ASP.NET Core Minimal APIs internamente crea un `ForbidHttpResult` que, al ejecutarse, delega en `IAuthenticationService` para invocar el handler del esquema de autenticación activo. Esa interfaz se registra automáticamente cuando se llama `builder.Services.AddAuthentication(...)` en `Program.cs`.

El módulo Inspecciones **no** llama `AddAuthentication()`. Decisión: identidad 100 % del host PWA Sinco MYE (ADR-002 tentativo, formalizado el 2026-05-05). El módulo no tiene IdP propio, no tiene app registration, no registra middleware de auth. Consecuencia: `IAuthenticationService` **no existe en el contenedor DI** del módulo.

Cuando el endpoint llama `Results.Forbid()`, el pipeline intenta resolver `IAuthenticationService`, no lo encuentra y lanza:

```
System.InvalidOperationException: Unable to find the required 'IAuthenticationService' service.
```

`DeveloperExceptionPageMiddleware` captura la excepción y devuelve HTTP 500. El cliente recibe 500 donde debería recibir 403.

### Decisión arquitectónica relevante

`Results.Forbid()` está conceptualmente diseñado para flujos de auth con esquemas registrados (cookies, JWT bearer, OIDC). En este módulo, la autenticación la realiza el host PWA; el módulo solo **autoriza** evaluando claims ya extraídos. Un 403 sin esquema de auth activo no requiere `IAuthenticationService` — simplemente hay que devolver la respuesta HTTP directamente.

La alternativa correcta es `Results.Json(new { codigoError, mensaje }, statusCode: 403)`, que construye una respuesta JSON con status 403 sin tocar el pipeline de autenticación. Este patrón es consistente con el resto del archivo (`Results.NotFound(new { codigoError, mensaje })`, `Results.Conflict(new { codigoError, mensaje })`), donde todas las respuestas de error llevan un body tipado con `codigoError` + `mensaje`.

**Alternativas descartadas:**

| Alternativa | Por qué se descarta |
|---|---|
| `TypedResults.Forbid()` / `ForbidHttpResult` | Mismo problema — sigue delegando en `IAuthenticationService`. |
| Registrar `AddAuthentication()` vacío en `Program.cs` | Viola ADR-002. El módulo no debe tener infraestructura de auth propia. Crea falsa dependencia. |
| Custom `IResult` que devuelve 403 | Sobrecarga innecesaria para 6 callsites. `Results.Json(..., statusCode: 403)` con helper estático es suficiente y consistente con el patrón del archivo. |
| `Results.StatusCode(403)` sin body | Rompe consistencia — el cliente no podría distinguir el código de error. |

---

## 1. Scope

### 1.1 Callsites afectados — 6 ocurrencias de `Results.Forbid()` en `InspeccionesEndpoints.cs`

| Línea aprox. | Endpoint (slice) | Excepción / condición | Estado actual |
|---|---|---|---|
| L76 | `POST /api/v1/inspecciones` (`IniciarInspeccion` — 1b) | `catch (ProyectoNoAutorizadoException)` | LATENTE — sin test rojo |
| L157 | `POST /api/v1/inspecciones/monitoreo` (`IniciarInspeccionMonitoreo` — 1h) | `catch (ProyectoNoAutorizadoException)` | LATENTE — sin test rojo |
| L452 | `POST /api/v1/inspecciones/{id}/firmar` (`FirmarInspeccion` — 1g) | `catch (CapabilityRequeridaException)` | LATENTE — sin test rojo |
| L456 | `POST /api/v1/inspecciones/{id}/firmar` (`FirmarInspeccion` — 1g) | `catch (TecnicoNoContribuyenteException)` | LATENTE — sin test rojo |
| L790 | `POST /api/v1/inspecciones/{id}/generar-ot` (`GenerarOT` — 1k) | Header `X-Sin-Capability-Generar-OT` presente | TEST ROJO VISIBLE |
| L894 | `POST /api/v1/inspecciones/{id}/rechazar-generar-ot` (`RechazarGenerarOT` — 1l) | Header `X-Sin-Capability-Generar-OT` presente | TEST ROJO VISIBLE |

Los 4 callsites latentes se corrigen en el mismo fix para prevenir regresiones. No se escriben tests nuevos para ellos en este slice (ver §3).

### 1.2 Helper estático propuesto

Se extrae un helper estático (p. ej. `Forbidden403(string codigoError, string mensaje)`) dentro de `InspeccionesEndpoints.cs` que construye `Results.Json(new { codigoError, mensaje }, statusCode: 403)`. Con 6 callsites el umbral DRY está superado; el helper también garantiza consistencia en el shape del body en futuros callsites.

### 1.3 Fuera de scope

- FU-11 (`CapabilityRequeridaException` dominio vs HTTP) — ortogonal. FU-38 solo corrige el mecanismo de respuesta HTTP; la semántica de qué excepción lanza el dominio es un tema separado.
- Escritura de tests nuevos para los 4 callsites latentes — out of scope de este slice; sería una ampliación adicional.
- Cualquier cambio al dominio, handlers, eventos, agregados, proyecciones, adapters, `Program.cs`.

---

## 2. Tests target (rojos que se vuelven verdes)

Ambos tests ya existen y están fallando con HTTP 500 en lugar de 403:

### Test 1 — `GenerarOTEndpointTests.cs` (slice 1k)

**Método:** `POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1`
**Ubicación:** `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs` líneas 292-315
**Falla actual:** HTTP 500 (`InvalidOperationException: Unable to find the required 'IAuthenticationService' service`)
**Falla esperada tras fix:** HTTP 403 con body `{ "codigoError": "PRE-1", "mensaje": "..." }`

### Test 2 — `RechazarGenerarOTEndpointTests.cs` (slice 1l)

**Método:** `POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1`
**Ubicación:** `tests/Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs` líneas 157-176
**Falla actual:** HTTP 500 (`InvalidOperationException: Unable to find the required 'IAuthenticationService' service`)
**Falla esperada tras fix:** HTTP 403

Nota: los tests de los 4 callsites latentes NO tienen test rojo asociado. La corrección de esos callsites es preventiva — su validación corre dentro de la no-regresión general de los 26 tests que ya pasan.

---

## 3. No toca

- `src/Inspecciones.Domain/**` — sin cambios al dominio.
- `src/Inspecciones.Application/**` — sin cambios a handlers.
- `src/Inspecciones.Api/Program.cs` — sin cambios al host.
- `tests/**` — no se escriben tests nuevos. Los 2 tests rojos ya existen y se volverán verdes con el fix.
- Eventos, agregados, proyecciones, adapters Sinco, hub SignalR.

**Único archivo modificado:** `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs`

---

## 4. Resultado esperado

### 4.1 Conteo de tests

| Estado | Antes del fix | Después del fix |
|---|---|---|
| Passing | 26 | 28 |
| Failing | 4 | 2 |
| Skipped | 2 | 2 |
| Total | 32 | 32 |

Los 2 failing residuales son los tests de `RegistrarHallazgo` bloqueados por FU-36 (bug independiente). Los 2 skips son los tests de idempotencia ADR-008 (Wolverine envelope dedup, marcados antes de este slice).

### 4.2 Working tree tras el fix

```
 M src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs
```

Un solo archivo modificado. Sin archivos nuevos.

### 4.3 Body de la respuesta 403

Todos los callsites deben producir un body JSON con la estructura:

```json
{ "codigoError": "<CODIGO>", "mensaje": "<mensaje>" }
```

Los valores de `codigoError` para cada callsite:

| Callsite | codigoError | Fuente |
|---|---|---|
| `IniciarInspeccion` — `ProyectoNoAutorizadoException` | `"PRE-3-PROYECTO"` | spec 1b §9 |
| `IniciarInspeccionMonitoreo` — `ProyectoNoAutorizadoException` | `"PRE-3-PROYECTO"` | spec 1h §9 |
| `FirmarInspeccion` — `CapabilityRequeridaException` | `"PRE-1"` | spec 1g §9 |
| `FirmarInspeccion` — `TecnicoNoContribuyenteException` | `"PRE-F3"` | spec 1g §9 |
| `GenerarOT` — header `X-Sin-Capability-Generar-OT` | `"PRE-1"` | spec 1k §9 |
| `RechazarGenerarOT` — header `X-Sin-Capability-Generar-OT` | `"PRE-1"` | spec 1l §9 |

**Pregunta pre-firma (ver §12):** los `codigoError` de los 4 callsites latentes no tienen un test rojo que los valide en este slice. El verde asume que los specs de los slices correspondientes (1b, 1g, 1h) ya establecen los códigos correctos. Si hay discrepancia, `red`/`green` deben reportarla sin bloquear.

---

## 5. Riesgos

### R-1 — Consistencia del shape del body en todos los catch-blocks

El helper `Forbidden403(string codigoError, string mensaje)` fuerza `{ codigoError, mensaje }` en los 6 callsites. El riesgo es que algún callsite latente actualmente pase un `null` o un string vacío como `mensaje` (dado que `Results.Forbid()` original no requería body).

**Mitigación:** el rol `green` debe verificar, catch-block por catch-block, que cada callsite dispone de un `mensaje` válido. Para los catch que reciben excepciones tipadas (p. ej. `catch (ProyectoNoAutorizadoException ex)`), se usa `ex.Message`. Para el catch del header `X-Sin-Capability-Generar-OT` (que no es excepción), se proporciona un mensaje literal fijo (p. ej. `"Capability 'generar-ot' requerida."`).

### R-2 — Deserialización del body 403 en los tests existentes

Los 2 tests rojos (Test 1 y Test 2 en §2) solo verifican `response.StatusCode == 403`. No deserializan el body. El fix no rompe esa aserción, pero si en el futuro se añade aserción sobre el body, el shape debe ser `{ codigoError, mensaje }`.

### R-3 — Regresión en los 26 tests verdes actuales

Los 26 tests verdes no ejercen ninguno de los 6 callsites de `Forbid()`. La modificación de `InspeccionesEndpoints.cs` es quirúrgica — solo los catch-blocks y el helper. No hay riesgo de regresión si el helper está correctamente aislado.

---

## 6. Definición de Done

- [ ] Los 2 tests rojos (`GenerarOT.sin_capability` y `RechazarOT.sin_capability`) pasan en verde (HTTP 403).
- [ ] Los 26 tests que estaban verdes antes del fix siguen verdes (cero regresiones).
- [ ] El helper estático compila sin warnings (`nullable` habilitado, `TreatWarningsAsErrors=true`).
- [ ] Los 6 callsites de `Results.Forbid()` están eliminados del archivo.
- [ ] Suite completa: 28 passing, 2 failing (FU-36), 2 skipped.
- [ ] Commit: `fix(FU-38): reemplazar Results.Forbid() por Forbidden403 helper — IAuthenticationService no registrado`.
- [ ] No se modificó ningún archivo fuera de `InspeccionesEndpoints.cs`.

---

## 7. Idempotencia / retries

No aplica. Este fix no modifica comportamiento del dominio ni introduce nuevas rutas de escritura. No hay comandos que se reintenten. El endpoint devuelve 403 de forma determinista cuando la condición de capability falla — idempotente por construcción.

---

## 8. Impacto en proyecciones / read models

No aplica. El fix no emite ni consume eventos. No toca proyecciones.

---

## 9. Impacto en endpoints HTTP

No hay endpoints nuevos ni cambios en rutas, métodos o DTOs. El único cambio observable desde el cliente es que las condiciones que antes devolvían 500 ahora devuelven 403 con body `{ codigoError, mensaje }`. Este es el comportamiento correcto que los specs de los slices 1k y 1l ya declaran.

---

## 10. Impacto en SignalR / push

No aplica. El fix solo afecta rutas de error (HTTP 403) que no llegan al dominio ni al hub.

---

## 11. Impacto en adapters Sinco on-prem

No aplica. Los callsites de `Forbid()` son todos en la capa de validación HTTP, previos a cualquier llamada al ERP.

---

## 12. Preguntas abiertas

- [ ] **`codigoError` para `ProyectoNoAutorizadoException` en `IniciarInspeccion` y `IniciarInspeccionMonitoreo`:** el diagnóstico asigna `"PRE-3-PROYECTO"` basándose en la conv. del archivo, pero los specs 1b/1h pueden haber definido un código distinto. Como los 4 callsites latentes no tienen test rojo en este slice, el valor correcto debe ser confirmado por el usuario antes de que `green` escriba el fix, o bien `green` lo toma de los specs correspondientes y lo documenta en `green-notes.md`.

---

## 13. Checklist pre-firma

- [x] Causa raíz documentada con evidencia (stack trace, decisión ADR-002).
- [x] Scope delimitado: 1 archivo, 6 callsites, 1 helper.
- [x] Alternativas al fix descartadas con justificación en §0.
- [x] Tests target identificados con archivo, línea y falla actual.
- [x] Tests que NO se escriben en este slice justificados (4 latentes — out of scope).
- [x] Resultado esperado con conteo de tests antes/después.
- [x] Shape del body 403 especificado con `codigoError` por callsite.
- [x] Riesgos documentados con mitigación.
- [x] DoD con criterios verificables y commit message.
- [x] §§ 7-11 resueltos (no aplica / justificado).
- [ ] Pregunta abierta §12 respondida por el usuario antes de avanzar a `green`.
