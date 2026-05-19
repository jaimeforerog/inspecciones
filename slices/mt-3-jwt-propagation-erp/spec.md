# Slice mt-3 — Propagación del JWT entrante al `MaquinariaErpClient` + estrategia de listeners

**Autor:** orquestador (rol domain-modeler — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-3)
**Fecha:** 2026-05-19
**Estado:** **Firmado: 2026-05-19** — autor firma: Usuario (Santiago Ramirez), autorización pre-otorgada en el ciclo mt-3.
**Agregado afectado:** ninguno (el aggregate `Inspeccion` y sus eventos `_v1` no se tocan — D3 firmada en mt-1). El slice opera sobre el adapter `MaquinariaErpClient` + listeners Wolverine + el wiring de Wolverine outbox (captura del JWT entrante en el envelope).
**Decisiones previas relevantes:**
- `slices/mt-1-jwt-claims-pipeline/spec.md` §0.D1..D7 — claims canónicos, bypass `FakeSessionService` en env Test, FU-44 explícitamente rolleado a mt-3.
- `slices/mt-2-marten-conjoined-tenancy/spec.md` §D-MT2-2 — listeners reciben tenant del envelope. `Envelope.TenantId` ya está poblado; añadir headers custom es ortogonal.
- `Inspecciones/docs/00-investigacion-mercado.md §9.17` — ADR-009 multi-tenancy (mt-3 lo extiende con nota sobre JWT propagation).
- `Inspecciones/docs/00-investigacion-mercado.md §9.X (ADR-006)` — política de reintentos outbox 5s→30s→2m→10m. mt-3 agrega nota sobre JWT expirado en retry.
- `Inspecciones/docs/06-contrato-apis-erp.md §0.B.5` — 5 claims del JWT (UsuarioId, NomUsuario, IdEmpresa, IdSucursal, IdProyecto). `IdEmpresa` viaja implícito en el Bearer.
- `FOLLOWUPS.md #44` — propagación JWT al ERP (cierra acá).
- `FOLLOWUPS.md #57` — logs estructurados con tenant en `DescartarNovedadPreopErpListener` (cierra acá).
- `src/Inspecciones.Infrastructure/Erp/MaquinariaErpClient.cs` — adapter HTTP actual, `HttpClient.DefaultRequestHeaders.Authorization` fijo.
- `src/Inspecciones.Infrastructure/Erp/MaquinariaErpOptions.cs` — `JwtToken` global con FU-14 (ya cerrado por mt-1).

---

## 1. Intención

Al cierre de mt-2, el módulo aísla data por `IdEmpresa` dentro de Marten + Wolverine — pero **las llamadas al ERP Maquinaria_V4 todavía usan un token fijo de servicio** (`MaquinariaErpOptions.JwtToken`) configurado al startup. Esto rompe la cadena de identidad cross-process: cuando un técnico de la empresa 7 ejecuta `POST /api/v1/catalogos/sync` o cuando el listener `SincronizarDictamenVigenteListener` publica un `PUT /equipos/{id}/dictamen-vigente`, Maquinaria_V4 ve **siempre el mismo usuario de servicio**, no al usuario originador. Implicaciones:

1. **Audit del ERP es genérico** — Maquinaria_V4 atribuye toda escritura a la cuenta de servicio, no al técnico.
2. **Multi-tenancy del ERP no se honra** — Maquinaria_V4 también es multi-empresa (asumimos paridad con Inspecciones hasta evidencia contraria); el JWT del host trae `IdEmpresa` que el ERP valida. Si el token es genérico, todos los requests caen sobre la empresa hard-coded en el token de servicio.
3. **`MaquinariaErpOptions.JwtToken` cita FU-14** como deuda; FU-14 cerró en mt-1 (ya hay `ISessionService` disponible en scope HTTP).

Este slice **propaga el Bearer del request HTTP entrante al `MaquinariaErpClient`** (D-MT3-1) y resuelve la estrategia para listeners Wolverine (D-MT3-2): el JWT viaja en el envelope Wolverine como header custom (`X-Forwarded-Authorization`), capturado por un middleware Wolverine en el handler HTTP origen y leído por el listener antes de invocar al adapter. El token de servicio (`MaquinariaErpOptions.JwtToken`) **no se elimina** — cambia su rol a **fallback** para casos sin caller (sagas cron, retries con JWT expirado, bootstrap).

Con mt-3 cerrado, la cadena de identidad del usuario que originó cada acción del módulo viaja end-to-end hasta el ERP, manteniendo la política de outbox + retry de ADR-006.

---

## 2. Comando

Este slice **no es event-sourced**. No hay aggregate ni comando de dominio nuevo. El "comando" lógico es la apertura de cada llamada HTTP del `MaquinariaErpClient` con el Bearer correcto.

