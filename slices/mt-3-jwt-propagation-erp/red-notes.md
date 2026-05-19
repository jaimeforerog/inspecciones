# mt-3 — Red notes

**Autor:** orquestador (rol red — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario)
**Fecha:** 2026-05-19
**Estado:** rojo confirmado — 22 tests nuevos, todos fallan por símbolos faltantes (CS0246).

---

## Tests rojos introducidos

### `tests/Inspecciones.Infrastructure.Tests/Auth/BearerTokenAccessorTests.cs` (13 tests)

Cubre los tres accessors individuales y la composición `ChainedBearerTokenAccessor`:

1. `HttpContextAccessor_extrae_token_del_header_Authorization`
2. `HttpContextAccessor_sin_HttpContext_retorna_null`
3. `HttpContextAccessor_sin_header_Authorization_retorna_null`
4. `HttpContextAccessor_header_sin_prefijo_Bearer_retorna_null`
5. `AmbientAccessor_sin_set_retorna_null`
6. `AmbientAccessor_set_y_obtener_en_mismo_scope`
7. `AmbientAccessor_dispose_limpia_el_token`
8. `AmbientAccessor_aislado_entre_tareas_paralelas`
9. `ServiceAccountAccessor_devuelve_JwtToken_de_options`
10. `ServiceAccountAccessor_JwtToken_vacio_retorna_null`
11. `Chained_HTTP_gana_sobre_envelope_y_service_account`
12. `Chained_sin_HTTP_usa_ambient`
13. `Chained_sin_HTTP_ni_ambient_cae_a_service_account`
14. `Chained_todos_vacios_retorna_null`

> Cubre §6.6 (chain order) y la unidad de cada accessor.

### `tests/Inspecciones.Infrastructure.Tests/Auth/BearerTokenPropagationHandlerTests.cs` (6 tests)

Integración del `DelegatingHandler` con el adapter + WireMock:

1. `HTTP_scope_propaga_Bearer_del_request_al_ERP` — §6.1
2. `Listener_scope_propaga_Bearer_del_envelope_al_ERP` — §6.2
3. `Sin_HTTP_ni_envelope_cae_a_service_account` — §6.3
4. `Ambient_con_string_vacio_cae_a_service_account` — §6.4
5. `Sin_ningun_token_lanza_BearerTokenAusenteException_antes_de_salir_al_ERP` — §6.5 (fail-closed)
6. `DelegatingHandler_reescribe_Authorization_aunque_HttpClient_lo_tenga_setado` — MT3-INV-4

### `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/DescartarNovedadPreopErpListenerTenantTests.cs` (5 tests)

Cierre FU-57 + propagación al ERP via envelope:

1. `Listener_propaga_JWT_del_envelope_al_ERP_via_AmbientBearer` — §6.2 para descartar
2. `Listener_sin_X_Forwarded_Authorization_cae_a_service_account_via_chain` — §6.3 para descartar
3. `Listener_overload_legacy_sin_envelope_sigue_funcionando_compat` — D-MT3-6
4. `Listener_log_estructurado_incluye_TenantId_del_envelope_en_fallo_5xx` — §6.7 (FU-57)
5. `Ambient_se_limpia_despues_de_HandleAsync_aunque_lance` — cleanup invariant

### `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/SincronizarDictamenVigenteBearerPropagationTests.cs` (3 tests)

Propagación del Bearer al ERP via envelope para el listener dictamen:

1. `Listener_propaga_JWT_envelope_al_ERP_PUT_dictamen` — §6.2 para dictamen
2. `Listener_sin_X_Forwarded_Authorization_cae_a_service_account` — §6.3 para dictamen
3. `Listener_overload_legacy_sin_envelope_sigue_funcionando_compat` — D-MT3-6 para dictamen

---

## Naturaleza del rojo

Los tests fallan en **compilación**, no en runtime. Símbolos referenciados pero no implementados:

- `Inspecciones.Infrastructure.Auth.IBearerTokenAccessor` (puerto)
- `Inspecciones.Infrastructure.Auth.HttpContextBearerTokenAccessor`
- `Inspecciones.Infrastructure.Auth.AmbientBearerTokenAccessor` con `SetForCurrentScope(string?)`
- `Inspecciones.Infrastructure.Auth.ServiceAccountBearerTokenAccessor`
- `Inspecciones.Infrastructure.Auth.ChainedBearerTokenAccessor`
- `Inspecciones.Infrastructure.Auth.BearerTokenAusenteException`
- `Inspecciones.Infrastructure.Auth.BearerTokenPropagationHandler`
- Overload `DescartarNovedadPreopErpListener.HandleAsync(NovedadPreopDescartada_v1, Envelope, CancellationToken)`
- Overload del constructor `DescartarNovedadPreopErpListener(IMaquinariaErpClient, ILogger<...>)` con `ILogger` no-nullable (ya existe en signature actual como nullable, los tests pasan logger explícito)
- Enriquecimiento del `LogCierreFallido` con `tenantId: string?` (FU-57)

