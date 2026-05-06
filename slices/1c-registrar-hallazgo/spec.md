# Slice 1c — RegistrarHallazgo

**Autor:** domain-modeler
**Fecha:** 2026-05-06
**Estado:** draft
**Agregado afectado:** `Inspeccion` (primer evento de captura sobre el aggregate iniciado en slice 1a)
**Decisiones previas relevantes:**
- `slices/1a-iniciar-inspeccion-aggregate/spec.md` — aggregate puro, `InspeccionIniciada_v1`, invariantes I-I1..I-I3, I2 (estado EnEjecucion), I2b (contribuyentes derivados)
- `slices/1b-iniciar-inspeccion-handler/spec.md` — handler, `InspeccionAbiertaPorEquipoView`, endpoint `POST /api/v1/inspecciones`
- `01-modelo-dominio.md §15.2` — estructura final del `Hallazgo` (value object): `ParteEquipoId`, `Origen`, `AccionRequerida`, `TipoFallaId`, `CausaFallaId`, `NovedadPreopOrigenId`, `SeguimientoOrigenId`, `Eliminado`
- `01-modelo-dominio.md §15.3` — invariantes I-H1..I-H11 del Hallazgo
- `01-modelo-dominio.md §15.4` — catálogo MVP de 24 eventos; `HallazgoRegistrado_v1` es el evento #6
- `01-modelo-dominio.md §15.9` — patrón unificado de 3 opciones: Origen ∈ {Manual, PreOperacional, Seguimiento}
- `01-modelo-dominio.md §15.12.1` — `DetalleInspeccionView` consume `HallazgoRegistrado_v1`
- `01-modelo-dominio.md §15.12.3` — `BandejaTecnicoView` no actualiza conteos por hallazgo individual (diferido)
- `FOLLOWUPS.md #12` — test de evento desconocido en `AplicarEvento` se resuelve en el primer slice que añada un segundo `case`; este slice es ese momento
- ADR-004 (`§9.15`) — `ParteLocal` sincronizado desde catálogo; el handler valida existencia de `ParteEquipoId` contra la snapshot `EquipoLocal.Partes[]` o `ParteLocal`, no contra el ERP en línea
- ADR-006 (`§16`) — no aplica a este slice (sin POST saliente al ERP)
- ADR-008 (`§9.16`) — idempotencia por `X-Client-Command-Id` (mismo patrón que slice 1b)

---

## 1. Intención

El técnico necesita registrar una observación técnica durante la inspección. Dependiendo del origen, puede ser un hallazgo descubierto directamente durante la inspección (`Manual`), o una novedad del preoperacional que el técnico decide importar (`PreOperacional`). En ambos casos indica si requiere intervención correctiva (genera OT), seguimiento continuo (abre `SeguimientoHallazgo` al firmar) o ninguna acción adicional.

Este slice cubre exclusivamente los orígenes `Manual` y `PreOperacional` — los únicos que dispara el técnico activamente desde la pantalla de inspección técnica. Los orígenes `Seguimiento` y `Monitoreo` se modelan en slices posteriores porque dependen de aggregates (`SeguimientoHallazgo`, flujo monitoreo) que aún no existen.

---

## 2. Comando

```csharp
public sealed record RegistrarHallazgo(
    Guid   InspeccionId,
    Guid   HallazgoId,             // generado por el cliente (UUIDv7 preferido)
    OrigenHallazgo Origen,         // Manual | PreOperacional  (Seguimiento y Monitoreo: slices futuros)
    int    ParteEquipoId,          // PK del ERP — obligatorio (I-H1); validado contra catálogo local
    int?   NovedadPreopOrigenId,   // obligatorio si Origen=PreOperacional (I-H2); null si Origen=Manual (I-H3)
    int?   ActividadId,            // opcional — ID de actividad del catálogo Sinco (presente si Origen=PreOperacional)
    string? ActividadDescripcion,  // texto libre — presente si Origen=Manual; heredado del preop si PreOperacional
    string NovedadTecnica,         // descripción técnica del hallazgo — obligatorio, no vacío
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,      // texto libre — obligatorio si AccionRequerida=RequiereIntervencion
    int?   TipoFallaId,            // obligatorio si AccionRequerida=RequiereIntervencion (I-H4)
    int?   CausaFallaId,           // obligatorio si AccionRequerida=RequiereIntervencion (I-H4)
    string? ObservacionCampo,      // texto libre opcional — nota adicional del técnico
    UbicacionGps? Ubicacion,       // GPS al registrar — opcional (no GPS en taller cubierto)
    string EmitidoPor              // tecnicoId opaco del JWT
) : ICommand;
```