Se modela como **el contrato del puerto** introducido:

```csharp
namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Puerto de acceso al raw Bearer token del request actual o del envelope Wolverine.
/// Lo consume <see cref="MaquinariaErpClient"/> (via DelegatingHandler) para propagar
/// la identidad del caller cross-process al ERP Maquinaria_V4.
///
/// Distinto a <see cref="ISessionService"/> (claims parseadas del JWT): este puerto
/// expone el token raw, sin parsing. Mezclar ambos viola SRP.
///
/// Implementaciones:
/// - <see cref="HttpContextBearerTokenAccessor"/> — scope HTTP. Lee
///   <c>HttpContext.Request.Headers.Authorization</c> y extrae el Bearer.
/// - <see cref="AmbientBearerTokenAccessor"/> — scope listener Wolverine.
///   Expone <c>AsyncLocal&lt;string?&gt;</c> que el listener setea al inicio
///   de cada <c>HandleAsync(evento, envelope, ct)</c> con el contenido de
///   <c>envelope.Headers["X-Forwarded-Authorization"]</c>. El DelegatingHandler
///   lo lee cuando no hay HttpContext.
/// - <see cref="ServiceAccountBearerTokenAccessor"/> — fallback. Lee
///   <see cref="MaquinariaErpOptions.JwtToken"/>.
///
/// Composición en producción: <see cref="ChainedBearerTokenAccessor"/> intenta
/// HTTP → ambient → service-account en orden, devolviendo el primero no-vacío.
/// </summary>
public interface IBearerTokenAccessor
{
    /// <summary>
    /// Devuelve el raw Bearer token a propagar al ERP, o <c>null</c> si no hay
    /// ningún token disponible (todos los fallbacks fallaron).
    ///
    /// El caller (DelegatingHandler) decide si fallar la request o continuar
    /// sin Authorization. La política <see cref="MT3-INV-3"/> dice fail-closed.
    /// </summary>
    string? ObtenerBearerToken();
}
```

Implementación del adapter:

```csharp
// Reemplaza http.DefaultRequestHeaders.Authorization fijo por DelegatingHandler.
public sealed class BearerTokenPropagationHandler : DelegatingHandler
{
    private readonly IBearerTokenAccessor _accessor;
    public BearerTokenPropagationHandler(IBearerTokenAccessor accessor) { _accessor = accessor; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var token = _accessor.ObtenerBearerToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            // MT3-INV-3: fail-closed. Nunca se envía request anónimo al ERP.
            throw new BearerTokenAusenteException(
                $"No hay Bearer token disponible para la llamada a {request.RequestUri}. " +
                "Configure MaquinariaErpOptions.JwtToken como fallback o asegure HttpContext/Envelope.");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, ct);
    }
}
```

Pattern del listener (lee del envelope y setea ambient):

```csharp
public sealed class SincronizarDictamenVigenteListener
{
    private readonly IInspeccionReader _reader;
    private readonly IMaquinariaErpClient _erp;
    private readonly AmbientBearerTokenAccessor _ambient; // ← inyectado

    public async Task HandleAsync(InspeccionFirmada_v1 evento, Envelope envelope, CancellationToken ct)
    {
        var tenantId = envelope.TenantId
            ?? throw new TenantRequeridoEnEnvelopeException(...);

        // Setear el JWT del envelope en ambient antes de invocar al adapter.
        // El DelegatingHandler lo recoge cuando no hay HttpContext.
        var jwtEnvelope = envelope.Headers.TryGetValue("X-Forwarded-Authorization", out var v) ? v : null;
        using var _ = _ambient.SetForCurrentScope(jwtEnvelope);

        var aggregate = await _reader.LeerAsync(evento.InspeccionId, tenantId, ct);
        // ... (resto: invoca _erp.ActualizarDictamenEquipoAsync — el DelegatingHandler
        //     leerá el ambient y propagará el header Authorization).
    }
}
```

Captura del JWT en el endpoint HTTP (outbox publish):

```csharp
// Wolverine middleware o handler explícito que enriquece el envelope antes
// de outbox-publish. Captura el Authorization del HttpContext y lo persiste
// como header del envelope para que el listener lo recupere post-retry.
public static class CaptureBearerForOutboxMiddleware
{
    public static void Before(Envelope envelope, IHttpContextAccessor httpAccessor)
    {
        var http = httpAccessor.HttpContext;
        if (http is null) return; // No HTTP scope — listener-to-listener publish, skip.
        var auth = http.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth)) return;
        envelope.Headers["X-Forwarded-Authorization"] = auth;
    }
}
```

---

## 3. Evento(s) emitido(s)

**Cero eventos de dominio.** El slice no toca aggregates ni emite mensajes nuevos.

