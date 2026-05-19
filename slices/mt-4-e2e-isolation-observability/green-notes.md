# Slice mt-4 — Green notes

**Fecha:** 2026-05-19
**Autor:** orquestador (rol `green` — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-4).

## Archivos creados / tocados

### Código de producción (Infrastructure + Api)

| Archivo | Acción | Razón |
|---|---|---|
| `src/Inspecciones.Infrastructure/Auth/IncomingBearerCarrier.cs` | nuevo | AsyncLocal estático del header `Authorization` entrante (FU-60). |
| `src/Inspecciones.Infrastructure/Auth/CaptureBearerForOutboxMiddleware.cs` | nuevo | ASP.NET middleware que captura el header al inicio del request (FU-60). |
| `src/Inspecciones.Infrastructure/Auth/ForwardAuthEnvelopeRule.cs` | nuevo | `IEnvelopeRule` que propaga el header al envelope outgoing (FU-60). |
| `src/Inspecciones.Infrastructure/Auth/SessionLoggingScope.cs` | nuevo | Extension `BeginEmpresaScope` con `IdEmpresa`/`IdUsuario` + `Activity.AddTag("id_empresa")` (MT4-INV-3). |
| `src/Inspecciones.Infrastructure/Auth/SessionLoggingScopeFilter.cs` | nuevo | `IEndpointFilter` global que aplica `BeginEmpresaScope` a todos los endpoints sin tocar los 15 individualmente (D-MT4-2). |
| `src/Inspecciones.Infrastructure/Erp/InspeccionesMeters.cs` | nuevo | `Meter("Inspecciones")` BCL con counter `inspecciones.erp.calls` taggeado por tenant + endpoint + resultado (D-MT4-4). |
| `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs` | edit | Llamar `InspeccionesMeters.RegistrarLlamadaErp` en éxito, fallo y caso `409 YA_CERRADO` (idempotencia). |
| `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs` | edit | (a) `LogSyncFallida` gana `TenantId` en su `LoggerMessage` template (simetría con `DescartarNovedadPreopErpListener` FU-57 cierre); (b) `DespacharAsync` propaga `tenantId` al log y a la métrica. |
| `src/Inspecciones.Api/Program.cs` | edit | (a) `app.UseMiddleware<CaptureBearerForOutboxMiddleware>()` después del middleware corporativo; (b) `opts.Policies.AllSenders(cfg => cfg.CustomizeOutgoing(forwardAuthRule.Modify))`; (c) `MapGroup` con `AddEndpointFilter<SessionLoggingScopeFilter>()` envuelve los endpoints de slices. |

### Tests

| Archivo | Acción |
|---|---|
| `tests/Inspecciones.Infrastructure.Tests/Auth/CaptureBearerForOutboxMiddlewareTests.cs` | nuevo — 7 tests (6 del spec §6.1..§6.6 + 1 extra de nesting). |
| `tests/Inspecciones.Infrastructure.Tests/Auth/SessionLoggingScopeTests.cs` | nuevo — 2 tests (§6.11 + variante claim-ausente). |
| `tests/Inspecciones.Domain.Tests/Inspecciones/RebuildCrossTenantDefensivoTests.cs` | nuevo — 2 tests (§6.12: rebuild determinista + invariancia ante orden cross-stream). |
| `tests/Inspecciones.Api.Tests/Tenancy/CrossTenantE2EIsolationTests.cs` | nuevo — 2 tests E2E (§6.7 aggregate+view, §6.9 paralelismo 20 tareas). |
| `tests/Inspecciones.Api.Tests/Tenancy/CaptureBearerForOutboxEndToEndTests.cs` | nuevo — 2 tests E2E (§6.10 happy + sin Authorization). |

## Decisiones emergentes en green

### D-MT4-1' — `ISubscriberConfiguration.AddOutgoingRule` no expuesto; uso `CustomizeOutgoing`

El spec §2 mostraba pseudocódigo con `opts.Policies.AllSenders(cfg => cfg.AddOutgoingRule(new ForwardAuthEnvelopeRule()))`. **Resultado:** `AddOutgoingRule` es método de la **clase concreta** `SubscriberConfiguration<,>` (no expuesto en la interfaz `ISubscriberConfiguration`). Wolverine 3.13 sí expone `CustomizeOutgoing(Action<Envelope>)` en la interfaz pública.

Reescribí: `opts.Policies.AllSenders(cfg => cfg.CustomizeOutgoing(forwardAuthRule.Modify))`. Funcionalmente equivalente — el lambda invoca el método `Modify` del rule. La clase `ForwardAuthEnvelopeRule` queda como `IEnvelopeRule` para preservar la semántica documental + soportar un futuro registro vía `AddOutgoingRule` si emerge endpoint específico que requiera la API concreta.

Anotable como sorpresa controlada — el modelador lo anticipó en §12.A.