**Restricciones de scope de este slice:**

- `Origen ∈ {Manual, PreOperacional}`. El comando rechaza `Seguimiento` y `Monitoreo` con excepción explícita hasta que sus slices respectivos estén implementados.
- `HallazgoId` lo genera el cliente (mismo patrón que `InspeccionId` en slice 1b: UUIDv7 client-side, fallback `Guid.NewGuid()` en handler si no viene).

---

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `HallazgoRegistrado_v1` | Ver campos a continuación | Siempre que el comando pase todas las precondiciones e invariantes |

```csharp
public sealed record HallazgoRegistrado_v1(
    Guid   InspeccionId,
    Guid   HallazgoId,
    OrigenHallazgo Origen,
    int?   NovedadPreopOrigenId,   // poblado si Origen=PreOperacional; null si Manual
    int    ParteEquipoId,
    int?   ActividadId,            // poblado si Origen=PreOperacional
    string? ActividadDescripcion,  // poblado si Origen=Manual; puede heredarse de preop
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,      // presente si AccionRequerida=RequiereIntervencion
    int?   TipoFallaId,            // presente si AccionRequerida=RequiereIntervencion
    int?   CausaFallaId,           // presente si AccionRequerida=RequiereIntervencion
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTimeOffset RegistradoEn    // TimeProvider.GetUtcNow() en el handler
);
```

**Nota sobre `ResultadoVerificacion`:** eliminado del modelo en §15.2. El evento no lo incluye. La distinción Verificar / Seguimiento / Descartar se expresa enteramente por `AccionRequerida` (para los primeros dos) y por el evento `NovedadPreopDescartada_v1` (para el tercero), que es un comando distinto (`DescartarNovedadPreop` — slice posterior).

**Nota sobre el Adapter Preop:** cuando `Origen=PreOperacional`, la `CerrarInspeccionSaga` (slice posterior) hace `POST /preop/novedades/{id}/verificar` al ERP con el `NovedadPreopOrigenId`. Este slice no invoca el adapter — solo emite el evento. El adapter se implementa en el slice de la saga (§3.D roadmap paso 3.28).

---

## 4. Precondiciones

Las precondiciones viven en el **método de decisión del aggregate** (capa que produce el evento), salvo PRE-1 y PRE-2 que pertenecen a la capa HTTP/handler. Los `Apply` son puros y nunca re-validan.

- **PRE-1 (capa HTTP):** capability `ejecutar-inspeccion` requerida. Excepción: `403 Forbidden`. Mismo mecanismo de claims que slice 1b.
- **PRE-2 (handler):** `InspeccionId` debe existir como stream en Marten (el aggregate debe haber sido creado). El handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es null → excepción `InspeccionNoEncontradaException` (`404 Not Found`).
- **PRE-3 (aggregate — método de decisión):** `Estado == EnEjecucion`. Si la inspección ya está `Firmada`, `Cerrada`, `CerradaSinOT` o `Cancelada` → `InspeccionNoEnEjecucionException` (`422 Unprocessable Entity`).
- **PRE-4 (handler):** `ParteEquipoId` debe existir en el catálogo local. El handler valida contra `EquipoLocal` cargado del aggregate (las partes del equipo vienen embebidas en `InspeccionIniciada_v1` — si el handler necesita validar contra el catálogo, usa `session.Query<EquipoLocal>().SingleOrDefault(e => e.EquipoId == inspeccion.EquipoId)`). Si el `ParteEquipoId` no pertenece al equipo de la inspección → `ParteNoCorrespondeAlEquipoException` (`422 Unprocessable Entity`).
- **PRE-5 (aggregate — método de decisión):** si `Origen=PreOperacional`, `NovedadPreopOrigenId` debe ser non-null (I-H2). Si es null → `NovedadPreopOrigenIdRequeridoException`.
- **PRE-6 (aggregate — método de decisión):** si `Origen=Manual`, `NovedadPreopOrigenId` debe ser null (I-H3). Si no es null → `NovedadPreopOrigenIdNoPermitidoException`.
- **PRE-7 (aggregate — método de decisión):** si `AccionRequerida=RequiereIntervencion`, `TipoFallaId` y `CausaFallaId` deben ser non-null (I-H4). Si alguno falta → `TipoYCausaFallaRequeridosException`.
- **PRE-8 (aggregate — método de decisión):** si `AccionRequerida=RequiereIntervencion`, `AccionCorrectiva` debe ser non-null y no vacío. Excepción: `AccionCorrectivaRequeridaException`.
- **PRE-9 (aggregate — método de decisión):** `NovedadTecnica` no puede ser null ni vacío. Excepción: `NovedadTecnicaVaciaException`.
- **PRE-10 (aggregate — método de decisión):** `Origen` debe ser `Manual` o `PreOperacional`. Si es `Seguimiento` o `Monitoreo` → `OrigenNoSoportadoException` (con mensaje "Origen {X} se implementa en slice posterior"). Esta precondición se elimina cuando los slices de seguimiento y monitoreo estén implementados.

