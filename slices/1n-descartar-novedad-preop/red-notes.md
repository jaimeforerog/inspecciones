# Red Notes — Slice 1n: DescartarNovedadPreop

**Fase:** red
**Fecha:** 2026-05-11
**Estado:** Rojo confirmado — 12 Domain + 6 Api fallan por la razón correcta.

---

## 1. Tests escritos

### Nivel Domain (`tests/Inspecciones.Domain.Tests/Inspecciones/DescartarNovedadPreopTests.cs`)

| # | Test | Escenario spec | Razón de fallo esperada |
|---|------|----------------|-------------------------|
| 1 | `DescartarNovedadPreop_en_inspeccion_en_ejecucion_emite_NovedadPreopDescartada_v1` | §6.1 | `NotImplementedException` en `Inspeccion.Descartar` |
| 2 | `DescartarNovedadPreop_happy_path_payload_del_evento_es_correcto` | §6.1 | `NotImplementedException` en `Inspeccion.Descartar` |
| 3 | `DescartarNovedadPreop_happy_path_estado_permanece_EnEjecucion_D5` | §6.1 + D-5 | `NotImplementedException` en `Inspeccion.Descartar` |
| 4 | `DescartarNovedadPreop_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_PRE2` | §6.2 | `NotImplementedException` en vez de `InspeccionNoEnEjecucionException` |
| 5 | `DescartarNovedadPreop_en_inspeccion_cancelada_lanza_InspeccionNoEnEjecucionException_PRE2` | §6.3 | `NotImplementedException` en vez de `InspeccionNoEnEjecucionException` |
| 6 | `DescartarNovedadPreop_novedad_ya_descartada_lanza_NovedadYaDescartadaException_PRE5` | §6.4 | `NotImplementedException` en vez de `NovedadYaDescartadaException` |
| 7 | `DescartarNovedadPreop_novedad_distinta_no_falla_por_PRE5` | §6.4 borde | `NotImplementedException` en `Inspeccion.Descartar` |
| 8 | `DescartarNovedadPreop_novedad_ya_importada_como_hallazgo_lanza_NovedadYaConvertidaEnHallazgoException_PRE6` | §6.5 | `NotImplementedException` en vez de `NovedadYaConvertidaEnHallazgoException` |
| 9 | `DescartarNovedadPreop_hallazgo_preopcional_con_otra_novedad_no_falla_por_PRE6` | §6.5 borde | `NotImplementedException` en `Inspeccion.Descartar` |
| 10 | `DescartarNovedadPreop_rebuild_desde_stream_reproduce_estado` | §6.9 (obligatorio) | **PASA** — `Apply` puro ya operativo |
| 11 | `DescartarNovedadPreop_rebuild_desde_stream_completo_previos_mas_emitidos` | §6.9 derivado | `NotImplementedException` en `Inspeccion.Descartar` |
| 12 | `DescartarNovedadPreop_motivo_autogenerado_sigue_plantilla_D4_exacta` | §6.10 | `NotImplementedException` en `Inspeccion.Descartar` |
| 13 | `DescartarNovedadPreop_agregado_al_set_de_contribuyentes_I2b` | §6.1 + I2b | `NotImplementedException` en `Inspeccion.Descartar` |

**Skips (3):**
- `DescartarNovedadPreop_novedad_no_pertenece_a_la_inspeccion_lanza_DomainException_PRE7` — D-2 opción A (no se trackea `_novedadesImportadas`)
- `DescartarNovedadPreop_sin_capability_ejecutar_inspeccion_lanza_403_PRE4` — capa HTTP
- `DescartarNovedadPreop_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException_PRE1` — requiere Marten

### Nivel Api/E2E (`tests/Inspecciones.Api.Tests/DescartarNovedadPreopEndpointTests.cs`)

| # | Test | Escenario spec | Razón de fallo esperada |
|---|------|----------------|-------------------------|
| 1 | `POST_descartar_novedad_preop_happy_path_responde_200_OK` | §6.1 | `500` (handler `NotImplementedException`) |
| 2 | `POST_descartar_novedad_inspeccion_firmada_responde_422` | §6.2 | `500` (handler `NotImplementedException`) |
| 3 | `POST_descartar_novedad_inspeccion_cancelada_responde_422` | §6.3 | `500` (handler `NotImplementedException`) |
| 4 | `POST_descartar_novedad_ya_descartada_responde_422_PRE5` | §6.4 | `500` (handler `NotImplementedException`) |
| 5 | `POST_descartar_novedad_ya_importada_como_hallazgo_responde_422_PRE6` | §6.5 | `500` (handler `NotImplementedException`) |
| 6 | `POST_descartar_novedad_sin_capability_responde_403` | §6.7 | **PASA** — verificado antes del handler |
| 7 | `POST_descartar_novedad_inspeccion_inexistente_responde_404` | §6.8 | `500` (handler `NotImplementedException`) |
| 8 | `POST_descartar_novedad_sin_header_X_Client_Command_Id_responde_400` | Header | **PASA** — verificado antes del handler |

