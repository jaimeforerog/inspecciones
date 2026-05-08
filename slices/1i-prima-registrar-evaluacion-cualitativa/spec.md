# Slice 1i' — RegistrarEvaluacionCualitativa

**Autor:** domain-modeler
**Fecha:** 2026-05-08
**Estado:** firmado (2026-05-08 por Jaime — `EmitidoPor` aprobado en `EvaluacionCualitativaRegistrada_v1`; corrección al modelo §12.11.5 punto 5 se aplica en este PR)
**Agregado afectado:** `Inspeccion` (aggregate unificado — discriminador `TipoInspeccion.Monitoreo`).
**Decisiones previas relevantes:**
- `slices/1i-registrar-medicion/spec.md` — predecesor inmediato. Establece el patrón: método nuevo en el aggregate, invariantes I-M1..I-M6, `_itemsMedidos`, `_itemsOmitidos`, `ParteEquipoIdAusenteEnSnapshotException`.
- `01-modelo-dominio.md §12.11.5 puntos 3, 5, 6, 8` — shape canónico de `EvaluacionCualitativaRegistrada_v1`, enum `CalificacionCualitativa`, tabla de trigger de hallazgo, invariantes derivadas de `Origen=Monitoreo`.
- `01-modelo-dominio.md §15.3` — invariantes I-H1..I-H12, I-M1..I-M6. Este slice propone I-M5b e I-M7.
- `01-modelo-dominio.md §15.4` — catálogo MVP de eventos; `EvaluacionCualitativaRegistrada_v1` listado.
- `roadmap.md §3.B' paso 3.16g` — comando `RegistrarEvaluacionCualitativa`.
- ADR-002 (`§9.11`) — claims recibidos del host PWA como parámetro, nunca del HTTP context.
- ADR-006 (`§16`) — outbox transaccional; atomicidad multi-evento en único `SaveChangesAsync`.
- ADR-008 (`§9.16`) — `clientCommandId` UUIDv7 como `MessageId` Wolverine; idempotencia end-to-end.
- CLAUDE.md — `Apply` puro, rebuild test obligatorio, IDs `int` ERP + `Guid` internos.
- Followup #22 — `ParteEquipoId` en snapshot (M-16). El guard `ParteEquipoIdAusenteEnSnapshotException` ya existe (slice 1i) y se reusa aquí.
- Followup #20 — `ObservacionCampo` ausente del record `Hallazgo`. Este slice es oportunidad de cerrarlo.

---

## 1. Intención

El técnico de campo necesita registrar la calificación cualitativa de un ítem de monitoreo durante la ejecución de la inspección. El ítem tiene `EvaluacionEsperada = EvaluacionCualitativaEsperada()` (no un rango numérico) y el técnico elige entre tres valores: `Bueno`, `Regular` o `Malo`.

Si la calificación es `Malo`, el sistema emite atómicamente — en un único `SaveChangesAsync` — el evento de evaluación **y** un `HallazgoRegistrado_v1` con `Origen=Monitoreo`, `AccionRequerida=RequiereSeguimiento`, y trazabilidad bidireccional hacia el ítem. La `NovedadTecnica` del hallazgo automático describe la parte y la calificación; el técnico puede editarla posteriormente mediante `ActualizarHallazgo`.

Si la calificación es `Bueno` o `Regular`, solo se emite `EvaluacionCualitativaRegistrada_v1` (un único evento). `Regular` no dispara hallazgo automático (§12.11.5 punto 6, decisión 2 confirmada: solo `Malo` dispara).

Este slice también formaliza la extensión de `HallazgoRegistrado_v1` con el campo `EvaluacionOrigenId: int?` (campo semánticamente correcto, paralelo a `MedicionOrigenId: int?` de slice 1i — ver P-3), la extensión del record `Hallazgo` con `EvaluacionOrigenId: int?` (P-4), y la adición del set `_itemsEvaluados: HashSet<int>` al state del aggregate (P-5).

**Motivación de negocio:** los ítems cualitativos de la rutina de monitoreo (estado de conectores, cableado, mangueras) no tienen rango numérico; la calificación visual es la evidencia primaria. Sin este comando, el checklist de ítems cualitativos queda sin captura y el aggregate no puede validar completitud al firmar.

---

## 2. Comando

> **Decisión de diseño — método nuevo `Inspeccion.RegistrarEvaluacionCualitativa`:** se opta por un método separado, paralelo a `RegistrarMedicion`, por las mismas razones documentadas en slice 1i §2: (a) la atomicidad multi-evento es una decisión explícita del aggregate, no un efecto colateral; (b) no contamina el path numérico con lógica cualitativa; (c) las invariantes I-M5b e I-M7 son propias del contexto cualitativo y no encajan en `RegistrarMedicion`. El aggregate mantiene dos sets (`_itemsMedidos` / `_itemsEvaluados`) — siempre separados (ver P-5).

```csharp
public sealed record RegistrarEvaluacionCualitativa(
    Guid   InspeccionId,          // stream del aggregate
    Guid   HallazgoId,            // generado client-side (UUIDv7 preferido) — usado SOLO si Calificacion=Malo
    int    ItemId,                // PK ERP del ítem de la rutina (int — convención §15.4)
    CalificacionCualitativa Calificacion,  // Bueno | Regular | Malo
    string? Observacion,          // opcional — p. ej. "conector flojo, ajustado en campo"
    string EmitidoPor,            // tecnicoId opaco del JWT
    IReadOnlyCollection<string> Capabilities  // claims del host PWA — nunca toca HTTP
);
```

> `HallazgoId` viaja en el comando siempre (el cliente lo genera aunque la calificación sea `Bueno` o `Regular`), por simetría con `RegistrarMedicion`. Si la calificación no dispara hallazgo, el aggregate ignora `HallazgoId`.

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/evaluacion
public sealed record RegistrarEvaluacionCualitativaRequest(
    Guid    HallazgoId,           // UUIDv7 generado client-side
    string  Calificacion,         // "Bueno" | "Regular" | "Malo" (deserializa a enum)
    string? Observacion);

public sealed record RegistrarEvaluacionCualitativaResult(
    Guid    InspeccionId,
    int     ItemId,
    string  Calificacion,
    Guid?   HallazgoGeneradoId,  // poblado solo si Calificacion=Malo; null en Bueno/Regular
    DateTimeOffset RegistradaEn);
