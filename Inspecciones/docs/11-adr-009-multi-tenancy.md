# ADR-009 — Multi-tenancy por `IdEmpresa` (Marten conjoined + propagación JWT)

**Estado:** Aceptada — 2026-05-19 (sub-track mt-1..mt-4 cerrado).
**Slices implementadores:** `mt-1-jwt-claims-pipeline`, `mt-2-marten-conjoined-tenancy`, `mt-3-jwt-propagation-erp`, `mt-4-e2e-isolation-observability`.
**Supersede:** nada. Complementa ADR-002 (identidad heredada del host PWA — pasa de "tentativa" a "aceptada" con mt-1).

---

## 1. Contexto

El módulo Inspecciones se opera por varias empresas dentro de la PWA Sinco MYE. Hasta antes del 2026-05-19 cada despliegue suponía un schema por empresa. Para soportar **una única instancia Azure sirviendo a múltiples empresas (piloto multi-empresa)** se necesitó:

1. Pipeline de identidad real (no headers de test) que exponga `IdEmpresa` desde el JWT del host.
2. Aislamiento estricto de streams, proyecciones y catálogos por `IdEmpresa` — sin que el dominio conozca el concepto de tenant.
3. Propagación del JWT entrante a las llamadas salientes al ERP `Maquinaria_V4` (para que el ERP haga su propio check de empresa).
4. Validación end-to-end de aislamiento bajo paralelismo y observabilidad correlacionada por empresa.

---

## 2. Decisión

### 2.1 Pipeline de identidad (mt-1)

Puerto `ISessionService` en `src/Inspecciones.Infrastructure/Auth/ISessionService.cs` expone los **5 claims canónicos** del JWT del host:

| Claim | Tipo | Notas |
|---|---|---|
| `IdEmpresa` | `int` | Tenant key. Paridad 1:1 con ERP. |
| `IdUsuario` | `int` | Equivalente al `tecnicoId` opaco usado en eventos. |
| `NomUsuario` | `string` | Para logs estructurados. |
| `IdSucursal` | `int` | Puede ser `0` si N/A. |
| `IdProyecto` | `int` | Puede ser `0` si N/A. |
| `Capabilities` | `IReadOnlyCollection<string>` | Ej: `["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]`. |

Implementación de producción: `SincoMiddlewareSessionService` lee `MiddlewareAuthorizationToken.SessionVariables()` del paquete corporativo `SincoSoft.MYE.Common 1.5.1`. Tests: `FakeSessionService` (bypass) y `TestHeaderAwareSessionService` (compat legacy via headers `X-Sin-*`).

Endpoints HTTP leen identidad **exclusivamente** desde `ISessionService`. Prohibido `HttpContext.User` (MT1-INV-1). Una claim ausente produce `ClaimRequeridaException` → 401 con `codigoError = "CLAIM-{NOMBRE}-AUSENTE"`.

### 2.2 Aislamiento Marten (mt-2)

Configuración global en `Program.cs`:

```csharp
opts.Policies.AllDocumentsAreMultiTenanted();
opts.Events.TenancyStyle = TenancyStyle.Conjoined;
```

Discriminador: `tenant_id = session.IdEmpresa.ToString(CultureInfo.InvariantCulture)` (string, paridad con Marten).

Factory `ITenantedDocumentSessionFactory` en `src/Inspecciones.Infrastructure/Auth/TenantedDocumentSessionFactory.cs` es el único punto de apertura de sesión en producción (MT2-INV-1). Abre sesión con tenant resuelto desde:

- Endpoint HTTP: `ISessionService.IdEmpresa`.
- Listener Wolverine: `envelope.TenantId`.

Si la apertura no recibe tenant, lanza `TenantRequeridoEnEnvelopeException` — fail-closed por construcción.

**Todos los catálogos son por-empresa** (D-MT2-3). No hay excepciones globales. Streams del aggregate `Inspeccion` y de proyecciones quedan particionados automáticamente por el discriminador.

### 2.3 Propagación JWT al ERP (mt-3)

Puerto `IBearerTokenAccessor` separado de `ISessionService` (D-MT3-1, SRP). Cadena ordenada de impls:

