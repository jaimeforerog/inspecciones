# Green notes — fix-FU-36 (JsonStringEnumConverter en Minimal APIs)

**Autor:** green
**Fecha:** 2026-05-11
**Estado:** CERRADO — 29 passing, 3 skip, 0 failing en Api.Tests; 197/197 en Domain.Tests

---

## 1. Archivos modificados

- `src/Inspecciones.Api/Program.cs`
- `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs`

---

## 2. Diff resumido — `src/Inspecciones.Api/Program.cs`

### a) Using agregado (línea 1)

```csharp
using System.Text.Json.Serialization;
```

### b) Comentario removido (líneas 28-30 originales)

```diff
-        // JSON serializer — usa el default de Marten (Newtonsoft.Json). El detalle de
-        // configuración del serializer (System.Text.Json + casing + enum como string) se
-        // cierra en un slice posterior cuando emerja necesidad concreta.
```

El comentario fue eliminado del bloque `AddMarten`. FU-36 es la "necesidad concreta" que el comentario anticipaba.

### c) Bloque ConfigureHttpJsonOptions agregado (antes de `builder.Build()`)

```csharp
// JSON serializer — Minimal APIs: enums como string en request y response bodies.
// FU-36: cierra el comentario que anticipaba esta configuración.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
```

---

## 3. Diff resumido — `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs`

### a) Armonización de `CapturadoEn` (línea 20)

**Antes:**
```csharp
private static readonly DateTimeOffset CapturadoEn = new(2026, 5, 6, 10, 0, 0, TimeSpan.FromHours(-5));
```

**Después:**
```csharp
private static readonly DateTimeOffset CapturadoEn = new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);
```

**Justificación:** El `FakeTimeProvider` registrado en `InspeccionesAppFactory.cs` (introducido en FU-37) usa el timestamp canónico `2026-05-08T15:00:00Z`. El handler invoca `_time.GetUtcNow()` y produce `RegistradoEn = 2026-05-08T15:00:00Z`, pero el assert original exigía `BeCloseTo(2026-05-06T15:00:00Z, precision: 30s)` — delta de 2 días. Cambio idéntico al aplicado en FU-37 para `GenerarOTEndpointTests.cs` (commit `152e080`), donde se armonizó el timestamp del test al `FakeTimeProvider` canónico. Aprobado por el orquestador como desvío del spec.

### b) Skip ADR-008 en test de idempotencia (línea 122)

**Antes:**
```csharp
[Fact]
public async Task POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008()
```

**Después:**
```csharp
[Fact(Skip = "Requiere Wolverine envelope storage con MessageId dedup. " +
             "El store en Testcontainers no tiene Wolverine envelope habilitado. " +
             "Implementar cuando el handler esté registrado como Wolverine handler " +
             "con durable local queues. Ver spec §6.16, §7, ADR-008 §9.16.")]
public async Task POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008()
```

**Justificación:** Sin Wolverine envelope dedup activo en el entorno de test local, el segundo POST ejecuta el handler de nuevo y retorna `201 Created` en lugar del `200 OK` que la dedup devolvería con la respuesta original del envelope. El spec FU-36 §2 anticipaba este comportamiento. El formato del `[Fact(Skip = ...)]` es idéntico al usado en `GenerarOTEndpointTests.cs` (§6.9) y `RechazarGenerarOTEndpointTests.cs` (§6.13) — patrón canónico del repo. Aprobado por el orquestador.

---

## 4. Output dotnet test

### 4.1 Inspecciones.Domain.Tests

```
Correctas! - Con error: 0, Superado: 197, Omitido: 12, Total: 209
```

Sin regresión en Domain.Tests.

### 4.2 Inspecciones.Api.Tests

```
Correctas! - Con error: 0, Superado: 29, Omitido: 3, Total: 32
```

**Resultado final:**
- 29 passing (era 28 antes del fix; +1 por el happy path `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` ahora verde)
- 3 skip (era 2; +1 por el test ADR-008 de `RegistrarHallazgo` ahora marcado como skip)
- 0 failing (era 2)

---

## 5. Verificación de compilación

`TreatWarningsAsErrors=true` satisfecho. Cero warnings en compilación.

---

## 6. Estado de los criterios del DoD (spec §6)

| Criterio | Estado |
|---|---|
| `ConfigureHttpJsonOptions` con `JsonStringEnumConverter` registrado antes de `builder.Build()` | CUMPLIDO |
| Comentario `Program.cs:28-30` eliminado | CUMPLIDO |
| App compila sin warnings (`nullable` + `TreatWarningsAsErrors=true`) | CUMPLIDO |
| `Inspecciones.Domain.Tests` sin regresión | CUMPLIDO (197/197) |
| Los 28 tests que estaban verdes antes del fix siguen verdes | CUMPLIDO |
| Happy path `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` en verde | CUMPLIDO |
| Test ADR-008 marcado como skip (patrón canónico del repo) | CUMPLIDO |
| Suite completa: 0 failing | CUMPLIDO |

---

## 7. Decisiones de "código más simple de lo que podría ser"

- `JsonStringEnumConverter` sin `JsonNamingPolicy` — el spec §0 y §5 justifican no añadir naming policy ahora para no romper los 28 tests verdes. Se mantiene case-sensitive: solo acepta PascalCase exacto (`"Manual"`, `"NoRequiereIntervencion"`).
- Sin cambios en ningún DTO — el converter global cubre todos los endpoints presentes y futuros.

---

## 8. Impulsos de refactor no implementados

- Los DTOs `GenerarOTRequest` y `RegistrarEvaluacionCualitativaRequest` usan `string + Enum.TryParse` en el cuerpo del endpoint. Ahora que hay `JsonStringEnumConverter` global, podrían refactorizarse para usar el tipo enum directo en el DTO. No se hace — no hay test rojo que lo pida (spec §1.2 lo excluye explícitamente del scope).
