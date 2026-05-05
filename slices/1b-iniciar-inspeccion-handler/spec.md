# Slice 1b — IniciarInspeccionHandler + InspeccionAbiertaPorEquipoView

**Autor:** domain-modeler
**Fecha:** 2026-05-05
**Estado:** draft (depende de slice 1a cerrado)
**Split:** este slice depende de `slices/1a-iniciar-inspeccion-aggregate/` cerrado en green/refactor/review. Se desarrolla como sub-slice independiente para aislar plumbing fundacional (configuración Marten del aggregate, proyección con índice único parcial Postgres, integración Wolverine + outbox + envelope dedup).
**Agregado afectado:** `Inspeccion` (consume) + `InspeccionAbiertaPorEquipoView` (proyección nueva).
**Decisiones previas relevantes:** ver `slices/1a-iniciar-inspeccion-aggregate/spec.md`. Adicionalmente:
- `01-modelo-dominio.md §15.12.6` — `InspeccionAbiertaPorEquipoView` con índice único parcial Postgres.
- ADR-006 (`§16` modelo) — outbox transaccional Wolverine + Marten en una sola transacción.
- ADR-008 (`§9.16` + refinamientos 2026-05-05) — `clientCommandId` UUIDv7 como `MessageId` Wolverine.

---

## 1. Intención

Exponer el comando `IniciarInspeccion` como caso de uso ejecutable end-to-end: orquestar I-I1 (validación blanda + defensa dura concurrente), resolver catálogos `EquipoLocal` y `RutinaTecnicaLocal` desde Marten, invocar el aggregate del slice 1a, persistir el evento atómicamente con la proyección y devolver el resultado al cliente.

## 2. Comando

Mismo `IniciarInspeccion` definido en slice 1a. Sin cambios.

## 3. Evento(s) emitido(s)

`InspeccionIniciada_v1` (definido en slice 1a) cuando el handler **no** corto-circuita por I-I1. Cuando sí corto-circuita, **ningún evento** se emite y el handler devuelve la `InspeccionId` existente.

## 4. Precondiciones (adicionales a las del slice 1a)

- **PRE-3 (handler):** `EquipoLocal` con `EquipoId` debe existir tras sync (ADR-004). Cache stale extrema (>7 días) bloquea (ADR-004 punto 3 refinamientos 2026-05-05). Excepción: `EquipoNoEncontradoException` (`404 Not Found`).
- **PRE-1 (capa HTTP):** capability `ejecutar-inspeccion` heredada del host PWA (ADR-002 tentativo). Excepción: `403 Forbidden` antes de invocar el handler.
- I-I1 (validación blanda): consultar `InspeccionAbiertaPorEquipoView` antes de invocar el aggregate; si hay fila → corto-circuito.

## 5. Invariantes tocadas

- **I-I1** Una sola inspección abierta por equipo. **Defensa dual (decisión 2026-05-05, definitiva):**
  - **Validación blanda** en handler — lee `InspeccionAbiertaPorEquipoView` antes de invocar el aggregate.
  - **Defensa dura** en Postgres — índice único parcial `WHERE Estado='EnEjecucion'` sobre `EquipoId`.

## 6. Escenarios Given / When / Then

### 6.1 (= §6.10 spec original) — I-I1 shortcut: equipo con activa retorna existente

**Given** `InspeccionAbiertaPorEquipoView` ya tiene fila para `EquipoId=4521` con `Estado=EnEjecucion`.
**When** se ejecuta `IniciarInspeccionHandler.ManejarAsync(cmd, claims)`.
**Then** no se emite ningún evento; el handler retorna `IniciarInspeccionResult(InspeccionId=existente, RedirigeAExistente=true, Version=N)`.

### 6.2 (= §6.11 spec original) — I-I1 race condition concurrente

