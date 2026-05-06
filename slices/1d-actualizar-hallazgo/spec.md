# Slice 1d — ActualizarHallazgo

**Autor:** domain-modeler
**Fecha:** 2026-05-06
**Estado:** firmado
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `slices/1c-registrar-hallazgo/spec.md` — `HallazgoRegistrado_v1`, invariantes I-H1..I-H6, INV-PartePerteneceAlEquipo (I-H12 propuesto), PRE-* de registro; envelope dedup ADR-008
- `01-modelo-dominio.md §12.10.6` — semántica de edición de hallazgos; Apply de `HallazgoActualizado_v1`
- `01-modelo-dominio.md §15.2` — estructura del value object `Hallazgo` (campos editables vs. inmutables)
- `01-modelo-dominio.md §15.3` — invariantes `I-H4`, `I-H5`, `I-H7`, `I-H8`
- `01-modelo-dominio.md §15.4` — evento #7 `HallazgoActualizado_v1` en catálogo MVP
- `01-modelo-dominio.md §15.7` — inmutabilidad post-firma; el aggregate rechaza todo comando fuera de `EnEjecucion`
- `01-modelo-dominio.md §15.12.5` — `BandejaInspeccionesPendientesOTView` consume `HallazgoActualizado_v1` para recomputar presencia de `RequiereIntervencion`
- ADR-002 (tentativo) — identidad 100 % del host PWA; `tecnicoId` opaco del JWT
- ADR-008 (`§9.16`) — idempotencia por `X-Client-Command-Id`

---

## 1. Intención

El técnico necesita corregir o completar los datos de un hallazgo que ya registró durante la inspección en curso. Puede cambiar la descripción técnica, la acción requerida, la acción correctiva, el tipo y causa de falla, la observación de campo, o recapturar la ubicación GPS. No puede cambiar la parte del equipo ni el origen del hallazgo — esos campos son inmutables (I-H8). El comando solo tiene efecto mientras la inspección está `EnEjecucion` (I-H7).

---

## 2. Comando

```csharp
public sealed record ActualizarHallazgo(
    Guid             InspeccionId,
    Guid             HallazgoId,
    string           NovedadTecnica,          // no vacío — I-H4 align + PRE-C
    AccionRequerida  AccionRequerida,          // NoRequiereIntervencion | RequiereSeguimiento | RequiereIntervencion
    string?          AccionCorrectiva,         // obligatorio si AccionRequerida = RequiereIntervencion — PRE-D
    int?             TipoFallaId,             // obligatorio si AccionRequerida = RequiereIntervencion — PRE-D (I-H4)
    int?             CausaFallaId,            // obligatorio si AccionRequerida = RequiereIntervencion — PRE-D (I-H4)
    string?          ObservacionCampo,         // texto libre opcional — siempre aceptado
    UbicacionGps?    UbicacionGps,            // opcional — técnico puede recapturar GPS
    string           TecnicoId               // extraído del JWT por la capa API; el dominio lo recibe como parámetro
) : ICommand;
```

**Campos que este comando NO recibe** (inmutables por I-H8):

- `Origen` — inmutable desde `HallazgoRegistrado_v1`.
- `ParteEquipoId` — inmutable desde `HallazgoRegistrado_v1`.
- `NovedadPreopOrigenId` — inmutable desde `HallazgoRegistrado_v1`.
- `SeguimientoOrigenId` — inmutable desde `HallazgoRegistrado_v1`.

---

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `HallazgoActualizado_v1` | Ver campos a continuación | Siempre que el comando supere todas las precondiciones e invariantes. Un único evento por invocación. |

