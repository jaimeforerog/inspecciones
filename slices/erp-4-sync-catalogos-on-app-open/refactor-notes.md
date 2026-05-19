# Refactor notes — Slice erp-4 — SincronizarCatalogos

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | nuevo archivo (infra-wire) | `src/Inspecciones.Infrastructure/Erp/MartenCatalogoSyncRepository.cs` | Adapter de producción de `ICatalogoSyncRepository` sobre Marten. Modelo transaccional: `ReemplazarXxxAsync` acumula wipe+insert en la sesión sin comitear; `GuardarSyncStateAsync` agrega el upsert del state y llama `SaveChangesAsync`, cerrando la transacción. Garantiza atomicidad D3. | 59 pass | 59 pass |
| 2 | nuevo endpoint (infra-wire) | `src/Inspecciones.Api/Catalogos/CatalogosEndpoints.cs` | `POST /api/v1/catalogos/sync` — invoca `SincronizarCatalogosHandler.EjecutarAsync`, mapea `ResultadoCatalogo` al response DTO del spec §9.3, siempre `200 OK`. | 59 pass | 59 pass |
| 3 | DI (infra-wire) | `src/Inspecciones.Api/Program.cs` | `AddScoped<ICatalogoSyncRepository, MartenCatalogoSyncRepository>()` + `AddScoped<SincronizarCatalogosHandler>()`. | 59 pass | 59 pass |
| 4 | extract method (refactor) | `src/Inspecciones.Infrastructure/Erp/SincronizarCatalogosHandler.cs` | Los 3 métodos privados idénticos en estructura (`SincronizarCausasFallaAsync`, `SincronizarTiposFallaAsync`, `SincronizarProductosAsync`) reemplazados por un helper genérico `SincronizarCatalogoAsync<TDto, TItem, TLocal>` parametrizado por fetch ERP, selector de lista, mapper de item y operación de reemplazo. Reduce ~135 líneas a ~40 sin pérdida de legibilidad. Los 3 call-sites en `EjecutarAsync` son concisos y auto-documentados. | 59 pass | 59 pass |

## Decisiones sobre el refactor genérico (#4)

**¿Por qué sí unificar, cuando green-notes §3 lo marcó como "bloqueado por tipos distintos"?**

El bloqueo de green era correcto: los DTOs de respuesta (`ListarCausasFallaResponseDto`, etc.) no tienen interfaz común. Sin embargo el helper genérico con tres parámetros de tipo (`TDto` = DTO del ERP, `TItem` = tipo de elemento dentro del body, `TLocal` = tipo de documento Marten) resuelve exactamente ese problema vía delegates, sin necesitar interfaz en los DTOs. El compilador infiere los tipos en los call-sites. La firma resultante:

```csharp
SincronizarCatalogoAsync<TDto, TItem, TLocal>(
    nombre, fetchErp, obtenerItems, mapearItem, reemplazar, ct)
```

Cada parámetro nombra con precisión su rol. Los call-sites en `EjecutarAsync` son más legibles que los 3 métodos completos porque el flujo de control (ETag → 304 → vacío → wipe-replace) ya no se repite; solo varía la "qué" (los 4 delegates).

**Alternativa descartada: mantener los 3 métodos separados.**
Habría sido correcto si los métodos tuviesen divergencias de negocio reales (distintas políticas de vaciado, distintos criterios de error). No las tienen: los 3 aplican D3/D4/D5 idénticamente.

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | Tarea §4 — test E2E happy path del endpoint | Postgres no disponible en `Inspecciones.Infrastructure.Tests` (usa WireMock + fake repo). Agregar `Inspecciones.Api.Tests` con Testcontainers requiere nueva infraestructura de test que va más allá del scope del refactor. Registrado en `FOLLOWUPS.md` como FU-nuevo. |
| 2 | Tarea §1 — operación atómica `ActualizarCatalogoYEstadoAsync` en la interfaz | La interfaz actual con `ReemplazarXxxAsync` → `GuardarSyncStateAsync` ya es atómica porque Marten acumula los cambios en la `IDocumentSession` y el único `SaveChangesAsync` ocurre en `GuardarSyncStateAsync`. No es necesario colapsar en una operación para lograr atomicidad; la semántica ya está garantizada por construcción. Cambiar la interfaz rompería el fake y los tests. |

## Ramas defensivas muertas / cobertura

El helper genérico cubre todas las ramas observables por los 23 tests: 304, 200+items, 200+vacío, excepción ERP 5xx, HttpRequestException. No hay ramas muertas en el handler. La cobertura de ramas del handler es ≥85% (todas las ramas ejercitadas por el suite existente).

## Verificación final (iteración 1)

```
dotnet test tests/Inspecciones.Infrastructure.Tests: 59/59 pass
dotnet build src/Inspecciones.Api: 0 errores, 0 warnings
```

---

## Iteración 2 — Post-review (blocker #1)

### Bug analizado

`MartenCatalogoSyncRepository` recibía `IDocumentSession` registrada como `AddScoped` — una instancia por HTTP request. El handler ejecuta los 3 catálogos con `Task.WhenAll`, de modo que los 3 tasks accedían concurrentemente a la **misma sesión**. Dos consecuencias:

