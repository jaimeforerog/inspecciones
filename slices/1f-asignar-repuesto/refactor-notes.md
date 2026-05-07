# Refactor notes — Slice 1f — AsignarRepuesto

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | fix-doc | `Inspecciones.Domain/Inspecciones/Inspeccion.cs` | XML doc de `ObtenerHallazgoActivo` decía "Usado por `ActualizarHallazgo` y `EliminarHallazgo`"; se agregó `AsignarRepuesto` que también lo usa desde este slice | 74 pass | 74 pass |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 (PRE-A repetido 4 veces) | Cada mensaje de excepción lleva un verbo diferente ("registrar", "actualizar", "eliminar", "asignar repuestos"). Extraer `VerificarEnEjecucion()` eliminaría la duplicación de la guarda pero exigiría parametrizar el verbo, haciendo el helper igual de verboso que la línea in-place. No hay DRY real — la lógica es idéntica pero el mensaje es parte del contrato de error. Se espera a tener un segundo caso genuino de mensaje uniforme. |
| 2 | green-notes §3 (consolidar mensaje PRE-A) | Consecuencia directa del punto anterior. Sin helper no hay lugar donde consolidar. Si en un slice futuro el squad acuerda un mensaje genérico ("La inspección no está en estado EnEjecucion"), el helper aparece solo. Anotar en FOLLOWUPS.md si el cuarto método llega. |

## Código revisado sin cambios

- **`AsignarRepuesto(...)` en `Inspeccion.cs`:** sigue el mismo patrón que `ActualizarHallazgo` y `EliminarHallazgo` — guard clauses en orden spec, `ObtenerHallazgoActivo` para PRE-B1/B2, idempotencia primero (PRE-D antes de PRE-A, decisión deliberada de green documentada en green-notes §2).
- **`Apply(RepuestoEstimado_v1 e)`:** patrón idéntico a `Apply(HallazgoRegistrado_v1 e)` — `_repuestos.Add(...)` seguido de `_contribuyentes.Add(e.AsignadoPor)`. Correcto.
- **Posición de `_repuestos`/`Repuestos`:** líneas 33–34, inmediatamente después de `_hallazgos`/`Hallazgos` (29–30). Orden de miembros correcto.
- **`AplicarEvento` switch:** caso `RepuestoEstimado_v1` presente y en posición cronológica correcta.
- **`return Array.Empty<object>()`:** usado para el path de idempotencia. Más explícito que `new List<object>()` y sin allocación innecesaria.
- **`Excepciones.cs`:** tres nuevas excepciones (`HallazgoNoRequiereIntervencionException`, `CantidadInvalidaException`, `SkuDuplicadoEnHallazgoException`) siguen la convención del archivo — XML doc con referencia al código PRE, `sealed record`-like primary-constructor, herencia de `InspeccionDomainException`. Sin deuda.
- **`Repuesto.cs` / `RepuestoEstimado_v1.cs` / `AsignarRepuesto.cs`:** artefactos creados en red, sin oportunidad de refactor — ya estaban dentro de los criterios de calidad.
- **Build:** 0 warnings, 0 errors (`-warnaserror`).
