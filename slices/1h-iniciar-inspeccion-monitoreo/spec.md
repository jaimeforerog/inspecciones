# Slice 1h — IniciarInspeccionMonitoreo

**Autor:** domain-modeler
**Fecha:** 2026-05-07
**Estado:** signed (P-1..P-6 + ambigüedades firmadas por el usuario el 2026-05-07)
**Agregado afectado:** `Inspeccion` (aggregate unificado — discriminador `TipoInspeccion.Monitoreo`). Extiende `InspeccionIniciada_v1` con tres campos nuevos (nullable cuando `Tipo=Tecnica`).
**Decisiones previas relevantes:**
- `slices/1b-iniciar-inspeccion-handler/spec.md` — patrón de referencia más cercano (handler + proyección + endpoint HTTP `POST /api/v1/inspecciones`).
- `01-modelo-dominio.md §12.11.5` — modelo de monitoreo (rutinas por grupo, items snapshoteados, `EvaluacionEsperada`, trigger hallazgo automático).
- `01-modelo-dominio.md §15.4` — catálogo de 24 eventos; `InspeccionIniciada_v1` extendido con `Tipo`, `RutinaMonitoreoSeleccionadaId`, `ItemsSnapshot`.
- `01-modelo-dominio.md §15.7` — invariantes I-I1, I-I2, I-I3 adaptadas al contexto monitoreo.
- `01-modelo-dominio.md §15.4 convención de tipos` — `int` para PKs ERP, `Guid` para IDs internos del módulo.
- `roadmap.md §3.B' paso 3.16e` — alcance exacto del slice.
- `06-contrato-apis-erp.md M-3b + M-16` — origen de `grupoMantenimientoId` y rutinas de monitoreo.
- ADR-002 (`§9.11`) — capa HTTP recibe claims del host PWA; handler los recibe como `ClaimsTecnico`.
- ADR-004 (`§9.15`) — `RutinaMonitoreoLocal` y `EquipoLocal` consultados vía `IDocumentSession` (sync on-app-open).
- ADR-006 (`§16`) — outbox transaccional; atomicidad evento + proyección + envelope en `SaveChangesAsync`.
- ADR-008 (`§9.16`) — `clientCommandId` UUIDv7 como `MessageId` Wolverine; idempotencia end-to-end.
- **Decisión 2026-05-05:** aggregate unificado con discriminador `TipoInspeccion`; rutinas-monitoreo asignadas por grupo, no per-equipo; técnico elige entre las rutinas activas del grupo; snapshot de items obligatorio en evento.

---

## 1. Intención

El técnico de campo necesita iniciar una **inspección de monitoreo** sobre un equipo, eligiendo la rutina de monitoreo aplicable al grupo del equipo. El sistema debe:

1. Validar que el equipo existe y que la rutina elegida pertenece al mismo grupo de mantenimiento que el equipo.
2. Construir un snapshot inmutable de los items activos de la rutina (necesario porque `FueraDeRango` se calculará más adelante contra la `EvaluacionEsperada` snapshoteada, que puede cambiar en el catálogo).
3. Persistir `InspeccionIniciada_v1` con `Tipo=Monitoreo`, `RutinaMonitoreoSeleccionadaId` e `ItemsSnapshot`.
4. Devolver la `InspeccionId` al cliente para que el frontend abra la pantalla de checklist de la rutina.

A diferencia de la inspección técnica, el técnico **elige** la rutina (no se auto-resuelve), y el flujo posterior es un checklist estructurado de items (no hallazgos libres). Todo lo demás —proyección de equipo activo, idempotencia I-I1, autorización, endpoint HTTP— sigue el mismo patrón del slice 1b.

**Motivación de negocio:** el monitoreo periódico de sistemas específicos (p. ej. "Sistema eléctrico" de camionetas) permite detectar tendencias de deterioro antes de que deriven en falla. Al promoverlo a MVP (decisión Jaime 2026-05-05), el módulo cierra el gap entre inspección puntual (técnica) y vigilancia continua (monitoreo).

---

## 2. Comando

```csharp
public sealed record IniciarInspeccionMonitoreo(
    Guid InspeccionId,                              // generado client-side (UUIDv7 recomendado)
    int EquipoId,                                   // PK ERP — equipo a inspeccionar
    int ProyectoId,                                 // PK ERP — proyecto del técnico (del JWT claim sinco_obras)
    int RutinaMonitoreoId,                          // PK ERP — elegida por el técnico en UI (decisión 2026-05-05)
    string IniciadaPor,                             // TecnicoId opaco del JWT — nunca validado por el dominio
    UbicacionGps Ubicacion,                         // GPS obligatorio al iniciar (§12.10.4)
    DateOnly FechaReportada,                        // fecha real declarada por el técnico (I-I3)
    LecturaMedidor? LecturaMedidorPrimario,         // horómetro / cuentakilómetros — opcional
    LecturaMedidor? LecturaMedidorSecundario,       // segundo medidor — opcional
    IReadOnlyCollection<string> Capabilities);      // claims del host PWA — nunca toca HTTP
```

> `ClaimsTecnico` se forma en la capa HTTP con `IniciadaPor`, `ProyectoId` implícito en claims, `Capabilities`. El dominio recibe `ProyectosAsignados: ISet<ProyectoId>` y `Capabilities: IReadOnlyCollection<string>` separados — nunca lee el JWT directamente.

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// DTO de entrada (mapea al record de comando en la capa API)
public sealed record IniciarInspeccionMonitoreoRequest(
    Guid InspeccionId,
    int EquipoId,
    int ProyectoId,
    int RutinaMonitoreoId,
    UbicacionGps UbicacionInicio,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario);

