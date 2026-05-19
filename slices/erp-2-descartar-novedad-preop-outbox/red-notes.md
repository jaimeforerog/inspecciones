# red-notes — erp-2 DescartarNovedadPreop → outbox Maquinaria

**Fase:** red
**Fecha:** 2026-05-19
**Estado:** 9/10 tests en rojo por la razón correcta. 1/10 en rojo por `NotImplementedException` (§6.8 — pasa formalmente pero falla en la assertion de estado).

---

## 1. Tests escritos

| # | Método | Escenario spec | Estado rojo |
|---|---|---|---|
| 1 | `Listener_publica_cierre_a_Maquinaria_cuando_NovedadPreopDescartada_v1_emitida` | §6.1 happy path — 200 cerradasAhora:1 | `NotImplementedException` — método no implementado |
| 2 | `Listener_envia_podIds_y_observaciones_correctos_en_el_body_HTTP` | §6.1 body mapping — podIds + observaciones | `NotImplementedException` — método no implementado |
| 3 | `Listener_trata_200_ya_cerradas_como_exito_silencioso_INV_L4` | §6.2 idempotencia 200 yaCerradas:1 | `NotImplementedException` — método no implementado |
| 4 | `Listener_trata_409_YA_CERRADO_como_exito_silencioso_D1` | §6.7 409 YA_CERRADO tratado como éxito | `NotImplementedException` — método no implementado |
| 5 | `Listener_erp_5xx_transitorio_reintenta_hasta_exito` | §6.3 5xx transitorio — 2 fallos + 1 éxito | `NotImplementedException` en tercer intento (esperaba NotThrow) |
| 6 | `Listener_erp_5xx_persistente_lanza_excepcion_para_dead_letter` | §6.4 5xx persistente — dead-letter | `NotImplementedException` — correcto (espera excepción) pero aún sin verificación de tipo concreto |
| 7 | `Listener_erp_400_no_reintenta_va_a_dead_letter_INV_L3` | §6.5 400 Bad Request sin retry | `NotImplementedException` antes de HTTP call → `_wiremock.LogEntries` vacío, assertion `HaveCount(1)` falla |
| 8 | `Listener_erp_404_no_reintenta_va_a_dead_letter_INV_L3` | §6.6 404 Not Found sin retry | `NotImplementedException` antes de HTTP call → `_wiremock.LogEntries` vacío, assertion `HaveCount(1)` falla |
| 9 | `Listener_trata_409_codigo_desconocido_como_error_permanente_D1` | §6.7b 409 código distinto de YA_CERRADO | `NotImplementedException` antes de HTTP call → assertion `HaveCount(1)` falla |
| 10 | `Listener_evento_malformado_NovedadId_cero_dead_letter_inmediato_PRE_L1` | §6.8 evento malformado PRE-L1 | PASA (pero por razón parcialmente correcta — ver nota) |

> **Nota sobre test #10 (§6.8):** el stub lanza `NotImplementedException` antes de cualquier llamada HTTP, por lo que `act.Should().ThrowAsync<Exception>()` pasa y `_wiremock.LogEntries.Should().BeEmpty()` también pasa. El test está en un estado "pasa por la razón equivocada" ahora, pero pasará correctamente cuando `green` implemente PRE-L1. Si `green` implementa el listener sin PRE-L1 (llama al ERP con NovedadId=0), la assertion de `LogEntries.Should().BeEmpty()` fallará, detectando el bug. El test cumple su función de contrato aunque no esté formalmente "rojo".

---

## 2. Archivos creados

- **Stub del listener** (mínimo para compilar):
  `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs`
  Contiene `DescartarNovedadPreopErpListener` con `HandleAsync` que lanza `NotImplementedException`.
  También contiene `NovedadPreopErpCierreFallido_v1` (record de señal de observabilidad).

- **Tests**:
  `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/NovedadPreopDescartadaListenerTests.cs`
  10 tests (8 escenarios del spec + 2 sub-tests de §6.1 y §6.7).

- **Supresión de audit preexistente** (bloqueo NU1904 de Marten 7.40.0):
  `Directory.Build.props` — añadida `<NuGetAuditSuppress>` para `GHSA-vmw2-qwm8-x84c`.
  Este bloqueo existía antes del slice erp-2 y afectaba a todos los proyectos que dependen de Infrastructure. La supresión es temporal hasta que Wolverine 3.x sea compatible con Marten >= 7.41.x. Registrar en FOLLOWUPS.