```

---

## 3. Evento(s) emitido(s)

Este slice emite uno o dos eventos al mismo stream, en un único `SaveChangesAsync`, en el siguiente orden causal:

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `EvaluacionCualitativaRegistrada_v1` | Ver §3.1 | **Siempre** (Bueno, Regular, Malo) |
| 2 | `HallazgoRegistrado_v1` (extendido con `EvaluacionOrigenId`) | Ver §3.2 | **Solo si** `Calificacion=Malo` |

**Orden causal obligatorio:** `EvaluacionCualitativaRegistrada_v1` primero, `HallazgoRegistrado_v1` segundo. El aggregate necesita haber registrado la evaluación (y marcado el ítem como evaluado en `_itemsEvaluados`) antes de emitir el hallazgo derivado. Invertir el orden rompe el rebuild desde stream — un `Apply(HallazgoRegistrado_v1)` que intente leer `_itemsEvaluados` para contexto lo encontraría incompleto.

### 3.1 `EvaluacionCualitativaRegistrada_v1` — sin cambio de nombre ni versión

El evento ya está definido en §12.11.5 punto 5. El campo `RegistradaEn` se usa con `DateTimeOffset` (mismo ajuste aplicado en slice 1i a `MedicionRegistrada_v1` — coherencia con la convención del módulo; el modelo histórico usa `DateTime`).

```csharp
public sealed record EvaluacionCualitativaRegistrada_v1(
    Guid          InspeccionId,
    int           ItemId,             // PK ERP del ítem (int — §15.4)
    CalificacionCualitativa Calificacion,
    string?       Observacion,        // opcional
    string        EmitidoPor,         // tecnicoId opaco — campo a añadir (no está en §12.11.5 punto 5)
    DateTimeOffset RegistradaEn);     // DateTimeOffset en lugar de DateTime — coherencia módulo
```

> **Campo `EmitidoPor` no presente en §12.11.5 punto 5:** se propone añadirlo para coherencia con el resto de eventos del módulo (todos los eventos de acción llevan `EmitidoPor`). Necesario para proyectar `Contribuyentes` en `Apply(EvaluacionCualitativaRegistrada_v1)`. Si el usuario prefiere omitirlo, se anota en §12 y el `Apply` no actualiza contribuyentes para este evento.

### 3.2 `HallazgoRegistrado_v1` extendido con `EvaluacionOrigenId: int?`

**P-3 — decisión: campo `EvaluacionOrigenId: int?` separado de `MedicionOrigenId: int?`.**

El modelo §12.11.5 punto 6 dice: `"MedicionOrigenId=ItemId (trazabilidad bidireccional)"` — nombre elegido en el contexto de ítems numéricos (mediciones). Reusar `MedicionOrigenId` para ítems cualitativos sería semánticamente impreciso: `Medicion` implica valor numérico, pero una evaluación cualitativa no es una medición. Desde el punto de vista de proyecciones y reporting, una consulta `"hallazgos derivados de mediciones fuera de rango"` vs. `"hallazgos derivados de calificaciones Malo"` son dos conjuntos distintos y útiles por separado.

Se agrega `EvaluacionOrigenId: int?` como campo paralelo a `MedicionOrigenId: int?`. Para eventos históricos con `Origen ∈ {Manual, PreOperacional, Seguimiento, Monitoreo/numérico}`, Marten deserializa el campo como `null` — sin migración de datos.

```csharp
public sealed record HallazgoRegistrado_v1(
    Guid          InspeccionId,
    Guid          HallazgoId,
    OrigenHallazgo Origen,
    int?          NovedadPreopOrigenId,     // null cuando Origen=Monitoreo
    int?          MedicionOrigenId,         // Slice 1i — int? PK ERP ítem numérico. Null para este slice.
    int?          EvaluacionOrigenId,       // NUEVO (slice 1i') — int? PK ERP ítem cualitativo. Obligatorio cuando Origen=Monitoreo y la fuente es cualitativa; null en todos los demás casos.
    int           ParteEquipoId,
    int?          ActividadId,
    string?       ActividadDescripcion,
    string        NovedadTecnica,           // autogenerado: "Estado calificado Malo en conectores batería"
    AccionRequerida AccionRequerida,         // siempre RequiereSeguimiento cuando Origen=Monitoreo
    string?       AccionCorrectiva,         // null cuando Origen=Monitoreo
    int?          TipoFallaId,             // null cuando Origen=Monitoreo (I-H5)
    int?          CausaFallaId,            // null cuando Origen=Monitoreo (I-H5)
    string?       ObservacionCampo,        // propagado desde cmd.Observacion del ítem
    UbicacionGps? Ubicacion,               // null cuando Origen=Monitoreo (GPS no requerido)
    string        EmitidoPor,
    DateTimeOffset RegistradoEn);
