# Slice 1o — ActualizarRepuesto

**Autor:** domain-modeler
**Fecha:** 2026-05-11
**Estado:** draft
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.4` — catálogo canónico de 24 eventos; evento #11 `RepuestoActualizado_v1`. Nota: §12.10.14 usa el nombre histórico `RepuestoEstimadoActualizado_v1`; §15.4 es la fuente de verdad — nombre canónico es `RepuestoActualizado_v1`.
- `01-modelo-dominio.md §12.10.14` — caso operativo y campos patcheables: `Cantidad` y `Justificacion`. `SkuId`, `Unidad` y `HallazgoId` son inmutables (cambo de SKU = remover + asignar). Campo `UbicacionGps` presente en el modelo histórico — ver §12 P-1.
- `01-modelo-dominio.md §15.3` — `I-H7` (editable solo en `EnEjecucion`); `I-H9` (bloqueante si tiene hijos activos — aplicable a `RemoverRepuesto`, no este slice).
- `01-modelo-dominio.md §15.7` — estados del aggregate y invariantes de lifecycle.
- `slices/1f-asignar-repuesto/spec.md` — introduce el record `Repuesto` (VO de estado), colección `_repuestos`, `RepuestoEstimado_v1`; define PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-G; invariante I-H12; `SkuId: int`, `RepuestoId: Guid`.
- `slices/1d-actualizar-hallazgo/spec.md` — patrón PATCH semántico con PUT (body completo de campos editables); `200 OK` en happy path; idempotencia ADR-008; Apply puro; invariantes I-H7, I-H8.
- `slices/1n-descartar-novedad-preop/spec.md` — patrón reciente de spec; `ClaimsTecnico`, ADR-008.
- ADR-002 (tentativo) — identidad 100% del host PWA; `TecnicoId` opaco del JWT.
- ADR-008 (`00-investigacion-mercado.md §9.16`) — `X-Client-Command-Id` para idempotencia end-to-end.

---

## 1. Intención

El técnico necesita corregir la cantidad estimada o la justificación de un repuesto que ya asignó a un hallazgo durante la inspección en curso. El caso típico es "estimé 1 filtro, pero después de revisar veo que necesito 2". En lugar de remover y volver a asignar (5+ taps), puede editar directamente. Solo los campos `Cantidad` y `Justificacion` son patcheables (el SKU, la unidad y el hallazgo destino son inmutables por diseño). El comando tiene efecto únicamente mientras la inspección está `EnEjecucion`.

---

## 2. Comando

```csharp
public sealed record ActualizarRepuesto(
    Guid     InspeccionId,
    Guid     HallazgoId,        // hallazgo al que pertenece el repuesto — inmutable, va en el path
    Guid     RepuestoId,        // ID interno del repuesto a actualizar
    decimal? CantidadNueva,     // null = no cambiar; si viene, debe ser > 0
    string?  ObservacionNueva,  // null = no cambiar; string vacío = limpiar el valor (ver P-2)
    string   ActualizadoPor     // TecnicoId opaco del JWT, extraído por la capa API
) : ICommand;
```

**Semántica PATCH (campos opcionales):**

El comando sigue el patrón PATCH semántico del proyecto: cada campo patcheable viene como nullable. `null` significa "no tocar". Al menos uno de los dos campos patcheables (`CantidadNueva`, `ObservacionNueva`) debe ser no-`null` (PRE-8 — rechazar comando vacío).

> **`HallazgoId` en el comando:** viaja en el path HTTP y en el record de comando como referencia para localizar el repuesto en el aggregate state. El aggregate usa `_repuestos.Find(r => r.RepuestoId == cmd.RepuestoId)` — que ya contiene el `HallazgoId` — por lo que PRE-5 verifica la pertenencia hallazgo-repuesto al mismo tiempo. Ver §4.

> **`RepuestoId`:** `Guid` v7 — ID interno del módulo. No es el `SkuId` (PK del ERP).

> **`SkuId`, `Unidad`, `HallazgoId`:** inmutables — no viajan en el comando. Para cambiar SKU: `RemoverRepuesto` (slice 1p) + `AsignarRepuesto` (slice 1f).

> **Nota sobre `string? ObservacionNueva` vs `string? Justificacion`:** el VO `Repuesto` de slice 1f usa el campo `Justificacion`. Este slice usa `ObservacionNueva` como nombre del parámetro del comando para indicar el valor nuevo; el evento lo persiste como `Justificacion`. Ver §3 y §12 P-2.

**Claims del técnico** (parámetros adicionales del handler, no del command record):

```csharp
// Misma forma que ClaimsTecnico de slices 1g, 1m, 1n.
public sealed record ClaimsTecnico(
    string    TecnicoId,
    ISet<int> ProyectosAsignados,
    bool      TieneCapabilityEjecutarInspeccion);
