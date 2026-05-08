# Slice 1i — RegistrarMedicion

**Autor:** domain-modeler
**Fecha:** 2026-05-08
**Estado:** firmado (2026-05-08 por Jaime — decisiones P-1/P-2/P-3 aplicadas)
**Agregado afectado:** `Inspeccion` (aggregate unificado — discriminador `TipoInspeccion.Monitoreo`).
**Decisiones previas relevantes:**
- `slices/1h-iniciar-inspeccion-monitoreo/spec.md` — predecesor inmediato. Establece el estado del aggregate tras iniciar: `ItemsSnapshot` poblado, `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `slices/1c-registrar-hallazgo/spec.md` — precedente de claims, GPS, timestamp, errores, y el check `PRE-10 OrigenNoSoportadoException` que bloquea `Origen=Monitoreo` y que este slice elimina.
- `01-modelo-dominio.md §12.11.5 puntos 5, 6, 8` — shapes canónicos de `MedicionRegistrada_v1`, trigger de hallazgo automático, invariantes derivadas de `Origen=Monitoreo`.
- `01-modelo-dominio.md §15.3` — invariantes I-H1..I-H12; este slice agrega I-M1..I-M5 (ver §5).
- `01-modelo-dominio.md §15.4` — catálogo MVP de 24 eventos; `MedicionRegistrada_v1` listado como evento monitoreo.
- `roadmap.md §3.B' pasos 3.16d y 3.16f` — extensión `HallazgoRegistrado_v1` con `MedicionOrigenId: int?` y comando `RegistrarMedicion`.
- ADR-002 (`§9.11`) — claims recibidos del host PWA como parámetro, nunca leídos del HTTP context en el dominio.
- ADR-006 (`§16`) — outbox transaccional; atomicidad multi-evento en un único `SaveChangesAsync`.
- ADR-008 (`§9.16`) — `clientCommandId` UUIDv7 como `MessageId` Wolverine; idempotencia end-to-end.
- CLAUDE.md — atomicidad eventos, `Apply` puro, rebuild test obligatorio, IDs `int` ERP + `Guid` internos.

---

## 1. Intención

El técnico de campo necesita registrar el valor medido para un ítem numérico de la rutina de monitoreo durante la ejecución de la inspección. El sistema calcula automáticamente si el valor cae dentro o fuera del rango esperado (`MedicionEsperada.ValorMin..ValorMax`) usando la copia snapshoteada en `InspeccionIniciada_v1.ItemsSnapshot`.

Si la medición **cae fuera del rango**, el sistema emite atómicamente —en un único `SaveChangesAsync`— el evento de medición **y** un `HallazgoRegistrado_v1` con `Origen=Monitoreo`, `AccionRequerida=RequiereSeguimiento` y trazabilidad bidireccional (`MedicionOrigenId=ItemId`). La `NovedadTecnica` del hallazgo automático incluye el valor medido y el rango esperado; el técnico puede editarla posteriormente.

Si la medición **está dentro del rango**, solo se emite `MedicionRegistrada_v1` (un único evento).

Este slice también formaliza la extensión de `HallazgoRegistrado_v1` con el campo `MedicionOrigenId: int?` (nullable para backward compat con orígenes `Manual`, `PreOperacional`, `Seguimiento`) y el campo paralelo `MedicionOrigenId: int?` en el record `Hallazgo` del state del aggregate. Ambas extensiones se especifican aquí y se ejecutan en las fases `red`/`green`.

**Motivación de negocio:** el monitoreo periódico cobra valor operativo solo cuando los valores fuera de rango disparan seguimiento automático antes de convertirse en falla. Sin este comando, el checklist de ítems numéricos de la rutina queda sin captura y el aggregate no puede validar completitud al firmar.

---

## 2. Comando

> **Decisión de diseño — método nuevo vs. relajar `RegistrarHallazgo`:** se opta por un **método nuevo** `Inspeccion.RegistrarMedicion` en el aggregate. Razones: (a) preserva la atomicidad multi-evento como decisión explícita del aggregate, no como efecto colateral del método de hallazgo; (b) no contamina el path manual con lógica de monitoreo; (c) el check `PRE-10` en `RegistrarHallazgo` puede levantarse limpiamente para `Origen=Monitoreo` sin tocar la lógica del handler técnico; (d) las invariantes I-M* son propias del contexto monitoreo y no encajan semánticamente en `RegistrarHallazgo`. El green decide el nombre exacto del método; la spec lo llama `RegistrarMedicion`.

```csharp
public sealed record RegistrarMedicion(
    Guid   InspeccionId,          // stream del aggregate
    Guid   HallazgoId,            // generado client-side (UUIDv7 preferido) — usado SOLO si FueraDeRango
    int    ItemId,                // PK ERP del ítem de la rutina (int — convención §15.4)
    decimal ValorMedido,          // valor capturado por el técnico
    string? Observacion,          // opcional — p. ej. "multímetro con pila baja"
    string EmitidoPor,            // tecnicoId opaco del JWT
    IReadOnlyCollection<string> Capabilities  // claims del host PWA — nunca toca HTTP
);
```

> `HallazgoId` viaja en el comando aunque la medición esté dentro del rango — el cliente lo genera siempre (UUIDv7) para que el handler pueda usarlo si emerge el hallazgo automático, sin necesidad de una segunda request. Si la medición está en rango, `HallazgoId` es ignorado por el aggregate.

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// DTO de entrada (mapea al record de comando en la capa API)
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/medicion
public sealed record RegistrarMedicionRequest(
    Guid    HallazgoId,           // UUIDv7 generado client-side (para hallazgo automático si aplica)
    decimal ValorMedido,
    string? Observacion);

// DTO de resultado
public sealed record RegistrarMedicionResult(
    Guid    InspeccionId,
    int     ItemId,
    decimal ValorMedido,
    bool    FueraDeRango,
    Guid?   HallazgoGeneradoId,  // poblado solo si FueraDeRango=true; null si dentro del rango
    DateTimeOffset RegistradaEn);
