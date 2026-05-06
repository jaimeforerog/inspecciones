# Slice 2 — ActualizarHallazgo

**Autor:** domain-modeler
**Fecha:** 2026-05-06
**Estado:** draft
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `slices/1c-registrar-hallazgo/spec.md` — `HallazgoRegistrado_v1`, invariantes I-H1..I-H11, shape del value object `Hallazgo`
- `01-modelo-dominio.md §15.2` — estructura canónica del `Hallazgo` value object
- `01-modelo-dominio.md §15.3` — invariantes I-H7 (editable solo en EnEjecucion), I-H8 (campos inmutables), I-H4 (RequiereIntervencion → tipo/causa obligatorios), I-H5 (otras acciones → tipo/causa opcionales)
- `01-modelo-dominio.md §15.4` — catálogo MVP; `HallazgoActualizado_v1` es el evento #7
- ADR-008 (`§9.16`) — idempotencia por `X-Client-Command-Id` (mismo patrón que slices 1b/1c)

---

## 1. Intención

El técnico necesita corregir datos de un hallazgo que ya registró durante la misma sesión de inspección — por ejemplo, cambiar la acción requerida de seguimiento a intervención, ajustar la novedad técnica o actualizar la observación de campo. La edición solo es posible mientras la inspección sigue en ejecución. Los campos que identifican el origen del hallazgo (`Origen`, `NovedadPreopOrigenId`, `SeguimientoOrigenId`, `ParteEquipoId`) son inmutables — no se pueden cambiar tras el registro inicial.

---

## 2. Comando

```csharp
public sealed record ActualizarHallazgo(
    Guid   InspeccionId,
    Guid   HallazgoId,
    int?   ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int?   TipoFallaId,
    int?   CausaFallaId,
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,
    string ActualizadoPor
);
```

Nota: los campos `Origen`, `NovedadPreopOrigenId`, `SeguimientoOrigenId` y `ParteEquipoId` no forman parte del payload — son inmutables por I-H8 y no se aceptan como input.

---

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `HallazgoActualizado_v1` | `InspeccionId`, `HallazgoId`, `ActividadId?`, `ActividadDescripcion?`, `NovedadTecnica`, `AccionRequerida`, `AccionCorrectiva?`, `TipoFallaId?`, `CausaFallaId?`, `ObservacionCampo?`, `Ubicacion?`, `ActualizadoPor`, `ActualizadoEn` | Al actualizarse exitosamente un hallazgo existente y activo. |

---

## 4. Precondiciones

- `PRE-1`: La inspección debe existir en el stream. Si no existe, el handler devuelve 404 antes de llegar al método de decisión — no es una excepción de dominio.
- `PRE-2`: La inspección debe estar en estado `EnEjecucion` — excepción: `InspeccionNoEnEjecucionException`.
- `PRE-3`: El `HallazgoId` referenciado debe existir en la lista de hallazgos de la inspección — excepción: `HallazgoNoEncontradoException`.
- `PRE-4`: El hallazgo referenciado no debe estar eliminado (`Eliminado = false`) — excepción: `HallazgoEliminadoException`.
- `PRE-5` (I-H4): Si `AccionRequerida = RequiereIntervencion` → `TipoFallaId` y `CausaFallaId` deben ser no-null — excepción: `TipoYCausaFallaRequeridosException`.
- `PRE-6`: Si `AccionRequerida = RequiereIntervencion` → `AccionCorrectiva` debe ser no-vacía — excepción: `AccionCorrectivaRequeridaException`.
- `PRE-7`: `NovedadTecnica` no puede ser vacía o solo espacios — excepción: `NovedadTecnicaVaciaException`.

> **Capa donde viven**: las pre-condiciones PRE-2 a PRE-7 se evalúan en el **método de decisión** `ActualizarHallazgo` del agregado. PRE-1 vive en el handler (lectura del stream Marten). Los `Apply` son puros — nunca validan.

---

## 5. Invariantes tocadas

