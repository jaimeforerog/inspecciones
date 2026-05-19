# Slice mt-4 — E2E cross-tenant isolation + `CaptureBearerForOutboxMiddleware` + observabilidad por `IdEmpresa` (cierre sub-track multi-tenancy)

**Autor:** orquestador (rol domain-modeler — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-4)
**Fecha:** 2026-05-19
**Estado:** **Firmado: 2026-05-19** — autor firma: Usuario (Santiago Ramirez), autorización pre-otorgada en el ciclo mt-4.
**Agregado afectado:** ninguno (D3 vigente desde mt-1 — el aggregate `Inspeccion` y sus eventos `_v1` no se tocan). El slice opera sobre wiring Wolverine (captura del bearer al outbox), tests E2E cross-tenant que cierran auditorías abiertas, structured logging por `IdEmpresa`, y un test de rebuild defensivo en `Domain.Tests`.
**Decisiones previas relevantes:**
- `slices/mt-1-jwt-claims-pipeline/spec.md` §0 D1..D7 — claims canónicos del host, bypass `FakeSessionService` en env Test.
- `slices/mt-2-marten-conjoined-tenancy/spec.md` §D-MT2-1..D-MT2-10 — `Conjoined`, factory tenanted, listeners reciben tenant del envelope.
- `slices/mt-3-jwt-propagation-erp/spec.md` §D-MT3-1..D-MT3-10 — `IBearerTokenAccessor` chain, `AmbientBearerTokenAccessor`, listeners propagan JWT del envelope al adapter.
- `Inspecciones/docs/00-investigacion-mercado.md §9.17` — ADR-009 multi-tenancy Marten conjoined (cierra con mt-4).
- `Inspecciones/docs/00-investigacion-mercado.md §9.X` (ADR-006) — política outbox retry/dead-letter.
- `FOLLOWUPS.md #56` — validar Wolverine 3 prefiere overload tenant-aware → cierra acá vía test E2E.
- `FOLLOWUPS.md #59` — test rebuild cross-tenant defensivo del aggregate → cierra acá.
- `FOLLOWUPS.md #60` — `CaptureBearerForOutboxMiddleware` para enriquecer envelope con JWT entrante → cierra acá.
- `src/Inspecciones.Api/Program.cs` — pipeline ASP.NET + DI Wolverine + Marten ya cableado.
- `tests/Inspecciones.Api.Tests/Tenancy/MartenConjoinedTenancyTests.cs` — patrón cross-tenant E2E ya existente (mt-2).

---

## 1. Intención

Al cierre de mt-3, el módulo aísla data por tenant Marten (mt-2), valida claims del host (mt-1) y propaga el JWT del caller al ERP cuando hay HTTP scope (mt-3). Quedan **tres agujeros pendientes antes del piloto multi-empresa**:

1. **`CaptureBearerForOutboxMiddleware` ausente (FU-60).** Hoy un `POST /api/v1/inspecciones/{id}/firmar` con `Authorization: Bearer X` no propaga `X` al envelope del outbox. Cuando Wolverine despacha `InspeccionFirmada_v1` al listener `SincronizarDictamenVigenteListener`, el envelope no trae `X-Forwarded-Authorization`. El listener cae al service-account fallback. MT3-INV-1 se cumple para HTTP scope inline, pero la cadena audit fino → ERP (MT3-INV-2 end-to-end) solo está ejercitada por tests in-process con envelopes construidos a mano. Sin el middleware, el ERP audita toda escritura al service-account, no al usuario originador — pierde el valor principal del feature en producción.

2. **Tests E2E cross-tenant no auditan los caminos críticos (FU-56).** mt-2 cubre cross-tenant del aggregate, catálogos, `CatalogoSyncState` y un smoke del outbox. Falta: (a) **outbox cross-tenant** — verificar que un evento del tenant 7 no se procesa con tenant del tenant 8 después del despacho; (b) **proyecciones cross-tenant** — `InspeccionAbiertaPorEquipoView` debe filtrar por tenant; (c) **stress de paralelismo** — dos tenants ejecutando flujos en paralelo (`Task.WhenAll`) no leakean entre sí. Sin estos tests, el piloto multi-empresa puede descubrir leaks que la auditoría defensiva del slice debería detectar antes.

3. **Test rebuild cross-tenant defensivo (FU-59).** El `Apply(evento)` del aggregate `Inspeccion` debe ser puro (regla CLAUDE.md). Marten Conjoined garantiza que `AggregateStreamAsync(streamId)` con tenant N solo carga eventos con `tenant_id=N`, pero **falta un test explícito** que: dado un stream cualquiera, comparar `Inspeccion.Reconstruir(stream)` (rebuild manual, sin tocar Marten) vs `session.Events.AggregateStreamAsync` con el mismo tenant — deben ser idénticos. Defensa contra regresión: si alguien introduce lógica tenant-aware dentro de `Apply`, el test rompe.

4. **Observabilidad por `IdEmpresa` para baseline pre-piloto.** Los handlers y listeners actuales no emiten `IdEmpresa` como scope estructurado del logger. App Insights debería poder filtrar logs y métricas por tenant. Hoy: solo `DescartarNovedadPreopErpListener` enriquece logs con `TenantId` (cierre FU-57 en mt-3). Faltan: los 15 handlers HTTP, el listener de dictamen, el sync de catálogos. Sin esto, la sala de operaciones no puede troubleshoot incidentes multi-empresa.