> **Capa donde viven:** PRE-1 en capa HTTP; PRE-2 y PRE-4 en el handler (acceso a Marten/catálogos); PRE-3, PRE-5, PRE-6, PRE-7, PRE-8, PRE-9, PRE-10 en el método de decisión del aggregate. Los `Apply(HallazgoRegistrado_v1)` son puros — no re-validan ninguna de estas condiciones.

---

## 5. Invariantes tocadas

- **I2** (`01-modelo-dominio.md §2.1`): solo se pueden agregar hallazgos en estado `EnEjecucion`. Cubierta por PRE-3.
- **I2b** (`01-modelo-dominio.md §2.1`): `EmitidoPor` del evento se agrega automáticamente al `HashSet<string> _contribuyentes` en `Apply(HallazgoRegistrado_v1)` — derivado, sin evento dedicado.
- **I-H1** (`§15.3`): `ParteEquipoId` siempre presente (no nullable). Cubierta por PRE-4 (capa handler) e implícita en el tipo del record (`int` no nullable).
- **I-H2** (`§15.3`): `Origen=PreOperacional` → `NovedadPreopOrigenId` obligatorio e inmutable; `SeguimientoOrigenId` debe ser null (no aplica en este slice). Cubierta por PRE-5.
- **I-H3** (`§15.3`): `Origen=Manual` → `NovedadPreopOrigenId` debe ser null; `SeguimientoOrigenId` debe ser null. Cubierta por PRE-6.
- **I-H4** (`§15.3`): `AccionRequerida=RequiereIntervencion` → `TipoFallaId` y `CausaFallaId` obligatorios. Cubierta por PRE-7.
- **I-H5** (`§15.3`): `AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}` → `TipoFallaId` y `CausaFallaId` pueden ser null. Esta regla no genera excepción; simplemente el aggregate no los exige. El escenario de happy path con `RequiereSeguimiento` sin tipo/causa la cubre.
- **I-H6** (`§15.3`): múltiples hallazgos sobre la misma `ParteEquipoId` están permitidos. No hay validación de unicidad. Verificar con escenario explícito (dos hallazgos sobre la misma parte).

**Invariante nueva — INV-PartePerteneceAlEquipo:** el `ParteEquipoId` debe corresponder al equipo de la inspección. Esta regla no está numerada en §15.3 pero es operativamente necesaria — sin ella, el técnico podría registrar hallazgos con partes de otro equipo. Se documenta como `INV-PartePerteneceAlEquipo` y se propone agregar a §15.3 en este PR.

> **Propuesta de enmienda a §15.3:** añadir `I-H12 ParteEquipoId debe pertenecer al equipo de la inspección (validado contra EquipoLocal.Partes[] en el handler)`.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — Origen=Manual, AccionRequerida=NoRequiereIntervencion

**Given**
- Stream `inspeccion-{id}` contiene `InspeccionIniciada_v1` con `EquipoId=4521`, `Estado=EnEjecucion`.
- `EquipoLocal[4521].Partes` incluye `ParteEquipoId=77` (parte válida del equipo).
- `HallazgoId=G1` no existe aún en la lista de hallazgos del aggregate.

**When**
- Comando `RegistrarHallazgo(InspeccionId=X, HallazgoId=G1, Origen=Manual, ParteEquipoId=77, NovedadPreopOrigenId=null, ActividadId=null, ActividadDescripcion="Revisión visual de manguera", NovedadTecnica="Manguera con desgaste leve superficial", AccionRequerida=NoRequiereIntervencion, AccionCorrectiva=null, TipoFallaId=null, CausaFallaId=null, ObservacionCampo=null, Ubicacion=null, EmitidoPor="ana.gomez")`.

