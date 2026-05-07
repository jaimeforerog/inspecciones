# Slice 1f — AsignarRepuesto

**Autor:** domain-modeler
**Fecha:** 2026-05-07
**Estado:** firmado
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.2` — estructura del value object `Hallazgo`; confirma que `AccionRequerida` es campo de estado que el aggregate mantiene por hallazgo
- `01-modelo-dominio.md §15.3` — invariante `I-H9` (eliminar hallazgo bloqueado si tiene hijos: repuestos o adjuntos); invariante `I-H7` (editable solo si `EnEjecucion`); invariante I10 del modelo histórico (`§12.10.12`): solo `RequiereIntervencion` puede tener repuestos
- `01-modelo-dominio.md §15.4` — catálogo final de 24 eventos; evento #10 `RepuestoEstimado_v1`, #11 `RepuestoActualizado_v1`, #12 `RepuestoRemovido_v1`; convención de tipos de IDs (ERP → `int`, internos → `Guid`)
- `01-modelo-dominio.md §12.10.12` — decisiones operativas de `RepuestoEstimadoAgregado_v1` (nombre histórico): `Unidad` se deriva del catálogo, compatibilidad SKU↔Parte es hard error en handler, `Cantidad > 0`, `AccionRequerida = RequiereIntervencion` es obligatorio
- `01-modelo-dominio.md §12.7` — `RepuestoLocal` con campos `SkuId: int`, `CodigoSinco: string`, `UnidadMedida: string`, `ParteIdsCompatibles: List<int>`
- `slices/1e-eliminar-hallazgo/spec.md` — patrón PRE-A / PRE-B1 / PRE-B2 / PRE-D (I-H9); §6.7 marcado `[Fact(Skip=...)]` esperando `AsignarRepuesto` — este slice levanta ese skip
- ADR-002 (tentativo) — identidad 100% del host PWA; `TecnicoId` opaco del JWT
- ADR-004 (`§9.15`) — sync on-app-open de catálogos; `RepuestoLocal` vive en catálogo local sincronizado
- ADR-008 (`§9.16`) — idempotencia por `X-Client-Command-Id`

---

## 1. Intención

El técnico necesita registrar los repuestos estimados para un hallazgo que requiere intervención, para que la saga de cierre (`CerrarInspeccionSaga`) pueda consolidar el BOM (Bill of Materials) de la OT correctiva que se abrirá en Sinco MYE. El repuesto se elige de un catálogo local sincronizado (`RepuestoLocal`) y se vincula a un hallazgo específico. Solo los hallazgos con `AccionRequerida = RequiereIntervencion` admiten repuestos. Los repuestos son optativos en el cierre (V-F3 §15.5 confirma que la intervención puede ser solo mano de obra), pero cuando se asignan deben referenciar un SKU válido y compatible con la parte del hallazgo destino.

---

## 2. Comando

```csharp
public sealed record AsignarRepuesto(
    Guid    InspeccionId,
    Guid    HallazgoId,     // hallazgo destino; debe tener AccionRequerida=RequiereIntervencion
    Guid    RepuestoId,     // ID interno del módulo generado por el cliente (handler o test)
    int     SkuId,          // PK del insumo/repuesto en el ERP Sinco (int, no string)
    decimal Cantidad,       // cantidad estimada; > 0, permite fracciones (litros, galones)
    string? Justificacion,  // opcional — texto libre del técnico
    string  TecnicoId       // extraído del JWT por la capa API; el dominio lo recibe como parámetro
) : ICommand;
```

**Nota sobre `Unidad`:** no viaja en el comando. El handler la deriva de `RepuestoLocal.UnidadMedida` antes de invocar el método de decisión del aggregate. El aggregate recibe `Unidad: string` ya resuelta (parámetro adicional del método, no del record de comando).

**Nota sobre `SkuCodigo`:** el handler lo obtiene de `RepuestoLocal.CodigoSinco` para que el evento lo persista legible en el stream (UI/reporting sin joins).

**Nota sobre `RepuestoId`:** lo genera el handler (o el test en la fase red) con `Guid.NewGuid()` — el dominio lo recibe desde fuera, conforme a la convención del CLAUDE.md.

---

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `RepuestoEstimado_v1` | Ver campos a continuación | Cuando el comando supera todas las precondiciones e invariantes. Un único evento por invocación. |

```csharp
public sealed record RepuestoEstimado_v1(
    Guid           InspeccionId,
    Guid           HallazgoId,
    Guid           RepuestoId,     // ID interno del módulo
    int            SkuId,          // PK ERP Sinco
    string         SkuCodigo,      // CodigoSinco del catálogo — legible para UI/reporting
    decimal        Cantidad,
    string?        Justificacion,
    string         Unidad,         // derivada de RepuestoLocal.UnidadMedida en el handler
    string         AsignadoPor,    // TecnicoId del JWT
    DateTimeOffset AsignadoEn      // TimeProvider.GetUtcNow() en el handler — no editable
);
```

**Nota sobre `DateTimeOffset`:** el modelo histórico §12.10.12 usa `DateTime EstimadoEn`; la convención vigente del CLAUDE.md es `DateTimeOffset` para todos los timestamps. Este slice usa `DateTimeOffset AsignadoEn`, consistente con `ActualizadoEn: DateTimeOffset` de `HallazgoActualizado_v1` y `IniciadaEn: DateTimeOffset` de `InspeccionIniciada_v1`.

**Nota sobre `Apply`:** `Apply(RepuestoEstimado_v1)` añade un record `Repuesto` (value object de estado) a la colección `_repuestos` del aggregate. Los campos del record de estado son: `RepuestoId`, `HallazgoId`, `SkuId`, `SkuCodigo`, `Cantidad`, `Justificacion`, `Unidad`. El `Apply` es puro — sin validaciones.

**Estado interno nuevo que introduce este slice:**

```csharp
// Value object de estado interno del aggregate Inspeccion.
// Introducido en slice 1f. Solo para fold — no es evento ni comando.
public sealed record Repuesto(
    Guid     RepuestoId,
    Guid     HallazgoId,
    int      SkuId,
    string   SkuCodigo,
    decimal  Cantidad,
    string?  Justificacion,
    string   Unidad
);

