# Refactor notes — Slice 1m — CancelarInspeccion

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | simplify | `src/Inspecciones.Application/Inspecciones/CancelarInspeccionHandler.cs` | Extraer `motivoTrimmed = cmd.Motivo.Trim()` para evitar triple evaluación de la misma expresión en el bloque PRE-4 (líneas 39/42/44). Elimina repetición concreta sin cambiar ningún comportamiento. | 213 pass / 15 skip | 213 pass / 15 skip |
| 2 | remove stale comment | `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | El comentario de cabecera del endpoint `/cancelar` decía "El handler es stub — lanza NotImplementedException hasta que green lo implemente." El handler está implementado desde el paso green. Se reescribió el comentario para describir el estado actual: PRE-1 y semántica 200 OK. | 213 pass / 15 skip | 213 pass / 15 skip |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §4.1 | Mensaje PRE-5 "La inspección {Id} está en estado '{Estado}'. {Comando} solo aplica..." — los mensajes difieren en el nombre del comando (`CancelarInspeccion`, `RegistrarHallazgo`, etc.). No es duplicación real (mismo patrón, distinto contenido). Extraer helper requeriría parametrizar el nombre del comando, lo que no reduce complejidad. Seguimiento: si un cuarto método adopta el mismo patrón, evaluar. |
| 2 | green-notes §4.2 | Validación de motivo duplicada entre `CasoDeUso.Cancelar` y `CancelarInspeccionHandler.Handle`: `CasoDeUso` es código de tests — prohibido tocar. En producción (`src/`) la lógica aparece solo una vez (en el handler), por lo que no hay duplicación real en código de producción. |
| 3 | green-notes §4.3 | Patrón de guardias PRE-2/PRE-3/PRE-4 repetido — la nota misma condiciona a "≥4 handlers con ese patrón". Solo hay 2 handlers con PRE-3 (`FirmarInspeccion` y `CancelarInspeccion`). Abstracción especulativa por ahora. |

## Verificación final

- `dotnet test Inspecciones.Domain.Tests`: 213 pass / 15 skip / 0 error — idéntico pre/post.
- `dotnet build Inspecciones.sln`: 0 warnings, 0 errores.
- `Inspecciones.Api.Tests`: requiere Docker (Testcontainers). No ejecutado localmente — misma condición que en el paso green. Los 4 skip ADR-008 son esperados.