Este slice **cierra el sub-track multi-tenancy** introduciendo el `CaptureBearerForOutboxMiddleware`, una batería E2E cross-tenant que blinda los caminos críticos (outbox, proyecciones, paralelismo), el test rebuild defensivo, structured logging por `IdEmpresa` en los handlers y listeners principales, y un documento `baseline-piloto.md` con el checklist operativo previo al primer deploy multi-empresa.

---

## 2. Comando

No hay aggregate ni comando de dominio. El "comando" lógico es **la captura del bearer al envelope outgoing**, equivalente al contrato del componente:

```csharp
namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// AsyncLocal estático que el ASP.NET middleware <see cref="CaptureBearerForOutboxMiddleware"/>
/// setea al inicio de cada request HTTP con el header <c>Authorization</c> entrante.
///
/// El <see cref="ForwardAuthEnvelopeRule"/> (un <c>IEnvelopeRule</c> registrado vía
/// Wolverine policy global) lo lee al hacer publish del outbox y lo escribe en
/// <c>envelope.Headers["X-Forwarded-Authorization"]</c>.
///
/// Por qué AsyncLocal estático y no DI scoped:
///   - El outbox publish ocurre dentro del mismo async-flow que el handler HTTP.
///   - Wolverine no pasa el HttpContext al envelope rule (es framework-agnostic).
///   - AsyncLocal aísla por contexto async (mismo idea que Activity.Current).
///
/// Patrón: idéntico a <see cref="AmbientBearerTokenAccessor"/> ya en uso por mt-3
/// pero con scope distinto: aquí el getter es estático, no se expone como puerto
/// (no se mockea — los tests inyectan via header HTTP real).
/// </summary>
public static class IncomingBearerCarrier
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? GetForwardedAuth() => Current.Value;
    public static IDisposable SetForCurrentScope(string? authHeader)
    {
        var anterior = Current.Value;
        Current.Value = authHeader;
        return new Restorer(anterior);
    }

    private sealed class Restorer : IDisposable { /* ... */ }
}
```

ASP.NET middleware:

```csharp
public sealed class CaptureBearerForOutboxMiddleware
{
    private readonly RequestDelegate _next;
    public CaptureBearerForOutboxMiddleware(RequestDelegate next) { _next = next; }

    public Task Invoke(HttpContext ctx)
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth) ||
            !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return _next(ctx);
        }
        using var _ = IncomingBearerCarrier.SetForCurrentScope(auth);
        return _next(ctx);
    }
}
```

Wolverine envelope rule (global, registrado vía policy):

```csharp
public sealed class ForwardAuthEnvelopeRule : IEnvelopeRule
{
    public void Modify(Envelope envelope)
    {
        // Si el handler HTTP corriente capturó un bearer, lo propagamos al envelope.
        // Si no (listener-to-listener publish, seed manual, cron), no tocamos el header.
        var auth = IncomingBearerCarrier.GetForwardedAuth();
        if (string.IsNullOrWhiteSpace(auth)) return;
        // No sobrescribimos si el publisher ya lo seteó explícitamente.
        envelope.Headers ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (!envelope.Headers.ContainsKey("X-Forwarded-Authorization"))
        {
            envelope.Headers["X-Forwarded-Authorization"] = auth;
        }
    }
}
```

Registro en `Program.cs` (Wolverine policy global + ASP.NET middleware):

```csharp
// Wolverine: agregar el rule a todos los senders (incluyendo el local outbox).
opts.Policies.AllSenders(cfg => cfg.AddOutgoingRule(new ForwardAuthEnvelopeRule()));
opts.Policies.AllLocalQueues(cfg => { /* sin AddOutgoingRule — IListenerConfiguration no lo expone */ });

// ASP.NET pipeline (antes de los endpoints, después del middleware corporativo de auth).
app.UseMiddleware<CaptureBearerForOutboxMiddleware>();
```

> **D-MT4-1 final:** la combinación "ASP.NET middleware → AsyncLocal → `IEnvelopeRule` global" es la opción más simple, testeable y desacoplada de Wolverine internals. Alternativas evaluadas y descartadas: (a) Wolverine `Policies.AddMiddleware<T>` por handler — requeriría inyectar `IHttpContextAccessor` a cada middleware, scope-per-message; (b) custom `IMessageBus` decorator — invasivo, complica el outbox transaccional. Ver §12.A.

---

## 3. Evento(s) emitido(s)

**Cero eventos de dominio.** El slice no toca aggregates ni emite mensajes nuevos.

Cambios en metadata Wolverine (no payload):

| Mutación | Cuándo |
|---|---|
| `envelope.Headers["X-Forwarded-Authorization"] = "Bearer {jwt}"` al outbox publish | Cada handler HTTP con `Authorization: Bearer X` entrante despachando un mensaje. |

El header vive en `wolverine_outgoing_envelopes.headers` (JSON ya existente). Cero cambios al schema.

---

## 4. Precondiciones

Documentamos las del wiring (no del aggregate):