// En la clase Inspeccion:
// private readonly List<Repuesto> _repuestos = [];
// public IReadOnlyList<Repuesto> Repuestos => _repuestos.AsReadOnly();
```

La colección `_repuestos` habilita la verificación de I-H9 en `EliminarHallazgo`. El comentario `// PRE-D / I-H9: verificar cuando existan slices de repuestos/adjuntos` en `Inspeccion.EliminarHallazgo` se completa en este slice: el agente `green` debe implementar la verificación real usando `_repuestos.Any(r => r.HallazgoId == cmd.HallazgoId)` para habilitar PRE-D de `EliminarHallazgo`.

---

## 4. Precondiciones

Las precondiciones del aggregate viven en el **método de decisión `AsignarRepuesto`**. Las del handler viven antes de la invocación al aggregate. Los `Apply` son puros y no re-validan.

- **PRE-0 (capa HTTP):** capability `ejecutar-inspeccion` requerida. Si el claim está ausente → `403 Forbidden`. Mismo mecanismo que slices 1b..1e.
- **PRE-F (handler):** `InspeccionId` debe existir como stream en Marten. Si `AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)` devuelve `null` → `InspeccionNoEncontradaException` (`404 Not Found`).
- **PRE-H1 (handler — catálogo):** `SkuId` debe existir en el catálogo local `RepuestoLocal`. Si no existe → `RepuestoNoEncontradoEnCatalogoException` (`422 Unprocessable Entity`). El handler lo carga de Marten document store (proyección local ADR-004).
- **PRE-H2 (handler — compatibilidad SKU↔Parte):** `RepuestoLocal.ParteIdsCompatibles` debe contener el `ParteEquipoId` del hallazgo destino. Si no contiene → `SkuIncompatibleConParteException` (`422 Unprocessable Entity`). Decisión del modelo §12.10.12: hard error, no advertencia. El handler obtiene el hallazgo del aggregate cargado para leer su `ParteEquipoId`.
- **PRE-A (aggregate — método de decisión):** `Estado == EnEjecucion` (I-H7). Si la inspección está en cualquier otro estado → `InspeccionNoEnEjecucionException` con el estado actual (`422 Unprocessable Entity`).
- **PRE-B1 (aggregate — método de decisión):** `HallazgoId` debe existir en `_hallazgos`. Si no existe → `HallazgoNoEncontradoException` (`404 Not Found`).
- **PRE-B2 (aggregate — método de decisión):** El hallazgo no debe estar eliminado (`Eliminado == true`). Si está eliminado → `HallazgoEliminadoException` (`422 Unprocessable Entity`). Mismo helper `ObtenerHallazgoActivo` de slices 1d y 1e.
- **PRE-C (aggregate — método de decisión):** `hallazgo.AccionRequerida == RequiereIntervencion`. Si no es `RequiereIntervencion` → `HallazgoNoRequiereIntervencionException` (`422 Unprocessable Entity`). Invariante I10 del modelo histórico §12.10.12, cubierta en el aggregate como defensa en profundidad (la UI también bloquea, pero el aggregate es la fuente de verdad).
- **PRE-E (aggregate — método de decisión):** `Cantidad > 0`. Si `Cantidad <= 0` → `CantidadInvalidaException` (`422 Unprocessable Entity`).
- **PRE-D / idempotencia (aggregate — método de decisión):** Si `RepuestoId` ya existe en `_repuestos` (retry con el mismo ID) → devuelve lista vacía de eventos (sin emitir, sin lanzar). No se emite un segundo `RepuestoEstimado_v1`. Ver §7.

