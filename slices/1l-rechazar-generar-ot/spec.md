# Slice 1l — RechazarGenerarOT

**Autor:** domain-modeler
**Fecha:** 2026-05-08
**Estado:** draft
**Agregado afectado:** `Inspeccion` (aggregate unificado — aplica a `TipoInspeccion.Tecnica` y `TipoInspeccion.Monitoreo`).
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §17` (ADR-007) — fuente de verdad. Define el command record `RechazarGenerarOT`, los dos eventos emitidos (`GeneracionOTRechazada_v1` → `InspeccionCerradaSinOT_v1`), el discriminador `MotivoCierreSinOT`, el state interno (`OTRechazada`, `MotivoRechazoOT`) y las invariantes I-F6 (precondiciones del rechazo).
- `01-modelo-dominio.md §15.7` — catálogo de invariantes I-F* (I-F4 precondiciones de `GenerarOT`, I-F6 precondiciones de `RechazarGenerarOT`).
- `01-modelo-dominio.md §15.12.6` — `InspeccionAbiertaPorEquipoView` queda libre al recibir `InspeccionCerradaSinOT_v1`.
- `slices/1k-generar-ot/spec.md` — slice hermano inmediato. Define precondiciones I-F4 simétricas, patrón de escenarios, shape de eventos stub.
- `slices/1k-generar-ot/review-notes.md` — followup FU-31: el shape del modelo §17 usa `DateTime`; la convención del módulo exige `DateTimeOffset`. La misma corrección aplica a los eventos de este slice.
- `src/Inspecciones.Domain/Inspecciones/GeneracionOTRechazada_v1.cs` — stub creado en 1k: `(InspeccionId, RechazadoPor, MotivoRechazo, RechazadaEn: DateTimeOffset)`. Este slice lo eleva a canónico.
- `src/Inspecciones.Domain/Inspecciones/InspeccionCerradaSinOT_v1.cs` — stub creado en 1k: `(InspeccionId, CerradoPor, CerradaEn)`. Este slice **extiende** el shape con el discriminador `MotivoCierreSinOT` (ADR-007 §17), cerrando parcialmente FU-31.
- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — ya tiene `bool OTRechazada`, `Apply(GeneracionOTRechazada_v1)` (stub: pone `OTRechazada = true`), `Apply(InspeccionCerradaSinOT_v1)` (stub: pone `Estado = CerradaSinOT`). Este slice los eleva a implementación completa y añade `string? MotivoRechazoOT`.
- ADR-006 (`§16`) — no aplica: el handler no hace POST a ningún ERP.
- ADR-005 (`§14`) — push SignalR: el evento `OTRechazada` lo emite un proyector lateral sobre `GeneracionOTRechazada_v1`; no es responsabilidad directa de este slice (ver §10).
- ADR-008 (`00-investigacion-mercado.md §9.16`) — `X-Client-Command-Id` mapeado a `MessageId` Wolverine; idempotencia end-to-end.
- `roadmap.md §3.42c` — entrada `POST /inspecciones/{id}/rechazar-ot` en roadmap Fase 3.

---

## 1. Intención

Un usuario con capability `generar-ot` (típicamente jefe de campo, supervisor o contralor) necesita poder rechazar explícitamente la generación de una Orden de Trabajo para una inspección firmada que tiene al menos un hallazgo con `AccionRequerida = RequiereIntervencion`, cuando considera que no es pertinente o oportuno crear la OT. El rechazo cierra la inspección de forma definitiva en estado `CerradaSinOT`, liberando el equipo para una nueva inspección. El motivo del rechazo queda auditado en el stream. Sin este comando, una inspección firmada con `RequiereIntervencion` quedaría bloqueada indefinidamente esperando aprobación de OT (origen: observación de Sergio 2026-04-30, ADR-007 §17).

---

## 2. Comando

```csharp
public sealed record RechazarGenerarOT(
    Guid         InspeccionId,
    string       Motivo,                                 // texto libre; min 10 chars (trimmed); obligatorio
    string       RechazadoPor,                           // userId del aprobador del host PWA — opaco para el dominio
    IReadOnlyCollection<string> Capabilities             // del contexto del host PWA; debe contener "generar-ot"
) : ICommand;
```

**Notas sobre el payload:**

> **`Motivo`:** mínimo 10 caracteres después de trim. No se define máximo en el modelo canónico §17; este spec propone un máximo de 500 chars (operativo y razonable para un campo de texto libre de auditoría). Sin máximo definido en §17 — ver Decisión D-1.
>
> **`RechazadoPor`:** el handler extrae el userId del JWT del host PWA y lo pasa como `RechazadoPor`; el dominio lo recibe como string opaco. Mismo patrón que `SolicitadaPor` en `GenerarOT`.
>
> **Sin `Prioridad`, `Responsable`, ni campos de saga:** el rechazo no genera OT ni invoca MYE; el payload es mínimo. Solo el motivo y la identidad del aprobador son necesarios.

**Claims del aprobador** (integrados en el comando):

```csharp
// Capability requerida: "generar-ot" (misma que GenerarOT — ADR-007 §17).
// Verificación en capa HTTP (middleware) antes de invocar el handler.
// El método de decisión RechazarOT del aggregate no verifica capabilities directamente.
```

**DTOs de capa HTTP** (fuera del dominio):

```csharp
// Ruta: POST /api/v1/inspecciones/{inspeccionId}/rechazar-ot
public sealed record RechazarGenerarOTRequest(
    string Motivo);

public sealed record RechazarGenerarOTResult(
    Guid           InspeccionId,
    string         Estado,          // "CerradaSinOT"
    DateTimeOffset RechazadaEn,
    string         RechazadoPor,
    string         Motivo);