Tabla de mutaciones (ninguna):

| Mutación | Cuándo |
|---|---|
| — | — |

El header `X-Forwarded-Authorization` que se agrega al envelope es **metadata Wolverine, no payload de dominio** — vive en `wolverine_outgoing_envelopes.headers` (columna JSON ya existente).

---

## 4. Precondiciones

No aplican PRE de aggregate. Documentamos las del wiring:

- **`MT3-PRE-1`** — para llamadas HTTP-originadas: `HttpContext.Request.Headers.Authorization` debe contener `Bearer {jwt}`. Si está vacío en env Production y el caller no es un health-check, el middleware corporativo `MiddlewareAuthorizationToken` ya rechaza con 401 antes de llegar al endpoint (PRE-AUTH-1 de mt-1). En env Test, los tests E2E con `FakeSessionService` envían un Bearer placeholder (`Bearer test-jwt-empresa-{N}`) en el header para que la propagación funcione, o el `ChainedBearerTokenAccessor` cae al fallback service-account.

- **`MT3-PRE-2`** — para llamadas listener-originadas: el envelope del mensaje **debería** traer el header `X-Forwarded-Authorization`. Si no lo trae (publisher en otro context, mensaje legacy, etc.), `EnvelopeBearerTokenAccessor.ObtenerBearerToken()` retorna `null` y el `ChainedBearerTokenAccessor` cae al siguiente fallback. El listener **no lanza** `TenantRequeridoEnEnvelopeException` por ausencia de `X-Forwarded-Authorization` (eso es solo para `TenantId` de mt-2). El token de servicio (`MaquinariaErpOptions.JwtToken`) es el escape válido.

- **`MT3-PRE-3`** — `MaquinariaErpOptions.JwtToken` configurado como fallback. Si está vacío Y el HTTP context Y el envelope no traen token, `BearerTokenPropagationHandler` lanza `BearerTokenAusenteException` → request al ERP nunca sale → Wolverine la trata como error transitorio si emerge en listener (5s→30s→...) o como 500 si emerge en endpoint HTTP. **fail-closed** (MT3-INV-3).

---

## 5. Invariantes / decisiones de diseño

No aplican invariantes de aggregate. Se documentan las invariantes del wiring y las decisiones:

**MT3-INV-1 — Toda llamada HTTP del `MaquinariaErpClient` originada en un request HTTP propaga el Bearer del request, no el token de servicio.**
Validable por test WireMock que verifica el header `Authorization: Bearer {jwt-del-request}` recibido por el mock cuando `HttpContextBearerTokenAccessor` retorna un JWT específico. Cuando hay un caller HTTP, **no** se cae al fallback service-account.

**MT3-INV-2 — Llamadas del `MaquinariaErpClient` originadas en listeners Wolverine usan el JWT capturado en el envelope (`X-Forwarded-Authorization`), con fallback al token de servicio si está ausente.**
Validable por test que construye un envelope con `Headers["X-Forwarded-Authorization"] = "Bearer jwt-tenant-7"`, invoca el listener, y verifica el header recibido por WireMock.

**MT3-INV-3 — Si no hay token disponible (ni HTTP, ni envelope, ni service-account), la llamada falla `fail-closed` con `BearerTokenAusenteException` ANTES de salir al ERP.**
Defensa en profundidad: nunca request anónimo al ERP (incluso si Maquinaria_V4 lo rechazaría con 401). Cero información leak por error de configuración.

**MT3-INV-4 — El header `Authorization` en el HttpClient ya NO se setea estáticamente en `Program.cs`.**
La línea actual `http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.JwtToken)` se reemplaza por el `DelegatingHandler` registrado vía `AddHttpMessageHandler<BearerTokenPropagationHandler>()`. Si por error queda en producción, el handler sobrescribe el header en cada request (cada `SendAsync` recalcula `request.Headers.Authorization`), pero la regla de mantenimiento dice eliminarlo.

**D-MT3-1 — Puerto separado `IBearerTokenAccessor` (no extensión de `ISessionService`).**

Razón factual:

| Aspecto | Extender `ISessionService` | Puerto separado `IBearerTokenAccessor` |
|---|---|---|
| Semántica | Claims parseadas del JWT | Raw token de transporte HTTP |
| Variantes scope | Solo HTTP scope hoy | HTTP + envelope + service-account |
| SRP | Mezcla parsing con transporte | Cada uno cumple un rol |
| Impacto en tests existentes | Los tests del mt-1 que construyen `FakeSessionService` necesitarían un campo más | Cero cambios a tests mt-1 — el puerto es nuevo |

**Decisión:** puerto separado. `ISessionService` queda intacto.

