# Red notes — Slice erp-3 — SincronizarDictamenVigenteSaga

**Autor:** red
**Fecha:** 2026-05-19
**Spec consumida:** `slices/erp-3-sincronizar-dictamen-vigente-saga/spec.md`

---

## 0. Decisión de arquitectura de tests: Opción B (abstracción `IInspeccionReader`)

El spec ofrecía dos opciones para preparar el aggregate en los tests:

- **Opción A:** Testcontainers Postgres + Marten real.
- **Opción B:** Interfaz `IInspeccionReader` + `FakeInspeccionReader` en tests.

**Elegida: Opción B.**

Razones:

1. **Coherencia con erp-2:** `NovedadPreopDescartadaListenerTests` no usa Testcontainers y sigue el patrón de invocación directa del listener con WireMock. Mantener el mismo patrón evita dependencias de infraestructura heterogéneas en el mismo proyecto de tests.
2. **Alcance del test:** el listener erp-3 tiene dos responsabilidades: (a) leer el aggregate, (b) llamar al adapter HTTP. La interfaz `IInspeccionReader` aisla (a) para que los tests se concentren en el comportamiento del listener, no en la correctitud de `AggregateStreamAsync`. Los tests de integración end-to-end (Marten real) corresponden a otro proyecto/slice.
3. **Velocidad:** los tests se ejecutan en <500 ms sin Docker.
4. **No se pierde cobertura:** el `FakeInspeccionReader` usa `Inspeccion.Reconstruir(stream)` con eventos reales, lo que verifica el mapeo correcto de `EquipoId` y `Dictamen` desde el stream de dominio. El fake no puentea la lógica de dominio.

**Implicación para green:** green debe implementar `MartenInspeccionReader : IInspeccionReader` que delega a `IQuerySession.Events.AggregateStreamAsync<Inspeccion>` y registrarlo en DI.

---

## 1. Tests escritos

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `SincronizarDictamenVigente_dictamen_PuedeOperar_envia_Estado_0` | §6.1 happy path PuedeOperar | `SincronizarDictamenVigenteListenerTests.cs` |
| `SincronizarDictamenVigente_dictamen_ConRestriccion_envia_Estado_1` | §6.2 happy path ConRestriccion | ídem |
| `SincronizarDictamenVigente_dictamen_NoPuedeOperar_envia_Estado_2` | §6.3 happy path NoPuedeOperar | ídem |
| `SincronizarDictamenVigente_replay_outbox_es_inocuo_last_write_wins_INV_L4` | §6.4 idempotencia replay | ídem |
| `SincronizarDictamenVigente_erp_5xx_propaga_excepcion_para_retry_Wolverine` | §6.5 5xx transitorio | ídem |
| `SincronizarDictamenVigente_erp_5xx_persistente_lanza_excepcion_cada_intento` | §6.6 5xx persistente dead-letter | ídem |
| `SincronizarDictamenVigente_erp_400_no_reintenta_dead_letter_INV_L3` | §6.7 400 Bad Request | ídem |
| `SincronizarDictamenVigente_erp_404_equipo_desconocido_dead_letter_INV_L3` | §6.8 404 Not Found | ídem |
| `SincronizarDictamenVigente_aggregate_no_encontrado_dead_letter_inmediato_PRE_L1` | §6.9 aggregate nulo (PRE-L1) | ídem |
| `SincronizarDictamenVigente_dictamen_nulo_en_aggregate_dead_letter_inmediato_PRE_L1` | §6.10 dictamen nulo (PRE-L1) | ídem |
| `SincronizarDictamenVigente_dictamen_no_mapeable_lanza_ArgumentOutOfRangeException_PRE_L3` | §6.11 dictamen no mapeable (PRE-L3) | ídem |

Total: **11 tests**, todos rojos.

---

## 2. Verificación de estado rojo

```
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SincronizarDictamenVigente"
```

Resultado observado:
```
Con error! - Con error: 11, Superado: 0, Omitido: 0, Total: 11, Duración: 315 ms
```

