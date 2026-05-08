# Red notes — slice 1i' RegistrarEvaluacionCualitativa

**Fecha:** 2026-05-08
**Autor:** red
**Estado:** rojo verificado — 26 tests fallan por NotImplementedException, 3 Skip, 125 tests previos siguen verdes.

---

## 1. Tests escritos

| # | Método de test | Escenario spec |
|---|---|---|
| 1 | `RegistrarEvaluacionCualitativa_Bueno_emite_exactamente_un_EvaluacionCualitativaRegistrada_v1` | §6.1 |
| 2 | `RegistrarEvaluacionCualitativa_Bueno_no_emite_HallazgoRegistrado_v1` | §6.1 |
| 3 | `RegistrarEvaluacionCualitativa_Bueno_payload_correcto_en_evento_emitido` | §6.1 |
| 4 | `RegistrarEvaluacionCualitativa_Bueno_agrega_item_a_itemsEvaluados` | §6.1 |
| 5 | `RegistrarEvaluacionCualitativa_Regular_emite_exactamente_un_EvaluacionCualitativaRegistrada_v1` | §6.2 |
| 6 | `RegistrarEvaluacionCualitativa_Regular_no_emite_HallazgoRegistrado_v1` | §6.2 |
| 7 | `RegistrarEvaluacionCualitativa_Regular_propagada_observacion_en_evento` | §6.2 |
| 8 | `RegistrarEvaluacionCualitativa_Malo_emite_dos_eventos_en_orden_causal` | §6.3 |
| 9 | `RegistrarEvaluacionCualitativa_Malo_EvaluacionCualitativaRegistrada_v1_con_payload_correcto` | §6.3 |
| 10 | `RegistrarEvaluacionCualitativa_Malo_HallazgoRegistrado_v1_con_payload_correcto` | §6.3 |
| 11 | `RegistrarEvaluacionCualitativa_Malo_aggregate_tiene_hallazgo_monitoreo_activo` | §6.3 |
| 12 | `RegistrarEvaluacionCualitativa_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1` | §6.4 |
| 13 | `RegistrarEvaluacionCualitativa_en_inspeccion_monitoreo_firmada_lanza_InspeccionNoEnEjecucionException_I_M2` | §6.5 |
| 14 | `RegistrarEvaluacionCualitativa_con_ItemId_inexistente_en_snapshot_lanza_ItemNoEncontradoEnSnapshotException_I_M3` | §6.6 |
| 15 | `RegistrarEvaluacionCualitativa_en_item_omitido_lanza_ItemOmitidoNoPuedeMedirseException_I_M4` | §6.7 |
| 16 | `RegistrarEvaluacionCualitativa_en_item_numerico_lanza_ItemNoEsCualitativoException_I_M5b` | §6.8 |
| 17 | `RegistrarEvaluacionCualitativa_segunda_vez_el_mismo_item_lanza_ItemYaEvaluadoException_I_M7` | §6.9 |
| 18 | `RegistrarEvaluacionCualitativa_segunda_vez_no_cambia_itemsEvaluados` | §6.9 |
| 19 | `RegistrarEvaluacionCualitativa_InspeccionId_no_existe_lanza_InspeccionNoEncontradaException` [SKIP] | §6.10 |
| 20 | `RegistrarEvaluacionCualitativa_Malo_con_ParteEquipoId_nulo_en_snapshot_lanza_ParteEquipoIdAusenteEnSnapshotException` | §6.11 |
| 21 | `RegistrarEvaluacionCualitativa_Bueno_con_ParteEquipoId_nulo_en_snapshot_no_lanza` | §6.11 guard negativo |
| 22 | `RegistrarEvaluacionCualitativa_mismo_clientCommandId_retorna_respuesta_original_sin_reejecutar` [SKIP] | §6.12 |
| 23 | `RegistrarEvaluacionCualitativa_segundo_item_Malo_coexiste_con_hallazgo_previo` | §6.13 |
| 24 | `RegistrarEvaluacionCualitativa_segundo_item_Malo_aggregate_tiene_dos_hallazgos_activos` | §6.13 |
| 25 | `RegistrarEvaluacionCualitativa_en_item_numerico_lanza_I_M5b_aunque_itemsMedidos_tenga_ese_ItemId` | §6.14 |
| 26 | `RegistrarEvaluacionCualitativa_SaveChangesAsync_falla_no_persiste_ningun_evento` [SKIP] | §6.15 |
| 27 | `RegistrarEvaluacionCualitativa_Malo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones` | §6.16 (obligatorio) |
| 28 | `RegistrarEvaluacionCualitativa_Bueno_rebuild_desde_stream_no_tiene_hallazgos` | §6.16 |
| 29 | `RegistrarEvaluacionCualitativa_Malo_Apply_puro_EvaluacionCualitativaRegistrada_antes_de_HallazgoRegistrado` | §6.16 orden causal |

**Total:** 26 tests activos (23 fallando correctamente + 3 Skip). 3 Skip por dependencia de infra.

---

## 2. Comando para verificar fallo

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj \
  --filter "FullyQualifiedName~RegistrarEvaluacionCualitativa" \
  --no-build
```

**Resultado:** Con error: 26, Superado: 0, Omitido: 3 — todos los tests activos fallan.

Para verificar que los tests previos siguen verdes:

```powershell
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj \
  --filter "FullyQualifiedName!~RegistrarEvaluacionCualitativa" \
  --no-build