```

---

## 3. Evento(s) emitido(s)

Este slice emite **exactamente dos eventos** en todos los casos de éxito, en un único `SaveChangesAsync` (atomicidad Marten — un único append al stream). El orden refleja la causalidad lógica.

| # | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `GeneracionOTRechazada_v1` | Ver shape a continuación | Siempre que las precondiciones I-F6 se cumplan — audit del rechazo |
| 2 | `InspeccionCerradaSinOT_v1` | Ver shape a continuación | Inmediatamente después del anterior — cierre terminal |

> **Causalidad:** el rechazo ocurre primero (se registra el acto y el motivo), luego el aggregate transiciona a estado terminal `CerradaSinOT`. Este orden es el mismo que describe ADR-007 §17.

### 3.1 `GeneracionOTRechazada_v1` — shape canónico (elevado de stub)

El stub en `1k` tenía `(InspeccionId, RechazadoPor, MotivoRechazo, RechazadaEn)`. Este slice lo eleva a canónico con la misma estructura y nombre de campo alineado con el comando (`Motivo` vs `MotivoRechazo` — ver Decisión D-2).

```csharp
public sealed record GeneracionOTRechazada_v1(
    Guid           InspeccionId,
    string         Motivo,          // texto auditado del rechazo
    string         RechazadoPor,    // userId opaco del aprobador
    DateTimeOffset RechazadaEn);    // DateTimeOffset — TimeProvider.GetUtcNow() en el handler
```

> **`DateTimeOffset` vs `DateTime`:** el modelo §17 usa `DateTime RechazadaEn`. La convención del módulo (CLAUDE.md) exige `DateTimeOffset`. Se aplica la misma corrección que en slices anteriores (FU-31). Se propone corrección al modelo §17 en el PR de este slice.
>
> **Campo `Motivo` vs `MotivoRechazo`:** el stub del slice 1k usaba `MotivoRechazo`; el command record del §17 canónico usa `Motivo`. Este spec alinea el nombre del campo del evento con el nombre del parámetro del comando (`Motivo`) para consistencia. El stub de 1k usa `MotivoRechazo` — el agente `red` debe renombrar el campo en el evento al elevar el stub. Ver Decisión D-2.

### 3.2 `InspeccionCerradaSinOT_v1` — shape canónico extendido (cierra FU-31 parcialmente)

El stub en `1k` tenía `(InspeccionId, CerradoPor, CerradaEn)`. Este slice **extiende** el shape con el discriminador `MotivoCierreSinOT` y reemplaza `CerradoPor` (que no corresponde a una persona en el caso automático de la saga) por la semántica correcta.

```csharp
public enum MotivoCierreSinOT
{
    AutomaticoSinIntervencion,   // saga CerrarInspeccionSaga al firmar (no había RequiereIntervencion)
    RechazadaPorAprobador        // RechazarGenerarOT — este slice
}

public sealed record InspeccionCerradaSinOT_v1(
    Guid                InspeccionId,
    MotivoCierreSinOT   MotivoCierre,    // discriminador del motivo del cierre sin OT
    DateTimeOffset      CerradaEn);      // DateTimeOffset — TimeProvider.GetUtcNow() en el handler
```

> **Eliminación de `CerradoPor`:** el stub del slice 1k tenía `CerradoPor: string`. El campo no es correcto semánticamente para el caso automático (la saga no tiene una persona; solo el caso de rechazo tiene aprobador). El aprobador ya quedó auditado en `GeneracionOTRechazada_v1.RechazadoPor`. `InspeccionCerradaSinOT_v1` queda sin campo de persona — el motivo del cierre es suficiente para derivar qué proceso lo emitió. Ver Decisión D-3.
>
> **Impacto en stub de 1k:** el agente `red` de este slice debe actualizar el shape del event record `InspeccionCerradaSinOT_v1` (añadir `MotivoCierre`, eliminar `CerradoPor`). El `Apply(InspeccionCerradaSinOT_v1)` existente en `Inspeccion.cs` no cambia de comportamiento: sigue poniendo `Estado = CerradaSinOT`. El slice 1k no tiene tests que dependan de `CerradoPor` — confirmar en `GenerarOTTests.cs` y `CasoDeUso.cs`.

### 3.3 Impacto en estado interno del aggregate

```csharp
// Apply(GeneracionOTRechazada_v1) — mutación pura (eleva stub de 1k):
// OTRechazada    = true;
// MotivoRechazoOT = e.Motivo;    // nuevo campo; el stub no lo tenía