// DTO de resultado (misma forma que slice 1b — reutilizable)
public sealed record IniciarInspeccionMonitoreoResult(
    Guid InspeccionId,
    bool RedirigeAExistente,
    long Version,
    string? Mensaje);
```

---

## 3. Evento(s) emitido(s)

Este slice extiende `InspeccionIniciada_v1` con tres campos nuevos, manteniendo el nombre y el sufijo `_v1` (ver decisión D6 en §11).

| Evento | Payload extendido | Cuándo |
|---|---|---|
| `InspeccionIniciada_v1` | Todos los campos del slice 1a/1b + `Tipo=Monitoreo` + `RutinaMonitoreoSeleccionadaId: int?` + `ItemsSnapshot: IReadOnlyList<ItemRutinaMonitoreoSnapshot>?` | Al iniciar inspección de monitoreo. Los campos nuevos son `null` cuando `Tipo=Tecnica` (streams previos del slice 1b). |

**Shape completo del evento extendido:**

```csharp
public sealed record InspeccionIniciada_v1(
    Guid InspeccionId,
    TipoInspeccion Tipo,                                    // Tecnica | Monitoreo
    int EquipoId,
    int RutinaId,                                           // RutinaMonitoreoId cuando Tipo=Monitoreo
    string RutinaCodigo,                                    // nombre de la rutina monitoreo
    string TecnicoIniciador,
    int ProyectoId,
    UbicacionGps Ubicacion,
    DateTimeOffset IniciadaEn,
    DateOnly FechaReportada,
    LecturaMedidor? LecturaMedidorPrimario,
    LecturaMedidor? LecturaMedidorSecundario,
    // Campos nuevos — null cuando Tipo=Tecnica (backward compat)
    int? RutinaMonitoreoSeleccionadaId,                     // PK ERP de la rutina elegida
    IReadOnlyList<ItemRutinaMonitoreoSnapshot>? ItemsSnapshot); // items activos al momento de iniciar
```

**Value object `ItemRutinaMonitoreoSnapshot`** (namespace `Inspecciones.Domain.Inspecciones`):

```csharp
public sealed record ItemRutinaMonitoreoSnapshot(
    int ItemId,                                             // PK ERP del item
    string Parte,                                           // ej. "Batería"
    string Actividad,                                       // ej. "Medición de voltaje"
    EvaluacionEsperada Evaluacion);                         // MedicionEsperada | EvaluacionCualitativaEsperada