```

---

## 3. Evento(s) emitido(s)

Este slice emite uno o dos eventos al mismo stream, en un único `SaveChangesAsync`, en el siguiente orden causal:

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `MedicionRegistrada_v1` | Ver shape a continuación | **Siempre** (dentro y fuera de rango) |
| 2 | `HallazgoRegistrado_v1` (extendido) | Ver shape a continuación | **Solo si** `FueraDeRango=true` |

**Orden causal obligatorio:** `MedicionRegistrada_v1` primero, `HallazgoRegistrado_v1` segundo. El aggregate necesita haber registrado la medición (y marcado el item como medido) antes de emitir el hallazgo derivado. Invertir el orden rompe el rebuild desde stream.

### 3.1 `MedicionRegistrada_v1` — sin cambio de nombre ni versión

El evento ya está definido en §12.11.5 punto 5. El campo `RegistradaEn` se corrige aquí a `DateTimeOffset` (el modelo histórico usa `DateTime` — se usa `DateTimeOffset` para coherencia con la convención del módulo; ver nota abajo).

```csharp
public sealed record MedicionRegistrada_v1(
    Guid          InspeccionId,
    int           ItemId,             // PK ERP del ítem (int — §15.4)
    decimal       ValorMedido,
    string?       Observacion,        // opcional
    bool          FueraDeRango,       // calculado en el aggregate contra MedicionEsperada del snapshot
    string        EmitidoPor,         // tecnicoId opaco
    DateTimeOffset RegistradaEn);     // TimeProvider.GetUtcNow() en el handler
```

> **Nota sobre `DateTime` vs `DateTimeOffset` en §12.11.5 punto 5:** el modelo canónico usa `DateTime RegistradaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset` para timestamps. Este slice usa `DateTimeOffset` y lo propone como corrección al modelo §12.11.5. Si el usuario prefiere mantener `DateTime`, se documenta como pregunta abierta P-1.

### 3.2 `HallazgoRegistrado_v1` extendido con `MedicionOrigenId: int?`

**Extensión backward compatible:** se agrega el campo `MedicionOrigenId: int?` al record existente. Para todos los eventos históricos con `Origen ∈ {Manual, PreOperacional, Seguimiento}`, Marten/System.Text.Json deserializa el campo como `null` — sin migración de datos. El `Apply(HallazgoRegistrado_v1)` del aggregate tolera `null`.

```csharp
public sealed record HallazgoRegistrado_v1(
    Guid          InspeccionId,
    Guid          HallazgoId,
    OrigenHallazgo Origen,
    int?          NovedadPreopOrigenId,     // null cuando Origen=Monitoreo
    int?          MedicionOrigenId,         // NUEVO — int? (PK ERP del ítem). Obligatorio cuando Origen=Monitoreo; null en otros orígenes
    int           ParteEquipoId,
    int?          ActividadId,
    string?       ActividadDescripcion,
    string        NovedadTecnica,           // autogenerado: "Voltaje 10.2V fuera de rango esperado [12.3, 12.5]"
    AccionRequerida AccionRequerida,         // siempre RequiereSeguimiento cuando Origen=Monitoreo
    string?       AccionCorrectiva,         // null cuando Origen=Monitoreo (no aplica)
    int?          TipoFallaId,             // null cuando Origen=Monitoreo (I-H5)
    int?          CausaFallaId,            // null cuando Origen=Monitoreo (I-H5)
    string?       ObservacionCampo,        // propagado desde cmd.Observacion del ítem
    UbicacionGps? Ubicacion,               // null cuando Origen=Monitoreo (GPS no requerido en este slice)
    string        EmitidoPor,
    DateTimeOffset RegistradoEn);          // mismo timestamp que MedicionRegistrada_v1