```

> **Posición de `EvaluacionOrigenId`:** se inserta inmediatamente después de `MedicionOrigenId` para agrupar los campos de trazabilidad de origen. No rompe serialización (System.Text.Json usa nombres de propiedades, no posición).

> **Invariante derivada de §12.11.5 punto 8 para este slice:** cuando `Origen=Monitoreo` y el hallazgo proviene de un ítem cualitativo → `EvaluacionOrigenId` obligatorio e inmutable; `MedicionOrigenId` debe ser `null`. Cubierta por la construcción del evento en `RegistrarEvaluacionCualitativa`.

### 3.3 Extensión paralela en el record `Hallazgo` del aggregate (P-4)

El record `Hallazgo` gana `EvaluacionOrigenId: int?` paralelo. El `Apply(HallazgoRegistrado_v1)` lo proyecta.

```csharp
// Campos a agregar al record Hallazgo en Hallazgo.cs (posición: después de MedicionOrigenId):
int? EvaluacionOrigenId = null   // null para orígenes Manual/PreOperacional/Seguimiento/numérico; = ItemId para Monitoreo cualitativo
```

> **Oportunidad para followup #20 (`ObservacionCampo`):** el record `Hallazgo` actual carece de `ObservacionCampo: string?` (followup #20 abierto). Este slice modifica `Hallazgo.cs` para añadir `EvaluacionOrigenId`. El implementador `green` puede cerrar #20 simultáneamente añadiendo `ObservacionCampo: string?` y actualizando `Apply(HallazgoActualizado_v1)` para incluirlo en el `with { ... }`. Si lo hace, documenta en `green-notes.md`. Si no, #20 sigue abierto hasta el slice de proyecciones de detalle.

### 3.4 Nuevo estado interno del aggregate (P-5)

Se añade `HashSet<int> _itemsEvaluados` separado de `_itemsMedidos`.

**Justificación de separación (P-5):** los sets tienen semántica distinta. `_itemsMedidos` guarda ítems con evaluación numérica; `_itemsEvaluados` guarda ítems con evaluación cualitativa. El aggregate necesita distinguirlos porque:
(a) I-M5 ("ítem numérico rechaza cualitativo") y I-M5b ("ítem cualitativo rechaza numérico") usan el snapshot para saber el tipo — no el set — pero la invariante de unicidad (I-M6 vs I-M7) necesita saber si un ítem fue evaluado en su propio contexto.
(b) Un ítem numérico y un ítem cualitativo podrían hipotéticamente tener el mismo `ItemId` en una versión futura de la rutina — sets separados evitan colisión.
(c) La proyección futura `ItemsMonitoreoView` necesitará mostrar el estado por ítem (pendiente / medido / evaluado / omitido) — tener sets separados hace la proyección trivial.

Un set unificado `_itemsRegistrados` ahorraría un set pero opacaría la semántica y forzaría al `Apply` a leer el snapshot para determinar de qué tipo fue el registro — violando la pureza del Apply.

```csharp
// Campo a añadir al aggregate Inspeccion.cs (junto a _itemsMedidos):
private readonly HashSet<int> _itemsEvaluados = [];
public IReadOnlySet<int> ItemsEvaluados => _itemsEvaluados;
```

La invariante de doble evaluación es **I-M7** (nueva — no reusar I-M6 que aplica solo a ítems numéricos):

> _"I-M7: Cada ítem cualitativo del snapshot admite exactamente una evaluación por inspección. La corrección usa el comando futuro `ActualizarEvaluacionCualitativa`."_

---

## 4. Precondiciones

Las precondiciones se clasifican por la capa donde viven. Los `Apply` son puros — nunca re-validan.

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability):** el usuario tiene capability `ejecutar-inspeccion` en los claims del host PWA (ADR-002 tentativo). Excepción: `403 Forbidden`.

### Capa handler (antes de invocar el método de decisión del aggregate)

- **PRE-2 (inspección existe):** el handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).

### Método de decisión del aggregate (`Inspeccion.RegistrarEvaluacionCualitativa`)

- **PRE-3 (tipo Monitoreo — I-M1):** `Tipo == TipoInspeccion.Monitoreo`. Si la inspección es `Tecnica` → `InspeccionNoEsMonitoreoException` (`422`, código `I-M1`). Reusa excepción existente (slice 1i).
- **PRE-4 (estado EnEjecucion — I-M2):** `Estado == EstadoInspeccion.EnEjecucion`. Si `Estado ∈ {Firmada, CierrePendienteOT, Cerrada, CerradaSinOT, Cancelada}` → `InspeccionNoEnEjecucionException` (`422`, código `I-M2`). Reusa excepción existente.
- **PRE-5 (ítem existe en snapshot — I-M3):** `ItemId ∈ ItemsSnapshot.Select(i => i.ItemId)`. Si no existe → `ItemNoEncontradoEnSnapshotException` (`422`, código `I-M3`). Reusa excepción existente (slice 1i).
- **PRE-6 (ítem no omitido — I-M4):** `ItemId ∉ _itemsOmitidos`. Si el ítem fue omitido → `ItemOmitidoNoPuedeMedirseException` (`422`, código `I-M4`). Reusa excepción existente (slice 1i). Mismo nombre — la semántica de "no puede recibir evaluación posterior si fue omitido" es idéntica a medición.
- **PRE-7 (ítem es cualitativo — I-M5b):** el snapshot del ítem debe tener `Evaluacion` del tipo `EvaluacionCualitativaEsperada` (no `MedicionEsperada`). Pattern matching: `snapshot.Evaluacion is EvaluacionCualitativaEsperada`. Si es numérico → `ItemNoEsCualitativoException` (`422`, código `I-M5b`). **Nueva excepción** (análoga a `ItemNoEsNumericoException` de slice 1i).
- **PRE-8 (idempotencia de doble evaluación — I-M7):** `ItemId ∉ _itemsEvaluados`. Si el ítem ya fue evaluado → `ItemYaEvaluadoException` (`409 Conflict`, código `I-M7`). **Nueva excepción** separada de `ItemYaMedidoException`. El mensaje debe orientar al futuro comando `ActualizarEvaluacionCualitativa`.

> **Capa de validación:** PRE-1 en capa HTTP; PRE-2 en el handler; PRE-3 a PRE-8 en el método de decisión `Inspeccion.RegistrarEvaluacionCualitativa`. Ningún `Apply` re-valida.

> **Guard adicional cuando `Calificacion=Malo` (I-H1):** antes de emitir `HallazgoRegistrado_v1`, el aggregate verifica que `snapshot.ParteEquipoId is not null`. Si es `null` → `ParteEquipoIdAusenteEnSnapshotException` (`422`, followup #22). Esta excepción ya existe (slice 1i) — se reusa. El guard se evalúa solo si `Calificacion=Malo`.

---

## 5. Invariantes tocadas

### Nuevas invariantes del contexto Monitoreo

- **I-M5b (ítem cualitativo):** `RegistrarEvaluacionCualitativa` solo aplica a ítems con `EvaluacionEsperada = EvaluacionCualitativaEsperada`. Cubierta por PRE-7. Propuesta de texto para §15.3: _"I-M5b: El comando `RegistrarEvaluacionCualitativa` requiere ítem con evaluación cualitativa (`EvaluacionCualitativaEsperada`). Ítems numéricos usan `RegistrarMedicion`."_ Simétrica a I-M5.
- **I-M7 (unicidad de evaluación por ítem):** un ítem cualitativo solo puede evaluarse una vez por inspección. Cubierta por PRE-8. Propuesta de texto: _"I-M7: Cada ítem del snapshot con evaluación cualitativa admite exactamente una evaluación por inspección. La corrección usa el comando futuro `ActualizarEvaluacionCualitativa`."_

### Invariantes reutilizadas (sin cambio de texto ni código)

- **I-M1:** `RegistrarEvaluacionCualitativa` solo aplica a inspecciones `Tipo=Monitoreo`. Cubierta por PRE-3.
- **I-M2 (estado EnEjecucion):** equivalente a I2/§15.3 para flujo monitoreo. Cubierta por PRE-4. Reutiliza `InspeccionNoEnEjecucionException`.
- **I-M3 (ítem pertenece al snapshot):** cubierta por PRE-5. Reutiliza `ItemNoEncontradoEnSnapshotException`.
- **I-M4 (ítem no omitido):** cubierta por PRE-6. Reutiliza `ItemOmitidoNoPuedeMedirseException`.

### Invariantes heredadas de `HallazgoRegistrado_v1` cuando se emite con `Origen=Monitoreo`

- **I-H1:** `ParteEquipoId` siempre presente — heredado del snapshot. El guard `ParteEquipoIdAusenteEnSnapshotException` protege esta invariante (followup #22).
- **I-H4 / I-H5:** `AccionRequerida=RequiereSeguimiento` → `TipoFallaId` y `CausaFallaId` son `null`. Hardcodeado en el aggregate.
- **I-H6:** múltiples hallazgos de diferentes ítems cualitativos del mismo equipo están permitidos — cada `Malo` genera su propio hallazgo.
- **Invariante derivada §12.11.5 punto 8 (EvaluacionOrigenId obligatorio):** `Origen=Monitoreo` (cualitativo) → `EvaluacionOrigenId` obligatorio e inmutable; `MedicionOrigenId=null`. Cubierta por construcción del evento en `RegistrarEvaluacionCualitativa`.
- **Invariante derivada §12.11.5 punto 8 (AccionRequerida fija):** `Origen=Monitoreo` → `AccionRequerida=RequiereSeguimiento`. Hardcodeado.
- **Invariante derivada §12.11.5 punto 8 (TipoInspeccion):** `Origen=Monitoreo` → `Tipo` del stream `= Monitoreo`. Cubierta por PRE-3.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — calificación `Bueno` (un evento)

**Given**
- Stream con `InspeccionIniciada_v1` donde `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `ItemsSnapshot` contiene `ItemId=2` con `Evaluacion=EvaluacionCualitativaEsperada()`, `Parte="Conectores batería"`, `Actividad="Revisar estado"`.
- `_itemsEvaluados = {}`, `_itemsOmitidos = {}`.
- Claims: `EmitidoPor="ana.gomez"`, capability `ejecutar-inspeccion`.

