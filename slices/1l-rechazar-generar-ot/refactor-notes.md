# Refactor notes — Slice 1l — RechazarGenerarOT

**Agente:** refactorer
**Fecha:** 2026-05-08
**Estado:** COMPLETO

---

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | fix naming | `Inspeccion.cs` | Mensajes de excepción en `RechazarOT`: `cmd.InspeccionId` → `InspeccionId` (propiedad del aggregate). Consistencia con `SolicitarOT` que ya usaba `InspeccionId`. En runtime son idénticos (el handler carga el aggregate por ese id), pero el aggregate debe referirse a su propio estado, no a los parámetros del comando. | 197 pass | 197 pass |
| 2 | fix comment | `Inspeccion.cs` | Corrección FU-30 en `AplicarEvento`: `case ItemMonitoreoOmitido_v1` estaba bajo el comentario `// Slice 1i — RegistrarMedicion`. Corregido a `// Slice 1j — OmitirItemMonitoreo`. También actualizado el bloque 1k para mencionar 1l: `// Slice 1k — GenerarOT / Slice 1l — RechazarGenerarOT` (ambos slices comparten los Apply de `GeneracionOTRechazada_v1` e `InspeccionCerradaSinOT_v1`). | 197 pass | 197 pass |
| 3 | extract method | `Inspeccion.cs` | Extraído helper privado `TieneHallazgosConIntervencionActivos()`. La expresión `_hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == AccionRequerida.RequiereIntervencion)` aparecía literalmente duplicada: en `SolicitarOT` (PRE-4 / I-F4.b) y en `RechazarOT` (PRE-5 / I-F6.b). DRY real — dos usos del mismo predicado en la misma clase. El helper se documenta indicando los dos callers. | 197 pass | 197 pass |

---

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §Impulsos | Extraer `ValidarPrecondicionesOT()` como método completo que valide todas las precondiciones compartidas entre `SolicitarOT` y `RechazarOT`. Descartado: las precondiciones son similares en forma pero difieren en orden, en qué excepciones lanzan y en mensajes de error. La única línea verdaderamente duplicada era el predicado LINQ sobre hallazgos (cubierta por refactor #3). Un método que agrupe PRE-3/PRE-4/PRE-5 comunes implicaría parámetros de configuración o rama condicional, que produce más complejidad que la duplicación que elimina. |

---

## Output final de `dotnet test`

```
Correctas! - Con error: 0, Superado: 197, Omitido: 12, Total: 209, Duración: 73 ms
```

Desglose por slice:
- Slice 1l (RechazarGenerarOTTests): 18 pass / 3 skip / 0 error
- Slice 1k (GenerarOTTests): 12 pass / 3 skip / 0 error
- Resto de Domain.Tests: sin regresión

### `dotnet build`

```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

---

## Archivos modificados

| Archivo | Cambios |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Refactors #1, #2 y #3 |

---

## Followups nuevos descubiertos

Ninguno. El FU-30 (comentario `ItemMonitoreoOmitido_v1` bajo slice incorrecto en el switch) fue cerrado por el refactor #2 de este slice.