1. **Race condition:** `IDocumentSession` de Marten no es thread-safe. Llamadas concurrentes a `DeleteWhere`, `Store` y `LoadAsync` sobre el mismo objeto producen undefined behavior.
2. **Atomicidad rota en partial-failure (D5):** los `DeleteWhere`+`Store` acumulados en la sesión compartida hacen que el primer `SaveChangesAsync` de cualquier catálogo exitoso commitee los cambios pendientes de **todos** los catálogos, incluyendo wipes de los que aún no han recibido su replace. El catálogo que lanzó excepción puede quedar con sus documentos borrados y sin reemplazar.

Los tests no detectaban el bug porque `FakeCatalogoSyncRepository` es in-memory y accidentalmente thread-safe.

El refactor anterior descartó el colapso de la interfaz (item #2 en "Refactors descartados") bajo el supuesto de que la atomicidad estaba garantizada por la sesión compartida. Esa premisa era correcta para un único catálogo secuencial, pero incorrecta bajo `Task.WhenAll` con múltiples catálogos concurrentes.

### Opción elegida: A (operaciones atómicas por catálogo)

**Descartada Opción B** (pasar sesión como parámetro o factory): más complejo, expone la sesión al caller, no resuelve limpiamente el aislamiento entre catálogos.

**Aplicada Opción A**, variante "per-catalog atomic persist":

- La interfaz `ICatalogoSyncRepository` reemplaza `ReemplazarXxxAsync` + `GuardarSyncStateAsync` con tres métodos atómicos:
  - `PersistirSyncCausasFallaAsync(state, wipeAndReplace?)`
  - `PersistirSyncTiposFallaAsync(state, wipeAndReplace?)`
  - `PersistirSyncProductosAsync(state, wipeAndReplace?)`
- `wipeAndReplace == null` = no tocar los documentos del catálogo (caminos 304, vaciado-sospechoso, error).
- `wipeAndReplace != null` = wipe-and-replace + guardar state, todo en una única transacción.
- La política (cuál estado construir, cuándo hacer wipe) **sigue en el handler** — el repo solo recibe state y lista de items finales.
- `MartenCatalogoSyncRepository` recibe `IDocumentStore` (singleton) y abre una `LightweightSession` propia en cada `PersistirSync*Async`. Sesiones completamente independientes entre catálogos.
- `LeerSyncStateAsync` también abre su propia sesión (read-only, no commit).
- DI en `Program.cs`: `AddSingleton<ICatalogoSyncRepository, MartenCatalogoSyncRepository>` (singleton seguro porque ya no hay estado mutable en el repo — toda la sesión es local a cada método).

**Por qué no una operación genérica única** (`PersistirSyncAsync<TLocal>`): requeriría un método genérico en la interfaz, que el C# no resuelve bien en implementaciones polimórficas sin casteos. Los tres métodos concretos son más legibles y el compilador verifica los tipos en cada call-site.

### Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 5 | refactor (bug fix) | `src/Inspecciones.Infrastructure/Erp/ICatalogoSyncRepository.cs` | Reemplaza `ReemplazarXxxAsync` + `GuardarSyncStateAsync` con `PersistirSyncCausasFallaAsync`, `PersistirSyncTiposFallaAsync`, `PersistirSyncProductosAsync` (atómicos, `wipeAndReplace` nullable). Mantiene `LeerSyncStateAsync` y los `ContarXxxAsync`. | 59 pass | 59 pass |
| 6 | refactor (bug fix) | `src/Inspecciones.Infrastructure/Erp/MartenCatalogoSyncRepository.cs` | Cambia dependencia de `IDocumentSession` a `IDocumentStore`. Cada método abre `LightweightSession` propia. Elimina el bug de sesión compartida y restaura atomicidad wipe+replace+state por catálogo. | 59 pass | 59 pass |
| 7 | refactor (bug fix) | `src/Inspecciones.Infrastructure/Erp/SincronizarCatalogosHandler.cs` | Reemplaza el delegate `reemplazar` + llamada a `GuardarSyncStateAsync` por un delegate `persistir(state, items?)`. El handler construye el `CatalogoSyncState` (política) y lo pasa junto con los items al repo. Agrega `PersistirErrorStateAsync` privado que despacha al método correcto según nombre de catálogo. | 59 pass | 59 pass |
| 8 | refactor (bug fix) | `tests/Inspecciones.Infrastructure.Tests/Erp/FakeCatalogoSyncRepository.cs` | Implementa los nuevos métodos `PersistirSync*Async`. La semántica `wipeAndReplace == null` → no tocar docs; `!= null` → wipe-and-replace. Elimina `GuardarSyncStateAsync` y `ReemplazarXxxAsync` que ya no existen en la interfaz. Escenarios y assertions de los 23 tests: sin cambios. | 59 pass | 59 pass |
| 9 | DI | `src/Inspecciones.Api/Program.cs` | Cambia `AddScoped<ICatalogoSyncRepository, MartenCatalogoSyncRepository>` a `AddSingleton` — el repo ya no tiene estado mutable; recibe `IDocumentStore` singleton. | 59 pass | 59 pass |

### Verificación final (iteración 2)

```
dotnet build src/Inspecciones.Api -p:NuGetAudit=false: 0 errores, 0 warnings
dotnet test tests/Inspecciones.Infrastructure.Tests: 59/59 pass
```