// Apply(InspeccionCerradaSinOT_v1) — mutación pura (ya implementada en stub de 1k, sin cambio):
// Estado = EstadoInspeccion.CerradaSinOT;
```

Las precondiciones I-F6 viven en el método de decisión `RechazarOT(...)`. Ningún `Apply` lanza excepción.

> **Campo nuevo `MotivoRechazoOT`:** el stub de 1k tenía `bool OTRechazada` sin el motivo. Este slice añade `string? MotivoRechazoOT` al aggregate para que proyecciones / sagas puedan leerlo del estado materializado sin re-leer el stream. El campo es `null` hasta que `Apply(GeneracionOTRechazada_v1)` lo ponga.

---

## 4. Precondiciones

Las precondiciones se clasifican por la capa donde viven. Los `Apply` son puros — nunca re-validan.

### Capa HTTP (antes de invocar el handler)

- **PRE-1 (capability `generar-ot`):** `Capabilities.Contains("generar-ot")`. Sin ella, el endpoint devuelve `403 Forbidden` sin invocar el aggregate. La misma capability que `GenerarOT` — quien puede aprobar puede rechazar (ADR-007 §17). `generar-ot` es **independiente** de `ejecutar-inspeccion`.

### Capa handler (antes de invocar el método de decisión)

- **PRE-2 (inspección existe):** el handler carga el aggregate con `IDocumentSession.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId)`. Si es `null` → `InspeccionNoEncontradaException` (`404 Not Found`).

### Validación de input (antes de invocar el método de decisión — reglas del comando)

- **PRE-3 (motivo no vacío y ≥10 chars — I-F6):** `cmd.Motivo.Trim().Length >= 10`. Si no cumple → `MotivoRechazoInvalidoException` (`422 Unprocessable Entity`). Se valida sobre el valor trimmed; la validación puede vivir en el handler o al inicio del método de decisión.

### Método de decisión del aggregate (`Inspeccion.RechazarOT`) — invariantes I-F6

- **PRE-4 (estado Firmada — I-F6.a):** `Estado == EstadoInspeccion.Firmada`. Si el estado es `EnEjecucion`, `CerradaSinOT`, `Cerrada`, `CierrePendienteOT` o `Cancelada` → `InspeccionNoFirmadaException` (`422 Unprocessable Entity`). Nota: el estado `Firmada` incluye el estado derivado `EsperandoAprobacionOT` (§15.12.5 — derivado por la proyección, no persistido en el aggregate).
- **PRE-5 (≥1 hallazgo activo con RequiereIntervencion — I-F6.b):** `Hallazgos.Any(h => !h.Eliminado && h.AccionRequerida == AccionRequerida.RequiereIntervencion)`. Si ningún hallazgo cumple → `SinHallazgosConIntervencionException` (`422 Unprocessable Entity`). Si no hay hallazgos con `RequiereIntervencion`, la saga `CerrarInspeccionSaga` ya habría cerrado la inspección automáticamente al firmar — el rechazo no aplica.
- **PRE-6 (OT no solicitada — I-F6.c):** `!OTSolicitada`. Si ya existe un `OTSolicitada_v1` en el stream → `OTYaSolicitadaException` (`409 Conflict`). Una vez enviada la solicitud al ERP, cancelar requiere coordinación cross-team — fuera de alcance MVP (confirmado ADR-007 §17).
- **PRE-7 (OT no rechazada previamente — I-F6.d):** `!OTRechazada`. Si ya existe un `GeneracionOTRechazada_v1` en el stream → `OTYaRechazadaException` (`409 Conflict`). No se acepta doble rechazo.

> **Capa de validación:** PRE-1 en capa HTTP; PRE-2 y PRE-3 en el handler; PRE-4 a PRE-7 en el método de decisión `Inspeccion.RechazarOT`. Ningún `Apply` re-valida. Todas las condiciones de I-F6 (§17) están cubiertas.

---

## 5. Invariantes tocadas

- **I-F6 (RechazarGenerarOT — precondiciones):** cubierta íntegramente por PRE-4 (estado Firmada), PRE-5 (≥1 RequiereIntervencion activo), PRE-6 (!OTSolicitada), PRE-7 (!OTRechazada). Todas las condiciones de I-F6 mapean a precondiciones del método de decisión.
- **I-F4.d (simetría con GenerarOT):** PRE-6 de este slice es el espejo de PRE-6 del slice 1k. Si `GenerarOT` prohíbe solicitar una OT ya rechazada, `RechazarGenerarOT` prohíbe rechazar una OT ya solicitada. La invariante I-F6 incluye ambos sentidos.
- **I-F1 (inmutabilidad post-firma):** el aggregate está en estado `Firmada`; los hallazgos son inmutables. PRE-4 confirma el estado. Las validaciones de PRE-5 se hacen sobre el estado congelado del stream.
- **Estado terminal `CerradaSinOT`:** tras emitir `InspeccionCerradaSinOT_v1`, el aggregate entra en estado terminal. No hay comandos válidos sobre una inspección `CerradaSinOT`. Este slice no define invariante nueva — el estado terminal ya está en el modelo §15.

### Invariantes existentes no tocadas

- I-H*, I-I*, V-F*, I-F2, I-F3, I-F5: no aplican — el aggregate ya está firmado y los hallazgos son inmutables.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — rechazo con dictamen NoPuedeOperar y hallazgo RequiereIntervencion

**Given**
- Stream con eventos:
  1. `InspeccionIniciada_v1` (EquipoId=42, TecnicoId="carlos.ruiz", Tipo=Tecnica)
  2. `HallazgoRegistrado_v1` (HallazgoId=`h1`, AccionRequerida=`RequiereIntervencion`, Eliminado=false, TipoFallaId=1, CausaFallaId=2)
  3. `DiagnosticoEmitido_v1` (DiagnosticoFinal="Falla estructural en brazo hidráulico")
  4. `DictamenEstablecido_v1` (Dictamen=NoPuedeOperar)
  5. `InspeccionFirmada_v1` (FirmadoPor="carlos.ruiz", Estado=Firmada)
- `aggregate.OTSolicitada == false`, `aggregate.OTRechazada == false`, `aggregate.Estado == Firmada`.
- `TimeProvider` retorna `2026-05-08T15:00:00Z`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="El equipo será dado de baja definitiva en 10 días", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Se emiten exactamente **dos** eventos en este orden:
  1. `GeneracionOTRechazada_v1(InspeccionId=X, Motivo="El equipo será dado de baja definitiva en 10 días", RechazadoPor="jefe.campo.01", RechazadaEn=2026-05-08T15:00:00Z)`.
  2. `InspeccionCerradaSinOT_v1(InspeccionId=X, MotivoCierre=RechazadaPorAprobador, CerradaEn=2026-05-08T15:00:00Z)`.
- Estado post-comando: `aggregate.OTRechazada == true`, `aggregate.MotivoRechazoOT == "El equipo será dado de baja definitiva en 10 días"`, `aggregate.Estado == CerradaSinOT`.
- Handler retorna `RechazarGenerarOTResult(InspeccionId=X, Estado="CerradaSinOT", RechazadaEn=2026-05-08T15:00:00Z, RechazadoPor="jefe.campo.01", Motivo="El equipo será dado de baja definitiva en 10 días")`.
- Capa API devuelve `200 OK` (la inspección ya está cerrada — no es asíncrono como GenerarOT).

### 6.2 Happy path — rechazo con dictamen ConRestriccion y hallazgo RequiereIntervencion

**Given**
- Mismo setup base que 6.1 excepto:
  - `DictamenEstablecido_v1` (Dictamen=ConRestriccion)
  - Hallazgo h1 activo con `RequiereIntervencion`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="Presupuesto no disponible hasta el próximo trimestre", RechazadoPor="supervisor.01", Capabilities=["generar-ot"])`.