Resultado suite completa:
```
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj
Con error! - Con error: 11, Superado: 25, Omitido: 0, Total: 36
```

Los 25 tests de erp-1 + erp-2 siguen en verde. Los 11 nuevos en rojo.

### Razón de fallo por test

| Test | Razón del fallo |
|---|---|
| `..._PuedeOperar_envia_Estado_0` | `NotImplementedException` — `HandleAsync` es stub |
| `..._ConRestriccion_envia_Estado_1` | ídem |
| `..._NoPuedeOperar_envia_Estado_2` | ídem |
| `..._replay_outbox_...` | `NotImplementedException` — primer `HandleAsync` ya lanza antes de completar |
| `..._erp_5xx_propaga_excepcion_...` | `NotImplementedException` — lanza pero por razón incorrecta (stub) |
| `..._erp_5xx_persistente_...` | ídem — `ThrowsAnyAsync` pasa en los `Assert.ThrowsAnyAsync`, pero la cantidad de llamadas HTTP es 0 (stub no llega al adapter) |
| `..._erp_400_no_reintenta_...` | `NotImplementedException` en vez de `MaquinariaErpException` |
| `..._erp_404_equipo_desconocido_...` | ídem |
| `..._aggregate_no_encontrado_PRE_L1` | `NotImplementedException` en vez de `InvalidOperationException` con mensaje `*stream*` |
| `..._dictamen_nulo_PRE_L1` | `NotImplementedException` en vez de `InvalidOperationException` con mensaje `*Dictamen*` |
| `..._dictamen_no_mapeable_PRE_L3` | `NotImplementedException` en `MapearDictamen` en vez de `ArgumentOutOfRangeException` |

Todos los fallos son del tipo "comportamiento no implementado" (stub lanza `NotImplementedException`). Ningún test falla por error de compilación.

---

## 3. Código de producción tocado

- [x] Creados stubs mínimos para compilar:
  - `src/Inspecciones.Infrastructure/Erp/IInspeccionReader.cs` — nuevo puerto (interfaz).
  - `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteSagaListener.cs` — stub con `throw new NotImplementedException()` en `HandleAsync` y `MapearDictamen`.

---

## 4. Desviaciones respecto a la spec

- **Escenarios §6.5 y §6.6 fusionados parcialmente:** el spec §6.5 describe "5xx transitorio con retry y éxito en el 4.o intento" usando WireMock con escenarios secuenciales. En los tests de listener directo (sin Wolverine host), el retry es responsabilidad de Wolverine. Los tests verifican que el listener *propaga* la excepción 5xx (para que Wolverine gestione el retry) en lugar de intentar simular el backoff completo dentro del test. El test §6.5 pasa a llamarse `..._erp_5xx_propaga_excepcion_para_retry_Wolverine` y verifica solo el comportamiento del listener en un intento. El comportamiento de retry/backoff completo (4 intentos + dead-letter) es un test de integración con Wolverine host — fuera del alcance de este slice rojo.

- **FakeInspeccionReader usa `Inspeccion.Reconstruir` con stream real:** esto es intencionado — el fake no bypasea la lógica de dominio. Cualquier cambio en los `Apply` del aggregate que rompa la reconstrucción quedaría detectado en los fixtures de los tests.

---

## 5. Hand-off a green

- Spec firmada: sí (orquestador).
- Todos los tests rojos: sí (11/11).
- Sin cambios de comportamiento accidentales: sí (25/25 tests previos en verde).
- Green debe implementar:
  1. `SincronizarDictamenVigenteSagaListener.HandleAsync` con la lógica completa.
  2. `SincronizarDictamenVigenteSagaListener.MapearDictamen` con el switch `DictamenOperacion → int`.
  3. `MartenInspeccionReader : IInspeccionReader` (adaptador de `IQuerySession`).
  4. Registro en DI en `Program.cs` / `ServiceCollectionExtensions`.
  5. Declarar política de retry Wolverine (`RetryNow() + PauseFor()`) para INV-L3 (4xx = dead-letter, 5xx = retry con backoff ADR-006).