```

> `EvaluacionEsperada` (abstract record), `MedicionEsperada` y `EvaluacionCualitativaEsperada` ya están definidos en §12.11.5. El snapshot preserva el polimorfismo para que `FueraDeRango` se calcule correctamente en slices futuros (`RegistrarMedicion`, `RegistrarEvaluacionCualitativa`).

**Convención de `RutinaId` cuando `Tipo=Monitoreo`:** el campo existente `RutinaId: int` del evento lleva el `RutinaMonitoreoId`. El campo `RutinaMonitoreoSeleccionadaId` es su alias explícito para legibilidad de los handlers de monitoreo. El aggregate almacena ambos; el fold los mantiene consistentes.

---

## 4. Precondiciones

Las precondiciones se clasifican por la capa donde viven:

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability):** el usuario tiene capability `ejecutar-inspeccion` en los claims del host PWA (ADR-002 tentativo). Excepción: `403 Forbidden`. La capa HTTP lee los claims inyectados por el host; el handler recibe `IniciadaPor: string` y `Capabilities: IReadOnlyCollection<string>` como parámetros.
- **PRE-2 (proyecto autorizado):** `ProyectoId ∈ claims.ProyectosAsignados` (claim `sinco_obras` mapeado a `proyectos` por el módulo). Excepción: `ProyectoNoAutorizadoException` (`403 Forbidden`). El dominio también la revalida como defensa en profundidad (I-I2 del aggregate).

### Capa handler (antes de invocar el método de decisión del aggregate)

- **PRE-3 (equipo en catálogo):** `EquipoLocal` con `EquipoId` debe existir (sync M-3b vía ADR-004). El handler hace `session.Query<EquipoLocal>().Where(e => e.EquipoId == cmd.EquipoId).SingleOrDefaultAsync()`. Excepción: `EquipoNoEncontradoException` (`404 Not Found`).
- **PRE-4 (rutina en catálogo):** `RutinaMonitoreoLocal` con `RutinaMonitoreoId` debe existir (sync M-16 vía ADR-004). Excepción: `RutinaMonitoreoNoSincronizadaException` (`422 Unprocessable Entity`, código `I-I-Mon-0`).
- **PRE-5 (rutina pertenece al grupo del equipo):** `rutina.GrupoMantenimientoId == equipo.GrupoMantenimientoId`. Excepción: `RutinaNoAplicableAlGrupoException` (`422 Unprocessable Entity`, código `I-I-Mon-2`). Esta regla impide que un técnico elija una rutina de "Sistema hidráulico BULLDOZER" para una "Camioneta".
- **PRE-6 (rutina con items activos):** la rutina debe tener `≥1 item activo` tras el filtro de items con `Activo=true`. Si `Items.Where(i => i.Activo).Count() == 0` → `EquipoSinRutinasMonitoreoException` (`422 Unprocessable Entity`, código `I-I-Mon-1`). Ver decisión D3 en §11.
- **PRE-7 (I-I1 blanda):** consultar `InspeccionAbiertaPorEquipoView` por `EquipoId`. Si hay fila → corto-circuito (no emite evento, devuelve `RedirigeAExistente=true`). Ver invariante I-I1 y decisión D5.

### Método de decisión del aggregate (validaciones en el aggregate, no en Apply)

- **PRE-8 (I-I2 defensa en profundidad):** `ProyectoId ∈ proyectosAsignados` — revalida en el aggregate en caso de bypass de capa HTTP. Excepción: `ProyectoNoAutorizadoException`.
- **PRE-9 (I-I3 — rango de FechaReportada):** `FechaReportada <= DateOnly.FromDateTime(IniciadaEn)` AND `FechaReportada >= DateOnly.FromDateTime(IniciadaEn).AddDays(-30)`. Excepción: `FechaReportadaFueraDeRangoException` (`422 Unprocessable Entity`).

> **Capa de validación:** PRE-1 y PRE-2 en capa HTTP; PRE-3 a PRE-7 en el handler (`IniciarInspeccionMonitoreoHandler`); PRE-8 y PRE-9 en el método de decisión del aggregate (`Inspeccion.IniciarMonitoreo`). Los `Apply` son puros — nunca lanzan ni revalidan.

---

## 5. Invariantes tocadas

- **I-I1** — Una sola inspección abierta por equipo (§15.7). Defensa dual:
  - Validación blanda en handler (PRE-7): lee `InspeccionAbiertaPorEquipoView` por `EquipoId`; corto-circuita si hay fila.
  - Defensa dura en Postgres: índice único sobre `data->>'EquipoId'` en `mt_doc_inspeccionabiertaporequipoview`; atrapa race conditions concurrentes.
  - **Aplica sin distinción de tipo:** si hay una `InspeccionTecnica` activa y se intenta iniciar una `InspeccionMonitoreo`, I-I1 redirige a la existente (ver decisión D5 en §11).

- **I-I2 (adaptada)** — El equipo debe tener rutinas de monitoreo activas para el grupo. En inspección técnica, I-I2 exige rutina técnica asignada per-equipo. En monitoreo, la equivalencia es: `RutinaMonitoreoLocal` con `GrupoMantenimientoId == equipo.GrupoMantenimientoId` y con `≥1 item activo`. PRE-4, PRE-5 y PRE-6 materializan esta invariante. Si el equipo no tiene rutinas de monitoreo para su grupo → 422.

- **I-I3** — Rango válido de `FechaReportada` (§15.7). Aplica igual que en inspección técnica (PRE-9).

- **I-I-Mon-1 (nueva)** — Rutina de monitoreo sin items activos es rechazada al inicio (PRE-6). Propuesta: registrar esta invariante en `01-modelo-dominio.md §15.7` como `I-I-Mon-1` en el mismo PR de este slice.

- **I-I-Mon-2 (nueva)** — La rutina elegida debe pertenecer al mismo grupo de mantenimiento que el equipo (PRE-5). Propuesta: registrar como `I-I-Mon-2` en `01-modelo-dominio.md §15.7`.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — inicio de inspección de monitoreo

**Given**
- `EquipoLocal` con `EquipoId=4521`, `EquipoCodigo="D11T-001"`, `ProyectoId=3`, `GrupoMantenimientoId=7`, `GrupoMantenimiento="BULLDOZER"` (sync M-3b).
- `RutinaMonitoreoLocal` con `RutinaMonitoreoId=42`, `Nombre="Sistema eléctrico"`, `GrupoMantenimientoId=7`, items activos: `[ItemId=1 (Parte="Batería", Actividad="Medir voltaje", Evaluacion=MedicionEsperada("voltaje","V",12.3,12.5)), ItemId=2 (Parte="Conectores", Actividad="Estado visual", Evaluacion=EvaluacionCualitativaEsperada())]` (sync M-16).
- `InspeccionAbiertaPorEquipoView` vacía para `EquipoId=4521`.
- Claims con `IniciadaPor="ana.gomez"`, `ProyectosAsignados={1,2,3}`, capability `ejecutar-inspeccion`.
- `MessageId=X` único.

**When**
- `POST /api/v1/inspecciones/monitoreo` con `InspeccionId=W`, `EquipoId=4521`, `ProyectoId=3`, `RutinaMonitoreoId=42`, GPS válido, `FechaReportada=hoy`.

**Then**
- Emite `InspeccionIniciada_v1` con `Tipo=Monitoreo`, `RutinaId=42`, `RutinaCodigo="Sistema eléctrico"`, `RutinaMonitoreoSeleccionadaId=42`, `ItemsSnapshot=[{ItemId=1,...},{ItemId=2,...}]`, `TecnicoIniciador="ana.gomez"`, `ProyectoId=3`, `Ubicacion` del GPS, `IniciadaEn` del `TimeProvider`.
- `InspeccionAbiertaPorEquipoView` tiene fila con `EquipoId=4521`, `InspeccionId=W`, `Tipo=Monitoreo`.
- Handler retorna `IniciarInspeccionMonitoreoResult(InspeccionId=W, RedirigeAExistente=false, Version=1, Mensaje=null)`.
- Capa API devuelve `201 Created`, `Location: /api/v1/inspecciones/{W}`.

### 6.2 I-I1 — equipo con inspección activa (misma o distinta tipo) redirige a existente

**Given**
- `InspeccionAbiertaPorEquipoView` tiene fila para `EquipoId=4521` con `InspeccionId=Y` (puede ser técnica o monitoreo previa).
- Claims válidos. `MessageId=Z` distinto del que originó la activa.

**When**
- Handler ejecuta con `cmd.EquipoId=4521` (cualquier `RutinaMonitoreoId`).

**Then**
- Ningún evento se emite. Stream `W` queda vacío.
- Handler retorna `IniciarInspeccionMonitoreoResult(InspeccionId=Y, RedirigeAExistente=true, Version=N, Mensaje="Ya hay inspección activa, abriendo la existente")`.
- Capa API devuelve `200 OK`.

### 6.3 I-I1 race condition concurrente — pierde el segundo en unique violation

**Given**
- `InspeccionAbiertaPorEquipoView` sin fila para `EquipoId=4521`.
- Dos comandos simultáneos `cmd_A(InspeccionId=Wa)` y `cmd_B(InspeccionId=Wb)` sobre `EquipoId=4521`.
- Ambos pasan la validación blanda (read model stale para B).

**When**
- Ambos `SaveChangesAsync()` se ejecutan en paralelo.

**Then**
- Uno gana (A): persiste evento + fila proyección.
- B recibe `MartenCommandException` envolviendo `PostgresException(SqlState=23505)`; handler de B reintenta una vez, ahora ve fila `Wa`, devuelve `RedirigeAExistente=true, InspeccionId=Wa`.
- Exactamente un `InspeccionIniciada_v1` persistido para `EquipoId=4521`. Stream `Wb` queda vacío.

### 6.4 Idempotencia cliente — replay con mismo `clientCommandId`

**Given**
- Comando con `MessageId=X` ya ejecutado exitosamente; Wolverine envelope storage tiene respuesta original `(InspeccionId=W, RedirigeAExistente=false, Version=1)`.

**When**
- Cliente reenvía mismo `MessageId=X` tras timeout.

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- Stream `W` mantiene un solo `InspeccionIniciada_v1`.
- Capa API devuelve `200 OK` con body original.

### 6.5 PRE-3 — equipo no encontrado en catálogo

**Given**
- `EquipoLocal` sin fila para `EquipoId=99999`.

**When**
- `POST /api/v1/inspecciones/monitoreo` con `EquipoId=99999`.

**Then**
- Lanza `EquipoNoEncontradoException("Equipo {EquipoId=99999} no encontrado en catálogo local. Refresca catálogos.")`.
- Sin evento. Sin fila en proyección.
- Capa API devuelve `404 Not Found` con `{ "codigoError": "PRE-3", "mensaje": "..." }`.

### 6.6 PRE-4 — rutina de monitoreo no sincronizada

**Given**
- `EquipoLocal[4521]` existe con `GrupoMantenimientoId=7`.
- `RutinaMonitoreoLocal[42]` no existe (sync no ejecutado o rutina eliminada del ERP).

**When**
- `POST /api/v1/inspecciones/monitoreo` con `EquipoId=4521`, `RutinaMonitoreoId=42`.

**Then**
- Lanza `RutinaMonitoreoNoSincronizadaException("Rutina de monitoreo {RutinaMonitoreoId=42} no sincronizada. Refresca catálogos.")`.
- Sin evento.
- Capa API devuelve `422` con `{ "codigoError": "I-I-Mon-0", "mensaje": "..." }`.

### 6.7 PRE-5 — rutina no pertenece al grupo del equipo (I-I-Mon-2)

**Given**
- `EquipoLocal[4521].GrupoMantenimientoId=7` (BULLDOZER).
- `RutinaMonitoreoLocal[99].GrupoMantenimientoId=12` (CAMIONETA) — grupo distinto.

**When**
- `POST /api/v1/inspecciones/monitoreo` con `EquipoId=4521`, `RutinaMonitoreoId=99`.

**Then**
- Lanza `RutinaNoAplicableAlGrupoException("Rutina 99 pertenece al grupo 12 (CAMIONETA). El equipo 4521 pertenece al grupo 7 (BULLDOZER).")`.
- Sin evento.
- Capa API devuelve `422` con `{ "codigoError": "I-I-Mon-2", "mensaje": "..." }`.

### 6.8 PRE-6 — rutina sin items activos (I-I-Mon-1)

**Given**
- `EquipoLocal[4521].GrupoMantenimientoId=7`.
- `RutinaMonitoreoLocal[42].GrupoMantenimientoId=7` — mismo grupo. `Items = []` (rutina vacía) o todos con `Activo=false`.

**When**
- `POST /api/v1/inspecciones/monitoreo` con `EquipoId=4521`, `RutinaMonitoreoId=42`.

**Then**
- Lanza `EquipoSinRutinasMonitoreoException("La rutina de monitoreo 42 no tiene items activos.")`.
- Sin evento.
- Capa API devuelve `422` con `{ "codigoError": "I-I-Mon-1", "mensaje": "..." }`.

### 6.9 PRE-9 / I-I3 — FechaReportada futura

**Given**
- Contexto válido. `TimeProvider` retorna `2026-05-07T10:00:00-05:00`.

**When**
- Comando con `FechaReportada=2026-05-08` (mañana).

**Then**
- Método de decisión del aggregate lanza `FechaReportadaFueraDeRangoException("FechaReportada no puede ser futura. Fecha hoy: 2026-05-07.")`.
- Sin evento.
- Capa API devuelve `422`.

### 6.10 PRE-9 / I-I3 — FechaReportada con más de 30 días retroactivos

**Given**
- `TimeProvider` retorna `2026-05-07T10:00:00-05:00`.

**When**
- Comando con `FechaReportada=2026-04-05` (33 días atrás).

**Then**
- Lanza `FechaReportadaFueraDeRangoException("FechaReportada 2026-04-05 excede la ventana de 30 días retroactivos. Mínimo aceptable: 2026-04-07.")`.
- Sin evento.
- Capa API devuelve `422`.

### 6.11 PRE-8 / I-I2 defensa en profundidad — proyecto no autorizado

**Given**
- `claims.ProyectosAsignados = {1, 2}`. Comando con `ProyectoId=99`.

**When**
- Handler invoca el método de decisión del aggregate.

**Then**
- Lanza `ProyectoNoAutorizadoException`.
- Sin evento.
- Capa API devuelve `403 Forbidden`.

### 6.12 Snapshot solo incluye items activos

**Given**
- `RutinaMonitoreoLocal[42]` con 3 items: `ItemId=1 (Activo=true)`, `ItemId=2 (Activo=false)`, `ItemId=3 (Activo=true)`.
- Todo lo demás válido.

**When**
- `POST /api/v1/inspecciones/monitoreo` con `RutinaMonitoreoId=42`.

**Then**
- Evento `InspeccionIniciada_v1` emitido con `ItemsSnapshot=[{ItemId=1,...},{ItemId=3,...}]` — exactamente 2 items.
- `ItemId=2` no aparece en el snapshot.

### 6.13 Atomicidad evento + proyección + envelope

**Given**
- Mismo contexto que 6.1.

**When**
- Handler ejecuta con un único `SaveChangesAsync()` que comprende: (a) Append de `InspeccionIniciada_v1`, (b) upsert en `InspeccionAbiertaPorEquipoView`, (c) write en `wolverine.envelope_storage`.

**Then**
- Las tres escrituras son atómicas. Si el upsert de la proyección falla → `mt_events` no persiste el evento, `wolverine.envelope_storage` no persiste la respuesta.

### 6.14 Rebuild desde stream (obligatorio — §6.X del template)

**Given**
- Aggregate `Inspeccion` en estado inicial vacío (sin eventos).

**When**
- Se reproyecta el evento `InspeccionIniciada_v1` emitido por el happy path (escenario 6.1) aplicándolo vía `Apply(InspeccionIniciada_v1 e)`.

**Then**
- Estado resultante es idéntico al obtenido tras ejecutar el comando en 6.1:
  - `Tipo = TipoInspeccion.Monitoreo`
  - `RutinaId = 42`
  - `RutinaCodigo = "Sistema eléctrico"`
  - `TecnicoIniciador = "ana.gomez"`
  - `EquipoId = 4521`
  - `ProyectoId = 3`
  - `Estado = EstadoInspeccion.EnEjecucion`
  - `ItemsSnapshot.Count = 2`
  - `RutinaMonitoreoSeleccionadaId = 42`
- Ningún `Apply` lanza excepción.
- La prueba garantiza que `Apply(InspeccionIniciada_v1)` es puro y no contiene validaciones intrusas.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

- El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico confirma "Iniciar monitoreo". Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine.
- Wolverine envelope dedup detecta replays: mismo `clientCommandId` reenviado tras timeout devuelve `200 OK` con la respuesta original sin reaplicar (escenario 6.4).
- TTL del envelope: 30 días (igual que slice 1b).

**Idempotencia natural por I-I1:**

- Si el cliente reenvía con un `clientCommandId` distinto pero el equipo ya tiene activa (misma o distinta tipo), el handler corto-circuita (escenario 6.2) y devuelve la `InspeccionId` existente. Idempotente desde el punto de vista del usuario.

**Defensa concurrente:**

- Índice único parcial Postgres sobre `data->>'EquipoId'` en `mt_doc_inspeccionabiertaporequipoview` (ya existente del slice 1b — sin cambio necesario). Race condition: segundo `SaveChangesAsync` falla con `SqlState=23505`; handler reintenta y devuelve `RedirigeAExistente=true` (escenario 6.3).

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica directo. El outbox transaccional de Wolverine garantiza atomicidad evento + proyección + envelope en un único commit (escenario 6.13).

---

## 8. Impacto en proyecciones / read models

### 8.1 `InspeccionAbiertaPorEquipoView` — extensión de la proyección existente (slice 1b)

La proyección `InspeccionAbiertaPorEquipoView` ya existe y ya consume `InspeccionIniciada_v1`. La extensión del evento con campos nuevos (`Tipo`, `RutinaMonitoreoSeleccionadaId`, `ItemsSnapshot`) es **backward compatible** porque los campos son nullable.

**Cambio requerido en la proyección:** añadir campo `Tipo: TipoInspeccion` para que el frontend pueda distinguir el tipo de inspección activa al redirigir (I-I1). No se requiere cambio estructural — solo proyectar el nuevo campo.

```csharp
// Shape actualizado (delta sobre slice 1b)
public sealed record InspeccionAbiertaPorEquipoView(
    int EquipoId,                  // PK Marten (Identity)
    Guid InspeccionId,
    string TecnicoIniciador,
    DateTimeOffset IniciadaEn,
    int ProyectoId,
    TipoInspeccion Tipo);          // NUEVO en slice 1h — Tecnica | Monitoreo