**When**
- Comando `RegistrarEvaluacionCualitativa(InspeccionId=X, HallazgoId=G_ignorado, ItemId=2, Calificacion=Bueno, Observacion=null, EmitidoPor="ana.gomez")`.

**Then**
- Se emite exactamente **un** evento: `EvaluacionCualitativaRegistrada_v1(InspeccionId=X, ItemId=2, Calificacion=Bueno, Observacion=null, EmitidoPor="ana.gomez", RegistradaEn=now)`.
- **No** se emite `HallazgoRegistrado_v1`.
- `_itemsEvaluados` contiene `2`.
- `Hallazgos.Count` no cambia.
- Handler retorna `RegistrarEvaluacionCualitativaResult(InspeccionId=X, ItemId=2, Calificacion="Bueno", HallazgoGeneradoId=null, RegistradaEn=now)`.
- Capa API devuelve `200 OK`.

### 6.2 Happy path — calificación `Regular` (un evento; no dispara hallazgo)

**Given** — mismo aggregate que 6.1.

**When**
- Comando `RegistrarEvaluacionCualitativa(ItemId=2, Calificacion=Regular, Observacion="desgaste visible, revisar próximo mantenimiento", ...)`.

**Then**
- Se emite exactamente **un** evento: `EvaluacionCualitativaRegistrada_v1(ItemId=2, Calificacion=Regular, Observacion="desgaste visible...", RegistradaEn=now)`.
- **No** se emite `HallazgoRegistrado_v1`.
- `_itemsEvaluados` contiene `2`.
- `Hallazgos.Count` no cambia.
- Capa API devuelve `200 OK`.

> **Decisión P-2:** `Regular` no dispara hallazgo automático. Fuente: §12.11.5 punto 6, tabla explícita — "Cualitativo `Regular` | No (decisión 2 = solo Malo dispara) | —". El modelo es inequívoco; no requiere asunción del modelador.

### 6.3 Happy path — calificación `Malo` (dos eventos atómicos)

**Given**
- Mismo aggregate que 6.1.
- `ItemsSnapshot` contiene `ItemId=2` con `ParteEquipoId=55`, `Parte="Conectores batería"`, `Actividad="Revisar estado"`, `Evaluacion=EvaluacionCualitativaEsperada()`.
- `TimeProvider` retorna `2026-05-08T14:30:00Z`.

**When**
- Comando `RegistrarEvaluacionCualitativa(InspeccionId=X, HallazgoId=G1, ItemId=2, Calificacion=Malo, Observacion="corrosión severa en terminales", EmitidoPor="ana.gomez")`.

**Then**
- Se emiten **dos** eventos en orden causal en un único `SaveChangesAsync`:
  1. `EvaluacionCualitativaRegistrada_v1(InspeccionId=X, ItemId=2, Calificacion=Malo, Observacion="corrosión severa en terminales", EmitidoPor="ana.gomez", RegistradaEn=2026-05-08T14:30:00Z)`.
  2. `HallazgoRegistrado_v1(InspeccionId=X, HallazgoId=G1, Origen=Monitoreo, NovedadPreopOrigenId=null, MedicionOrigenId=null, EvaluacionOrigenId=2, ParteEquipoId=55, ActividadId=null, ActividadDescripcion=null, NovedadTecnica="Estado calificado Malo en conectores batería", AccionRequerida=RequiereSeguimiento, AccionCorrectiva=null, TipoFallaId=null, CausaFallaId=null, ObservacionCampo="corrosión severa en terminales", Ubicacion=null, EmitidoPor="ana.gomez", RegistradoEn=2026-05-08T14:30:00Z)`.
- `_itemsEvaluados` contiene `2`.
- `Hallazgos.Count = 1`, hallazgo activo: `Origen=Monitoreo`, `EvaluacionOrigenId=2`, `MedicionOrigenId=null`, `AccionRequerida=RequiereSeguimiento`, `Eliminado=false`.
- Handler retorna `RegistrarEvaluacionCualitativaResult(InspeccionId=X, ItemId=2, Calificacion="Malo", HallazgoGeneradoId=G1, RegistradaEn=2026-05-08T14:30:00Z)`.
- Capa API devuelve `200 OK`.

> **Formato de `NovedadTecnica`:** `"Estado calificado Malo en {parte}"` — paralelo al formato de 1i (`"Voltaje 10.2V fuera de rango esperado [12.3, 12.5]"`). `Parte` viene del snapshot (`snapshot.Parte`). No se requiere `CapitalizarPrimera` porque "Estado" ya está capitalizado como literal fijo.
> **P-6 (`NovedadTecnica`):** el formato exacto es `$"Estado calificado Malo en {snapshot.Parte}"`. El modelo §12.11.5 punto 6 dice textualmente: `"Estado calificado Malo en conectores batería"`. Se adopta este template literal. Separado de P-1/P-2/P-7 como decisión interna del aggregate.

