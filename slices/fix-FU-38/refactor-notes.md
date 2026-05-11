# Refactor notes — fix-FU-38 — Results.Forbid() reemplazado por Forbidden403 helper

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | extract constant | `InspeccionesEndpoints.cs` | Extraída constante privada `MensajeCapabilityGenerarOT = "Capability 'generar-ot' requerida."` para eliminar la duplicación de ese literal idéntico en los endpoints `GenerarOT` (L790) y `RechazarGenerarOT` (L894). Los dos callsites de `Forbidden403` ahora referencian la constante. | 28 pass | 28 pass* |

\* Docker no disponible en entorno de refactor — los tests E2E (Testcontainers) no pueden correr. Build compila sin warnings (`0 Advertencias, 0 Errores`). El conteo 28/32 es el reportado por `green` y no cambia: el refactor no toca lógica, solo sustituye un literal por una constante.

---

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | análisis propio | **Ubicación del helper** — El helper `Forbidden403` está al final de la clase estática, después de todos los endpoints. Es la posición natural para métodos privados auxiliares en C#. No hay convención alternativa en el archivo que indique otro orden. Sin cambio. |
| 2 | análisis propio | **Naming `Forbidden403`** — Alternativas evaluadas: `Result403` (demasiado genérico, no comunica Forbidden), `JsonForbidden` (oculta el status code). `Forbidden403` nombra el semántico (Forbidden) y el código HTTP (403) — correcto tal cual. Sin cambio. |
| 3 | análisis propio | **`codigoError` como constantes** — Los códigos `"PRE-1"`, `"PRE-3-PROYECTO"`, `"PRE-F3"` aparecen en los callsites del `Forbidden403`. Cada código aparece 1-2 veces en el archivo. El caso `"PRE-1"` se repite en dos callsites de `Forbidden403` (GenerarOT y RechazarGenerarOT), pero ya está cubierto por la constante `MensajeCapabilityGenerarOT` extraída en el cambio #1 — el código no es el literal duplicado, el mensaje sí lo era. Los demás códigos (`"PRE-3-PROYECTO"`, `"PRE-F3"`) aparecen una sola vez. Extraer constantes para un solo uso es ruido, no DRY. Sin cambio. |
| 4 | instrucción de orquestador | **Extraer helpers `NotFound404`/`Conflict409`** — El spec y el orquestador indican explícitamente que refactorizar los `Results.NotFound` / `Results.Conflict` es ampliación de scope de este slice. Registrado en FOLLOWUPS.md si corresponde, no se aplica aquí. |
| 5 | green-notes §5 | **Mover helper a clase utilitaria compartida** — Solo hay un archivo de endpoints en este módulo. No hay segundo consumidor. Abstracción especulativa descartada. |
| 6 | green-notes §5 | **Unificar catch `CapabilityRequeridaException` + `TecnicoNoContribuyenteException`** — Son clases independientes sin jerarquía común. Unificarlos requeriría cambio de jerarquía de dominio, lo cual está fuera de scope de este fix de capa HTTP. |
| 7 | análisis propio | **Extension method** — Los callsites están dentro de la misma clase estática. Un extension method sobre `IResult` no aportaría nada que el método privado actual no tenga. Sobrecarga innecesaria. |

---

## Veredicto

**Aplicado** — un cambio mínimo (constante privada para mensaje duplicado). El código resultante es idéntico en comportamiento al que entregó `green`.