**Given** dos comandos simultáneos sobre el mismo `EquipoId`. `InspeccionAbiertaPorEquipoView` aún sin fila.
**When** ambos handlers invocan `ManejarAsync` en paralelo.
**Then** uno de los dos `SaveChangesAsync` falla con unique violation Postgres; el handler que pierde reintenta, ahora ve la fila, devuelve `RedirigeAExistente=true` con la `InspeccionId` ganadora. Verificación: exactamente un `InspeccionIniciada_v1` persistido en el event store.

### 6.3 — Happy path end-to-end

**Given** `EquipoLocal` y `RutinaTecnicaLocal` poblados; `InspeccionAbiertaPorEquipoView` vacía.
**When** se ejecuta el handler.
**Then** un `InspeccionIniciada_v1` queda persistido en `mt_events`; `InspeccionAbiertaPorEquipoView` tiene una fila con `EquipoId` y `InspeccionId` del comando. Wolverine envelope dedup tabla tiene una entrada con el `MessageId` del comando.

### 6.4 — Idempotencia del cliente (replay con mismo `clientCommandId`)

**Given** ya se ejecutó exitosamente el comando con `MessageId=X`.
**When** el mismo `MessageId=X` reentra (cliente reintenta tras timeout).
**Then** Wolverine envelope dedup detecta y devuelve la respuesta original sin re-aplicar. El stream tiene un solo evento.

### 6.5 — PRE-3 equipo no encontrado en catálogo

**Given** `EquipoId=99999` no existe en `EquipoLocal`.
**When** se ejecuta el handler.
**Then** lanza `EquipoNoEncontradoException` (`404` desde la capa HTTP). Sin evento emitido, sin fila en proyección.

## 7. Idempotencia / retries

Definida en ADR-008. `clientCommandId` UUIDv7 como `MessageId` Wolverine. Envelope TTL=30 días. Defensa adicional: I-I1 corto-circuita reintentos sobre equipo activo aunque el envelope haya expirado.

## 8. Impacto en proyecciones / read models

- **`InspeccionAbiertaPorEquipoView`** — proyección **inline** (ejecuta en la misma transacción del Append). Schema:
  ```
  EquipoId int (PK)
  InspeccionId Guid
  ProyectoId int
  TecnicoIniciador string
  IniciadaEn timestamptz
  ```
  Índice único parcial en Postgres: `CREATE UNIQUE INDEX ... ON ... (EquipoId) WHERE Estado='EnEjecucion'`. Solo se llena con `InspeccionIniciada_v1`; se borra con `InspeccionFirmada_v1` o `InspeccionCancelada_v1` (slices futuros).

> Las proyecciones async (`BandejaTecnicoView`, `DetalleInspeccionView`) son slices independientes posteriores. Slice 1b se limita a la proyección inline crítica para I-I1.

## 9. Impacto en endpoints HTTP

`POST /api/v1/inspecciones`. Detalle del contrato en `slices/1a-iniciar-inspeccion-aggregate/spec.md §9` (preservado allí como referencia). Esta sección queda en draft hasta firma del 1b.

## 10. Impacto en SignalR / push (si aplica)

No aplica — definido en spec del 1a.

## 11. Impacto en adapters Sinco on-prem (si aplica)

No aplica — definido en spec del 1a.

## 12. Preguntas abiertas

- [ ] ¿La proyección `InspeccionAbiertaPorEquipoView` debe materializarse via Marten projection inline (`SingleStreamProjection`/`MultiStreamProjection`) o como proyección custom con `IDocumentSession.Store(...)` en el handler? Decisión a tomar al codear — Marten v7 prefiere `SingleStreamProjection` para correlación 1-stream-1-doc, pero acá necesitamos cubrir el lifecycle (`EnEjecucion → terminal`) que requiere reaccionar a múltiples eventos.

## 13. Checklist pre-firma

- [ ] Slice 1a cerrado (green + refactor + review approved).
- [ ] Decisión sobre forma de la proyección (pregunta §12).
- [ ] Spec firmada por el usuario antes de pasar a `red`.