- **`MT4-PRE-1`** — Para llamadas HTTP-originadas con outbox publish: el `CaptureBearerForOutboxMiddleware` debe estar registrado **antes** de los endpoints en el pipeline ASP.NET. Si no, el `ForwardAuthEnvelopeRule` no encuentra bearer en `IncomingBearerCarrier` y el envelope sale sin header — fallback al service-account (degradación a comportamiento pre-mt-4, sin regresión).

- **`MT4-PRE-2`** — Para llamadas listener-originadas: si el listener publica más mensajes (`bus.PublishAsync`), el rule no añade el header (el bearer del envelope entrante NO viaja automáticamente al envelope saliente — `AmbientBearerTokenAccessor` es para el adapter HTTP, `IncomingBearerCarrier` es para el ingreso HTTP). **Asunción documentada:** ningún listener actual publica mensajes adicionales (verificable). Si en el futuro se introduce uno, abrir followup para propagar el bearer del envelope entrante al saliente.

- **`MT4-PRE-3`** — Para tests cross-tenant E2E: Postgres disponible (Testcontainers o `POSTGRES_TEST_CONNSTRING`). Si no, los tests reciben `[Fact(Skip="...")]` documentado — mismo patrón que los 7 skips actuales de `Api.Tests`.

---

## 5. Invariantes / decisiones de diseño

No aplican invariantes de aggregate. Se documentan las del wiring + decisiones:

**MT4-INV-1 — `POST /api/v1/inspecciones` con tenant A no es visible en queries del tenant B.**
Validable por test E2E que crea inspección con tenant 7, abre sesión tenanted con tenant 8 y verifica que no la encuentra (aggregate + proyección `InspeccionAbiertaPorEquipoView`).

**MT4-INV-2 — `CaptureBearerForOutboxMiddleware` propaga el `Authorization` al envelope; el listener tenant-aware consume el bearer del envelope y lo aplica al adapter.**
Validable por test E2E que: (a) inyecta un `FakeMaquinariaErpClient` que captura el `Authorization` header recibido; (b) hace `POST /api/v1/inspecciones/{id}/firmar` con `Authorization: Bearer jwt-empresa-7`; (c) espera a que Wolverine despache `InspeccionFirmada_v1`; (d) verifica que el fake adapter recibió `Authorization: Bearer jwt-empresa-7` (no service-account).

**MT4-INV-3 — Logs estructurados de handlers HTTP críticos y listeners ERP incluyen `IdEmpresa`/`TenantId` como property.**
Validable por test que captura `ILogger` con `XunitLoggerProvider` o un `TestLoggerProvider` en memoria y verifica que las entradas correspondientes incluyen `IdEmpresa` en el state estructurado.

**MT4-INV-4 — Rebuild manual del aggregate desde stream con tenant A produce el mismo estado que `AggregateStreamAsync` con tenant A.**
Validable por test en `Domain.Tests` (puro, sin Marten) que dado los mismos eventos, compara `Inspeccion.Reconstruir(stream)` vs el output del `Apply` de Marten — deben ser idénticos. El test es defensivo: si alguien añade `if (tenantId == ...)` dentro de un `Apply`, el test rompe.

**D-MT4-1 — Wiring `CaptureBearerForOutboxMiddleware` por ASP.NET + `IEnvelopeRule` global.**

| Opción | Pro | Con | Decisión |
|---|---|---|---|
| (a) ASP.NET middleware → AsyncLocal estático → `IEnvelopeRule` global (esta) | Sin scope-per-message Wolverine; un solo punto de captura HTTP; trivialmente testeable | Acopla al ciclo de vida del request HTTP; no aplica a publish listener-to-listener (aceptable — no hay tales hoy) | **(a)** |
| (b) Wolverine `Policies.AddMiddleware<T>` con `IHttpContextAccessor` por handler | Más Wolverine-idiomático | Requiere registrar para los 15 handlers; scope DI per-message; más invasivo | rechazada |
| (c) `IMessageBus` decorator | Captura cualquier publish | Requiere wrapper sobre `IMessageBus`; complica outbox transaccional | rechazada |

**D-MT4-2 — Scope structured logging.**

Aplicar `ILogger.BeginScope(new Dictionary<string, object?> { ["IdEmpresa"] = session.IdEmpresa, ["IdUsuario"] = session.IdUsuario })` en los 15 endpoints HTTP via un **helper compartido** invocado al inicio de cada endpoint. Los listeners ya tienen patrón propio (`DescartarNovedadPreopErpListener` enriquece via `LoggerMessage` template con `TenantId={TenantId}` — cierre FU-57 en mt-3). `SincronizarDictamenVigenteListener` gana el mismo patrón en mt-4. Handlers de aplicación (`Inspecciones.Application.*Handler.cs`) NO se modifican — son puros, no emiten logs (regla del repo). Si emerge necesidad, los handlers ya operan dentro del scope del endpoint (heredan el scope).

**Helper:**

```csharp
internal static class SessionLoggingScope
{
    public static IDisposable? BeginEmpresaScope(this ILogger logger, ISessionService session)
    {
        try
        {
            return logger.BeginScope(new Dictionary<string, object?>
            {
                ["IdEmpresa"] = session.IdEmpresa,
                ["IdUsuario"] = session.IdUsuario,
            });
        }
        catch (ClaimRequeridaException)
        {
            // Si IdEmpresa no está disponible, el endpoint ya va a fallar con 401 —
            // no enriquecemos el scope. Logs preauth se quedan sin tenant (aceptable).
            return null;
        }
    }
}
```