### 6.4 Violación PRE-3 / I-M1 — inspección técnica rechaza evaluación cualitativa

**Given**
- Stream con `InspeccionIniciada_v1` donde `Tipo=Tecnica`, `Estado=EnEjecucion`.

**When**
- Comando `RegistrarEvaluacionCualitativa(InspeccionId=X, ItemId=2, Calificacion=Bueno, ...)`.

**Then**
- Aggregate lanza `InspeccionNoEsMonitoreoException("La inspección X es de tipo Tecnica. RegistrarEvaluacionCualitativa solo aplica a inspecciones de Tipo=Monitoreo.")`.
- Sin evento. Sin cambio en `Hallazgos`.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M1" }`.

### 6.5 Violación PRE-4 / I-M2 — inspección Firmada rechaza evaluación

**Given**
- Stream con `[InspeccionIniciada_v1(Tipo=Monitoreo), InspeccionFirmada_v1]` → `Estado=Firmada`.

**When**
- Comando `RegistrarEvaluacionCualitativa(InspeccionId=X, ItemId=2, Calificacion=Bueno, ...)`.

**Then**
- Aggregate lanza `InspeccionNoEnEjecucionException("La inspección está en estado 'Firmada'. Solo se pueden registrar evaluaciones en estado EnEjecucion.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M2" }`.

### 6.6 Violación PRE-5 / I-M3 — ítem inexistente en snapshot

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemsSnapshot` contiene solo `ItemId=2`.

**When**
- Comando `RegistrarEvaluacionCualitativa(ItemId=999, ...)`.

**Then**
- Aggregate lanza `ItemNoEncontradoEnSnapshotException("El ítem 999 no forma parte del snapshot de esta inspección.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M3" }`.

### 6.7 Violación PRE-6 / I-M4 — ítem previamente omitido

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `_itemsOmitidos = {2}` (ítem omitido por `ItemMonitoreoOmitido_v1` previo).

**When**
- Comando `RegistrarEvaluacionCualitativa(ItemId=2, ...)`.

**Then**
- Aggregate lanza `ItemOmitidoNoPuedeMedirseException("El ítem 2 fue omitido en esta inspección. Un ítem omitido no puede recibir evaluación posterior.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M4" }`.

### 6.8 Violación PRE-7 / I-M5b — ítem numérico rechaza evaluación cualitativa

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`. `ItemsSnapshot` contiene `ItemId=1` con `Evaluacion=MedicionEsperada(magnitud="voltaje", unidad="V", valorMin=12.3, valorMax=12.5)`.

**When**
- Comando `RegistrarEvaluacionCualitativa(ItemId=1, Calificacion=Bueno, ...)`.

**Then**
- Aggregate lanza `ItemNoEsCualitativoException("El ítem 1 ('Batería') tiene evaluación numérica. Usa el comando RegistrarMedicion para este ítem.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-M5b" }`.

### 6.9 Violación PRE-8 / I-M7 — doble evaluación del mismo ítem rechazada (409)

**Given**
- Aggregate con `_itemsEvaluados = {2}` (ítem ya evaluado por `EvaluacionCualitativaRegistrada_v1` previo).

**When**
- Segundo comando `RegistrarEvaluacionCualitativa(ItemId=2, Calificacion=Malo, ...)`.

**Then**
- Aggregate lanza `ItemYaEvaluadoException("El ítem 2 ya fue evaluado en esta inspección. Para corregir una evaluación, usa el comando ActualizarEvaluacionCualitativa (disponible en slice futuro).")`.
- Sin evento. `_itemsEvaluados` no cambia. `Hallazgos.Count` no cambia.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I-M7" }`.

### 6.10 PRE-2 — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando `RegistrarEvaluacionCualitativa(InspeccionId=Z, ItemId=2, ...)`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Capa API devuelve `404 Not Found`.

### 6.11 Guard I-H1 — `ParteEquipoId` ausente en snapshot cuando `Calificacion=Malo`

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `ItemsSnapshot` contiene `ItemId=2` con `Evaluacion=EvaluacionCualitativaEsperada()`, `ParteEquipoId=null` (snapshot del slice 1h anterior a la extensión P-1).

**When**
- Comando `RegistrarEvaluacionCualitativa(ItemId=2, Calificacion=Malo, HallazgoId=G1, ...)`.

**Then**
- Aggregate lanza `ParteEquipoIdAusenteEnSnapshotException("El snapshot del ítem 2 no tiene ParteEquipoId. El hallazgo automático requiere ParteEquipoId (I-H1). Refresha los catálogos y reinicia la inspección (followup #22).")`.
- Sin evento. `_itemsEvaluados` no cambia.
- Capa API devuelve `422 Unprocessable Entity` con código descriptivo.

> **Nota:** este guard NO aplica cuando `Calificacion ∈ {Bueno, Regular}` — no se emite hallazgo, no se necesita `ParteEquipoId`.

### 6.12 Idempotencia — replay con mismo `clientCommandId`

**Given**
- Comando con `MessageId=X` ya ejecutado exitosamente (ítem 2, `Calificacion=Bueno`). Wolverine envelope storage tiene respuesta original.

**When**
- Cliente reenvía mismo `MessageId=X` tras timeout de red.

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- `_itemsEvaluados` permanece con `{2}` (un solo registro).
- Un solo `EvaluacionCualitativaRegistrada_v1` en el stream.
- Capa API devuelve `200 OK` con body original.

### 6.13 Múltiples ítems cualitativos — cada `Malo` genera su propio hallazgo

**Given**
- Aggregate `Tipo=Monitoreo`, `Estado=EnEjecucion`.
- `ItemsSnapshot` contiene `ItemId=2` (cualitativo, `ParteEquipoId=55`, `Parte="Conectores batería"`) e `ItemId=4` (cualitativo, `ParteEquipoId=60`, `Parte="Mangueras hidráulicas"`).
- `_itemsEvaluados = {2}` con `EvaluacionCualitativaRegistrada_v1(ItemId=2, Calificacion=Malo)` y `HallazgoRegistrado_v1(HallazgoId=G1, EvaluacionOrigenId=2)` ya en el stream.

**When**
- Comando `RegistrarEvaluacionCualitativa(ItemId=4, Calificacion=Malo, HallazgoId=G2, ...)`.

**Then**
- `EvaluacionCualitativaRegistrada_v1(ItemId=4, Calificacion=Malo)` + `HallazgoRegistrado_v1(HallazgoId=G2, EvaluacionOrigenId=4, ParteEquipoId=60, NovedadTecnica="Estado calificado Malo en mangueras hidráulicas")`.
- `_itemsEvaluados = {2, 4}`.
- `Hallazgos.Count = 2` (G1 y G2 coexisten — I-H6 permitido).

