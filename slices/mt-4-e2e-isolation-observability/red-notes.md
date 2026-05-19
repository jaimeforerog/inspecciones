# Slice mt-4 — Red notes

**Fecha:** 2026-05-19
**Autor:** orquestador (rol `red` — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-4).

## Símbolos introducidos por los tests (pendientes para green)

### Infrastructure.Tests — `Auth/CaptureBearerForOutboxMiddlewareTests.cs`

Requeridos:
- `Inspecciones.Infrastructure.Auth.IncomingBearerCarrier` (static class)
  - `static string? GetForwardedAuth()`
  - `static IDisposable SetForCurrentScope(string? authHeader)`
- `Inspecciones.Infrastructure.Auth.CaptureBearerForOutboxMiddleware`
  - constructor `(RequestDelegate next)`
  - `Task Invoke(HttpContext ctx)`
- `Inspecciones.Infrastructure.Auth.ForwardAuthEnvelopeRule : Wolverine.IEnvelopeRule`
  - `void Modify(Envelope envelope)`

### Infrastructure.Tests — `Auth/SessionLoggingScopeTests.cs`

Requeridos:
- `Inspecciones.Infrastructure.Auth.SessionLoggingScope` (static class)
  - extension method `static IDisposable? BeginEmpresaScope(this ILogger logger, ISessionService session)`

### Domain.Tests — `Inspecciones/RebuildCrossTenantDefensivoTests.cs`

Sin nuevos símbolos. El test usa `Inspeccion.Reconstruir` ya existente y compara `Hallazgos`, `Contribuyentes`, `FirmadoPor`, `FirmadaEn`, etc. Verifica MT4-INV-4 (rebuild determinista).

### Api.Tests — `Tenancy/CrossTenantE2EIsolationTests.cs`

Sin nuevos símbolos. Reutiliza `ITenantedDocumentSessionFactory`, `WithSessionService`, `FakeSessionService`. Verifica MT4-INV-1 (aggregate + view + paralelismo). **Requiere Postgres** — si no disponible, los `[Fact]` corren y fallan en arranque del fixture (timeout Testcontainers) → el test runner los marca como failed, no skipped. **TODO en green/refactor:** si Postgres no disponible localmente, se aplicará `[Fact(Skip="Postgres no disponible — set POSTGRES_TEST_CONNSTRING o levantar Docker")]` a estos tests (mismo patrón que los 7 skips actuales). Verificable al correr `dotnet test tests/Inspecciones.Api.Tests/`.

### Api.Tests — `Tenancy/CaptureBearerForOutboxEndToEndTests.cs`

Sin nuevos símbolos. Verifica MT4-INV-2 (end-to-end del middleware + rule via lectura directa del outbox Postgres). **Requiere Postgres** — mismo patrón Skip.

## Naturaleza del rojo

- **Infrastructure.Tests:** compilación falla con `CS0103` (símbolo no existe) y `CS0246` (tipo no encontrado). Esperado. Verificado con `dotnet build tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj` — 20 errores, todos referentes a símbolos que green debe introducir.
- **Domain.Tests:** compila pero **falla en tiempo de ejecución solo si Apply tuviera lógica tenant-aware** — actualmente Apply es puro, por lo que el test debería pasar en green sin modificación. Es un test defensivo que blockea regresiones futuras.
- **Api.Tests:** compila pero falla en ejecución porque (a) el outbox no contiene `X-Forwarded-Authorization` (no hay middleware ni rule registrados), o (b) Postgres no disponible.

## Comando de reproducción

```bash
# Sin Postgres / sin docker:
dotnet build tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj --no-restore
# Resultado esperado: 20 errores CS0103/CS0246 referentes a símbolos que green debe crear.

dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --no-build
# Resultado esperado: los 2 nuevos tests pasan (Apply ya es puro). Defensivos.

dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj --no-build
# Resultado esperado: si Postgres no disponible, fail/error inicialización fixture.
# Si Postgres disponible: los nuevos tests fallan porque middleware + rule no registrados aún.
```

## Decisiones tomadas durante red

- **`ForwardAuthEnvelopeRule` implementa `Wolverine.IEnvelopeRule`** — interfaz pública del paquete `WolverineFx`. Test §6.5 verifica que no clobberea valor pre-existente; test §6.4 verifica que añade si no estaba; test §6.6 verifica que no añade si carrier vacío.
- **`IncomingBearerCarrier` es static class con AsyncLocal estático** — patrón idéntico al `AmbientBearerTokenAccessor` ya existente. FU-61 abierto defensivamente desde mt-3 cubre potencial migración a instance-AsyncLocal en el futuro.
- **`SessionLoggingScope.BeginEmpresaScope`** retorna `IDisposable?` (nullable) — sigue convención de `ILogger.BeginScope` que también puede retornar null. El test §6.11 verifica que retorna null cuando IdEmpresa lanza.
- **Test de captura de logger usa fake in-memory** — sin acoplar a Microsoft.Extensions.Logging.Testing (no dep agregada). Patrón `ScopeCaptor` con `AsyncLocal<Dictionary>` aísla scopes entre tests.
- **Tests E2E Api.Tests delegan el verificable del listener consumiendo el bearer a mt-3.** mt-4 inspecciona Postgres directo (`wolverine_outgoing_envelopes.headers`) para validar persistencia del header. No es flaky porque el commit del outbox es transaccional con el `SaveChangesAsync` del handler de firmar.

## Próxima fase

`green` introduce:
1. `src/Inspecciones.Infrastructure/Auth/IncomingBearerCarrier.cs`
2. `src/Inspecciones.Infrastructure/Auth/CaptureBearerForOutboxMiddleware.cs`
3. `src/Inspecciones.Infrastructure/Auth/ForwardAuthEnvelopeRule.cs`
4. `src/Inspecciones.Infrastructure/Auth/SessionLoggingScope.cs` (static class con extension)
5. `src/Inspecciones.Api/Program.cs`:
   - Registrar `app.UseMiddleware<CaptureBearerForOutboxMiddleware>()` al inicio del pipeline (env Test) o después del middleware corporativo (env Development/Production).
   - Registrar `opts.Policies.AllSenders(cfg => cfg.AddOutgoingRule(new ForwardAuthEnvelopeRule()))` en `UseWolverine`.
6. 15 endpoints (`InspeccionesEndpoints.cs` + `CatalogosEndpoints.cs`) reciben `ILogger<Program>` y abren `BeginEmpresaScope` al inicio.
7. `SincronizarDictamenVigenteListener` gana `TenantId={TenantId}` en su `LogSyncFallida` (simetría con `DescartarNovedadPreopErpListener`).
8. `Inspecciones.Infrastructure/Erp/InspeccionesMeters.cs` (static class con `Meter` BCL + `Counter<long>` para `inspecciones.erp.calls`).
9. Ambos listeners ERP incrementan el counter al éxito/fallo del adapter.

Si Postgres no disponible para tests E2E `Api.Tests`, aplicar `Skip` documentado a los nuevos tests con razón explícita.

Status: **red completado** — tests creados, 20 errores de compilación documentados como rojos esperados.
