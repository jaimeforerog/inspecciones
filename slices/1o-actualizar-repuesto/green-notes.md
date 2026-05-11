# green-notes — Slice 1o ActualizarRepuesto

**Agente:** green
**Fecha:** 2026-05-11
**Estado:** verde

---

## 1. Archivos modificados

### `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`
Reemplazado el stub `NotImplementedException` de `ActualizarRepuesto(cmd, ahora)` con implementación real.

Orden de validaciones (PRE-2 → PRE-3/PRE-4 → PRE-8 → PRE-7 → PRE-5):
- PRE-2: `Estado != EnEjecucion` → `InspeccionNoEnEjecucionException`
- PRE-3/PRE-4: `ObtenerHallazgoActivo(cmd.HallazgoId)` (reutiliza helper existente → lanza `HallazgoNoEncontradoException` o `HallazgoEliminadoException`)
- PRE-8: `CantidadNueva is null && ObservacionNueva is null` → `ComandoSinCambiosException`
- PRE-7: `CantidadNueva <= 0` → `CantidadInvalidaException`
- PRE-5: `!_repuestos.Any(r => r.RepuestoId == cmd.RepuestoId && r.HallazgoId == cmd.HallazgoId)` → `RepuestoNoEncontradoException`
- Emite `[RepuestoActualizado_v1(...)]` con delta.

### `src/Inspecciones.Application/Inspecciones/ActualizarRepuestoHandler.cs`
Reemplazado el stub `NotImplementedException` con implementación real:
- PRE-1: `AggregateStreamAsync<Inspeccion>` → `InspeccionNoEncontradaException` si null.
- P-2: `string.IsNullOrWhiteSpace(cmd.ObservacionNueva) ? null : cmd.ObservacionNueva` antes de delegar.
- Reconstruye `cmd` normalizado con `cmd with { ObservacionNueva = observacionNormalizada }`.
- Un único `SaveChangesAsync`.
- Estado post-update: aplica el delta del evento emitido sobre el repuesto pre-update del aggregate cargado, sin segundo round-trip a DB.

---

## 2. Resultado de `dotnet test`

### Domain (sin Postgres):
```
Correctas! - Con error: 0, Superado: 246, Omitido: 19, Total: 265
```
- ActualizarRepuesto: 20 PASS + 1 SKIP (PRE-1 vive en handler)
- Sin regresión en los 226 tests anteriores.

### Application + API (Testcontainers — Docker no disponible localmente):
```
Con error: 11 (todos por Docker not running, no por NotImplementedException)
```
Los tests de integration fallan por infraestructura (Docker no disponible), no por lógica. Se validan en CI según patrón documentado en project memory.

---

## 3. Decisiones deliberadas de código simple

- **Orden PRE-8 antes de PRE-7:** el spec sugiere PRE-8 para rechazar "comando vacío" antes de validar el valor de Cantidad. Implementado en ese orden.
- **PRE-5 después de PRE-8:** si el repuesto no existe pero el comando está vacío, devolvemos `ComandoSinCambiosException` primero (más informativo para el cliente). Consistente con D-3 del spec.
- **Estado post-update en handler:** en lugar de un segundo `FetchStreamAsync`, se aplica el delta en memoria sobre el aggregate ya cargado. Patrón análogo a `AsignarRepuestoHandler` que lee del evento emitido.

---

## 4. Impulsos de refactor no implementados (candidatos para `refactorer`)

- El orden de las PRE en `ActualizarRepuesto` (PRE-3/4 antes que PRE-8/7) podría unificarse con el patrón de `AsignarRepuesto` que valida estado, hallazgo, cantidad en ese orden. Sin embargo los tests pasan con el orden actual y cualquier reordenamiento es refactor. Documentado aquí.
- El patrón `cmd with { ... }` para normalizar el comando en el handler podría extraerse en un método `Normalizar()` del comando, pero sería gold-plating sin test que lo exija.

---

## 5. Desvíos del spec

Ninguno. La implementación sigue el spec al pie de la letra:
- Evento con semántica delta (D-1).
- P-2 normalización en handler (D-5).
- `RepuestoNoEncontradoException` para PRE-5 (D-2).
- `ComandoSinCambiosException` para PRE-8 (D-3).
- `CantidadInvalidaException` para PRE-7 (reutiliza excepción de slice 1f).