```

> Streams del slice 1b (Tipo=Tecnica) son backward compat: `InspeccionIniciada_v1` ya tenía el campo `Tipo` con valor `TipoInspeccion.Tecnica` desde el aggregate del slice 1a. No hay migración de datos necesaria — el campo existía pero no se proyectaba en la view.

**Eventos consumidos (sin cambio):**

| Evento | Acción |
|---|---|
| `InspeccionIniciada_v1` | Upsert con `EquipoId` como PK (ahora incluye `Tipo`) |
| `InspeccionFirmada_v1` | Delete (slice futuro) |
| `InspeccionCancelada_v1` | Delete (slice futuro) |

### 8.2 `DetalleInspeccionView` y `BandejaTecnicoView`

Estas proyecciones async (fuera del alcance de este slice) deberán incluir `Tipo` e `ItemsSnapshot` cuando se implementen sus slices. No se tocan aquí.

### 8.3 Catálogos consumidos (read-only)

`EquipoLocal` (M-3b) y `RutinaMonitoreoLocal` (M-16). Sincronizados via ADR-004 (on-app-open + ETag). El handler los consulta vía `IDocumentSession`; no los modifica. Si están vacíos o stale → escenarios 6.5 y 6.6.

---

## 9. Impacto en endpoints HTTP

**Endpoint:** `POST /api/v1/inspecciones/monitoreo`

> Ruta separada de `POST /api/v1/inspecciones` (técnica — slice 1b) para que el cliente sea explícito sobre el tipo. Alternativa `POST /api/v1/inspecciones` con campo `tipo` rechazada para evitar ambigüedad en validaciones de entrada.

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO:**

```json
{
  "inspeccionId": "0193a4f7-...",
  "equipoId": 4521,
  "proyectoId": 3,
  "rutinaMonitoreoId": 42,
  "ubicacionInicio": {
    "latitud": 4.711,
    "longitud": -74.072,
    "precisionMetros": 8.5,
    "capturadoEn": "2026-05-07T08:30:12-05:00"
  },
  "fechaReportada": "2026-05-07",
  "lecturaMedidorPrimario": null,
  "lecturaMedidorSecundario": null
}
```

**Response 201 Created (happy path — escenario 6.1):**
- Header `Location: /api/v1/inspecciones/{inspeccionId}`.
- Body:
```json
{
  "inspeccionId": "0193a4f7-...",
  "redirigeAExistente": false,
  "version": 1,
  "mensaje": null
}
```

**Response 200 OK (redirige a existente — escenarios 6.2 y 6.3):**
```json
{
  "inspeccionId": "<existente>",
  "redirigeAExistente": true,
  "version": 1,
  "mensaje": "Ya hay inspección activa, abriendo la existente"
}
```

**Response 200 OK (replay idempotente — escenario 6.4):** body idéntico al original (Wolverine replay).

**Response 422 Unprocessable Entity:**

| Código de error | Escenario |
|---|---|
| `I-I-Mon-0` | Rutina de monitoreo no sincronizada (PRE-4) |
| `I-I-Mon-1` | Rutina sin items activos (PRE-6) |
| `I-I-Mon-2` | Rutina no pertenece al grupo del equipo (PRE-5) |
| `I-I3` | FechaReportada fuera de rango (PRE-9) |
| `I-I2` | Proyecto no autorizado defensa en profundidad (PRE-8) |

**Response 404 Not Found:** equipo no encontrado en catálogo (PRE-3). Código `PRE-3`.

**Response 403 Forbidden:** capability faltante (PRE-1) o proyecto no autorizado (PRE-2). Código `PRE-1` / `PRE-2`.

**Rol/permiso:** capability `ejecutar-inspeccion` con proyecto asignado. Heredado del host PWA.

> `inspeccionId` viaja en el body desde el cliente (UUIDv7 generado client-side). Si el cliente no lo envía, el handler genera con `Guid.NewGuid()` (fallback). Misma convención que slice 1b (CLAUDE.md: "`Guid.NewGuid()` solo en handlers").

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `InspeccionIniciada_v1` con `Tipo=Monitoreo` no genera push hacia el frontend según el catálogo vigente de ADR-005 (`§14`). El push está reservado para eventos de cierre y fallo (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`).