```csharp
public sealed record HallazgoActualizado_v1(
    Guid             InspeccionId,
    Guid             HallazgoId,
    string           NovedadTecnica,
    AccionRequerida  AccionRequerida,
    string?          AccionCorrectiva,         // presente si AccionRequerida = RequiereIntervencion; null en otro caso
    int?             TipoFallaId,             // presente si AccionRequerida = RequiereIntervencion; null en otro caso
    int?             CausaFallaId,            // presente si AccionRequerida = RequiereIntervencion; null en otro caso
    string?          ObservacionCampo,
    UbicacionGps?    UbicacionGps,
    DateTimeOffset   ActualizadoEn,           // TimeProvider.GetUtcNow() en el handler — no editable por el cliente
    string           TecnicoId               // claim inyectado por el host PWA
);
```

**Nota sobre `ParteEquipoId`:** §15.3 I-H8 establece que `HallazgoActualizado_v1` no puede modificar `Origen`, `NovedadPreopOrigenId`, `SeguimientoOrigenId` ni `ParteEquipoId`. La fuente de verdad es §15.3. `ParteEquipoId` no viaja en el payload del evento ni en el comando — el valor original persiste inmutable en `HallazgoRegistrado_v1`. El estado del aggregate reconstruye la parte desde ese evento inicial.

**Nota sobre campos nulos en transición:** si el técnico cambia `AccionRequerida` de `RequiereIntervencion` a `RequiereSeguimiento` o `NoRequiereIntervencion`, los campos `AccionCorrectiva`, `TipoFallaId` y `CausaFallaId` deben venir en `null` en el comando (PRE-E). El evento los persiste en `null`, borrando los valores anteriores del state del aggregate.

---

## 4. Precondiciones

Las precondiciones PRE-A y PRE-B viven en el **método de decisión del aggregate**. PRE-0 vive en la capa HTTP. PRE-F vive en el handler (acceso a Marten para verificar existencia de stream). Los `Apply` son puros y nunca re-validan.

- **PRE-0 (capa HTTP):** capability `ejecutar-inspeccion` requerida. Si el claim está ausente → `403 Forbidden`. Mismo mecanismo que slices 1b y 1c.
- **PRE-F (handler):** `InspeccionId` debe existir como stream en Marten. El handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).
- **PRE-A (aggregate — método de decisión):** `Estado == EnEjecucion` (I-H7). Si la inspección está `Firmada`, `Cerrada`, `CerradaSinOT`, `CierrePendienteOT` o `Cancelada` → `InspeccionNoEnEjecucionException` (`422 Unprocessable Entity`). Cubre también el caso de inmutabilidad post-firma (§15.7).
- **PRE-B (aggregate — método de decisión):** el `HallazgoId` debe existir en `_hallazgos` del aggregate y no estar marcado como eliminado (`Eliminado == false`). Si no existe → `HallazgoNoEncontradoException` (`404 Not Found`). Si existe pero está eliminado → `HallazgoEliminadoException` (`422 Unprocessable Entity`).
- **PRE-C (aggregate — método de decisión):** `NovedadTecnica` no puede ser `null`, vacío ni solo whitespace. Excepción: `NovedadTecnicaVaciaException` (`422 Unprocessable Entity`).
- **PRE-D (aggregate — método de decisión):** si `AccionRequerida == RequiereIntervencion`:
  - `AccionCorrectiva` debe ser non-`null` y no vacío. Excepción: `AccionCorrectivaRequeridaException`.
  - `TipoFallaId` debe ser non-`null`. Excepción: `TipoYCausaFallaRequeridosException`.
  - `CausaFallaId` debe ser non-`null`. Excepción: `TipoYCausaFallaRequeridosException`.
  Alineado con I-H4.
- **PRE-E (aggregate — método de decisión):** si `AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}`, entonces `AccionCorrectiva`, `TipoFallaId` y `CausaFallaId` deben ser `null`. Si alguno viene poblado → `CamposIntervencionNoPermitidosException` (`422 Unprocessable Entity`). Alineado con I-H5; garantiza coherencia al hacer downgrade de `AccionRequerida`.

> **Capa donde viven:** PRE-0 en capa HTTP; PRE-F en el handler (acceso a Marten); PRE-A, PRE-B, PRE-C, PRE-D, PRE-E en el método de decisión del aggregate. Los `Apply(HallazgoActualizado_v1)` son puros — no re-validan ninguna de estas condiciones.