```

> **Posición del campo `MedicionOrigenId`:** se inserta entre `NovedadPreopOrigenId` y `ParteEquipoId` para agrupar los campos de trazabilidad de origen. No rompe serialización de Marten (los records de C# con System.Text.Json usan nombres de propiedades, no posición).

### 3.3 Extensión paralela en el record `Hallazgo` del aggregate

El record `Hallazgo` (state) gana `MedicionOrigenId: int?` paralelo al campo del evento. El `Apply(HallazgoRegistrado_v1)` lo proyecta:

```csharp
// Campo a agregar al record Hallazgo en Hallazgo.cs:
int? MedicionOrigenId  // null para orígenes Manual/PreOperacional/Seguimiento; ItemId para Monitoreo
```

> **Cierre parcial de followup #20 (`ObservacionCampo`):** el campo `ObservacionCampo` existe en `HallazgoRegistrado_v1` y `HallazgoActualizado_v1` pero no en el record `Hallazgo` del aggregate (followup #20). Este slice **no cierra** ese followup directamente — la extensión de `Hallazgo` propuesta aquí es solo `MedicionOrigenId`. Cerrar #20 requiere añadir `ObservacionCampo: string?` al record, lo que es ortogonal a este slice. Sin embargo, en la fase `green` el implementador puede aprovechar la modificación de `Hallazgo.cs` para cerrar #20 al mismo tiempo. Si lo hace, se documenta en `green-notes.md`. Si no, #20 sigue abierto.

---

## 4. Precondiciones

Las precondiciones se clasifican por la capa donde viven. Los `Apply` son puros — nunca re-validan.

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability):** el usuario tiene capability `ejecutar-inspeccion` en los claims del host PWA (ADR-002 tentativo). Excepción: `403 Forbidden`.

### Capa handler (antes de invocar el método de decisión del aggregate)

- **PRE-2 (inspección existe):** el handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).

### Método de decisión del aggregate (`Inspeccion.RegistrarMedicion`)

- **PRE-3 (tipo Monitoreo — I-M1):** `Tipo == TipoInspeccion.Monitoreo`. Si la inspección es `Tecnica` → `InspeccionNoEsMonitoreoException` (`422 Unprocessable Entity`, código `I-M1`).
- **PRE-4 (estado EnEjecucion — I-M2):** `Estado ∈ {EnEjecucion}`. Si `Estado ∈ {Firmada, CierrePendienteOT, Cerrada, CerradaSinOT, Cancelada}` → `InspeccionNoEnEjecucionException` (`422 Unprocessable Entity`, código `I-M2`). Reutiliza la excepción existente del slice 1c.
- **PRE-5 (ítem existe en snapshot — I-M3):** `ItemId ∈ ItemsSnapshot.Select(i => i.ItemId)`. Si el ítem no existe en el snapshot → `ItemNoEncontradoEnSnapshotException` (`422 Unprocessable Entity`, código `I-M3`). Impide que ítems del catálogo que no pertenecen a esta inspección sean medidos.
- **PRE-6 (ítem no omitido — I-M4):** el ítem no debe haber sido omitido previamente (evento `ItemMonitoreoOmitido_v1` registrado en el aggregate). El aggregate mantiene un `HashSet<int> _itemsOmitidos`. Si `ItemId ∈ _itemsOmitidos` → `ItemOmitidoNoPuedeMedirseException` (`422 Unprocessable Entity`, código `I-M4`).
- **PRE-7 (ítem es numérico — I-M5):** el snapshot del ítem debe tener `Evaluacion` del tipo `MedicionEsperada` (no `EvaluacionCualitativaEsperada`). Se comprueba con pattern matching: `snapshot.Evaluacion is MedicionEsperada`. Si es cualitativo → `ItemNoEsNumericoException` (`422 Unprocessable Entity`, código `I-M5`).
- **PRE-8 (idempotencia de doble medición — I-M6):** **una sola medición por ítem por inspección.** El aggregate mantiene un `HashSet<int> _itemsMedidos`. Si `ItemId ∈ _itemsMedidos` → `ItemYaMedidoException` (`409 Conflict`, código `I-M6`). Justificación de la decisión: el modelo de monitoreo es un checklist; medir el mismo ítem dos veces en la misma inspección es ambiguo (¿corrección? ¿segunda lectura?). La corrección de una medición previa es responsabilidad de un comando futuro `ActualizarMedicion` — distinto de registrar dos mediciones. Esta invariante mantiene la semántica del ítem como "registrado o no registrado" dentro del contexto de una inspección.

> **Capa de validación:** PRE-1 en capa HTTP; PRE-2 en el handler; PRE-3 a PRE-8 en el método de decisión `Inspeccion.RegistrarMedicion`. Ningún `Apply` re-valida.

> **PRE-10 del slice 1c eliminada para `Origen=Monitoreo`:** el check `if (cmd.Origen != OrigenHallazgo.Manual && cmd.Origen != OrigenHallazgo.PreOperacional) throw OrigenNoSoportadoException` en `Inspeccion.RegistrarHallazgo` se mantiene sin cambio — `RegistrarMedicion` **no invoca** `RegistrarHallazgo`. El aggregate emite `HallazgoRegistrado_v1` directamente desde `RegistrarMedicion`. La PRE-10 de `RegistrarHallazgo` sigue siendo válida para los llamadores externos (slice 1c).

---

## 5. Invariantes tocadas

### Nuevas invariantes del contexto Monitoreo — convención de código `I-M*`

**Justificación de la convención:** el modelo §15.3 cubre invariantes `I-H*` (hallazgos), `I-I*` (inicio), `I-F*`/`V-F*` (firma). No existe serie para invariantes de ítem de monitoreo. Se propone `I-M*` para invariantes que aplican exclusivamente al contexto `Tipo=Monitoreo`. Se propone registrar estas invariantes en `01-modelo-dominio.md §15.3` en el mismo PR de este slice.

- **I-M1 (tipo Monitoreo):** `RegistrarMedicion` solo es válido sobre inspecciones con `Tipo=Monitoreo`. Cubierta por PRE-3. Propuesta de texto para §15.3: _"I-M1: El comando `RegistrarMedicion` requiere `Tipo=Monitoreo`. Una inspección técnica no acepta mediciones de rutina."_
- **I-M2 (estado EnEjecucion):** equivalente a I2 (§15.3) pero para el flujo monitoreo — mismo estado válido. Cubierta por PRE-4. Reutiliza `InspeccionNoEnEjecucionException`. No se propone nueva invariante separada (es la misma I2 aplicada al flujo monitoreo).
- **I-M3 (ítem pertenece al snapshot):** el `ItemId` debe existir en `ItemsSnapshot`. Cubierta por PRE-5. Propuesta: _"I-M3: ItemId debe existir en el snapshot de ítems capturado al iniciar la inspección de monitoreo."_
- **I-M4 (ítem no omitido):** no se puede medir un ítem previamente omitido. Cubierta por PRE-6. Propuesta: _"I-M4: Un ítem omitido (`ItemMonitoreoOmitido_v1`) no puede recibir medición posterior en la misma inspección."_
- **I-M5 (ítem numérico):** `RegistrarMedicion` solo aplica a ítems con `EvaluacionEsperada = MedicionEsperada`. Cubierta por PRE-7. Propuesta: _"I-M5: Solo ítems con evaluación numérica (`MedicionEsperada`) aceptan `RegistrarMedicion`. Ítems cualitativos usan `RegistrarEvaluacionCualitativa`."_
- **I-M6 (unicidad de medición por ítem):** un ítem solo puede medirse una vez por inspección. Cubierta por PRE-8. Propuesta: _"I-M6: Cada ítem del snapshot admite exactamente una medición por inspección. La corrección usa el comando futuro `ActualizarMedicion`."_

### Invariantes heredadas de `HallazgoRegistrado_v1` cuando se emite con `Origen=Monitoreo`

- **I-H1:** `ParteEquipoId` siempre presente — el aggregate lo hereda del snapshot (`snapshot.Parte` se convierte en el nombre de la parte; el `ParteEquipoId` se necesita. Ver **P-1 en §12 — pregunta abierta** sobre la ausencia de `ParteEquipoId` en `ItemRutinaMonitoreoSnapshot`).
- **I-H4 / I-H5:** `AccionRequerida=RequiereSeguimiento` → `TipoFallaId` y `CausaFallaId` son `null` (I-H5 aplica). Coherente con §12.11.5 punto 6.
- **I-H6:** múltiples hallazgos derivados de diferentes ítems del mismo equipo están permitidos (cada ítem fuera de rango genera su propio hallazgo).
- **Invariante derivada de §12.11.5 punto 8:** `Origen=Monitoreo → MedicionOrigenId obligatorio e inmutable`. Cubierta por la construcción del evento en `RegistrarMedicion`: el aggregate siempre pone `MedicionOrigenId=ItemId` cuando emite con `Origen=Monitoreo`.
- **Invariante derivada de §12.11.5 punto 8:** `Origen=Monitoreo → AccionRequerida=RequiereSeguimiento`. Hardcodeado en el aggregate — el técnico no puede elegir otra `AccionRequerida` para hallazgos de monitoreo automáticos.
- **Invariante derivada de §12.11.5 punto 8:** `Origen=Monitoreo → TipoInspeccion del stream = Monitoreo`. Cubierta por PRE-3.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — medición dentro del rango (un evento)

**Given**
- Stream con `InspeccionIniciada_v1` donde `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `ItemsSnapshot` contiene `ItemId=1` con `Evaluacion=MedicionEsperada(magnitud="voltaje", unidad="V", valorMin=12.3, valorMax=12.5)`, `Parte="Batería"`, `Actividad="Medir voltaje"`.
- `_itemsMedidos = {}` (vacío), `_itemsOmitidos = {}` (vacío).
- Claims con `EmitidoPor="ana.gomez"`, capability `ejecutar-inspeccion`.