```

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: PATCH /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos/{repuestoId}
public sealed record ActualizarRepuestoRequest(
    decimal? CantidadNueva,
    string?  ObservacionNueva);

public sealed record ActualizarRepuestoResult(
    Guid           InspeccionId,
    Guid           HallazgoId,
    Guid           RepuestoId,
    decimal        Cantidad,      // valor vigente post-update
    string?        Justificacion, // valor vigente post-update
    DateTimeOffset ActualizadoEn);
```

---

## 3. Evento(s) emitido(s)

Este slice emite **exactamente un evento** en todos los casos de éxito, en un único `SaveChangesAsync`.

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `RepuestoActualizado_v1` | Ver campos a continuación | Cuando el comando supera todas las precondiciones e invariantes. Un único evento por invocación. |

```csharp
public sealed record RepuestoActualizado_v1(
    Guid           InspeccionId,
    Guid           HallazgoId,
    Guid           RepuestoId,
    decimal?       Cantidad,       // null = no cambió en esta actualización
    string?        Justificacion,  // null = no cambió en esta actualización; ver P-2 sobre limpiar
    string         ActualizadoPor,
    DateTimeOffset ActualizadoEn   // TimeProvider.GetUtcNow() — prohibido DateTime.UtcNow
) : IEvent;
```

**Semántica de los campos nulos en el evento:**

`Cantidad` y `Justificacion` en el evento tienen semántica de delta: `null` significa "este campo no se tocó en esta actualización". La proyección y el `Apply` leen el valor previo del VO `Repuesto` y aplican solo los campos no-nulos.

> **Decisión D-1 — Evento con delta vs. evento con estado completo:** el evento porta solo el delta (campos que cambiaron), no el estado completo del repuesto post-update. Alternativa "estado completo" es más simple para projections pero infla el stream con datos redundantes cuando solo se cambia `Cantidad`. El delta es coherente con el patrón de `HallazgoActualizado_v1` (§15.4 / slice 1d), donde los campos inmutables no viajan en el evento. Ver §12 P-3 para discusión.

**Estado interno modificado por `Apply(RepuestoActualizado_v1)`:**

`Apply` actualiza el record `Repuesto` en `_repuestos` usando `with`:

```csharp
// Pseudocódigo del Apply — puro, sin validaciones.
// La implementación real la produce el agente green.
var idx = _repuestos.FindIndex(r => r.RepuestoId == e.RepuestoId);
if (idx < 0) return;   // evento en stream de un repuesto removido en un slice posterior; ignorar
var prev = _repuestos[idx];
_repuestos[idx] = prev with
{
    Cantidad     = e.Cantidad     ?? prev.Cantidad,
    Justificacion = e.Justificacion ?? prev.Justificacion
};
_contribuyentes.Add(e.ActualizadoPor);
```

El `Apply` es puro — sin validaciones, sin lanzar excepciones. Si el `RepuestoId` no existe en `_repuestos` (puede ocurrir si se aplica sobre un stream que ya contenía `RepuestoRemovido_v1` del slice 1p), el `Apply` hace return silencioso (no lanza).

---

## 4. Precondiciones

Las precondiciones viven en el **método de decisión del aggregate**. PRE-1 vive en el handler (acceso a Marten). PRE-0 en la capa HTTP. Los `Apply` son puros y nunca re-validan.

- **PRE-0 (capa HTTP):** capability `ejecutar-inspeccion` requerida. Si el claim está ausente → `403 Forbidden`. Mismo mecanismo que slices 1b..1n.
- **PRE-1 (handler):** `InspeccionId` debe existir como stream en Marten. Si `AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)` devuelve `null` → `InspeccionNoEncontradaException` (`404 Not Found`).
- **PRE-2 (aggregate — método de decisión):** `Estado == EnEjecucion` (I-H7). Si la inspección está en cualquier otro estado → `InspeccionNoEnEjecucionException` con el estado actual (`422 Unprocessable Entity`).
- **PRE-3 (aggregate — método de decisión):** `HallazgoId` debe existir en `_hallazgos`. Si no existe → `HallazgoNoEncontradoException` (`404 Not Found`).
- **PRE-4 (aggregate — método de decisión):** el hallazgo no debe estar marcado como eliminado (`Eliminado == false`). Si está eliminado → `HallazgoEliminadoException` (`422 Unprocessable Entity`). Helper `ObtenerHallazgoActivo` de slices 1d/1e.
- **PRE-5 (aggregate — método de decisión):** el repuesto identificado por `RepuestoId` debe existir en `_repuestos` y pertenecer al `HallazgoId` indicado (i.e., `_repuestos.Any(r => r.RepuestoId == cmd.RepuestoId && r.HallazgoId == cmd.HallazgoId)`). Si el `RepuestoId` no existe en absoluto → `RepuestoNoEncontradoException` (`404 Not Found`). Si existe pero pertenece a un hallazgo distinto → también `RepuestoNoEncontradoException` (el cliente no puede actualizar repuestos fuera del hallazgo declarado en el path — evita operaciones cross-hallazgo). Ver Decisión D-2.
- **PRE-6 (aggregate — método de decisión, reservado para slice 1p):** si `RemoverRepuesto` (slice 1p) introduce soft-delete de repuestos, este slice necesitará verificar que el repuesto no esté marcado como removido. Por ahora no existe el mecanismo de soft-delete de repuestos en el modelo (§12.10.13 especifica hard delete para repuestos, a diferencia del soft delete de hallazgos). Ver §12 P-4.
- **PRE-7 (aggregate — método de decisión):** `CantidadNueva > 0` si el campo viene presente (no-null). Si `CantidadNueva <= 0` → `CantidadInvalidaException` (`422 Unprocessable Entity`).
- **PRE-8 (aggregate — método de decisión):** al menos uno de los campos patcheables (`CantidadNueva`, `ObservacionNueva`) debe ser no-`null`. Si ambos son `null` → `ComandoSinCambiosException` (`400 Bad Request`). No se emite evento vacío. Ver Decisión D-3.