---

## 5. Invariantes tocadas

- **I-H4** (`§15.3`): `AccionRequerida = RequiereIntervencion` → `TipoFallaId`, `CausaFallaId` y `AccionCorrectiva` obligatorios. Cubierta por PRE-D.
- **I-H5** (`§15.3`): `AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}` → `TipoFallaId`, `CausaFallaId` pueden ser `null`. Este slice extiende I-H5 con la regla simétrica del PRE-E: los campos de intervención deben ser `null` al hacer downgrade (coherencia activa, no solo permisividad pasiva).
- **I-H7** (`§15.3`): editable solo si la inspección está `EnEjecucion`. Cubierta por PRE-A.
- **I-H8** (`§15.3`): `HallazgoActualizado_v1` no puede modificar `Origen`, `NovedadPreopOrigenId`, `SeguimientoOrigenId`, `ParteEquipoId`. Cubierta por la ausencia de esos campos en el comando y el evento — el método de decisión nunca los lee del comando para producir el evento.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — cambio de descripción, AccionRequerida=NoRequiereIntervencion a RequiereIntervencion

**Given**
- Stream `inspeccion-{X}` contiene `[InspeccionIniciada_v1, HallazgoRegistrado_v1(HallazgoId=G1, AccionRequerida=NoRequiereIntervencion, TipoFallaId=null, CausaFallaId=null)]`.
- `Estado=EnEjecucion`, `_hallazgos[G1].Eliminado=false`.

**When**
- Comando `ActualizarHallazgo(InspeccionId=X, HallazgoId=G1, NovedadTecnica="Fuga confirmada en sello hidráulico — revisión detallada", AccionRequerida=RequiereIntervencion, AccionCorrectiva="Reemplazar sello hidráulico", TipoFallaId=3, CausaFallaId=12, ObservacionCampo="Fuga visible con luz UV", UbicacionGps=UbicacionGps(4.711,-74.072,8.5,now), TecnicoId="rmartinez")`.

**Then**
- Se emite exactamente un `HallazgoActualizado_v1` con `HallazgoId=G1`, `NovedadTecnica` y `AccionCorrectiva` como en el comando, `TipoFallaId=3`, `CausaFallaId=12`, `ActualizadoEn=DateTimeOffset.UtcNow(TimeProvider)`.
- `_hallazgos[G1].AccionRequerida=RequiereIntervencion`, `_hallazgos[G1].TipoFallaId=3`, `_hallazgos[G1].CausaFallaId=12` en el aggregate en memoria.
- `_hallazgos[G1].Origen` y `_hallazgos[G1].ParteEquipoId` permanecen inalterados (I-H8).

### 6.2 Happy path — downgrade de RequiereIntervencion a RequiereSeguimiento (campos de intervención se limpian)

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G2, AccionRequerida=RequiereIntervencion, TipoFallaId=5, CausaFallaId=7, AccionCorrectiva="Cambiar filtro")]`.
- `Estado=EnEjecucion`, `_hallazgos[G2].Eliminado=false`.

**When**
- Comando `ActualizarHallazgo(InspeccionId=X, HallazgoId=G2, NovedadTecnica="Desgaste menor, solo seguimiento", AccionRequerida=RequiereSeguimiento, AccionCorrectiva=null, TipoFallaId=null, CausaFallaId=null, ObservacionCampo=null, UbicacionGps=null, TecnicoId="rmartinez")`.

**Then**
- Se emite `HallazgoActualizado_v1` con `AccionRequerida=RequiereSeguimiento`, `AccionCorrectiva=null`, `TipoFallaId=null`, `CausaFallaId=null`.
- `_hallazgos[G2].AccionRequerida=RequiereSeguimiento`, `_hallazgos[G2].TipoFallaId=null`, `_hallazgos[G2].CausaFallaId=null`, `_hallazgos[G2].AccionCorrectiva=null` en el aggregate.
- Los valores anteriores (`TipoFallaId=5`, `CausaFallaId=7`) quedan únicamente en el `HallazgoRegistrado_v1` del stream (auditables) pero no en el state vigente.

### 6.3 Happy path — recaptura de GPS sin cambiar AccionRequerida

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G3, UbicacionGps=null, AccionRequerida=NoRequiereIntervencion)]`.
- `Estado=EnEjecucion`.

