# Slice 1e — EliminarHallazgo

**Autor:** domain-modeler
**Fecha:** 2026-05-06
**Estado:** firmado
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `slices/1d-actualizar-hallazgo/spec.md` — patrón PRE-A / PRE-B1 / PRE-B2 (este slice es más simple: solo requiere el ID del hallazgo, sin campos editables)
- `slices/1c-registrar-hallazgo/spec.md` — `HallazgoRegistrado_v1`, invariantes I-H1..I-H6, PRE-* de registro
- `01-modelo-dominio.md §15.2` — estructura del value object `Hallazgo` (`Eliminado`, `MotivoEliminacion` — este slice añade `MotivoEliminacion` al record)
- `01-modelo-dominio.md §15.3` — invariantes `I-H7` (editable solo en EnEjecucion), `I-H9` (bloqueado si tiene hijos)
- `01-modelo-dominio.md §15.4` — evento #8 `HallazgoEliminado_v1` en catálogo MVP (catálogo final §15.4 renombrado a 24 eventos)
- `01-modelo-dominio.md §15.7` — invariante I-F1: eliminar hallazgos bloqueado post-firma
- `01-modelo-dominio.md §12.10.6` — definición original de `HallazgoEliminado_v1` con motivo
- ADR-002 (tentativo) — identidad 100 % del host PWA; `TecnicoId` opaco del JWT
- ADR-008 (`§9.16`) — idempotencia por `X-Client-Command-Id`
- FOLLOWUPS.md #21 — test §6.7 de `ActualizarHallazgoTests.cs` marcado `[Fact(Skip=...)]` pendiente de este slice

---

## 1. Intención

El técnico necesita eliminar un hallazgo que registró por error o que ya no es pertinente durante la inspección en curso. La eliminación es un **soft delete**: el evento `HallazgoEliminado_v1` se persiste en el stream (el histórico queda intacto para auditoría) y el agregado marca el hallazgo como `Eliminado=true`. El hallazgo no aparece en la pantalla activa ni viaja al PDF de cierre, pero sigue visible en la vista de auditoría (`DetalleInspeccionView` — §15.12.1). El comando solo puede ejecutarse mientras la inspección está `EnEjecucion` y el hallazgo no tiene hijos activos (repuestos o adjuntos no eliminados).

---

## 2. Comando

```csharp
public sealed record EliminarHallazgo(
    Guid   InspeccionId,
    Guid   HallazgoId,
    string Motivo,      // obligatorio — texto libre; razón del técnico para el audit
    string TecnicoId    // extraído del JWT por la capa API; el dominio lo recibe como parámetro
) : ICommand;
```

**Nota sobre `Motivo`:** el campo `Motivo` es obligatorio (non-null, non-empty). La eliminación silenciosa sin razón viola la trazabilidad de auditoría requerida por el brief del consultor mecánico. El modelo §15.2 incluye `MotivoEliminacion` en el value object `Hallazgo`. Este slice añade ese campo al record existente.

---

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `HallazgoEliminado_v1` | Ver campos a continuación | Siempre que el comando supere todas las precondiciones e invariantes. Un único evento por invocación. |

```csharp
public sealed record HallazgoEliminado_v1(
    Guid            InspeccionId,
    Guid            HallazgoId,
    string          Motivo,         // razón del técnico — persiste para auditoría
    string          EliminadoPor,   // TecnicoId del JWT — claim inyectado por el host PWA
    DateTimeOffset  EliminadoEn     // TimeProvider.GetUtcNow() en el handler — no editable por el cliente
);
```

**Nota sobre `DateTimeOffset` vs `DateTime`:** §12.10.6 del modelo histórico muestra `DateTime EliminadoEn`. La convención vigente del CLAUDE.md es `DateTimeOffset` para todos los timestamps. Este slice usa `DateTimeOffset` — conforme con `ActualizadoEn` de `HallazgoActualizado_v1` y `IniciadaEn` de `InspeccionIniciada_v1`.

