# Refactor notes — Slice erp-3 — SincronizarDictamenVigenteSaga

## Cambios aplicados

| # | Tipo | Archivo(s) | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | rename | `SincronizarDictamenVigenteSagaListener.cs` → `SincronizarDictamenVigenteListener.cs` | Quitar misnomer "Saga" — el listener no es una saga (no tiene estado, no coordina pasos compensables). El patrón de referencia erp-2 se llama `DescartarNovedadPreopErpListener` sin "Saga". Ajustadas 3 referencias en tests: docstring `<see cref>`, `CrearListenerCon` tipo de retorno, y `SincronizarDictamenVigenteListener.MapearDictamen` en el test §6.11. | 36/36 | 36/36 |
| 2 | fix log level | `SincronizarDictamenVigenteListener.cs` | `LogSyncFallida` usaba `LogLevel.Critical` pero la spec §5 INV-L2 dice explícitamente nivel `Error` y el patrón erp-2 usa `Error`. Se corrigió a `LogLevel.Error` para consistencia. (El green lo introdujo sin documentar la discrepancia — no era intencional.) | 36/36 | 36/36 |
| 3 | cleanup comments | `SincronizarDictamenVigenteListener.cs` | Eliminados comentarios WHAT inline (`// PRE-L1: leer aggregate — null indica stream inexistente`). Los WHY se movieron al cuerpo del `if` donde tienen más contexto. Los labels de precondición (PRE-L1, PRE-L3) se conservan porque referencian la spec. | 36/36 | 36/36 |
| 4 | new file + DI | `src/Inspecciones.Infrastructure/Erp/MartenInspeccionReader.cs` + `Program.cs` | Implementación de producción de `IInspeccionReader` que delega a `IQuerySession.Events.AggregateStreamAsync<Inspeccion>`. Registrado en DI como `AddScoped<IInspeccionReader, MartenInspeccionReader>()` (Scoped porque `IQuerySession` es Scoped en Marten). Cierra el gap "puerto sin adapter" señalado en green-notes §2 y red-notes §5.3. | 36/36 | 36/36 |

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §candidatos | Extraer lógica de log de fallo a helper compartido entre erp-2 y erp-3. Los dos listeners tienen firmas de log distintas (`StatusCode int?` + `CodigoErp` en erp-2 vs `EquipoId` + `Dictamen` en erp-3). No es DRY real — son señales distintas. Anotar en FOLLOWUPS.md si emerge un tercer listener con duplicación exacta. |
| 2 | green-notes §1 | `intentosAgotados: 1` hardcodeado. Señalado como decisión deliberada en green-notes §1 — el contador real está en el dead-letter handler de Wolverine. No tocar sin un slice que implemente ese handler. |

## Decisión sobre rename del listener

**Se aplicó.** `SincronizarDictamenVigenteSaga` → `SincronizarDictamenVigente`.

Justificación: "Saga" en DDD/Wolverine tiene semántica específica — proceso de larga duración con estado compensable. Este listener no tiene estado, no coordina múltiples steps y no necesita compensación. Es un listener de integración simple, idéntico en estructura a `DescartarNovedadPreopErpListener`. Mantener "Saga" en el nombre crearía confusión cuando se implementen sagas reales (p. ej. `GenerarOTSaga`). El rename no cambia comportamiento, el impacto es mínimo (3 referencias en tests + archivo) y los tests siguen pasando idénticos.

## Nota para reviewer (no es arreglo silencioso — se documenta)

El verde introdujo `LogLevel.Critical` en `LogSyncFallida`. La spec §5 INV-L2 dice explícitamente "log `Error`". El patrón erp-2 también usa `Error`. Se corrigió en el refactor #2 arriba. No se consideró un cambio de comportamiento porque el nivel de log no afecta la decisión de retry/dead-letter de Wolverine (eso lo maneja la política por tipo de excepción) — es solo un cambio de severidad en el log estructurado.

## Output de tests

**Antes (línea de base verde del green):**
```
Correctas! - Con error: 0, Superado: 36, Omitido: 0, Total: 36
```

**Después (tras todos los refactors):**
```
Correctas! - Con error: 0, Superado: 36, Omitido: 0, Total: 36
```

**Build:**
```
Compilación correcta. 0 Advertencia(s), 0 Errores.
```

## Ramas del listener — cobertura

| Rama | Cubierta | Test |
|---|---|---|
| `aggregate is null` (PRE-L1) | Sí | §6.9 |
| `aggregate.Dictamen is null` (PRE-L1 corrupto) | Sí | §6.10 |
| `MapearDictamen` valor no mapeado (PRE-L3) | Sí | §6.11 |
| 200 OK (éxito) | Sí | §6.1, §6.2, §6.3 |
| `MaquinariaErpException` con StatusCode (4xx/5xx) | Sí | §6.5, §6.6, §6.7, §6.8 |
| `MaquinariaErpException` sin StatusCode (`!ex.StatusCode.HasValue`) | Rama defensiva muerta — igual que en erp-2. `MaquinariaErpClient` siempre propaga el `HttpStatusCode`. No está testeada. Documentada como rama defensiva. |

## Ramas de `MartenInspeccionReader`

`AggregateStreamAsync` devuelve `null` o un aggregate; la comprobación de null la hace el listener (PRE-L1). El adapter es un wrapper de una sola línea — no hay ramas propias.