> Cuando emerja la necesidad de notificar en bandeja del proyecto que una nueva inspección de monitoreo fue iniciada, se agrega evento push `InspeccionEstadoCambiado` con audiencia `Group=proyectoId`. No bloquea este slice.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `IniciarInspeccionMonitoreo` no consume ni publica hacia el ERP. Trabaja exclusivamente con catálogos locales (`EquipoLocal`, `RutinaMonitoreoLocal`, `InspeccionAbiertaPorEquipoView`). Los catálogos se llenan vía sync ADR-004 (M-3b y M-16) gestionado por slices independientes del roadmap §3.E y §4.B.

> M-3b y M-16 están marcados `🚧 en construcción` en `06-contrato-apis-erp.md`. En tests, `RutinaMonitoreoLocal` y `EquipoLocal` se llenan directamente con `session.Store(...)` o WireMock. No hay bloqueante de adapter para este slice.

---

## 12. Preguntas abiertas / decisiones pendientes

Las siguientes decisiones (D1–D6) emergen del modelado. Todas tienen propuesta firmada por el modelador. Requieren confirmación del usuario antes de pasar a la fase `red`.

---

### P-1 (= D1) — Naming exacto del enum de tipo de item de monitoreo

**Contexto:** el modelo §12.11.5 usa el type `EvaluacionEsperada` (abstract record) con dos subtipos `MedicionEsperada` y `EvaluacionCualitativaEsperada`. El snapshot necesita distinguir qué tipo de evaluación trae cada item.