**Then**
- Se emite exactamente un `HallazgoRegistrado_v1` con todos los campos del comando más `RegistradoEn=DateTimeOffset.UtcNow(TimeProvider)`.
- `TipoFallaId=null`, `CausaFallaId=null`, `AccionCorrectiva=null` en el evento (I-H5 — opcionales para este AccionRequerida).
- El aggregate en memoria tiene `Hallazgos.Count=1`, `Hallazgos[0].ParteEquipoId=77`, `Hallazgos[0].Eliminado=false`.
- `_contribuyentes` contiene `"ana.gomez"`.

### 6.2 Happy path — Origen=PreOperacional, AccionRequerida=RequiereIntervencion

**Given**
- Stream `inspeccion-{id}` con `Estado=EnEjecucion`, `EquipoId=4521`.
- `EquipoLocal[4521].Partes` incluye `ParteEquipoId=88`.
- Novedad preop con `NovedadPreopOrigenId=1042` existe en el preop (el módulo no valida esto en línea — solo exige que el campo esté presente).

**When**
- Comando `RegistrarHallazgo(InspeccionId=X, HallazgoId=G2, Origen=PreOperacional, ParteEquipoId=88, NovedadPreopOrigenId=1042, ActividadId=55, ActividadDescripcion=null, NovedadTecnica="Fuga confirmada en sello hidráulico", AccionRequerida=RequiereIntervencion, AccionCorrectiva="Reemplazar sello hidráulico y rellenar aceite", TipoFallaId=3, CausaFallaId=12, ObservacionCampo="Fuga visualmente confirmada con luz UV", Ubicacion=UbicacionGps(4.711, -74.072, 8.5, now), EmitidoPor="ana.gomez")`.

**Then**
- Se emite `HallazgoRegistrado_v1` con `Origen=PreOperacional`, `NovedadPreopOrigenId=1042`, `ActividadId=55`, `TipoFallaId=3`, `CausaFallaId=12`, `AccionCorrectiva` no nulo.
- `ActividadDescripcion=null` en el evento (viene de catálogo preop, no de texto libre).
- `Hallazgos.Count=1`, hallazgo no eliminado.

### 6.3 Happy path — Origen=Manual, AccionRequerida=RequiereSeguimiento (sin tipo/causa — I-H5)

**Given**
- Mismo aggregate `EnEjecucion`.
- `ParteEquipoId=77` pertenece al equipo.

**When**
- Comando con `AccionRequerida=RequiereSeguimiento`, `TipoFallaId=null`, `CausaFallaId=null`, `AccionCorrectiva=null`.

**Then**
- Se emite `HallazgoRegistrado_v1` con `TipoFallaId=null`, `CausaFallaId=null`.
- No lanza excepción (I-H5: opcionales para este AccionRequerida).
- `followup #1` registrado en `FOLLOWUPS.md` documenta que la ausencia de tipo/causa degrada la reportería de seguimientos — aceptado en MVP.

### 6.4 Múltiples hallazgos sobre la misma parte (I-H6 — permitido)

**Given**
- Aggregate con `Hallazgos` = `[{HallazgoId=G1, ParteEquipoId=77}]`.

**When**
- Segundo comando con `HallazgoId=G2`, `ParteEquipoId=77` (misma parte).

**Then**
- Se emite `HallazgoRegistrado_v1` para G2 con `ParteEquipoId=77`.
- `Hallazgos.Count=2`, ambos activos, ambos sobre parte 77.
- No lanza excepción (I-H6: multiplicidad permitida).

### 6.5 Violación PRE-3 — inspección no está en estado EnEjecucion

**Given**
- Aggregate con `Estado=Firmada` (ya firmado — slice posterior agrega el evento).
- Para este test: aggregate construido manualmente con `Estado=Firmada` o con eventos `[InspeccionIniciada_v1, InspeccionFirmada_v1]` en el stream.

**When**
- Comando `RegistrarHallazgo(InspeccionId=X, ...)`.

**Then**
- Lanza `InspeccionNoEnEjecucionException` con mensaje que incluye el estado actual.
- No se emite ningún evento.

### 6.6 Violación I-H1 / INV-PartePerteneceAlEquipo — parte no pertenece al equipo

**Given**
- `EquipoLocal[4521].Partes` no contiene `ParteEquipoId=9999`.
- Aggregate con `EquipoId=4521`, `Estado=EnEjecucion`.

**When**
- Comando con `ParteEquipoId=9999`.