Los endpoints lo usan como:

```csharp
group.MapPost("/", async (
    ISessionService session, ILogger<Program> logger, ... ) =>
{
    using var _ = logger.BeginEmpresaScope(session);
    // ... resto del endpoint sin cambios.
});
```

**D-MT4-3 — Estrategia de tests E2E con/sin Postgres.**

| Escenario | Postgres local | Sin Postgres |
|---|---|---|
| Tests E2E cross-tenant (outbox, proyecciones, paralelismo) | `Api.Tests` reales contra `WebApplicationFactory<Program>` + tenanted sessions | `[Fact(Skip="Postgres no disponible — set POSTGRES_TEST_CONNSTRING o levantar Docker")]` documentado |
| Test `CaptureBearerForOutboxMiddleware` unit | `Infrastructure.Tests` con `WireMock` (sin Postgres) | mismo — pasa en cualquier máquina |
| Test E2E del middleware end-to-end (POST → outbox → listener → ERP mock) | `Api.Tests` con WireMock + Postgres | `[Fact(Skip)]` documentado |
| Test rebuild cross-tenant defensivo (Domain.Tests) | puro, sin Postgres | mismo — pasa siempre |

En esta máquina (al momento del slice) Postgres no está corriendo y `POSTGRES_TEST_CONNSTRING` no está exportada. Los tests Postgres-dependientes irán a skip, **igual** que los 7 skips actuales. Cuando el orquestador o CI corra en env con Postgres, los skips se vuelven pass.

**D-MT4-4 — Métricas: `System.Diagnostics.Metrics.Meter` sin cablear OpenTelemetry completo.**

OpenTelemetry NO está cableado hoy. Cablearlo es un slice operativo separado (fuera de scope mt-4). En mt-4: **declaramos un `Meter("Inspecciones")` estático** con dos métricas:
- `inspecciones.command.duration` (Histogram\<double> en ms, tag `id_empresa`, tag `comando`).
- `inspecciones.erp.calls` (Counter\<long>, tag `id_empresa`, tag `endpoint`, tag `resultado` ∈ `{exito, fallo}`).

App Insights / Azure Monitor pueden scrapear `Meter` estándar sin OpenTelemetry. Si en piloto se decide migrar a OpenTelemetry, el contrato del `Meter` ya está documentado. **Por scope:** instrumentamos solo los listeners ERP (`SincronizarDictamenVigenteListener`, `DescartarNovedadPreopErpListener`) con `inspecciones.erp.calls`. El histogram `command.duration` queda como **followup** (FU nuevo) — instrumentar 15 endpoints amplía blast radius del slice sin valor crítico hoy. App Insights ya mide latencia HTTP por endpoint vía `Microsoft.ApplicationInsights.AspNetCore` cuando se cablee.

**D-MT4-5 — Activity / Distributed tracing.**

Trivial: en los handlers HTTP, dentro del scope ya creado, hacer `Activity.Current?.AddTag("id_empresa", session.IdEmpresa.ToString(CultureInfo.InvariantCulture))`. ASP.NET ya genera `Activity` por request. App Insights / Application Insights propagarán el tag. Se hace en el mismo helper `BeginEmpresaScope` para no duplicar lógica.

**D-MT4-6 — `baseline-piloto.md` con checklist operativo.**

Archivo nuevo en `slices/mt-4-e2e-isolation-observability/baseline-piloto.md`. Contenido:
- Estado de los 4 slices mt-* con commits.
- Invariantes verificados E2E: MT1-PRE-AUTH-*, MT2-INV-1..4, MT3-INV-1..4, MT4-INV-1..4.
- Followups operativos abiertos para el piloto: FU-58 backfill staging, FU-47 Application.Tests sin Docker, FU-53 NuGet feeds CI, FU-54 cross-team claim `capabilities`, FU-61 AsyncLocal storage estático (defensivo), FU-62 refresh JWT en retry.
- Checklist "qué validar antes del primer deploy multi-empresa": SQL backfill, smoke con 2 tenants reales, dashboard App Insights filtrable por `id_empresa`, etc.

**D-MT4-7 — Sin cambios al dominio ni a los eventos.**

D3 de mt-1/mt-2/mt-3 vigente. Cero cambios a `Inspecciones.Domain.*`. Cero cambios a eventos `*_v1`. El test rebuild defensivo opera sobre el aggregate ya existente sin modificarlo.

**D-MT4-8 — Sin nuevas dependencias NuGet.**

`Meter` está en BCL (`System.Diagnostics.Metrics`). `Activity.Current` está en BCL. `ILogger.BeginScope` está en `Microsoft.Extensions.Logging.Abstractions` (ya transitiva). `IEnvelopeRule` está en Wolverine (ya referenciado).

**D-MT4-9 — Logging no leakea PII del JWT.**

