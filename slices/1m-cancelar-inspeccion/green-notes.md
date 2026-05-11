# Green Notes — Slice 1m: CancelarInspeccion

**Agente:** green
**Fecha:** 2026-05-11
**Estado:** VERDE — build limpio, todos los tests destrabados

---

## 1. Archivos modificados

| Archivo | Operación | Descripción |
|---|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Modificado | Implementado `Cancelar(motivo, canceladaPor, canceladaEn)`: guardia PRE-5 (I6 + I-F1) + emite `InspeccionCancelada_v1` |
| `src/Inspecciones.Application/Inspecciones/CancelarInspeccionHandler.cs` | Modificado | Implementado `Handle()`: PRE-2 (AggregateStreamAsync null-check), PRE-3 (contribuyente), PRE-4 (motivo ≥10 chars trimmed), llama `aggregate.Cancelar()`, un único `SaveChangesAsync` |

### Archivos no tocados (ya completos desde el agente `red`)

- `src/Inspecciones.Domain/Inspecciones/InspeccionCancelada_v1.cs` — shape correcto (DateTimeOffset, D-3)
- `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` — `MotivoCancelacionInvalidoException` ya estaba
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` — endpoint completo con todos los catches correctos
- `src/Inspecciones.Api/Program.cs` — handler registrado en DI
- `src/Inspecciones.Application/Inspecciones/CancelarInspeccion.cs` — record de comando
- `src/Inspecciones.Application/Inspecciones/CancelarInspeccionResult.cs` — record de resultado

---

## 2. Output `dotnet test` final

### Dominio puro (sin Postgres)

```
Correctas! - Con error: 0, Superado: 16, Omitido: 3, Total: 19
```

Los 3 skip son permanentes por diseño (PRE-1 capa HTTP, PRE-2 requiere Marten, idempotencia Wolverine ADR-008).

### Todos los tests de dominio (repo completo)

```
Correctas! - Con error: 0, Superado: 213, Omitido: 15, Total: 228
```

Ningún test de slices anteriores roto.

### Nota sobre Application.Tests y Api.Tests

Requieren Postgres (Testcontainers). No se ejecutaron localmente (Docker no disponible en entorno local — ver `project_docker_block.md` en memoria del agente). Los 6 tests de Application y los 8 tests E2E destrabados dependen de Postgres y se verificarán en CI.

---

## 3. Lógica implementada

### `Inspeccion.Cancelar()`

```csharp
// PRE-5 (I6 + I-F1): solo se puede cancelar en estado EnEjecucion.
if (Estado != EstadoInspeccion.EnEjecucion)
    throw new InspeccionNoEnEjecucionException(
        $"La inspección {InspeccionId} está en estado '{Estado}'...");

return [new InspeccionCancelada_v1(InspeccionId, motivo, canceladaPor, canceladaEn)];
```

Decisión deliberada: el mensaje de error incluye el estado actual entre comillas simples. El test §6.10 espera `*Firmada*`, §6.11 espera `*Cancelada*`, §6.12 espera `*CerradaSinOT*` — todos cubiertos porque el `{Estado}` resuelve al nombre del enum.

### `CancelarInspeccionHandler.Handle()`

Orden de guardias: PRE-2 → PRE-3 → PRE-4 → PRE-5 (en aggregate).  
TimeProvider: `_time.GetUtcNow()` — nunca `DateTime.UtcNow`.  
Atomicidad: un único `_session.Events.Append(...) + SaveChangesAsync()`.

---

## 4. Impulsos de refactor no implementados (candidatos para `refactorer`)

- **Mensaje de error PRE-5 duplicado**: el patrón `"La inspección {Id} está en estado '{Estado}'. {Comando} solo aplica a inspecciones en estado 'EnEjecucion'."` aparece en varios comandos (`RegistrarHallazgo`, `Firmar`, `CancelarInspeccion`, etc.) con texto ligeramente distinto. Candidato para extraer a helper o mensaje compartido.
- **Validación de motivo duplicada**: la lógica `motivo.Trim().Length < 10` con mensajes diferenciados (vacío vs. corto) aparece idéntica en `CasoDeUso.Cancelar` (test helper) y en `CancelarInspeccionHandler.Handle`. Candidato para extraer a método estático en dominio o en un validador compartido.
- **Patrón de guardias PRE-2/PRE-3/PRE-4 repetido**: cada handler tiene la misma secuencia AggregateStreamAsync → null-check → contribuyente-check → motivo-check. Candidato para un `InspeccionHandlerBase` o extension methods, pero solo vale la pena cuando haya ≥4 handlers con ese patrón.

---

## 5. Decisiones de "código más simple de lo esperado"

- PRE-4 (validación de motivo) vive en el handler en lugar de en el aggregate, conforme al spec §4. Si se moviera al aggregate, los tests de dominio puro necesitarían pasar el motivo al método `Cancelar`, cambiando la firma. El spec es explícito: PRE-4 es responsabilidad del handler.
- El `TecnicoNoContribuyenteException` catch en el endpoint ya existía desde `red` — no fue necesario modificar `InspeccionesEndpoints.cs`.