```

**Resultado:** Correctas: 125, Con error: 0, Omitido: 1 — baseline intacto.

---

## 3. Razón del fallo de cada test

**Todos los tests activos fallan por:**

```
System.NotImplementedException: Pendiente de implementación en fase green del slice 1i'.
El método debe validar PRE-3..PRE-8 y emitir EvaluacionCualitativaRegistrada_v1
(y HallazgoRegistrado_v1 si Calificacion=Malo).
   at Inspecciones.Domain.Inspecciones.Inspeccion.RegistrarEvaluacionCualitativa(...)
```

El stub en `Inspeccion.cs` lanza `NotImplementedException` incondicionalmente. Esto es correcto para la fase roja.

- Tests de **happy path** (§6.1, §6.2, §6.3): fallan porque el método no retorna eventos.
- Tests de **precondición** (§6.4..§6.9, §6.11): fallan porque se espera excepción de dominio específica pero se lanza `NotImplementedException`.
- Tests de **rebuild** (§6.16): fallan porque primero llaman al método de decisión (que lanza NotImplementedException) antes de hacer el rebuild.
- Test de **coexistencia de sets** (§6.14): falla por la misma razón que los de precondición.

---

## 4. Stubs de producción añadidos

### Nuevos archivos

- `src/Inspecciones.Domain/Inspecciones/CalificacionCualitativa.cs` — enum `{Bueno, Regular, Malo}` (P-1).
- `src/Inspecciones.Domain/Inspecciones/EvaluacionCualitativaRegistrada_v1.cs` — evento con `EmitidoPor` (aprobado en firma).
- `src/Inspecciones.Domain/Inspecciones/RegistrarEvaluacionCualitativa.cs` — record del comando.
- `src/Inspecciones.Application/Inspecciones/RegistrarEvaluacionCualitativaResult.cs` — DTO resultado.
- `src/Inspecciones.Application/Inspecciones/RegistrarEvaluacionCualitativaHandler.cs` — handler stub.
- `tests/Inspecciones.Domain.Tests/Inspecciones/EvaluacionCualitativaFixtures.cs` — fixtures del slice.
- `tests/Inspecciones.Domain.Tests/Inspecciones/RegistrarEvaluacionCualitativaTests.cs` — tests.

### Archivos modificados (incidentales — backward compat)

- `src/Inspecciones.Domain/Inspecciones/HallazgoRegistrado_v1.cs` — campo `EvaluacionOrigenId: int?` en posición 6 (después de `MedicionOrigenId`). Backward compat: Marten deserializa como null para eventos históricos.
- `src/Inspecciones.Domain/Inspecciones/Hallazgo.cs` — campo `EvaluacionOrigenId: int? = null` con default.
- `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` — 2 nuevas excepciones: `ItemNoEsCualitativoException` (I-M5b) e `ItemYaEvaluadoException` (I-M7).
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`:
  - Campo `_itemsEvaluados: HashSet<int>` + propiedad `ItemsEvaluados`.
  - Case `EvaluacionCualitativaRegistrada_v1` en `AplicarEvento`.
  - `Apply(EvaluacionCualitativaRegistrada_v1)` puro: añade a `_itemsEvaluados` + `Contribuyentes`.
  - `Apply(HallazgoRegistrado_v1)` extendido: propaga `EvaluacionOrigenId`.
  - Método stub `RegistrarEvaluacionCualitativa` lanza `NotImplementedException`.
  - Los dos constructores de `HallazgoRegistrado_v1` internos actualizados con `EvaluacionOrigenId: null`.
- `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` — método `RegistrarEvaluacionCualitativa` añadido.
- `tests/Inspecciones.Domain.Tests/Inspecciones/HallazgoFixtures.cs` — `EvaluacionOrigenId: null` en `HallazgoRegistradoEjemplo`.
- `tests/Inspecciones.Domain.Tests/Inspecciones/MedicionFixtures.cs` — `EvaluacionOrigenId: null` en `HallazgoMonitoreoEjemplo`.
- `tests/Inspecciones.Domain.Tests/Inspecciones/ActualizarHallazgoTests.cs` — `EvaluacionOrigenId: null` en construcción inline de `HallazgoRegistrado_v1`.
- `tests/Inspecciones.Api.Tests/EliminarHallazgoEndpointTests.cs` — `EvaluacionOrigenId: null` backward compat.
- `tests/Inspecciones.Api.Tests/AsignarRepuestoEndpointTests.cs` — `EvaluacionOrigenId: null` backward compat.

---

## 5. Qué debe hacer green

1. Implementar `Inspeccion.RegistrarEvaluacionCualitativa` con las pre-condiciones PRE-3..PRE-8 y el guard I-H1 en el orden correcto (I-M1 → I-M2 → I-M3 → I-M4 → I-M5b → I-M7 → guard I-H1 solo si Malo).
2. Emitir `EvaluacionCualitativaRegistrada_v1` (siempre) y `HallazgoRegistrado_v1` (solo si `Calificacion=Malo`) en ese orden causal.
3. `NovedadTecnica` autogenerada: `$"Estado calificado Malo en {snapshot.Parte}"` (P-6).
4. `EvaluacionOrigenId = cmd.ItemId` en el hallazgo derivado; `MedicionOrigenId = null`.
5. `Ubicacion = null` en el hallazgo (GPS no requerido — spec §9).
6. Implementar `RegistrarEvaluacionCualitativaHandler` con PRE-2 (carga aggregate → 404 si null) y `SaveChangesAsync` único.
7. Registrar endpoint `POST /api/v1/inspecciones/{id}/items/{itemId}/evaluacion` en `Inspecciones.Api`.
8. Cerrar followup #20 (`ObservacionCampo` en record `Hallazgo`) si el implementador lo decide — documentar en `green-notes.md`.
