# Red notes — Slice 1e — EliminarHallazgo

**Autor:** red
**Fecha:** 2026-05-06
**Spec consumida:** `slices/1e-eliminar-hallazgo/spec.md` (firmada 2026-05-06).

---

## 1. Tests escritos

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `EliminarHallazgo_en_inspeccion_en_ejecucion_emite_HallazgoEliminado_v1` | §6.1 happy path — hallazgo sin hijos | `tests/…/EliminarHallazgoTests.cs` |
| `EliminarHallazgo_con_RequiereIntervencion_emite_HallazgoEliminado_v1_sin_restriccion` | §6.2 happy path — hallazgo RequiereIntervencion | ídem |
| `EliminarHallazgo_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7` | §6.3 PRE-A / I-H7 / I-F1 | ídem |
| `EliminarHallazgo_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE_B1` | §6.4 PRE-B1 | ídem |
| `EliminarHallazgo_con_HallazgoId_ya_eliminado_lanza_HallazgoEliminadoException_PRE_B2` | §6.5 PRE-B2 | ídem |
| `EliminarHallazgo_con_Motivo_vacio_lanza_MotivoEliminacionVacioException_PRE_C` | §6.6 PRE-C | ídem |
| `EliminarHallazgo_con_hijos_activos_lanza_HallazgoTieneHijosActivosException_I_H9` | §6.7 PRE-D / I-H9 `[Skip]` | ídem |
| (§6.8 omitido — integración) | §6.8 PRE-F | ver nota §6.8 abajo |
| `EliminarHallazgo_rebuild_desde_stream_reproduce_estado_Eliminado` | §6.9 rebuild desde stream | ídem |
| `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` | §6.10 DoD — levantar skip followup #21 | `tests/…/ActualizarHallazgoTests.cs` |

### §6.7 — Skip justificado

El test `EliminarHallazgo_con_hijos_activos_lanza_HallazgoTieneHijosActivosException_I_H9` lleva
`[Fact(Skip = "I-H9: requiere slices de repuestos/adjuntos")]`. No existen eventos de
`RepuestoEstimado_v1` ni `AdjuntoSubido_v1` aún — construir el estado con hijos activos sin esos
eventos viola la regla de cero mocks del dominio. El código de PRE-D **sí** debe implementarse
con las colecciones internas del aggregate (vacías en MVP), para que la invariante esté activa
automáticamente cuando lleguen esos slices. Skip levantado con el DoD del primer slice
`AsignarRepuesto` o `AdjuntarArchivo`.

### §6.8 — PRE-F omitido en tests unitarios