> **Sobre PRE-D vs excepción:** se elige el retorno silencioso (lista vacía) en lugar de `RepuestoDuplicadoException` porque la idempotencia por `RepuestoId` es un mecanismo de dedup explícito (el cliente generó el ID y lo reintenta en buenas fe). Lanzar excepción en este caso obliga al cliente a discriminar entre "ya existe — OK" y "SKU duplicado en el mismo hallazgo — error de negocio". El retorno silencioso simplifica el contrato del endpoint: `201 Created` en el primer intento, `201 Created` en el reintento (mismo cuerpo). Contrasta con PRE-B2 de `EliminarHallazgo` donde el segundo intento lanza porque el cliente no puede distinguir fácilmente qué pasó.

> **Restricción de SKU duplicado por hallazgo:** el modelo histórico §12.10.12 valida "no duplicar SKU en el mismo hallazgo" como error de negocio (`DomainException`). Este slice modela esa validación como **PRE-G** separada: si el aggregate ya tiene un repuesto activo con el mismo `SkuId` para el mismo `HallazgoId` **y distinto `RepuestoId`** → `SkuDuplicadoEnHallazgoException` (`422 Unprocessable Entity`). La distinción `distinto RepuestoId` es la que separa idempotencia de error de negocio.

> **Capa donde viven:** PRE-0 en capa HTTP; PRE-F, PRE-H1, PRE-H2 en el handler (requieren acceso a Marten document store / catálogo local); PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-E, PRE-D, PRE-G en el método de decisión del aggregate. Los `Apply(RepuestoEstimado_v1)` son puros — no re-validan ninguna de estas condiciones.

---

## 5. Invariantes tocadas

- **I-H7** (`§15.3`): editable (y con capacidad de añadir repuestos) solo si la inspección está `EnEjecucion`. Cubierta por PRE-A.
- **I-H9** (`§15.3`): eliminar hallazgo bloqueado si tiene hijos (repuestos o adjuntos activos). Este slice introduce la colección `_repuestos`, lo que **activa** la verificación real de I-H9 en `EliminarHallazgo` (el comentario placeholder de slice 1e se reemplaza con código real). El agente `green` debe completar PRE-D de `EliminarHallazgo` en este mismo slice.
- **I10 (histórico §12.10.12, sin código canónico §15.3):** solo hallazgos con `AccionRequerida = RequiereIntervencion` pueden tener repuestos asignados. Cubierta por PRE-C. Dado que §15.3 enumera I-H1..I-H11 y esta invariante no aparece con código propio allí (sí en §12.10.12 como "I10"), se propone registrarla como **I-H12** al actualizar §15.3 en el mismo PR de este slice.

  > **Propuesta de invariante nueva para §15.3:**
  > ```
  > I-H12  Repuestos solo permitidos en hallazgos con AccionRequerida = RequiereIntervencion.
  >        (defensa en profundidad — la UI bloquea el botón "Agregar repuesto"
  >         si AccionRequerida ≠ RequiereIntervencion; el aggregate lo rechaza también.)
  > ```
  > Esta propuesta debe quedar registrada en el mismo PR del slice para actualización del modelo (embargo de docs en vigor hasta 4 slices cerrados — anotar en FOLLOWUPS.md para cuando se levante el embargo).

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — repuesto asignado correctamente

**Given**
- Stream `inspeccion-{X}` contiene:
  1. `InspeccionIniciada_v1(InspeccionId=X, EquipoId=4521, ProyectoId=99, Estado→EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Manual, ParteEquipoId=77, AccionRequerida=RequiereIntervencion, TipoFallaId=3, CausaFallaId=12, AccionCorrectiva="Reemplazar sello de aceite")`