**D-MT3-2 — Estrategia listeners: JWT en envelope con fallback a service-account.**

Comparativa:

| Estrategia | Pro | Con |
|---|---|---|
| (a) JWT en envelope (`X-Forwarded-Authorization`) | Audit fino: ERP ve el usuario originador | JWT puede expirar entre encolado y retry (ADR-006: 5s→30s→2m→10m = hasta ~12 min); PII en outbox |
| (b) Token de servicio M2M dedicado | No caduca; cero PII en outbox | Audit genérico: ERP no sabe qué usuario originó |
| **(a)+(b) chained** (decisión) | Audit fino normalmente; resiliente a expiración | Complejidad: 3 accessors compuestos |

**Decisión:** **chained (a)+(b)**. `ChainedBearerTokenAccessor` intenta en orden HTTP → envelope → service-account. Si el JWT del envelope expiró y el ERP responde 401, el `DelegatingHandler` **no reintenta automáticamente con service-account** — la política ADR-006 maneja el retry con el envelope. Si en el retry siguiente el envelope sigue trayendo el mismo JWT expirado (no se refresca), el dead-letter eventual es el comportamiento correcto. **El service-account es el último recurso si el envelope no trae token** (caso patológico: mensaje publicado antes de mt-3, listener-to-listener publish sin captura).

PII del JWT en el outbox: aceptado. Razones:
1. El outbox vive en la **misma DB Postgres del módulo** (`inspecciones` schema), red privada, mismo nivel de protección que el resto del dominio.
2. Ya hay datos sensibles en streams (GPS de técnicos, motivos de descarte). El JWT es una credencial efímera (TTL del host PWA, probablemente <1h) — no es un secreto rotated frequently.
3. ADR-006 ganará una nota sobre "JWT en outbox puede expirar antes del retry final — fallback service-account si así está configurado".

**D-MT3-3 — `MaquinariaErpOptions.JwtToken` queda como service-account fallback.**

NO se elimina. Cambia su rol y semántica:

- **Antes:** token global para todas las llamadas (deuda FU-14).
- **Después:** **fallback** cuando no hay HTTP context ni envelope con `X-Forwarded-Authorization`. Casos de uso legales:
  1. Bootstrap/seed manual (`SeedManualCatalogoHandler` que el dev corre con `dotnet run`).
  2. Listener-to-listener publish (no hay HttpContext en el primer publish).
  3. Mensaje legacy del outbox publicado antes de mt-3 (post-deploy gradual).

El comentario en `MaquinariaErpOptions.cs` se actualiza para reflejar el nuevo rol. FU-14 queda cerrado.

**D-MT3-4 — `tenantId` propagado al ERP via el Bearer (no header extra).**

Hipótesis verificable: Maquinaria_V4 lee `IdEmpresa` del JWT (paridad con Inspecciones, `SincoMiddlewareSessionService` del proyecto Attachment lo hace igual). Si el Bearer del caller viaja al ERP, el `IdEmpresa` viaja implícito — Maquinaria_V4 lo extrae y filtra su Marten por ese tenant.

**Asunción del slice:** Maquinaria_V4 NO requiere un header explícito `X-Tenant-Id` o similar. El JWT es suficiente. Si emerge evidencia contraria (404/403 del ERP en test con tenant correcto pero JWT del tenant equivocado), abrir followup y agregar header explícito al adapter. **Asunción documentada — no validable en mt-3 (Maquinaria_V4 no está corriendo localmente para test E2E).**

**D-MT3-5 — Refactor del `MaquinariaErpClient` para depender del DelegatingHandler.**

El cliente sigue recibiendo `HttpClient` como antes. El cambio es solo en `Program.cs` (DI):

```csharp
builder.Services.AddScoped<HttpContextBearerTokenAccessor>();
builder.Services.AddScoped<EnvelopeBearerTokenAccessor>();
builder.Services.AddSingleton<ServiceAccountBearerTokenAccessor>();
builder.Services.AddScoped<IBearerTokenAccessor, ChainedBearerTokenAccessor>();
builder.Services.AddScoped<BearerTokenPropagationHandler>();

builder.Services.AddHttpClient<IMaquinariaErpClient, MaquinariaErpClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<MaquinariaErpOptions>>().Value;
    if (string.IsNullOrWhiteSpace(opts.BaseUrl))
        throw new InvalidOperationException("Falta Maquinaria:BaseUrl.");
    http.BaseAddress = new Uri(opts.BaseUrl.EndsWith('/') ? opts.BaseUrl : opts.BaseUrl + "/");
    http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSegundos > 0 ? opts.TimeoutSegundos : 30);
    // ELIMINADO: http.DefaultRequestHeaders.Authorization fijo.
})
.AddHttpMessageHandler<BearerTokenPropagationHandler>();
```