El escenario §6.8 (`InspeccionId` no existe en Marten) requiere infra: el handler carga el
aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)` y
verifica si retorna `null`. Esto es un test de integración (handler + Marten + Postgres).
Se implementa en `tests/Inspecciones.Infrastructure.Tests` o `tests/Inspecciones.Application.Tests`
en el slice de integración correspondiente.

### §6.10 — DoD especial: test ex-skip de ActualizarHallazgoTests

El test `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` (slice 1d
§6.7, followup #21) estaba marcado `[Fact(Skip=...)]`. En este slice:
1. Se implementó `StreamConHallazgoEliminado()` en `HallazgoFixtures.cs` usando el evento real
   `HallazgoEliminado_v1` sobre `HallazgoG5`.
2. Se quitó el `[Fact(Skip=...)]` y se reemplazó el `dados` por `StreamConHallazgoEliminado()`.
3. El `HallazgoId` del comando se actualizó a `HallazgoG5` (el que el fixture elimina).
4. El test **falla en rojo** actualmente porque `Apply(HallazgoEliminado_v1)` es un stub con
   `NotImplementedException`. Cuando green implemente ese `Apply`, el test pasará en verde
   automáticamente (PRE-B2 ya estaba implementada en slice 1d).

---

## 2. Verificación de estado rojo

```
dotnet test tests/Inspecciones.Domain.Tests --filter "EliminarHallazgo" --no-build
# resultado: Con error: 7, Superado: 0, Omitido: 1
```

Razones de fallo por test:

| Test | Razón del fallo en rojo |
|---|---|
| `…_emite_HallazgoEliminado_v1` (§6.1) | `NotImplementedException` — `Inspeccion.EliminarHallazgo` es stub |
| `…_sin_restriccion` (§6.2) | `NotImplementedException` — ídem |
| `…_InspeccionNoEnEjecucionException_I_H7` (§6.3) | Esperaba `InspeccionNoEnEjecucionException`, recibió `NotImplementedException` |
| `…_HallazgoNoEncontradoException_PRE_B1` (§6.4) | Esperaba `HallazgoNoEncontradoException`, recibió `NotImplementedException` |
| `…_HallazgoEliminadoException_PRE_B2` (§6.5) | Esperaba `HallazgoEliminadoException`, recibió `NotImplementedException` en `Apply(HallazgoEliminado_v1)` (stub) durante `Reconstruir` del `dados` |
| `…_MotivoEliminacionVacioException_PRE_C` (§6.6) | Esperaba `MotivoEliminacionVacioException`, recibió `NotImplementedException` |
| `…_I_H9` (§6.7) | `[Skip]` — no ejecutado |
| `…_reproduce_estado_Eliminado` (§6.9) | `NotImplementedException` — `Inspeccion.EliminarHallazgo` es stub |

Test adicional afectado por este slice:

| Test | Razón del fallo en rojo |
|---|---|
| `ActualizarHallazgo_con_HallazgoId_eliminado_…` (1d §6.7) | Esperaba `HallazgoEliminadoException`, recibió `NotImplementedException` en `Apply(HallazgoEliminado_v1)` |

---

## 3. Código de producción tocado

Stubs y tipos de datos añadidos (todos en `src/Inspecciones.Domain/Inspecciones/`):

- **`EliminarHallazgo.cs`** — record comando nuevo. Completo (datos, sin lógica).
- **`HallazgoEliminado_v1.cs`** — record evento nuevo. Completo (datos, sin lógica).
- **`Excepciones.cs`** — añadidas `MotivoEliminacionVacioException` y `HallazgoTieneHijosActivosException`.
- **`Hallazgo.cs`** — añadido parámetro `MotivoEliminacion: string?` al record. Cambio de shape aditivo.
- **`Inspeccion.cs`** — cuatro cambios:
  1. `Apply(HallazgoRegistrado_v1)`: añadido `MotivoEliminacion: null` en la construcción del record `Hallazgo`.
  2. `Apply(HallazgoActualizado_v1)`: añadido `MotivoEliminacion = _hallazgos[idx].MotivoEliminacion` en el `with { ... }` para preservar el valor.
  3. `AplicarEvento` switch: añadido `case HallazgoEliminado_v1 e: Apply(e); break;`.
  4. Stubs `EliminarHallazgo(...)` y `Apply(HallazgoEliminado_v1 e)` con `throw new NotImplementedException()`.

Tests modificados:

- **`HallazgoFixtures.cs`** — `StreamConHallazgoEliminado()` implementado con evento real; añadidos `ComandoEliminarHallazgo()` y `HallazgoEliminadoEjemplo()`.
- **`CasoDeUso.cs`** — añadido método `EliminarHallazgo(...)`.
- **`ActualizarHallazgoTests.cs`** — quitado `[Fact(Skip=...)]` del test §6.7; reemplazado `dados` por `StreamConHallazgoEliminado()` y `HallazgoId` por `HallazgoG5`.

---

## 4. Desviaciones respecto a la spec

Sin desviaciones. Todos los escenarios de §6 tienen cobertura directa:
- §6.1 → test §6.1
- §6.2 → test §6.2
- §6.3 → test §6.3
- §6.4 → test §6.4
- §6.5 → test §6.5
- §6.6 → test §6.6
- §6.7 → test §6.7 `[Skip]` (decisión de firma)
- §6.8 → omitido (integración, documentado)
- §6.9 → test §6.9 rebuild
- §6.10 → modificación en `ActualizarHallazgoTests.cs`

Nota: El test §6.5 (PRE-B2) usa `HallazgoG1` (no `HallazgoG3` como dice el texto narrativo de la spec §6.5) porque el fixture `HallazgoEliminadoEjemplo` por defecto usa `HallazgoG1` en el stream ad-hoc del test. La invariante probada es la misma.

---

## 5. Hand-off a green

- Spec firmada: sí.
- Todos los tests nuevos en rojo: sí — 7 failing por `NotImplementedException`, 1 skip.
- Sin cambios de comportamiento accidentales: sí — los 14 tests pre-existentes de `ActualizarHallazgo` siguen pasando.
- Build limpio: sí — 0 errores, 0 advertencias.

**Lo que green debe implementar:**

1. `Inspeccion.EliminarHallazgo(EliminarHallazgo cmd, DateTimeOffset ahora)` — validar PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-D (con colecciones vacías para I-H9), y emitir `HallazgoEliminado_v1`.
2. `Inspeccion.Apply(HallazgoEliminado_v1 e)` — mutar `_hallazgos[idx]` con `Eliminado=true` y `MotivoEliminacion=e.Motivo`; añadir `e.EliminadoPor` a `_contribuyentes`.
3. Al implementar `Apply`, el test `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` debe pasar automáticamente (followup #21 cerrado).