**When**
- Comando `ActualizarHallazgo(InspeccionId=X, HallazgoId=G3, NovedadTecnica="Desgaste superficial en manguera", AccionRequerida=NoRequiereIntervencion, AccionCorrectiva=null, TipoFallaId=null, CausaFallaId=null, ObservacionCampo=null, UbicacionGps=UbicacionGps(4.712,-74.071,5.0,now), TecnicoId="rmartinez")`.

**Then**
- Se emite `HallazgoActualizado_v1` con `UbicacionGps` poblado.
- `_hallazgos[G3].UbicacionGps` actualizado en el aggregate.
- Ningún campo inmutable modificado (I-H8).

### 6.4 Happy path — actualización sin cambiar AccionRequerida=RequiereIntervencion (solo texto)

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G4, AccionRequerida=RequiereIntervencion, TipoFallaId=2, CausaFallaId=8, AccionCorrectiva="Primer texto")]`.
- `Estado=EnEjecucion`.

**When**
- Comando con `HallazgoId=G4`, `AccionRequerida=RequiereIntervencion`, `AccionCorrectiva="Texto corregido con más detalle"`, `TipoFallaId=2`, `CausaFallaId=8`, `NovedadTecnica="Descripción más completa"`.

**Then**
- Se emite `HallazgoActualizado_v1` con `AccionCorrectiva="Texto corregido con más detalle"`, `TipoFallaId=2`, `CausaFallaId=8`.
- `_hallazgos[G4].AccionCorrectiva` actualizado; `TipoFallaId` y `CausaFallaId` permanecen iguales.

### 6.5 Violación PRE-A (I-H7) — inspección no está en EnEjecucion

**Given**
- Aggregate con `Estado=Firmada` (se puede construir con `[InspeccionIniciada_v1, InspeccionFirmada_v1]`).
- `_hallazgos[G1].Eliminado=false`.

**When**
- Comando `ActualizarHallazgo(InspeccionId=X, HallazgoId=G1, ...)`.

**Then**
- Lanza `InspeccionNoEnEjecucionException` con mensaje que incluye el estado actual (`Firmada`).
- No se emite ningún evento.
- Código HTTP `422 Unprocessable Entity`.

### 6.6 Violación PRE-B — HallazgoId no existe en el aggregate

**Given**
- Aggregate `EnEjecucion` sin hallazgos (solo `InspeccionIniciada_v1`).

**When**
- Comando con `HallazgoId=G_INEXISTENTE`.

**Then**
- Lanza `HallazgoNoEncontradoException` con mensaje "El hallazgo {G_INEXISTENTE} no existe en la inspección {X}."
- No se emite evento.
- Código HTTP `404 Not Found`.

### 6.7 Violación PRE-B — HallazgoId existe pero está eliminado (soft delete)

**Given**
- Stream `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G5), HallazgoEliminado_v1(G5)]`.
- `_hallazgos[G5].Eliminado=true`.

**When**
- Comando con `HallazgoId=G5`.

**Then**
- Lanza `HallazgoEliminadoException` con mensaje "El hallazgo {G5} fue eliminado y no puede actualizarse."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`.

### 6.8 Violación PRE-C — NovedadTecnica vacía

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].Eliminado=false`.

**When**
- Comando con `NovedadTecnica=""` (o solo espacios).

**Then**
- Lanza `NovedadTecnicaVaciaException`.
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`.