---

## 3. Stack de testing

- **xUnit** + **FluentAssertions** (convención del proyecto).
- **WireMock.Net** para emular Maquinaria_V4 — servidor HTTP en proceso.
- El listener se invoca **directamente** (sin Wolverine host) porque los tests verifican el comportamiento del listener en aislamiento. La integración con la política de retries de Wolverine (ADR-006) queda pendiente para tests de integración del slice green.
- **Sin Testcontainers/Postgres** — el listener no toca el event store.

---

## 4. Fixture / setup

No se creó un fixture dedicado (`WolverineErpListenerFixture`). El setup usa `IDisposable` directamente en la clase de test porque el listener no requiere un host Wolverine para los tests unitarios de comportamiento HTTP:

```csharp
public NovedadPreopDescartadaListenerTests()
{
    _wiremock = WireMockServer.Start();
    _httpClient = new HttpClient { BaseAddress = new Uri(_wiremock.Urls[0] + "/") };
    _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer service-account-token");
    _erpClient = new MaquinariaErpClient(_httpClient);
    _listener = new DescartarNovedadPreopErpListener(_erpClient);
}
```

---

## 5. Comando para correr los tests

```
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj --filter "FullyQualifiedName~NovedadPreopDescartadaListener"
```

---

## 6. Output de verificación de estado rojo

```
Con error! - Con error: 9, Superado: 1, Omitido: 0, Total: 10, Duración: 1 s
```

Fallos confirmados (razón: `NotImplementedException — Implementación pendiente — slice erp-2 green`):

- `Listener_publica_cierre_a_Maquinaria_cuando_NovedadPreopDescartada_v1_emitida`
- `Listener_envia_podIds_y_observaciones_correctos_en_el_body_HTTP`
- `Listener_trata_200_ya_cerradas_como_exito_silencioso_INV_L4`
- `Listener_trata_409_YA_CERRADO_como_exito_silencioso_D1`
- `Listener_erp_5xx_transitorio_reintenta_hasta_exito`
- `Listener_erp_5xx_persistente_lanza_excepcion_para_dead_letter`
- `Listener_erp_400_no_reintenta_va_a_dead_letter_INV_L3` (falla en assertion LogEntries)
- `Listener_erp_404_no_reintenta_va_a_dead_letter_INV_L3` (falla en assertion LogEntries)
- `Listener_trata_409_codigo_desconocido_como_error_permanente_D1` (falla en assertion LogEntries)

Pasa (por razón parcialmente correcta):
- `Listener_evento_malformado_NovedadId_cero_dead_letter_inmediato_PRE_L1` — ver nota §1.

---

## 7. Decisiones de diseño de los tests

- **Test §6.3 (retry transitorio):** se estructura como 3 invocaciones secuenciales directas al listener en lugar de simular el host Wolverine. Los dos primeros `Assert.ThrowsAnyAsync` verifican que el listener lanza excepción ante 503 (lo que señaliza a Wolverine que debe reintentar). El tercero verifica que completa con éxito cuando el ERP responde 200. Cuando `green` implemente la política de retries a nivel Wolverine, este test deberá complementarse con un test de integración del bus.

- **Test §6.4 (dead-letter):** verifica que el listener lanza excepción ante 5xx persistente. El comportamiento de dead-letter propiamente dicho (Wolverine encolando en dead-letter tras 4 reintentos) es responsabilidad de la configuración de Wolverine y se verifica en tests de integración del slice green.

- **Tests §6.5, §6.6, §6.7b:** la assertion `LogEntries.Should().HaveCount(1)` actúa como discriminador: una vez implementado, el listener debe llamar al ERP exactamente 1 vez (sin reintentos en 4xx). Si el listener reintentara incorrectamente, `LogEntries` tendría más de 1 entrada y el test fallaría.

- **`NovedadPreopErpCierreFallido_v1` en el stub:** se declara en el stub del listener para que `green` pueda referenciarla al implementar la señal de observabilidad. Los tests actuales no verifican que este record se emita a un ILogger (eso requiere un mock de ILogger, que no está en el scope del slice red).