**Then**
- Se emiten exactamente **dos** eventos en orden causal:
  1. `GeneracionOTRechazada_v1(InspeccionId=X, Motivo="Presupuesto no disponible hasta el próximo trimestre", RechazadoPor="supervisor.01", RechazadaEn=...)`.
  2. `InspeccionCerradaSinOT_v1(InspeccionId=X, MotivoCierre=RechazadaPorAprobador, CerradaEn=...)`.
- `aggregate.Estado == CerradaSinOT`.
- Capa API devuelve `200 OK`.

### 6.3 Violación PRE-1 — capability `generar-ot` ausente (403)

**Given**
- Aggregate en estado `Firmada` con ≥1 hallazgo `RequiereIntervencion`.
- Claims del usuario: `Capabilities=["ejecutar-inspeccion"]` (sin `generar-ot`).

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="Motivo de ejemplo suficientemente largo", RechazadoPor="carlos.ruiz", Capabilities=["ejecutar-inspeccion"])`.

**Then**
- Middleware de autorización lanza excepción 403 antes de llegar al handler.
- Sin evento. `aggregate.Estado` permanece `Firmada`, `aggregate.OTRechazada` permanece `false`.
- Capa API devuelve `403 Forbidden` con `{ "codigoError": "PRE-1", "mensaje": "Capability 'generar-ot' requerida para este comando." }`.

### 6.4 Violación PRE-3 — motivo demasiado corto (422)

**Given**
- Aggregate en estado `Firmada` con ≥1 hallazgo `RequiereIntervencion`.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="Corto", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Handler (o método de decisión) lanza `MotivoRechazoInvalidoException("El motivo del rechazo debe tener al menos 10 caracteres.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-MOTIVO" }`.

### 6.5 Violación PRE-3 — motivo vacío o solo espacios (422)

**Given**
- Aggregate en estado `Firmada` con ≥1 hallazgo `RequiereIntervencion`.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="   ", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Handler lanza `MotivoRechazoInvalidoException` (trim da cadena vacía → length = 0 < 10).
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-MOTIVO" }`.

### 6.6 Violación PRE-4 / I-F6.a — inspección no firmada (EnEjecucion) (422)

**Given**
- Stream con `InspeccionIniciada_v1` (Estado=EnEjecucion). Inspección sin firmar.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="No aplica OT por razones operativas", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `InspeccionNoFirmadaException("La inspección X está en estado 'EnEjecucion'. RechazarGenerarOT solo aplica a inspecciones en estado 'Firmada'.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-ESTADO" }`.

### 6.7 Violación PRE-4 — inspección ya cerrada CerradaSinOT (422)

**Given**
- Stream con `InspeccionCerradaSinOT_v1` (Estado=CerradaSinOT — estado terminal). La inspección ya fue cerrada previamente (p. ej. por la saga automática).
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="No aplica OT por razones operativas", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `InspeccionNoFirmadaException("La inspección X está en estado 'CerradaSinOT'. RechazarGenerarOT solo aplica a inspecciones en estado 'Firmada'.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-ESTADO" }`.

### 6.8 Violación PRE-5 / I-F6.b — sin hallazgos con RequiereIntervencion (422)

**Given**
- Stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereSeguimiento`, Eliminado=false) + `DiagnosticoEmitido_v1` + `DictamenEstablecido_v1` (ConRestriccion) + `InspeccionFirmada_v1` (Estado=Firmada).
- `aggregate.Hallazgos`: solo hallazgos con `RequiereSeguimiento` — ninguno con `RequiereIntervencion`.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="Rechazo por razones operativas evidentes", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `SinHallazgosConIntervencionException("La inspección X no tiene hallazgos activos con AccionRequerida=RequiereIntervencion. RechazarGenerarOT no aplica.")`.
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-SIN-INTERVENCION" }`.

### 6.9 Violación PRE-5 — hallazgo RequiereIntervencion eliminado no cuenta (422)

**Given**
- Stream con firma completa donde el único hallazgo con `RequiereIntervencion` fue eliminado: `HallazgoEliminado_v1` (h1, Eliminado=true). No quedan hallazgos activos con `RequiereIntervencion`.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="Rechazo por razones operativas evidentes", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `SinHallazgosConIntervencionException` (el hallazgo eliminado no cuenta — misma lógica que PRE-5 en 6.8).
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-SIN-INTERVENCION" }`.

### 6.10 Violación PRE-6 / I-F6.c — OT ya solicitada (409)

