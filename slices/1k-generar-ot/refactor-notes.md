# Refactor notes — Slice 1k — GenerarOT

## Cero cambios

Cero cambios. Motivo: el código de green quedó ya dentro de los criterios de calidad.

---

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §4 | `VerificarEstadoFirmada()` — solo hay un consumidor (`SolicitarOT`). Un único uso no es DRY real; extraer un helper de una sola llamada añade indirección sin reducir duplicación. Abstracción especulativa. |
| 2 | green-notes §4 | Propiedad computada `HallazgosActivosConIntervencion` — solo la usa `SolicitarOT`. No hay un segundo consumidor en ningún método del aggregate. Igual que el caso anterior: abstracción especulativa. |
| 3 | revisión propia | Comentarios inline en los parámetros de `GenerarOT.cs` y `OTSolicitada_v1.cs` (`// userId del aprobador del host PWA — opaco para el dominio`, etc.) — CLAUDE.md "Default to writing no comments" pero hace excepción cuando el WHY es no-obvio. Estos comentarios explican opacidad del userId y propósito de campos para la saga `EjecutarOTSaga`. Son justificados; eliminarlos oscurecería la intención de diseño. |
| 4 | revisión propia | `Apply(OTSolicitada_v1)` no llama `_contribuyentes.Add(e.SolicitadaPor)` — podría parecer inconsistente con otros `Apply`. Pero la propiedad `Contribuyentes` está documentada como "técnicos que han contribuido eventos al stream" (línea 79 de `Inspeccion.cs`). El aprobador (`SolicitadaPor`) no es un técnico de campo; su rol es diferente y fuera del invariante I2b. El comportamiento actual es correcto, no un olvido. |

## Seguimientos a FOLLOWUPS.md

| # | Observación |
|---|---|
| 1 | En el switch `AplicarEvento`, los cases `MedicionRegistrada_v1` e `ItemMonitoreoOmitido_v1` comparten el comentario `// Slice 1i — RegistrarMedicion` aunque `ItemMonitoreoOmitido_v1` pertenece al slice 1j. Deuda técnica de los slices anteriores — anotar en FOLLOWUPS para corrección transversal en el próximo ciclo de limpieza. |

---

## Resultado del test runner final

```
Correctas! - Con error: 0, Superado: 179, Omitido: 9, Total: 188, Duración: ~71 ms
```

Baseline entrante: 179 pass / 9 omit / 0 errores.
Baseline saliente: 179 pass / 9 omit / 0 errores — sin regresiones, sin cambios.
