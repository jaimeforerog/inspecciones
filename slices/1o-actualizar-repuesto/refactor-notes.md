# Refactor notes — Slice 1o — ActualizarRepuesto

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | extract file | `ActualizarRepuestoHandler.cs` → `ActualizarRepuestoResult.cs` | Mover `ActualizarRepuestoResult` a su propio archivo en `src/Inspecciones.Application/Inspecciones/`. El record vivía al final del handler. Alinea con la convención dominante del proyecto: 9 de 13 Result records tienen archivo propio (IniciarInspeccionResult, RegistrarMedicionResult, CancelarInspeccionResult, DescartarNovedadPreopResult, etc.). Los 4 co-locados (ActualizarHallazgo, AsignarRepuesto, FirmarInspeccion, RegistrarHallazgo) son deuda técnica anterior, no modelo a seguir. | Domain 246/265 (19 skip) · Api 57/63 (6 skip) | Domain 246/265 (19 skip) · Api 57/63 (6 skip) |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §4 — orden PRE en `ActualizarRepuesto` | El orden PRE-3/4 → PRE-8 → PRE-7 → PRE-5 es deliberado (spec §4 D-3: comando vacío da mejor feedback que repuesto no encontrado). Reordenar no reduce duplicación ni mejora claridad — solo cambia semántica observable. Prohibición de cambio de comportamiento. |
| 2 | green-notes §4 — método `Normalizar()` en comando | Sería abstracción especulativa sin segundo caso de uso. El patrón `cmd with { ... }` en el handler es correcto para la capa Application y no hay duplicación real. |
| 3 | Normalización duplicada entre endpoint y handler | El endpoint normaliza antes de construir el comando (`InspeccionesEndpoints.cs` línea 1149); el handler normaliza nuevamente al recibirlo (línea 27). Son capas distintas con razones independientes: el handler es defensa en profundidad del dominio (puede ser llamado sin pasar por el endpoint). No es DRY real entre capas — cada capa tiene su responsabilidad de normalización. |
| 4 | Consolidar variable `observacionNormalizada` + `cmdNormalizado` en una sola expresión inline | Las dos variables hacen el código más legible que un `cmd with { ObservacionNueva = string.IsNullOrWhiteSpace(...) ? null : cmd.ObservacionNueva }` inline. Sin ganancia neta de claridad. |

## Output de tests

### Domain (sin Postgres):
```
Correctas! - Con error: 0, Superado: 246, Omitido: 19, Total: 265
```

### Api (Testcontainers con Postgres):
```
Correctas! - Con error: 0, Superado: 57, Omitido: 6, Total: 63
```

## Veredicto

Verde confirmado. Un refactor de alineación de convención aplicado, cuatro candidatos descartados con justificación.
