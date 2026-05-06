# red-notes — slice 1d: ActualizarHallazgo

## Estado: ROJO VÁLIDO

15/15 tests fallan. 0 fallan por error de compilación.

---

## Tests escritos vs escenarios spec §6

| # | Nombre del método | Escenario spec | Razón del fallo en rojo |
|---|---|---|---|
| 1 | `ActualizarHallazgo_upgrade_a_RequiereIntervencion_emite_HallazgoActualizado_v1` | §6.1 | `NotImplementedException` desde `Inspeccion.ActualizarHallazgo` stub |
| 2 | `ActualizarHallazgo_downgrade_a_RequiereSeguimiento_limpia_campos_intervencion` | §6.2 | `NotImplementedException` desde `Inspeccion.ActualizarHallazgo` stub |
| 3 | `ActualizarHallazgo_recaptura_GPS_sin_cambiar_accion_requerida_emite_ubicacion_actualizada` | §6.3 | `NotImplementedException` desde `Inspeccion.ActualizarHallazgo` stub |
| 4 | `ActualizarHallazgo_solo_texto_mantiene_RequiereIntervencion_emite_evento_con_datos_correctos` | §6.4 | `NotImplementedException` desde `Inspeccion.ActualizarHallazgo` stub |
| 5 | `ActualizarHallazgo_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException` | §6.5 PRE-A | Esperaba `InspeccionNoEnEjecucionException`; recibió `NotImplementedException` |
| 6 | `ActualizarHallazgo_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException` | §6.6 PRE-B1 | Esperaba `HallazgoNoEncontradoException`; recibió `NotImplementedException` |
| 7 | `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` | §6.7 PRE-B2 | Esperaba `HallazgoEliminadoException`; recibió `NotImplementedException` |
| 8 | `ActualizarHallazgo_con_NovedadTecnica_vacia_lanza_NovedadTecnicaVaciaException` | §6.8 PRE-C | Esperaba `NovedadTecnicaVaciaException`; recibió `NotImplementedException` |
| 9 | `ActualizarHallazgo_RequiereIntervencion_sin_TipoFallaId_lanza_TipoYCausaFallaRequeridosException` | §6.9 PRE-D1 | Esperaba `TipoYCausaFallaRequeridosException`; recibió `NotImplementedException` |
| 10 | `ActualizarHallazgo_RequiereIntervencion_sin_CausaFallaId_lanza_TipoYCausaFallaRequeridosException` | §6.10 PRE-D1 | Esperaba `TipoYCausaFallaRequeridosException`; recibió `NotImplementedException` |
| 11 | `ActualizarHallazgo_RequiereIntervencion_sin_AccionCorrectiva_lanza_AccionCorrectivaRequeridaException` | §6.11 PRE-D2 | Esperaba `AccionCorrectivaRequeridaException`; recibió `NotImplementedException` |
| 12 | `ActualizarHallazgo_NoRequiereIntervencion_con_TipoFallaId_lanza_CamposIntervencionNoPermitidosException` | §6.12 PRE-E | Esperaba `CamposIntervencionNoPermitidosException`; recibió `NotImplementedException` |
| 13 | `ActualizarHallazgo_RequiereSeguimiento_con_AccionCorrectiva_lanza_CamposIntervencionNoPermitidosException` | §6.13 PRE-E | Esperaba `CamposIntervencionNoPermitidosException`; recibió `NotImplementedException` |
| 14 | `ActualizarHallazgo_campos_inmutables_no_cambian_tras_actualizacion` | §6.15 I-H8 | `NotImplementedException` desde `Inspeccion.ActualizarHallazgo` stub |
| 15 | `ActualizarHallazgo_rebuild_desde_stream_reproduce_estado` | §6.16 | `NotImplementedException` desde `Inspeccion.Apply(HallazgoActualizado_v1)` stub |

---

## §6.14 — omitido (integración)

PRE-F: "stream no existe en Marten → `InspeccionNoEncontradaException`" requiere Marten/Postgres.
No pertenece a tests unitarios de dominio. Se implementa en tests de integración del handler.

---

## Notas sobre §6.7 (PRE-B2)

El test afirma `HallazgoEliminadoException` sobre un stream donde el hallazgo NO está eliminado
(porque `HallazgoEliminado_v1` no existe aún — es del slice `EliminarHallazgo`).
Esto es correcto: en rojo falla con `NotImplementedException` (stub). El comentario en el
test documenta que **green debe**:
1. Implementar `Apply(HallazgoActualizado_v1)` y `ActualizarHallazgo`.
2. Cuando llegue el slice `EliminarHallazgo`, reemplazar `dados` por `StreamConHallazgoEliminado()`.

---

## Stubs añadidos en este slice

### Dominio (src/Inspecciones.Domain)

| Archivo | Cambio |
|---|---|
| `ActualizarHallazgo.cs` | Nuevo — record del comando |
| `HallazgoActualizado_v1.cs` | Nuevo — record del evento |
| `Excepciones.cs` | Añadidas: `HallazgoNoEncontradoException`, `HallazgoEliminadoException`, `CamposIntervencionNoPermitidosException` |
| `Hallazgo.cs` | Añadidos campos: `NovedadTecnica`, `AccionCorrectiva`, `UbicacionGps` |
| `Inspeccion.cs` | Añadidos: método `ActualizarHallazgo` (stub), `Apply(HallazgoActualizado_v1)` (stub), `case HallazgoActualizado_v1` en `AplicarEvento`, campos nuevos en `Apply(HallazgoRegistrado_v1)` |

### Tests (tests/Inspecciones.Domain.Tests)

| Archivo | Cambio |
|---|---|
| `CasoDeUso.cs` | Añadido helper `ActualizarHallazgo` |
| `HallazgoFixtures.cs` | Añadidos: `HallazgoG3..G6`, fixtures `StreamConHallazgoRegistrado`, `ComandoActualizarConIntervencion`, `ComandoActualizarConSeguimiento`, `ComandoActualizarSoloTexto` |
| `ActualizarHallazgoTests.cs` | Nuevo — 15 tests |

---

## Comando para verificar rojo

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
    --filter "FullyQualifiedName~ActualizarHallazgoTests" --no-build
```

Resultado esperado: `Con error: 15, Superado: 0`

## Comando para verificar que slices anteriores siguen verdes

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj `
    --filter "FullyQualifiedName~IniciarInspeccionTests|FullyQualifiedName~RegistrarHallazgoTests" --no-build
```

Resultado esperado: `Correcto: 39, Con error: 0`