**When**
- Comando `RegistrarMedicion(InspeccionId=X, HallazgoId=G_ignorado, ItemId=1, ValorMedido=12.4, Observacion=null, EmitidoPor="ana.gomez")`.

**Then**
- Se emite exactamente **un** evento: `MedicionRegistrada_v1(InspeccionId=X, ItemId=1, ValorMedido=12.4, Observacion=null, FueraDeRango=false, EmitidoPor="ana.gomez", RegistradaEn=now)`.
- **No** se emite `HallazgoRegistrado_v1`.
- `_itemsMedidos` contiene `1`.
- `Hallazgos.Count` no cambia.
- Handler retorna `RegistrarMedicionResult(InspeccionId=X, ItemId=1, ValorMedido=12.4, FueraDeRango=false, HallazgoGeneradoId=null, RegistradaEn=now)`.
- Capa API devuelve `200 OK`.

### 6.2 Happy path — medición fuera de rango por debajo (dos eventos atómicos)

**Given**
- Mismo aggregate que 6.1.
- `TimeProvider` retorna `2026-05-08T10:00:00Z`.

**When**
- Comando `RegistrarMedicion(InspeccionId=X, HallazgoId=G1, ItemId=1, ValorMedido=10.2, Observacion="multímetro con pila baja", EmitidoPor="ana.gomez")`.

**Then**
- Se emiten **dos** eventos en orden causal en un único `SaveChangesAsync`:
  1. `MedicionRegistrada_v1(ItemId=1, ValorMedido=10.2, FueraDeRango=true, Observacion="multímetro con pila baja", RegistradaEn=2026-05-08T10:00:00Z)`.
  2. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Monitoreo, MedicionOrigenId=1, ParteEquipoId=<ver P-1>, NovedadTecnica="Voltaje 10.2V fuera de rango esperado [12.3, 12.5]", AccionRequerida=RequiereSeguimiento, NovedadPreopOrigenId=null, TipoFallaId=null, CausaFallaId=null, AccionCorrectiva=null, ObservacionCampo="multímetro con pila baja", Ubicacion=null, EmitidoPor="ana.gomez", RegistradoEn=2026-05-08T10:00:00Z)`.
- `_itemsMedidos` contiene `1`.
- `Hallazgos.Count = 1`, hallazgo activo con `Origen=Monitoreo`, `MedicionOrigenId=1`, `AccionRequerida=RequiereSeguimiento`, `Eliminado=false`.
- Handler retorna `RegistrarMedicionResult(FueraDeRango=true, HallazgoGeneradoId=G1)`.
- Capa API devuelve `200 OK`.

### 6.3 Happy path — medición fuera de rango por encima (dos eventos atómicos)

**Given** — mismo aggregate que 6.1.

**When**
- Comando con `ValorMedido=15.0` (por encima de `ValorMax=12.5`).

**Then**
- `MedicionRegistrada_v1` con `FueraDeRango=true`, `ValorMedido=15.0`.
- `HallazgoRegistrado_v1` con `NovedadTecnica="Voltaje 15.0V fuera de rango esperado [12.3, 12.5]"`, `AccionRequerida=RequiereSeguimiento`.
- Comportamiento idéntico al escenario 6.2 (la dirección del desvío no cambia el modelo; solo el texto).

### 6.4 Happy path — medición en el borde exacto del rango (dentro)

**Given** — mismo aggregate que 6.1.

**When**
- Comando con `ValorMedido=12.3` (igual al `ValorMin`).

**Then**
- `MedicionRegistrada_v1` con `FueraDeRango=false` (borde inclusivo).
- No se emite hallazgo. `Hallazgos.Count` no cambia.

> **Decisión de borde:** rango es cerrado `[ValorMin, ValorMax]`. `ValorMedido >= ValorMin && ValorMedido <= ValorMax` → `FueraDeRango=false`. Si el usuario prefiere rango abierto en uno de los extremos, se registra en §12 P-2.

### 6.5 Violación PRE-3 / I-M1 — inspección técnica rechaza medición

**Given**
- Stream con `InspeccionIniciada_v1` donde `Tipo=Tecnica`, `Estado=EnEjecucion`.

**When**
- Comando `RegistrarMedicion(InspeccionId=X, ItemId=1, ...)`.

**Then**
- Aggregate lanza `InspeccionNoEsMonitoreoException("La inspección X es de tipo Tecnica. RegistrarMedicion solo aplica a inspecciones de Tipo=Monitoreo.")`.
- Sin evento. Sin cambio en `Hallazgos`.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M1" }`.