> **Capa donde viven:** PRE-0 en capa HTTP; PRE-1 en el handler (acceso a Marten); PRE-2..PRE-8 en el método de decisión del aggregate. Los `Apply(RepuestoActualizado_v1)` son puros — no re-validan ninguna de estas condiciones.

---

## 5. Invariantes tocadas

- **I-H7** (`§15.3`): editable (y con capacidad de actualizar repuestos) solo si la inspección está `EnEjecucion`. Cubierta por PRE-2.
- **I-H12** (`§15.3` — propuesta en slice 1f): solo hallazgos con `AccionRequerida = RequiereIntervencion` pueden tener repuestos. Este slice no re-valida I-H12 directamente (el repuesto ya pasó por esa validación al ser asignado con `AsignarRepuesto`). Si un hallazgo tenía `AccionRequerida = RequiereIntervencion` cuando se asignó el repuesto, su `AccionRequerida` pudo haber cambiado con `ActualizarHallazgo` (slice 1d). Sin embargo, ese cambio no elimina los repuestos existentes (eso sería responsabilidad de una saga o de una regla nueva). Este slice no introduce nueva invariante al respecto — ver §12 P-5.
- **INV-RA1 (nuevo — propuesto para agregar a §15.3 en este PR):** los campos `SkuId`, `Unidad` y `HallazgoId` de un repuesto son inmutables desde `RepuestoEstimado_v1`. `ActualizarRepuesto` no puede cambiarlos. Cubierta por la ausencia de esos campos en el comando y el evento.

  > **Propuesta para §15.3:**
  > ```
  > INV-RA1  SkuId, Unidad y HallazgoId de un Repuesto son inmutables
  >          desde RepuestoEstimado_v1. Para cambiar SKU: RemoverRepuesto
  >          + AsignarRepuesto (dos comandos). Para cambiar HallazgoId:
  >          no permitido (los repuestos son parte estructural del hallazgo).
  > ```

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — actualizar solo la cantidad

**Given**
- Stream `inspeccion-{X}` con:
  1. `InspeccionIniciada_v1(InspeccionId=X, Estado→EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=G1, AccionRequerida=RequiereIntervencion, ParteEquipoId=77)`
  3. `RepuestoEstimado_v1(HallazgoId=G1, RepuestoId=R1, SkuId=501, SkuCodigo="INS-501", Cantidad=1, Justificacion="Cambio rutinario", Unidad="unidad", AsignadoPor="rmartinez")`
- `Estado=EnEjecucion`, `_hallazgos[G1].Eliminado=false`.
- `_repuestos` contiene `Repuesto(RepuestoId=R1, HallazgoId=G1, SkuId=501, Cantidad=1, Justificacion="Cambio rutinario", Unidad="unidad")`.

**When**
- Comando `ActualizarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, CantidadNueva=2, ObservacionNueva=null, ActualizadoPor="rmartinez")`.

**Then**
- Se emite exactamente un `RepuestoActualizado_v1` con:
  - `InspeccionId=X`, `HallazgoId=G1`, `RepuestoId=R1`
  - `Cantidad=2`, `Justificacion=null` (no cambió)
  - `ActualizadoPor="rmartinez"`, `ActualizadoEn=DateTimeOffset.UtcNow(TimeProvider)`
- `_repuestos[R1].Cantidad=2`.
- `_repuestos[R1].Justificacion="Cambio rutinario"` (sin cambio — valor anterior preservado).
- `_repuestos[R1].SkuId=501`, `_repuestos[R1].HallazgoId=G1` (inmutables — sin cambio).
- `_contribuyentes` incluye `"rmartinez"`.

---

### 6.2 Happy path — actualizar solo la observación/justificación

**Given**
- Stream con los mismos eventos del §6.1. Estado `EnEjecucion`.

