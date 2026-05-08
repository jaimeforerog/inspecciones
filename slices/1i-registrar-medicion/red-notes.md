# red-notes — Slice 1i: RegistrarMedicion

**Agente:** red
**Fecha:** 2026-05-08
**Estado:** rojo verificado

---

## 1. Tests escritos

### Proyecto: `tests/Inspecciones.Domain.Tests` — `RegistrarMedicionTests.cs`

| # | Nombre del test | Escenario spec | Invariante |
|---|---|---|---|
| 1 | `RegistrarMedicion_dentro_del_rango_emite_un_solo_MedicionRegistrada_v1` | §6.1 | Happy path — dentro |
| 2 | `RegistrarMedicion_dentro_del_rango_payload_correcto_y_no_emite_HallazgoRegistrado_v1` | §6.1 | Happy path — dentro |
| 3 | `RegistrarMedicion_dentro_del_rango_agrega_item_a_itemsMedidos` | §6.1 | `_itemsMedidos` state |
| 4 | `RegistrarMedicion_fuera_de_rango_por_debajo_emite_MedicionRegistrada_v1_y_HallazgoRegistrado_v1` | §6.2 | Happy path — fuera |
| 5 | `RegistrarMedicion_fuera_de_rango_por_debajo_MedicionRegistrada_v1_con_timestamp_correcto` | §6.2 | Payload MedicionRegistrada_v1 |
| 6 | `RegistrarMedicion_fuera_de_rango_por_debajo_HallazgoRegistrado_v1_con_payload_correcto` | §6.2 | Payload HallazgoRegistrado_v1 + MedicionOrigenId |
| 7 | `RegistrarMedicion_fuera_de_rango_por_debajo_aggregate_tiene_hallazgo_monitoreo_activo` | §6.2 | State tras rebuild |
| 8 | `RegistrarMedicion_fuera_de_rango_por_encima_emite_dos_eventos_con_FueraDeRango_true` | §6.3 | Fuera por encima |
| 9 | `RegistrarMedicion_en_borde_inferior_ValorMin_emite_FueraDeRango_false` | §6.4 | P-2 rango cerrado `[min, max]` |
| 10 | `RegistrarMedicion_en_borde_superior_ValorMax_emite_FueraDeRango_false` | §6.4 | P-2 borde superior |
| 11 | `RegistrarMedicion_en_inspeccion_tecnica_lanza_InspeccionNoEsMonitoreoException_I_M1` | §6.5 | PRE-3 / I-M1 |
| 12 | `RegistrarMedicion_en_inspeccion_monitoreo_firmada_lanza_InspeccionNoEnEjecucionException_I_M2` | §6.6 | PRE-4 / I-M2 |
| 13 | `RegistrarMedicion_con_ItemId_inexistente_en_snapshot_lanza_ItemNoEncontradoEnSnapshotException_I_M3` | §6.7 | PRE-5 / I-M3 |
| 14 | `RegistrarMedicion_en_item_omitido_lanza_ItemOmitidoNoPuedeMedirseException_I_M4` | §6.8 | PRE-6 / I-M4 |
| 15 | `RegistrarMedicion_en_item_cualitativo_lanza_ItemNoEsNumericoException_I_M5` | §6.9 | PRE-7 / I-M5 |
| 16 | `RegistrarMedicion_segunda_vez_el_mismo_item_lanza_ItemYaMedidoException_I_M6` | §6.10 | PRE-8 / I-M6 |
| 17 | `RegistrarMedicion_SaveChangesAsync_falla_no_persiste_ningun_evento` | §6.14 | Atomicidad (Skip) |
| 18 | `RegistrarMedicion_segundo_item_fuera_de_rango_coexiste_con_hallazgo_previo` | §6.13 | I-H6 multiplicidad |
| 19 | `RegistrarMedicion_segundo_item_fuera_de_rango_aggregate_tiene_dos_hallazgos_activos` | §6.13 | State tras rebuild |
| 20 | `RegistrarMedicion_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones` | §6.15 | Rebuild obligatorio |
| 21 | `RegistrarMedicion_rebuild_dentro_del_rango_no_tiene_hallazgos` | §6.15 | Rebuild complementario |

### Proyecto: `tests/Inspecciones.Application.Tests` — `RegistrarMedicionHandlerTests.cs`

