# Refactor notes — Slice 1d — ActualizarHallazgo

## Cambios aplicados

Cero cambios. Motivo: el código de green quedó ya dentro de los criterios de calidad.

- `dotnet build` → 0 advertencias, 0 errores.
- `dotnet test` (dominio) → 54 pass, 1 skip (deferred a `EliminarHallazgo`).

## Refactors descartados

| # | Sugerido por | Descripción | Motivo para no aplicar |
|---|---|---|---|
| 1 | green-notes §impulsos / candidato 1 | Extraer método privado `ValidarCamposAccionRequerida` para unificar PRE-D1 + PRE-D2 compartidas entre `RegistrarHallazgo` y `ActualizarHallazgo`. | Duplicación parcial, no completa. PRE-E (campos de intervención no permitidos cuando `AccionRequerida != RequiereIntervencion`) solo existe en `ActualizarHallazgo`. El orden de validaciones difiere entre los dos métodos de forma intencional y documentada en la spec §4 de cada slice. Con solo dos callers y orden deliberadamente distinto, la extracción acoplaría dos flujos de reglas diferentes sin reducción de complejidad real. Se espera un tercer uso antes de factorizar. |
| 2 | green-notes §ObservacionCampo / candidato 2 | Añadir `ObservacionCampo` al record `Hallazgo` para alinear con `HallazgoActualizado_v1`. | Ningún test del slice 1d verifica `ObservacionCampo` en el estado del agregado. Añadir código sin test violaría la regla de `green`. El campo se materializará cuando el slice que implemente `DetalleInspeccionView` (o la proyección de hallazgo) lo requiera. Ver FOLLOWUPS.md. |
| 3 | green-notes §candidato 3 | Eliminar comentario `// rebuild con gaps — puro, no lanza.` en `Apply(HallazgoActualizado_v1)`. | El comentario es correcto y útil: documenta la invariante de que `Apply` no lanza incluso si el hallazgo no existe en el state local (gap en rebuild). Se conserva. |