### 6.14 Coexistencia — ítem numérico y cualitativo con mismo `ItemId` (improbable pero posible)

> **Nota:** este escenario es teórico — hoy una rutina no tiene ítems con el mismo `ItemId` de tipo distinto. El test documenta que los sets `_itemsMedidos` y `_itemsEvaluados` son independientes.

**Given**
- Aggregate con `_itemsMedidos = {1}` y `_itemsEvaluados = {}`.

**When**
- Se intenta `RegistrarEvaluacionCualitativa(ItemId=1, ...)` sobre un ítem que resulta numérico en el snapshot.

**Then**
- I-M5b se dispara (`ItemNoEsCualitativoException`) — el set `_itemsMedidos` no interfiere con el guard.

### 6.15 Atomicidad — dos eventos o ninguno (rollback si falla persist)

**Given**
- Aggregate con `ItemId=2` cualitativo. `Calificacion=Malo`. `ParteEquipoId=55` en snapshot.

**When**
- `SaveChangesAsync()` falla en mitad de la transacción (simulado en test de integración con Testcontainers).

**Then**
- Ni `EvaluacionCualitativaRegistrada_v1` ni `HallazgoRegistrado_v1` se persisten en `mt_events`.
- El stream permanece en el estado previo.
- `_itemsEvaluados` permanece vacío.
- El handler propaga la excepción al middleware.

> Atomicidad garantizada por Marten con único `SaveChangesAsync`. Test de integración con Testcontainers Postgres.

### 6.16 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos).
- Lista de eventos en orden causal para reproducir el happy path 6.3:
  1. `InspeccionIniciada_v1(Tipo=Monitoreo, ItemsSnapshot=[{ItemId=1, MedicionEsperada(12.3, 12.5), ParteEquipoId=50}, {ItemId=2, EvaluacionCualitativaEsperada(), Parte="Conectores batería", ParteEquipoId=55}])`.
  2. `EvaluacionCualitativaRegistrada_v1(ItemId=2, Calificacion=Malo, Observacion="corrosión severa", EmitidoPor="ana.gomez", RegistradaEn=2026-05-08T14:30:00Z)`.
  3. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Monitoreo, EvaluacionOrigenId=2, MedicionOrigenId=null, ParteEquipoId=55, NovedadTecnica="Estado calificado Malo en conectores batería", AccionRequerida=RequiereSeguimiento, ...)`.

**When**
- Se reproyectan los tres eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- Estado resultante:
  - `Tipo = TipoInspeccion.Monitoreo`.
  - `Estado = EstadoInspeccion.EnEjecucion`.
  - `ItemsSnapshot.Count = 2`.
  - `_itemsMedidos = {}`.
  - `_itemsEvaluados = {2}`.
  - `_itemsOmitidos = {}`.
  - `Hallazgos.Count = 1`.
  - `Hallazgos[0].HallazgoId = G1`, `Origen=Monitoreo`, `EvaluacionOrigenId=2`, `MedicionOrigenId=null`, `AccionRequerida=RequiereSeguimiento`, `Eliminado=false`.
  - `Contribuyentes = {"ana.gomez"}` (desde `InspeccionIniciada_v1` + `EvaluacionCualitativaRegistrada_v1` + `HallazgoRegistrado_v1`).
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al obtenido tras ejecutar el comando en 6.3.
- El test garantiza que invertir el orden de los eventos 2 y 3 dejaría el `Apply(HallazgoRegistrado_v1)` procesándose antes de que `_itemsEvaluados` tenga el ítem 2 registrado — evidenciando la dependencia causal.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente PWA genera `clientCommandId: UUIDv7` cuando el técnico confirma la calificación del ítem. Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler (escenario 6.12).

**Idempotencia natural por I-M7:**

Si el cliente reenvía con un `clientCommandId` distinto (nuevo retry) sobre un ítem ya evaluado, el aggregate lanza `ItemYaEvaluadoException` (`409 Conflict`). El cliente debe distinguir: `409` = el ítem ya fue evaluado en esta sesión, no es error de red — abrir el flujo `ActualizarEvaluacionCualitativa` si el técnico quiere corregir.

**Doble evaluación vs. corrección:**

El `409` por I-M7 es intencional y no es retryable automáticamente. El cliente no debe reintentar en `409`.

**Sin POST a Sinco:**

Este comando no cruza al ERP en línea. ADR-006 (outbox para integraciones ERP) no aplica directamente — no hay llamada HTTP saliente en el handler. El outbox transaccional de Wolverine garantiza atomicidad evento(s) + proyección + envelope en un único `SaveChangesAsync`.

**`Idempotency-Key` para Sinco:**

No aplica en este slice (sin llamadas al ERP).

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` — consumirá `EvaluacionCualitativaRegistrada_v1`

La proyección `DetalleInspeccionView` (§15.12.1) no existe aún (roadmap 3.45). Cuando se implemente, debe consumir `EvaluacionCualitativaRegistrada_v1` para mostrar la calificación y la observación por ítem cualitativo. Este slice no la crea; documenta qué evento consume.

Campos proyectados: `ItemId`, `Calificacion`, `Observacion`, `EmitidoPor`, `RegistradaEn`.

### 8.2 `BandejaTecnicoView` — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio.

### 8.3 `InspeccionAbiertaPorEquipoView` — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio.

### 8.4 `ItemsMonitoreoView` (futura)

Una proyección dedicada al checklist de ítems de la inspección de monitoreo (estado por ítem: pendiente / medido / evaluado / omitido) es necesaria para la UX del técnico. No existe aún. Cuando se implemente, consumirá `MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1` e `ItemMonitoreoOmitido_v1`. Este slice no la crea; ya especificada como futura en slice 1i §8.5.

---

## 9. Impacto en endpoints HTTP

**Endpoint:** `POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/evaluacion`

> Ruta paralela a `POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/medicion` (slice 1i). `inspeccionId` e `itemId` en el path. El body solo contiene los campos propios de la evaluación.

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO:**

```json
{
  "hallazgoId": "0193a5b2-...",
  "calificacion": "Malo",
  "observacion": "corrosión severa en terminales"
}
```

**Response 200 OK (happy path — Bueno o Regular):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "itemId": 2,
  "calificacion": "Bueno",
  "hallazgoGeneradoId": null,
  "registradaEn": "2026-05-08T14:30:00Z"
}
```

**Response 200 OK (happy path — Malo):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "itemId": 2,
  "calificacion": "Malo",
  "hallazgoGeneradoId": "0193a5b2-...",
  "registradaEn": "2026-05-08T14:30:00Z"
}
```