- `Estado=EnEjecucion`, `_hallazgos[G1].Eliminado=false`, `_hallazgos[G1].AccionRequerida=RequiereIntervencion`.
- `_repuestos` vacío.
- `RepuestoLocal(SkuId=501, CodigoSinco="INS-501", Descripcion="Sello de aceite motor", UnidadMedida="unidad", ParteIdsCompatibles=[77, 88])` existe en catálogo.
- Handler derivó `Unidad="unidad"` y `SkuCodigo="INS-501"` del catálogo.
- Handler generó `RepuestoId=R1`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, SkuId=501, Cantidad=2, Justificacion="Sello desgastado — requiere 2 unidades", TecnicoId="rmartinez")`.

**Then**
- Se emite exactamente un `RepuestoEstimado_v1` con `InspeccionId=X`, `HallazgoId=G1`, `RepuestoId=R1`, `SkuId=501`, `SkuCodigo="INS-501"`, `Cantidad=2`, `Justificacion="Sello desgastado — requiere 2 unidades"`, `Unidad="unidad"`, `AsignadoPor="rmartinez"`, `AsignadoEn=DateTimeOffset.UtcNow(TimeProvider)`.
- `_repuestos.Count=1`.
- `_repuestos[0].RepuestoId=R1`, `_repuestos[0].HallazgoId=G1`, `_repuestos[0].SkuId=501`, `_repuestos[0].Cantidad=2`, `_repuestos[0].Unidad="unidad"`.
- `_contribuyentes` incluye `"rmartinez"`.

### 6.2 Happy path — cantidad fraccionaria (litros)

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G2, ParteEquipoId=33, AccionRequerida=RequiereIntervencion)]`.
- `RepuestoLocal(SkuId=201, CodigoSinco="FLT-201", UnidadMedida="galón", ParteIdsCompatibles=[33])` en catálogo.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G2, RepuestoId=R2, SkuId=201, Cantidad=0.5m, Justificacion=null, TecnicoId="jperez")`.

**Then**
- Se emite `RepuestoEstimado_v1` con `Cantidad=0.5`, `Unidad="galón"`, `Justificacion=null`.
- `_repuestos.Count=1`, `_repuestos[0].Cantidad=0.5m`.

### 6.3 Idempotencia — retry con el mismo RepuestoId (PRE-D)

**Given**
- Stream con `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G1, AccionRequerida=RequiereIntervencion), RepuestoEstimado_v1(HallazgoId=G1, RepuestoId=R1, SkuId=501, Cantidad=2)]`.
- `_repuestos[R1]` ya existe.

**When**
- Segundo intento: comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, SkuId=501, Cantidad=2, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- El método de decisión devuelve lista vacía de eventos (`IReadOnlyList<object>` con `Count=0`).
- No se emite ningún evento adicional al stream.
- No se lanza excepción.
- Código HTTP `201 Created` con body del estado actual (misma respuesta que el primer intento).

### 6.4 Violación PRE-A (I-H7) — inspección no está en EnEjecucion

**Given**
- Aggregate con `Estado=Firmada` (construido con `[InspeccionIniciada_v1, InspeccionFirmada_v1]`).
- `_hallazgos[G1].AccionRequerida=RequiereIntervencion`, `_hallazgos[G1].Eliminado=false`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, SkuId=501, Cantidad=1, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- Lanza `InspeccionNoEnEjecucionException` con mensaje que incluye el estado actual (`Firmada`).
- No se emite ningún evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="I-H7"`.

### 6.5 Violación PRE-B1 — HallazgoId no existe en el aggregate

**Given**
- Aggregate `EnEjecucion` sin hallazgos (solo `InspeccionIniciada_v1`).

**When**
- Comando con `HallazgoId=G_INEXISTENTE`.

**Then**
- Lanza `HallazgoNoEncontradoException` con mensaje "El hallazgo {G_INEXISTENTE} no existe en la inspección {X}."
- No se emite evento.
- Código HTTP `404 Not Found`, `codigoError="PRE-B1"`.

### 6.6 Violación PRE-B2 — HallazgoId existe pero está eliminado

**Given**
- Stream `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G3, AccionRequerida=RequiereIntervencion), HallazgoEliminado_v1(G3)]`.
- `_hallazgos[G3].Eliminado=true`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G3, RepuestoId=R1, SkuId=501, Cantidad=1, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- Lanza `HallazgoEliminadoException` con mensaje "El hallazgo {G3} está eliminado."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="PRE-B2-ELIMINADO"`.

### 6.7 Violación PRE-C (I-H12) — hallazgo no requiere intervención

**Given**
- Stream `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G4, AccionRequerida=NoRequiereIntervencion)]`.
- `_hallazgos[G4].AccionRequerida=NoRequiereIntervencion`, `_hallazgos[G4].Eliminado=false`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G4, RepuestoId=R1, SkuId=501, Cantidad=1, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- Lanza `HallazgoNoRequiereIntervencionException` con mensaje "Solo se pueden asignar repuestos a hallazgos con AccionRequerida=RequiereIntervencion. El hallazgo {G4} tiene AccionRequerida=NoRequiereIntervencion."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="I-H12"`.

### 6.8 Violación PRE-E — Cantidad igual o menor a cero

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].AccionRequerida=RequiereIntervencion`, `_hallazgos[G1].Eliminado=false`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, SkuId=501, Cantidad=0, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- Lanza `CantidadInvalidaException` con mensaje "Cantidad debe ser mayor que cero."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="PRE-E"`.

### 6.9 Violación PRE-G — SKU ya asignado al mismo hallazgo con distinto RepuestoId (error de negocio)

**Given**
- Stream `[InspeccionIniciada_v1, HallazgoRegistrado_v1(G1, AccionRequerida=RequiereIntervencion), RepuestoEstimado_v1(HallazgoId=G1, RepuestoId=R1, SkuId=501)]`.
- `_repuestos` contiene un repuesto con `HallazgoId=G1`, `SkuId=501`, `RepuestoId=R1`.

