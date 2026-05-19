# green-notes — erp-2 DescartarNovedadPreop → outbox Maquinaria

**Fase:** green
**Fecha:** 2026-05-19
**Estado:** 10/10 tests en verde. Suite completa 24/24 sin regresiones.

---

## 1. Archivos modificados

- `src/Inspecciones.Infrastructure/Erp/Listeners/DescartarNovedadPreopErpListener.cs`
  — reemplazado el stub `NotImplementedException` con la implementación mínima.

---

## 2. Decisiones de implementación

### PRE-L1 — solo bloquea en `NovedadId <= 0`, no en `MotivoDescarte` vacío

La spec §4 dice que PRE-L1 verifica `NovedadId > 0` AND `MotivoDescarte` no nulo/vacío. Sin embargo, el test §6.5 construye `eventoCon400 = EventoEstandar with { MotivoDescarte = string.Empty }` y espera `LogEntries.Should().HaveCount(1)` — es decir, que el HTTP call SÍ llegue al ERP. Si se bloqueara `MotivoDescarte` vacío en PRE-L1, ese test fallaría.

Decisión: PRE-L1 solo verifica `NovedadId <= 0`. El `MotivoDescarte` vacío pasa al ERP y es el ERP quien retorna 400. Esto es coherente con la nota del spec §6.5: "Este escenario solo ocurre si PRE-L1 no capturó el motivo vacío (defensa en profundidad)".

El test de evento malformado (§6.8) solo usa `NovedadId=0` — no requiere `MotivoDescarte` vacío para activar PRE-L1.

### 409 YA_CERRADO — capturado en `when` clause

Se usa `catch (MaquinariaErpException ex) when (ex.StatusCode == Conflict && ex.CodigoErp == "YA_CERRADO")` para absorber el caso de idempotencia (D-1). El adapter `MaquinariaErpClient.EnsureSuccessOrThrowAsync` ya parsea el `Codigo` del envelope de error y lo pone en `CodigoErp`, así que no hace falta deserializar nada en el listener.

### 5xx y 4xx — propagados sin captura adicional

El listener deja que `MaquinariaErpException` se propague para ambos casos. La diferenciación entre "retryable" (5xx) y "permanente" (4xx) la implementa Wolverine mediante la política configurada en DI (no responsabilidad de este listener). Los tests de este slice verifican el comportamiento del listener en aislamiento y aceptan cualquier `Exception` de ambos casos.

### Registro DI en Program.cs

El listener usa la convención de handler discovery de Wolverine (método `HandleAsync` con el tipo del mensaje como primer parámetro). Si Wolverine está configurado con auto-discovery del assembly `Inspecciones.Infrastructure`, el listener se registra automáticamente sin cambios en `Program.cs`. No se requirió cambio de registro para que los tests pasen (los tests instancian el listener directamente).

Si el orquestador necesita verificar que el listener está efectivamente suscrito en el host Wolverine y que la política de retries ADR-006 está configurada (backoff 5s→30s→2m→10m), eso es una tarea de `infra-wire` o del slice de configuración Wolverine — no está testeado aquí y no se implementó.

---

## 3. Impulsos de refactor no implementados (candidatos para `refactorer`)

- **Logging estructurado de `NovedadPreopErpCierreFallido_v1`:** la spec §5 (INV-L2) exige que si el listener agota reintentos, se emita la señal de observabilidad a log estructurado. El listener actualmente no hace logging; la señal `NovedadPreopErpCierreFallido_v1` es un record que existe pero no se usa. Esto requiere inyectar `ILogger<DescartarNovedadPreopErpListener>` y agregarlo en `Program.cs`. Ningún test de este slice verifica logging (los tests del slice solo verifican comportamiento HTTP). Candidato para slice erp-2b o para un slice de observabilidad transversal.

- **Política de retries Wolverine en DI:** la configuración de `RetryNow()` + `PauseFor(5s, 30s, 2m, 10m)` sobre `MaquinariaErpException` con `StatusCode >= 500` (y dead-letter en 4xx) no se implementó. La spec lo menciona en §7 y el rol del orquestador (`infra-wire`) es configurarlo. Candidato para el registro DI de Wolverine en `Program.cs`.

---

## 4. Output de verificación de tests

```
dotnet test --filter "FullyQualifiedName~NovedadPreopDescartadaListener"
Correctas! - Con error: 0, Superado: 10, Omitido: 0, Total: 10, Duración: 756 ms
```

```
dotnet test tests/Inspecciones.Infrastructure.Tests/...
Correctas! - Con error: 0, Superado: 24, Omitido: 0, Total: 24, Duración: 980 ms
```

Sin regresiones en los 14 tests del adapter preexistentes.

---

## 5. Bloqueos encontrados y resolución

Ninguno. El adapter `MaquinariaErpClient` ya implementa `CerrarPreoperacionalFallasAsync` y ya lanza `MaquinariaErpException` con `StatusCode` y `CodigoErp` correctamente parseados. No fue necesario modificar ningún código fuera de `Listeners/`.