**Given**
- Stream con firma completa + `OTSolicitada_v1` previo (aggregate.OTSolicitada == true). La OT ya fue enviada al ERP por la saga `EjecutarOTSaga`.
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Comando `RechazarGenerarOT(InspeccionId=X, Motivo="Rechazo tardío, no debería proceder", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Aggregate lanza `OTYaSolicitadaException("La inspección X ya tiene una OT solicitada. No se puede rechazar una OT ya enviada al ERP.")`.
- Sin evento.
- Capa API devuelve `409 Conflict` con `{ "codigoError": "I-F6-OT-YA-SOLICITADA" }`.

### 6.11 Violación PRE-7 / I-F6.d — OT ya rechazada (409)

**Given**
- Stream con firma completa + `GeneracionOTRechazada_v1` previo (aggregate.OTRechazada == true) + `InspeccionCerradaSinOT_v1` previo (aggregate.Estado == CerradaSinOT).
- Claims: `Capabilities=["generar-ot"]`.

**When**
- Segundo intento de rechazo: `RechazarGenerarOT(InspeccionId=X, Motivo="Segundo intento de rechazo innecesario", RechazadoPor="jefe.campo.02", Capabilities=["generar-ot"])`.

**Then**
- PRE-4 dispara antes que PRE-7 (Estado=CerradaSinOT != Firmada).
- Aggregate lanza `InspeccionNoFirmadaException` (PRE-4 tiene precedencia — la inspección ya está cerrada).
- Sin evento.
- Capa API devuelve `422 Unprocessable Entity` con `{ "codigoError": "I-F6-ESTADO" }`.

> **Nota:** en un estado coherente, PRE-4 siempre intercepta el doble rechazo antes que PRE-7 porque `InspeccionCerradaSinOT_v1` pone el estado en `CerradaSinOT` en el mismo `SaveChangesAsync` que `GeneracionOTRechazada_v1`. PRE-7 (`!OTRechazada`) es una defensa redundante para el caso hipotético en que el stream tenga `GeneracionOTRechazada_v1` sin el `InspeccionCerradaSinOT_v1` subsiguiente (stream corrupto). El `red` debe incluir un test explícito para PRE-7 aislado (aggregate con `OTRechazada=true` pero `Estado=Firmada` — estado teóricamente imposible pero que PRE-7 defiende).

### 6.12 PRE-2 — InspeccionId no existe (404)

**Given**
- Ningún stream con `InspeccionId=Z` en Marten.

**When**
- Comando `RechazarGenerarOT(InspeccionId=Z, Motivo="Motivo de rechazo suficientemente largo", RechazadoPor="jefe.campo.01", Capabilities=["generar-ot"])`.

**Then**
- Handler lanza `InspeccionNoEncontradaException`.
- Capa API devuelve `404 Not Found`.

### 6.13 Idempotencia — replay con mismo `X-Client-Command-Id`

**Given**
- Comando `RechazarGenerarOT` con `MessageId=Y` ya ejecutado exitosamente. Wolverine envelope storage tiene respuesta original (dos eventos emitidos, `aggregate.Estado == CerradaSinOT`).

**When**
- Cliente reenvía mismo `MessageId=Y` tras timeout de red (mismo header `X-Client-Command-Id`).

**Then**
- Wolverine envelope dedup devuelve respuesta original sin re-aplicar handler.
- El stream sigue con exactamente dos eventos (`GeneracionOTRechazada_v1` + `InspeccionCerradaSinOT_v1`) — sin duplicación.
- `aggregate.Estado` permanece `CerradaSinOT`.
- Capa API devuelve `200 OK` con body original.

### 6.14 Rebuild desde stream — Apply puro y orden causal (obligatorio)

**Given**
- Aggregate vacío (sin eventos).
- Lista de eventos en orden causal para reproducir el happy path 6.1:
  1. `InspeccionIniciada_v1(EquipoId=42, TecnicoId="carlos.ruiz", Tipo=Tecnica, Estado=EnEjecucion)`
  2. `HallazgoRegistrado_v1(HallazgoId=h1, AccionRequerida=RequiereIntervencion, Eliminado=false, TipoFallaId=1, CausaFallaId=2)`
  3. `DiagnosticoEmitido_v1(DiagnosticoFinal="Falla estructural en brazo hidráulico")`
  4. `DictamenEstablecido_v1(Dictamen=NoPuedeOperar)`
  5. `InspeccionFirmada_v1(FirmadoPor="carlos.ruiz", Estado=Firmada)`
  6. `GeneracionOTRechazada_v1(InspeccionId=X, Motivo="El equipo será dado de baja definitiva en 10 días", RechazadoPor="jefe.campo.01", RechazadaEn=2026-05-08T15:00:00Z)`
  7. `InspeccionCerradaSinOT_v1(InspeccionId=X, MotivoCierre=RechazadaPorAprobador, CerradaEn=2026-05-08T15:00:00Z)`

**When**
- Se reproyectan los siete eventos en orden sobre `Inspeccion.Reconstruir(events)`.

**Then**
- Estado resultante:
  - `Estado == EstadoInspeccion.CerradaSinOT`.
  - `OTRechazada == true`.
  - `MotivoRechazoOT == "El equipo será dado de baja definitiva en 10 días"`.
  - `OTSolicitada == false`.
  - `Dictamen == DictamenOperacion.NoPuedeOperar`.
  - `Hallazgos.Count == 1` (h1 activo, RequiereIntervencion).
- Ningún `Apply` lanza excepción.
- El estado resultante es idéntico al obtenido tras ejecutar el comando en 6.1.

> **Justificación del rebuild:** garantiza que `Apply(GeneracionOTRechazada_v1)` y `Apply(InspeccionCerradaSinOT_v1)` son puros, que el orden causal es correcto (rechazo antes de cierre), y que la adición de `MotivoRechazoOT` al aggregate no introduce lógica de validación.

---

## 7. Idempotencia / retries

**Idempotencia end-to-end (ADR-008 §9.16):**

El cliente PWA genera `clientCommandId: UUIDv7` cuando el aprobador confirma el rechazo. Viaja en header `X-Client-Command-Id`, mapeado a `MessageId` Wolverine. Replay detectado por envelope dedup → devuelve respuesta original sin re-ejecutar handler (escenario 6.13).

**Idempotencia natural por PRE-7 (I-F6.d):**

Si el aprobador reenvía con un `clientCommandId` distinto (nuevo retry humano) sobre una inspección que ya tiene `GeneracionOTRechazada_v1`, el aggregate detectará en PRE-4 que el estado es `CerradaSinOT` y lanzará `InspeccionNoFirmadaException` (`422 Unprocessable Entity`). El `422` es intencional: señala que la inspección ya está cerrada. El cliente no debe reintentar automáticamente en `422`.

**Sin POST a Sinco en este slice:**

Este comando no cruza al ERP. El cierre `CerradaSinOT` es una operación puramente local — solo persiste los dos eventos en el stream de Marten. No hay sagas adicionales disparadas por `GeneracionOTRechazada_v1` (el equipo se libera automáticamente por la proyección que consume `InspeccionCerradaSinOT_v1`).

**Sin `Idempotency-Key` para Sinco:**

No aplica — no hay llamadas HTTP al ERP en este slice.

---

## 8. Impacto en proyecciones / read models

### 8.1 `InspeccionAbiertaPorEquipoView` (§15.12.6) — equipo liberado

La proyección consume `InspeccionCerradaSinOT_v1` para eliminar la fila y marcar el equipo como disponible. Este comportamiento **ya existe en la implementación actual** (el stub de 1k incluyó el case para `InspeccionCerradaSinOT_v1`). No requiere cambio en esta proyección — el evento es el mismo.

Tras el cierre: otro técnico puede iniciar una nueva inspección sobre el equipo de inmediato.

### 8.2 `BandejaInspeccionesPendientesOTView` (§15.12.5)

Si esta proyección existiera (roadmap 3.25 / 3.45b), consumiría `GeneracionOTRechazada_v1` para marcar la fila como `EstadoOT=Rechazada` y posiblemente `InspeccionCerradaSinOT_v1` para eliminarla. Esta proyección es responsabilidad de un slice separado. El agente `infra-wire` de este slice debe dejar un comentario `// TODO: actualizar BandejaInspeccionesPendientesOTView con GeneracionOTRechazada_v1 en slice de proyecciones`.