**When**
- Comando con `HallazgoId=G1`, `SkuId=501`, **`RepuestoId=R2`** (distinto ID — no es retry, es error de usuario intentando agregar el mismo SKU dos veces).

**Then**
- Lanza `SkuDuplicadoEnHallazgoException` con mensaje "El SKU 501 ya fue estimado en el hallazgo {G1}. Edita o elimina el repuesto existente antes de volver a agregar."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="PRE-G"`.

### 6.10 Violación PRE-H1 (handler) — SkuId no existe en catálogo local

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].AccionRequerida=RequiereIntervencion`.
- El catálogo local `RepuestoLocal` no tiene ningún repuesto con `SkuId=9999`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, SkuId=9999, Cantidad=1, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- Handler lanza `RepuestoNoEncontradoEnCatalogoException` antes de invocar el aggregate.
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="PRE-H1"`.

### 6.11 Violación PRE-H2 (handler) — SKU no compatible con la parte del hallazgo

**Given**
- Aggregate `EnEjecucion`. `_hallazgos[G1].AccionRequerida=RequiereIntervencion`, `_hallazgos[G1].ParteEquipoId=77`.
- `RepuestoLocal(SkuId=888, CodigoSinco="INS-888", ParteIdsCompatibles=[10, 20])` — no incluye `77`.

**When**
- Comando `AsignarRepuesto(InspeccionId=X, HallazgoId=G1, RepuestoId=R1, SkuId=888, Cantidad=1, Justificacion=null, TecnicoId="rmartinez")`.

**Then**
- Handler lanza `SkuIncompatibleConParteException` con mensaje "El SKU INS-888 no está catalogado como compatible con la parte 77 del hallazgo. Si crees que es un error, escala al admin del catálogo de inventario."
- No se emite evento.
- Código HTTP `422 Unprocessable Entity`, `codigoError="PRE-H2"`.

### 6.12 Violación PRE-F — InspeccionId no existe

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando con `InspeccionId=Z`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- No se emite evento.
- Código HTTP `404 Not Found`, `codigoError="PRE-F"`.

### 6.13 DoD especial — levantar skip del test §6.7 de EliminarHallazgoTests.cs (FOLLOWUPS #21)

Este escenario forma parte del DoD de este slice. La precondición PRE-D de `EliminarHallazgo` estaba comentada (`// PRE-D / I-H9: verificar cuando existan slices de repuestos/adjuntos`). Ahora que `_repuestos` existe y `Apply(RepuestoEstimado_v1)` añade repuestos a la colección, la verificación real puede implementarse.

**Given**
- Fixture `StreamConHallazgoConRepuestoActivo()` existe y emite un stream con:
  1. `InspeccionIniciada_v1(InspeccionId=X)`
  2. `HallazgoRegistrado_v1(HallazgoId=G5, AccionRequerida=RequiereIntervencion)`
  3. `RepuestoEstimado_v1(HallazgoId=G5, RepuestoId=R5, SkuId=501)`
- `_repuestos` contiene un repuesto activo con `HallazgoId=G5`.
- El test `EliminarHallazgo_con_hallazgo_con_repuestos_activos_lanza_HallazgoTieneHijosActivosException` en `EliminarHallazgoTests.cs` se ejecuta sin `[Fact(Skip=...)]`.

**When**
- Comando `EliminarHallazgo(InspeccionId=X, HallazgoId=G5, Motivo="Error de registro", TecnicoId="rmartinez")`.

**Then**
- El método de decisión `EliminarHallazgo` verifica `_repuestos.Any(r => r.HallazgoId == G5)` → `true`.
- Lanza `HallazgoTieneHijosActivosException` (PRE-D / I-H9 del slice 1e).
- El test pasa en verde.