- `I-H7` — Editable solo si la inspección está en estado `EnEjecucion`. Cubre PRE-2.
- `I-H8` — `HallazgoActualizado_v1` no puede modificar `Origen`, `NovedadPreopOrigenId`, `SeguimientoOrigenId`, `ParteEquipoId`. Garantizado por diseño: esos campos no están en el payload del comando.
- `I-H4` — `AccionRequerida = RequiereIntervencion` → `TipoFallaId` y `CausaFallaId` obligatorios. Cubre PRE-5.
- `I-H5` — Otras acciones: `TipoFallaId`/`CausaFallaId` opcionales (el inverso de I-H4 — el método de decisión no fuerza null, solo no los exige).

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — actualizar hallazgo Manual con AccionRequerida=RequiereIntervencion

**Given**
- `InspeccionIniciada_v1` con estado resultante `EnEjecucion`
- `HallazgoRegistrado_v1` con `HallazgoId=H1`, `Origen=Manual`, `AccionRequerida=NoRequiereIntervencion`

**When**
- `ActualizarHallazgo(InspeccionId, HallazgoId=H1, NovedadTecnica="Fisura en bloque motor", AccionRequerida=RequiereIntervencion, AccionCorrectiva="Reemplazar bloque", TipoFallaId=10, CausaFallaId=5, ActualizadoPor="tecnico-01")`

**Then**
- emite `HallazgoActualizado_v1` con `HallazgoId=H1`, `NovedadTecnica="Fisura en bloque motor"`, `AccionRequerida=RequiereIntervencion`, `AccionCorrectiva="Reemplazar bloque"`, `TipoFallaId=10`, `CausaFallaId=5`, `ActualizadoPor="tecnico-01"`, `ActualizadoEn` no-null.

### 6.2 Happy path — actualizar hallazgo PreOperacional sin intervención

**Given**
- `InspeccionIniciada_v1` con estado resultante `EnEjecucion`
- `HallazgoRegistrado_v1` con `HallazgoId=H2`, `Origen=PreOperacional`, `NovedadPreopOrigenId=99`, `AccionRequerida=RequiereIntervencion`, `TipoFallaId=7`, `CausaFallaId=3`

**When**
- `ActualizarHallazgo(InspeccionId, HallazgoId=H2, NovedadTecnica="Vibración leve en eje", AccionRequerida=RequiereSeguimiento, AccionCorrectiva=null, TipoFallaId=null, CausaFallaId=null, ActualizadoPor="tecnico-02")`

**Then**
- emite `HallazgoActualizado_v1` con `HallazgoId=H2`, `AccionRequerida=RequiereSeguimiento`, `TipoFallaId=null`, `CausaFallaId=null`.

### 6.3 Violación de precondición PRE-2 — inspección no en ejecución (I-H7)

**Given**
- `InspeccionIniciada_v1`
- `HallazgoRegistrado_v1` con `HallazgoId=H1`
- `InspeccionFirmada_v1` (estado resultante: `Firmada`)

**When**
- `ActualizarHallazgo(InspeccionId, HallazgoId=H1, ...)`

**Then**
- lanza `InspeccionNoEnEjecucionException` con mensaje que menciona el estado actual.

### 6.4 Violación de precondición PRE-3 — HallazgoId no existe

**Given**
- `InspeccionIniciada_v1` con estado `EnEjecucion`

**When**
- `ActualizarHallazgo(InspeccionId, HallazgoId=Guid-desconocido, ...)`

**Then**
- lanza `HallazgoNoEncontradoException`.

### 6.5 Violación de precondición PRE-4 — hallazgo eliminado

**Given**
- `InspeccionIniciada_v1`
- `HallazgoRegistrado_v1` con `HallazgoId=H1`
- `HallazgoEliminado_v1` con `HallazgoId=H1` (estado resultante: `Eliminado=true`)

**Then**
- lanza `HallazgoEliminadoException`.