### 8.3 `BandejaTecnicoView` (§15.12.3)

La inspección aparecía en la bandeja del técnico como `Firmada`. Al emitir `InspeccionCerradaSinOT_v1`, la fila debe transicionar a estado `CerradaSinOT` (o desaparecer de la bandeja activa). Comportamiento ya cubierto por el case de `InspeccionCerradaSinOT_v1` si la proyección lo consume — verificar en implementación de esa proyección. Sin cambio nuevo en este slice.

### 8.4 Proyección de notificaciones — capability `recibir-alertas-ot-rechazada`

ADR-007 §17 define la capability `recibir-alertas-ot-rechazada`: los usuarios con esta capability deben recibir notificación cuando un aprobador rechaza la OT. Los destinatarios típicos son el técnico firmante y el supervisor del área. Este proyector es responsabilidad de un slice separado (roadmap 3.45c o similar). El agente `infra-wire` debe dejar un TODO.

---

## 9. Impacto en endpoints HTTP

### Endpoint principal

| Campo | Valor |
|---|---|
| Método + ruta | `POST /api/v1/inspecciones/{id}/rechazar-ot` |
| Path param | `{id}` = `InspeccionId` (Guid) |
| Content-Type | `application/json` |
| Authorization | JWT del host PWA; el middleware extrae `userId` y capabilities del token |

**Headers requeridos:**
- `X-Client-Command-Id: <UUID>` (idempotencia ADR-008; UUIDv7 preferido).
- `Authorization: Bearer <JWT>` heredado del host PWA (ADR-002 tentativo).

**DTO de request (body JSON):**

```json
{
  "motivo": "El equipo será dado de baja definitiva en 10 días"
}
```

**DTO de response (200 OK, body JSON):**

```json
{
  "inspeccionId": "0193a4f7-...",
  "estado": "CerradaSinOT",
  "rechazadaEn": "2026-05-08T15:00:00Z",
  "rechazadoPor": "jefe.campo.01",
  "motivo": "El equipo será dado de baja definitiva en 10 días"
}
```

**Códigos HTTP:**

| Escenario | Código | `codigoError` | Notas |
|---|---|---|---|
| Happy path | `200 OK` | — | La inspección cierra síncronamente — no hay saga asíncrona pendiente (a diferencia de `GenerarOT` que usa `202`) |
| Capability ausente (`generar-ot`) | `403 Forbidden` | `"PRE-1"` | PRE-1 — middleware de auth |
| Inspección no existe | `404 Not Found` | — | PRE-2 |
| Motivo < 10 chars o vacío | `422 Unprocessable Entity` | `"I-F6-MOTIVO"` | PRE-3 |
| Estado != Firmada | `422 Unprocessable Entity` | `"I-F6-ESTADO"` | PRE-4 |
| Sin hallazgos RequiereIntervencion | `422 Unprocessable Entity` | `"I-F6-SIN-INTERVENCION"` | PRE-5 |
| OT ya solicitada al ERP | `409 Conflict` | `"I-F6-OT-YA-SOLICITADA"` | PRE-6 |
| OT ya rechazada (doble rechazo) | `409 Conflict` | `"I-F6-OT-YA-RECHAZADA"` | PRE-7 — defensa |

**Nota sobre código HTTP 200 vs 202:** se usa `200 OK` (no `202 Accepted`) porque el cierre de la inspección es síncrono — ambos eventos se persisten en el mismo `SaveChangesAsync` y el aggregate ya está en estado `CerradaSinOT` cuando el handler retorna. No hay saga asíncrona pendiente para este comando (a diferencia de `GenerarOT` que desencadena `EjecutarOTSaga`).

**Permiso requerido:** capability `generar-ot` extraída del JWT del host PWA. Independiente de `ejecutar-inspeccion`. El handler recibe las claims como `IReadOnlyCollection<string> Capabilities` por parámetro; el dominio nunca conoce JWTs directamente.

---

## 10. Impacto en SignalR / push (si aplica)