**Nota sobre el Apply:** `Apply(HallazgoEliminado_v1)` pone `Eliminado=true` y persiste `MotivoEliminacion` en el value object `Hallazgo`. Para ello, el record `Hallazgo` debe extenderse con el campo `MotivoEliminacion: string?`. El agente `green` añade ese campo al record en este slice. El `Apply(HallazgoActualizado_v1)` existente no necesita cambiar — el `with { ... }` no toca `Eliminado` ni `MotivoEliminacion`.

---

## 4. Precondiciones

Las precondiciones PRE-A y PRE-B viven en el **método de decisión del aggregate**. PRE-0 vive en la capa HTTP. PRE-F vive en el handler (acceso a Marten para verificar existencia de stream). PRE-C vive también en el método de decisión del aggregate. Los `Apply` son puros y nunca re-validan.

- **PRE-0 (capa HTTP):** capability `ejecutar-inspeccion` requerida. Si el claim está ausente → `403 Forbidden`. Mismo mecanismo que slices 1b, 1c y 1d.
- **PRE-F (handler):** `InspeccionId` debe existir como stream en Marten. El handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).
- **PRE-A (aggregate — método de decisión):** `Estado == EnEjecucion` (I-H7, I-F1). Si la inspección está `Firmada`, `Cerrada`, `CerradaSinOT`, `CierrePendienteOT` o `Cancelada` → `InspeccionNoEnEjecucionException` (`422 Unprocessable Entity`). Cubre el bloqueo de I-F1 ("eliminar hallazgos" es uno de los comandos explícitamente bloqueados post-firma).
- **PRE-B (aggregate — método de decisión):** el `HallazgoId` debe existir en `_hallazgos` del aggregate. Si no existe → `HallazgoNoEncontradoException` (`404 Not Found`). Si existe pero ya está eliminado (`Eliminado == true`) → `HallazgoEliminadoException` (`422 Unprocessable Entity`). Mismo patrón PRE-B1/PRE-B2 del slice 1d.
- **PRE-C (aggregate — método de decisión):** `Motivo` no puede ser `null`, vacío ni solo whitespace. Excepción: `MotivoEliminacionVacioException` (`422 Unprocessable Entity`).
- **PRE-D (aggregate — método de decisión):** el hallazgo no puede tener hijos activos — repuestos no eliminados ni adjuntos no eliminados (I-H9). Si el hallazgo tiene `≥1 repuesto activo o ≥1 adjunto activo` → `HallazgoTieneHijosActivosException` (`422 Unprocessable Entity`).

> **Nota sobre PRE-D en MVP:** en este slice `RepuestoEstimado_v1` y `AdjuntoSubido_v1` aún no están implementados (los slices de repuestos y adjuntos son posteriores). La verificación de I-H9 en el aggregate depende de colecciones que en MVP inicial estarán vacías por construcción. El método de decisión DEBE verificar la condición usando las colecciones internas del aggregate (que estarán vacías hasta que esos slices existan), para que cuando los slices posteriores añadan datos a esas colecciones, la invariante ya esté activa sin modificar este código.
>
> **Capa donde viven:** PRE-0 en capa HTTP; PRE-F en el handler (acceso a Marten); PRE-A, PRE-B, PRE-C, PRE-D en el método de decisión del aggregate. Los `Apply(HallazgoEliminado_v1)` son puros — no re-validan ninguna de estas condiciones.

---

## 5. Invariantes tocadas

- **I-H7** (`§15.3`): editable (y eliminable) solo si la inspección está `EnEjecucion`. Cubierta por PRE-A.
- **I-H9** (`§15.3`): eliminar hallazgo bloqueado si tiene hijos (repuestos o adjuntos activos). Cubierta por PRE-D.
- **I-F1** (`§15.7`): post-firma, eliminar hallazgos está bloqueado. Cubierta por PRE-A (la condición `Estado == EnEjecucion` excluye todos los estados post-firma por construcción del state machine).