**When**
- Comando `ActualizarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, CantidadNueva=null, ObservacionNueva="Filtro doble en este modelo de motor", ActualizadoPor="rmartinez")`.

**Then**
- Se emite `RepuestoActualizado_v1` con `Cantidad=null`, `Justificacion="Filtro doble en este modelo de motor"`.
- `_repuestos[R1].Cantidad=1` (sin cambio — valor anterior preservado).
- `_repuestos[R1].Justificacion="Filtro doble en este modelo de motor"`.

---

### 6.3 Happy path — actualizar ambos campos en una sola operación

**Given**
- Mismo stream del §6.1. Estado `EnEjecucion`.

**When**
- Comando `ActualizarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, CantidadNueva=3, ObservacionNueva="Revisión extendida, se necesitan 3", ActualizadoPor="jperez")`.

**Then**
- Se emite `RepuestoActualizado_v1` con `Cantidad=3`, `Justificacion="Revisión extendida, se necesitan 3"`, `ActualizadoPor="jperez"`.
- `_repuestos[R1].Cantidad=3`, `_repuestos[R1].Justificacion="Revisión extendida, se necesitan 3"`.
- `_contribuyentes` incluye `"jperez"` (puede ser un técnico diferente al que asignó el repuesto).

---

### 6.4 Happy path — segunda actualización sobre el mismo repuesto (trazabilidad)

**Given**
- Stream con los eventos del §6.1 más `RepuestoActualizado_v1(R1, Cantidad=2, Justificacion=null, ActualizadoPor="rmartinez", T1)`.
- `_repuestos[R1].Cantidad=2`, `_repuestos[R1].Justificacion="Cambio rutinario"`.

**When**
- Comando `ActualizarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, CantidadNueva=null, ObservacionNueva="Filtro doble en este modelo", ActualizadoPor="rmartinez")`.

**Then**
- Se emite segundo `RepuestoActualizado_v1` con `Cantidad=null`, `Justificacion="Filtro doble en este modelo"`.
- `_repuestos[R1].Cantidad=2` (preservado de actualización anterior), `_repuestos[R1].Justificacion="Filtro doble en este modelo"`.
- El stream conserva ambos eventos — historial completo de cambios (trazabilidad §12.10.14).

---

### 6.5 Violación PRE-2 (I-H7) — inspección no está en EnEjecucion

**Given**
- Aggregate con `Estado=Firmada`.

**When**
- Comando `ActualizarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, CantidadNueva=2, ObservacionNueva=null, ActualizadoPor="rmartinez")`.

**Then**
- Lanza `InspeccionNoEnEjecucionException` con mensaje que incluye el estado actual (`Firmada`).
- No se emite ningún evento.
- HTTP `422 Unprocessable Entity`, `codigoError="I-H7"`.

---

### 6.6 Violación PRE-3 — HallazgoId no existe en el aggregate

**Given**
- Aggregate `EnEjecucion` sin hallazgos (solo `InspeccionIniciada_v1`).

**When**
- Comando con `HallazgoId=G_INEXISTENTE`.

**Then**
- Lanza `HallazgoNoEncontradoException` con mensaje "El hallazgo {G_INEXISTENTE} no existe en la inspección {X}."
- No se emite evento.
- HTTP `404 Not Found`, `codigoError="PRE-3"`.

---

### 6.7 Violación PRE-4 — hallazgo existe pero está eliminado

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G2, AccionRequerida=RequiereIntervencion), HallazgoEliminado_v1(G2), RepuestoEstimado_v1(HallazgoId=G2, RepuestoId=R2)]`.
- `_hallazgos[G2].Eliminado=true`.

**When**
- Comando con `HallazgoId=G2`, `RepuestoId=R2`, `CantidadNueva=2`.

**Then**
- Lanza `HallazgoEliminadoException` con mensaje "El hallazgo {G2} está eliminado."
- No se emite evento.
- HTTP `422 Unprocessable Entity`, `codigoError="PRE-4-ELIMINADO"`.

---

### 6.8 Violación PRE-5 — RepuestoId no existe en el aggregate

**Given**
- Aggregate `EnEjecucion` con `_hallazgos[G1]` activo pero `_repuestos` vacío.

**When**
- Comando con `HallazgoId=G1`, `RepuestoId=R_INEXISTENTE`, `CantidadNueva=2`.

**Then**
- Lanza `RepuestoNoEncontradoException` con mensaje "El repuesto {R_INEXISTENTE} no existe en el hallazgo {G1} de la inspección {X}."
- No se emite evento.
- HTTP `404 Not Found`, `codigoError="PRE-5"`.

---

### 6.9 Violación PRE-5 — RepuestoId existe pero pertenece a hallazgo distinto

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G1, AccionRequerida=RequiereIntervencion), HallazgoRegistrado_v1(G2, AccionRequerida=RequiereIntervencion), RepuestoEstimado_v1(HallazgoId=G1, RepuestoId=R1)]`.
- `_repuestos` contiene `Repuesto(RepuestoId=R1, HallazgoId=G1)`.