**Propuesta del modelador:** mantener el patrón de discriminador de tipo via el tipo de record de C# (pattern matching sobre `EvaluacionEsperada`). No se necesita un enum separado `ItemTipo` para el snapshot — el polimorfismo del record ya lo resuelve. Si el serializar/deserializar de Marten requiere discriminador explícito, se usa el atributo `[DerivedType]` de Marten en la jerarquía de `EvaluacionEsperada`.

**Estado:** ✅ firmada por el usuario el 2026-05-07.

---

### P-2 (= D2) — Ubicación del `ItemRutinaMonitoreoSnapshot`

**Contexto:** el value object `ItemRutinaMonitoreoSnapshot` es parte del payload del evento `InspeccionIniciada_v1` y también usado por los handlers de monitoreo futuros (`RegistrarMedicion`, `RegistrarEvaluacionCualitativa`).

**Propuesta del modelador:** `Inspecciones.Domain/Inspecciones/ItemRutinaMonitoreoSnapshot.cs` — junto a `InspeccionIniciada_v1.cs` y demás value objects del aggregate. El namespace `Inspecciones.Domain.Inspecciones` ya tiene `LecturaMedidor`, `UbicacionGps` y otros VOs del mismo aggregate. `EvaluacionEsperada`, `MedicionEsperada` y `EvaluacionCualitativaEsperada` también van en este namespace si aún no existen en el código (actualmente el código en `src/` no los tiene — son nuevos en este slice).