**Invariantes que no aplican en este slice:**
- I-H4, I-H5: no aplican — el comando no modifica `AccionRequerida` ni campos de intervención.
- I-H8: no aplica — el comando no modifica ningún campo inmutable del hallazgo.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — hallazgo sin hijos activos, inspección en ejecución

**Given**
- Stream `inspeccion-{X}` contiene `[InspeccionIniciada_v1, HallazgoRegistrado_v1(HallazgoId=G1, Origen=Manual, AccionRequerida=NoRequiereIntervencion)]`.
- `Estado=EnEjecucion`, `_hallazgos[G1].Eliminado=false`.
- `_hallazgos[G1]` no tiene repuestos ni adjuntos activos.

**When**
- Comando `EliminarHallazgo(InspeccionId=X, HallazgoId=G1, Motivo="Registrado por error — parte incorrecta", TecnicoId="rmartinez")`.

**Then**
- Se emite exactamente un `HallazgoEliminado_v1` con `HallazgoId=G1`, `Motivo="Registrado por error — parte incorrecta"`, `EliminadoPor="rmartinez"`, `EliminadoEn=DateTimeOffset.UtcNow(TimeProvider)`.
- `_hallazgos[G1].Eliminado=true` en el aggregate en memoria.
- `_hallazgos[G1].MotivoEliminacion="Registrado por error — parte incorrecta"` en el aggregate en memoria.
- El hallazgo sigue en `_hallazgos` (no se borra de la lista — soft delete).
- `_contribuyentes` incluye `"rmartinez"`.

### 6.2 Happy path — hallazgo con RequiereIntervencion (soft delete igual que cualquier otro)

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G2, AccionRequerida=RequiereIntervencion, TipoFallaId=3, CausaFallaId=12)]`.
- `Estado=EnEjecucion`, `_hallazgos[G2].Eliminado=false`.

**When**
- Comando `EliminarHallazgo(InspeccionId=X, HallazgoId=G2, Motivo="Hallazgo duplicado — el técnico lo registró dos veces", TecnicoId="rmartinez")`.

**Then**
- Se emite `HallazgoEliminado_v1` con `HallazgoId=G2` y `Motivo` correcto.
- `_hallazgos[G2].Eliminado=true`.
- `AccionRequerida`, `TipoFallaId`, `CausaFallaId` permanecen en el state del aggregate (read-only tras soft delete, pero inmutables desde `HallazgoEliminado_v1`).
- El hallazgo con `AccionRequerida=RequiereIntervencion` eliminado NO bloquea la firma: `FirmarInspeccion` evalúa solo hallazgos no eliminados para V-F3/V-F8 (responsabilidad de ese slice, no de este).

### 6.3 Violación PRE-A (I-H7 / I-F1) — inspección no está en EnEjecucion

**Given**
- Aggregate con `Estado=Firmada` (construido con `[InspeccionIniciada_v1, InspeccionFirmada_v1]`).
- `_hallazgos[G1].Eliminado=false`.

**When**
- Comando `EliminarHallazgo(InspeccionId=X, HallazgoId=G1, Motivo="Ya no aplica", TecnicoId="rmartinez")`.

**Then**
- Lanza `InspeccionNoEnEjecucionException` con mensaje que incluye el estado actual (`Firmada`).
- No se emite ningún evento.
- Código HTTP `422 Unprocessable Entity`.

### 6.4 Violación PRE-B1 — HallazgoId no existe en el aggregate

**Given**
- Aggregate `EnEjecucion` sin hallazgos (solo `InspeccionIniciada_v1`).

**When**
- Comando con `HallazgoId=G_INEXISTENTE`.

**Then**
- Lanza `HallazgoNoEncontradoException` con mensaje "El hallazgo {G_INEXISTENTE} no existe en la inspección {X}."
- No se emite evento.
- Código HTTP `404 Not Found`.

### 6.5 Violación PRE-B2 — HallazgoId existe pero ya está eliminado (idempotencia negativa)

**Given**
- Stream `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G3), HallazgoEliminado_v1(G3, ...)]`.
- `_hallazgos[G3].Eliminado=true`.

**When**
- Segundo intento: comando `EliminarHallazgo(InspeccionId=X, HallazgoId=G3, Motivo="Intento duplicado", TecnicoId="rmartinez")`.

**Then**
- Lanza `HallazgoEliminadoException` con mensaje "El hallazgo {G3} ya fue eliminado."
- No se emite segundo evento.
- Código HTTP `422 Unprocessable Entity`.
- Decisión de cliente documentada en §7.

### 6.6 Violación PRE-C — Motivo vacío o null

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].Eliminado=false`.