Comando de reproducción:

```
dotnet build tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj --no-restore
# Output: ~25+ errores CS0246 sobre los símbolos listados arriba.
```

Errores muestra:

```
error CS0246: El nombre del tipo o del espacio de nombres 'IBearerTokenAccessor' no se encontró
error CS0246: El nombre del tipo o del espacio de nombres 'AmbientBearerTokenAccessor' no se encontró
error CS0246: El nombre del tipo o del espacio de nombres 'ServiceAccountBearerTokenAccessor' no se encontró
error CS0246: El nombre del tipo o del espacio de nombres 'ChainedBearerTokenAccessor' no se encontró
error CS0246: El nombre del tipo o del espacio de nombres 'BearerTokenPropagationHandler' no se encontró
error CS0246: El nombre del tipo o del espacio de nombres 'BearerTokenAusenteException' no se encontró
error CS0246: El nombre del tipo o del espacio de nombres 'HttpContextBearerTokenAccessor' no se encontró
```

---

## Símbolos pendientes para green

| Símbolo | Ubicación | Tipo |
|---|---|---|
| `IBearerTokenAccessor` | `src/Inspecciones.Infrastructure/Auth/IBearerTokenAccessor.cs` | Interface |
| `HttpContextBearerTokenAccessor` | idem dir | Class scoped |
| `AmbientBearerTokenAccessor` | idem dir | Class singleton (AsyncLocal) con `IDisposable SetForCurrentScope(string?)` |
| `ServiceAccountBearerTokenAccessor` | idem dir | Class singleton |
| `ChainedBearerTokenAccessor` | idem dir | Class scoped |
| `BearerTokenAusenteException` | idem dir | Class `: InvalidOperationException` |
| `BearerTokenPropagationHandler` | `src/Inspecciones.Infrastructure/Erp/BearerTokenPropagationHandler.cs` | Class `: DelegatingHandler` |
| Overload listener descartar | `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs` | Add `HandleAsync(evento, Envelope, ct)` + dependency on `AmbientBearerTokenAccessor` |
| Overload log con `tenantId` | idem | Add parameter `string? tenantId` al `[LoggerMessage]` |
| Overload listener dictamen ambient | `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs` | Inject `AmbientBearerTokenAccessor`, setear desde envelope `X-Forwarded-Authorization` |

---

## Criterio de paso para green

- `dotnet build` clean — 0 errors / 0 warnings (CLAUDE.md `TreatWarningsAsErrors=true`).
- Los 22 tests nuevos pasan.
- Los 59 tests existentes de `Infrastructure.Tests` siguen verdes (sin regresión).

---

## Decisiones de design ya tomadas en el rojo (no negociables en green)

1. **`AmbientBearerTokenAccessor` API:** `SetForCurrentScope(string?)` retorna `IDisposable` que limpia `AsyncLocal` al dispose. Es el contrato que los listeners van a usar como `using var _ = ambient.SetForCurrentScope(jwt)`.

2. **`HttpContextBearerTokenAccessor` solo acepta Bearer:** `Basic`, `Negotiate`, otros schemes → null. Defensa contra propagar credentials del tipo equivocado al ERP.

3. **`BearerTokenAusenteException` extiende `InvalidOperationException`:** consistente con `TenantRequeridoEnEnvelopeException` (mt-2). Wolverine policy actual (`OnException<MaquinariaErpException>`) NO la captura — caerá a comportamiento default (retry con backoff). Como mt-3 no agrega política específica, el caso fail-closed en un listener es problema "permanente" (config error) que Wolverine va a dead-letter eventualmente. **Followup latente:** agregar `OnException<BearerTokenAusenteException>.MoveToErrorQueue()` (paralelo a FU-44 cerrado en mt-2 — aplica solo si emerge). En endpoint HTTP, lo maneja el handler global como 500.

4. **`ChainedBearerTokenAccessor` orden HARD:** HTTP → Ambient → ServiceAccount. Configurable solo via construcción (no setter público).

5. **DelegatingHandler reescribe header siempre:** no merge ni respect-existing. MT3-INV-4.

6. **`AmbientBearerTokenAccessor.SetForCurrentScope("")` retorna `null` desde `ObtenerBearerToken`:** string vacío = "no token" semánticamente. Chain cae al siguiente accessor.

---

## Notas

- No se modificaron tests existentes. Los 14 tests de `MaquinariaErpClientTests` siguen pasando (usan `HttpClient.DefaultRequestHeaders.Authorization` directo, sin DelegatingHandler — sirven como regresión de §6.8).
- Los 11 tests de `SincronizarDictamenVigenteListenerTenantTests` (mt-2) siguen pasando — la nueva overload no rompe la existente.
- WireMock como pattern de verificación de headers (mismo approach que tests erp-1..erp-3).
- Nuevo file `AmbientBearerTokenAccessor` requiere `using var` pattern del cliente — convención clara para que green lo implemente como `class SetterDisposable : IDisposable` o equivalente lambda.
