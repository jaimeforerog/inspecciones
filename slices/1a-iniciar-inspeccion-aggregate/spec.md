# Slice 1a — IniciarInspeccion (aggregate puro)

**Autor:** domain-modeler
**Fecha:** 2026-05-05
**Estado:** firmado (2026-05-05)
**Split:** este slice cubre **solo el aggregate** (método de decisión + `Apply` + rebuild). El handler con Marten, la proyección `InspeccionAbiertaPorEquipoView` y la defensa dual de I-I1 (handler + índice único Postgres) viven en `slices/1b-iniciar-inspeccion-handler/`. Razón del split (decisión 2026-05-05): mantener el slice en tamaño manejable; el alcance original incluía plumbing fundacional (configuración Marten del aggregate, proyección con índice único parcial) que excedía un slice típico.
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.4` — catálogo final de eventos (evento 1: `InspeccionIniciada_v1`).
- `01-modelo-dominio.md §12.11.3` — payload vigente del evento (con `Tipo`, `FechaReportada`, lecturas de medidor).
- `01-modelo-dominio.md §15.7` — invariantes I-I1, I-I2, I-I3.
- `01-modelo-dominio.md §15.12.6` — `InspeccionAbiertaPorEquipoView` con índice único Postgres (defensa de I-I1 ante race conditions).
- ADR-004 (`§9.15` + Refinamientos posteriores 2026-05-05) — el handler usa `RutinaTecnicaLocal` (sync de M-17, shape mínimo) y `EquipoLocal` (sync de M-3b).
- ADR-007 (`§17` modelo) — capability `ejecutar-inspeccion` heredada del host PWA.
- ADR-008 (`§9.16` + refinamientos 2026-05-05) — `clientCommandId` UUIDv7 viaja como `MessageId` Wolverine para idempotencia end-to-end.
- Refinamiento β 2026-05-04 — `Equipo.RutinaTecnicaId: int` viene de M-3b; cardinalidad 1 por equipo; el técnico no elige rutina.
- Followup #2 cerrado — `FechaReportada` separada del timestamp del sistema; ver mock pantalla 2.
- Followup #3 cerrado — equipos con dos medidores (primario + secundario), capturados al iniciar.

---

## 1. Intención

El técnico de mantenimiento tap "Iniciar inspección" sobre un equipo desde la bandeja de trabajo. El sistema crea un nuevo stream `Inspeccion` en estado `EnEjecucion`, registrando equipo, rutina técnica auto-resuelta, proyecto, técnico iniciador, ubicación GPS, fecha que el técnico afirma como "real" (puede ser distinta a hoy), y opcionalmente lecturas iniciales de los medidores. A partir de ese momento puede agregar hallazgos, importar novedades del preop y firmar.

Si el equipo ya tiene una inspección abierta (otro técnico colaborando, o la misma sesión re-tapeada), el sistema **no falla**: devuelve la `InspeccionId` existente con un flag `redirigeAExistente=true` para que el frontend abra la inspección activa.

## 2. Comando

```csharp
public sealed record IniciarInspeccion(
    Guid InspeccionId,                              // generado en handler con Guid.NewGuid() (regla CLAUDE.md);
                                                     // recibido por parámetro al método de decisión del agregado.
    int EquipoId,                                    // PK del ERP. Resolución a EquipoLocal en el handler.
    int ProyectoId,                                  // PK del ERP. El handler valida que ∈ tecnico.ProyectosAsignados.
    UbicacionGps UbicacionInicio,                    // GPS capturado al iniciar; sirve de base, V-F3 requiere
                                                     // re-capturar al firmar.
    DateOnly FechaReportada,                         // fecha "real" afirmada por el técnico (puede ser retroactiva
                                                     // hasta 30 días — ver I-I3).
    LecturaMedidor? LecturaMedidorPrimario,          // opcional al iniciar; el técnico puede capturar luego.
    LecturaMedidor? LecturaMedidorSecundario);       // ídem.
```

**Value objects referenciados:**

```csharp
public sealed record UbicacionGps(
    decimal Latitud,
    decimal Longitud,
    decimal PrecisionMetros,
    DateTimeOffset CapturadoEn);

public sealed record LecturaMedidor(
    string Tipo,                 // "Km", "Hr", según el equipo (denormalizado del medidor)
    decimal Valor,
    DateTimeOffset CapturadoEn);
