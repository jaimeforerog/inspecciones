# Red Notes — fix-FU-38: Results.Forbid() lanza 500 porque IAuthenticationService no está registrado

**Fecha:** 2026-05-11
**Agente:** red
**Estado:** rojo confirmado — listo para `green`

---

## 1. Tests rojos identificados

Los 2 tests rojos preexisten en el repo. No se escribió ningún test nuevo en este slice.

### Test 1

- **Archivo:** `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs` líneas 292–315
- **Nombre completo:** `Inspecciones.Api.Tests.GenerarOTEndpointTests.POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1`
- **Escenario spec:** §6.3 PRE-1 — capability "generar-ot" ausente → 403 Forbidden (callsite L790 de `InspeccionesEndpoints.cs`)

### Test 2

- **Archivo:** `tests/Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs` líneas 157–176
- **Nombre completo:** `Inspecciones.Api.Tests.RechazarGenerarOTEndpointTests.POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1`
- **Escenario spec:** §6.3 PRE-1 — capability "generar-ot" ausente → 403 Forbidden (callsite L894 de `InspeccionesEndpoints.cs`)

---

## 2. Comando para verificar el rojo

```bash
POSTGRES_TEST_CONNSTRING="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test" \
  dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj \
    --filter "FullyQualifiedName~sin_capability" \
    --no-build
```

Resultado: **2 failing, 0 passing** (duración ~8 s).

---

## 3. Mensajes de fallo precisos

### Test 1 — GenerarOT

```
Expected response.StatusCode to be HttpStatusCode.Forbidden {value: 403}
because PRE-1: el endpoint debe rechazar si el aprobador no tiene capability 'generar-ot',
but found HttpStatusCode.InternalServerError {value: 500}.

at Inspecciones.Api.Tests.GenerarOTEndpointTests
  .POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1()
  in GenerarOTEndpointTests.cs:line 313
```

### Test 2 — RechazarGenerarOT

```
Expected response.StatusCode to be HttpStatusCode.Forbidden {value: 403}
because PRE-1: el endpoint debe rechazar si el aprobador no tiene capability 'generar-ot',
but found HttpStatusCode.InternalServerError {value: 500}.

at Inspecciones.Api.Tests.RechazarGenerarOTEndpointTests
  .POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1()
  in RechazarGenerarOTEndpointTests.cs:line 174
```

---

## 4. Causa raíz confirmada

Ambos tests envían el header `X-Sin-Capability-Generar-OT: true`. El endpoint lo detecta y llama `Results.Forbid()`. `ForbidHttpResult` internamente delega en `IAuthenticationService` para notificar el esquema de auth activo. El módulo no llama `AddAuthentication()` (ADR-002: identidad 100 % del host PWA), por lo que `IAuthenticationService` no está en el contenedor DI. ASP.NET Core lanza `System.InvalidOperationException: Unable to find the required 'IAuthenticationService' service` y el pipeline devuelve HTTP 500.

El fallo es exactamente el que predice la spec §0: la causa raíz está en los callsites de `Results.Forbid()` y la ausencia de `IAuthenticationService`.

---

## 5. Callsites de `Results.Forbid()` en producción (6 ocurrencias)

Verificados con grep sobre `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs`:

| Línea | Endpoint | Condición | Estado |
|-------|----------|-----------|--------|
| L76  | `POST /api/v1/inspecciones` (IniciarInspeccion — 1b) | `catch (ProyectoNoAutorizadoException)` | LATENTE — sin test rojo |
| L157 | `POST /api/v1/inspecciones/monitoreo` (IniciarInspeccionMonitoreo — 1h) | `catch (ProyectoNoAutorizadoException)` | LATENTE — sin test rojo |
| L452 | `POST /api/v1/inspecciones/{id}/firmar` (FirmarInspeccion — 1g) | `catch (CapabilityRequeridaException)` | LATENTE — sin test rojo |
| L456 | `POST /api/v1/inspecciones/{id}/firmar` (FirmarInspeccion — 1g) | `catch (TecnicoNoContribuyenteException)` | LATENTE — sin test rojo |
| L790 | `POST /api/v1/inspecciones/{id}/generar-ot` (GenerarOT — 1k) | header `X-Sin-Capability-Generar-OT` presente | **TEST ROJO VISIBLE** |
| L894 | `POST /api/v1/inspecciones/{id}/rechazar-generar-ot` (RechazarGenerarOT — 1l) | header `X-Sin-Capability-Generar-OT` presente | **TEST ROJO VISIBLE** |

Los 4 callsites latentes NO tienen test rojo asociado en este slice — decisión consciente del spec §1. Se corrigen en el mismo fix para prevenir regresiones; su validación corre dentro de la no-regresión general de los 26 tests que ya pasan.

---

## 6. Alineación con la spec

- Causa raíz: coincide exactamente con spec §0 (`IAuthenticationService` no registrado).
- Fix propuesto: spec §1.2 — helper `Forbidden403(codigoError, mensaje)` que construye `Results.Json(..., statusCode: 403)`.
- Archivo afectado: únicamente `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` (spec §3).
- Resultado esperado: 28 passing, 2 failing (FU-36), 2 skipped (spec §4.1).

---

## 7. Tests nuevos escritos en este slice

Ninguno. Los 2 tests rojos preexistían. No se modificó ningún archivo `.cs` de tests.

---

## 8. Razón del fallo

Ambos tests fallan porque el endpoint invoca `Results.Forbid()` → ASP.NET Core lanza `InvalidOperationException` por `IAuthenticationService` ausente → HTTP 500. El fallo NO es "método no existe" ni "no compila" — es una excepción en runtime observable a través del status code 500 vs. el 403 esperado. El rojo es válido.