### 6.9 Violación PRE-D (I-H4) — RequiereIntervencion sin TipoFallaId

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].Eliminado=false`.

**When**
- Comando con `AccionRequerida=RequiereIntervencion`, `TipoFallaId=null`, `CausaFallaId=9`, `AccionCorrectiva="Texto"`.

**Then**
- Lanza `TipoYCausaFallaRequeridosException` con mensaje "TipoFallaId y CausaFallaId son obligatorios cuando AccionRequerida=RequiereIntervencion."
- No se emite evento.

### 6.10 Violación PRE-D (I-H4) — RequiereIntervencion sin CausaFallaId

**Given / When** — mismo que 6.9 pero `TipoFallaId=3`, `CausaFallaId=null`.

**Then** — lanza `TipoYCausaFallaRequeridosException`.

### 6.11 Violación PRE-D — RequiereIntervencion sin AccionCorrectiva

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].Eliminado=false`.

**When**
- Comando con `AccionRequerida=RequiereIntervencion`, `TipoFallaId=3`, `CausaFallaId=12`, `AccionCorrectiva=null`.

**Then**
- Lanza `AccionCorrectivaRequeridaException` con mensaje "AccionCorrectiva es obligatoria cuando AccionRequerida=RequiereIntervencion."
- No se emite evento.

### 6.12 Violación PRE-E (I-H5) — NoRequiereIntervencion con TipoFallaId poblado

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].Eliminado=false`.

**When**
- Comando con `AccionRequerida=NoRequiereIntervencion`, `TipoFallaId=3`, `CausaFallaId=null`, `AccionCorrectiva=null`.

**Then**
- Lanza `CamposIntervencionNoPermitidosException` con mensaje "AccionCorrectiva, TipoFallaId y CausaFallaId deben ser null cuando AccionRequerida != RequiereIntervencion."
- No se emite evento.

### 6.13 Violación PRE-E (I-H5) — RequiereSeguimiento con AccionCorrectiva poblada

**Given / When** — `AccionRequerida=RequiereSeguimiento`, `AccionCorrectiva="Texto"`, `TipoFallaId=null`, `CausaFallaId=null`.

**Then** — lanza `CamposIntervencionNoPermitidosException`.

### 6.14 Violación PRE-F — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando con `InspeccionId=Z`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Código HTTP `404 Not Found`.
- No se emite evento.

### 6.15 Verificación de inmutabilidad I-H8 — los campos inmutables no cambian tras actualización

**Given**
- Stream `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G6, Origen=PreOperacional, ParteEquipoId=77, NovedadPreopOrigenId=1042)]`.

**When**
- Comando `ActualizarHallazgo(HallazgoId=G6, NovedadTecnica="Nueva descripción", AccionRequerida=NoRequiereIntervencion, ...)`.

**Then**
- Se emite `HallazgoActualizado_v1` sin campos `Origen`, `ParteEquipoId` ni `NovedadPreopOrigenId`.
- `_hallazgos[G6].Origen` permanece `PreOperacional`.
- `_hallazgos[G6].ParteEquipoId` permanece `77`.
- `_hallazgos[G6].NovedadPreopOrigenId` permanece `1042`.

### 6.16 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos previos).
- Lista de eventos en orden causal:
  1. `InspeccionIniciada_v1(InspeccionId=X, EquipoId=4521, Estado→EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Manual, ParteEquipoId=77, AccionRequerida=NoRequiereIntervencion, TipoFallaId=null, CausaFallaId=null)`
  3. `HallazgoActualizado_v1(HallazgoId=G1, NovedadTecnica="Descripción corregida", AccionRequerida=RequiereIntervencion, AccionCorrectiva="Reemplazar sello", TipoFallaId=3, CausaFallaId=12, ActualizadoEn=T1, TecnicoId="rmartinez")`

**When**
- Se reproyectan los tres eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- `Estado=EnEjecucion`.
- `_hallazgos.Count=1`.
- `_hallazgos[G1].AccionRequerida=RequiereIntervencion`.
- `_hallazgos[G1].TipoFallaId=3`, `_hallazgos[G1].CausaFallaId=12`, `_hallazgos[G1].AccionCorrectiva="Reemplazar sello"`.
- `_hallazgos[G1].Origen=Manual`, `_hallazgos[G1].ParteEquipoId=77` — inalterados (I-H8).
- `_hallazgos[G1].Eliminado=false`.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al que produce el método de decisión seguido de `Apply` in-process.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente envía `X-Client-Command-Id: <UUIDv7>` como header HTTP por cada tap "Guardar cambios". El header se mapea a `MessageId` Wolverine. Si Wolverine detecta el `MessageId` ya procesado (envelope dedup), devuelve la respuesta original sin re-ejecutar el handler. No se emite un segundo `HallazgoActualizado_v1`.

**Idempotencia y semántica de "última escritura gana":**

Si el técnico hace dos ediciones distintas sobre el mismo `HallazgoId` con dos `X-Client-Command-Id` diferentes (flujo normal), ambas se procesan y se emiten dos `HallazgoActualizado_v1` al stream — la segunda sobrescribe la primera en el state del aggregate. Esto es correcto: el historial de cambios queda íntegro en el stream y el state refleja el último estado.

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica.

**Atomicidad:** un único `IDocumentSession.SaveChangesAsync()` persiste el evento `HallazgoActualizado_v1` y actualiza las proyecciones afectadas (§8).

> **Nota sobre dedup real de Wolverine:** el mecanismo concreto de dedup (ADR-008) sigue siendo followup #15 del proyecto (igual que en slice 1c). El endpoint valida la presencia del header `X-Client-Command-Id` pero la dedup real a nivel Wolverine outbox store no está implementada. Este slice sigue el mismo patrón sin avanzar ese followup.

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` (§15.12.1) — actualización del hallazgo editado