### 6.6 Violación PRE-4 / I-M2 — inspección Firmada rechaza medición

**Given**
- Stream con `[InspeccionIniciada_v1(Tipo=Monitoreo), InspeccionFirmada_v1]` → `Estado=Firmada`.

**When**
- Comando `RegistrarMedicion(InspeccionId=X, ItemId=1, ...)`.

**Then**
- Aggregate lanza `InspeccionNoEnEjecucionException("La inspección está en estado 'Firmada'. Solo se pueden registrar mediciones en estado EnEjecucion.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M2" }`.

### 6.7 Violación PRE-5 / I-M3 — ítem inexistente en snapshot

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemsSnapshot` contiene solo `ItemId=1`.

**When**
- Comando `RegistrarMedicion(ItemId=999, ...)`.

**Then**
- Aggregate lanza `ItemNoEncontradoEnSnapshotException("El ítem 999 no forma parte del snapshot de esta inspección. Solo pueden medirse los ítems del snapshot: [1].")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M3" }`.

### 6.8 Violación PRE-6 / I-M4 — ítem previamente omitido

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `_itemsOmitidos = {1}` (ítem omitido por `ItemMonitoreoOmitido_v1` previo).

**When**
- Comando `RegistrarMedicion(ItemId=1, ...)`.

**Then**
- Aggregate lanza `ItemOmitidoNoPuedeMedirseException("El ítem 1 fue omitido en esta inspección. Un ítem omitido no puede recibir medición posterior.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M4" }`.

### 6.9 Violación PRE-7 / I-M5 — ítem cualitativo rechaza medición numérica

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemsSnapshot` contiene `ItemId=2` con `Evaluacion=EvaluacionCualitativaEsperada()`.

**When**
- Comando `RegistrarMedicion(ItemId=2, ValorMedido=1.5, ...)`.

**Then**
- Aggregate lanza `ItemNoEsNumericoException("El ítem 2 ('Conectores batería') tiene evaluación cualitativa. Usa el comando RegistrarEvaluacionCualitativa para este ítem.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M5" }`.

### 6.10 Violación PRE-8 / I-M6 — doble medición del mismo ítem rechazada (409)

**Given**
- Aggregate con `_itemsMedidos = {1}` (ítem ya medido por `MedicionRegistrada_v1` previo).

**When**
- Segundo comando `RegistrarMedicion(ItemId=1, ValorMedido=12.1, ...)`.

**Then**
- Aggregate lanza `ItemYaMedidoException("El ítem 1 ya fue medido en esta inspección. Para corregir una medición, usa el comando ActualizarMedicion (disponible en slice futuro).")`.
- Sin evento. `_itemsMedidos` no cambia.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I-M6" }`.

### 6.11 PRE-2 — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando `RegistrarMedicion(InspeccionId=Z, ItemId=1, ...)`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Capa API devuelve `404 Not Found`.

### 6.12 Idempotencia — replay con mismo `clientCommandId`

**Given**
- Comando con `MessageId=X` ya ejecutado exitosamente (ítem 1 dentro del rango). Wolverine envelope storage tiene respuesta original.

**When**
- Cliente reenvía mismo `MessageId=X` tras timeout de red.

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- `_itemsMedidos` permanece con `{1}` (un solo registro).
- Un solo `MedicionRegistrada_v1` en el stream.
- Capa API devuelve `200 OK` con body original.

### 6.13 Múltiples ítems — cada ítem fuera de rango genera su propio hallazgo

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemsSnapshot` contiene `ItemId=1` (numérico, rango [12.3, 12.5]) e `ItemId=3` (numérico, rango [0.9, 1.1]).
- `_itemsMedidos = {1}` con `MedicionRegistrada_v1(ItemId=1, FueraDeRango=true)` y `HallazgoRegistrado_v1(HallazgoId=G1, MedicionOrigenId=1)` ya en el stream.

**When**
- Comando `RegistrarMedicion(ItemId=3, ValorMedido=0.5, HallazgoId=G2, ...)`.

**Then**
- `MedicionRegistrada_v1(ItemId=3, FueraDeRango=true)` + `HallazgoRegistrado_v1(HallazgoId=G2, MedicionOrigenId=3)`.
- `_itemsMedidos = {1, 3}`.
- `Hallazgos.Count = 2` (G1 y G2 coexisten — I-H6 permitido).

### 6.14 Atomicidad — dos eventos o ninguno (rollback si falla persist)

**Given**
- Aggregate con `ItemId=1` fuera de rango.

**When**
- `SaveChangesAsync()` falla en mitad de la transacción (simulado en test de integración con Testcontainers).

**Then**
- Ni `MedicionRegistrada_v1` ni `HallazgoRegistrado_v1` se persisten en `mt_events`.
- El stream permanece en el estado previo.
- El handler propaga la excepción al middleware.

> Nota: la atomicidad la garantiza Marten con un único `SaveChangesAsync`. Este escenario se verifica como test de integración (Testcontainers Postgres), no como test de dominio puro.

### 6.15 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos).
- Lista de eventos en orden causal para reproducir el happy path 6.2:
  1. `InspeccionIniciada_v1(Tipo=Monitoreo, ItemsSnapshot=[{ItemId=1, MedicionEsperada(12.3, 12.5)}, {ItemId=2, EvaluacionCualitativaEsperada()}])`.
  2. `MedicionRegistrada_v1(ItemId=1, ValorMedido=10.2, FueraDeRango=true, EmitidoPor="ana.gomez")`.
  3. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Monitoreo, MedicionOrigenId=1, AccionRequerida=RequiereSeguimiento, ...)`.