| # | Nombre del test | Escenario spec | Invariante |
|---|---|---|---|
| 22 | `RegistrarMedicion_con_InspeccionId_inexistente_lanza_InspeccionNoEncontradaException` | §6.11 | PRE-2 (handler) |
| 23 | `RegistrarMedicion_replay_mismo_clientCommandId_no_re_ejecuta_handler` | §6.12 | Idempotencia Wolverine (Skip) |
| 24 | `RegistrarMedicion_fuera_de_rango_evento_previo_en_stream_antes_de_commit_no_visible` | §6.14 | Atomicidad sin SaveChangesAsync |

---

## 2. Comando de verificación

```bash
dotnet test tests/Inspecciones.Domain.Tests/ --filter "FullyQualifiedName~RegistrarMedicion" --no-build
```

Resultado confirmado:
```
Con error! - Con error: 20, Superado: 0, Omitido: 1, Total: 21, Duración: 754 ms
```

```bash
dotnet test tests/Inspecciones.Application.Tests/ --filter "FullyQualifiedName~RegistrarMedicion" --no-build
```

(Requiere Docker/Testcontainers para §6.11 y §6.14. §6.12 es Skip.)

---

## 3. Razón del fallo de cada test

Todos los tests de dominio (21 del Domain.Tests) fallan con `NotImplementedException`:

```
System.NotImplementedException: Slice 1i — RegistrarMedicion no implementado.
El green debe implementar este método.
   at Inspeccion.RegistrarMedicion(RegistrarMedicion cmd, DateTimeOffset ahora)
```

- Tests de happy path (§6.1, §6.2, §6.3, §6.4, §6.13, §6.15): fallan porque `RegistrarMedicion` lanza `NotImplementedException` en lugar de emitir los eventos esperados.
- Tests de invariante violada (§6.5, §6.6, §6.7, §6.8, §6.9, §6.10): fallan porque `RegistrarMedicion` lanza `NotImplementedException` en lugar de la excepción de dominio esperada (`InspeccionNoEsMonitoreoException`, `InspeccionNoEnEjecucionException`, `ItemNoEncontradoEnSnapshotException`, `ItemOmitidoNoPuedeMedirseException`, `ItemNoEsNumericoException`, `ItemYaMedidoException`).
- Test §6.14 (`atomicidad`): Skip con razón documentada — test de integración Application.Tests.

---

## 4. Escenarios cubiertos y sus invariantes

| Escenario spec | Invariante | Cobertura |
|---|---|---|
| §6.1 | Happy path dentro del rango, 1 evento | 3 tests (payload + state) |
| §6.2 | Happy path fuera del rango por debajo, 2 eventos | 4 tests (payload M + H + state) |
| §6.3 | Happy path fuera del rango por encima | 1 test |
| §6.4 | Borde inclusivo rango cerrado [min, max] (P-2) | 2 tests (ValorMin + ValorMax) |
| §6.5 | PRE-3 / I-M1 inspección Tecnica rechaza medición | 1 test |
| §6.6 | PRE-4 / I-M2 inspección Firmada rechaza medición | 1 test |
| §6.7 | PRE-5 / I-M3 ItemId inexistente en snapshot | 1 test |
| §6.8 | PRE-6 / I-M4 ítem previamente omitido | 1 test |
| §6.9 | PRE-7 / I-M5 ítem cualitativo rechaza medición | 1 test |
| §6.10 | PRE-8 / I-M6 doble medición del mismo ítem | 1 test |
| §6.11 | PRE-2 handler InspeccionId no existe | 1 test (Application.Tests) |
| §6.12 | Idempotencia Wolverine | 1 test (Skip — requiere Wolverine) |
| §6.13 | I-H6 múltiples ítems fuera de rango, hallazgos coexisten | 2 tests |
| §6.14 | Atomicidad SaveChangesAsync | 1 test Skip (Domain) + 1 test (Application.Tests) |
| §6.15 | Rebuild desde stream, Apply puro, orden causal | 2 tests (fuera de rango + dentro) |

---

## 5. Stubs de producción creados (mínimos para compilar)

Los siguientes archivos se crearon/modificaron en producción como stubs mínimos para que los tests compilen. Ninguno implementa lógica de negocio — el green los completa:

### Nuevos en `src/Inspecciones.Domain/Inspecciones/`

- `RegistrarMedicion.cs` — command record (stub mínimo).
- `MedicionRegistrada_v1.cs` — evento (stub mínimo con shape correcto).
- `ItemMonitoreoOmitido_v1.cs` — evento de omisión (stub mínimo para PRE-6 / I-M4).