**Este slice no emite eventos SignalR directamente desde el handler.** Sin embargo, ADR-007 §17 define la capability `recibir-alertas-ot-rechazada` para notificación a interesados (técnico firmante, supervisor del área). La implementación de este push es responsabilidad de un proyector lateral sobre `GeneracionOTRechazada_v1`:

| Push SignalR | Emitido por | Audiencia | Cuándo |
|---|---|---|---|
| `OTRechazada` (propuesto) | Proyector sobre `GeneracionOTRechazada_v1` (slice futuro 3.45c) | Usuarios con capability `recibir-alertas-ot-rechazada` | Al persistir el evento de rechazo |

El hub `InspeccionesHub` existe por ADR-005 pero **este slice no lo instancia**. El push de notificación de rechazo es responsabilidad de los slices de proyecciones/notificaciones.

**No aplica directamente para este slice.**

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**Este slice no invoca ningún endpoint Sinco on-prem.** El rechazo de la OT es una operación puramente de dominio local — solo persiste los dos eventos en el stream de Marten y retorna `200 OK`. No hay POST a MYE ni llamadas al ERP en este slice.

**No aplica para este slice.**

---

## 12. Decisiones del modelador y preguntas abiertas

### Decisiones tomadas (el usuario debe confirmar o revertir)

**D-1 — Longitud máxima de `Motivo`: 500 chars (asunción del modelador)**

El modelo §17 define solo el mínimo (10 chars). Este spec propone un máximo de 500 chars como límite razonable para un campo de texto libre de auditoría en una pantalla móvil. La validación del máximo puede vivir en la capa HTTP (DTO validation) sin llegar al dominio.

Si el usuario prefiere sin límite superior, eliminar la validación de máximo del handler — no afecta el dominio ni los eventos.

**D-2 — Nombre del campo en `GeneracionOTRechazada_v1`: `Motivo` (no `MotivoRechazo`)**

El stub de 1k usaba `MotivoRechazo`. El command record del §17 usa `Motivo`. Este spec alinea el nombre del campo del evento con el del comando (`Motivo`) para consistencia con el patrón del slice (el evento refleja el nombre del campo tal como viene del comando). El `red` debe renombrar el campo en el event record al elevar el stub. Impacto: el `Apply` existente en `Inspeccion.cs` se compila correctamente sin cambio de comportamiento; solo cambia el nombre del campo del record.

**D-3 — Eliminación de `CerradoPor` de `InspeccionCerradaSinOT_v1`**

El stub de 1k tenía `CerradoPor: string`. Se elimina porque:
1. En el caso de cierre automático por saga (motivo `AutomaticoSinIntervencion`), no hay una persona que "cierra" — la saga no tiene identidad de usuario.
2. El aprobador que rechaza ya queda auditado en `GeneracionOTRechazada_v1.RechazadoPor` — duplicar en `InspeccionCerradaSinOT_v1` sería redundante.
3. El shape resultante es más limpio: el motivo del cierre es suficiente para trazar el origen.

Si el usuario necesita `CerradoPor` en `InspeccionCerradaSinOT_v1` para alguna proyección específica, puede añadirse como campo nullable (`string? CerradoPor`) con valor `null` en el caso automático y el `RechazadoPor` en el caso de rechazo.

**D-4 — Código HTTP 200 OK (no 202 Accepted)**

`RechazarGenerarOT` cierra la inspección síncronamente en un único `SaveChangesAsync`. No hay saga asíncrona ni llamada al ERP. Se usa `200 OK` por coherencia con la semántica (el resultado ya es observable cuando el handler retorna), a diferencia de `GenerarOT` que usa `202 Accepted` porque el cierre real ocurre via saga asíncrona.

**D-5 — `InspeccionCerradaSinOT_v1` es el mismo evento para cierre automático y cierre por rechazo**

Se usa un único event record con discriminador `MotivoCierre`, no dos event records separados. Ventaja: la proyección `InspeccionAbiertaPorEquipoView` (y cualquier otro consumidor) solo necesita un case. Desventaja: si los consumidores necesitan comportamiento diferente por motivo (p. ej. notificaciones distintas), deben leer el discriminador. Esta es la decisión de ADR-007 §17 — el modelador la preserva.

### Preguntas abiertas

Cero preguntas abiertas que bloqueen la spec. Las decisiones D-1 a D-5 son asunciones justificadas que el usuario puede revertir antes de firmar. El `red` puede comenzar con las asunciones actuales.

**P-1 (para el `red` — confirmar impacto de renombrar stub):** Verificar que ningún test existente de 1k ni código de producción referencia `GeneracionOTRechazada_v1.MotivoRechazo` directamente. El stub en 1k solo configura el evento en `Given` de PRE-6 (§6.7) y en el `Apply` — ambos son triviales de actualizar. El `red` debe confirmar antes de escribir los tests del slice 1l.

