# Refactor notes — Slice 1j — OmitirItemMonitoreo

**Autor:** refactorer
**Fecha:** 2026-05-08

---

## Cambios aplicados en `src/`

Ninguno.

## Cero cambios

El código del green quedó ya dentro de los criterios de calidad. Motivo detallado en la sección de refactors descartados.

---

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 — `ValidarContextoMonitoreo(itemId)` | Los mensajes de las tres excepciones PRE-5/PRE-6 varían por nombre de comando ("OmitirItemMonitoreo", "RegistrarMedicion", "RegistrarEvaluacionCualitativa") y por el sustantivo de la acción ("omitir ítems", "mediciones", "evaluaciones"). Un helper unificado requeriría pasar el nombre del comando como parámetro, degradando la legibilidad sin reducir líneas. El feedback `feedback_pre_a_helper.md` del proyecto ya documenta este mismo rechazo para el patrón `VerificarEnEjecucion()`. PRE-7/snapshot tiene además inconsistencia intencional entre métodos (1i incluye lista de IDs; 1i' no la incluye — los tests asertanan el mensaje exacto). Extraer el helper cambiaría o uniformizaría ese comportamiento. |
| 2 | green-notes §3 — `EstaItemProcesado(itemId)` | PRE-8 usa dos `if` separados con mensajes que distinguen "medición" vs "evaluación" — diferenciación semántica intencional según spec §6.4 y §6.5. Un helper booleano colapsaría la distinción o requeriría un enum de resultado, añadiendo complejidad sin reducir el número de casos del método. La spec §6.4/§6.5 exige mensajes distintos por razón de procesamiento. |
| 3 | Evaluación propia — `snapshotIds` lazy antes del `FirstOrDefault` | La variable `snapshotIds` es `IEnumerable<string>` (lazy — LINQ sin `.ToList()`). Solo se materializa dentro del `string.Join` al construir el mensaje del throw. No hay overhead en el happy path. El orden (declarar antes del `FirstOrDefault`) sigue el patrón de `RegistrarMedicion` (línea 851-852) por coherencia visual. Sin cambio. |
| 4 | Evaluación propia — comentario "Emisión: exactamente un evento. La omisión nunca genera hallazgo." | Comenta el POR QUÉ (constraintón de dominio §12.11.5 punto 6 — la tabla de trigger de hallazgo no incluye fila para omisión). Es un comentario legítimo de invariante de dominio, no descripción del qué. Se mantiene. |

---

## Followup actualizado (sin cambio de código)

`FOLLOWUPS.md #27` — contador de instancias del guard `X-Client-Command-Id` actualizado de 7 a 9 (el slice 1j añadió el endpoint `POST .../omitir`). Solo cambio documental en `FOLLOWUPS.md`.

---

## Output del `dotnet test` final

```
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --no-build

Correctas! - Con error: 0, Superado: 167, Omitido: 6, Total: 173, Duración: 79 ms
```

```
dotnet build (solución completa)

Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

- 167 tests verdes. 0 regresiones.
- Build limpia sin warnings.