**When**
- Comando con `HallazgoId=G2`, `RepuestoId=R1`, `CantidadNueva=2`.
  _(El cliente intenta actualizar R1 usando el path del hallazgo G2, pero R1 pertenece a G1.)_

**Then**
- Lanza `RepuestoNoEncontradoException` (el repuesto no pertenece al hallazgo indicado).
- No se emite evento.
- HTTP `404 Not Found`, `codigoError="PRE-5"`.

---

### 6.10 Violación PRE-7 — CantidadNueva igual o menor a cero

**Given**
- Stream del §6.1. Estado `EnEjecucion`, `_repuestos[R1]` activo.

**When**
- Comando con `CantidadNueva=0, ObservacionNueva=null`.

**Then**
- Lanza `CantidadInvalidaException` con mensaje "Cantidad debe ser mayor que cero."
- No se emite evento.
- HTTP `422 Unprocessable Entity`, `codigoError="PRE-7"`.

---

### 6.11 Violación PRE-8 — comando sin campos patcheables (ambos null)

**Given**
- Stream del §6.1. Estado `EnEjecucion`, `_repuestos[R1]` activo.

**When**
- Comando con `CantidadNueva=null, ObservacionNueva=null`.

**Then**
- Lanza `ComandoSinCambiosException` con mensaje "Se requiere al menos un campo para actualizar (CantidadNueva o ObservacionNueva)."
- No se emite evento.
- HTTP `400 Bad Request`, `codigoError="PRE-8"`.

---

### 6.12 Violación PRE-1 — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando con `InspeccionId=Z`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- No se emite evento.
- HTTP `404 Not Found`, `codigoError="PRE-1"`.

---

### 6.13 Inmutabilidad INV-RA1 — los campos inmutables no cambian tras actualización

**Given**
- Stream del §6.1 con `RepuestoEstimado_v1(R1, SkuId=501, SkuCodigo="INS-501", Unidad="unidad", HallazgoId=G1)`.

**When**
- Comando `ActualizarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, CantidadNueva=3, ObservacionNueva=null, ActualizadoPor="rmartinez")`.

**Then**
- Se emite `RepuestoActualizado_v1` sin campos `SkuId`, `SkuCodigo`, `Unidad`, `HallazgoId`.
- `_repuestos[R1].SkuId=501` (inmutable — sin cambio).
- `_repuestos[R1].Unidad="unidad"` (inmutable — sin cambio).
- `_repuestos[R1].HallazgoId=G1` (inmutable — sin cambio).

---

### 6.14 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos previos).
- Lista de eventos en orden causal:
  1. `InspeccionIniciada_v1(InspeccionId=X, Estado→EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=G1, AccionRequerida=RequiereIntervencion, ParteEquipoId=77)`
  3. `RepuestoEstimado_v1(HallazgoId=G1, RepuestoId=R1, SkuId=501, SkuCodigo="INS-501", Cantidad=1, Justificacion="Cambio rutinario", Unidad="unidad", AsignadoPor="rmartinez", AsignadoEn=T0)`
  4. `RepuestoActualizado_v1(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, Cantidad=2, Justificacion=null, ActualizadoPor="rmartinez", ActualizadoEn=T1)`