### Nuevos en `src/Inspecciones.Application/Inspecciones/`

- `RegistrarMedicionHandler.cs` — handler stub. PRE-2 implementado; método de decisión lanza `NotImplementedException` después de la carga del aggregate.
- `RegistrarMedicionResult.cs` — result record stub.

### Modificados en `src/Inspecciones.Domain/Inspecciones/`

- `Excepciones.cs` — 5 nuevas excepciones: `InspeccionNoEsMonitoreoException`, `ItemNoEncontradoEnSnapshotException`, `ItemOmitidoNoPuedeMedirseException`, `ItemNoEsNumericoException`, `ItemYaMedidoException`.
- `HallazgoRegistrado_v1.cs` — campo `MedicionOrigenId: int?` insertado antes de `ParteEquipoId` (backward compat — callers existentes pasan `null` explícito).
- `ItemRutinaMonitoreoSnapshot.cs` — campo `ParteEquipoId: int?` añadido con default `null` (P-1 opción A, backward compat).
- `Hallazgo.cs` — campo `MedicionOrigenId: int?` añadido con default `null`.
- `Inspeccion.cs` — añadidos:
  - Propiedades `ItemsMedidos: IReadOnlySet<int>` e `ItemsOmitidos: IReadOnlySet<int>` con backing `HashSet<int>`.
  - Método `RegistrarMedicion(cmd, ahora)` — stub con `throw new NotImplementedException()`.
  - `Apply(MedicionRegistrada_v1 e)` — actualiza `_itemsMedidos` y `_contribuyentes` (puro).
  - `Apply(ItemMonitoreoOmitido_v1 e)` — actualiza `_itemsOmitidos` y `_contribuyentes` (puro).
  - Ramas `case MedicionRegistrada_v1` y `case ItemMonitoreoOmitido_v1` en `AplicarEvento`.
  - `Apply(HallazgoRegistrado_v1 e)` — propagación de `MedicionOrigenId` al record `Hallazgo`.

### Incidentally modificados (callers de `HallazgoRegistrado_v1` que deben pasar `MedicionOrigenId: null`)

- `tests/Inspecciones.Domain.Tests/Inspecciones/HallazgoFixtures.cs` — `HallazgoRegistradoEjemplo` (añade `MedicionOrigenId: null`).
- `tests/Inspecciones.Domain.Tests/Inspecciones/ActualizarHallazgoTests.cs` — instancia directa en test §6.11 (añade `MedicionOrigenId: null`).
- `tests/Inspecciones.Api.Tests/AsignarRepuestoEndpointTests.cs` — instancia directa (añade `MedicionOrigenId: null`).
- `tests/Inspecciones.Api.Tests/EliminarHallazgoEndpointTests.cs` — instancia directa (añade `MedicionOrigenId: null`).

---

## 6. Notas para el green

### Qué implementar en `Inspeccion.RegistrarMedicion`

1. **PRE-3 / I-M1:** `if (Tipo != TipoInspeccion.Monitoreo) throw new InspeccionNoEsMonitoreoException(...)`.
2. **PRE-4 / I-M2:** `if (Estado != EstadoInspeccion.EnEjecucion) throw new InspeccionNoEnEjecucionException(...)`.
3. **PRE-5 / I-M3:** buscar `ItemsSnapshot.FirstOrDefault(i => i.ItemId == cmd.ItemId)`. Si `null` → `ItemNoEncontradoEnSnapshotException`.
4. **PRE-6 / I-M4:** `if (_itemsOmitidos.Contains(cmd.ItemId)) throw new ItemOmitidoNoPuedeMedirseException(...)`.
5. **PRE-7 / I-M5:** `if (snapshot.Evaluacion is not MedicionEsperada) throw new ItemNoEsNumericoException(...)`.
6. **PRE-8 / I-M6:** `if (_itemsMedidos.Contains(cmd.ItemId)) throw new ItemYaMedidoException(...)`.
7. **Cálculo FueraDeRango (P-2):** `medicion.ValorMedido < medicion.ValorMinimo || medicion.ValorMedido > medicion.ValorMaximo`. Rango cerrado `[min, max]`.
8. **Emisión de eventos:**
   - Siempre: `MedicionRegistrada_v1(...)`.
   - Si `FueraDeRango`: también `HallazgoRegistrado_v1(Origen=Monitoreo, MedicionOrigenId=cmd.ItemId, ParteEquipoId=snapshot.ParteEquipoId ?? 0, AccionRequerida=RequiereSeguimiento, NovedadTecnica=$"{magnitud} {valorMedido}{unidad} fuera de rango esperado [{min}, {max}]", ...)`.
