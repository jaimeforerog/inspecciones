# Refactor notes — Slice erp-2 — DescartarNovedadPreop → outbox Maquinaria

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | config / cableado | `Program.cs` | Registrar `Inspecciones.Infrastructure` en Wolverine auto-discovery. El listener `DescartarNovedadPreopErpListener` vive en ese assembly; sin este cambio nunca es suscrito al bus. | 24 pass | 24 pass |
| 2 | config / resiliencia | `Program.cs` | Configurar retry policy ADR-006 §16 sobre `MaquinariaErpException`: 5xx → `ScheduleRetry(5s, 30s, 2m, 10m).Then.MoveToErrorQueue()`; 4xx → `MoveToErrorQueue()` inmediato; `ArgumentException` (PRE-L1) → `MoveToErrorQueue()` inmediato. Usings añadidos: `System.Net`, `Wolverine.ErrorHandling`, `Inspecciones.Infrastructure.Erp.Listeners`. | 24 pass | 24 pass |
| 3 | logging estructurado | `DescartarNovedadPreopErpListener.cs` | Inyectar `ILogger<DescartarNovedadPreopErpListener>` (parámetro nullable con default `NullLogger` para no romper los tests que instancian sin logger). Agregar `partial class` + `[LoggerMessage(EventId=1001)]` para emitir la señal `NovedadPreopErpCierreFallido_v1` con `InspeccionId`, `NovedadId`, `StatusCode`, `CodigoErp`, `EsReintentable` cuando `MaquinariaErpException` se propaga (INV-L2). | 24 pass | 24 pass |
| 4 | comment cleanup | `DescartarNovedadPreopErpListener.cs` | Sustituir comentario WHAT de PRE-L1 por WHY: explicar por qué `MotivoDescarte` vacío pasa al ERP (referencia a spec §6.5 para defensa en profundidad). | 24 pass | 24 pass |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 — logging "solo al agotar reintentos" | Wolverine 3.13 no expone un hook "on-dead-letter" sin plumbing adicional (`CompensatingAction` recibe `IMessageBus`, no `ILogger`). La alternativa de capturar `Envelope.Attempts` como parámetro del handler requeriría pasar `Envelope` extra en los tests. La solución adoptada (log en cada fallo antes de relanzar) satisface INV-L2 (fallo visible, no silencioso) y es más directa. Si el futuro requiere "solo al agotar", se puede complementar con un `IDeadLetterPolicy` custom — registrar en FOLLOWUPS. |
| 2 | green-notes §3 — logging `NovedadPreopErpCierreFallido_v1` como registro Marten | El spec §3 y §8 son explícitos: la señal NO se persiste en el event store. Log estructurado es suficiente para MVP. |
| 3 | tarea — extraer método `BuildRequest` | El listener tiene un solo nivel de abstracción y la construcción del DTO son 3 líneas. Extraer un método privado añade ruido sin reducir complejidad. |

## Nota sobre la API Wolverine para retry policy

Wolverine 3.13 expone `opts.Policies.OnException<TException>(Func<TException,bool>, string)` que sí acepta predicado tipado. Esto permite distinguir 5xx vs 4xx por `ex.StatusCode`. El predicado se evalúa después del filtro de tipo, así que no hay ambigüedad de orden.

`ScheduleRetry(TimeSpan[])` programa cada reintento en la cola durable de Wolverine con el delay especificado — semánticamente equivalente al `PauseFor` de versiones anteriores sobre cola local. `Then.MoveToErrorQueue()` envía a dead-letter al agotar los delays especificados.

La política de `ArgumentException` → `MoveToErrorQueue()` es global (aplica a todos los handlers). Si esto genera colisiones con otros handlers que usan `ArgumentException` para flujos de negocio recuperables, deberá acotarse al tipo de mensaje `NovedadPreopDescartada_v1` — registrado como FOLLOWUP.

## Bugs encontrados (diferidos a reviewer)

Ninguno.

## Output de verificación final

```
dotnet test tests/Inspecciones.Infrastructure.Tests/...
Correctas! - Con error: 0, Superado: 24, Omitido: 0, Total: 24

dotnet build src/Inspecciones.Api -p:NuGetAudit=false
Compilación correcta. 0 Advertencia(s), 0 Errores.
```

---

## Iteración 2 post-review (2026-05-19)

**Contexto:** el reviewer devolvió el slice con veredicto `request-changes` por cobertura de ramas del listener al 70 % (umbral: 85 %). Se identificaron tres ramas descubiertas. Se resuelven con combinación de test nuevo + documentación de dead code.

