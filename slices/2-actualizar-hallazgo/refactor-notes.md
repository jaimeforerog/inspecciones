# Refactor notes — Slice 2: ActualizarHallazgo

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | extract method | `Inspeccion.cs` | Extraído `ValidarRequiereIntervencion(accionRequerida, tipoFallaId, causaFallaId, accionCorrectiva)` como método privado estático. Elimina duplicación real entre `RegistrarHallazgo` y `ActualizarHallazgo` (mismas dos guardas PRE-5/PRE-8 y PRE-6). | 53 pass | 53 pass |
| 2 | normalize | `Inspeccion.cs` | `ActualizarHallazgo` pasó de `FirstOrDefault` a `FindIndex` para buscar el hallazgo. Consistencia con los métodos `Apply(HallazgoActualizado_v1)` y `Apply(HallazgoEliminado_v1)`. Sin cambio de comportamiento. | 53 pass | 53 pass |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 | Extracción de `ValidarNovedadTecnica` — solo 3 líneas, un único punto de entrada cada uno, no es DRY real hoy. Candidato cuando haya un tercer método de decisión que la use. Anotado en FOLLOWUPS si corresponde. |

## Verificación final

- `dotnet build`: 0 warnings, 0 errores.
- `dotnet test tests/Inspecciones.Domain.Tests/`: Correctas: 53, Con error: 0.