9. **NovedadTecnica autogenerada:** format `"Voltaje 10.2V fuera de rango esperado [12.3, 12.5]"` — usar magnitud, unidad, ValorMedido, ValorMinimo, ValorMaximo del snapshot.
10. **Orden causal:** `MedicionRegistrada_v1` PRIMERO, `HallazgoRegistrado_v1` SEGUNDO (spec §3).

### Nota sobre `MedicionEsperada` campos

El record existente usa `ValorMinimo`/`ValorMaximo` (no `ValorMin`/`ValorMax` del spec). El green usa los nombres del código (`ValorMinimo`, `ValorMaximo`).

### Nota sobre §6.12 (idempotencia Wolverine)

El test está marcado Skip. El green NO lo implementa — queda para el test E2E con `WebApplicationFactory`. La idempotencia natural por I-M6 (test §6.10) es suficiente para la cobertura de dominio.

### Nota sobre `_itemsOmitidos` y `Apply(ItemMonitoreoOmitido_v1)`

El `Apply(ItemMonitoreoOmitido_v1 e)` ya existe como stub en este slice. El slice `OmitirItemMonitoreo` lo completará. Los tests del slice 1i que usan `StreamMonitoreoConItemOmitido` pasan el evento directamente al stream de Given — el Apply puro del stub maneja esto correctamente.

### Nota sobre `ParteEquipoId` nullable (P-1)

`snapshot.ParteEquipoId` es `int?`. Cuando es `null` (streams del slice 1h sin el campo), el green debe decidir cómo manejar el `ParteEquipoId` en `HallazgoRegistrado_v1` (que requiere `int`, no `int?`). Opciones: usar `snapshot.ParteEquipoId ?? 0` (workaround temporal) o lanzar `InspeccionDomainException` si es null. Documentar en `green-notes.md`. El test `§6.2 HallazgoRegistrado_v1_con_payload_correcto` verifica que `ParteEquipoId` viene del snapshot (asume snapshot con `ParteEquipoId=88`).

---

## 7. Tests de otros slices que no se modificaron en lógica

Los cambios a `HallazgoFixtures.cs`, `ActualizarHallazgoTests.cs`, `AsignarRepuestoEndpointTests.cs` y `EliminarHallazgoEndpointTests.cs` son estrictamente incidentales: se añade `MedicionOrigenId: null` a instancias directas del record `HallazgoRegistrado_v1` para que sigan compilando tras la extensión del record. No se alteró ninguna aserción ni lógica de test.

Verificado: los 103 tests previos de `Inspecciones.Domain.Tests` siguen pasando con 0 fallos.

---

## 8. Iteración correctiva — 2026-05-08

**Disparador:** reviewer devolvió `request-changes` con un único blocker: la rama `Inspeccion.cs:889-893` (guard `ParteEquipoIdAusenteEnSnapshotException` cuando `FueraDeRango=true && snapshot.ParteEquipoId is null`) no tenía test.

**Test añadido:**

| # | Nombre del test | Rama cubierta |
|---|---|---|
| 22 | `RegistrarMedicion_fuera_de_rango_con_ParteEquipoId_nulo_en_snapshot_lanza_ParteEquipoIdAusenteEnSnapshotException` | `Inspeccion.cs:889-893` guard I-H1 |

**Comportamiento al ejecutar:** el test PASA en el primer run (auto-verde). Esto es esperado en una iteración correctiva post-review: la implementación del guard existía pero la rama nunca se ejercía desde los tests. La rama ahora está cubierta.

**Nit corregido:** docstring obsoleto en `Apply(MedicionRegistrada_v1)` (`Inspeccion.cs:939`) eliminado: frase "Stub mínimo fase red — el green completa la mutación de estado." reemplazada por docstring actualizado sin referencia a la fase red.

**Verificación post-iteración:**

```
dotnet build tests/Inspecciones.Domain.Tests/ → 0 Advertencias, 0 Errores
dotnet test tests/Inspecciones.Domain.Tests/ → Superado: 125, Omitido: 1, Con error: 0, Total: 126
```

Diferencia respecto a la baseline del review (124 pasados → 125 pasados): +1 test nuevo, sin regresiones.