**Códigos de error:**

| Código HTTP | `codigoError` | Escenario |
|---|---|---|
| `403 Forbidden` | `"PRE-1"` | Capability ausente |
| `404 Not Found` | `"PRE-2"` | InspeccionId no existe |
| `409 Conflict` | `"I-M7"` | Ítem ya evaluado (PRE-8) |
| `422 Unprocessable Entity` | `"I-M1"` | Inspección es Tecnica (PRE-3) |
| `422 Unprocessable Entity` | `"I-M2"` | Inspección no en EnEjecucion (PRE-4) |
| `422 Unprocessable Entity` | `"I-M3"` | ItemId no en snapshot (PRE-5) |
| `422 Unprocessable Entity` | `"I-M4"` | Ítem omitido (PRE-6) |
| `422 Unprocessable Entity` | `"I-M5b"` | Ítem numérico (PRE-7) |
| `422 Unprocessable Entity` | `"PARTE-AUSENTE"` | ParteEquipoId null en snapshot al intentar Malo (followup #22) |

**Rol/permiso requerido:** capability `ejecutar-inspeccion`. Heredado del host PWA.

**Nota sobre GPS:** el comando `RegistrarEvaluacionCualitativa` **no incluye** `UbicacionGps`. Por simetría con slice 1i — la ubicación ya fue capturada al iniciar (`InspeccionIniciada_v1.Ubicacion`) y se considera suficiente para el MVP. El hallazgo derivado lleva `Ubicacion=null`.

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `EvaluacionCualitativaRegistrada_v1` y el `HallazgoRegistrado_v1` derivado no generan push hacia el frontend según el catálogo vigente de ADR-005 (`§14` del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). El registro de evaluaciones es una operación local del técnico en su dispositivo — no hay otras partes que necesiten notificación en tiempo real.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `RegistrarEvaluacionCualitativa` no consume ni publica hacia el ERP. Trabaja exclusivamente con el aggregate cargado desde el stream de Marten (`ItemsSnapshot` fue persistido en `InspeccionIniciada_v1`). No hay llamadas a M-3b, M-16 ni a ningún otro endpoint ERP en este handler.

> Los datos del ítem (parte, actividad) se leen del `ItemsSnapshot` dentro del evento ya persistido, no del catálogo local `RutinaMonitoreoLocal`. Esto garantiza que la evaluación se hace contra el ítem vigente al momento del inicio de la inspección.

---

## 12. Preguntas abiertas

### P-1 — Enum `CalificacionCualitativa` — confirmado contra §12.11.5

**Estado: RESUELTA.** El enum ya está definido en §12.11.5 punto 3:

```csharp
public enum CalificacionCualitativa
{
    Bueno,    // estado correcto
    Regular,  // estado deteriorado, requiere atención eventual
    Malo      // estado crítico, dispara hallazgo automático con seguimiento
}
```

Tres valores. Sin orden numérico explícito en el enum. El modelo no asigna valores `int` — la serialización en Marten usa nombre de cadena (convención del módulo). No se requiere ninguna propuesta alternativa.

### P-2 — ¿`Regular` dispara hallazgo automático?

**Estado: RESUELTA.** §12.11.5 punto 6 es inequívoco:

> "Cualitativo `Regular` | No (decisión 2 = solo Malo dispara) | —"

Solo `Malo` dispara `HallazgoRegistrado_v1`. `Regular` emite únicamente `EvaluacionCualitativaRegistrada_v1`. No requiere asunción del modelador.

### P-3 — `HallazgoRegistrado_v1`: campo `EvaluacionOrigenId: int?` nuevo vs. reusar `MedicionOrigenId`

**Estado: RESUELTA. Decisión: campo nuevo `EvaluacionOrigenId: int?`.**

**Justificación:**

1. **Semántica:** `MedicionOrigenId` implica valor numérico medido. Una evaluación cualitativa (`Bueno`/`Regular`/`Malo`) no es una medición. Proyecciones y reportes que consulten `"hallazgos de mediciones fuera de rango"` vs. `"hallazgos de calificaciones Malo"` son conjuntos distintos y útiles — un campo unificado los opacaría.
2. **Backward compatibility:** ambos campos son `int?`. Los eventos históricos de orígenes `Manual`, `PreOperacional`, `Seguimiento` o `Monitoreo-numérico` tendrán `EvaluacionOrigenId=null` por deserialización. Sin migración.
3. **Extensibilidad:** si en el futuro emergen otros tipos de ítems (p. ej. ítems de verificación booleana), cada uno puede tener su propio campo de trazabilidad sin ambigüedad. Un campo polimórfico requeriría un discriminador adicional.
4. **El modelo §12.11.5 punto 6 no nombra explícitamente el campo para ítems cualitativos** — dice `"MedicionOrigenId=ItemId"` en el contexto de la decisión que unifica ambos tipos. La spec 1i adoptó `MedicionOrigenId` para el caso numérico. El campo `EvaluacionOrigenId` es la extensión natural para el caso cualitativo.

> **Nota sobre el texto de §12.11.5 punto 6:** el modelo dice `"MedicionOrigenId=ItemId"` para ambos tipos de hallazgo automático (numérico y cualitativo). Este spec propone `EvaluacionOrigenId` para el cualitativo como extensión semánticamente superior al texto del modelo. Se propone actualizar §12.11.5 punto 6 y §15.3 en el PR de este slice para reflejar la distinción.

### P-4 — Record `Hallazgo` gana `EvaluacionOrigenId: int?`

**Estado: RESUELTA. Sí.** Si P-3 agrega el campo al evento, el record `Hallazgo` del state debe reflejarlo para que el aggregate pueda consultarlo (p. ej. proyección futura que liste hallazgos con su ítem origen). Campo nullable con valor default `null` — backward compatible con hallazgos existentes.

### P-5 — Estado interno: `_itemsEvaluados` separado vs. `_itemsRegistrados` unificado

**Estado: RESUELTA. `_itemsEvaluados` separado.** Justificación detallada en §3.4. Los sets separados preservan la semántica, simplifican las proyecciones futuras y no rompen nada existente (`_itemsMedidos` ya existe y no se toca).

### P-6 — Formato de `NovedadTecnica` autogenerada

**Estado: RESUELTA.** Template: `$"Estado calificado Malo en {snapshot.Parte}"`.

Fuente: §12.11.5 punto 6 proporciona el ejemplo literal: `"Estado calificado Malo en conectores batería"`. Se adopta este template. No requiere `CapitalizarPrimera` — el literal empieza con "Estado" ya capitalizado. El campo es editable por el técnico mediante `ActualizarHallazgo` si el texto autogenerado no es suficientemente descriptivo.

### P-7 — `ItemYaEvaluadoException` nueva vs. reusar `ItemYaMedidoException`

**Estado: RESUELTA. Nueva excepción `ItemYaEvaluadoException` (I-M7).** Justificación: el mensaje de error orientativo hacia el técnico debe mencionar el tipo correcto de operación ("evaluación", no "medición") y el comando de corrección correcto (`ActualizarEvaluacionCualitativa`). Reusar `ItemYaMedidoException` produciría mensajes de error confusos ("ya fue medido" para un ítem cualitativo). El código HTTP `409 Conflict` es el mismo.

### Followup #22 (M-16 / `ParteEquipoId`)

Sigue pendiente (David). El guard `ParteEquipoIdAusenteEnSnapshotException` ya existe (slice 1i) y se reusa en este slice para el caso `Calificacion=Malo`. Escenario 6.11 cubre este caso. No bloquea el spec — la excepción ya está implementada.

### Followup #20 (`ObservacionCampo` en record `Hallazgo`)

Sigue abierto. Este slice es la segunda oportunidad de cerrarlo (la primera fue slice 1i, que no lo cerró). Se delega al implementador `green` la decisión de añadir `ObservacionCampo: string?` al record `Hallazgo` al mismo tiempo que añade `EvaluacionOrigenId: int?`. Si lo cierra, documenta en `green-notes.md`.

---

## 13. Checklist pre-firma

- [ ] P-1 resuelta — `CalificacionCualitativa { Bueno, Regular, Malo }` confirmado contra §12.11.5 punto 3.
- [ ] P-2 resuelta — `Regular` no dispara hallazgo. Solo `Malo`. Fuente: §12.11.5 punto 6 (explícito).
- [ ] P-3 resuelta — campo nuevo `EvaluacionOrigenId: int?` en `HallazgoRegistrado_v1`. Backward compatible. Propone actualizar §12.11.5 punto 6 en el PR.
- [ ] P-4 resuelta — record `Hallazgo` gana `EvaluacionOrigenId: int?`.
- [ ] P-5 resuelta — `_itemsEvaluados: HashSet<int>` separado de `_itemsMedidos`.
- [ ] P-6 resuelta — `NovedadTecnica` autogenerada: `$"Estado calificado Malo en {snapshot.Parte}"`.
- [ ] P-7 resuelta — nueva excepción `ItemYaEvaluadoException` (código `I-M7`, `409 Conflict`).
- [ ] Todas las precondiciones (PRE-1..PRE-8 + guard I-H1) tienen escenario Given/When/Then en §6 (6.4→PRE-3/I-M1, 6.5→PRE-4/I-M2, 6.6→PRE-5/I-M3, 6.7→PRE-6/I-M4, 6.8→PRE-7/I-M5b, 6.9→PRE-8/I-M7, 6.10→PRE-2, 6.11→guard I-H1).
- [ ] Happy paths presentes: 6.1 (Bueno, 1 evento), 6.2 (Regular, 1 evento), 6.3 (Malo, 2 eventos).
- [ ] Escenario de rebuild desde stream presente (6.16) — incluye verificación de orden causal.
- [ ] Idempotencia decidida (§7): envelope dedup ADR-008 + I-M7 natural (409 Conflict).
- [ ] §10 SignalR resuelto explícitamente ("no aplica").
- [ ] §11 adapters Sinco resuelto explícitamente ("no aplica").
- [ ] Extensión de `HallazgoRegistrado_v1` con `EvaluacionOrigenId: int?` documentada (§3.2) — backward compatible.
- [ ] Extensión de `Hallazgo` record con `EvaluacionOrigenId: int?` documentada (§3.3).
- [ ] Campo `EmitidoPor` añadido a `EvaluacionCualitativaRegistrada_v1` (§3.1) — pendiente confirmación del usuario.
- [ ] Nuevo estado interno del aggregate documentado: `HashSet<int> _itemsEvaluados` (§3.4).
- [ ] Nuevas invariantes I-M5b e I-M7 propuestas para `01-modelo-dominio.md §15.3`.
- [ ] Nuevas excepciones propuestas: `ItemNoEsCualitativoException` (I-M5b), `ItemYaEvaluadoException` (I-M7). Ambas en `Inspecciones.Domain.Inspecciones.Excepciones.cs`.
- [ ] Preguntas abiertas: cero items bloqueantes. P-1..P-7 resueltas. Followups #22 y #20 documentados como no-bloqueantes.

---

## Notas de cierre para revisión humana

**Lo que este slice añade respecto al 1i:**

- Extensión del record `HallazgoRegistrado_v1`: campo `EvaluacionOrigenId: int?` (posición 3, después de `MedicionOrigenId`). Backward compatible.
- Extensión del record `Hallazgo`: campo `EvaluacionOrigenId: int?`.
- Nuevo campo `EmitidoPor` en `EvaluacionCualitativaRegistrada_v1` (a confirmar).
- Nuevo método de decisión `Inspeccion.RegistrarEvaluacionCualitativa` (emite 1 o 2 eventos, atómico).
- Nuevo estado interno: `HashSet<int> _itemsEvaluados` + propiedad `IReadOnlySet<int> ItemsEvaluados`.
- `AplicarEvento` extendido con `case EvaluacionCualitativaRegistrada_v1`.
- `Apply(EvaluacionCualitativaRegistrada_v1)` puro: actualiza `_itemsEvaluados` + `Contribuyentes`.
- `Apply(HallazgoRegistrado_v1)` extendido: proyecta `EvaluacionOrigenId` en el record `Hallazgo`.
- Nuevas excepciones de dominio (2): `ItemNoEsCualitativoException`, `ItemYaEvaluadoException`.
- Handler `RegistrarEvaluacionCualitativaHandler` en `Inspecciones.Application`.
- Endpoint `POST /api/v1/inspecciones/{id}/items/{itemId}/evaluacion` en `Inspecciones.Api`.

**Lo que NO hace este slice:**

- `OmitirItemMonitoreo` — fuera de alcance.
- `ActualizarEvaluacionCualitativa` — comando de corrección post-registro; fuera de alcance.
- `ActualizarMedicion` — fuera de alcance.
- Validaciones pre-firma para completitud del checklist de monitoreo — se modelará en slice de `FirmarInspeccion` si aplica.
- Proyecciones async `DetalleInspeccionView` e `ItemsMonitoreoView`.