**Estado:** ✅ firmada por el usuario el 2026-05-07.

---

### P-3 (= D3) — Cardinalidad mínima de items: ¿se permite rutina vacía?

**Contexto:** la rutina de monitoreo en el ERP puede existir con 0 items activos (bug de configuración o rutina en construcción).

**Propuesta del modelador:** **NO** se permite iniciar inspección de monitoreo con 0 items activos. Invariante `I-I-Mon-1`: rutina sin items activos → 422. Justificación: el flujo de monitoreo es un checklist estructurado; sin items no hay checklist. Además el aggregate no podría calcular `FueraDeRango` ni registrar mediciones sin snapshot. Esta regla se aplica en el handler (PRE-6) antes de invocar el aggregate — es defensa de datos del catálogo, no del aggregate.

Consecuencia para el aggregate: el método de decisión `Inspeccion.IniciarMonitoreo` recibe los `ItemsSnapshot` ya construidos y validados (≥1 item) por el handler. El aggregate no re-valida cardinalidad — confía en el handler (separación de capas).

**Estado:** ✅ firmada por el usuario el 2026-05-07.

---

### P-4 (= D4) — Items inactivos en la rutina: ¿el snapshot los incluye?

**Contexto:** `ItemRutinaMonitoreo` puede tener un campo `Activo: bool` (no explícito en el record del modelo §12.11.5, pero operativamente necesario para permitir deprecar items sin romper inspecciones en curso).

**Propuesta del modelador:** el snapshot incluye **solo items con `Activo=true`** — filtro aplicado en el handler antes de pasar los items al aggregate. El aggregate recibe la lista ya filtrada. Esto garantiza que el técnico no recibe items obsoletos en su checklist. Items con `Activo=false` no aparecen en el snapshot ni en la UI.

**Consecuencia sobre el modelo §12.11.5:** el record `ItemRutinaMonitoreo` debe incluir `bool Activo` (o equivalente). Si el ERP no distingue activos/inactivos, todos los items del catálogo se consideran activos. Esto se confirma al implementar el adapter M-16.

**Estado:** ✅ firmada por el usuario el 2026-05-07. **Asunción aceptada:** el ERP M-16 expone `bool Activo` por item. Followup #22 (FOLLOWUPS.md) registra la confirmación pendiente con David — si el ERP no lo soporta, el módulo trata todos los items como activos sin cambio en el dominio.

---

### P-5 (= D5) — I-I1 cruzando tipos: ¿InspeccionTecnica activa bloquea IniciarInspeccionMonitoreo?

**Contexto:** I-I1 dice "una sola inspección abierta por equipo". La pregunta es si aplica sin distinción de tipo o si se permite tener una técnica y una monitoreo simultáneas para el mismo equipo.

**Propuesta del modelador:** **redirige** — un equipo solo puede tener UNA inspección activa en cualquier momento, sin importar el tipo. Razones:

1. **Consistencia operativa:** si un técnico está inspeccionando un equipo (técnica), otro no debería iniciar monitoreo en paralelo sobre el mismo equipo. La inspección activa ya captura el estado del equipo.
2. **Simpleza del modelo:** `InspeccionAbiertaPorEquipoView` tiene una fila por equipo (PK=`EquipoId`). Permitir dos tipos simultáneos requeriría una PK compuesta `(EquipoId, Tipo)` y rompe el índice único existente — cambio estructural mayor.
3. **El modelo §15.7 I-I1 no hace distinción de tipo** — "para un `EquipoId` no puede existir otra inspección con `Estado=EnEjecucion`".

Si emerge la necesidad de monitoreo concurrente a técnica (caso operativo no confirmado), se evalúa como cambio aditivo con ADR propio.

**Estado:** ✅ firmada por el usuario el 2026-05-07.

---

### P-6 (= D6) — Versionado del evento: ¿`InspeccionIniciada_v1` o `InspeccionIniciada_v2`?

**Contexto:** `InspeccionIniciada_v1` en el código actual (slice 1a/1b) NO tiene los campos `RutinaMonitoreoSeleccionadaId` ni `ItemsSnapshot`. Este slice necesita agregarlos.

**Propuesta del modelador:** mantener **`InspeccionIniciada_v1`** (sin crear `_v2`) aplicando migración soft:

- Los tres campos nuevos (`RutinaMonitoreoSeleccionadaId`, `ItemsSnapshot`, y la proyección del `Tipo` ya existente) son **nullables** en el record.
- Los streams del slice 1b (Tipo=Tecnica) deserializan con `RutinaMonitoreoSeleccionadaId=null` e `ItemsSnapshot=null`. Los handlers que consumen eventos de Tecnica nunca usan esos campos — compatible por diseño.
- El campo `Tipo` ya existe en el evento del 1a/1b con valor `Tecnica` — no es nuevo.
- El `Apply(InspeccionIniciada_v1)` del aggregate es puro y tolerante a nulls: si `ItemsSnapshot=null`, el aggregate no proyecta items (flujo técnica). Si `ItemsSnapshot!=null`, proyecta el snapshot (flujo monitoreo).

