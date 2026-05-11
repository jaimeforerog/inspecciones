# Slice fix-FU-37 — FakeTimeProvider en InspeccionesAppFactory

**Autor:** orchestrator (asumiendo rol domain-modeler para fix de plumbing de tests, no slice de comando — análogo a fix-FU-32)
**Fecha:** 2026-05-11
**Estado:** draft
**Agregado afectado:** ninguno — fix transversal de fixture de tests E2E
**Decisiones previas relevantes:**
- `FOLLOWUPS.md` FU-37 (original, ver §0 corrección de causa raíz)
- `CLAUDE.md` regla dura: "prohibido `DateTime.UtcNow` en dominio — `TimeProvider` inyectado"
- `48b8575 fix(FU-32): TestServer/Oakton lifecycle + switch Postgres local + paralelismo xUnit`
- `commits f0cb808 (1j), 475cfeb (1k), acc61b1 (1l)` — slices que añadieron tests E2E con asertos `BeCloseTo(CapturadoEn, ...)`

---

## 0. Corrección del FU-37 original

El FU-37 original en `FOLLOWUPS.md` (línea 257) afirma:

> "Tests `POST_generar_ot_happy_path...` y `POST_rechazar_generar_ot_happy_path...` fallan porque `resultado.SolicitadaEn` / `resultado.RechazadaEn` retornan la fecha actual del sistema (`2026-05-11`) en lugar de la fecha del test (`2026-05-08`). El test inyecta `CapturadoEn` como `DateTimeOffset(2026, 5, 8, ...)` pero los handlers `GenerarOTHandler` y `RechazarGenerarOTHandler` (o el aggregate) usan `DateTimeOffset.UtcNow` / `DateTime.UtcNow` directo en algún punto en lugar del `TimeProvider` inyectado."

**Esa causa raíz es incorrecta.** Auditoría del 2026-05-11 verificó:

- `src/Inspecciones.Application/Inspecciones/GenerarOTHandler.cs:18-21` — recibe `TimeProvider time` por DI, llama `_time.GetUtcNow()` en línea 37.
- `src/Inspecciones.Application/Inspecciones/RechazarGenerarOTHandler.cs:18-21` — idem, línea 37.
- `grep -r "DateTime\.UtcNow" src/Inspecciones.Domain` — sin coincidencias.
- `grep -r "DateTime\.UtcNow" src/Inspecciones.Application` — sin coincidencias.

Los handlers y el dominio cumplen la regla dura de CLAUDE.md. El bug real es **en la fixture de tests**:

- `Program.cs` registra `TimeProvider.System` en el contenedor DI (wall-clock).
- `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` **no reemplaza** ese registro con un `FakeTimeProvider` determinístico.
- El test instancia `CapturadoEn = new DateTimeOffset(2026, 5, 8, 15, 0, 0, TimeSpan.Zero)` y siembra eventos con ese timestamp.
- Cuando el handler se ejecuta, `_time.GetUtcNow()` devuelve el wall-clock real (`2026-05-11T...Z`), no `CapturadoEn`.
- La aserción `resultado.RechazadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5))` falla por delta de 3+ días.

**Conclusión:** el FU-37 original auditó la cadena de DI mal. El bug es plumbing puro de tests. Cero código de producción a tocar. El fix vive en la fixture.

## 1. Intención

Reemplazar `TimeProvider.System` por un `FakeTimeProvider` con timestamp fijo `2026-05-08T15:00:00Z` en la fixture `InspeccionesAppFactory`, para que los tests E2E que asertan timestamps con `BeCloseTo(CapturadoEn, ...)` se vuelvan deterministas y no dependan del wall-clock real al correrlos.

## 2. Comando

N/A — slice de fixture de tests, no es un comando de dominio.

## 3. Evento(s) emitido(s)

N/A — sin cambios de eventos del dominio.

## 4. Precondiciones

N/A — sin cambios al método de decisión del agregado.

## 5. Invariantes tocadas

N/A — sin cambios al dominio. Las invariantes del agregado siguen idénticas.

## 6. Escenarios Given / When / Then

Los tests existentes del slice 1k/1l/1c son los que validan el comportamiento. El fix de fixture los desbloquea sin añadir tests nuevos.

### 6.1 Test §6.1 del slice 1k — happy path `GenerarOT`

**Given**
- Una inspección firmada con hallazgo `RequiereIntervencion` y dictamen `NoPuedeOperar`, sembrada con `CapturadoEn = 2026-05-08T15:00:00Z` (todos los eventos del stream).
- `FakeTimeProvider.SetUtcNow(2026-05-08T15:00:00Z)` registrado en el contenedor DI de la fixture.

**When**
- El cliente envía `POST /api/v1/inspecciones/{id}/generar-ot` con header `X-Client-Command-Id` y `X-Capabilities: generar-ot`.

**Then**
- Status 202 Accepted.
- `resultado.SolicitadaEn` está dentro de `TimeSpan.FromSeconds(60)` de `CapturadoEn` — pasa porque `_time.GetUtcNow()` devuelve `2026-05-08T15:00:00Z`.

### 6.2 Test §6.1 del slice 1l — happy path `RechazarGenerarOT`

**Given**
- Misma siembra que §6.1.
- `FakeTimeProvider.SetUtcNow(2026-05-08T15:00:00Z)`.

**When**
- El cliente envía `POST /api/v1/inspecciones/{id}/rechazar-generar-ot` con body `{ motivo: "..." }`.

