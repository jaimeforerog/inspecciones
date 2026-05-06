# Red notes — Slice 1c — RegistrarHallazgo

**Autor:** red
**Fecha:** 2026-05-06
**Spec consumida:** `slices/1c-registrar-hallazgo/spec.md`

---

## 1. Tests escritos

### Tests de dominio puro — `tests/Inspecciones.Domain.Tests/Inspecciones/RegistrarHallazgoTests.cs`

| Test | Escenario spec §6.X | Razón del fallo |
|---|---|---|
| `RegistrarHallazgo_origen_manual_sin_intervencion_emite_HallazgoRegistrado_v1` | §6.1 happy path | `NotImplementedException` en stub |
| `RegistrarHallazgo_origen_manual_sin_intervencion_emite_evento_con_payload_completo` | §6.1 payload | `NotImplementedException` |
| `RegistrarHallazgo_origen_manual_sin_intervencion_aggregate_tiene_un_hallazgo_activo` | §6.1 estado I2b | `NotImplementedException` |
| `RegistrarHallazgo_origen_preoperacional_con_intervencion_emite_HallazgoRegistrado_v1` | §6.2 happy path preop | `NotImplementedException` |
| `RegistrarHallazgo_origen_preoperacional_con_intervencion_aggregate_tiene_un_hallazgo` | §6.2 estado | `NotImplementedException` |
| `RegistrarHallazgo_con_RequiereSeguimiento_sin_tipo_causa_falla_no_lanza_I_H5` | §6.3 I-H5 | `NotImplementedException` (lanza cuando no debería) |
| `RegistrarHallazgo_con_RequiereSeguimiento_emite_evento_con_tipo_causa_nulos_I_H5` | §6.3 evento | `NotImplementedException` |
| `RegistrarHallazgo_dos_hallazgos_sobre_misma_parte_I_H6_ambos_permitidos` | §6.4 I-H6 | `NotImplementedException` |
| `RegistrarHallazgo_dos_hallazgos_sobre_misma_parte_aggregate_tiene_dos_activos_I_H6` | §6.4 estado | `NotImplementedException` |
| `RegistrarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucion_PRE_3` | §6.5 PRE-3 | `NotImplementedException` (excepción incorrecta) |
| `RegistrarHallazgo_en_inspeccion_cancelada_lanza_InspeccionNoEnEjecucion_PRE_3` | §6.5 PRE-3 variante | `NotImplementedException` |
| `RegistrarHallazgo_origen_preoperacional_sin_NovedadPreopId_lanza_I_H2` | §6.7 I-H2 | `NotImplementedException` |
| `RegistrarHallazgo_origen_manual_con_NovedadPreopId_lanza_I_H3` | §6.8 I-H3 | `NotImplementedException` |
| `RegistrarHallazgo_con_RequiereIntervencion_sin_TipoFallaId_lanza_I_H4` | §6.9 I-H4 | `NotImplementedException` |
| `RegistrarHallazgo_con_RequiereIntervencion_sin_CausaFallaId_lanza_I_H4` | §6.10 I-H4 variante | `NotImplementedException` |
| `RegistrarHallazgo_con_RequiereIntervencion_sin_AccionCorrectiva_lanza_PRE_8` | §6.11 PRE-8 | `NotImplementedException` |
| `RegistrarHallazgo_con_RequiereIntervencion_con_AccionCorrectiva_vacia_lanza_PRE_8` | §6.11 PRE-8 whitespace | `NotImplementedException` |
| `RegistrarHallazgo_con_NovedadTecnica_vacia_lanza_PRE_9` | §6.12 PRE-9 | `NotImplementedException` |
| `RegistrarHallazgo_con_NovedadTecnica_solo_espacios_lanza_PRE_9` | §6.12 PRE-9 whitespace | `NotImplementedException` |
| `RegistrarHallazgo_con_origen_Seguimiento_lanza_OrigenNoSoportado_PRE_10` | §6.13 PRE-10 | `NotImplementedException` |
| `RegistrarHallazgo_con_origen_Monitoreo_lanza_OrigenNoSoportado_PRE_10` | §6.13 PRE-10 Monitoreo | `NotImplementedException` |
| `RegistrarHallazgo_rebuild_desde_stream_reproduce_estado` | §6.15 rebuild obligatorio | `Apply(HallazgoRegistrado_v1)` es stub vacío — `Hallazgos.Count == 0` cuando se esperan 2 |
| `RegistrarHallazgo_rebuild_estado_identico_al_de_decision_in_process` | §6.15 rebuild in-process | `NotImplementedException` (decisión no implementada) |
| `Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12` | §6.15 sub-escenario FOLLOWUPS #12 | **VERDE** — `AplicarEvento` ya tenía `default: throw InvalidOperationException` |