**When**
- Se reproyectan los tres eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- Estado resultante:
  - `Tipo = TipoInspeccion.Monitoreo`.
  - `Estado = EstadoInspeccion.EnEjecucion`.
  - `ItemsSnapshot.Count = 2`.
  - `_itemsMedidos = {1}`.
  - `_itemsOmitidos = {}`.
  - `Hallazgos.Count = 1`.
  - `Hallazgos[0].HallazgoId = G1`, `Origen=Monitoreo`, `MedicionOrigenId=1`, `AccionRequerida=RequiereSeguimiento`, `Eliminado=false`.
  - `Contribuyentes = {"ana.gomez"}` (desde `InspeccionIniciada_v1` + `HallazgoRegistrado_v1`; `MedicionRegistrada_v1` también actualiza contribuyentes).
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al obtenido tras ejecutar el comando en 6.2.
- El test garantiza que invertir el orden de los eventos 2 y 3 rompería la lógica de `_itemsMedidos` (el ítem aparecería como medido antes de que el `Apply(HallazgoRegistrado_v1)` tenga el contexto de `MedicionOrigenId`).

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico confirma el valor del ítem. Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler (escenario 6.12).

**Idempotencia natural por I-M6:**

Si el cliente reenvía con un `clientCommandId` distinto (nuevo retry) sobre un ítem ya medido, el aggregate lanza `ItemYaMedidoException` (`409 Conflict`). El cliente debe distinguir: `409` = el ítem ya fue medido en esta sesión, no es error — abrir el flujo `ActualizarMedicion` si el técnico quiere corregir.

**Doble medición vs. corrección:**

El `409` por I-M6 es intencional y no es retryable. El cliente no debe reintentar automáticamente en `409`.

**Sin POST a Sinco:**

Este comando no cruza al ERP en línea. ADR-006 (outbox para integraciones ERP) no aplica directamente — no hay llamada HTTP saliente en el handler. El outbox transaccional de Wolverine garantiza atomicidad evento(s) + proyección + envelope en un único `SaveChangesAsync`.

**`Idempotency-Key` para Sinco:**

No aplica en este slice (sin llamadas al ERP).

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` — consumirá `MedicionRegistrada_v1`

La proyección `DetalleInspeccionView` (§15.12.1) no existe aún (roadmap 3.45). Cuando se implemente, debe consumir `MedicionRegistrada_v1` para mostrar el valor medido, `FueraDeRango` y la observación por ítem. Este slice no la crea; documenta qué evento consume.

Campos proyectados por `MedicionRegistrada_v1`:
- `ItemId`, `ValorMedido`, `FueraDeRango`, `Observacion`, `EmitidoPor`, `RegistradaEn`.

### 8.2 `BandejaTecnicoView` — no impactada

`BandejaTecnicoView` (§15.12.3) muestra estado de la inspección, no ítems individuales. Sin cambio.

### 8.3 `InspeccionAbiertaPorEquipoView` — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio.

### 8.4 `AuditoriaInspeccionesView` — no impactada en este slice

Consumirá `MedicionRegistrada_v1` en el slice futuro que la implemente (roadmap 3.55). No se toca aquí.

### 8.5 Proyección `ItemsMonitoreoView` (futura)

Una proyección dedicada al checklist de ítems de la inspección de monitoreo (estado por ítem: pendiente / medido / omitido / evaluado) es necesaria para la UX del técnico. No existe aún. Este slice la especifica como **futura** sin bloquear el slice. Cuando se implemente, consumirá `MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1` e `ItemMonitoreoOmitido_v1`.

---

## 9. Impacto en endpoints HTTP

**Endpoint:** `POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/medicion`

> Ruta siguiendo el patrón `roadmap.md §3.36c`. `inspeccionId` e `itemId` viajan en el path. El body solo contiene los campos propios de la medición.

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO:**

```json
{
  "hallazgoId": "0193a4f8-...",
  "valorMedido": 10.2,
  "observacion": "multímetro con pila baja"
}
```

**Response 200 OK (happy path — dentro del rango):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "itemId": 1,
  "valorMedido": 12.4,
  "fueraDeRango": false,
  "hallazgoGeneradoId": null,
  "registradaEn": "2026-05-08T10:00:00Z"
}
```

