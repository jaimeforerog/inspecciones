# Refactor notes — fix-FU-37 (FakeTimeProvider en InspeccionesAppFactory)

**Autor:** orchestrator asumiendo rol `refactorer` (slice transversal de fixture)
**Fecha:** 2026-05-11
**Veredicto:** **sin cambios**

## 1. Alcance del refactor

El slice no tocó código de dominio, ni handlers, ni adapters. Los únicos archivos modificados son plumbing de tests:

- `tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj` (1 línea agregada)
- `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` (1 using + bloque de swap de DI)
- `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs` (1 dígito cambiado)

No hay duplicación a extraer, no hay primitive obsession a corregir, no hay nombres a refactorizar.

## 2. Calidad del código añadido (revisión rápida)

### 2.1 Bloque del swap en `InspeccionesAppFactory.cs:115-133`

- Comentario explícito que documenta intención, timestamp canónico, FU que cierra, y la corrección de causa raíz original. Útil para futura mantención.
- Patrón consistente con el bloque inmediatamente anterior que remueve `EventLogLoggerProvider` (mismo idiom `services.Where(...).ToList()` + `foreach` + `services.Remove(...)`).
- Variable local `fakeTime` única, sin estado mutable expuesto — el `FakeTimeProvider` no se expone afuera de la fixture; los tests que necesiten avanzar el reloj podrían hacerlo via `factory.Services.GetRequiredService<TimeProvider>() as FakeTimeProvider`, pero ningún test actual lo requiere.

### 2.2 Armonización en `GenerarOTEndpointTests.cs:29`

- Cambio de 1 dígito en una constante. No hay nada que refactorizar.

### 2.3 csproj

- 1 línea adicional, posición correcta dentro del `<ItemGroup>` de packages. Versión heredada de `Directory.Packages.props` (Central Package Management) — sin duplicación de versión.

## 3. Warnings

`dotnet build` no introduce warnings nuevos. `TreatWarningsAsErrors=true` está activo en el repo (regla dura CLAUDE.md) — si hubiera warnings, el build hubiera fallado.

## 4. Tests siguen verdes

Confirmado en green-notes §3: 26/32 passing, los 4 rojos restantes son FU-36/FU-38 preexistentes.

## 5. Criterio de paso a review

- [x] Cero cambios de refactor aplicados — slice no los amerita.
- [x] Calidad del código añadido revisada y aprobada.
- [x] Cero warnings nuevos.
- [x] Tests siguen verdes.