> Nota: el `Apply(HallazgoEliminado_v1)` es un stub en este slice (el comando `EliminarHallazgo` se modela en slice 3). El agregado debe incluir el `case` en `AplicarEvento` para que el rebuild desde stream no lance `InvalidOperationException`. Ver §7 idempotencia + FOLLOWUPS.

### 6.6 Violación de invariante I-H4 — RequiereIntervencion sin tipo/causa de falla (PRE-5)

**Given**
- `InspeccionIniciada_v1`
- `HallazgoRegistrado_v1` con `HallazgoId=H1`, `AccionRequerida=RequiereSeguimiento`

**When**
- `ActualizarHallazgo(..., AccionRequerida=RequiereIntervencion, TipoFallaId=null, CausaFallaId=null, AccionCorrectiva="Reparar")`

**Then**
- lanza `TipoYCausaFallaRequeridosException`.

### 6.7 Violación de PRE-6 — RequiereIntervencion sin AccionCorrectiva

**Given**
- `InspeccionIniciada_v1`
- `HallazgoRegistrado_v1` con `HallazgoId=H1`

**When**
- `ActualizarHallazgo(..., AccionRequerida=RequiereIntervencion, TipoFallaId=5, CausaFallaId=3, AccionCorrectiva=null)`

**Then**
- lanza `AccionCorrectivaRequeridaException`.

### 6.8 Violación de PRE-7 — NovedadTecnica vacía

**Given**
- `InspeccionIniciada_v1`
- `HallazgoRegistrado_v1` con `HallazgoId=H1`

**When**
- `ActualizarHallazgo(..., NovedadTecnica="   ", AccionRequerida=NoRequiereIntervencion)`

**Then**
- lanza `NovedadTecnicaVaciaException`.

### 6.9 Rebuild desde stream (obligatorio)

**Given** el agregado en estado vacío

**When** se reproyectan en orden causal:
1. `InspeccionIniciada_v1`
2. `HallazgoRegistrado_v1` con `HallazgoId=H1`, `AccionRequerida=NoRequiereIntervencion`
3. `HallazgoActualizado_v1` con `HallazgoId=H1`, `AccionRequerida=RequiereIntervencion`, `TipoFallaId=10`, `CausaFallaId=5`

**Then**
- el estado resultante tiene `Estado=EnEjecucion`, el hallazgo H1 con `AccionRequerida=RequiereIntervencion`, `TipoFallaId=10`, `CausaFallaId=5`.
- ningún `Apply` lanza excepción.

---

## 7. Idempotencia / retries