### Tests de integración — `tests/Inspecciones.Application.Tests/Inspecciones/RegistrarHallazgoHandlerTests.cs`

| Test | Escenario spec §6.X | Razón del fallo |
|---|---|---|
| `RegistrarHallazgo_inspeccion_no_existe_lanza_InspeccionNoEncontrada_PRE_2` | §6.14 PRE-2 | `NotImplementedException` en handler stub |
| `RegistrarHallazgo_parte_no_pertenece_al_equipo_lanza_ParteNoCorrespondeAlEquipo_INV` | §6.6 INV-PartePerteneceAlEquipo | `NotImplementedException` en handler stub |
| `RegistrarHallazgo_con_parte_valida_del_equipo_no_lanza_INV_PartePerteneceAlEquipo` | §6.6 variante positiva | `NotImplementedException` — confirma que la validación pasa y el stub es el único obstáculo |

### Tests E2E — `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs`

| Test | Escenario spec §6.X | Razón del fallo |
|---|---|---|
| `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` | §6.1 E2E HTTP | Endpoint no registrado + `NotImplementedException` |
| `POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008` | §6.16 idempotencia | Endpoint no registrado |

---

## 2. Verificación de estado rojo

### Comando ejecutado:

```
dotnet test tests/Inspecciones.Domain.Tests/ --filter "FullyQualifiedName~RegistrarHallazgo"
```

### Resultado:

```
Con error: 23, Superado: 1 (evento_desconocido), Total: 24
```

Tests del slice 1a: 17/17 verdes (16 tests originales + 1 bootstrap). No rotos.

### Razón exacta del fallo de cada test:

- **Tests happy path, invariantes y rebuild de decisión (21 tests):** `System.NotImplementedException: Pendiente implementar en fase green del slice 1c.` — lanzado por el stub `Inspeccion.RegistrarHallazgo(cmd, ahora)`.
- **Test rebuild estado:** `Expected aggregate.Hallazgos to contain 2 item(s), but found 0` — porque `Apply(HallazgoRegistrado_v1)` es stub vacío que no muta `_hallazgos`.
- **Test evento desconocido (VERDE):** Pasa porque la rama defensiva `default: throw InvalidOperationException` ya estaba implementada en `AplicarEvento` desde el slice 1a.

---

## 3. Stubs de producción creados