**D-MT3-6 — Listeners ganan overload `HandleAsync(evento, Envelope, ct)` y setean `AmbientBearerTokenAccessor` antes de invocar al adapter.**

`SincronizarDictamenVigenteListener` **ya tiene** la overload con `Envelope` (introducida en mt-2 para `TenantId`). En mt-3 se extiende para leer también `envelope.Headers["X-Forwarded-Authorization"]` y setearlo en `AmbientBearerTokenAccessor` via `using var _ = _ambient.SetForCurrentScope(jwt)` — el ambient se limpia al disponer.

`DescartarNovedadPreopErpListener` **NO tiene** esa overload hoy (mt-2 no la añadió, FU-57). Se añade en mt-3 (cierre FU-57 + sub-objetivo MT3). Mismo pattern.

**`AmbientBearerTokenAccessor` usa `AsyncLocal<string?>`** — propagación por contexto async sin DI scope-magic. El listener lo setea explícitamente; el DelegatingHandler lo lee cuando no hay `HttpContext`. Es scoped singleton (sin estado mutable entre requests, AsyncLocal aísla por contexto). Patrón estándar .NET (mismo idea que `Activity.Current`, `CultureInfo.CurrentCulture`).

**Ventaja sobre DI scope-per-message:** funciona aunque Wolverine no exponga el envelope vía DI (solo necesitamos que lo pase por parámetro al método, ya confirmado por mt-2). Cero asunciones sobre internals Wolverine. **Asunción de mt-3:** Wolverine 3 propaga `AsyncLocal` correctamente entre el listener `HandleAsync` y el call al `IMaquinariaErpClient` (corre en el mismo async-flow). Trivialmente verificable en green con un test directo.

**D-MT3-7 — `DescartarNovedadPreopErpListener` cierre de FU-57 (logs estructurados con tenant).**

El listener gana overload `HandleAsync(NovedadPreopDescartada_v1, Envelope, ct)` y enriquece `LogCierreFallido` con `tenantId` del envelope. El campo se añade al `LoggerMessage` template + record `NovedadPreopErpCierreFallido_v1` (campo aditivo, sin bumpear `_v1`).

**D-MT3-8 — `ChainedBearerTokenAccessor` no cachea entre llamadas.**

Cada `ObtenerBearerToken()` consulta los accessors en orden. Cache podría introducir staleness (JWT refreshed en el mismo request HTTP — improbable pero posible). Cero estado mutable.

**D-MT3-9 — Tests usan WireMock para verificar el header `Authorization` recibido.**

Pattern existente (`SincronizarDictamenVigenteListenerTenantTests`). Verifican:
1. HTTP scope con `HttpContextBearerTokenAccessor` que retorna `Bearer jwt-X` → WireMock recibe `Authorization: Bearer jwt-X`.
2. Listener con envelope que trae `X-Forwarded-Authorization: Bearer jwt-Y` → WireMock recibe `Authorization: Bearer jwt-Y`.
3. Listener con envelope sin `X-Forwarded-Authorization` Y service-account configurado → WireMock recibe `Authorization: Bearer service-account-token`.
4. Todos los fallbacks vacíos → `BearerTokenAusenteException` (request nunca sale al WireMock).

**D-MT3-10 — Sin cambios al dominio, eventos `_v1`, o flujo de errores ADR-006.**

Cero cambios a `Inspecciones.Domain.*`. La política `OnException<MaquinariaErpException>` sigue intacta — el slice solo altera **cómo** se setea el header `Authorization`, no qué pasa si la llamada falla.

**D-MT3-11 — Sin nuevas dependencias NuGet.**

Reutilizamos `IHttpContextAccessor` (ya en DI por mt-1), `DelegatingHandler` (.NET standard), `IServiceProvider` (DI). Cero packages nuevos. Sin impacto en FU-53.

---

## 6. Escenarios Given / When / Then

Mt-3 no tiene aggregate. Los escenarios son **tests unitarios del adapter + listeners con WireMock**. Mínimo siete escenarios.

### 6.1 Happy path HTTP — `MaquinariaErpClient` con `HttpContextBearerTokenAccessor` propaga el JWT del request

**Given**
- `HttpContextBearerTokenAccessor` configurado con un `HttpContext` cuyo header `Authorization` es `Bearer jwt-empresa-7`.
- `ServiceAccountBearerTokenAccessor` configurado con token `Bearer service-account-token`.
- `ChainedBearerTokenAccessor` registrado.
- WireMock server con stub para `GET /api/equipos`.

**When**
- `MaquinariaErpClient.ListarEquiposAsync(filtro: null)` invocado.

**Then**
- WireMock recibe la request con header `Authorization: Bearer jwt-empresa-7`.
- WireMock NO recibe `Authorization: Bearer service-account-token`.
- `Body` deserializa correctamente.