**When**
- Comando con `Motivo=""` (o solo espacios).

**Then**
- Lanza `MotivoEliminacionVacioException` con mensaje "Motivo de eliminación es obligatorio."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`.

### 6.7 Violación PRE-D (I-H9) — hallazgo tiene hijos activos `[Skip]`

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].Eliminado=false`.
- `_hallazgos[G1]` tiene ≥1 repuesto activo o ≥1 adjunto activo.

**When**
- Comando `EliminarHallazgo(InspeccionId=X, HallazgoId=G1, Motivo="Ya no aplica", TecnicoId="rmartinez")`.

**Then**
- Lanza `HallazgoTieneHijosActivosException` con mensaje "El hallazgo {G1} tiene repuestos o adjuntos activos. Elimínalos antes de eliminar el hallazgo."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`.

> **Decisión de firma (opción 1):** este test debe marcarse `[Fact(Skip="I-H9: requiere slices de repuestos/adjuntos")]`. Los slices `AsignarRepuesto` y `AdjuntarArchivo` no existen aún — no hay eventos para construir el state con hijos activos sin violar la regla de cero mocks de dominio. El código de PRE-D **sí** se implementa en el método de decisión (colecciones vacías en MVP, invariante activa sin cambios cuando lleguen los slices posteriores). El skip se levanta como parte del DoD del primer slice de repuestos o adjuntos.

### 6.8 Violación PRE-F — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando con `InspeccionId=Z`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Código HTTP `404 Not Found`.
- No se emite evento.

### 6.9 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos previos).
- Lista de eventos en orden causal:
  1. `InspeccionIniciada_v1(InspeccionId=X, EquipoId=4521, Estado→EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Manual, ParteEquipoId=77, AccionRequerida=RequiereIntervencion, TipoFallaId=3, CausaFallaId=12, AccionCorrectiva="Reparar sello")`
  3. `HallazgoEliminado_v1(HallazgoId=G1, Motivo="Registrado por error", EliminadoPor="rmartinez", EliminadoEn=T1)`

**When**
- Se reproyectan los tres eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- `Estado=EnEjecucion`.
- `_hallazgos.Count=1` (el hallazgo sigue en la lista — soft delete, no borrado).
- `_hallazgos[G1].Eliminado=true`.
- `_hallazgos[G1].MotivoEliminacion="Registrado por error"`.
- `_hallazgos[G1].Origen=Manual`, `_hallazgos[G1].ParteEquipoId=77` — inalterados.
- `_contribuyentes` incluye `"rmartinez"`.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al que produce el método de decisión seguido de `Apply` in-process.

### 6.10 DoD especial — levantar skip del test §6.7 de ActualizarHallazgoTests.cs (followup #21)

**Given**
- Fixture `StreamConHallazgoEliminado()` existe y emite un stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G5), HallazgoEliminado_v1(G5)]`.
- `Apply(HallazgoEliminado_v1)` está implementado y pone `_hallazgos[G5].Eliminado=true`.

**When**
- Test `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` en `ActualizarHallazgoTests.cs` se ejecuta sin el `[Fact(Skip=...)]` (el skip se elimina).
- Comando `ActualizarHallazgo(InspeccionId=X, HallazgoId=G5, ...)`.

