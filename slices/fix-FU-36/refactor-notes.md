# Refactor notes — fix-FU-36 — JsonStringEnumConverter en Minimal APIs

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | remove comment | `src/Inspecciones.Api/Program.cs` | Eliminada la línea `// FU-36: cierra el comentario que anticipaba esta configuración.` del banner del bloque `ConfigureHttpJsonOptions`. La línea era historia de desarrollo (referencia al comentario ya eliminado en green), no documentación del comportamiento activo. El banner de primera línea (`// JSON serializer — Minimal APIs: enums como string en request y response bodies.`) fue preservado. | 29 pass, 3 skip | 29 pass, 3 skip |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | evaluación propia | **Extracción a método `ConfigureSerialization()`:** el bloque tiene 3 líneas de body. No hay ROI real — fragmentar la lectura lineal de `Program.cs` para un bloque tan pequeño introduce una indirección sin beneficio. El patrón del archivo es un top-level script lineal; los banners de guiones son suficiente estructura visual. |
| 2 | evaluación propia | **Agrupación con otros bloques DI cercanos:** el bloque ya sigue exactamente el mismo patrón (banner + llamada a `builder.Services.*`) que todos los demás bloques de `Program.cs`. No hay desalineación que corregir. |
| 3 | green-notes §8 | **Refactor de `GenerarOTRequest` y `RegistrarEvaluacionCualitativaRequest` a enum directo:** ahora que `JsonStringEnumConverter` está registrado globalmente, estos DTOs podrían reemplazar su `string + Enum.TryParse` por el tipo enum directo. No hay test rojo que lo pida hoy (spec §1.2 lo excluye explícitamente). Scope fuera del slice actual. Registrado en `FOLLOWUPS.md` si no está ya. |

## Output dotnet test post-refactor

```
Correctas! - Con error: 0, Superado: 29, Omitido: 3, Total: 32, Duración: 8 s - Inspecciones.Api.Tests.dll (net9.0)
```

`dotnet build` — 0 Advertencias, 0 Errores.

## Veredicto

**Aplicado** (1 cambio mínimo: eliminación de 1 línea de comentario histórico). El código de green era esencialmente minimal; el único ruido era la línea de referencia interna al comentario pre-existente que ya no existe.