### 6.2 Happy path listener — envelope con `X-Forwarded-Authorization` propaga al ERP

**Given**
- Listener `SincronizarDictamenVigenteListener` configurado con `EnvelopeBearerTokenAccessor` que lee del envelope actual.
- Envelope con `Headers["X-Forwarded-Authorization"] = "Bearer jwt-tenant-7"`, `TenantId = "7"`.
- `ServiceAccountBearerTokenAccessor` con `Bearer service-account-token`.
- WireMock con stub `PUT /api/equipos/{id}/dictamen-vigente`.

**When**
- Listener invocado con evento `InspeccionFirmada_v1` + envelope.

**Then**
- WireMock recibe `Authorization: Bearer jwt-tenant-7`.
- WireMock NO recibe `Authorization: Bearer service-account-token`.
- `IInspeccionReader` invocado con tenant `"7"` (preserva mt-2).

### 6.3 Fallback service-account — listener sin `X-Forwarded-Authorization` cae al token de servicio

**Given**
- Envelope con `TenantId = "7"` pero `Headers` sin `X-Forwarded-Authorization`.
- `ServiceAccountBearerTokenAccessor` con `Bearer service-account-token`.
- WireMock con stub.

**When**
- Listener invocado.

**Then**
- WireMock recibe `Authorization: Bearer service-account-token`.

### 6.4 Listener con envelope `X-Forwarded-Authorization` vacío cae al service-account

**Given**
- Envelope con `Headers["X-Forwarded-Authorization"] = ""` (string vacío).
- `ServiceAccountBearerTokenAccessor` con token configurado.

**When**
- Listener invocado.

**Then**
- WireMock recibe `Authorization: Bearer service-account-token` (el accessor de envelope retorna null/empty → cae al chain).

### 6.5 Fail-closed — todos los fallbacks vacíos lanzan `BearerTokenAusenteException`

**Given**
- `HttpContextBearerTokenAccessor` sin HttpContext.
- `EnvelopeBearerTokenAccessor` con envelope sin header.
- `ServiceAccountBearerTokenAccessor` con `JwtToken = ""`.
- WireMock con stub (que no debería ser invocado).

**When**
- `MaquinariaErpClient.ListarEquiposAsync` invocado.

**Then**
- Lanza `BearerTokenAusenteException`.
- WireMock NO recibe ninguna request (`LogEntries.Should().BeEmpty()`).

### 6.6 Chain order — HTTP gana sobre envelope si ambos están presentes

**Given**
- `HttpContextBearerTokenAccessor` con `Bearer jwt-http-call`.
- `EnvelopeBearerTokenAccessor` con `Bearer jwt-envelope-stale`.
- `ServiceAccountBearerTokenAccessor` con `Bearer service-account`.

**When**
- `ChainedBearerTokenAccessor.ObtenerBearerToken()` invocado.

**Then**
- Retorna `jwt-http-call` (orden: HTTP → envelope → service-account).

> Test unitario directo sobre `ChainedBearerTokenAccessor`, sin WireMock.

### 6.7 `DescartarNovedadPreopErpListener` overload tenant-aware enriquece logs (cierre FU-57)

**Given**
- Listener configurado con envelope `TenantId = "7"`.
- Envelope `Headers["X-Forwarded-Authorization"] = "Bearer jwt-7"`.
- WireMock stub que retorna `500 Internal Server Error`.
- `ILogger` capturado para inspección.

**When**
- Listener invocado con envelope. WireMock devuelve 500. Listener relanza `MaquinariaErpException`.

**Then**
- El log estructurado `NovedadPreopErpCierreFallido` incluye `TenantId = "7"`.
- WireMock recibe `Authorization: Bearer jwt-7`.

### 6.8 `MaquinariaErpClient` test legacy preserva — propaga `Authorization` directo si DelegatingHandler ausente

**Given**
- Test legacy de `MaquinariaErpClientTests` que construye `HttpClient.DefaultRequestHeaders.Authorization = "Bearer test-jwt"` directamente, **sin** DelegatingHandler.
- WireMock stub.

**When**
- `MaquinariaErpClient.ListarEquiposAsync` invocado.

**Then**
- WireMock recibe `Authorization: Bearer test-jwt`.

> Garantiza que los 14 tests existentes de `MaquinariaErpClientTests` siguen verde sin reescritura. El test los preserva.

### 6.9 Smoke regresión — `Infrastructure.Tests` 59/59 + nuevos pass; sin regresión en `Api.Tests` 73 / `Domain.Tests` 246

**Given**
- Suite actual `Infrastructure.Tests` (59 pass al cierre de mt-2), `Api.Tests` (73 pass + 7 skip), `Domain.Tests` (246 pass + 19 skip).