El scope `IdEmpresa`/`IdUsuario` son enteros, no PII. **Nunca** logueamos el JWT completo. El bearer en el envelope vive en `wolverine_outgoing_envelopes.headers` (Postgres del módulo, red privada) — aceptado por D-MT3-2 (mt-3 §5). Si emerge requirement de sanitizar headers en logs, abrir followup.

**D-MT4-10 — Tests E2E determinísticos.**

El test de paralelismo (`Task.WhenAll` con dos tenants) usa `Guid.NewGuid()` para `InspeccionId` y `Guid.NewGuid()` para `X-Client-Command-Id` por tarea — sin colisión posible entre tareas. Tenants 7 y 8, EquipoIds distintos por tenant (`7901`/`8901`), rangos no usados por slices previos (mt-2 usa 7001..7004). Sin races sobre el outbox: Wolverine + Marten transaccional garantiza orden causal por stream.

---

## 6. Escenarios Given / When / Then

Mt-4 no tiene aggregate. Los escenarios son tests E2E (`Api.Tests`), tests de unidad (`Infrastructure.Tests`) y un test de dominio (`Domain.Tests`). Mínimo nueve escenarios.

### 6.1 `CaptureBearerForOutboxMiddleware` unit — pasa el `Authorization` al `IncomingBearerCarrier`

**Given**
- `CaptureBearerForOutboxMiddleware` configurado con un `next` que captura el valor de `IncomingBearerCarrier.GetForwardedAuth()` al ejecutar.
- `HttpContext` con header `Authorization: Bearer jwt-empresa-7`.

**When**
- `middleware.Invoke(ctx)` ejecutado.

**Then**
- Durante `next`, `IncomingBearerCarrier.GetForwardedAuth()` retorna `"Bearer jwt-empresa-7"`.
- Después de `Invoke`, retorna `null` (scope cerrado por dispose).

> Test unit en `Infrastructure.Tests/Auth/CaptureBearerForOutboxMiddlewareTests.cs`. Sin Postgres.

### 6.2 `CaptureBearerForOutboxMiddleware` sin header → no captura

**Given**
- `HttpContext` sin header `Authorization`.

**When**
- `middleware.Invoke(ctx)` ejecutado.

**Then**
- Durante `next`, `IncomingBearerCarrier.GetForwardedAuth()` retorna `null`.

### 6.3 `CaptureBearerForOutboxMiddleware` con `Authorization: Basic ...` → no captura

**Given**
- `HttpContext` con `Authorization: Basic dXNlcjpwYXNz`.

**When**
- `middleware.Invoke(ctx)` ejecutado.

**Then**
- `IncomingBearerCarrier.GetForwardedAuth()` retorna `null` durante `next`. El middleware solo intercepta esquema Bearer.

### 6.4 `ForwardAuthEnvelopeRule` unit — propaga al envelope cuando hay bearer en el carrier

**Given**
- `IncomingBearerCarrier` con `"Bearer jwt-empresa-7"` seteado en el scope actual.
- `Envelope` vacío.

**When**
- `rule.Modify(envelope)` ejecutado.

**Then**
- `envelope.Headers["X-Forwarded-Authorization"]` == `"Bearer jwt-empresa-7"`.

### 6.5 `ForwardAuthEnvelopeRule` unit — no sobrescribe si publisher ya seteó el header

**Given**
- `IncomingBearerCarrier` con `"Bearer jwt-A"` seteado.
- `Envelope.Headers["X-Forwarded-Authorization"] = "Bearer jwt-B"` (publisher explícito).

**When**
- `rule.Modify(envelope)` ejecutado.

**Then**
- `envelope.Headers["X-Forwarded-Authorization"]` permanece `"Bearer jwt-B"` (D-MT4-1: el rule no clobberea).

### 6.6 `ForwardAuthEnvelopeRule` unit — sin carrier no añade header

**Given**
- `IncomingBearerCarrier` vacío (no scope HTTP).
- `Envelope` sin headers.

**When**
- `rule.Modify(envelope)` ejecutado.

**Then**
- `envelope.Headers` no contiene `X-Forwarded-Authorization`. Listener cae al service-account fallback (comportamiento pre-mt-4 aceptable para sagas cron / seed manual).

### 6.7 E2E cross-tenant aggregate — `POST /inspecciones` con tenant 7 no visible para tenant 8

**Given**
- Postgres disponible (de lo contrario `Skip`).
- Catálogo sembrado para tenants 7 y 8 (`equipoId=7901`/`8901`, distintas rutinas).

**When**
- `clientTenant7` (con `FakeSessionService(idEmpresa: 7)`) hace `POST /api/v1/inspecciones` → 201, retorna `inspeccionIdT7`.
- `clientTenant8` (con `FakeSessionService(idEmpresa: 8)`) hace `POST /api/v1/inspecciones` → 201, retorna `inspeccionIdT8`.

**Then**
- Sesión tenanted con `"7"` puede leer `inspeccionIdT7` via `AggregateStreamAsync<Inspeccion>` pero NO `inspeccionIdT8`.
- Sesión tenanted con `"8"` puede leer `inspeccionIdT8` pero NO `inspeccionIdT7`.
- `InspeccionAbiertaPorEquipoView` filtrada por tenant 7 solo retorna `equipoId=7901`; filtrada por tenant 8 solo retorna `equipoId=8901`.