| Archivo | Tipo | Contenido |
|---|---|---|
| `src/Inspecciones.Domain/Inspecciones/OrigenHallazgo.cs` | enum nuevo | `Manual, PreOperacional, Seguimiento, Monitoreo` |
| `src/Inspecciones.Domain/Inspecciones/AccionRequerida.cs` | enum nuevo | `NoRequiereIntervencion, RequiereIntervencion, RequiereSeguimiento` |
| `src/Inspecciones.Domain/Inspecciones/RegistrarHallazgo.cs` | record comando | Payload completo según spec §2 |
| `src/Inspecciones.Domain/Inspecciones/HallazgoRegistrado_v1.cs` | record evento | Payload completo según spec §3 |
| `src/Inspecciones.Domain/Inspecciones/Hallazgo.cs` | record value object | Shape mínimo para state en aggregate |
| `src/Inspecciones.Domain/Inspecciones/InspeccionFirmada_v1.cs` | record evento stub | Para tests PRE-3; se reemplaza cuando exista slice FirmarInspeccion |
| `src/Inspecciones.Domain/Inspecciones/InspeccionCancelada_v1.cs` | record evento stub | Para tests PRE-3 variante Cancelada |
| `src/Inspecciones.Domain/Catalogos/ParteEquipoLocal.cs` | record nuevo | `ParteEquipoId, ParteCodigo, ParteNombre` |
| `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | extensión | 7 nuevas excepciones: `InspeccionNoEnEjecucionException`, `NovedadPreopOrigenIdRequeridoException`, `NovedadPreopOrigenIdNoPermitidoException`, `TipoYCausaFallaRequeridosException`, `AccionCorrectivaRequeridaException`, `NovedadTecnicaVaciaException`, `OrigenNoSoportadoException` |
| `src/Inspecciones.Application/Inspecciones/Excepciones.cs` | extensión | 2 nuevas: `InspeccionNoEncontradaException`, `ParteNoCorrespondeAlEquipoException` |
| `src/Inspecciones.Application/Inspecciones/RegistrarHallazgoHandler.cs` | stub handler | `throw new NotImplementedException()` + `RegistrarHallazgoResult` record |
| `src/Inspecciones.Api/Inspecciones/RegistrarHallazgoRequest.cs` | DTO entrada | Payload del endpoint |
| `src/Inspecciones.Api/Inspecciones/RegistrarHallazgoResponse.cs` | DTO salida | `HallazgoId, InspeccionId, AccionRequerida, RegistradoEn` |

### Modificaciones a existentes:

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | + `Hallazgos`, `Contribuyentes`, `RegistrarHallazgo(stub)`, `Apply(HallazgoRegistrado_v1)(stub)`, `Apply(InspeccionFirmada_v1)`, `Apply(InspeccionCancelada_v1)`, casos en `AplicarEvento` |
| `src/Inspecciones.Domain/Catalogos/EquipoLocal.cs` | + `Partes: IReadOnlyList<ParteEquipoLocal>?` con default null (backwards-compatible) |
| `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` | + método `RegistrarHallazgo(dados, cmd, ahora)` |

---

## 4. Desviaciones respecto a la spec

### Decisión de diseño — eventos de estado para PRE-3

La spec §6.5 dice "construir aggregate con Estado=Firmada manualmente o con eventos `[InspeccionIniciada_v1, InspeccionFirmada_v1]`". Como `InspeccionFirmada_v1` no existía, se crearon stubs mínimos en el dominio (`InspeccionFirmada_v1`, `InspeccionCancelada_v1`) con `Apply` puro que solo muta `Estado`. Esto es un avance parcial del slice de firma — no hay lógica, solo la transición de estado. El green los reemplazará o extenderá cuando llegue el slice FirmarInspeccion.

### Decisión de diseño — Apply(HallazgoRegistrado_v1) stub

El `Apply(HallazgoRegistrado_v1)` es un stub vacío (accede a campos para pasar CA1822 pero no los muta). Esto hace que el test de rebuild falle de forma "observable" (`Hallazgos.Count == 0`), que es la razón correcta del rojo: el green debe implementar la mutación real.

### Test del followup #12 en verde

El test `Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12` pasa en verde porque la rama defensiva ya estaba implementada. Esto es correcto — el followup pide verificar ese comportamiento, no implementarlo desde cero. El test cumple su función de test de regresión.

### `Apply(InspeccionIniciada_v1)` actualizado

Se añadió `_contribuyentes.Add(e.TecnicoIniciador)` al `Apply(InspeccionIniciada_v1)` para que el test `I2b` del rebuild pueda verificar que los contribuyentes del evento inicial también se registran. Esto no rompe los tests del slice 1a (el campo no se asercionaba allí).

---

## 5. Hand-off a green

- Spec firmada: sí (checklist pre-firma completo en spec.md §13).
- Todos los tests del 1c rojos: sí (23/24 en Domain.Tests; los de integración y E2E también en rojo por stub).
- Test evento desconocido (followup #12): verde — comportamiento ya implementado.
- Tests del slice 1a: 17/17 verdes — no rotos.
- Build: 0 errores, 0 warnings (`TreatWarningsAsErrors=true`).

### Para el green — qué implementar en orden:

1. `Inspeccion.RegistrarHallazgo(cmd, ahora)` — las validaciones en este orden: PRE-10, PRE-9, PRE-3, PRE-6, PRE-5, PRE-7, PRE-8 (para que las excepciones tengan precedencia lógica). Emitir `HallazgoRegistrado_v1`.
2. `Apply(HallazgoRegistrado_v1 e)` — añadir `Hallazgo` a `_hallazgos`, añadir `e.EmitidoPor` a `_contribuyentes`.
3. `RegistrarHallazgoHandler.ManejarAsync` — cargar aggregate, validar PRE-2 (InspeccionId existe), PRE-4 (ParteEquipoId ∈ EquipoLocal.Partes), delegar al aggregate, `Events.Append`, `SaveChangesAsync`.
4. Endpoint `POST /api/v1/inspecciones/{id}/hallazgos` en `InspeccionesEndpoints.cs`.
5. Registrar `RegistrarHallazgoHandler` en `Program.cs`.