`DetalleInspeccionView` consume `HallazgoActualizado_v1`. Al recibir el evento, la proyección reemplaza los campos editables del hallazgo con `HallazgoId` correspondiente:
- `NovedadTecnica`, `AccionRequerida`, `AccionCorrectiva`, `TipoFallaId`, `CausaFallaId`, `ObservacionCampo`, `UbicacionGps`, `ActualizadoEn`, `TecnicoId`.
- Los campos inmutables (`Origen`, `ParteEquipoId`, `NovedadPreopOrigenId`, `SeguimientoOrigenId`) no se tocan en el handler de proyección.

La proyección `DetalleInspeccionView` no está implementada aún — el slice documenta qué campos actualiza cuando se implemente (roadmap paso 3.45).

### 8.2 `BandejaInspeccionesPendientesOTView` (§15.12.5) — recomputo de presencia de RequiereIntervencion

La bandeja de aprobación de OT consume `HallazgoActualizado_v1` para recalcular si la inspección tiene ≥1 hallazgo activo con `AccionRequerida=RequiereIntervencion`. Si el técnico cambia un hallazgo de `RequiereIntervencion` a `RequiereSeguimiento` (downgrade), y ese era el único hallazgo con intervención, la inspección sale de la bandeja `EsperandoAprobacion`.

Caso contrario: si cambia de `NoRequiereIntervencion` a `RequiereIntervencion`, la inspección podría entrar a la bandeja una vez firmada. El recomputo ocurre sobre el state del aggregate derivado del stream — no hay valor persistido del conteo.

La proyección `BandejaInspeccionesPendientesOTView` no está implementada aún — se documenta el contrato esperado para cuando se implemente.

### 8.3 `BandejaTecnicoView` (§15.12.3) — no impactada

Muestra el estado de la inspección, no el detalle de hallazgos. Sin cambio en este slice.

### 8.4 `InspeccionAbiertaPorEquipoView` (§15.12.6) — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio en este slice.

---

## 9. Impacto en endpoints HTTP

**Endpoint nuevo:** `PUT /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}`