> Test E2E en `Api.Tests/Tenancy/CrossTenantE2EIsolationTests.cs`.

### 6.8 E2E cross-tenant catálogos — `POST /api/v1/catalogos/sync` con tenant 7 no aparece en query del tenant 8

**Given**
- Postgres disponible.
- WireMock con stubs `GET /api/causas-falla` etc. para ambos tenants.

**When**
- Tenant 7 sincroniza 2 causas-falla → `200`, ETag `"v1-t7"`.
- Tenant 8 sincroniza 3 causas-falla (distintas) → `200`, ETag `"v1-t8"`.

**Then**
- Sesión tenanted "7" cuenta 2 `CausaFallaCatalogo`; sesión "8" cuenta 3.
- `CatalogoSyncState` por tenant tiene su propio ETag.

> Test E2E en `Api.Tests/Tenancy/CrossTenantE2EIsolationTests.cs`. Refactor del existente §6.3 de mt-2 con `WebApplicationFactory<Program>` real + WireMock para que ejercite el endpoint completo (no solo `IDocumentSession` directa).

### 6.9 E2E paralelismo cross-tenant — 2 tenants ejecutando `Task.WhenAll` no leakean

**Given**
- Postgres disponible.
- Catálogos sembrados para tenant 7 (`equipoId=7902`) y tenant 8 (`equipoId=8902`).
- 20 tareas: 10 ejecutan `POST /inspecciones` con tenant 7, 10 con tenant 8 (entrelazadas).

**When**
- `await Task.WhenAll(tareas)`.

**Then**
- Todas las 20 retornan `201 Created`.
- Sesión tenanted "7" lista exactamente 10 inspecciones (todas con `equipoId=7902`).
- Sesión tenanted "8" lista exactamente 10 (`equipoId=8902`).
- Ningún `InspeccionId` aparece en el tenant equivocado.

> Test E2E en `Api.Tests/Tenancy/CrossTenantE2EIsolationTests.cs`. Stress básico — no pretende ser load test, solo verifica aislamiento bajo paralelismo.

### 6.10 E2E `CaptureBearerForOutboxMiddleware` end-to-end (FU-60 cierre)

**Given**
- Postgres disponible + WireMock.
- `FakeMaquinariaErpClient` registrado vía `WithServices` que captura los headers `Authorization` de cada call.
- Catálogo sembrado tenant 7. Stream firmable en tenant 7 (`PRE-1g` cumplido).

**When**
- `POST /api/v1/inspecciones/{id}/firmar` con `Authorization: Bearer jwt-usuario-empresa-7`.
- Se espera el procesamiento del outbox (`waitForOutboxFlush` o similar — `IHostedService` de Wolverine).

**Then**
- `FakeMaquinariaErpClient.UltimoAuthHeader` == `"Bearer jwt-usuario-empresa-7"` (no `"Bearer service-account-token"` del fallback).
- El envelope persistido en `wolverine_outgoing_envelopes` tiene `headers->>'X-Forwarded-Authorization' = 'Bearer jwt-usuario-empresa-7'` (verificable opcional).

> Test E2E en `Api.Tests/Tenancy/CaptureBearerForOutboxEndToEndTests.cs`. **Si Postgres no disponible: Skip.**

### 6.11 Structured logging por `IdEmpresa` — endpoint emite scope con tenant

**Given**
- `XunitLoggerProvider` capturando entradas con scopes.
- `FakeSessionService(idEmpresa: 7)`.

**When**
- `POST /api/v1/inspecciones` ejecutado.

**Then**
- Alguna entrada del log del endpoint tiene scope con `IdEmpresa=7` (assert con un `LogEntry.Scopes` que incluya el par `("IdEmpresa", 7)`).

> Test E2E en `Api.Tests/Tenancy/StructuredLoggingTests.cs`. Requiere Postgres.

### 6.12 Rebuild cross-tenant defensivo (FU-59 cierre)

**Given**
- Stream con 5 eventos (`InspeccionIniciada_v1`, 2× `HallazgoRegistrado_v1`, `DiagnosticoEmitido_v1`, `DictamenEstablecido_v1`, `InspeccionFirmada_v1`).
- Tenant arbitrario (`"7"`).

**When**
- Reconstruir el aggregate **dos veces**: (a) usando el mecanismo Marten `AggregateStreamAsync<Inspeccion>` con tenant 7 (requiere Postgres → `Skip` parcial), o (b) **rebuild puro**: instanciar `Inspeccion` vacío y aplicar cada evento manualmente.

**Then**
- El estado del aggregate (campos públicos: `InspeccionId`, `EquipoId`, `Estado`, `Hallazgos`, `Dictamen`, `FirmadoEn`) es idéntico entre los dos rebuilds.

> Test en `Domain.Tests/Inspecciones/RebuildCrossTenantDefensivoTests.cs`. Variante puramente de dominio (sin Postgres) que verifica MT4-INV-4 sobre `Apply`. La variante con Marten queda en `Api.Tests` con Skip si Postgres no disponible.

### 6.13 Smoke regresión — `Infrastructure.Tests` 93/93, `Api.Tests` 73/80+7skip + nuevos, `Domain.Tests` 246+1 nuevo

**Given**
- Estado al cierre de mt-3.

**When**
- `dotnet build` + `dotnet test` por proyecto.