**Justificación con la regla de CLAUDE.md:** la convención dice "sufijo `_v1` cuando emerja una segunda versión (`HallazgoRegistrado_v2`)". Agregar campos opcionalmente nullables a un record es una evolución backward-compatible — no requiere nueva versión. Una `_v2` se justificaría si se cambiara la semántica de campos existentes o se eliminaran. Aquí solo se agregan nullable fields.

**Plan de migración:** ninguna. Los eventos existentes en `mt_events` para `InspeccionIniciada_v1` con `Tipo=Tecnica` deserializan sin problema: Marten / System.Text.Json rellenan `null` para los campos nuevos ausentes en el JSON persistido. El `Apply` del aggregate tolera `null`.

**Estado:** ✅ firmada por el usuario el 2026-05-07.

---

### Ambigüedades detectadas en el modelo §12.11.5 — ✅ resueltas por el usuario el 2026-05-07

**Resolución del usuario:** *"asume sí en ambas ambigüedades"* — se acepta la asunción optimista; los hallazgos en la integración real (M-16 con David) abren followup pero no rebloquean este slice.

1. **Campo `Activo` en `ItemRutinaMonitoreo` — ✅ asumido `true`.** El record `ItemRutinaMonitoreo` del catálogo local incluye `bool Activo`. El handler filtra items por `Activo=true` antes de pasarlos al aggregate (PRE-6). Si la confirmación con David revela que M-16 no expone el campo, el adapter siempre lo materializa como `true` y el filtro queda inerte — ningún cambio en el dominio. **Followup #22 (FOLLOWUPS.md)** registra el seguimiento.

2. **Orden de los items en el snapshot — ✅ asumido sí.** El record `ItemRutinaMonitoreo` incluye `int Orden`. El handler ordena los items por `Orden` ascendente antes de construir el snapshot. El snapshot preserva ese orden en `IReadOnlyList<ItemRutinaMonitoreoSnapshot>`. La UI recorre los items en el orden snapshoteado. **Followup #22 (FOLLOWUPS.md)** cubre también esta confirmación.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-9) tienen un escenario Given/When/Then en §6.
- [x] Todas las invariantes tocadas (I-I1, I-I2 adaptada, I-I3, I-I-Mon-1, I-I-Mon-2) tienen escenario en §6.
- [x] Happy path presente (6.1).
- [x] Escenario de rebuild desde stream obligatorio presente (6.14).
- [x] Idempotencia decidida en §7 (no en blanco): envelope dedup ADR-008 + natural I-I1 + defensa concurrente índice único.
- [x] §10 SignalR resuelto explícitamente ("no aplica en este slice").
- [x] §11 adapters Sinco resueltos explícitamente ("no aplica en este slice").
- [x] §12 preguntas abiertas D1–D6 resueltas con propuesta del modelador; 2 ambigüedades menores del modelo documentadas.
- [x] Evento extendido `InspeccionIniciada_v1` (no se crea evento separado).
- [x] Aggregate unificado — no se crea `InspeccionMonitoreo` separado.
- [x] Un único `SaveChangesAsync` — atomicidad garantizada.
- [x] **Firma del usuario completada (D1–D6 + ambigüedades §12)** el 2026-05-07 — el slice pasa a `red`.

---

## Notas de cierre para revisión humana

**Lo que este slice añade respecto al 1b:**

- Extensión del record `InspeccionIniciada_v1`: campos `RutinaMonitoreoSeleccionadaId: int?` e `ItemsSnapshot: IReadOnlyList<ItemRutinaMonitoreoSnapshot>?`.
- Value objects nuevos en `Inspecciones.Domain`: `ItemRutinaMonitoreoSnapshot`, `EvaluacionEsperada` (abstract), `MedicionEsperada`, `EvaluacionCualitativaEsperada`, `CalificacionCualitativa` (si no existen aún).
- Método de decisión `Inspeccion.IniciarMonitoreo` (nuevo método en el aggregate, o mismo método `Iniciar` parametrizado por tipo — decisión del `green`).
- Handler `IniciarInspeccionMonitoreoHandler` en `Inspecciones.Application`.
- Extensión de `InspeccionAbiertaPorEquipoView` con campo `Tipo: TipoInspeccion`.
- Endpoint HTTP `POST /api/v1/inspecciones/monitoreo` en `Inspecciones.Api`.
- Excepciones nuevas: `RutinaMonitoreoNoSincronizadaException`, `RutinaNoAplicableAlGrupoException`, `EquipoSinRutinasMonitoreoException`.

**Lo que NO hace este slice:**

- Slice `RegistrarMedicion` (roadmap 3.16f) — fuera de alcance.
- Slice `RegistrarEvaluacionCualitativa` (roadmap 3.16g) — fuera de alcance.
- Slice `OmitirItemMonitoreo` (roadmap 3.16h) — fuera de alcance.
- Sync M-16 (roadmap §4.B) — catálogo de rutinas de monitoreo; se asume existente en test como documento Marten.
- Validaciones pre-firma para inspecciones de monitoreo (§15.5 V-F1..V-F8 aplican igual — se tocarán en slice `FirmarInspeccion` si hay diferencias).
- Proyecciones async `BandejaTecnicoView` y `DetalleInspeccionView` con soporte monitoreo.