El verbo `PUT` es coherente con la semántica de actualización completa de los campos editables del hallazgo. El body contiene todos los campos editables (el cliente los envía completos, no solo el delta).

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO (`ActualizarHallazgoRequest`):**

```json
{
  "novedadTecnica": "Fuga confirmada en sello hidráulico — revisión detallada",
  "accionRequerida": "RequiereIntervencion",
  "accionCorrectiva": "Reemplazar sello hidráulico",
  "tipoFallaId": 3,
  "causaFallaId": 12,
  "observacionCampo": "Fuga visible con luz UV",
  "ubicacionGps": {
    "latitud": 4.711,
    "longitud": -74.072,
    "precisionMetros": 8.5,
    "capturadoEn": "2026-05-06T14:22:00.000Z"
  }
}
```

> `InspeccionId` y `HallazgoId` viajan en el path. `TecnicoId` se extrae del JWT en la capa API (ADR-002 tentativo — mock `const string tecnicoId = "rmartinez"` consistente con slices 1b y 1c hasta que ADR-002 esté resuelto — followup #14).

**Response `200 OK` (happy path):**

```json
{
  "hallazgoId": "0193a4f7-...",
  "inspeccionId": "...",
  "accionRequerida": "RequiereIntervencion",
  "actualizadoEn": "2026-05-06T14:22:00.000Z"
}
```

> Se usa `200 OK` en lugar de `201 Created` porque el recurso (hallazgo) ya existe — este endpoint actualiza, no crea.

**Códigos de error:**

| Escenario | Código HTTP | `codigoError` |
|---|---|---|
| Capability ausente (PRE-0) | `403 Forbidden` | `"PRE-0"` |
| InspeccionId no existe (PRE-F) | `404 Not Found` | `"PRE-F"` |
| HallazgoId no existe (PRE-B) | `404 Not Found` | `"PRE-B"` |
| HallazgoId eliminado (PRE-B variante) | `422 Unprocessable Entity` | `"PRE-B-ELIMINADO"` |
| Inspección no en EnEjecucion (PRE-A / I-H7) | `422 Unprocessable Entity` | `"I-H7"` |
| NovedadTecnica vacía (PRE-C) | `422 Unprocessable Entity` | `"PRE-C"` |
| TipoFallaId/CausaFallaId faltantes con RequiereIntervencion (PRE-D / I-H4) | `422 Unprocessable Entity` | `"I-H4"` |
| AccionCorrectiva faltante con RequiereIntervencion (PRE-D) | `422 Unprocessable Entity` | `"PRE-D-ACCION"` |
| Campos intervención presentes sin RequiereIntervencion (PRE-E / I-H5) | `422 Unprocessable Entity` | `"I-H5"` |

**Rol/permiso requerido:** capability `ejecutar-inspeccion` con el proyecto de la inspección asignado (heredado del host PWA — ADR-002 tentativo).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `HallazgoActualizado_v1` no está en el catálogo de eventos SignalR (ADR-005, §14 del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). La edición de hallazgos es una operación local del técnico — el resultado es visible inmediatamente en la pantalla del técnico sin necesidad de notificación en tiempo real a otras partes en MVP. Si en el futuro emerge colaboración multi-técnico en tiempo real, se añadiría push `HallazgoActualizado` como cambio aditivo sin afectar este slice.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `ActualizarHallazgo` es puramente local al módulo. No hay llamadas salientes al ERP on-prem. No se requiere WireMock. El adapter `POST /preop/novedades/{id}/verificar` (P-2) y cualquier otro endpoint Sinco relacionado con hallazgos son responsabilidad de `CerrarInspeccionSaga` (slice posterior). La actualización de un hallazgo no retroalimenta al ERP en tiempo de edición.

---

## 12. Preguntas abiertas

Todas resueltas antes de entregar esta spec.

- [x] **¿`ParteEquipoId` va en el payload del evento?** No. §15.3 I-H8 es la fuente de verdad: `HallazgoActualizado_v1` no puede modificar `ParteEquipoId`. El campo no viaja en el comando ni en el evento. La nota en §12.10.6 que lista `int ParteId` en el evento es una inconsistencia con §15.3; §15.3 tiene precedencia (establecido en el enunciado del slice y coherente con la decisión de diseño de inmutabilidad de la parte).

- [x] **¿Qué pasa si el técnico envía `AccionRequerida=RequiereSeguimiento` con `TipoFallaId` poblado?** PRE-E lo rechaza con `CamposIntervencionNoPermitidosException`. La coherencia es activa: no basta con que I-H5 "permita" que sean null — el handler exige que sean null cuando no aplican, para evitar datos huérfanos en el evento. Esta regla es el complemento simétrico de PRE-D / I-H4.

- [x] **¿`ActualizarHallazgo` puede actualizar un hallazgo de `Origen=Monitoreo`?** En principio sí, siempre que pase PRE-A y PRE-B (inspección `EnEjecucion`, hallazgo no eliminado). Los orígenes `Monitoreo` y `Seguimiento` tienen invariantes adicionales (I-H10, I-H11) que este slice no toca porque `ActualizarHallazgo` no modifica `Origen`. Si en el futuro el slice de `Origen=Monitoreo` necesita restricciones adicionales de edición, se modelan allí como invariantes nuevas. Asunción documentada: sin restricción diferencial por origen en este slice.

- [x] **¿Qué HTTP response code devuelve el happy path?** `200 OK`. El recurso ya existe; `PUT` sobre recurso existente → `200`. No `201 Created` (no hay creación) ni `204 No Content` (se devuelve el body con `ActualizadoEn` para que el cliente confirme el timestamp del sistema).

- [x] **¿El comando puede limpiar `ObservacionCampo` a `null`?** Sí. `ObservacionCampo` es siempre opcional. Si el técnico lo deja vacío en la UI, el cliente envía `null` y el evento lo persiste en `null`, reemplazando el valor anterior. No hay invariante que lo proteja.

- [x] **¿El comando puede limpiar `UbicacionGps` a `null`?** Sí. `UbicacionGps?` es opcional en el evento. Si el cliente envía `null`, el event state del hallazgo pierde la ubicación. No hay invariante que obligue a mantener GPS una vez capturado. Asunción conservadora: la UI debería confirmar antes de limpiar, pero eso es una decisión de UX, no de dominio.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-0, PRE-F, PRE-A, PRE-B, PRE-C, PRE-D, PRE-E) tienen escenario Given/When/Then en §6 (6.14→PRE-F, 6.5→PRE-A/I-H7, 6.6/6.7→PRE-B, 6.8→PRE-C, 6.9/6.10/6.11→PRE-D/I-H4, 6.12/6.13→PRE-E/I-H5).
- [x] Todas las invariantes tocadas (I-H4, I-H5, I-H7, I-H8) tienen escenario de cobertura (6.9/6.10/6.11→I-H4, 6.12/6.13→I-H5, 6.5→I-H7, 6.15→I-H8).
- [x] Happy paths presentes: 6.1 (upgrade a RequiereIntervencion), 6.2 (downgrade a RequiereSeguimiento), 6.3 (recaptura GPS sin cambiar AccionRequerida), 6.4 (solo texto, mantiene RequiereIntervencion).
- [x] Escenario rebuild desde stream presente (6.16) — verifica que `Apply(HallazgoActualizado_v1)` es puro y que los campos inmutables (I-H8) permanecen inalterados tras reproyección.
- [x] §7 Idempotencia decidida: envelope dedup Wolverine por `X-Client-Command-Id` (ADR-008); followup #15 sigue pendiente — documentado.
- [x] §10 SignalR marcado explícitamente "no aplica" con justificación.
- [x] §11 Adapters Sinco marcado explícitamente "no aplica".
- [x] §12 Preguntas abiertas: todas respondidas.
- [ ] **Firma del usuario pendiente** — al firmar, el slice pasa a `red`.
