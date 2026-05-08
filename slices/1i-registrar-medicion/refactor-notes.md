# Refactor notes — Slice 1i — RegistrarMedicion

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | cleanup | `RegistrarMedicionHandler.cs` | Eliminado comentario residual de fase red en docstring ("Stub mínimo fase red — lanza NotImplementedException") y en comentario inline de `Handle` ("stub — NotImplementedException hasta que el green implemente"). El handler ya está implementado; el comentario era incorrecto. | 124 pass | 124 pass |
| 2 | cleanup | `MedicionRegistrada_v1.cs` | Eliminado "Slice 1i — stub mínimo fase red" del docstring del record. El evento es código de producción; la etiqueta de fase es ruido. | 124 pass | 124 pass |
| 3 | consistency | `InspeccionesEndpoints.cs` | Cambiado `StringValues clientCommandIdValues` a `var` en el endpoint de RegistrarMedicion para alinear con el patrón de los 6 endpoints anteriores (todos usan `var`). Eliminado el `using Microsoft.Extensions.Primitives` que quedó huérfano. | 124 pass | 124 pass |
| 4 | doc | `HallazgoRegistrado_v1.cs` | Movido comentario inline del campo `MedicionOrigenId` al docstring del record como `<c>MedicionOrigenId</c>:` prose. El comentario `// NUEVO slice 1i — int? PK ERP del ítem. Obligatorio...` estaba adherido a la declaración del parámetro del record, que no acepta XML-doc por parámetro individual. El docstring del tipo es el lugar correcto. | 124 pass | 124 pass |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 (duplicación `X-Client-Command-Id`) | El guard se repite en 7 endpoints (incluye el del 1i), pero la duplicación no la introduce el slice 1i — viene del slice 1b en adelante. Tocarlo aquí requeriría refactorizar los 6 endpoints existentes, que están fuera del scope del slice actual. Registrado como followup #27. |
| 2 | green-notes §3 (cast `(MedicionRegistrada_v1)eventos[0]`) | El cast directo por posición en `RegistrarMedicionHandler` es frágil pero el contrato del aggregate lo garantiza (el primer evento siempre es `MedicionRegistrada_v1`). Hacerlo seguro requeriría cambiar el tipo de retorno de `RegistrarMedicion` (tupla o tipo discriminado), lo que afectaría los tests del aggregate y potencialmente otros slices. No hay un segundo handler que repita el patrón; no es DRY real. Registrado como followup #28. |
| 3 | instrucción de misión (CapitalizarPrimera) | `CapitalizarPrimera` se usa exclusivamente en `RegistrarMedicion`. Moverlo a un helper estático compartido sería abstracción especulativa — no hay un segundo sitio que lo use. Se mantiene privado en el aggregate. |
| 4 | instrucción de misión (formato NovedadTecnica) | El formato `"{Magnitud} {ValorMedido}{Unidad} fuera de rango..."` está hardcoded en `RegistrarMedicion`. No existe todavía ningún otro comando de monitoreo con el mismo formato (el slice de `RegistrarEvaluacionCualitativa` es posterior). Extraer a constante sin un segundo caso sería especulativo. |
| 5 | instrucción de misión (DRY con RegistrarHallazgo/IniciarInspeccion) | La duplicación de guard `EnEjecucion`, lookup de snapshot e invariantes PRE en los métodos de decisión no es nueva en el 1i — el aggregate viene repitiendo ese patrón desde el slice 1c. No hay un helper factorizable sin tocar slices anteriores. Followups #23/#24 ya cubren la deuda transversal de handlers. |

## Resultado de `dotnet test` post-refactor

```
Correctas! - Con error: 0, Superado: 124, Omitido: 1, Total: 125
```

`dotnet build` — 0 advertencias, 0 errores.

## Cobertura del aggregate post-refactor

No se ejecutó `coverlet` (sin configuración en el repo). Por inspección manual:

- `RegistrarMedicion`: todas las ramas cubiertas (PRE-3..PRE-8, dentro/fuera de rango, bordes inclusivos, I-H1 guard, múltiples ítems).
- `Apply(MedicionRegistrada_v1)`: cubierto por tests §6.1, §6.15.
- `Apply(HallazgoRegistrado_v1)` con `MedicionOrigenId`: cubierto por §6.2, §6.15.
- `CapitalizarPrimera`: cubierto por todos los tests §6.2, §6.3, §6.13 (genera `NovedadTecnica`).

Estimación global del aggregate > 85 % (umbral CLAUDE.md — sin cambio respecto al estado green).