**Then**
- Lanza `ParteNoCorrespondeAlEquipoException` con mensaje "La parte {9999} no pertenece al equipo {4521}. Selecciona una parte válida de este equipo."
- Código HTTP `422 Unprocessable Entity` con `{ "codigoError": "INV-PartePerteneceAlEquipo", "mensaje": "..." }`.
- No se emite ningún evento.

### 6.7 Violación I-H2 — Origen=PreOperacional sin NovedadPreopOrigenId

**Given**
- Aggregate `EnEjecucion`.
- Parte válida.

**When**
- Comando con `Origen=PreOperacional`, `NovedadPreopOrigenId=null`.

**Then**
- Lanza `NovedadPreopOrigenIdRequeridoException` con mensaje "NovedadPreopOrigenId es obligatorio cuando Origen=PreOperacional."
- No se emite evento.

### 6.8 Violación I-H3 — Origen=Manual con NovedadPreopOrigenId presente

**Given**
- Aggregate `EnEjecucion`.
- Parte válida.

**When**
- Comando con `Origen=Manual`, `NovedadPreopOrigenId=999`.

**Then**
- Lanza `NovedadPreopOrigenIdNoPermitidoException` con mensaje "NovedadPreopOrigenId debe ser null cuando Origen=Manual."
- No se emite evento.

### 6.9 Violación I-H4 — AccionRequerida=RequiereIntervencion sin TipoFallaId

**Given**
- Aggregate `EnEjecucion`. Parte válida.

**When**
- Comando con `AccionRequerida=RequiereIntervencion`, `TipoFallaId=null`, `CausaFallaId=5`.

**Then**
- Lanza `TipoYCausaFallaRequeridosException` con mensaje "TipoFallaId y CausaFallaId son obligatorios cuando AccionRequerida=RequiereIntervencion."
- No se emite evento.

### 6.10 Violación I-H4 — AccionRequerida=RequiereIntervencion sin CausaFallaId

**Given / When** — mismo que 6.9 pero `TipoFallaId=3`, `CausaFallaId=null`.

**Then** — lanza `TipoYCausaFallaRequeridosException`.

### 6.11 Violación PRE-8 — AccionRequerida=RequiereIntervencion sin AccionCorrectiva

**Given**
- Aggregate `EnEjecucion`. Parte válida. `TipoFallaId=3`, `CausaFallaId=12`.

**When**
- Comando con `AccionRequerida=RequiereIntervencion`, `AccionCorrectiva=null`.

**Then**
- Lanza `AccionCorrectivaRequeridaException` con mensaje "AccionCorrectiva es obligatoria cuando AccionRequerida=RequiereIntervencion."
- No se emite evento.

### 6.12 Violación PRE-9 — NovedadTecnica vacía

**Given**
- Aggregate `EnEjecucion`. Parte válida.

**When**
- Comando con `NovedadTecnica=""` (o `null`).

**Then**
- Lanza `NovedadTecnicaVaciaException`.
- No se emite evento.

### 6.13 Violación PRE-10 — Origen=Seguimiento (no soportado en este slice)

**Given**
- Aggregate `EnEjecucion`. Parte válida.

**When**
- Comando con `Origen=Seguimiento`.

**Then**
- Lanza `OrigenNoSoportadoException` con mensaje "Origen 'Seguimiento' aún no soportado. Se implementa en slice posterior."
- No se emite evento.

### 6.14 Violación PRE-2 — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando con `InspeccionId=Z`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Capa API devuelve `404 Not Found`.
- No se emite evento.

### 6.15 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos previos).
- Lista de eventos ordenados: `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G1, Manual, 77, NoRequiereIntervencion), HallazgoRegistrado_v1(G2, PreOperacional, 88, RequiereIntervencion, TipoFallaId=3, CausaFallaId=12)]`.

**When**
- Se reproyectan los eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- `Estado=EnEjecucion`.
- `Hallazgos.Count=2`.
- `Hallazgos[0].HallazgoId=G1`, `Origen=Manual`, `ParteEquipoId=77`, `AccionRequerida=NoRequiereIntervencion`, `Eliminado=false`.
- `Hallazgos[1].HallazgoId=G2`, `Origen=PreOperacional`, `NovedadPreopOrigenId=1042`, `TipoFallaId=3`, `CausaFallaId=12`, `Eliminado=false`.
- `_contribuyentes` contiene los `EmitidoPor` de todos los eventos.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al que habría producido el método de decisión seguido de `Apply` in-process.