1. `HttpContextBearerTokenAccessor` — lee `HttpContext.Request.Headers.Authorization`.
2. `AmbientBearerTokenAccessor` — lee `AsyncLocal` seteado por listener desde envelope.
3. `ServiceAccountBearerTokenAccessor` — fallback `MaquinariaErpOptions.JwtToken` (rol degradado de "default" a "fallback", D-MT3-3).
4. `ChainedBearerTokenAccessor` — orquesta la cadena, retorna el primero no-vacío.

`BearerTokenPropagationHandler` (DelegatingHandler) en el HTTP client de `MaquinariaErpClient` consulta el accessor en cada `SendAsync()` y reescribe `Authorization`. Si no hay token, lanza `BearerTokenAusenteException` antes de salir al ERP (MT3-INV-3, fail-closed).

Listeners Wolverine ganan overload `HandleAsync(evento, Envelope, ct)` para leer JWT del envelope (capturado por `EnvelopeBearerExtractor`) y aplicarlo al accessor ambient antes de invocar al adapter.

### 2.4 Captura del JWT entrante para outbox (mt-4)

Cuando un endpoint HTTP encola un mensaje en el outbox, el JWT entrante se persiste en el envelope para que el listener pueda propagarlo después:

- `CaptureBearerForOutboxMiddleware` (ASP.NET) extrae `Authorization` del request y lo guarda en `IncomingBearerCarrier` (`AsyncLocal<string?>`).
- `ForwardAuthEnvelopeRule` (Wolverine `IEnvelopeRule` global) lee del carrier y escribe `envelope.Headers["X-Forwarded-Authorization"]` en cada mensaje saliente.
- El envelope persistido en `wolverine_outgoing_envelopes` lleva el JWT junto con el `tenant_id`.

**Observabilidad por empresa** (MT4-INV-3):

- `SessionLoggingScopeFilter` (IEndpointFilter global) abre un scope estructurado con `IdEmpresa` + `IdUsuario` para todos los logs del request.
- `Activity.Current?.AddTag("id_empresa", ...)` enriquece el distributed tracing.
- Counter `inspecciones.erp.calls` (en `InspeccionesMeters`) con tags `id_empresa`, `endpoint`, `resultado`.

### 2.5 Verificación end-to-end (mt-4)

`tests/Inspecciones.Api.Tests/Tenancy/CrossTenantE2EIsolationTests.cs` ejecuta 20 tareas concurrentes (10 tenant 7, 10 tenant 8) verificando:

- POST con tenant A invisible en GET con tenant B (aggregate + proyección).
- Outbox respeta tenant del envelope al despachar al listener.
- Rebuild del aggregate es determinista entre tenants (`RebuildCrossTenantDefensivoTests`, MT4-INV-4 — atrapa cualquier intento futuro de meter tenant-awareness en `Apply`).

---

## 3. Consecuencias

### 3.1 Positivas

- Una sola instancia Azure sirve a N empresas con isolation estructural (no a nivel de WHERE manual).
- ERP `Maquinaria_V4` recibe el JWT del usuario real → su capa de autorización funciona end-to-end sin trucos.
- Dominio **sigue siendo agnóstico de multi-tenancy**: no conoce `IdEmpresa`, no la valida. Toda la responsabilidad vive en Infrastructure y en la configuración Marten. Si mañana se quiere mono-tenant, sólo se desactiva la policy.
- Logs y métricas correlacionables por empresa desde el primer request.

### 3.2 Negativas / deuda

- **FU-65 abierto (crítico):** `MartenInspeccionReader` resuelve `IQuerySession` eager por scope; en listeners Wolverine con outbox real puede caer fuera del scope tenanted y leer sin tenant. Requiere refactor a `IDocumentStore` + sesión por mensaje (mismo patrón que `MartenCatalogoSyncRepository` en erp-4).
- **FU-53 abierto (bloqueante CI):** los paquetes `SincoSoft.MYE.Common 1.5.1` y `SincoSoft.MYE.Middleware 1.1.6` viven en feeds Azure DevOps corporativos. CI necesita PAT antes del primer merge desde mt-1.
- **FU-54 abierto:** confirmar con Sergio/David el nombre exacto del claim `capabilities` en el JWT del host. Hoy hay fallback "always-allow" si falta.
- **FU-61 abierto:** `IncomingBearerCarrier` usa `AsyncLocal`. Si emergen patrones con thread-switch fuera de `await` (Task.Run sin captura, etc.), revisar.
- **FU-62 abierto:** si un mensaje queda en outbox > 1h y el JWT capturado expira, el retry fallará. Mitigación futura: refresh contra el host o token de servicio degradado.