**Then**
- `Infrastructure.Tests`: 93 + nuevos del slice (mínimo +6 de §6.1..§6.6). Verde.
- `Api.Tests`: sin regresión de los 73 existentes; los nuevos del slice pasan si Postgres está, skip si no. **No degradar los 7 skips existentes.**
- `Domain.Tests`: 246 + 1 nuevo (§6.12 puro). Verde.

### 6.14 Rebuild desde stream — N/A

El slice no emite eventos nuevos. §6.X omitido por convención (idéntico a mt-1/mt-2/mt-3). El test §6.12 cubre rebuild defensivo del aggregate existente.

---

## 7. Idempotencia / retries

- **`CaptureBearerForOutboxMiddleware` es idempotente** — N invocaciones con el mismo `HttpContext` setean el mismo bearer en el `AsyncLocal`. Sin side effects fuera del scope.
- **`ForwardAuthEnvelopeRule` es idempotente** — N modificaciones del mismo envelope no cambian el resultado (no sobrescribe si ya hay header). Modificaciones de envelopes distintos son aislados por instancia.
- **Tests E2E paralelismo (§6.9):** `Task.WhenAll` con `Guid.NewGuid()` por tarea → sin race condition sobre IDs. El outbox de Wolverine procesa cada envelope de forma transaccional por stream.
- **Retries del outbox preservan el header `X-Forwarded-Authorization`** — política ADR-006 (5s→30s→2m→10m). El envelope persiste el header en `wolverine_outgoing_envelopes.headers`; cada retry lo lee. Si el JWT expiró → 401 del ERP → dead-letter (comportamiento documentado en mt-3 §7).

---

## 8. Impacto en proyecciones / read models

- **`InspeccionAbiertaPorEquipoView`** — proyección inline tenant-aware desde mt-2 (Marten Conjoined la particiona automáticamente). mt-4 **verifica** que el filtro funciona via §6.7. Cero cambios al código de la proyección.
- **`CatalogoSyncState`** — idem mt-2. mt-4 verifica via §6.8.

---

## 9. Impacto en endpoints HTTP

**Cero endpoints nuevos.** Cero endpoints removidos. Cero cambios al request/response shape.

**Cambios internos:**
- Cada endpoint adopta el `using var _ = logger.BeginEmpresaScope(session);` al inicio (15 endpoints en total — `InspeccionesEndpoints.cs` + `CatalogosEndpoints.cs`).
- Pipeline ASP.NET gana `app.UseMiddleware<CaptureBearerForOutboxMiddleware>()` justo después del middleware corporativo de auth (env Development/Production) o al inicio del pipeline (env Test).

OpenAPI: sin cambios.

---

## 10. Impacto en SignalR / push

- **Cero impacto.** mt-4 no toca el hub.
- **Diferido a piloto:** la audiencia del hub debería filtrarse por tenant (`Group = $"{idEmpresa}-{tecnicoId}"`). Followup latente si emerge — actualmente sin uso real de SignalR.

---

## 11. Impacto en adapters Sinco on-prem

- **`MaquinariaErpClient` sin cambios** al adapter — la propagación via `BearerTokenPropagationHandler` ya cubre el caso (mt-3). Lo que cambia es que el `AmbientBearerTokenAccessor` (cuando lo setea el listener) y el `HttpContextBearerTokenAccessor` (cuando hay HTTP scope inline) ahora reciben el bearer correcto end-to-end gracias al middleware.
- Estado disponibilidad: 🟡 mock-only (Maquinaria_V4 con auth real no testeable en CI). Test E2E con WireMock cubre el contrato.

---

## 12. Preguntas abiertas

Todas con autorización pre-otorgada por el usuario.

### A. ¿`IEnvelopeRule` global vía `AllSenders` cubre el outbox local?

El outbox transaccional de Wolverine + Marten usa "sender" para enrutar al local. `AllSenders` debería aplicarse al sender local también. **Decisión firmada 2026-05-19 — asumir sí; verificar en green con el test §6.10 (end-to-end real).** Si emerge que el rule no se aplica, alternativa: invocar `envelope.Headers[...] = ...` explícitamente desde un `OutgoingMessageInterceptor` registrado en cada handler. Reportable como sorpresa si emerge.

### B. ¿`IncomingBearerCarrier` static vs scoped?

mt-3 ya tiene precedente con `AmbientBearerTokenAccessor` estático (FU-61 abierto defensivamente). mt-4 sigue el mismo patrón por simetría — un AsyncLocal estático aísla por contexto async. **Decisión firmada — static.** Si FU-61 escala a slice real, mt-4 hereda la refactorización (la API del puerto no cambia).

### C. ¿Cablear OpenTelemetry completo?

NO en mt-4. **Decisión firmada — solo `Meter` BCL + `Activity.AddTag`.** OpenTelemetry full requiere setup separado (exporter, sampling, AppInsights connection string). Followup nuevo si emerge.

### D. ¿Instrumentar los 15 handlers con `inspecciones.command.duration`?

NO en mt-4. **Decisión firmada — solo los 2 listeners ERP con `inspecciones.erp.calls`.** App Insights ya mide latencia HTTP por endpoint via `ApplicationInsights.AspNetCore` (cuando se cablee). El histogram custom es valor marginal hoy. Followup nuevo para instrumentar comandos cuando emerja necesidad.

