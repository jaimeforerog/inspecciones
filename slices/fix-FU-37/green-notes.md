# Green notes — fix-FU-37 (FakeTimeProvider en InspeccionesAppFactory)

**Autor:** orchestrator asumiendo rol `green` (slice transversal de fixture — análogo a fix-FU-32)
**Fecha:** 2026-05-11
**Estado:** verde verificado

## 1. Cambios aplicados

### 1.1 `tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj`

Se añadió la referencia de paquete (versión heredada de `Directory.Packages.props`):

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

### 1.2 `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs`

Dentro de `ConfigureWebHost.ConfigureServices`, después del bloque que remueve `EventLogLoggerProvider`, se añadió el swap del descriptor:

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
var fakeTime = new FakeTimeProvider(
    new DateTimeOffset(2026, 5, 8, 15, 0, 0, TimeSpan.Zero));
services.AddSingleton<TimeProvider>(fakeTime);
```

Junto con el `using` correspondiente:

```csharp
using Microsoft.Extensions.Time.Testing;
```

## 2. Desvío del spec aprobado por el usuario — armonización colateral en test 1k

### 2.1 Contexto

El spec §0 establece que el `CapturadoEn` canónico de los tests es `2026-05-08T15:00:00Z`. Al cargar el `FakeTimeProvider` con ese timestamp, el test `POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto` (slice 1k) seguía rojo: la constante `CapturadoEn` declarada en `GenerarOTEndpointTests.cs:29` estaba a las **14:00Z**, no a las 15:00Z como el resto del suite.

La aserción `resultado.SolicitadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(60))` exigía cercanía a `2026-05-08T14:00:00Z`, pero el handler invocaba `_time.GetUtcNow()` que devolvía `2026-05-08T15:00:00Z` (el FakeTimeProvider) — delta de 1 h, fuera del umbral de 60 s.

### 2.2 Decisión del usuario

**Opción A elegida:** armonizar `CapturadoEn` del slice 1k de `14:00Z` a `15:00Z` para alinear con el timestamp canónico del spec FU-37. El test 1l (`RechazarGenerarOTEndpointTests`) ya usaba `15:00Z`, así que el slice 1k era el outlier.

Alternativas descartadas:
- Cambiar el `FakeTimeProvider` a `14:00Z` rompía 1l.
- Cargar dos `FakeTimeProvider` por test agregaba complejidad innecesaria.

### 2.3 Cambio aplicado

```diff
- private static readonly DateTimeOffset CapturadoEn =
-     new(2026, 5, 8, 14, 0, 0, TimeSpan.Zero);
+ private static readonly DateTimeOffset CapturadoEn =
+     new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);
```

Archivo: `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs:28-29`.

La constante es la única declaración del timestamp en ese archivo — los 5+ usos (siembra de eventos, asertos GPS, asertos de timestamp) derivan automáticamente del cambio. No hay literales `14, 0, 0` adicionales que rompan al armonizar.

## 3. Verificación del fix

### 3.1 Tests del spec en verde

Comando:

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj `
  --filter "FullyQualifiedName~POST_generar_ot_happy_path|FullyQualifiedName~POST_rechazar_generar_ot_happy_path"
```

Output:

```
Correctas! - Con error: 0, Superado: 2, Omitido: 0, Total: 2, Duración: 6 s
```

Ambos tests (slice 1k §6.1 y slice 1l §6.1) verdes — confirma que `FakeTimeProvider` swappeó correctamente y la aserción `BeCloseTo(CapturadoEn, ...)` cae dentro del umbral.

### 3.2 Suite completa de `Inspecciones.Api.Tests`

Comando:

```powershell
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj
```

Output:

```
Con error: 4, Superado: 26, Omitido: 2, Total: 32, Duración: 11 s
```

- **+2 verdes** vs estado pre-fix (24 → 26) — exactamente lo predicho en spec §14.
- **4 rojos** restantes son bugs preexistentes documentados como FU-36 (×2) y FU-38 (×2). No regresan, no son del scope de FU-37.
- **2 skip** son los tests de idempotencia ADR-008 (Wolverine envelope dedup) ya marcados antes de este slice.

### 3.3 No-regresión

- `Inspecciones.Domain.Tests` no requirió re-corrida — cero cambios al dominio.
- Los 24 tests que ya estaban verdes en `Inspecciones.Api.Tests` (e.g. los 7 de `RechazarGenerarOTEndpointTests` no-happy-path, los 5 de `AsignarRepuestoEndpointTests`, etc.) siguen verdes — el swap de TimeProvider es transparente para tests que no inspeccionan timestamps.

## 4. Estado final de los 4 rojos remanentes (no FU-37)

| Test | Falla | FU |
|---|---|---|
| `RegistrarHallazgo.happy_path` | 400 BadRequest en vez de 201 | FU-36 |
| `RegistrarHallazgo.replay_ADR_008` | 400 BadRequest en vez de 201 | FU-36 |
| `GenerarOT.sin_capability` | 500 InternalServerError en vez de 403 | FU-38 |
| `RechazarOT.sin_capability` | 500 InternalServerError en vez de 403 | FU-38 |

El test `RegistrarHallazgo.happy_path` (latente §6.3) seguirá rojo hasta que FU-36 cierre — el endpoint retorna 400 antes de evaluar el aserto de timestamp, así que el fix de FU-37 no lo hace verde por sí solo.

## 5. Archivos modificados (resumen)

| Archivo | Cambio |
|---|---|
| `tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj` | +1 `<PackageReference>` (`Microsoft.Extensions.TimeProvider.Testing`) |
| `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` | +1 using; bloque swap `TimeProvider` → `FakeTimeProvider(2026-05-08T15:00Z)` |
| `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs` | Armonización `CapturadoEn`: `14:00Z` → `15:00Z` (desvío aprobado por usuario, ver §2) |

**Cero código de dominio tocado. Cero handlers tocados. Cero archivos de producción tocados.**

## 6. Criterio de paso a refactor

- [x] Tests del spec en verde (2/2 happy paths).
- [x] Suite completa: cero regresiones, +2 verdes esperados conseguidos.
- [x] Cambios documentados con archivo, línea, diff.
- [x] Desvío del spec (armonización 1k) documentado con decisión del usuario y justificación.
- [x] FU-36 y FU-38 claramente identificados como out-of-scope.