```

**Claims del técnico** (parámetros adicionales del handler, no del comando — ver §6 de `domain-modeler.md`):

```csharp
public sealed record ClaimsTecnico(
    string TecnicoIniciador,                         // username opaco del host PWA
    ISet<int> ProyectosAsignados,                    // proyectos donde tiene capability ejecutar-inspeccion
    bool TieneCapabilityEjecutarInspeccion);
```

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `InspeccionIniciada_v1` | `InspeccionId, Tipo=Tecnica, EquipoId, RutinaId, RutinaCodigo, TecnicoIniciador, ProyectoId, Ubicacion, IniciadaEn, FechaReportada, LecturaMedidorPrimario?, LecturaMedidorSecundario?` | Al validar todas las precondiciones e invariantes y crear el stream. |

**Sin evento** si I-I1 detecta una inspección activa para el mismo equipo: el handler retorna la `InspeccionId` activa con `redirigeAExistente=true` (ver §9 endpoint). El stream existente no se modifica.

## 4. Precondiciones

- **PRE-1: Capability presente.** `claims.TieneCapabilityEjecutarInspeccion == true`. Sin ella, el comando es rechazado por la capa de auth antes de llegar al método de decisión. — excepción: `CapabilityRequeridaException` (manejada como `403 Forbidden` por la capa HTTP).
- **PRE-2: Proyecto autorizado.** `ProyectoId ∈ claims.ProyectosAsignados`. El técnico solo puede iniciar inspecciones en proyectos donde tiene asignación. — excepción: `ProyectoNoAutorizadoException` (`403 Forbidden`).
- **PRE-3: Equipo existe en cache local.** `EquipoLocal` con `EquipoId` debe existir tras sync (ADR-004). Cache stale extrema (>7 días) bloquea (ver punto 3 de Refinamientos posteriores ADR-004). — excepción: `EquipoNoEncontradoException` (`404 Not Found`).
- **PRE-4: Equipo pertenece al proyecto.** `EquipoLocal.ProyectoId == ProyectoId` del comando. Defensa contra captura cruzada por error o manipulación. — excepción: `EquipoNoPerteneceAProyectoException` (`422 Unprocessable Entity`).
- **PRE-5: Equipo tiene rutina técnica asignada (I-I2).** `EquipoLocal.RutinaTecnicaId != null`. El handler resuelve la rutina contra `RutinaTecnicaLocal` y la denormaliza al evento (`RutinaId`, `RutinaCodigo`). — excepción: `EquipoSinRutinaTecnicaException` con mensaje accionable "El equipo {EquipoCodigo} no tiene rutina técnica asignada en el ERP. Contacta al admin del catálogo en Sinco."
- **PRE-6: Rutina técnica existe en cache local (I-I2).** `RutinaTecnicaLocal[EquipoLocal.RutinaTecnicaId]` debe existir y ser `Tipo == TipoRutina.Tecnica`. — excepción: `RutinaTecnicaNoSincronizadaException` con mensaje "rutina referenciada por el equipo no está sincronizada — refresca catálogos".
- **PRE-7: FechaReportada en rango válido (I-I3).** `FechaReportada <= DateOnly.FromDateTime(IniciadaEn)` Y `FechaReportada >= DateOnly.FromDateTime(IniciadaEn).AddDays(-30)`. — excepción: `FechaReportadaFueraDeRangoException` con mensaje indicando rango aceptable.

> **Capa donde viven:** PRE-1 y PRE-2 viven en la capa HTTP (autorización del endpoint); PRE-3..PRE-7 viven en el **método de decisión** del agregado (`Inspeccion.Iniciar`). I-I1 (§5) tiene una validación blanda en el handler (consulta proyección antes de invocar el agregado) + una defensa dura en Postgres vía índice único — ver §5 abajo.

## 5. Invariantes tocadas

- **I-I1** Una sola inspección abierta por equipo (`§15.7`). **Fuera del alcance de 1a** — la validación dual (handler + índice único Postgres) vive en `slices/1b-iniciar-inspeccion-handler/`. El aggregate de 1a no la conoce: el handler corto-circuita antes de invocarlo si ya hay activa.
- **I-I2** Equipo debe tener rutina técnica asignada (`§15.7`). Cubierto por PRE-5 y PRE-6 arriba.
- **I-I3** Rango válido de `FechaReportada` (`§15.7`). Cubierto por PRE-7 arriba.

> No se introducen invariantes nuevos en este slice.

## 6. Escenarios Given / When / Then

### 6.1 Happy path — inicio nuevo

**Given** stream vacío (no hay eventos previos para `InspeccionId`), `claims` válidos con capability y proyecto autorizado, `EquipoLocal` existe con `RutinaTecnicaId=18`, `RutinaTecnicaLocal[18]` existe con `Tipo=Tecnica` y `Codigo="INSP. BULL.MOTOR"`, `FechaReportada` = hoy.
**When** se ejecuta `IniciarInspeccion(InspeccionId, EquipoId=4521, ProyectoId=3, UbicacionInicio, FechaReportada=hoy, LecturaMedidorPrimario=null, LecturaMedidorSecundario=null)`.
**Then** emite un único `InspeccionIniciada_v1` con `Tipo=Tecnica`, `EquipoId=4521`, `RutinaId=18`, `RutinaCodigo="INSP. BULL.MOTOR"` (denormalizado del catálogo), `TecnicoIniciador` del claim, `ProyectoId=3`, `Ubicacion=UbicacionInicio`, `IniciadaEn` desde `TimeProvider`, `FechaReportada=hoy`, lecturas null.

### 6.2 Happy path — inicio con lecturas de ambos medidores

**Given** mismo contexto que 6.1 + lecturas provistas: `LecturaMedidorPrimario=("Hr", 4523.5, ahora)`, `LecturaMedidorSecundario=("Km", 187432.0, ahora)`.
**When** se ejecuta el comando con lecturas.
**Then** el evento incluye ambas lecturas tal cual.

### 6.3 Happy path — inicio retroactivo (FechaReportada en rango)

**Given** mismo contexto, `IniciadaEn`=2026-05-05, `FechaReportada`=2026-05-03 (2 días atrás, dentro del rango I-I3).
**When** se ejecuta el comando.
**Then** emite el evento con `FechaReportada=2026-05-03` y `IniciadaEn` del system time.

### 6.4 Violación PRE-2 — proyecto no autorizado

**Given** `claims.ProyectosAsignados = {1, 2}`, comando con `ProyectoId=99`.
**When** se ejecuta el comando.
**Then** lanza `ProyectoNoAutorizadoException` con mensaje "El técnico {TecnicoIniciador} no tiene asignación al proyecto {ProyectoId}". No se emite ningún evento.

### 6.5 Violación PRE-4 — equipo no pertenece al proyecto

**Given** `EquipoLocal.ProyectoId = 1`, comando con `ProyectoId=2` (técnico autorizado en ambos pero el equipo no pertenece a 2).
**When** se ejecuta el comando.
**Then** lanza `EquipoNoPerteneceAProyectoException`. No se emite evento.

### 6.6 Violación PRE-5 — equipo sin rutina técnica (I-I2)

**Given** `EquipoLocal.RutinaTecnicaId = null` (admin del ERP no asignó rutina al equipo).
**When** se ejecuta el comando.
**Then** lanza `EquipoSinRutinaTecnicaException` con mensaje "El equipo {EquipoCodigo} no tiene rutina técnica asignada en el ERP. Contacta al admin del catálogo en Sinco."

### 6.7 Violación PRE-6 — rutina referenciada no sincronizada (I-I2)

**Given** `EquipoLocal.RutinaTecnicaId = 18`, `RutinaTecnicaLocal[18]` no existe (cache stale).
**When** se ejecuta el comando.
**Then** lanza `RutinaTecnicaNoSincronizadaException` con mensaje "rutina referenciada por el equipo no está sincronizada — refresca catálogos".

### 6.8 Violación PRE-7 — FechaReportada futura (I-I3)

**Given** `IniciadaEn`=2026-05-05, `FechaReportada`=2026-05-10.
**When** se ejecuta el comando.
**Then** lanza `FechaReportadaFueraDeRangoException` con mensaje indicando rango aceptable `[hoy-30, hoy]`.

### 6.9 Violación PRE-7 — FechaReportada >30 días retroactiva (I-I3)

**Given** `IniciadaEn`=2026-05-05, `FechaReportada`=2026-04-01 (35 días atrás).
**When** se ejecuta el comando.
**Then** lanza `FechaReportadaFueraDeRangoException`.

### 6.10 + 6.11 — I-I1 (movidos a slice 1b)

> El UX shortcut "ya hay activa, redirige a existente" (§6.10) y la defensa concurrente con índice único parcial Postgres (§6.11) son **escenarios del handler**, no del agregado. Se cubren en `slices/1b-iniciar-inspeccion-handler/spec.md`. Este slice 1a se mantiene focalizado en el aggregate puro.

### 6.12 Rebuild desde stream (obligatorio)

**Given** `Inspeccion` agregado vacío (sin eventos).
**When** se reproyecta el evento `InspeccionIniciada_v1` emitido por el happy path 6.1.
**Then** el estado resultante es idéntico al obtenido tras ejecutar el comando — `EquipoId=4521`, `Estado=EnEjecucion`, `RutinaId=18`, `RutinaCodigo="INSP. BULL.MOTOR"`, `TecnicoIniciador`, `ProyectoId=3`, `Ubicacion`, `FechaReportada`, lecturas. El método `Apply(InspeccionIniciada_v1)` es puro y no lanza.

> Garantiza que `Apply` es mutación pura de estado; no contiene validaciones que romperían el rebuild histórico.

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

- El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico tap "Iniciar". Este id viaja en header `X-Client-Command-Id`, mapeado a `MessageId` de Wolverine.
- Wolverine envelope dedup table (`wolverine.envelope_storage`) detecta replays: el mismo `clientCommandId` reenviado tras timeout/red flaky devuelve `200 OK` con la respuesta original sin reaplicar.
- TTL del envelope = 30 días en MVP.

**Idempotencia natural por I-I1:**

- Independientemente del envelope dedup, si el cliente reenvía con un `clientCommandId` distinto pero el equipo ya tiene una inspección activa (porque el primer intento sí llegó al server), el handler corto-circuita y devuelve la `InspeccionId` existente. Comportamiento idempotente desde el punto de vista del usuario aunque no desde el envelope.

**Sin POST a Sinco:** este comando no cruza al ERP — solo afecta state local del módulo (Marten + proyecciones). No requiere outbox de Wolverine para integraciones (ADR-006 no aplica directamente acá). El outbox transaccional sí está activo para garantizar atomicidad evento + proyección + envelope dedup en un único commit.

## 8. Impacto en proyecciones / read models

- **`InspeccionAbiertaPorEquipoView` (§15.12.6) — NUEVA proyección inline.** Upsert con clave `EquipoId`. Filtra por `Estado='EnEjecucion'`. Se llena en `Apply(InspeccionIniciada_v1)`; se borra cuando llegue `InspeccionFirmada_v1` o `InspeccionCancelada_v1` (slices futuros). **Índice único parcial Postgres sobre `EquipoId WHERE Estado='EnEjecucion'`** — defiende I-I1 ante race conditions.
- **`BandejaTecnicoView` (§15.12.3)** — agrega fila con la inspección recién creada para que aparezca en la bandeja del técnico. Es proyección async (no necesita atomicidad con el commit del evento).
- **`DetalleInspeccionView` (§15.12.1)** — proyección async. Crea documento detalle con todos los campos del evento.
- **Catálogos consumidos (read-only):** `EquipoLocal`, `RutinaTecnicaLocal`. Sincronizados via ADR-004 (sync on-login + ETag canonical, post refinamientos 2026-05-05). El handler los consulta vía `IDocumentSession`; no los modifica.

## 9. Impacto en endpoints HTTP

> **Fuera del alcance de 1a.** El endpoint HTTP `POST /api/v1/inspecciones` se expone en slice 1b. El aggregate puro de este slice no toca HTTP. El detalle del contrato HTTP queda como referencia abajo para que 1b no pierda contexto.

- **Método + ruta:** `POST /api/v1/inspecciones`.
- **Headers requeridos:**
  - `X-Client-Command-Id: <UUIDv7>` (idempotencia ADR-008).
  - `Authorization` heredado del host PWA (claims técnico + capability).
- **Request DTO:**
  ```json
  {
    "equipoId": 4521,
    "proyectoId": 3,
    "ubicacionInicio": {
      "latitud": 4.711,
      "longitud": -74.072,
      "precisionMetros": 8.5,
      "capturadoEn": "2026-05-05T08:30:12-05:00"
    },
    "fechaReportada": "2026-05-05",
    "lecturaMedidorPrimario": { "tipo": "Hr", "valor": 4523.5, "capturadoEn": "2026-05-05T08:30:10-05:00" },
    "lecturaMedidorSecundario": null
  }
  ```
- **Response 201 Created (inicio nuevo):**
  - `Location: /api/v1/inspecciones/{inspeccionId}`
  - Body:
    ```json
    {
      "inspeccionId": "0193a4f7-...",
      "redirigeAExistente": false,
      "version": 1
    }
    ```
- **Response 200 OK (redirige a existente, I-I1 shortcut):**
  - Body:
    ```json
    {
      "inspeccionId": "<inspeccionId activa preexistente>",
      "redirigeAExistente": true,
      "version": <versión actual del stream existente>,
      "mensaje": "Ya hay inspección activa, abriendo la existente"
    }
    ```
- **Response 409 Conflict (idempotencia dedup):** Wolverine devuelve la respuesta original del primer procesamiento (mismo `inspeccionId`, mismo `redirigeAExistente`).
- **Response 422 Unprocessable Entity:** violación de PRE-4..PRE-7 o I-I3. Body con `codigoError` (`PRE-4`, `I-I2`, `I-I3`, etc.) y `mensaje` accionable.
- **Response 403 Forbidden:** PRE-1 o PRE-2 (capability faltante o proyecto no autorizado).
- **Response 404 Not Found:** PRE-3 (equipo no existe).
- **Rol/permiso requerido:** capability `ejecutar-inspeccion` con asignación al `proyectoId` del request. La capa HTTP del módulo lee claims del contexto inyectado por el host PWA (mecanismo concreto del host pendiente — ADR-002 tentativo); el handler recibe `ClaimsTecnico` por parámetro.

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `InspeccionIniciada_v1` no genera push hacia el frontend en el catálogo vigente de ADR-005 (`§14`, post refinamiento 2026-05-05). El push es para eventos de cierre (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`) y para `InspeccionEstadoCambiado` (futuro, multi-técnico colaborando — fuera de MVP de este slice).

