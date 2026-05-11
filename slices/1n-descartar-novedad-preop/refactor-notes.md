# Refactor notes — Slice 1n — DescartarNovedadPreop

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | remove stale doc | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Eliminar bloque `<remarks>` residual del código de `red` en `Descartar(...)`. El texto decía "Stub — implementación pendiente (fase green). La firma es definitiva; el cuerpo lanza `NotImplementedException`" — completamente falso tras green. | Domain 226/244 · Api 46/51 | Domain 226/244 · Api 46/51 |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 — `_novedadesConvertidas: HashSet<int>` | Optimización especulativa. El `_hallazgos.Any(...)` de PRE-6 es O(n) sobre una lista pequeña (número de hallazgos por inspección en MVP). Ningún test exige un `HashSet<int>` separado. Cambio solo válido cuando haya evidencia de que la colección escala. |
| 2 | green-notes §3 — método estático para plantilla D-4 | La plantilla del motivo autogenerado es única a este slice; no hay segundo caller. Extraer a un helper estático sería DRY imaginario. |
| 3 | Retorno de `Descartar` — cambiar `IReadOnlyList<object>` a tipo más específico | `IReadOnlyList<object>` es el patrón uniforme de los 11 métodos de decisión del aggregate (verificado con grep). Cambiarlo solo en este método rompería la coherencia del aggregate. |

## Output `dotnet test` post-refactor

### Domain

```
Correctas! - Con error: 0, Superado: 226, Omitido: 18, Total: 244, Duración: 777 ms
```

### Api (E2E con Postgres)

```
Correctas! - Con error: 0, Superado: 46, Omitido: 5, Total: 51, Duración: 19 s
```

Los 5 omitidos son skips ADR-008 (Wolverine dedup) — idénticos al estado green, sin regresión.

### Build

```
Compilación correcta. 0 Advertencia(s), 0 Errores.
```

`TreatWarningsAsErrors=true` — ningún warning suprimido.

## Veredicto

Un cambio aplicado (eliminar `<remarks>` falso residual del `red`). El resto del código del slice quedó dentro de los criterios de calidad tras el green.