**P-2 (para el `red` — confirmar impacto de eliminar `CerradoPor` del stub):** Verificar que ningún test existente de 1k ni proyección referencia `InspeccionCerradaSinOT_v1.CerradoPor`. El stub en 1k usa el evento en `Given` de PRE-3/§6.12 — solo para reconstruir estado `CerradaSinOT`. Sin campo `CerradoPor` en el Given, el test sigue compilando (el campo desaparece del record).

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-7) mapean a al menos un escenario Given/When/Then en §6 (6.3→PRE-1, 6.12→PRE-2, 6.4+6.5→PRE-3, 6.6+6.7→PRE-4, 6.8+6.9→PRE-5, 6.10→PRE-6, 6.11→PRE-7).
- [x] Todas las invariantes tocadas (I-F6 completa con sus 4 sub-condiciones, I-F4.d simetría, I-F1) mapean a escenarios Then.
- [x] El happy path (§6.1) está presente con dictamen NoPuedeOperar.
- [x] Happy path secundario (§6.2) con dictamen ConRestriccion presente.
- [x] El escenario de rebuild desde stream (§6.14) está presente con los 7 eventos en orden causal.
- [x] §7 (idempotencia) está decidido: ADR-008 `X-Client-Command-Id` cubre replay de red; PRE-4 (estado CerradaSinOT) cubre segundo intento humano (`422`). Sin POST a Sinco en este slice.
- [x] §10 (SignalR) resuelto explícitamente: no aplica directamente; push `OTRechazada` es responsabilidad de proyector lateral sobre `GeneracionOTRechazada_v1` (slice futuro 3.45c).
- [x] §11 (adapters Sinco on-prem) resuelto explícitamente: no aplica.
- [x] §8 (proyecciones) documentadas: `InspeccionAbiertaPorEquipoView` impactada (ya consume el evento — sin cambio nuevo); `BandejaInspeccionesPendientesOTView` y notificaciones son responsabilidad de slices futuros.
- [x] §12 Decisiones del modelador: D-1 a D-5 documentadas con justificación. Cero preguntas abiertas bloqueantes.
- [x] Impacto en stubs de 1k documentado explícitamente (D-2 renombrar campo, D-3 eliminar `CerradoPor`, P-1/P-2 como asunciones para el `red`).

---

## Notas para el agente `red`

**Archivos a crear/modificar en este slice:**

| Tipo | Archivo (ruta relativa a `src/`) | Operación |
|---|---|---|
| Enum nuevo | `Inspecciones.Domain/Inspecciones/MotivoCierreSinOT.cs` | Crear — `AutomaticoSinIntervencion`, `RechazadaPorAprobador` |
| Evento (elevar stub) | `Inspecciones.Domain/Inspecciones/GeneracionOTRechazada_v1.cs` | Modificar — renombrar `MotivoRechazo` → `Motivo` (ver D-2) |
| Evento (elevar stub) | `Inspecciones.Domain/Inspecciones/InspeccionCerradaSinOT_v1.cs` | Modificar — eliminar `CerradoPor`, añadir `MotivoCierreSinOT MotivoCierre` (ver D-3) |
| Excepción nueva | `Inspecciones.Domain/Inspecciones/Excepciones.cs` | Añadir `MotivoRechazoInvalidoException`, `OTYaRechazadaException` |
| Aggregate state | `Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Añadir campo `string? MotivoRechazoOT`, método de decisión `RechazarOT(...)`, extender `Apply(GeneracionOTRechazada_v1)` para setear `MotivoRechazoOT` |
| Comando | `Inspecciones.Application/Inspecciones/RechazarGenerarOT.cs` | Crear |
| Handler | `Inspecciones.Application/Inspecciones/RechazarGenerarOTHandler.cs` | Crear |
| Request/Result | `Inspecciones.Api/Inspecciones/RechazarGenerarOTRequest.cs` | Crear — `RechazarGenerarOTRequest` + `RechazarGenerarOTResult` |
| Endpoint | `Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Añadir `POST .../rechazar-ot` |
| Tests dominio | `Inspecciones.Domain.Tests/Inspecciones/RechazarGenerarOTTests.cs` | Crear — cobertura ≥85 % ramas del aggregate afectadas |
| Fixtures | `Inspecciones.Domain.Tests/Inspecciones/RechazarGenerarOTFixtures.cs` | Crear |
| Tests integración | `Inspecciones.Application.Tests/Inspecciones/RechazarGenerarOTHandlerTests.cs` | Crear |
| Tests HTTP | `Inspecciones.Api.Tests/RechazarGenerarOTEndpointTests.cs` | Crear |

**Verificación previa al escribir tests (confirmación de P-1/P-2):**
1. Buscar en `GenerarOTTests.cs` y `GenerarOTFixtures.cs` referencias a `GeneracionOTRechazada_v1.MotivoRechazo` → renombrar a `Motivo` si aparece.
2. Buscar referencias a `InspeccionCerradaSinOT_v1.CerradoPor` en todos los test files y en `InspeccionAbiertaPorEquipoProjection.cs` → eliminar o ajustar.

**Cobertura mínima:** ≥85 % de ramas del aggregate para las rutas que toca este slice (PRE-4..PRE-7 + validación motivo + happy path + rebuild).

**Convención de nombres de tests (español, referenciando código de invariante):**
- `RechazarGenerarOT_inspeccion_firmada_con_hallazgo_intervencion_emite_dos_eventos_en_orden_causal`
- `RechazarGenerarOT_motivo_vacio_lanza_MotivoRechazoInvalidoException_I_F6`
- `RechazarGenerarOT_motivo_menor_10_chars_lanza_MotivoRechazoInvalidoException_I_F6`
- `RechazarGenerarOT_inspeccion_no_firmada_lanza_InspeccionNoFirmadaException_I_F6`
- `RechazarGenerarOT_inspeccion_cerrada_sin_OT_lanza_InspeccionNoFirmadaException`
- `RechazarGenerarOT_sin_hallazgos_con_intervencion_lanza_SinHallazgosConIntervencionException_I_F6`
- `RechazarGenerarOT_hallazgo_intervencion_eliminado_no_cuenta_lanza_SinHallazgos`
- `RechazarGenerarOT_OT_ya_solicitada_lanza_OTYaSolicitadaException_I_F6`
- `RechazarGenerarOT_OT_ya_rechazada_estado_firmada_lanza_OTYaRechazadaException_I_F6`
- `RechazarGenerarOT_estado_post_comando_OTRechazada_true_MotivoRechazoOT_seteado`
- `RechazarGenerarOT_rebuild_desde_stream_7_eventos_estado_correcto`
- `RechazarGenerarOT_GeneracionOTRechazada_v1_emitido_antes_de_InspeccionCerradaSinOT_v1`
