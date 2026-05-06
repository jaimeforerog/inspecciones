# green-notes — slice 1d: ActualizarHallazgo

## Archivos modificados

- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`
  - Implementado `ActualizarHallazgo(ActualizarHallazgo cmd, DateTimeOffset ahora)` (método de decisión).
  - Implementado `Apply(HallazgoActualizado_v1 e)` (mutación pura de estado).

No se modificaron tests ni código de otros slices.

## Resultado de tests

- 54/55 en verde.
- 1 test rojo esperado: `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` (§6.7).

## Test §6.7 — estado rojo esperado y documentado

El stream usado en §6.7 (`StreamConHallazgoRegistrado`) no incluye un evento que marque el hallazgo como eliminado. La lógica de PRE-B2 está implementada correctamente: busca el hallazgo, verifica `Eliminado == true` y lanza `HallazgoEliminadoException`. El test permanece rojo porque el fixture `StreamConHallazgoEliminado()` lanza `NotImplementedException` (stub), y el test mismo usa `StreamConHallazgoRegistrado` (hallazgo activo). El test pasará cuando el slice `EliminarHallazgo` provea `HallazgoEliminado_v1` y el fixture sea completado. Ver comentario en `HallazgoFixtures.StreamConHallazgoEliminado()`.

## Campo `ObservacionCampo` en `Hallazgo`

`HallazgoActualizado_v1` tiene `ObservacionCampo` pero el record `Hallazgo` no lo expone. Ningún test del slice 1d verifica `ObservacionCampo` en el estado del agregado, por lo que no se añadió al record (regla: prohibido agregar código sin test). Candidato para refactorer si un slice posterior lo requiere.

## Impulsos de refactor no implementados

- La validación de PRE-E (campos de intervención no permitidos) y PRE-D (campos requeridos) sigue el mismo patrón que `RegistrarHallazgo`. Existe duplicación entre ambos métodos de decisión. Candidato para extracción a método privado o clase de validación por `refactorer`.
- El orden PRE-E antes de PRE-D (E verifica que no haya campos de intervención cuando no RequiereIntervencion; D verifica que estén cuando sí RequiereIntervencion) es asimétrico con el orden en `RegistrarHallazgo`. Decisión deliberada: la spec §4 de este slice lo define así. Documentar en refactor-notes si se unifica.

## Decisiones deliberadas de código simple

- `_hallazgos.Find(...)` devuelve `null` si no existe — suficiente para PRE-B1. No se usó LINQ `FirstOrDefault` con expresión más elaborada.
- `Apply(HallazgoActualizado_v1)` usa `FindIndex` + asignación directa por índice. No se usa `RemoveAt`/`Insert` ni list inmutable. Más simple y suficiente.
- El `with { ... }` en `Apply` no incluye `ObservacionCampo` porque `Hallazgo` no lo tiene como campo. Si se añade en el futuro, se actualizará automáticamente al compilar (el compilador lo forzará).
