# Green notes — Slice 1k — GenerarOT

**Autor:** green
**Fecha:** 2026-05-08

---

## 1. Archivos modificados en `src/`

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Implementación del método stub `SolicitarOT(GenerarOT cmd, DateTimeOffset ahora)` — reemplaza el `throw new NotImplementedException` con la lógica de precondiciones e emisión del evento. |

Sin cambios en otros archivos. Las excepciones, enums, eventos y stubs de `Apply` ya estaban correctos desde la fase red.

---

## 2. Resultado del test runner

### Tests del slice 1k (filtro `FullyQualifiedName~GenerarOT`):

```
Correctas! - Con error: 0, Superado: 12, Omitido: 3, Total: 15, Duración: ~163 ms
```

- 12 tests activos: todos verdes.
- 3 tests Skip: permanecen skipeados (PRE-1 middleware, PRE-2 handler/Marten, idempotencia Wolverine).

### Suite completa `Inspecciones.Domain.Tests`:

```
Correctas! - Con error: 0, Superado: 179, Omitido: 9, Total: 188, Duración: ~68 ms
```

- Baseline previo: 167 verdes + 6 omitidos = 173 total.
- Delta correcto: +12 verdes + 3 skips del slice 1k.
- 0 regresiones.

---

## 3. Decisiones tomadas

### Orden de precondiciones

Se siguió el orden recomendado en `red-notes.md §6 — Hand-off a green`:

1. **PRE-3 / I-F4.a**: `Estado != EstadoInspeccion.Firmada` → `InspeccionNoFirmadaException` con mensaje que incluye el estado actual (cubre tanto `EnEjecucion` como `CerradaSinOT` en un solo check).
2. **PRE-7 / I-F4.e (defensa)**: `Dictamen == DictamenOperacion.PuedeOperar` → `DictamenNoPermiteOTException`.
3. **PRE-4 / I-F4.b**: `!_hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == AccionRequerida.RequiereIntervencion)` → `SinHallazgosConIntervencionException`.
4. **PRE-5 / I-F4.c**: `OTSolicitada` → `OTYaSolicitadaException`.
5. **PRE-6 / I-F4.d**: `OTRechazada` → `OTRechazadaException`.
6. **Emisión**: `new OTSolicitada_v1(...)`.

### Mensajes de excepción

Los mensajes incluyen el estado/valor relevante para facilitar el diagnóstico:
- `InspeccionNoFirmadaException`: incluye `{Estado}` actual → cubre el check `*EnEjecucion*` y `*CerradaSinOT*` del test con un solo mensaje formateado.
- `DictamenNoPermiteOTException`: incluye `'PuedeOperar'` textual → cubre el check `*PuedeOperar*`.
- `SinHallazgosConIntervencionException`: incluye `AccionRequerida=RequiereIntervencion` → cubre el check `*RequiereIntervencion*`.
- `OTYaSolicitadaException`: incluye `solicitada` → cubre el check `*solicitada*`.
- `OTRechazadaException`: incluye `rechazada` → cubre el check `*rechazada*`.

### PRE-7 (dictamen) antes de PRE-4 (hallazgos)

El orden PRE-7 antes de PRE-4 es deliberado: si el dictamen es `PuedeOperar`, la precondición de hallazgo con RequiereIntervencion es necesariamente falsa (V-F8 garantiza coherencia dictamen↔hallazgos al firmar). Evaluar primero el dictamen da un mensaje de error más semántico. Los tests no dependen del orden relativo entre estas dos precondiciones.

### Apply puro no tocado

`Apply(OTSolicitada_v1)` ya estaba implementado correctamente desde la fase red: solo muta `OTSolicitada = true` y `SolicitadaEn = e.SolicitadaEn`. No se tocó.

---

## 4. Impulsos de refactor no implementados (candidatos para `refactorer`)

- El método `SolicitarOT` podría extraer la validación de estado en un helper privado `VerificarEstadoFirmada()` — patrón similar a `ObtenerHallazgoActivo()`. No se hizo porque ningún otro método usa esa validación aún.
- La comprobación `!_hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == ...)` podría ser una propiedad computada `HallazgosActivosConIntervencion` del aggregate. Candidato para refactor cuando más métodos necesiten la misma consulta.

---

## 5. Ajustes respecto al estado dejado por red

Ninguno. El red dejó los stubs exactamente correctos:
- Firma del método: `SolicitarOT(GenerarOT cmd, DateTimeOffset ahora)` — coincide con lo que CasoDeUso.cs llama.
- Excepciones: todas las 5 ya existían en `Excepciones.cs`.
- Eventos y Apply: correctos desde el red.
- `EstadoInspeccion.CerradaSinOT`: existía en el enum (verificado por Apply stub en Inspeccion.cs).