**Then**
- Status 200 OK.
- `resultado.RechazadaEn` está dentro de `TimeSpan.FromMinutes(5)` de `CapturadoEn` — pasa.

### 6.3 Test §6.1 del slice 1c — happy path `RegistrarHallazgo` (latente)

**Given**
- Inspección iniciada, equipo con parte 77.
- `FakeTimeProvider.SetUtcNow(2026-05-08T15:00:00Z)`.

**When**
- El cliente envía `POST /api/v1/inspecciones/{id}/hallazgos`.

**Then**
- **Bloqueado por FU-36** — el endpoint retorna 400 BadRequest antes de llegar al aserto de timestamp. Cuando FU-36 cierre, el aserto `resultado.RegistradoEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(30))` quedará cubierto por este fix. No cuenta como test verde de este slice.

### 6.4 No-regresión — tests que NO asertan `BeCloseTo`

**Given**
- `FakeTimeProvider` registrado.

**When**
- Cualquiera de los 24 tests ya verdes en el suite `Inspecciones.Api.Tests` corre.

**Then**
- Siguen verdes. El cambio de `TimeProvider.System` a `FakeTimeProvider` con timestamp fijo es transparente para tests que no inspeccionan timestamps.

### 6.5 No-regresión — `Inspecciones.Domain.Tests` (197/197)

**Given**
- Sin cambios al dominio.

**When**
- `dotnet test tests/Inspecciones.Domain.Tests`.

**Then**
- 197 pass. Garantía estructural — el fix solo toca código de tests.

### 6.6 No-regresión — `Inspecciones.Application.Tests`

**Given**
- Sin cambios al handler.

**When**
- `dotnet test tests/Inspecciones.Application.Tests`.

**Then**
- Los tests del slice 1k y 1l de `Application.Tests` ya inyectan `FakeTimeProvider` en sus propios fixtures (verificado al revisar la suite). Sin regresión.

## 7. Idempotencia / retries

N/A — no aplica a fixture de tests.

## 8. Impacto en proyecciones / read models

N/A — sin cambios a proyecciones.

## 9. Impacto en endpoints HTTP

N/A — los endpoints no cambian. Cambia el `TimeProvider` que el contenedor DI les inyecta en el contexto de tests.

## 10. Impacto en SignalR / push

N/A.

## 11. Impacto en adapters Sinco on-prem

N/A.

## 12. Preguntas abiertas

Ninguna. La decisión Opción A (registrar `FakeTimeProvider` en la fixture) fue tomada por el usuario antes de arrancar el slice. Las opciones B (`TimeProvider.System` global con `AddDays(-3)`) y C (parametrizar `CapturadoEn` en los tests con `time.GetUtcNow()` dinámico) fueron descartadas.

## 13. Cambios concretos esperados (cero código de dominio)

**Archivo 1 — `tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj`:**

Añadir al `<ItemGroup>` de package references:

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

El paquete ya está en `Directory.Packages.props` línea 29 con versión `9.0.0` — solo falta la referencia explícita en el csproj.

**Archivo 2 — `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs`:**

Dentro de `ConfigureWebHost.builder.ConfigureServices`, después del bloque que remueve `EventLogLoggerProvider`, añadir:

```csharp
// Reemplazar TimeProvider.System por FakeTimeProvider con timestamp fijo, para que
// los tests E2E que asertan BeCloseTo(CapturadoEn, ...) sean deterministas
// independientemente del wall-clock real al momento de correr.
//
// CapturadoEn canónico de los tests: 2026-05-08T15:00:00Z.
//
// Cierra FU-37. Causa raíz original mal descrita: el bug NO es DateTime.UtcNow en
// handlers (auditado — los handlers usan TimeProvider correctamente). El bug es
// la ausencia del swap en esta fixture.
var timeDescriptors = services
    .Where(d => d.ServiceType == typeof(TimeProvider))
    .ToList();
foreach (var d in timeDescriptors)
{
    services.Remove(d);
}
var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
    new DateTimeOffset(2026, 5, 8, 15, 0, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(fakeTime);
```

**No se toca:**
- `src/Inspecciones.Api/Program.cs` — sigue registrando `TimeProvider.System` para producción.
- `src/Inspecciones.Application/Inspecciones/*Handler.cs` — sin cambios.
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — sin cambios.
- Ningún test individual — la fixture cubre todo el suite.

## 14. Resultado esperado

Antes:
```
Passed: 24, Failed: 8 (8 = FU-36 ×2 + FU-37 ×2 + FU-38 ×2 + idempotencia skip ×2... etc)
```

Después:
```
Passed: 26, Failed: 6 (6 = FU-36 ×2 + FU-38 ×4)
```

Los 2 tests adicionales que pasan son:
- `POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto` (slice 1k)
- `POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto` (slice 1l)

El test `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` (slice 1c) sigue rojo por FU-36 — el endpoint retorna 400 antes del aserto de timestamp.

## 15. Checklist pre-firma

- [x] Cero cambios al dominio. Cero invariantes nuevas. Cero excepciones nuevas.
- [x] Causa raíz original auditada y corregida explícitamente en §0.
- [x] Archivos a tocar listados en §13 — solo 2: csproj + fixture.
- [x] Tests existentes que se desbloquean enumerados en §6.1, §6.2, §6.3.
- [x] Tests que NO deben regresar enumerados en §6.4, §6.5, §6.6.
- [x] Preguntas abiertas: 0.
- [x] Justificación de la Opción A (vs B/C) implícita en la decisión del usuario antes de arrancar el slice.