**When**
- Se corre `dotnet test` post-mt-3.

**Then**
- `Infrastructure.Tests`: 59 + nuevos del slice (mínimo +7 tests del §6).
- `Api.Tests`: sin regresión (73 pass, mismo skip count).
- `Domain.Tests`: sin regresión (246 pass).

### 6.10 Rebuild desde stream — N/A

El slice no emite eventos. §6.X omitido por convención (idéntico a mt-1, mt-2).

---

## 7. Idempotencia / retries

- **Propagación del Bearer es idempotente** — `IBearerTokenAccessor.ObtenerBearerToken()` no tiene side effects. N invocaciones devuelven el mismo valor (asumiendo HttpContext/envelope estable durante el scope).
- **Retries de Wolverine + JWT expirado:** la política ADR-006 (5s→30s→2m→10m) sigue. Si el JWT del envelope expiró entre el primer intento y el retry, el ERP devolverá `401`. Hoy el adapter mapea 401 a `MaquinariaErpException` con `StatusCode=Unauthorized` que cae en la rama 4xx → `MoveToErrorQueue()` permanente. **mt-3 no introduce refresh automático del JWT en retry.** Si emerge requirement de refresh, abrir followup. ADR-006 gana una nota sobre este caso.
- **Service-account fallback durante retry:** intencionalmente NO se aplica. Si la primera invocación usó el JWT del envelope y falló con 401, los retries siguen usando el mismo JWT del envelope (no se reescribe). La política dead-letter inmediato del 4xx asegura que no entre en loop.

---

## 8. Impacto en proyecciones / read models

- **Cero impacto.** mt-3 solo toca el adapter HTTP y los listeners — sin proyecciones, sin documents Marten.

---

## 9. Impacto en endpoints HTTP

- **Cero endpoints nuevos.** Cero endpoints removidos.
- **Cambios internos:** los handlers que invocan al `MaquinariaErpClient` (admin endpoints de `CatalogosEndpoints.cs` + cualquier sync handler) ahora propagan el Bearer del request. Transparente vía DelegatingHandler. **El código del handler no cambia**.

Códigos HTTP nuevos: ninguno. `BearerTokenAusenteException` se mapea a 500 (`InternalServerError`) via el handler global de excepciones — es un error de configuración, no del usuario.

OpenAPI: sin cambios.

---

## 10. Impacto en SignalR / push

- **Cero impacto.** mt-3 no toca el hub.

---

## 11. Impacto en adapters Sinco on-prem

- **`MaquinariaErpClient`** es el adapter afectado. Cambia el mecanismo de seteo del header `Authorization`:
  - Antes: estático en `Program.cs` con `MaquinariaErpOptions.JwtToken`.
  - Después: dinámico via `BearerTokenPropagationHandler` (DelegatingHandler) que consulta `IBearerTokenAccessor`.
- Estado disponibilidad: 🟡 mock-only (Maquinaria_V4 con auth real no testeable en CI hoy — TestE2E con WireMock cubre el contrato HTTP).
- **No se valida real-world cross-tenant del ERP en mt-3.** Esa es responsabilidad de mt-4 (smoke E2E + observabilidad) o de piloto.

---

## 12. Preguntas abiertas

Todas con autorización pre-otorgada por el usuario. Decisiones firmadas con defaults razonables.

### A. ¿`IBearerTokenAccessor` o extender `ISessionService`?

**Recomendación del modelador:** puerto separado (D-MT3-1). Razón: SRP — claims parseadas vs raw transport token.
**Decisión firmada 2026-05-19 — D-MT3-1 puerto separado.**

### B. ¿JWT en envelope o token de servicio para listeners?

**Recomendación del modelador:** chained (a)+(b) — envelope con fallback service-account (D-MT3-2). Razón: audit fino normalmente, resiliencia a JWT expirado, mantiene rol del `MaquinariaErpOptions.JwtToken`.
**Decisión firmada 2026-05-19 — D-MT3-2 chained.**

### C. ¿PII (JWT) en outbox Postgres aceptable?

**Recomendación del modelador:** aceptable. Razón: outbox vive en la misma DB del módulo, JWT es credencial efímera, ya hay datos sensibles en streams. Documentar en ADR-006.
**Decisión firmada 2026-05-19 — aceptable; ADR-006 gana nota.**

### D. ¿Maquinaria_V4 lee `IdEmpresa` del JWT o requiere header explícito?

**Recomendación del modelador:** asumir paridad con Inspecciones (lee del JWT). No agregar header explícito. Si emerge evidencia contraria en mt-4 / piloto, abrir followup.
**Decisión firmada 2026-05-19 — asumir JWT-only; followup defensivo en mt-3 review si emerge bloqueo.**