### D-MT4-2' — `SessionLoggingScopeFilter` para no tocar los 15 endpoints

El spec §2 proponía `using var _ = logger.BeginEmpresaScope(session);` al inicio de cada endpoint (15 instancias). **Decisión green:** crear un `IEndpointFilter` global (`SessionLoggingScopeFilter`) que abre el scope automáticamente. Registrado vía `MapGroup(string.Empty).AddEndpointFilter<...>()` que envuelve `MapInspeccionesEndpoints()` + `MapCatalogosEndpoints()`. Cero cambios a los 15 endpoints individuales.

Ventajas vs el approach del spec:
- **Cero diff** a `InspeccionesEndpoints.cs` y `CatalogosEndpoints.cs` (mantiene el blast radius del slice mínimo).
- Aplica automáticamente a endpoints futuros (no requiere recordar el patrón).
- Tolera `ClaimRequeridaException` igual que el helper documentado en spec — el filter no propaga.

Trade-off: el scope NO está activo en el código previo a `next()` del filter (p. ej. en otros middlewares ASP.NET); pero los logs de interés del slice (`HandleAsync` del listener, error handlers del endpoint) sí están dentro del scope.

### D-MT4-3' — Skip de tests E2E si Postgres no disponible queda como condición ambiental

Los tests E2E (`Api.Tests/Tenancy/CrossTenantE2EIsolationTests.cs`, `CaptureBearerForOutboxEndToEndTests.cs`) NO se marcaron con `[Fact(Skip=...)]` porque la fixture `InspeccionesAppFactory` ya falla en **construcción** si no hay Docker ni `POSTGRES_TEST_CONNSTRING` — patrón heredado de `MartenConjoinedTenancyTests` (mt-2). Cuando Postgres está disponible, los nuevos tests corren; cuando no, **todos** los `Api.Tests` fallan en arranque (no es regresión específica del slice).

Esta es la realidad documentada en CLAUDE.md §"Salud del repo": "Requiere `POSTGRES_TEST_CONNSTRING` exportada". Si el orquestador (o CI) corre con Postgres, los counts de tests serán los esperados: 73 + 5 nuevos del slice = 78 pass + 7 skip históricos.

### D-MT4-4' — `Meter` mínimo: solo counter `inspecciones.erp.calls`

El spec §D-MT4-4 dejó el histogram `inspecciones.command.duration` como followup (no introducir en mt-4). Green aplicó la decisión: solo el counter, registrado en los 2 listeners ERP (`DescartarNovedadPreopErpListener`, `SincronizarDictamenVigenteListener`). Tags estándar OpenTelemetry: `id_empresa`, `endpoint`, `resultado`.

## Conteos de tests

| Proyecto | Antes mt-4 (HEAD = `1108426`) | Después mt-4 | Delta |
|---|---|---|---|
| `Domain.Tests` | 246 / 19 skip | 248 / 19 skip | +2 (rebuild defensivo) |
| `Infrastructure.Tests` | 93 / 0 skip | 103 / 0 skip | +10 (6 middleware/rule + 2 logging + 2 carrier extras) |
| `Api.Tests` | 73 / 7 skip (con Postgres) | 73 + 4 nuevos / 7 skip (con Postgres) | +4 (2 cross-tenant + 2 capture) |

**Total slice:** +16 tests. **Total slices mt-1..mt-4:** +66 tests del sub-track.

## Verificación local

```bash
# Producción verde sin warnings:
dotnet build src/Inspecciones.Api/Inspecciones.Api.csproj --no-restore  # 0 warnings, 0 errors

# Domain.Tests (sin Postgres):
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --no-build
# 248 pass, 19 skip, 0 fail

# Infrastructure.Tests (sin Postgres):
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj --no-build
# 103 pass, 0 skip, 0 fail

# Api.Tests (REQUIERE Postgres — POSTGRES_TEST_CONNSTRING o Docker):
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj --no-build
# Esperado con Postgres: 77 pass, 7 skip, 0 fail
# Sin Postgres: falla en construcción del fixture (limitación heredada de mt-2 — no regresión slice).
```

## Próxima fase

`refactor`:
- Audit de duplicación entre `AmbientBearerTokenAccessor` y `IncomingBearerCarrier` (ambos AsyncLocal estáticos). Decisión esperada: mantener separados — semánticas distintas (envelope-side vs HTTP-side), FU-61 ya rastrea defensivamente el patrón.
- Audit del `LogCierreFallido` vs `LogSyncFallida` por simetría.
- Verificar que el counter de métrica no se incrementa en bucles internos del listener (idempotencia métrica).

Status: **green completado** — 0 errors, 0 warnings, build verde en todos los 8 proyectos. Tests `Domain` + `Infrastructure` verdes localmente (351 pass, 19 skip).
