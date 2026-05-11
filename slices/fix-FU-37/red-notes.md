# Red notes — fix-FU-37 (FakeTimeProvider en InspeccionesAppFactory)

**Autor:** orchestrator asumiendo rol `red` (slice transversal de fixture — análogo al patrón aplicado en `fix-FU-32`)
**Fecha:** 2026-05-11
**Estado:** rojo verificado

## 0. Naturaleza del slice

Este slice **no escribe tests nuevos** — el "rojo" ya existe en el repo. Los 2 tests E2E de `Inspecciones.Api.Tests` que asertan `BeCloseTo(CapturadoEn, ...)` (introducidos en slices 1k y 1l) fallan deterministicamente por la causa raíz documentada en `spec.md §0`: `InspeccionesAppFactory` registra `TimeProvider.System` (wall-clock real) en lugar de `FakeTimeProvider` con el timestamp canónico de los tests (`2026-05-08T15:00:00Z`).

El rol de la fase `red` aquí es **documentar formalmente** que los rojos existen, qué tests son, y que fallan por la razón correcta (delta de wall-clock vs `CapturadoEn`) — no por compilación, no por timeout, no por otra excepción.

## 1. Tests rojos identificados

### 1.1 `POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto`

**Archivo:** `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs:283`
**Slice de origen:** 1k (`feat(slice-1k): GenerarOT` — commit `475cfeb`)
**Aserto que falla:**

```csharp
resultado.SolicitadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(60));
```

**Mensaje de fallo (corrida 2026-05-11T14:47Z):**

```
Expected resultado.SolicitadaEn to be within 1m from <2026-05-08 14:00:00 +0h>,
but <2026-05-11 14:47:48.771918 +0h> was off by 3d, 47m, 48s, 771ms and 918.0µs.
```

**Stack trace:**

```
FluentAssertions.Primitives.DateTimeOffsetAssertions`1.BeCloseTo(...)
  → GenerarOTEndpointTests.POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto()
    at GenerarOTEndpointTests.cs:line 283
```

**Mapeo a spec §6:** §6.1 — happy path `GenerarOT`.

**Razón del fallo (validada):** `GenerarOTHandler` en línea 37 llama `_time.GetUtcNow()`. El `TimeProvider` resuelto por el contenedor DI del host de tests es `TimeProvider.System` (registrado en `Program.cs`), no fue reemplazado por la fixture. Devuelve wall-clock real `2026-05-11T14:47Z`, mientras `CapturadoEn` del test es `2026-05-08T14:00Z` — delta de 3 días + 47 min, que excede la precisión de 60s del aserto.

### 1.2 `POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto`

**Archivo:** `tests/Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs:150`
**Slice de origen:** 1l (`feat(slice-1l): RechazarGenerarOT` — commit `acc61b1`)
**Aserto que falla:**

```csharp
resultado.RechazadaEn.Should().BeCloseTo(CapturadoEn, precision: TimeSpan.FromMinutes(5));
```

**Mensaje de fallo (corrida 2026-05-11T14:47Z):**

```
Expected resultado.RechazadaEn to be within 5m from <2026-05-08 15:00:00 +0h>,
but <2026-05-11 14:47:49.0297783 +0h> was off by 2d, 23h, 47m, 49s, 29ms and 778.3µs.
```

**Stack trace:**

```
FluentAssertions.Primitives.DateTimeOffsetAssertions`1.BeCloseTo(...)
  → RechazarGenerarOTEndpointTests.POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto()
    at RechazarGenerarOTEndpointTests.cs:line 150
```

**Mapeo a spec §6:** §6.2 — happy path `RechazarGenerarOT`.

**Razón del fallo (validada):** misma causa raíz que 1.1 — `RechazarGenerarOTHandler` línea 37 invoca `_time.GetUtcNow()` que devuelve wall-clock real porque la fixture no swappea el descriptor.

## 2. Test latente bloqueado (no cubierto por este slice)

### 2.1 `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created`

**Archivo:** `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs:115`
**Slice de origen:** 1c (`RegistrarHallazgo`)
**Estado:** **bloqueado por FU-36** — el endpoint retorna 400 BadRequest antes de llegar al aserto `BeCloseTo(CapturadoEn, precision: TimeSpan.FromSeconds(30))`. No cuenta como rojo de FU-37. Cuando FU-36 cierre y el endpoint pase del 400 al 201, este test quedará automáticamente cubierto por el fix de FU-37 (gracias al `FakeTimeProvider` registrado en la fixture).

Documentado en spec §6.3 explícitamente como "no cuenta como test verde de este slice".

## 3. Comando exacto de verificación

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj `
  --filter "FullyQualifiedName~POST_generar_ot_happy_path|FullyQualifiedName~POST_rechazar_generar_ot_happy_path"
```

**Resultado observado (2026-05-11T14:47Z, antes del fix):**

```
Con error:    2, Superado:    0, Omitido:    0, Total:    2, Duración: 13 s - Inspecciones.Api.Tests.dll (net9.0)
```

## 4. Validez del rojo (checklist)

- [x] Los 2 tests **compilan** — el rojo no es por error de compilación.
- [x] Los 2 tests **fallan por aserción** — `BeCloseTo` reporta delta exacto, no excepción de runtime no esperada.
- [x] Los 2 tests **fallan por la razón correcta** — el delta es consistente con la teoría de causa raíz (wall-clock vs `CapturadoEn`).
- [x] Los 2 tests **fallan deterministicamente** — la corrida del 2026-05-11 reproduce el mismo delta de ~3 días (no flakiness, no timeout).
- [x] Mensaje de error contiene timestamp esperado (`2026-05-08`) y timestamp real (`2026-05-11`) — evidencia directa del bug.
- [x] Cero código de producción a tocar — auditoría §0 del spec confirma que `GenerarOTHandler:37` y `RechazarGenerarOTHandler:37` usan `_time.GetUtcNow()` correctamente.

## 5. Próxima fase

Pasar a `green`: añadir paquete `Microsoft.Extensions.TimeProvider.Testing` al csproj de `Api.Tests` y swappear el descriptor `TimeProvider` en `InspeccionesAppFactory.ConfigureWebHost` por un `FakeTimeProvider` con timestamp fijo `2026-05-08T15:00:00Z`. Cambios concretos enumerados en `spec.md §13`.

## 6. Criterio de paso a green

- [x] `red-notes.md` presente (este archivo) con los 2 rojos documentados, mensajes de fallo exactos y mapeo a spec §6.
- [x] Comando de reproducción del rojo capturado en §3.
- [x] Causa raíz validada como wall-clock vs `CapturadoEn` (no DateTime.UtcNow en handlers).