### E. ¿`AsyncLocal<string?>` propaga correctamente entre listener y DelegatingHandler?

**Recomendación del modelador:** asumir sí (patrón estándar .NET; mismo idea que `Activity.Current`). Reemplaza la necesidad de exponer `Envelope` por DI scope-per-message. Trivialmente verificable en green con un test directo.
**Decisión firmada 2026-05-19 — `AsyncLocal` pattern aceptado.**

### F. ¿Refresh del JWT en retry del outbox?

**Recomendación del modelador:** NO en mt-3. Política dead-letter inmediato del 4xx ya cubre 401 por JWT expirado. Si emerge requirement (operativo), abrir followup.
**Decisión firmada 2026-05-19 — NO refresh; followup defensivo si emerge.**

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (MT3-PRE-1..3) mapean a un escenario (§6.5, §6.3, §6.5).
- [x] Todas las invariantes (MT3-INV-1..4) están explícitas en §5 con escenarios asociados (§6.1, §6.2, §6.5, §6.8).
- [x] Happy path presente (§6.1, §6.2, §6.7).
- [x] Rebuild desde stream **no aplica** (§6.10 explícito).
- [x] Preguntas abiertas (§12.A..F) firmadas con defaults pre-autorizados.
- [x] Slice toca adapter Sinco on-prem; tests con WireMock (§11). FU-44 cierra explícitamente.
- [x] DoD de infra plumbing identificado:
  - [ ] Puerto `IBearerTokenAccessor` declarado en `Inspecciones.Infrastructure/Auth/` (pendiente — green).
  - [ ] `HttpContextBearerTokenAccessor`, `AmbientBearerTokenAccessor`, `ServiceAccountBearerTokenAccessor`, `ChainedBearerTokenAccessor` implementados (pendiente — green).
  - [ ] `BearerTokenPropagationHandler` (DelegatingHandler) implementado (pendiente — green).
  - [ ] `BearerTokenAusenteException` declarada (pendiente — green).
  - [ ] `Program.cs` registra DelegatingHandler en `AddHttpClient` y elimina `DefaultRequestHeaders.Authorization` fijo (pendiente — green).
  - [ ] `Program.cs` registra middleware Wolverine `CaptureBearerForOutboxMiddleware` (pendiente — green).
  - [ ] `DescartarNovedadPreopErpListener` gana overload tenant-aware y log enriquecido (pendiente — green; cierre FU-57).
  - [ ] `MaquinariaErpOptions.cs` actualiza docstring de `JwtToken` para reflejar rol service-account fallback (pendiente — green).
  - [ ] Tests `Infrastructure.Tests`: mínimo 7 escenarios §6.1..§6.7 (pendiente — red).
  - [ ] ADR-006 actualizado con nota sobre JWT en outbox + retry expirado (pendiente — doc-writer post-aprobación).
  - [ ] ADR-009 mt-3 marcado cerrado (pendiente — doc-writer post-merge).
  - [ ] `CLAUDE.md` "Multi-tenancy" actualizado con commit hash mt-3 (pendiente — doc-writer post-merge).
  - [ ] FU-44, FU-57 marcados ✅ cerrados; nuevos abiertos si emergen (pendiente — review/cierre).

---

## Nota final del modelador

mt-3 es la última pieza de la cadena de identidad cross-process. Después de mt-1 (claims del host PWA), mt-2 (tenancy Marten + outbox), mt-3 propaga el JWT al ERP — y la auditabilidad end-to-end por usuario+empresa queda cerrada.

Riesgos vivos:
1. **D-MT3-5/D-MT3-6 (Wolverine DI scope):** si Wolverine 3 no expone `Envelope` por DI scope-per-message, refactor a inyección manual al accessor (mínimo en green). Detectable inmediato al cablear.
2. **D-MT3-4 (Maquinaria_V4 lee `IdEmpresa` del JWT):** asunción no validable en mt-3 (sin Maquinaria_V4 real corriendo). Si emerge contra-evidencia en mt-4 / piloto, abrir followup.
3. **JWT en outbox expirado:** comportamiento es dead-letter inmediato. Para muchos casos esto es bueno (no se cubre con identidad equivocada). Si operativamente se requiere refresh, abrir followup.

mt-3 cierra el sub-track de auth cross-process. Después del commit, mt-4 puede arrancar sobre una base donde el JWT, el tenant y la identidad están enforced end-to-end.

Status: **firmado 2026-05-19** — autor firma: Usuario (Santiago Ramirez), autorización pre-otorgada en el ciclo mt-3. Próxima fase: `red` (tests rojos del `IBearerTokenAccessor` + DelegatingHandler + listener tenant-aware).