> Este escenario además cubre el followup #12 (test de evento desconocido): el `red` de este slice añade un sub-escenario adicional que pasa un tipo de evento anónimo a `Inspeccion.Reconstruir` y verifica que lanza `InvalidOperationException`. El momento natural de cubrir la rama defensiva ha llegado — este slice añade el segundo `case` en `AplicarEvento`.

### 6.16 Idempotencia — replay del mismo HallazgoId

**Given**
- Aggregate con `Hallazgos=[{HallazgoId=G1, ...}]`.

**When**
- Segundo comando con el mismo `HallazgoId=G1` (retry del cliente, mismo `X-Client-Command-Id`).

**Then**
- Wolverine envelope dedup detecta el `X-Client-Command-Id` ya procesado y devuelve la respuesta original.
- No se emite un segundo `HallazgoRegistrado_v1`.
- `Hallazgos.Count` permanece en 1.
- Respuesta HTTP `200 OK` con el body original.

> Justificación: el modelo no impone unicidad de `HallazgoId` en el aggregate (podría haber coincidencias client-side por defecto de generación UUIDv7). La idempotencia se delega al envelope dedup de Wolverine (ADR-008), no a una verificación interna del aggregate. Si el cliente envía un `HallazgoId` diferente con el mismo `X-Client-Command-Id`, el envelope dedup igualmente devuelve la respuesta original del primer envío.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente genera `X-Client-Command-Id: UUIDv7` por cada tap "Registrar hallazgo". Viaja como header HTTP, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler.

**Idempotencia natural por `HallazgoId`:**

El `HallazgoId` lo genera el cliente (UUIDv7). El aggregate no valida unicidad de `HallazgoId` — si el cliente genera dos hallazgos distintos con IDs distintos, ambos se persisten legítimamente (es el caso normal). La protección contra duplicación real viene del envelope dedup sobre `X-Client-Command-Id`.

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica. El adapter `POST /preop/novedades/{id}/verificar` lo invoca `CerrarInspeccionSaga` al firmar (slice posterior).