**Response 200 OK (happy path — fuera del rango):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "itemId": 1,
  "valorMedido": 10.2,
  "fueraDeRango": true,
  "hallazgoGeneradoId": "0193a4f8-...",
  "registradaEn": "2026-05-08T10:00:00Z"
}
```

**Códigos de error:**

| Código HTTP | `codigoError` | Escenario |
|---|---|---|
| `403 Forbidden` | `"PRE-1"` | Capability ausente |
| `404 Not Found` | `"PRE-2"` | InspeccionId no existe |
| `409 Conflict` | `"I-M6"` | Ítem ya medido (PRE-8) |
| `422 Unprocessable Entity` | `"I-M1"` | Inspección es Tecnica (PRE-3) |
| `422 Unprocessable Entity` | `"I-M2"` | Inspección no en EnEjecucion (PRE-4) |
| `422 Unprocessable Entity` | `"I-M3"` | ItemId no en snapshot (PRE-5) |
| `422 Unprocessable Entity` | `"I-M4"` | Ítem omitido (PRE-6) |
| `422 Unprocessable Entity` | `"I-M5"` | Ítem cualitativo (PRE-7) |

**Rol/permiso requerido:** capability `ejecutar-inspeccion`. Heredado del host PWA.

**Nota sobre GPS:** el comando `RegistrarMedicion` **no incluye** `UbicacionGps`. El precedente del slice 1c (`RegistrarHallazgo`) hace el GPS opcional (`UbicacionGps?`). Para mediciones de monitoreo, la ubicación ya fue capturada al iniciar (`InspeccionIniciada_v1.Ubicacion`) y se considera suficiente para el MVP. El hallazgo derivado lleva `Ubicacion=null`. Si emerge la necesidad de GPS por ítem, es cambio aditivo sin romper el modelo.

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `MedicionRegistrada_v1` y el `HallazgoRegistrado_v1` derivado no generan push hacia el frontend según el catálogo vigente de ADR-005 (`§14` del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). El registro de mediciones es una operación local del técnico en su dispositivo — no hay otras partes que necesiten notificación en tiempo real.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `RegistrarMedicion` no consume ni publica hacia el ERP. Trabaja exclusivamente con el aggregate cargado desde el stream de Marten (`ItemsSnapshot` fue persistido en `InspeccionIniciada_v1`). No hay llamadas a M-3b, M-16 ni a ningún otro endpoint ERP en este handler.

> Los datos del ítem (magnitud, unidad, rango) se leen del `ItemsSnapshot` dentro del evento ya persistido, no del catálogo local `RutinaMonitoreoLocal`. Esto garantiza que la evaluación se hace contra el rango vigente al momento del inicio de la inspección, no contra el rango actual del catálogo (que puede haber cambiado).

---

## 12. Preguntas abiertas

### P-1 — `ParteEquipoId` en `ItemRutinaMonitoreoSnapshot` — campo ausente del shape del slice 1h

**Contexto:** el `HallazgoRegistrado_v1` con `Origen=Monitoreo` requiere `ParteEquipoId: int` (I-H1 — siempre obligatorio, no nullable). El shape de `ItemRutinaMonitoreoSnapshot` definido en el slice 1h es:

```csharp
public sealed record ItemRutinaMonitoreoSnapshot(
    int ItemId,
    string Parte,   // nombre de la parte, ej. "Batería" — no es el PK del ERP
    string Actividad,
    EvaluacionEsperada Evaluacion);