> **Instrucción para el agente `red`:**
> 1. Implementar `StreamConHallazgoConRepuestoActivo()` en los fixtures (`HallazgoFixtures.cs` o `Fixtures.cs`) usando `RepuestoEstimado_v1`.
> 2. Quitar el `[Fact(Skip=...)]` del test §6.7 de `EliminarHallazgoTests.cs` (followup #21).
> 3. Actualizar el método de decisión `EliminarHallazgo` en `Inspeccion.cs` para reemplazar el comentario placeholder con la verificación real: `if (_repuestos.Any(r => r.HallazgoId == cmd.HallazgoId)) throw new HallazgoTieneHijosActivosException(...)`.
> 4. Todos los tests de este slice y el test levantado del slice 1e deben pasar en verde al cerrar el slice.

### 6.14 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos previos).
- Lista de eventos en orden causal:
  1. `InspeccionIniciada_v1(InspeccionId=X, EquipoId=4521, Estado→EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=G1, Origen=Manual, ParteEquipoId=77, AccionRequerida=RequiereIntervencion, TipoFallaId=3, CausaFallaId=12, AccionCorrectiva="Reemplazar sello")`
  3. `RepuestoEstimado_v1(HallazgoId=G1, RepuestoId=R1, SkuId=501, SkuCodigo="INS-501", Cantidad=2, Justificacion="Sello desgastado", Unidad="unidad", AsignadoPor="rmartinez", AsignadoEn=T1)`

**When**
- Se reproyectan los tres eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- `Estado=EnEjecucion`.
- `_hallazgos.Count=1`, `_hallazgos[G1].AccionRequerida=RequiereIntervencion`, `_hallazgos[G1].Eliminado=false`.
- `_repuestos.Count=1`.
- `_repuestos[0].RepuestoId=R1`, `_repuestos[0].HallazgoId=G1`, `_repuestos[0].SkuId=501`, `_repuestos[0].Cantidad=2`, `_repuestos[0].Unidad="unidad"`.
- `_contribuyentes` incluye `"rmartinez"`.
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al que produce el método de decisión seguido de `Apply` in-process.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente envía `X-Client-Command-Id: <UUIDv7>` como header HTTP por cada tap "Agregar repuesto". El header se mapea a `MessageId` Wolverine. Si Wolverine detecta el `MessageId` ya procesado (envelope dedup), devuelve la respuesta original sin re-ejecutar el handler. No se emite un segundo `RepuestoEstimado_v1`.

**Idempotencia en el aggregate por `RepuestoId` (PRE-D):**

El cliente genera `RepuestoId` como `Guid` antes de enviar el comando (UUIDv7 recomendado). Si el comando llega dos veces con el mismo `RepuestoId` (retry HTTP, red inestable) y no hay dedup de Wolverine activo, el método de decisión detecta que `_repuestos.Any(r => r.RepuestoId == cmd.RepuestoId)` → `true` y devuelve lista vacía de eventos. El endpoint devuelve `201 Created` con el estado actual. El cliente no distingue entre primera vez y retry — ambas reciben la misma respuesta. Esta es la diferencia clave con PRE-G (SKU duplicado con distinto `RepuestoId`): ese sí es error de negocio.

**Orden de verificación en el método de decisión:**

PRE-D (idempotencia por `RepuestoId`) se evalúa **antes** de PRE-G (SKU duplicado). Si el `RepuestoId` ya existe → retorno silencioso. Solo si el `RepuestoId` es nuevo → verificar si el SKU ya existe en el hallazgo.

**Sin POST a Sinco:** este comando no cruza al ERP. ADR-006 (outbox para integraciones ERP) no aplica en este slice. Los repuestos se persisten localmente y son consumidos por `CerrarInspeccionSaga` al momento del cierre.

**Atomicidad:** un único `IDocumentSession.SaveChangesAsync()` persiste `RepuestoEstimado_v1` y actualiza las proyecciones afectadas.

> **Nota sobre dedup real de Wolverine:** el mecanismo concreto de dedup (ADR-008) sigue siendo followup #15. Este slice sigue el mismo patrón que slices anteriores sin avanzar ese followup.

---

## 8. Impacto en proyecciones / read models

### 8.1 `DetalleInspeccionView` (§15.12.1) — añadir repuestos al hallazgo

`DetalleInspeccionView` consume `RepuestoEstimado_v1`. Al recibir el evento, la proyección añade el repuesto al hallazgo correspondiente dentro del documento:
- Añade entrada en `Hallazgo.Repuestos[]` con `RepuestoId`, `SkuId`, `SkuCodigo`, `Cantidad`, `Unidad`, `Justificacion`, `AsignadoPor`, `AsignadoEn`.

La proyección `DetalleInspeccionView` no está implementada aún — el slice documenta el contrato esperado para cuando se implemente.

### 8.2 `AuditoriaInspeccionesView` (§15.12.2) — consume RepuestoEstimado_v1

Según §15.12.2 del modelo, esta proyección consume `RepuestoEstimado_v1`, `RepuestoActualizado_v1`, `RepuestoRemovido_v1` para construir el historial de repuestos de la inspección. Sin detalles adicionales en este slice — el contrato completo llega con los slices `ActualizarRepuesto` y `RemoverRepuesto`.

### 8.3 `BandejaInspeccionesPendientesOTView` (§15.12.5) — no impactada

El agregado de repuestos no cambia el estado de la inspección ni el conteo de hallazgos con `RequiereIntervencion`. Sin cambio en este slice.

### 8.4 `BandejaTecnicoView` (§15.12.3) — no impactada

Muestra el estado de la inspección, no el detalle de repuestos. Sin cambio en este slice.

### 8.5 `InspeccionAbiertaPorEquipoView` (§15.12.6) — no impactada

Solo reacciona a eventos de lifecycle. Sin cambio en este slice.

---

## 9. Impacto en endpoints HTTP

**Endpoint nuevo:** `POST /api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos`

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008).
- `Authorization` heredado del host PWA (ADR-002 tentativo).