**When**
- Se reproyectan los cuatro eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- `Estado=EnEjecucion`.
- `_hallazgos.Count=1`, `_hallazgos[G1].Eliminado=false`.
- `_repuestos.Count=1`.
- `_repuestos[R1].Cantidad=2` (actualizado por evento #4).
- `_repuestos[R1].Justificacion="Cambio rutinario"` (preservado — evento #4 tenía `Justificacion=null`).
- `_repuestos[R1].SkuId=501`, `_repuestos[R1].HallazgoId=G1` (inmutables — sin cambio).
- `_contribuyentes` incluye `"rmartinez"`.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al que produce el método de decisión seguido de `Apply` in-process.

> **Justificación:** garantiza que `Apply(RepuestoActualizado_v1)` es puro (solo aplica el delta sobre el VO `Repuesto`) y que el orden de los eventos respeta la causalidad. Si un `Apply` tuviera validación intrusa (e.g., verificar que el repuesto existe antes de aplicar), el test de rebuild lo detectaría en el caso de streams con `RepuestoRemovido_v1` intercalados en versiones futuras.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente envía `X-Client-Command-Id: <UUIDv7>` como header HTTP por cada tap "Guardar cambios" en el repuesto. El header se mapea a `MessageId` Wolverine. Si Wolverine detecta el `MessageId` ya procesado (envelope dedup), devuelve la respuesta original sin re-ejecutar el handler. No se emite un segundo `RepuestoActualizado_v1`.

**Semántica de "última escritura gana":**

Si el técnico hace dos actualizaciones distintas sobre el mismo `RepuestoId` con dos `X-Client-Command-Id` diferentes (flujo normal), ambas se procesan. El stream recibe dos `RepuestoActualizado_v1` — el segundo sobrescribe al primero en el state del aggregate. El historial de cambios queda íntegro en el stream. Mismo comportamiento que `HallazgoActualizado_v1` en slice 1d.

**Sin idempotencia a nivel aggregate (diferencia con AsignarRepuesto):**

`ActualizarRepuesto` no implementa dedup por `RepuestoId` en el aggregate (a diferencia de `AsignarRepuesto` que tenía PRE-D). Dos actualizaciones con distinto `X-Client-Command-Id` pero mismo payload producen dos eventos idénticos en el stream — es correcto desde el dominio (el técnico confirma dos veces el mismo valor). El estado final del aggregate es el mismo. Esta es la semántica "última escritura gana" aceptada para operaciones de edición.

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica en este slice.

**Atomicidad:** un único `IDocumentSession.SaveChangesAsync()` persiste `RepuestoActualizado_v1` y actualiza las proyecciones afectadas.

> **Nota sobre dedup real de Wolverine:** el mecanismo concreto de dedup (ADR-008) sigue siendo followup #15. Este slice sigue el mismo patrón sin avanzar ese followup.

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` (§15.12.1) — actualización del repuesto en el hallazgo

`DetalleInspeccionView` consume `RepuestoActualizado_v1`. Al recibir el evento, la proyección aplica el delta sobre la entrada del repuesto dentro del hallazgo correspondiente:
- Si `Cantidad` no es null → actualiza `Repuesto.Cantidad`.
- Si `Justificacion` no es null → actualiza `Repuesto.Justificacion`.
- Actualiza `ActualizadoEn` y `ActualizadoPor` del repuesto.
- Los campos inmutables (`SkuId`, `SkuCodigo`, `Unidad`, `HallazgoId`) no se tocan en el handler de proyección.

La proyección `DetalleInspeccionView` no está implementada aún — el slice documenta qué campos actualiza cuando se implemente.

### 8.2 `AuditoriaInspeccionesView` (§15.12.2) — consume RepuestoActualizado_v1

Según §15.12.2 del modelo (confirmado en slice 1f §8.2), esta proyección consume `RepuestoEstimado_v1`, `RepuestoActualizado_v1` y `RepuestoRemovido_v1` para construir el historial de repuestos. Este slice materializa la cobertura de `RepuestoActualizado_v1` en esa proyección.

### 8.3 `BandejaInspeccionesPendientesOTView`, `BandejaTecnicoView`, `InspeccionAbiertaPorEquipoView` — no impactadas

La actualización de un repuesto no cambia el estado de la inspección, el conteo de hallazgos con `RequiereIntervencion` ni el estado de ciclo de vida. Estas vistas no cambian con `RepuestoActualizado_v1`.

---

## 9. Impacto en endpoints HTTP

**Endpoint nuevo:** `PATCH /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos/{repuestoId}`

El verbo `PATCH` es coherente con la semántica de actualización parcial (delta): solo viajan los campos que cambian. Contrasta con `PUT` de `ActualizarHallazgo` (slice 1d) que requería el body completo porque todos los campos son obligatorios. Aquí, los campos patcheables son opcionales independientemente (ver §2).

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO (`ActualizarRepuestoRequest`):**

```json
{
  "cantidadNueva": 2.0,
  "observacionNueva": null
}
```

> Ambos campos son opcionales. Al menos uno debe ser no-null (PRE-8 — validado en el aggregate). `InspeccionId`, `HallazgoId` y `RepuestoId` viajan en el path. `ActualizadoPor` se extrae del JWT en la capa API (ADR-002 tentativo — mock `const string tecnicoId = "rmartinez"` hasta que ADR-002 esté resuelto — followup #14).

**Response `200 OK` (happy path):**

```json
{
  "inspeccionId": "...",
  "hallazgoId": "...",
  "repuestoId": "01950000-0000-7000-0000-000000000001",
  "cantidad": 2.0,
  "justificacion": "Cambio rutinario",
  "actualizadoEn": "2026-05-11T14:32:00+00:00"
}
```

> Se usa `200 OK` porque el recurso ya existe — este endpoint actualiza, no crea. La respuesta lleva el estado vigente completo del repuesto post-update (incluyendo los campos que no cambiaron, para que el cliente no necesite un GET subsiguiente).

**Códigos de error:**

| Escenario | Código HTTP | `codigoError` |
|---|---|---|
| Capability ausente (PRE-0) | `403 Forbidden` | `"PRE-0"` |
| InspeccionId no existe (PRE-1) | `404 Not Found` | `"PRE-1"` |
| HallazgoId no existe (PRE-3) | `404 Not Found` | `"PRE-3"` |
| HallazgoId eliminado (PRE-4) | `422 Unprocessable Entity` | `"PRE-4-ELIMINADO"` |
| RepuestoId no encontrado o no pertenece al hallazgo (PRE-5) | `404 Not Found` | `"PRE-5"` |
| Inspección no en EnEjecucion (PRE-2 / I-H7) | `422 Unprocessable Entity` | `"I-H7"` |
| CantidadNueva ≤ 0 (PRE-7) | `422 Unprocessable Entity` | `"PRE-7"` |
| Ambos campos null — comando vacío (PRE-8) | `400 Bad Request` | `"PRE-8"` |

**Rol/permiso requerido:** capability `ejecutar-inspeccion` con el proyecto de la inspección asignado (heredado del host PWA — ADR-002 tentativo).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `RepuestoActualizado_v1` no está en el catálogo de eventos SignalR (ADR-005, §14 del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección. La actualización de un repuesto es una operación local del técnico — el resultado es visible inmediatamente en su pantalla sin notificación en tiempo real a otras partes en MVP.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `ActualizarRepuesto` es puramente local al módulo. No hay llamadas salientes al ERP on-prem. Los repuestos se persisten localmente y son consumidos por `CerrarInspeccionSaga` al momento del cierre, que consolida `_repuestos` con los valores vigentes (post cualquier actualización). ADR-006 (outbox para integraciones ERP) no aplica. No se requiere WireMock.

---

## 12. Preguntas abiertas

- **P-1 (a confirmar en firma): ¿`UbicacionGps` es campo patcheable?** El modelo histórico §12.10.14 incluye `UbicacionGps? Ubicacion` en el comando `EditarRepuestoEstimado` y en el evento `RepuestoEstimadoActualizado_v1`. El VO `Repuesto` de slice 1f no tiene campo `UbicacionGps` (el slice 1f no lo introdujo). §15.4 no lo detalla en el payload del evento #11.
  - **(A — asunción default de este spec):** `UbicacionGps` **no** es campo patcheable en MVP. El caso operativo principal es corregir cantidad/justificación. El GPS asociado al repuesto no tiene uso claro en el BOM de la OT. Si en el futuro se necesita, se agrega como campo en `RepuestoActualizado_v1_v2` o se extiende el VO con un campo nuevo. Esta asunción mantiene el slice enfocado.
  - **(B):** incluir `UbicacionGps? UbicacionNueva` como tercer campo patcheable, alineado con §12.10.14.
  - **Recomendación:** confirmar con Jaime antes de firmar. Si se acepta opción B, agregar `UbicacionNueva: UbicacionGps?` al comando y al evento, y añadir escenarios happy-path adicionales en §6. El modelo del VO `Repuesto` también necesitaría `UbicacionGps?` como campo (cambio en slice 1f retroactivo vía PR de este slice).

- **P-2 (a confirmar en firma): Semántica de "limpiar `Justificacion`".** Si `ObservacionNueva=""` (string vacío) ¿se trata como "limpiar el campo a null" o como "setear el valor a cadena vacía"? Opciones:
  - **(A — asunción default):** string vacío se normaliza a `null` en el handler antes de invocar el aggregate. El dominio no guarda strings vacíos en `Justificacion`. Consistente con el patrón de `ActualizarHallazgo` donde los campos opcionales son `null` o tienen contenido.
  - **(B):** string vacío y null son distintos; el dominio guarda vacío como vacío. Requiere PRE adicional `ObservacionNueva != ""` si se quiere rechazar vacíos explícitos.
  - **Recomendación:** opción A. El handler normaliza `ObservacionNueva?.Trim() == "" → null`.

- **P-3 (no bloqueante — decisión D-1): Evento con delta vs. estado completo.** Ver §3 Decisión D-1. Si la proyección `DetalleInspeccionView` prefiere recibir el estado completo del repuesto en el evento (sin tener que leer estado previo), el evento puede llevar `Cantidad: decimal` y `Justificacion: string?` con el estado post-update en lugar del delta. Implicación: el aggregate necesita resolver el estado final antes de emitir el evento.
  - **(A — asunción default):** delta en el evento (campos nulos = no cambiaron). La projección aplica el delta sobre su estado local. Más ligero en el stream; requiere estado previo para interpretar el evento aislado.
  - **(B):** estado completo en el evento. La projection no necesita leer estado previo. Más verbose pero más legible para projections stateless.
  - **Recomendación:** si se prefiere opción B, cambiar la firma del evento a `Cantidad: decimal` (obligatorio — siempre lleva el valor vigente) y `Justificacion: string?` (puede ser null si el repuesto no tiene justificación). Requiere que el método de decisión calcule el estado final antes de emitir. No bloquea la firma; es una decisión de ergonomía de projection.

- **P-4 (no bloqueante — reservado): Soft delete de repuestos.** §12.10.13 especifica hard delete para repuestos (a diferencia del soft delete de hallazgos). Si `RemoverRepuesto` (slice 1p) implementa hard delete, PRE-6 de este slice no es necesaria — un repuesto removido simplemente no estará en `_repuestos`. Si cambia a soft delete, este slice deberá agregar una precondición análoga a PRE-6 de `AsignarRepuesto`. Esto se definirá en el spec del slice 1p.

- **P-5 (no bloqueante — asunción): Coherencia `AccionRequerida` tras downgrade.** Un hallazgo pudo ser downgradeado de `RequiereIntervencion` a `RequiereSeguimiento` con `ActualizarHallazgo` (slice 1d). Los repuestos asignados antes del downgrade siguen en `_repuestos`. `ActualizarRepuesto` no re-valida I-H12. Asunción: la coherencia se resuelve en el BOM de cierre (la saga ignora repuestos de hallazgos que ya no son `RequiereIntervencion`) o mediante una regla de negocio en `ActualizarHallazgo` (fuera del scope de este slice). Si se decide que el downgrade debe limpiar repuestos, ese cambio va en slice 1d (followup).

---

## 13. Decisiones documentadas

| # | Decisión | Valor elegido | Justificación |
|---|---|---|---|
| D-1 | Evento con delta vs. estado completo | Delta (`Cantidad?`, `Justificacion?`) | Coherente con `HallazgoActualizado_v1` (slice 1d). Más liviano en stream. Ver §12 P-3 si se prefiere estado completo. |
| D-2 | PRE-5: RepuestoId con HallazgoId erróneo | `RepuestoNoEncontradoException` (404) en lugar de error específico "repuesto en hallazgo incorrecto" | El cliente no debería construir requests cross-hallazgo; el path HTTP provee el `HallazgoId`. Lanzar 404 es más seguro (no revela si el repuesto existe en otro hallazgo). |
| D-3 | Comando vacío (ambos campos null) | `ComandoSinCambiosException` (400) | No tiene sentido persistir un evento de cambio sin datos. La UI debería prevenir esto, pero el aggregate es defensa en profundidad. `400 Bad Request` porque es un error del cliente, no del estado del aggregate. |
| D-4 | UbicacionGps en MVP | Excluido (asunción P-1 opción A) | Sin caso de uso claro en BOM. Si emerge, es cambio aditivo. |
| D-5 | Semántica string vacío en ObservacionNueva | Normalizar a null en handler (asunción P-2 opción A) | El dominio no guarda strings vacíos. Consistente con patrón existente. |
| D-6 | Verbo HTTP | `PATCH` | Semántica de actualización parcial: los campos patcheables son opcionales independientemente. Contrasta con `PUT` de `ActualizarHallazgo` donde todos los campos son obligatorios. |

---

## 14. Checklist pre-firma

- [x] Todas las precondiciones (PRE-0..PRE-8) mapean a al menos un escenario Then en §6 (6.12→PRE-1, 6.5→PRE-2/I-H7, 6.6→PRE-3, 6.7→PRE-4, 6.8/6.9→PRE-5, 6.10→PRE-7, 6.11→PRE-8).
- [x] Todas las invariantes tocadas (I-H7, INV-RA1) tienen escenario de cobertura (6.5→I-H7, 6.13→INV-RA1).
- [x] Happy paths presentes: 6.1 (solo cantidad), 6.2 (solo observación), 6.3 (ambos campos), 6.4 (segunda actualización — trazabilidad).
- [x] Escenario de rebuild desde stream presente (6.14) — verifica que `Apply(RepuestoActualizado_v1)` aplica el delta de forma pura y que los campos inmutables permanecen inalterados.
- [x] §7 Idempotencia decidida: envelope dedup Wolverine por `X-Client-Command-Id` (ADR-008); "última escritura gana" para ediciones múltiples; followup #15 sigue pendiente.
- [x] §10 SignalR marcado explícitamente "no aplica" con justificación.
- [x] §11 Adapters Sinco marcado explícitamente "no aplica" con justificación.
- [x] §12 Preguntas abiertas: P-1 y P-2 requieren confirmación del usuario antes de firmar (marcadas "a confirmar en firma"). P-3, P-4, P-5 no bloqueantes con asunción documentada.
- [x] INV-RA1 (nuevo invariante) propuesto para agregar a §15.3 del modelo en el mismo PR.
- [x] Decisiones D-1..D-6 documentadas en §13.
- [ ] **P-1 (UbicacionGps) — confirmar con Jaime: ¿se incluye como campo patcheable o se excluye?**
- [ ] **P-2 (string vacío en ObservacionNueva) — confirmar: ¿normalizar a null o rechazar con 400?**
- [ ] **Firma del usuario pendiente** — al firmar (con P-1 y P-2 resueltas), el slice pasa a `red`.