**Then**
- El test pasa en verde: lanza `HallazgoEliminadoException` (PRE-B2 del slice 1d).
- `StreamConHallazgoEliminado()` en los fixtures usa `HallazgoEliminado_v1` real (no `NotImplementedException`).

> **Instrucción para el agente `red`:** este escenario forma parte del DoD de este slice. El agente `red` debe:
> 1. Implementar `StreamConHallazgoEliminado()` en los fixtures (`HallazgoFixtures.cs` o `Fixtures.cs`).
> 2. Quitar el `[Fact(Skip=...)]` del test §6.7 de `ActualizarHallazgoTests.cs`.
> 3. El test debe compilar y pasar en verde (junto con los tests nuevos de este slice) al cerrar el slice.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente envía `X-Client-Command-Id: <UUIDv7>` como header HTTP por cada tap "Eliminar hallazgo". El header se mapea a `MessageId` Wolverine. Si Wolverine detecta el `MessageId` ya procesado (envelope dedup), devuelve la respuesta original sin re-ejecutar el handler. No se emite un segundo `HallazgoEliminado_v1`.

**Idempotencia ante reintentos sin dedup de Wolverine:**

Si el mismo comando llega dos veces (red retry, doble tap) con `X-Client-Command-Id` distinto (o sin dedup activo — followup #15), el segundo intento lanza `HallazgoEliminadoException` (PRE-B2 — el hallazgo ya tiene `Eliminado=true` desde el primer intento). Esto es **no-idempotente** a nivel de dominio: el segundo intento produce un error 422, no silencio. El cliente (PWA React) debe tratar ese `422 HallazgoEliminadoException` como "el hallazgo ya fue eliminado — operación exitosa" y no mostrarlo como error al técnico. Esta decisión se documenta en el contrato del endpoint (§9).

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica.

**Atomicidad:** un único `IDocumentSession.SaveChangesAsync()` persiste el evento `HallazgoEliminado_v1` y actualiza las proyecciones afectadas (§8).

> **Nota sobre dedup real de Wolverine:** el mecanismo concreto de dedup (ADR-008) sigue siendo followup #15 del proyecto. El endpoint valida la presencia del header `X-Client-Command-Id` pero la dedup real a nivel Wolverine outbox store no está implementada. Este slice sigue el mismo patrón que slices 1c y 1d sin avanzar ese followup.

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` (§15.12.1) — actualización del hallazgo eliminado

`DetalleInspeccionView` consume `HallazgoEliminado_v1`. Al recibir el evento, la proyección actualiza el hallazgo con `HallazgoId` correspondiente:
- `Eliminado=true`, `MotivoEliminacion=e.Motivo`, `EliminadoPor=e.EliminadoPor`, `EliminadoEn=e.EliminadoEn`.
- Los hallazgos eliminados se incluyen con flag `Eliminado=true` y `MotivoEliminacion` (§15.12.1 — requisito explícito de auditoría, "no opcional").
- La proyección filtra los eliminados al construir la lista activa para la pantalla del técnico; los incluye en la sección de auditoría.

La proyección `DetalleInspeccionView` no está implementada aún — el slice documenta qué campos actualiza cuando se implemente.

### 8.2 `BandejaInspeccionesPendientesOTView` (§15.12.5) — recomputo de RequiereIntervencion

Si el hallazgo eliminado tenía `AccionRequerida=RequiereIntervencion` y era el único con esa acción, la inspección sale de la bandeja `EsperandoAprobacion`. La proyección consume `HallazgoEliminado_v1` y recomputa el conteo de hallazgos activos con `RequiereIntervencion`.

La proyección `BandejaInspeccionesPendientesOTView` no está implementada aún — se documenta el contrato esperado.

### 8.3 `BandejaTecnicoView` (§15.12.3) — no impactada

Muestra el estado de la inspección, no el detalle de hallazgos. Sin cambio en este slice.

### 8.4 `InspeccionAbiertaPorEquipoView` (§15.12.6) — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio en este slice.

---

## 9. Impacto en endpoints HTTP

**Endpoint nuevo:** `DELETE /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}`

El verbo `DELETE` es coherente con la semántica de eliminación del recurso hallazgo. El body lleva el `Motivo` (obligatorio) porque `DELETE` con payload es válido en HTTP/1.1 y REST, y el motivo es dato de negocio que no cabe en headers.

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO (`EliminarHallazgoRequest`):**

```json
{
  "motivo": "Registrado por error — parte incorrecta seleccionada"
}
```

> `InspeccionId` y `HallazgoId` viajan en el path. `TecnicoId` se extrae del JWT en la capa API (ADR-002 tentativo — mock `const string tecnicoId = "rmartinez"` consistente con slices anteriores hasta que ADR-002 esté resuelto — followup #14).

**Response `204 No Content` (happy path):**

Se usa `204 No Content` en lugar de `200 OK` porque no hay cuerpo de respuesta relevante: el recurso fue eliminado (soft delete). El cliente confirma el éxito por el código HTTP. No hay `EliminadoEn` ni body en la respuesta.

**Respuesta para reintento sin dedup activo:** el segundo intento recibe `422` con `codigoError="PRE-B2-ELIMINADO"`. El cliente PWA debe interpretar ese código como éxito silencioso ("ya fue eliminado — estado deseado alcanzado"). Ver §7.

**Códigos de error:**

| Escenario | Código HTTP | `codigoError` |
|---|---|---|
| Capability ausente (PRE-0) | `403 Forbidden` | `"PRE-0"` |
| InspeccionId no existe (PRE-F) | `404 Not Found` | `"PRE-F"` |
| HallazgoId no existe (PRE-B1) | `404 Not Found` | `"PRE-B1"` |
| HallazgoId ya eliminado (PRE-B2) | `422 Unprocessable Entity` | `"PRE-B2-ELIMINADO"` |
| Inspección no en EnEjecucion (PRE-A / I-H7 / I-F1) | `422 Unprocessable Entity` | `"I-H7"` |
| Motivo vacío (PRE-C) | `422 Unprocessable Entity` | `"PRE-C"` |
| Hallazgo tiene hijos activos (PRE-D / I-H9) | `422 Unprocessable Entity` | `"I-H9"` |

**Rol/permiso requerido:** capability `ejecutar-inspeccion` con el proyecto de la inspección asignado (heredado del host PWA — ADR-002 tentativo).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `HallazgoEliminado_v1` no está en el catálogo de eventos SignalR (ADR-005, §14 del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). La eliminación de un hallazgo es una operación local del técnico — el resultado es visible inmediatamente en su pantalla sin notificación en tiempo real a otras partes en MVP.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `EliminarHallazgo` es puramente local al módulo. No hay llamadas salientes al ERP on-prem. No se requiere WireMock. La eliminación de un hallazgo no retroalimenta al ERP — si el hallazgo tenía `Origen=PreOperacional`, la novedad preop sigue en el estado que estaba (verificada/pendiente según su propio evento) independientemente de si el hallazgo derivado se elimina. La reconciliación con el preop on-prem es responsabilidad de `CerrarInspeccionSaga` (slice posterior).

---

## 12. Preguntas abiertas

Todas resueltas antes de entregar esta spec.

- [x] **¿`Motivo` es obligatorio?** Sí. §15.2 incluye `MotivoEliminacion` en el value object. La eliminación silenciosa viola la trazabilidad requerida por el consultor mecánico. PRE-C lo hace obligatorio.

- [x] **¿El comando `EliminarHallazgo` tiene campo `Motivo` o es inferido?** Explícito en el comando. El técnico lo escribe en un mini-modal en la UI (mismo patrón que `EliminarAdjunto` — §12.10.11 del modelo). El handler no auto-genera el motivo.

- [x] **¿Segundo intento de eliminar un hallazgo ya eliminado es error o no-op?** Es error (`HallazgoEliminadoException` — PRE-B2, `422`). No es naturalmente idempotente. El cliente debe tratar `PRE-B2-ELIMINADO` como "estado deseado alcanzado" y no mostrarlo como error al técnico. Documentado en §7 y §9.

- [x] **¿`Apply(HallazgoEliminado_v1)` debe persistir `MotivoEliminacion` en el record `Hallazgo`?** Sí. §15.2 lo incluye. El record `Hallazgo` necesita el campo `MotivoEliminacion: string?` (añadido por el agente `green` en este slice). El `Apply(HallazgoActualizado_v1)` existente no necesita cambiar.

- [x] **¿La eliminación de un hallazgo con `AccionRequerida=RequiereIntervencion` impacta la firma?** No en este slice. `FirmarInspeccion` evaluará hallazgos no eliminados para V-F3/V-F8 — eso es responsabilidad del slice `FirmarInspeccion`. Aquí solo se documenta que el hallazgo queda `Eliminado=true` en el stream.

- [x] **¿`DELETE` con body es válido?** Sí — HTTP/1.1 (RFC 9110) no prohíbe body en `DELETE`. El `Motivo` es dato de negocio que no cabe en query params (longitud variable, caracteres especiales). Patrón coherente con `EliminarAdjunto` del modelo histórico (§12.10.11). ASP.NET Core lo soporta vía `[FromBody]`.

- [x] **¿El `Apply(HallazgoEliminado_v1)` rompe el rebuild del slice 1d (test §6.7)?** No — lo habilita. El test §6.7 de `ActualizarHallazgoTests.cs` estaba en skip esperando exactamente este evento. Una vez que `Apply(HallazgoEliminado_v1)` exista, `StreamConHallazgoEliminado()` puede construir el stream correctamente y el test verifica PRE-B2. Ver §6.10 y DoD.

- [x] **¿El timestamp del evento es `DateTimeOffset` o `DateTime`?** `DateTimeOffset` — convención vigente del CLAUDE.md. El modelo histórico §12.10.6 usa `DateTime`; la fuente de verdad es CLAUDE.md y los eventos implementados en slices anteriores (`HallazgoActualizado_v1.ActualizadoEn: DateTimeOffset`).

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-0, PRE-F, PRE-A, PRE-B, PRE-C, PRE-D) tienen escenario Given/When/Then en §6 (6.8→PRE-F, 6.3→PRE-A/I-H7/I-F1, 6.4→PRE-B1, 6.5→PRE-B2, 6.6→PRE-C, 6.7→PRE-D/I-H9).
- [x] Todas las invariantes tocadas (I-H7, I-H9, I-F1) tienen escenario de cobertura (6.3→I-H7/I-F1, 6.7→I-H9).
- [x] Happy paths presentes: 6.1 (hallazgo Manual sin hijos), 6.2 (hallazgo RequiereIntervencion — soft delete igual que cualquier otro).
- [x] Escenario rebuild desde stream presente (6.9) — verifica que `Apply(HallazgoEliminado_v1)` es puro y que el hallazgo queda `Eliminado=true` tras reproyección sin lanzar excepción.
- [x] DoD especial documentado (6.10 + §12): levantar skip del test §6.7 de `ActualizarHallazgoTests.cs` (followup #21) — instrucciones explícitas para agente `red`.
- [x] §7 Idempotencia decidida: envelope dedup Wolverine por `X-Client-Command-Id` (ADR-008); segundo intento sin dedup lanza `422 PRE-B2-ELIMINADO`; cliente debe tratarlo como éxito silencioso. Followup #15 sigue pendiente — documentado.
- [x] §10 SignalR marcado explícitamente "no aplica" con justificación.
- [x] §11 Adapters Sinco marcado explícitamente "no aplica".
- [x] §12 Preguntas abiertas: todas respondidas.
- [x] **Firmado por el usuario** — 2026-05-06. §6.7 (PRE-D/I-H9) marcado `[Fact(Skip=...)]` por decisión de firma (opción 1 — hijos activos requiere slices de repuestos/adjuntos).