### E. ¿El test §6.10 puede correr sin Postgres?

NO. Requiere Wolverine outbox real + Marten. Si Postgres no disponible → Skip. **Decisión firmada.**

### F. ¿Cambiar el patrón de logging de los listeners ERP?

`DescartarNovedadPreopErpListener` ya tiene `LoggerMessage` con `TenantId` (cierre FU-57). `SincronizarDictamenVigenteListener` gana el mismo patrón en mt-4. **Decisión firmada — aplicar simetría.**

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (MT4-PRE-1..3) mapean a un escenario (§6.1, §6.4, §6.10).
- [x] Todas las invariantes (MT4-INV-1..4) están explícitas en §5 con escenarios asociados (§6.7, §6.10, §6.11, §6.12).
- [x] Happy path presente (§6.1, §6.7, §6.10).
- [x] Rebuild desde stream cubierto (§6.12 — defensivo, cierra FU-59).
- [x] Preguntas abiertas (§12.A..F) firmadas con defaults pre-autorizados.
- [x] Slice toca adapter Sinco on-prem indirectamente; tests con WireMock + FakeMaquinariaErpClient (§11).
- [x] DoD de infra plumbing identificado:
  - [ ] `IncomingBearerCarrier` static class declarada en `Inspecciones.Infrastructure/Auth/` (pendiente — green).
  - [ ] `CaptureBearerForOutboxMiddleware` declarado en `Inspecciones.Infrastructure/Auth/` (pendiente — green).
  - [ ] `ForwardAuthEnvelopeRule` declarado en `Inspecciones.Infrastructure/Auth/` (pendiente — green).
  - [ ] `Program.cs` registra `app.UseMiddleware<CaptureBearerForOutboxMiddleware>()` + `opts.Policies.AllSenders(cfg => cfg.AddOutgoingRule(...))` (pendiente — green).
  - [ ] `SessionLoggingScope.BeginEmpresaScope` extension declarada (pendiente — green).
  - [ ] 15 endpoints adoptan `using var _ = logger.BeginEmpresaScope(session);` (pendiente — green).
  - [ ] `SincronizarDictamenVigenteListener` enriquece `LogSyncFallida` con `TenantId` (simetría con `DescartarNovedadPreopErpListener`) (pendiente — green).
  - [ ] `Meter("Inspecciones")` declarado + `inspecciones.erp.calls` counter en ambos listeners (pendiente — green).
  - [ ] Tests `Infrastructure.Tests`: §6.1..§6.6 (mínimo 6 nuevos, sin Postgres) (pendiente — red).
  - [ ] Tests `Api.Tests`: §6.7..§6.11 (con `[Fact(Skip)]` documentado si Postgres no disponible) (pendiente — red).
  - [ ] Tests `Domain.Tests`: §6.12 puro (1 nuevo, sin Postgres) (pendiente — red).
  - [ ] `baseline-piloto.md` redactado (pendiente — doc-writer post-aprobación).
  - [ ] ADR-009 actualizado en `00-investigacion-mercado.md §9.17` (sub-track cerrado, listo para piloto) (pendiente — doc-writer post-aprobación).
  - [ ] `CLAUDE.md` "Multi-tenancy" marca mt-4 cerrado + sub-track cerrado (pendiente — doc-writer post-merge).
  - [ ] FU-56, FU-59, FU-60 cerrados; nuevos abiertos si emergen (FU-instrumentar comandos, FU-OpenTelemetry full, etc.) (pendiente — review/cierre).

---

## Nota final del modelador

mt-4 es el slice de cierre del sub-track. Plumbing-heavy y defensivo: cero cambios al dominio, cero a eventos, cero a endpoints públicos. El valor es **blindar el sub-track antes del piloto multi-empresa** — un `CaptureBearerForOutboxMiddleware` que cierra la cadena end-to-end, una batería E2E que verifica los aislamientos críticos bajo paralelismo, un test rebuild defensivo, structured logging que permite troubleshooting operacional.

Riesgos vivos:
1. **D-MT4-1 (`AllSenders` cubre outbox local):** asunción documentada. Si en green emerge que el rule no se aplica al outbox local, alternativa: registrar el rule en `AllLocalQueues` también, o usar `OutgoingMessageInterceptor` per-handler. Reportable.
2. **Tests E2E sin Postgres → Skip:** mantiene la regla "tests skip son explícitos, no failures silenciosos". Postgres-arranque queda para CI o env del PO con `POSTGRES_TEST_CONNSTRING`.
3. **OpenTelemetry diferido:** el sub-track cierra sin observabilidad full. App Insights con `Meter` BCL + structured logs + `Activity.Current.AddTag` es suficiente para piloto. OpenTelemetry full = slice operativo separado.

Después del commit, el sub-track multi-tenancy se declara **cerrado** y listo para piloto (sujeto al checklist de `baseline-piloto.md`).

Status: **firmado 2026-05-19** — autor firma: Usuario (Santiago Ramirez), autorización pre-otorgada en el ciclo mt-4. Próxima fase: `red` (tests rojos del middleware + rule + E2E cross-tenant + rebuild defensivo).