**Request DTO (`AsignarRepuestoRequest`):**

```json
{
  "repuestoId": "01950000-0000-7000-0000-000000000001",
  "skuId": 501,
  "cantidad": 2.0,
  "justificacion": "Sello desgastado — requiere 2 unidades"
}
```

> `InspeccionId` y `HallazgoId` viajan en el path. `TecnicoId` se extrae del JWT en la capa API (ADR-002 tentativo — mock `const string tecnicoId = "rmartinez"` consistente con slices anteriores hasta que ADR-002 esté resuelto — followup #14). `SkuCodigo` y `Unidad` los resuelve el handler del catálogo local. `AsignadoEn` es inyectado por `TimeProvider`.

**Response `201 Created` (happy path):**

```json
{
  "repuestoId": "01950000-0000-7000-0000-000000000001",
  "skuId": 501,
  "skuCodigo": "INS-501",
  "cantidad": 2.0,
  "unidad": "unidad",
  "justificacion": "Sello desgastado — requiere 2 unidades",
  "asignadoEn": "2026-05-07T14:32:00+00:00"
}
```

Se usa `201 Created` con body porque el recurso fue creado y el `RepuestoId` + datos derivados (`SkuCodigo`, `Unidad`, `AsignadoEn`) son útiles al cliente para actualizar su estado local sin necesidad de un GET subsiguiente.

**Idempotencia en la respuesta:** si el mismo `RepuestoId` ya existía (PRE-D — retry), el endpoint devuelve `201 Created` con el mismo body. El cliente no distingue primera vez de retry.

**Códigos de error:**

| Escenario | Código HTTP | `codigoError` |
|---|---|---|
| Capability ausente (PRE-0) | `403 Forbidden` | `"PRE-0"` |
| InspeccionId no existe (PRE-F) | `404 Not Found` | `"PRE-F"` |
| HallazgoId no existe (PRE-B1) | `404 Not Found` | `"PRE-B1"` |
| HallazgoId eliminado (PRE-B2) | `422 Unprocessable Entity` | `"PRE-B2-ELIMINADO"` |
| Inspección no en EnEjecucion (PRE-A / I-H7) | `422 Unprocessable Entity` | `"I-H7"` |
| Hallazgo no requiere intervención (PRE-C / I-H12) | `422 Unprocessable Entity` | `"I-H12"` |
| Cantidad ≤ 0 (PRE-E) | `422 Unprocessable Entity` | `"PRE-E"` |
| SKU duplicado en hallazgo con distinto ID (PRE-G) | `422 Unprocessable Entity` | `"PRE-G"` |
| SkuId no existe en catálogo (PRE-H1) | `422 Unprocessable Entity` | `"PRE-H1"` |
| SKU incompatible con la parte (PRE-H2) | `422 Unprocessable Entity` | `"PRE-H2"` |

**Rol/permiso requerido:** capability `ejecutar-inspeccion` con el proyecto de la inspección asignado (heredado del host PWA — ADR-002 tentativo).

---

## 10. Impacto en SignalR / push (si aplica)

**No aplica en este slice.** `RepuestoEstimado_v1` no está en el catálogo de eventos SignalR (ADR-005, §14 del modelo). El push SignalR está reservado para eventos de cierre del ciclo de inspección (`OTGenerada`, `InspeccionCerradaSinOT`, `OTGeneracionFallida`, `AdjuntoPdfFallido`). La asignación de un repuesto es una operación local del técnico — el resultado es visible inmediatamente en su pantalla sin notificación en tiempo real a otras partes en MVP.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**No aplica en este slice.** `AsignarRepuesto` es puramente local al módulo. No hay llamadas salientes al ERP on-prem en el momento del comando. La consulta de catálogo `RepuestoLocal` es una lectura del Marten document store local (proyección sincronizada en app-open por ADR-004) — no una llamada HTTP en tiempo real a Sinco.

La integración con Sinco ocurre en el momento del cierre: `CerrarInspeccionSaga` consolida `_repuestos` del aggregate, agrupa por `SkuId`, suma cantidades y publica el BOM consolidado en el POST de OT correctiva a MYE (slice posterior).

---

## 12. Preguntas abiertas

Todas resueltas antes de entregar esta spec.

- [x] **¿`Justificacion` es obligatoria?** No. Según §12.10.12 del modelo histórico, `Justificacion` era obligatoria en la definición original. Sin embargo, en el contexto de un repuesto estimado durante la inspección, la justificación es información adicional útil pero no crítica para el cierre de OT. El campo es `string?` — opcional. Si el squad quiere hacerla obligatoria, se requiere decisión explícita del usuario.

  > **Asunción tomada:** campo opcional (`string?`). Justificación: el consultor mecánico no marcó esta decisión como bloqueante en su brief (§6 del brief) y el modelo histórico §12.10.12 la marcó como "obligatoria" en la primera versión pero no hay invariante canónica en §15.3 que lo sostenga. Si emerge la necesidad, es un cambio aditivo (validación PRE adicional).

- [x] **¿PRE-D (idempotencia por `RepuestoId`) devuelve vacío o lanza excepción?** Devuelve lista vacía (no-op silencioso). Decisión documentada en §4 y §7. El cliente recibe `201 Created` idéntico. La clave es que el `RepuestoId` fue generado por el cliente — el retry es legítimo por definición.

- [x] **¿SKU duplicado por hallazgo (distinto `RepuestoId`) es error o también no-op?** Es error de negocio (`SkuDuplicadoEnHallazgoException` — PRE-G, `422`). Un SKU duplicado con distinto `RepuestoId` no es retry: es el usuario intentando agregar el mismo insumo dos veces al mismo hallazgo. La UI debe bloquear esto, pero el aggregate es defensa en profundidad.

- [x] **¿El `Apply(RepuestoEstimado_v1)` hace hard delete o soft delete en `_repuestos`?** Para este slice (asignar), solo añade. Los slices `RemoverRepuesto` y `ActualizarRepuesto` definirán la semántica de eliminación. Según §12.10.13 del modelo histórico, `RemoverRepuesto` hace hard delete del estado (`_repuestos.RemoveAll`), distinto del soft delete de hallazgos. Esta decisión se confirma en el spec del slice correspondiente.

- [x] **¿Este slice introduce el record `Repuesto` (value object de estado)?** Sí. Definido en §3. El agente `green` lo añade como tipo en `Inspeccion.cs` (o en un archivo nuevo `Repuesto.cs` dentro del namespace `Inspecciones.Domain.Inspecciones`).

- [x] **¿La invariante I-H12 (solo `RequiereIntervencion` admite repuestos) debe actualizarse en §15.3?** Sí, propuesta en §5. Embargo de docs vigente — se anota en FOLLOWUPS.md.

- [x] **¿`SkuCodigo` viene del comando o del catálogo?** Del catálogo (`RepuestoLocal.CodigoSinco`). El handler lo resuelve antes de llamar al aggregate. El evento lo persiste para legibilidad en reporting/stream sin joins posteriores.

- [x] **¿El timestamp es `DateTimeOffset` o `DateTime`?** `DateTimeOffset` — convención vigente del CLAUDE.md. El modelo histórico §12.10.12 usa `DateTime EstimadoEn`; la fuente de verdad es CLAUDE.md.

---

## 13. Checklist pre-firma

- [ ] Todas las precondiciones (PRE-0, PRE-F, PRE-H1, PRE-H2, PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-E, PRE-D, PRE-G) tienen escenario Given/When/Then en §6 (6.12→PRE-F, 6.4→PRE-A/I-H7, 6.5→PRE-B1, 6.6→PRE-B2, 6.7→PRE-C/I-H12, 6.8→PRE-E, 6.3→PRE-D, 6.9→PRE-G, 6.10→PRE-H1, 6.11→PRE-H2).
- [ ] Invariantes tocadas (I-H7, I-H9, I-H12) tienen escenario de cobertura (6.4→I-H7, 6.13→I-H9, 6.7→I-H12).
- [ ] Happy paths presentes: 6.1 (caso base, Cantidad entera, Justificacion presente), 6.2 (Cantidad fraccionaria, Justificacion null).
- [ ] Escenario de idempotencia por `RepuestoId` (PRE-D) presente (6.3) — retorno silencioso documentado.
- [ ] Escenario de SKU duplicado con distinto `RepuestoId` (PRE-G) presente (6.9) — error de negocio documentado.
- [ ] Escenario de rebuild desde stream presente (6.14) — verifica que `Apply(RepuestoEstimado_v1)` es puro y `_repuestos` se reconstruye correctamente.
- [ ] DoD especial documentado (6.13): levantar skip del test §6.7 de `EliminarHallazgoTests.cs` (followup #21) — instrucciones explícitas para agente `red`.
- [ ] §7 Idempotencia decidida: envelope dedup Wolverine por `X-Client-Command-Id` (ADR-008); PRE-D en aggregate por `RepuestoId` como segunda línea de defensa; PRE-G distingue retry legítimo de error de negocio. Followup #15 sigue pendiente.
- [ ] §10 SignalR marcado explícitamente "no aplica" con justificación.
- [ ] §11 Adapters Sinco marcado explícitamente "no aplica" con justificación.
- [ ] §12 Preguntas abiertas: todas respondidas.
- [ ] Estado interno nuevo (`_repuestos`, record `Repuesto`) documentado en §3 con forma concreta para el agente `green`.
- [ ] Propuesta de invariante I-H12 en §5 para actualización del modelo (pendiente levantamiento de embargo de docs — anotar en FOLLOWUPS.md).