### 3.3 Followups cerrados por este ADR

- FU-14 (claims reales del JWT) → mt-1.
- FU-52 (capability check en `/catalogos/sync`) → mt-1.
- FU-44 (propagación JWT al ERP) → mt-3.
- FU-56 (overload tenant-aware Wolverine 3) → mt-4.
- FU-57 (logs con tenant en listeners) → mt-3.
- FU-59 (rebuild cross-tenant defensivo) → mt-4.
- FU-60 (middleware captura bearer para outbox) → mt-4.

---

## 4. Invariantes formales

| Código | Invariante | Slice | Verificado por |
|---|---|---|---|
| MT1-INV-1 | Endpoints leen identidad sólo vía `ISessionService` | mt-1 | `SessionServicePipelineTests` |
| MT2-INV-1 | Apertura de sesión Marten en prod pasa por factory | mt-2 | `MartenConjoinedTenancyTests` |
| MT2-INV-2 | Stream tenant A no accesible desde sesión tenant B | mt-2 | `CrossTenantE2EIsolationTests` |
| MT2-INV-3 | Catálogos `Conjoined` sin excepciones | mt-2 | inspección configuración Marten |
| MT3-INV-1 | HTTP propaga Bearer del request al ERP | mt-3 | `BearerTokenPropagationHandlerTests` |
| MT3-INV-2 | Listeners propagan JWT del envelope con fallback service-account | mt-3 | `SincronizarDictamenVigenteBearerPropagationTests` |
| MT3-INV-3 | Fail-closed si no hay token (lanza antes de salir al ERP) | mt-3 | `BearerTokenAccessorTests` |
| MT4-INV-1 | POST tenant A invisible en queries tenant B bajo paralelismo | mt-4 | `CrossTenantE2EIsolationTests` |
| MT4-INV-2 | Middleware → envelope → listener propaga JWT | mt-4 | `CaptureBearerForOutboxEndToEndTests` |
| MT4-INV-3 | Logs y métricas incluyen `IdEmpresa` | mt-4 | `SessionLoggingScopeTests` |
| MT4-INV-4 | `Apply()` del aggregate es determinista cross-tenant | mt-4 | `RebuildCrossTenantDefensivoTests` |

---

## 5. Pre-requisitos operativos pre-piloto multi-empresa

Ver `slices/mt-4-e2e-isolation-observability/baseline-piloto.md` para el checklist operativo completo. Resumen:

1. **FU-53 cerrado** — CI puede restaurar paquetes corporativos.
2. **FU-65 cerrado** — `MartenInspeccionReader` resuelve sesión por mensaje, no por scope.
3. **FU-54 cerrado** — claim `capabilities` confirmado con host PWA.
4. Schema Marten migrado en cada base por-empresa (incluye columnas `tenant_id` + índices).
5. Tablero Grafana / Application Insights filtrable por `id_empresa`.

---

## 6. Referencias

- `slices/mt-1-jwt-claims-pipeline/` — claims pipeline + `ISessionService`.
- `slices/mt-2-marten-conjoined-tenancy/` — Marten Conjoined + factory.
- `slices/mt-3-jwt-propagation-erp/` — propagación JWT al ERP.
- `slices/mt-4-e2e-isolation-observability/` — captura JWT outbox + observabilidad + E2E + `baseline-piloto.md`.
- `Inspecciones/docs/06-contrato-apis-erp.md §0.B.5` — convención de auth contra `Maquinaria_V4`.
- `Inspecciones/docs/00-investigacion-mercado.md §9.2` — ADR-002 (identidad heredada del host).