El comando se envía con `X-Client-Command-Id` en el header HTTP (mismo patrón ADR-008 que slice 1c). Si el mismo `X-Client-Command-Id` se recibe dos veces, el handler devuelve la respuesta cacheada sin re-ejecutar el método de decisión (dedup en Wolverine — FOLLOWUPS #15). Si el hallazgo ya tiene el mismo estado que el comando intentaría producir, el método de decisión igual emite `HallazgoActualizado_v1` — la idempotencia a nivel de negocio es responsabilidad del cliente (no se implementa dedup semántico en el dominio).

No hay integración con Sinco on-prem en este slice — no aplica ADR-003/ADR-006.

---

## 8. Impacto en proyecciones / read models

- `DetalleInspeccionView` (§15.12.1): actualizar los campos mutables del hallazgo `H1` al recibir `HallazgoActualizado_v1`. Los campos `Origen`, `ParteEquipoId`, `NovedadPreopOrigenId` no cambian.
- La proyección `InspeccionAbiertaPorEquipoView` no se ve afectada por este evento (solo reacciona a `InspeccionIniciada_v1`).
- Si no hay `DetalleInspeccionView` implementada aún (pospuesta por roadmap), se documenta como no-op en infra-wire y se abre followup.

---

## 9. Impacto en endpoints HTTP

- Método + ruta: `PATCH /api/v1/inspecciones/{id}/hallazgos/{hid}`
- DTO request:
  ```json
  {
    "actividadId": 12,
    "actividadDescripcion": "Inspección sistema de enfriamiento",
    "novedadTecnica": "Fisura en bloque motor",
    "accionRequerida": "RequiereIntervencion",
    "accionCorrectiva": "Reemplazar bloque",
    "tipoFallaId": 10,
    "causaFallaId": 5,
    "observacionCampo": "Detectado al revisar temperatura",
    "ubicacion": { "latitud": 4.710, "longitud": -74.072, "precisionMetros": 5.0, "capturadoEn": "2026-05-06T10:00:00Z" }
  }
  ```
- Response happy path: `200 OK` con body `{ "hallazgoId": "...", "actualizadoEn": "..." }`.
- Códigos de error:
  - `404 Not Found` — inspección no existe o hallazgo no encontrado (`HallazgoNoEncontradoException`).
  - `409 Conflict` — inspección no en ejecución (`InspeccionNoEnEjecucionException`) o hallazgo eliminado (`HallazgoEliminadoException`).
  - `422 Unprocessable Entity` — violaciones de invariante (I-H4, PRE-6, PRE-7).
- Rol/permiso requerido: claim `tecnico` con `InspeccionId` en scope del request. Mecanismo: ADR-002 (tentativo). El handler recibe `ActualizadoPor` como claim `sub` del JWT validado por el host.
- Header idempotencia: `X-Client-Command-Id: {clientGeneratedUuid}` (ADR-008).

---

## 10. Impacto en SignalR / push

No aplica en este slice. `HallazgoActualizado_v1` no está en el catálogo de eventos push de ADR-005 — la UI actualiza su estado local tras recibir la respuesta HTTP 200.

---

## 11. Impacto en adapters Sinco on-prem

No aplica en este slice. La edición de un hallazgo es puramente interna al aggregate — no genera OT ni publica hacia el ERP.

---

## 12. Preguntas abiertas

Ninguna. Todas las ambigüedades están resueltas por §15.3 (fuente de verdad sobre I-H8) y el patrón establecido en slice 1c.

Asunciones con justificación:
- **`ActividadId` permanece `int?`** (mismo tipo que en `HallazgoRegistrado_v1` / `RegistrarHallazgo.cs`). El modelo §15.2 menciona `ActividadRutinaId: Guid?` en la sección de value object pero el código existente usa `int?` alineado con la convención de IDs del ERP. Se mantiene `int?` para consistencia con el código ya cerrado en slice 1c — si hay divergencia, es followup de refinamiento sobre §15.2, no bloqueante para este slice.
- **`HallazgoEliminado_v1` stub**: el `Apply(HallazgoEliminado_v1)` se agrega como stub en `AplicarEvento` para que el escenario 6.5 compile y el rebuild no rompa. El comando `EliminarHallazgo` se modela completo en slice 3.
- **`Hallazgo` value object extendido**: el record `Hallazgo.cs` actual solo guarda `AccionRequerida`, `TipoFallaId`, `CausaFallaId`, `Eliminado` (y los campos de origen). Para que `Apply(HallazgoActualizado_v1)` pueda mutar los campos editables, el record debe extenderse con `ActividadId?`, `ActividadDescripcion?`, `NovedadTecnica`, `AccionCorrectiva?`, `ObservacionCampo?`. Esta extensión forma parte del green phase de este slice.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones mapean a un escenario Then (6.2..6.8 cubren PRE-2..PRE-7; PRE-1 vive en handler, no en dominio).
- [x] Todas las invariantes tocadas mapean a un escenario Then (I-H7 → 6.3; I-H8 → garantizado por diseño del payload; I-H4 → 6.6).
- [x] El happy path está presente (6.1, 6.2).
- [x] El escenario de rebuild desde stream está presente (6.9).
- [x] Preguntas abiertas están todas resueltas o marcadas como asunción con justificación.
- [x] No hay endpoints Sinco on-prem involucrados — no aplica `🟡 mock-only`.