> Cuando emerja la necesidad de "ver inspecciones nuevas en bandeja del proyecto en tiempo real" (caso multi-técnico), se agregará un evento push `InspeccionEstadoCambiado` con audiencia `Group=proyectoId`. Followup posible para slice posterior.

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `IniciarInspeccion` no consume ni publica hacia el ERP — trabaja exclusivamente con read models locales del módulo (`EquipoLocal`, `RutinaTecnicaLocal`, `InspeccionAbiertaPorEquipoView`). Estos catálogos se llenan vía sync (ADR-004) gestionado por slices distintos.

## 12. Preguntas abiertas

- [x] ¿El `clientCommandId` del header `X-Client-Command-Id` es **requerido** o **opcional** en MVP? **Resuelto:** requerido (la PWA siempre lo genera; ADR-008 lo establece como contrato vinculante; sin él, fallback a `Guid.NewGuid()` server-side rompe idempotencia ante retry de cliente).
- [x] Si el cliente envía `clientCommandId` que no coincide con UUIDv7 (otro UUID válido), ¿se acepta? **Resuelto:** sí, se acepta cualquier UUID válido. UUIDv7 es preferencia (orden temporal natural, mejor para particionado del envelope storage), no requisito de validación.
- [x] Si el técnico inicia una inspección sin lecturas de medidores, ¿puede agregarlas después? **Resuelto:** sí, vía comando hermano `RegistrarLecturaMedidor` (slice futuro). Este slice solo modela la captura inicial opcional.
- [x] ¿El response 200 (redirige a existente) debe incluir el snapshot del estado actual del stream (hallazgos ya capturados, etc.)? **Resuelto:** no en este slice. El frontend hace `GET /api/v1/inspecciones/{id}` por separado para cargar detalle. Mantiene este endpoint focalizado en "iniciar".
- [x] ¿Este slice debe registrar también el endpoint `GET /api/v1/inspecciones/{id}` para que el response sea utilizable? **Resuelto:** no. Ese endpoint es slice independiente de read model. El response 201 incluye `Location` apuntando ahí pero el GET puede 404 hasta que ese slice se implemente. Para MVP de este slice basta con que `POST` cree el stream + proyecciones; el detalle se accede vía read models en slices posteriores.

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-7) mapean a un escenario Then en §6 (PRE-1 y PRE-2 son auth de capa HTTP; PRE-3 es 404 trivial cubierto en endpoint; PRE-4..PRE-7 mapeados a 6.5, 6.6, 6.7, 6.8 + 6.9).
- [x] Todas las invariantes tocadas (I-I1, I-I2, I-I3) mapean a un escenario Then (I-I1 → 6.10 + 6.11; I-I2 → 6.6 + 6.7; I-I3 → 6.8 + 6.9).
- [x] El happy path está presente (6.1, 6.2, 6.3 cubren variantes).
- [x] El comando emite ≥1 evento → escenario de rebuild desde stream presente (6.12).
- [x] Preguntas abiertas todas respondidas.
- [x] Slice no toca endpoint Sinco — no requiere mock.