**Skips (1):**
- `POST_descartar_novedad_replay_mismo_ClientCommandId_no_duplica_eventos_ADR008` — §6.14 requiere Wolverine envelope dedup

---

## 2. Stubs creados para compilar

| Archivo | Tipo | Descripción |
|---------|------|-------------|
| `src/Inspecciones.Domain/Inspecciones/NovedadPreopDescartada_v1.cs` | Record completo | Evento canónico — shape definitivo |
| `src/Inspecciones.Domain/Inspecciones/DescartarNovedadPreop.cs` | Record completo | Comando — shape definitivo |
| `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | Excepciones nuevas | `NovedadYaDescartadaException`, `NovedadYaConvertidaEnHallazgoException` |
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Modificado | Campo `_novedadesDescartadas`, `Apply(NovedadPreopDescartada_v1)` (puro), `Descartar` stub |
| `src/Inspecciones.Application/Inspecciones/DescartarNovedadPreopHandler.cs` | Stub | `throw new NotImplementedException()` |
| `src/Inspecciones.Application/Inspecciones/DescartarNovedadPreopResult.cs` | Record completo | Result shape definitivo |
| `src/Inspecciones.Api/Inspecciones/DescartarNovedadPreopRequest.cs` | Record completo | DTO de request |
| `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Modificado | Endpoint stub `POST .../novedades-preop/{novedadId}/descartar` |
| `src/Inspecciones.Api/Program.cs` | Modificado | Registro DI `DescartarNovedadPreopHandler` |

---

## 3. Fixtures creados

| Archivo | Descripción |
|---------|-------------|
| `tests/Inspecciones.Domain.Tests/Inspecciones/DescartarNovedadPreopFixtures.cs` | `StreamEnEjecucionBase`, `StreamConNovedadYaDescartada`, `StreamConHallazgoPreopConNovedadImportada`, `ComandoDescartarNovedad` |

---

## 4. Comandos de verificación

```powershell
# Domain tests (sin Postgres)
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test"
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --filter "FullyQualifiedName~DescartarNovedadPreop"

# Resultado: Con error: 12, Superado: 1, Omitido: 3, Total: 16

# Api/E2E tests (requiere Postgres local)
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj --filter "FullyQualifiedName~DescartarNovedadPreop"

# Resultado: Con error: 6, Superado: 2, Omitido: 1, Total: 9

# Todos los Domain tests — verificar sin regresión
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj

# Resultado: Con error: 12, Superado: 214, Omitido: 18, Total: 244
```

---

## 5. Razón de fallo por test

### Domain tests — todos fallan con `NotImplementedException`

Los 12 tests que fallan lanzan `NotImplementedException` desde `Inspeccion.Descartar`. El mensaje es:
```
"Inspeccion.Descartar no implementado — pendiente fase green del slice 1n."
```

Esto es el estado rojo correcto. Los tests de excepción (`PRE-2`, `PRE-5`, `PRE-6`) fallan porque se espera un tipo específico de excepción pero se recibe `NotImplementedException`.

### Domain — test que pasa (rebuild)

`DescartarNovedadPreop_rebuild_desde_stream_reproduce_estado` pasa porque `Apply(NovedadPreopDescartada_v1)` ya es puro y operativo. Este test garantiza que el `Apply` no tiene validaciones intrusas.

### Api tests — fallan con HTTP 500

El endpoint llega al `handler.Handle(cmd, ct)` que lanza `NotImplementedException`. El middleware convierte eso en `500 Internal Server Error`. Los dos tests que pasan (`403` y `400`) se verifican antes de llegar al handler.

---

## 6. Sin regresiones

Los 214 tests del Domain que pasaban antes siguen pasando. Solo los 12 nuevos del slice 1n fallan (todos por `NotImplementedException` — estado rojo válido).

---

## 7. Notas de diseño para green

- `Inspeccion.Descartar` necesita: PRE-2 (estado `EnEjecucion`), PRE-5 (`_novedadesDescartadas.Contains(cmd.NovedadId)`), PRE-6 (buscar hallazgo con `Origen=PreOperacional` y `NovedadPreopOrigenId == cmd.NovedadId`), luego emitir `NovedadPreopDescartada_v1`.
- `_novedadesDescartadas` y `Apply` ya están implementados y puros.
- P-3 (simetría INV-ND1 en `RegistrarHallazgo`): verificar que `RegistrarHallazgo` también chequee `_novedadesDescartadas` antes de aceptar `Origen=PreOperacional`. El spec recomienda implementar esto en el mismo PR. Ver spec §12 P-3.