```

El campo `Parte: string` es el nombre legible, no el PK entero `ParteEquipoId: int` requerido por `HallazgoRegistrado_v1`. El catálogo de rutinas de monitoreo (M-16) no expone explícitamente `ParteEquipoId` en el shape documentado en §12.11.5 punto 2.

**Opciones:**

**A) Agregar `ParteEquipoId: int` al `ItemRutinaMonitoreoSnapshot`** — el catálogo M-16 debe exponer el `ParteEquipoId` (PK del ERP de partes) por ítem. El adapter M-16 lo mapea al snapshot. El hallazgo derivado lo hereda directamente. Esta es la opción recomendada porque mantiene I-H1 sin workaround.

**B) Usar `ItemId` como sustituto de `ParteEquipoId` en hallazgos de monitoreo** — no recomendado. `ItemId` es el PK del ítem de rutina-monitoreo (ERP), no el PK de la parte del equipo. Rompe la semántica de I-H1 y confunde las proyecciones que agrupan hallazgos por parte.

**C) Hacer `ParteEquipoId` nullable solo para `Origen=Monitoreo`** — no recomendado. Relaja I-H1 de forma incondicional en el modelo, impacta todos los hallazgos.

**Propuesta del modelador:** opción **A**. Agregar `ParteEquipoId: int` al `ItemRutinaMonitoreoSnapshot` del slice 1h como extensión backward compatible (campo nuevo nullable → `int?`, o proponer que M-16 lo exponga como campo requerido). El PR del slice 1i extiende `ItemRutinaMonitoreoSnapshot` con `ParteEquipoId: int?` (nullable para no romper snapshots existentes del slice 1h donde el campo aún no se capturó). El `Apply(HallazgoRegistrado_v1)` acepta `null` solo si viene de monitoreo con snapshot sin `ParteEquipoId` (workaround temporal). Confirmar con David si M-16 expone `ParteEquipoId` por ítem — registrar en followup #22.

**Estado:** **RESUELTA 2026-05-08 — opción A aprobada por Jaime.** Se documenta como invariante extendida del snapshot y se propone agregar a §15.3 de `01-modelo-dominio.md`. Followup #22 a David: confirmar que M-16 expone `ParteEquipoId` por ítem en el shape del catálogo de rutinas de monitoreo. La fase `green` extiende `ItemRutinaMonitoreoSnapshot` con `ParteEquipoId: int?` (nullable backward-compat). El `Apply(HallazgoRegistrado_v1)` propaga `MedicionOrigenId` y mantiene `ParteEquipoId` desde el snapshot.

---

### P-2 — Borde del rango: ¿cerrado [min, max] o abierto (min, max)?

**Contexto:** `MedicionEsperada(ValorMin=12.3, ValorMax=12.5)`. Si `ValorMedido=12.3` exactamente, ¿es `FueraDeRango=false`?

**Propuesta del modelador:** rango **cerrado** `[ValorMin, ValorMax]` — `FueraDeRango = ValorMedido < ValorMin || ValorMedido > ValorMax`. Justificación: en instrumentación de campo, un valor en el borde del rango nominal es aceptable; marcar el borde como falla genera falsos positivos con instrumentos de resolución limitada.

**Estado:** **RESUELTA 2026-05-08 — rango cerrado [min, max] aprobado por Jaime.** Se documenta como decisión en §12.11.5 del modelo. Escenario 6.4 cubre el borde inclusivo (`ValorMedido = ValorMin → FueraDeRango=false`).

---

### P-3 — `DateTimeOffset` vs `DateTime` en `MedicionRegistrada_v1.RegistradaEn`

**Contexto:** el shape en §12.11.5 punto 5 del modelo usa `DateTime RegistradaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset` para timestamps. Los eventos existentes (`HallazgoRegistrado_v1`, `InspeccionIniciada_v1`, etc.) usan `DateTimeOffset`.

**Propuesta del modelador:** usar `DateTimeOffset RegistradaEn` — coherente con la convención del módulo. Proponer corrección al modelo §12.11.5 punto 5 en el PR de este slice.

**Estado:** **RESUELTA 2026-05-08 — `DateTimeOffset` aprobado por Jaime.** Coherente con CLAUDE.md y resto de eventos del módulo. Corrección al modelo §12.11.5 punto 5 se aplica en el PR de este slice.

---

### Followups relacionados

- **#20 (`ObservacionCampo`):** la extensión de `Hallazgo.cs` en este slice es la oportunidad natural de cerrar este followup añadiendo `ObservacionCampo: string?` al record `Hallazgo`. Se deja a criterio del implementador `green`. Si lo cierra, documentarlo en `green-notes.md`.
- **#25 y #26 (del slice 1h):** no aplican directamente al slice 1i. #25 es sobre la aserción `fila.Tipo` en tests de integración del slice 1h; #26 es sobre `TecnicoIniciador` en `Inspeccion.IniciarMonitoreo`. Ninguno de los dos impacta el diseño del comando `RegistrarMedicion`.

---

## 13. Checklist pre-firma — TODO firmado 2026-05-08 por Jaime

- [x] **P-1 resuelta** — opción A: agregar `ParteEquipoId: int?` a `ItemRutinaMonitoreoSnapshot` (backward-compat). Followup #22 a David sobre M-16.
- [x] P-2 resuelta — rango cerrado `[min, max]`.
- [x] P-3 resuelta — `DateTimeOffset` en `MedicionRegistrada_v1.RegistradaEn`.
- [x] Todas las precondiciones (PRE-1..PRE-8) tienen un escenario Given/When/Then en §6 (6.5→PRE-3/I-M1, 6.6→PRE-4/I-M2, 6.7→PRE-5/I-M3, 6.8→PRE-6/I-M4, 6.9→PRE-7/I-M5, 6.10→PRE-8/I-M6, 6.11→PRE-2).
- [x] Todas las invariantes tocadas (I-M1..I-M6, I-H1, I-H4/I-H5, I-H6, invariantes derivadas §12.11.5 punto 8) tienen escenario de cobertura.
- [x] Happy paths presentes: 6.1 (dentro del rango, 1 evento), 6.2 (fuera de rango por debajo, 2 eventos), 6.3 (fuera de rango por encima, 2 eventos), 6.4 (borde).
- [x] Escenario de rebuild desde stream presente (6.15) — incluye verificación de orden causal.
- [x] Idempotencia decidida (§7): envelope dedup ADR-008 + I-M6 natural (409 Conflict).
- [x] §10 SignalR resuelto explícitamente ("no aplica").
- [x] §11 adapters Sinco resuelto explícitamente ("no aplica").
- [x] Extensión de `HallazgoRegistrado_v1` con `MedicionOrigenId: int?` documentada (§3.2) — backward compatible.
- [x] Extensión de `Hallazgo` record con `MedicionOrigenId: int?` documentada (§3.3).
- [x] Extensión de `ItemRutinaMonitoreoSnapshot` con `ParteEquipoId: int?` documentada (P-1 §12).
- [x] Nuevas invariantes I-M1..I-M6 propuestas para `01-modelo-dominio.md §15.3`.
- [x] Nuevas excepciones propuestas: `InspeccionNoEsMonitoreoException`, `ItemNoEncontradoEnSnapshotException`, `ItemOmitidoNoPuedeMedirseException`, `ItemNoEsNumericoException`, `ItemYaMedidoException`.
- [x] Nuevo estado interno del aggregate documentado: `HashSet<int> _itemsMedidos`, `HashSet<int> _itemsOmitidos` (el segundo ya debe existir tras slice futuro `OmitirItemMonitoreo` — si no existe aún, el `green` lo crea vacío para este slice).
- [x] **Spec firmada por Jaime — pasa a `red`.**

---

## Notas de cierre para revisión humana

**Lo que este slice añade respecto al 1h:**

- Extensión del record `HallazgoRegistrado_v1`: campo `MedicionOrigenId: int?` (posición 2, entre `NovedadPreopOrigenId` y `ParteEquipoId`). Backward compatible — Marten deserializa `null` para eventos históricos.
- Extensión del record `Hallazgo`: campo `MedicionOrigenId: int?`.
- Nuevo evento `MedicionRegistrada_v1` (`DateTimeOffset` en `RegistradaEn`).
- Nuevo método de decisión `Inspeccion.RegistrarMedicion` (atomic, emite 1 o 2 eventos).
- Nuevo estado interno del aggregate: `HashSet<int> _itemsMedidos` (proyectado desde `Apply(MedicionRegistrada_v1)`).
- `AplicarEvento` extendido con `case MedicionRegistrada_v1`.
- `Apply(MedicionRegistrada_v1)` puro: actualiza `_itemsMedidos`.
- `Apply(HallazgoRegistrado_v1)` extendido: proyecta `MedicionOrigenId` en el record `Hallazgo`.
- Nuevas excepciones de dominio (5): ver checklist §13.
- Handler `RegistrarMedicionHandler` en `Inspecciones.Application`.
- Endpoint `POST /api/v1/inspecciones/{id}/items/{itemId}/medicion` en `Inspecciones.Api`.

**Lo que NO hace este slice:**

- `RegistrarEvaluacionCualitativa` (roadmap 3.16g) — fuera de alcance.
- `OmitirItemMonitoreo` (roadmap 3.16h) — fuera de alcance. El `HashSet<int> _itemsOmitidos` que este slice lee puede no existir aún en el aggregate; el `green` lo inicializa vacío.
- `ActualizarMedicion` (roadmap §15.4 `MedicionActualizada_v1`) — comando de corrección post-registro; fuera de alcance.
- Validaciones pre-firma para completitud del checklist (¿todos los ítems deben tener registro antes de firmar?) — se modelará en el slice de `FirmarInspeccion` si aplica a monitoreo.
- Proyecciones async `DetalleInspeccionView` e `ItemsMonitoreoView`.
