# green-notes — Slice 1l — RechazarGenerarOT

**Agente:** green
**Fecha:** 2026-05-08
**Estado:** VERDE

---

## Archivos modificados

| Archivo | Operación |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Implementación completa de `RechazarOT(cmd, ahora)` — reemplazó el stub `NotImplementedException` |

### Ningún archivo nuevo creado

Todo el scaffolding (eventos, excepciones, enum, fixtures, tests) fue creado por el agente `red`. El `green` solo implementó el método de decisión faltante.

---

## Output de dotnet test

### Slice 1l — RechazarGenerarOTTests

```
Correctas! - Con error: 0, Superado: 18, Omitido: 3, Total: 21, Duración: 194 ms
```

Los 3 omitidos son los skips justificados del red:
- `RechazarGenerarOT_sin_capability_generar_ot_lanza_excepcion_403_PRE_1` — PRE-1 vive en middleware HTTP
- `RechazarGenerarOT_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE_2` — PRE-2 vive en handler/Marten
- `RechazarGenerarOT_replay_mismo_clientCommandId_no_duplica_eventos_ni_re_ejecuta_handler` — idempotencia Wolverine

### Slice 1k — GenerarOTTests (sin regresión)

```
Correctas! - Con error: 0, Superado: 12, Omitido: 3, Total: 15, Duración: 37 ms
```

### Proyecto Domain.Tests completo

```
Correctas! - Con error: 0, Superado: 197, Omitido: 12, Total: 209, Duración: 79 ms
```

---

## Implementación de `RechazarOT`

El método de decisión valida las precondiciones en este orden (matchea los tests del red):

1. **PRE-3** `cmd.Motivo.Trim().Length < 10` → `MotivoRechazoInvalidoException`
2. **PRE-4** `Estado != EstadoInspeccion.Firmada` → `InspeccionNoFirmadaException`
3. **PRE-5** `!_hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == AccionRequerida.RequiereIntervencion)` → `SinHallazgosConIntervencionException`
4. **PRE-6** `OTSolicitada` → `OTYaSolicitadaException`
5. **PRE-7** `OTRechazada` → `OTYaRechazadaException`

Emite exactamente dos eventos en orden causal:
1. `GeneracionOTRechazada_v1(InspeccionId, Motivo, RechazadoPor, RechazadaEn: ahora)`
2. `InspeccionCerradaSinOT_v1(InspeccionId, MotivoCierre: RechazadaPorAprobador, CerradaEn: ahora)`

Los `Apply` de ambos eventos ya estaban implementados por el red como mutaciones puras — el green no los tocó.

---

## Decisiones de "código más simple"

- El orden PRE-3 → PRE-4 es deliberado: el red confirmó en sus notas que los tests de PRE-3 usan streams en estado válido (Firmada), por lo que el orden entre PRE-3 y PRE-4 no afecta los tests actuales. Se eligió PRE-3 primero (validación de input antes de estado) por coherencia con la spec §4. Esta decisión es candidata a comentario del reviewer si prefiere PRE-4 primero.

- No se implementó validación de `Capabilities` en el dominio — la spec es explícita: PRE-1 vive solo en la capa HTTP.

---

## Impulsos de refactor no implementados (candidatos para `refactorer`)

- El método `RechazarOT` sigue el mismo patrón estructural que `SolicitarOT` (PRE-estado → PRE-hallazgos → PRE-OT flags). El `refactorer` podría extraer un método privado `ValidarPrecondicionesOT()` si ambos slices comparten suficiente lógica, pero no lo hice porque modificaría código del slice 1k.

- Los mensajes de error en las excepciones usan `cmd.InspeccionId` en lugar de `InspeccionId` (propiedad del aggregate). Son idénticos en runtime (el handler carga el aggregate por ese id), pero el aggregate ya conoce su propio id. Candidato a micro-cleanup del `refactorer`.

---

## Desviaciones de la spec

Ninguna. La implementación sigue exactamente la spec §4 y los escenarios §6 del slice 1l.