**Atomicidad:** un único `IDocumentSession.SaveChangesAsync()` persiste el evento `HallazgoRegistrado_v1` y actualiza las proyecciones impactadas (§8).

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` — actualización inline

`DetalleInspeccionView` (§15.12.1) consume `HallazgoRegistrado_v1`. Al recibir este evento, la proyección añade el hallazgo a la lista de hallazgos activos de la vista, con todos los campos del evento. La proyección es async (no inline con el commit del handler) — su delay es aceptable para la UX de detalle.

Campos a añadir en la proyección por cada `HallazgoRegistrado_v1`:
- `HallazgoId`, `Origen`, `ParteEquipoId`, `NovedadPreopOrigenId`, `ActividadId`, `ActividadDescripcion`, `NovedadTecnica`, `AccionRequerida`, `AccionCorrectiva`, `TipoFallaId`, `CausaFallaId`, `ObservacionCampo`, `Ubicacion`, `EmitidoPor`, `RegistradoEn`, `Eliminado=false`.

**La proyección `DetalleInspeccionView` no existe aún** — será implementada en el slice que la priorice según el roadmap (paso 3.45). Este slice no la crea; solo documenta qué evento consume.

### 8.2 `AuditoriaInspeccionesView` — consumirá este evento en slice futuro

`AuditoriaInspeccionesView` (§15.12.2) incluye `HallazgosCounts` derivado de `HallazgoRegistrado_v1`. Sin embargo, la proyección se implementa en un slice posterior (paso 3.55 del roadmap). Este slice no la toca.

### 8.3 `BandejaTecnicoView` — no impactada

`BandejaTecnicoView` (§15.12.3) no consume `HallazgoRegistrado_v1` directamente — muestra el estado de la inspección, no el detalle de hallazgos. Sin cambio en este slice.

### 8.4 `InspeccionAbiertaPorEquipoView` — no impactada

La proyección solo reacciona a eventos de lifecycle (`InspeccionIniciada_v1`, `InspeccionFirmada_v1`, `InspeccionCancelada_v1`). Sin cambio en este slice.

---

## 9. Impacto en endpoints HTTP

**Endpoint nuevo:** `POST /api/v1/inspecciones/{id}/hallazgos` (paso 3.37 del roadmap).

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO (`RegistrarHallazgoRequest`):**

```json
{
  "hallazgoId": "0193a4f7-...",
  "origen": "Manual",
  "parteEquipoId": 77,
  "novedadPreopOrigenId": null,
  "actividadId": null,
  "actividadDescripcion": "Revisión visual de manguera",
  "novedadTecnica": "Manguera con desgaste leve superficial",
  "accionRequerida": "NoRequiereIntervencion",
  "accionCorrectiva": null,
  "tipoFallaId": null,
  "causaFallaId": null,
  "observacionCampo": null,
  "ubicacion": null
}
```

> `hallazgoId` viaja en el body (UUIDv7 client-side). `InspeccionId` viaja en el path (`{id}`). `EmitidoPor` se extrae del JWT en la capa API (ADR-002 — mientras ADR-002 esté tentativo, el mock de claims provee `tecnicoId` como en slice 1b).

**Response 201 Created (happy path):**

```json
{
  "hallazgoId": "0193a4f7-...",
  "inspeccionId": "...",
  "accionRequerida": "NoRequiereIntervencion",
  "registradoEn": "2026-05-06T13:45:22.000Z"
}
```

**Códigos de error:**

| Escenario | Código HTTP | `codigoError` |
|---|---|---|
| Capability ausente (PRE-1) | `403 Forbidden` | `"PRE-1"` |
| InspeccionId no existe (PRE-2) | `404 Not Found` | `"PRE-2"` |
| Inspección no en EnEjecucion (PRE-3) | `422 Unprocessable Entity` | `"I2"` |
| Parte no pertenece al equipo (PRE-4) | `422 Unprocessable Entity` | `"INV-PartePerteneceAlEquipo"` |
| NovedadPreopOrigenId requerido (PRE-5) | `422 Unprocessable Entity` | `"I-H2"` |
| NovedadPreopOrigenId no permitido (PRE-6) | `422 Unprocessable Entity` | `"I-H3"` |
| TipoFallaId/CausaFallaId faltantes (PRE-7) | `422 Unprocessable Entity` | `"I-H4"` |
| AccionCorrectiva requerida (PRE-8) | `422 Unprocessable Entity` | `"PRE-8"` |
| NovedadTecnica vacía (PRE-9) | `422 Unprocessable Entity` | `"PRE-9"` |
| Origen no soportado (PRE-10) | `422 Unprocessable Entity` | `"PRE-10"` |

**Rol/permiso requerido:** capability `ejecutar-inspeccion` con el proyecto de la inspección asignado (heredado del host PWA — ADR-002 tentativo).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `HallazgoRegistrado_v1` no está en el catálogo de eventos SignalR (ADR-005, §14 del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). El registro de hallazgos es una operación local — el técnico ve el resultado en la pantalla inmediatamente; no hay otras partes que necesiten notificación en tiempo real por este evento en MVP.

> Si en el futuro emerja colaboración multi-técnico "en tiempo real" (técnico A ve los hallazgos que técnico B registra sin recargar pantalla), se añadiría push `HallazgoAgregado` con audiencia `Group=inspeccionId`. Es un cambio aditivo que no afecta este slice.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**Lectura de catálogo local (ADR-004):**

- `EquipoLocal` — el handler consulta las partes del equipo (`EquipoLocal.Partes[]`) para validar `INV-PartePerteneceAlEquipo` (PRE-4). Esta lectura es del catálogo sincronizado localmente (M-3b), no del ERP en línea.
- `TipoFallaLocal` y `CausaFallaLocal` — el handler **no** valida que `TipoFallaId` y `CausaFallaId` existan en el catálogo local (M-11/M-12) en este slice MVP. La validación de existencia se considera una mejora futura (la UI presenta solo los IDs del catálogo sincronizado, por lo que IDs inválidos son poco probables). Si emerge un caso, se trata como pregunta abierta en el slice que implemente los catálogos de causas/tipos de falla.

**Sin llamadas salientes al ERP:**

Este comando no invoca ningún endpoint Sinco on-prem. El adapter `POST /preop/novedades/{id}/verificar` (P-2 de `06-contrato-apis-erp.md`) es responsabilidad de `CerrarInspeccionSaga` (roadmap paso 3.28) — slice posterior. Marcado explícitamente: **no aplica en este slice**.

---

## 12. Preguntas abiertas

Todas resueltas antes de entregar este spec.

- [x] **¿`Origen=Seguimiento` y `Origen=Monitoreo` entran en este slice?** No. `Seguimiento` depende de `SeguimientoHallazgo` (aggregate no existente aún); `Monitoreo` depende del flujo MVP de monitoreo (§3.B' roadmap). Ambos se modelan en slices posteriores. PRE-10 bloquea esos orígenes con excepción explícita y mensaje descriptivo. La restricción es temporal y se quita cuando el slice respectivo la implemente. Asunción documentada: el `enum OrigenHallazgo` ya incluye todos los valores en el código del slice 1a; PRE-10 es una guardia en el método de decisión, no un cambio del enum.

- [x] **¿Cómo valida el handler que `ParteEquipoId` pertenece al equipo?** Via `EquipoLocal.Partes[]` — la lista de partes embebida en el catálogo local del equipo (M-3b trae partes embebidas según CLAUDE.md y roadmap 4.6). El handler carga `EquipoLocal` por `EquipoId` del aggregate y verifica que `ParteEquipoId ∈ equipo.Partes.Select(p => p.ParteEquipoId)`. Si `EquipoLocal` no tiene partes (sync incompleto), el handler rechaza con `ParteNoCorrespondeAlEquipoException` — comportamiento defensivo correcto para MVP.

- [x] **¿El handler valida existencia de `NovedadPreopOrigenId` contra el sistema preop?** No. El módulo no llama al ERP preop en tiempo de registro. Solo exige que el campo esté presente cuando `Origen=PreOperacional` (I-H2). La validación real de que la novedad existe en el preop la hace el adapter de la saga de cierre al llamar `POST /preop/novedades/{id}/verificar` — si la novedad no existe allá, el adapter fallará y la saga manejará el error. Este comportamiento es coherente con el diseño eventual del sistema.

- [x] **¿`ActividadDescripcion` viene del catálogo o es texto libre?** Depende del origen: `Origen=Manual` → texto libre obligatorio (el técnico describe la actividad); `Origen=PreOperacional` → puede ser null (la descripción de la actividad viene del catálogo del preop, el módulo solo almacena `ActividadId`). El campo del evento acepta null para el caso PreOperacional — coherente con §15.2 y el payload canónico.

- [x] **¿Se propone enmienda a §15.3 por `INV-PartePerteneceAlEquipo`?** Sí. Se documenta como `I-H12` y se propone agregar a §15.3 en el PR de este slice. No es una invariante nueva inventada — es la materialización operativa de I-H1 (parte obligatoria) extendida con la restricción de que debe pertenecer al equipo inspeccionado. Sin esta regla, I-H1 se cumple pero el hallazgo queda mal referenciado.

- [x] **¿`AccionCorrectiva` es obligatorio con `RequiereIntervencion`?** Sí. El campo no está en §15.3 como invariante numerada pero emerge de la semántica de negocio: si hay intervención, debe haber una acción correctiva planificada. El modelo histórico (§2.1) tenía `ActividadDescripcion` en ese rol; en §15.2 el campo es `AccionCorrectiva` (texto libre). Se modela como PRE-8 y se propone documentar como regla en §15.3 si el usuario valida. Asunción conservadora: se exige para no perder datos que la saga necesitará al construir el payload de OT.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-10) tienen escenario Given/When/Then en §6 (6.5→PRE-3, 6.6→INV-PartePerteneceAlEquipo, 6.7→PRE-5/I-H2, 6.8→PRE-6/I-H3, 6.9→PRE-7/I-H4, 6.10→PRE-7/I-H4 variante, 6.11→PRE-8, 6.12→PRE-9, 6.13→PRE-10, 6.14→PRE-2).
- [x] Todas las invariantes tocadas (I2, I2b, I-H1..I-H6, INV-PartePerteneceAlEquipo) tienen escenario de cobertura.
- [x] Happy paths presentes: 6.1 (Manual/NoRequiereIntervencion), 6.2 (PreOperacional/RequiereIntervencion), 6.3 (Manual/RequiereSeguimiento sin tipo/causa).
- [x] Escenario adicional de I-H6 (multiplicidad permitida sobre misma parte): 6.4.
- [x] Escenario rebuild desde stream presente (6.15) — incluye resolución del followup #12.
- [x] Idempotencia decidida (§7): envelope dedup ADR-008 + escenario 6.16.
- [x] §10 SignalR resuelto explícitamente como "no aplica" con justificación.
- [x] §11 Adapters Sinco resuelto: solo lectura de catálogo local (`EquipoLocal`); sin llamadas salientes al ERP en este slice.
- [x] §12 Preguntas abiertas: todas respondidas.
- [x] Propuesta de enmienda a §15.3 (`I-H12`) documentada.
- [x] Followup #12 resuelto en este slice (test de evento desconocido en `AplicarEvento`).
- [ ] **Firma del usuario pendiente** — al firmar, el slice pasa a `red`.