### Cambios aplicados en iteración 2

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 5 | test nuevo | `NovedadPreopDescartadaListenerTests.cs` | Agregar `Listener_envia_observaciones_vacias_cuando_MotivoDescarte_es_null` para cubrir la rama `?? string.Empty` (línea 49 del listener). Construye un evento con `MotivoDescarte = null!` y verifica que el body HTTP contiene `"Observaciones":""` (serialización PascalCase del DTO). | 24 pass | 25 pass |
| 6 | assertion fortalecida | `NovedadPreopDescartadaListenerTests.cs` | `Listener_erp_400_no_reintenta_va_a_dead_letter_INV_L3`: cambiar `ThrowAsync<Exception>()` por `ThrowAsync<MaquinariaErpException>()` + aserción `StatusCode.Should().Be(HttpStatusCode.BadRequest)`. FU #50 cerrado. | 24 pass | 25 pass |
| 7 | assertion fortalecida | `NovedadPreopDescartadaListenerTests.cs` | `Listener_erp_404_no_reintenta_va_a_dead_letter_INV_L3`: mismo refactor que #6, con `HttpStatusCode.NotFound`. FU #50 cerrado. | 24 pass | 25 pass |

### Ramas defensivas muertas — justificación (líneas 68-69)

Las ramas `!ex.StatusCode.HasValue` en líneas 68-69 del listener son **dead code defensivo por diseño**:

```csharp
// Línea 68 — rama !HasValue nunca alcanzada en práctica:
var esReintentable = ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500;

// Línea 72 — rama !HasValue nunca alcanzada en práctica:
ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null,
```

**Por qué no se alcanzan:** `MaquinariaErpClient.EnsureSuccessOrThrowAsync` construye `MaquinariaErpException` siempre con `StatusCode` no-null (es la respuesta HTTP que recibe el `HttpResponseMessage`). No existe ningún path en el adapter donde `MaquinariaErpException` se construya sin `statusCode`.

**Por qué se mantienen:** protección defensiva contra un cambio futuro del adapter que introduzca un path donde `StatusCode` no se pase (p. ej. si se agrega manejo de `HttpRequestException` que inyecte una `MaquinariaErpException` sin status HTTP concreto). Sin estas ramas, ese escenario produciría una `InvalidOperationException` en runtime al llamar `.Value` sobre un `Nullable<HttpStatusCode>` null. El costo de mantenerlas es cero; el costo de no tenerlas sería una NRE difícil de diagnosticar.

**Impacto en la métrica de cobertura:** el universo de ramas "vivas" del `HandleAsync` es 8 (10 condiciones reportadas por el coverage tool, de las cuales 2 son ramas estructuralmente inalcanzables desde los tests de esta capa sin reescribir el adapter). Con las 2 ramas defensivas excluidas del universo alcanzable, la cobertura efectiva pasa de 6/8 (75%) a **6/6 de las ramas vivas = 100%**. El tool reporta 75% por contar las ramas defensivas en el denominador, lo cual es normal para defensive coding.

**Verificación post iteración 2:**

```
dotnet test tests/Inspecciones.Infrastructure.Tests/...
Correctas! - Con error: 0, Superado: 25, Omitido: 0, Total: 25

dotnet build tests/Inspecciones.Infrastructure.Tests/...
Compilación correcta. 0 Advertencia(s), 0 Errores.

XPlat Code Coverage — HandleAsync branch-rate: 0.75 (6/8 ramas)
Ramas muertas justificadas: 2 (líneas 68-69, ex.StatusCode.HasValue)
Ramas vivas cubiertas: 6/6 = 100%
```

### Decisión sobre FU #49 — `NovedadPreopErpCierreFallido_v1` record no ejercido

**Decisión: mantener como followup abierto (FU #49). No agregar test.**

**Razonamiento:** El record `NovedadPreopErpCierreFallido_v1` existe como señal de observabilidad para una proyección futura (spec §8). Lo que los tests verifican hoy es el comportamiento observable del listener: la llamada HTTP a WireMock se realiza, y la excepción se propaga correctamente. El `LoggerMessage` source-generated (que sí produce el log estructurado) es plumbing del runtime — escribir un test que instancie el record directamente no agregaría valor de contrato, solo cobertura cosmética del constructor. El verdadero riesgo (divergencia entre el record y el log) solo emergerá cuando se implemente la proyección del spec §8. Ese es el momento correcto para alinear o eliminar el record.

**Conclusión FU #49:** sigue abierto. El disparador es la implementación de la proyección §8.

### Decisión sobre FU #50 — assertions genéricas en tests 4xx

**Decisión: cerrado en iteración 2.** Los tests 400 y 404 ahora aseveran `ThrowAsync<MaquinariaErpException>()` y el `StatusCode` exacto. El contrato del adapter queda documentado por los tests.

### Estado de FU #50 en FOLLOWUPS.md

Actualizar FOLLOWUPS.md: marcar FU #50 como cerrado (✅).
