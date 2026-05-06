# Red notes — Slice 2: ActualizarHallazgo

**Fecha:** 2026-05-06
**Autor:** red (ejecutado por orchestrator)
**Estado:** ROJO — 12 tests en rojo, 1 verde (rebuild directo), cero regresiones en slices anteriores.

---

## 1. Tests escritos

| Test | Escenario spec | Razón del fallo esperado |
|---|---|---|
| `ActualizarHallazgo_manual_a_RequiereIntervencion_emite_HallazgoActualizado_v1` | §6.1 | `NotImplementedException` — método de decisión es stub |
| `ActualizarHallazgo_manual_a_RequiereIntervencion_payload_correcto` | §6.1 | `NotImplementedException` — método de decisión es stub |
| `ActualizarHallazgo_preop_a_RequiereSeguimiento_emite_HallazgoActualizado_v1` | §6.2 | `NotImplementedException` — método de decisión es stub |
| `ActualizarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_IH7` | §6.3 | `NotImplementedException` en lugar de `InspeccionNoEnEjecucionException` |
| `ActualizarHallazgo_con_HallazgoId_desconocido_lanza_HallazgoNoEncontradoException` | §6.4 | `NotImplementedException` en lugar de `HallazgoNoEncontradoException` |
| `ActualizarHallazgo_sobre_hallazgo_eliminado_lanza_HallazgoEliminadoException` | §6.5 | `NotImplementedException` en lugar de `HallazgoEliminadoException` |
| `ActualizarHallazgo_RequiereIntervencion_sin_TipoFallaId_lanza_TipoYCausaFallaRequeridosException` | §6.6 | `NotImplementedException` en lugar de `TipoYCausaFallaRequeridosException` |
| `ActualizarHallazgo_RequiereIntervencion_sin_AccionCorrectiva_lanza_AccionCorrectivaRequeridaException` | §6.7 | `NotImplementedException` en lugar de `AccionCorrectivaRequeridaException` |
| `ActualizarHallazgo_RequiereIntervencion_con_AccionCorrectiva_vacia_lanza_AccionCorrectivaRequeridaException` | §6.7 | `NotImplementedException` en lugar de `AccionCorrectivaRequeridaException` |
| `ActualizarHallazgo_con_NovedadTecnica_solo_espacios_lanza_NovedadTecnicaVaciaException` | §6.8 | `NotImplementedException` en lugar de `NovedadTecnicaVaciaException` |
| `ActualizarHallazgo_con_NovedadTecnica_vacia_lanza_NovedadTecnicaVaciaException` | §6.8 | `NotImplementedException` en lugar de `NovedadTecnicaVaciaException` |
| `ActualizarHallazgo_rebuild_estado_identico_al_de_decision_in_process` | §6.9 | `NotImplementedException` — el método de decisión se invoca en el flujo |
| `ActualizarHallazgo_rebuild_desde_stream_reproduce_estado` | §6.9 | **VERDE** — usa `Inspeccion.Reconstruir` directamente sobre eventos manuales; `Apply(HallazgoActualizado_v1)` ya está implementado |

---

## 2. Comando de verificación

```
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj \
  --filter "FullyQualifiedName~ActualizarHallazgo" --no-build
```

**Resultado:** Con error: 12, Superado: 1, Omitido: 0, Total: 13

---

## 3. Estado de tests existentes (no regresión)

```
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --no-build
```

**Resultado:** Con error: 12, Superado: 41, Omitido: 0, Total: 53

Los 41 tests de slices 1a/1b/1c siguen en verde. Los 12 rojos son exclusivamente del slice 2.

---

## 4. Stubs y cambios de producción mínimos

Para compilar (TreatWarningsAsErrors=true), se requirieron:

- **`HallazgoActualizado_v1.cs`** — evento nuevo, record completo.
- **`HallazgoEliminado_v1.cs`** — evento stub (la lógica completa es slice 3).
- **`ActualizarHallazgo.cs`** — comando record, campos editables sin campos de origen (I-H8).
- **`Excepciones.cs`** — dos excepciones nuevas: `HallazgoNoEncontradoException`, `HallazgoEliminadoException`.
- **`Hallazgo.cs`** — extendido con campos editables: `ActividadId`, `ActividadDescripcion`, `NovedadTecnica`, `AccionCorrectiva`, `ObservacionCampo`, `Ubicacion`. Necesario para que `Apply(HallazgoActualizado_v1)` pueda mutar con `with`.
- **`Inspeccion.cs`** — tres adiciones:
  1. `AplicarEvento`: registrados los dos nuevos cases (`HallazgoActualizado_v1`, `HallazgoEliminado_v1`).
  2. `Apply(HallazgoRegistrado_v1)`: actualizado para pasar los nuevos campos del record `Hallazgo` extendido.
  3. `ActualizarHallazgo(cmd, ahora)` — stub con `throw new NotImplementedException()`. Genera el rojo.
  4. `Apply(HallazgoActualizado_v1)` — implementado (puro, sin validaciones). Permite que el test §6.9 de rebuild directo pase correctamente.
  5. `Apply(HallazgoEliminado_v1)` — stub mínimo que marca `Eliminado=true`. Permite que §6.5 (given con hallazgo eliminado) compile y el aggregate se pueda reconstruir.

---

## 5. Observación sobre el test verde

`ActualizarHallazgo_rebuild_desde_stream_reproduce_estado` pasa porque construye el stream manualmente con `HallazgoActualizado_v1` y llama `Inspeccion.Reconstruir(stream)` directamente — sin tocar el método de decisión stub. Este es el comportamiento esperado y correcto: valida que `Apply` es puro y que el agregado puede rebuildar desde un stream que incluye el evento. Cuando la fase green implemente el método de decisión, este test seguirá en verde por la misma razón.
