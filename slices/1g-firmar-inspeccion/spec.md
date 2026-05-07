# Slice 1g — FirmarInspeccion

**Autor:** domain-modeler
**Fecha:** 2026-05-07
**Estado:** draft
**Agregado afectado:** `Inspeccion`
**Decisiones previas relevantes:**
- `01-modelo-dominio.md §15.4` — catálogo final de 24 eventos; eventos #15 `DiagnosticoEmitido_v1`, #16 `DictamenEstablecido_v1`, #2 `InspeccionFirmada_v1`; convención de tipos de IDs.
- `01-modelo-dominio.md §15.5` — validaciones pre-firma V-F1..V-F8 (todas bloqueantes).
- `01-modelo-dominio.md §15.7` — invariantes I-F1..I-F6; inmutabilidad post-firma.
- `01-modelo-dominio.md §15.12.6` — `InspeccionAbiertaPorEquipoView`; FU-13 migración a `MultiStreamProjection` con `DeleteEvent`.
- ADR-007 (`§17`) — `FirmarInspeccion` ya no genera OT automáticamente; la firma solo emite los 3 eventos propios. La saga `CerrarInspeccionSaga` (simplificada) cierra-sin-OT si no hay `RequiereIntervencion`; si los hay, la inspección queda `Firmada` esperando el comando `GenerarOT` de un usuario con capability `generar-ot`. Sagas `AbrirSeguimientosSaga`, `SincronizarDictamenVigenteSaga` y `GenerarPdfInspeccionSaga` también se disparan desde `InspeccionFirmada_v1` — son slices separados y no forman parte de este slice.
- ADR-005 (`§14`) — SignalR push `OTGenerada` / `InspeccionCerradaSinOT` no aplica a este slice; aplica a las sagas posteriores.
- ADR-006 (`§16`) — outbox para integraciones ERP; este slice NO hace POST al ERP (la firma es solo dominio).
- `FOLLOWUPS.md #13` — FU-13 migración de `InspeccionAbiertaPorEquipoView` a `MultiStreamProjection` inline; este slice es el primer slice que maneja `InspeccionFirmada_v1` y es el disparador natural.
- `slices/1a-iniciar-inspeccion-aggregate/spec.md` — establece el patrón de `ClaimsTecnico` y `UbicacionGps`.
- `slices/1f-asignar-repuesto/spec.md` — establece el patrón de precondiciones PRE-X y convención de nombres.

---

## 1. Intención

El técnico finaliza la inspección técnica pulsando "Firmar y cerrar". El sistema valida que se cumplan todas las condiciones pre-firma (V-F1..V-F8), registra el diagnóstico final, el dictamen de operación y la firma manuscrita del técnico en un único acto atómico. A partir de ese momento la inspección es inmutable: no se pueden agregar, editar ni eliminar hallazgos, repuestos ni adjuntos. Las sagas post-firma (cierre-sin-OT, seguimientos, dictamen vigente, PDF) se disparan desde el evento `InspeccionFirmada_v1` y son slices posteriores.

---

## 2. Comando

```csharp
public sealed record FirmarInspeccion(
    Guid         InspeccionId,
    string       Diagnostico,         // texto libre obligatorio, no vacío — diagnóstico final de la inspección
    DictamenOperacion Dictamen,        // PuedeOperar | ConRestriccion | NoPuedeOperar
    string       JustificacionDictamen, // texto libre obligatorio, no vacío — justifica el dictamen elegido
    string       FirmaUri,            // URI del blob donde reside la imagen de firma manuscrita (no vacío)
    UbicacionGps UbicacionFirma,      // GPS re-capturado al momento de firmar (V-F6)
    string       TecnicoId            // extraído del JWT por la capa API; el dominio lo recibe como parámetro opaco
) : ICommand;
```

**Value objects referenciados:**

```csharp
public sealed record UbicacionGps(
    decimal       Latitud,
    decimal       Longitud,
    decimal       PrecisionMetros,
    DateTimeOffset CapturadoEn);

public enum DictamenOperacion
{
    PuedeOperar,
    ConRestriccion,
    NoPuedeOperar
}
```

**Claims del técnico** (parámetros adicionales del handler, no del record de comando):

```csharp
public sealed record ClaimsTecnico(
    string       TecnicoId,                           // username opaco del host PWA
    ISet<int>    ProyectosAsignados,                   // proyectos donde tiene capability
    bool         TieneCapabilityEjecutarInspeccion);
```

**Nota sobre `TecnicoId`:** el dominio lo recibe como `string` opaco del JWT; nunca lo valida como Guid ni consulta catálogos de usuario.

**Nota sobre `Diagnostico` y `JustificacionDictamen`:** son campos de texto libre; el handler valida que no sean nulos ni estén vacíos antes de invocar el método de decisión del aggregate.

**Nota sobre `FirmaUri`:** el handler recibe el URI del blob que el cliente ya subió (patrón SAS upload — el dominio nunca firma SAS). El handler valida que sea no vacío; el formato exacto de la URI es opaco para el dominio.

---

## 3. Evento(s) emitido(s)

Los tres eventos se emiten en un único `SaveChangesAsync` en el mismo stream `inspeccion-{InspeccionId}`, en estricto orden causal:

| Orden | Evento | Payload | Cuándo |
|---|---|---|---|
| 1 | `DiagnosticoEmitido_v1` | Ver campos a continuación | Primero — registra el texto de diagnóstico técnico. |
| 2 | `DictamenEstablecido_v1` | Ver campos a continuación | Segundo — registra el dictamen de operación con su justificación. |
| 3 | `InspeccionFirmada_v1` | Ver campos a continuación | Tercero — transiciona el estado a `Firmada`; sella la inspección como inmutable. |

```csharp
public sealed record DiagnosticoEmitido_v1(
    Guid           InspeccionId,
    string         DiagnosticoFinal,   // texto libre; copia del campo Diagnostico del comando
    string         EmitidoPor,         // TecnicoId opaco del JWT
    DateTimeOffset EmitidoEn);         // TimeProvider.GetUtcNow() en el handler

public sealed record DictamenEstablecido_v1(
    Guid              InspeccionId,
    DictamenOperacion Dictamen,
    string            Justificacion,   // texto libre; copia de JustificacionDictamen del comando
    string            EmitidoPor,
    DateTimeOffset    EstablecidoEn);  // mismo timestamp que EmitidoEn (un único GetUtcNow() en el handler)

public sealed record InspeccionFirmada_v1(
    Guid           InspeccionId,
    string         FirmadoPor,         // TecnicoId opaco
    string         FirmaUri,           // URI del blob de firma manuscrita
    UbicacionGps   UbicacionFirma,     // GPS al momento de firmar (V-F6)
    DateTimeOffset FirmadaEn);         // mismo timestamp
```

**Nota sobre timestamps:** el handler llama una sola vez a `TimeProvider.GetUtcNow()` y reutiliza el valor en los tres eventos. Prohibido `DateTime.UtcNow` directo (regla dura CLAUDE.md).

**Atomicidad:** los tres eventos se pasan como lista a `session.Events.Append(InspeccionId, evt1, evt2, evt3)` y el `SaveChangesAsync` único los persiste. Si el `SaveChanges` falla, ninguno queda. El orden en el stream es causal: Diagnostico → Dictamen → Firmada.

---

## 4. Precondiciones

Evaluadas en el **método de decisión** del aggregate antes de emitir cualquier evento. Los `Apply` son puros y nunca las re-evalúan.

- **PRE-1: Capability presente.** `claims.TieneCapabilityEjecutarInspeccion == true`. Sin ella, el endpoint devuelve `403 Forbidden` antes de llegar al aggregate. — excepción: `CapabilityRequeridaException`.
- **PRE-2: Inspección en estado EnEjecucion (V-F7).** `aggregate.Estado == InspeccionEstado.EnEjecucion`. Si la inspección ya está `Firmada`, `Cancelada`, `Cerrada` o `CerradaSinOT`, el comando es rechazado. — excepción: `InspeccionNoEnEjecucionException`.
- **PRE-3: Al menos un hallazgo registrado y no eliminado (V-F1).** `aggregate.Hallazgos.Any(h => !h.Eliminado)`. — excepción: `SinHallazgosException`.
- **PRE-4: Diagnóstico presente (campo del comando no vacío).** `!string.IsNullOrWhiteSpace(cmd.Diagnostico)`. Validado en el handler antes de invocar el método de decisión. — excepción: `DiagnosticoRequeridoException`.
- **PRE-5: Dictamen coherente con hallazgos (V-F8).** Si existe ≥1 hallazgo no eliminado con `AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion}`, entonces `cmd.Dictamen ∉ {PuedeOperar}`. — excepción: `DictamenIncoherenteException` (mensaje: "No puedes firmar con dictamen 'Apto'. Hay {N} hallazgos que requieren {seguimiento|intervención|ambos}. Selecciona 'Con restricciones' o 'No apto'.").
- **PRE-6: Hallazgos con RequiereIntervencion completos (V-F3).** Para cada hallazgo no eliminado con `AccionRequerida = RequiereIntervencion`: (a) `TipoFallaId != null`, (b) `CausaFallaId != null`, (c) el hallazgo tiene ≥1 adjunto no eliminado. — excepción: `HallazgoIntervencionIncompletoException` (mensaje indica cuál hallazgo falla y por qué).
- **PRE-7: Firma no vacía (V-F5).** `!string.IsNullOrWhiteSpace(cmd.FirmaUri)`. — excepción: `FirmaRequeridaException`.
- **PRE-8: GPS de firma presente (V-F6).** `cmd.UbicacionFirma != null`. El GPS puede diferir de la ubicación de inicio sin bloquear. — excepción: `GpsRequeridoException`.
- **PRE-9: Firmante es contribuyente o supervisor (I-F → I8 del modelo histórico).** `aggregate.TecnicosContribuyentes.Contains(cmd.TecnicoId)` O el técnico tiene capability de supervisor (ver §12 Preguntas abiertas — la capability de supervisor no está modelada en `ClaimsTecnico` en este slice). — excepción: `TecnicoNoContribuyenteException`.

> **Capa donde viven**: las pre-condiciones se evalúan en el método `Firmar(...)` del aggregate. Los `Apply` son mutaciones puras de estado sin lanzar excepciones. PRE-1 la verifica la capa HTTP antes de llegar al aggregate.

---

## 5. Invariantes tocadas

- **V-F1** (≥1 hallazgo no eliminado): cubierta en PRE-3.
- **V-F3** (hallazgos RequiereIntervencion con TipoFalla + CausaFalla + ≥1 adjunto): cubierta en PRE-6. Los repuestos son opcionales (la intervención puede ser solo mano de obra).
- **V-F4** (dictamen seleccionado — siempre obligatorio): cubierta implícitamente por el tipo del campo (`DictamenOperacion` es enum, el comando no puede llegar sin un valor válido); el handler valida que el campo esté presente en el DTO HTTP.
- **V-F5** (firma manuscrita capturada): cubierta en PRE-7.
- **V-F6** (GPS de firma obligatorio): cubierta en PRE-8.
- **V-F7** (estado = EnEjecucion): cubierta en PRE-2.
- **V-F8** (coherencia dictamen ↔ hallazgos): cubierta en PRE-5.
- **I-F1** (inmutabilidad post-firma): garantizada por PRE-2 (estado = EnEjecucion requerido). Tras `Apply(InspeccionFirmada_v1)` el estado cambia a `Firmada`; cualquier comando posterior que exija `EnEjecucion` falla automáticamente.
- **V-F2** (todas las novedades preop verificadas o descartadas): ver §12 Preguntas abiertas — esta invariante no está modelada aún en el aggregate en los slices 1a-1f; se marca como pregunta abierta.

---

## 6. Escenarios Given / When / Then

### 6.1 Happy path — firma con hallazgos sin intervención (PuedeOperar)

**Given**
- Aggregate `Inspeccion` reconstruido desde stream con:
  - `InspeccionIniciada_v1` (estado `EnEjecucion`, técnico `tecnico-01`, TecnicosContribuyentes = {`tecnico-01`})
  - `HallazgoRegistrado_v1` (HallazgoId=`h1`, AccionRequerida=`NoRequiereIntervencion`, Eliminado=false)

**When**
- `FirmarInspeccion(InspeccionId, Diagnostico="Inspección sin hallazgos críticos", Dictamen=PuedeOperar, JustificacionDictamen="Equipo en buen estado", FirmaUri="https://blobs/firma-01.png", UbicacionFirma=UbicacionGps(4.7,-74.1,5.0,now), TecnicoId="tecnico-01")`

**Then**
- Emite (en orden causal, mismo `SaveChangesAsync`):
  1. `DiagnosticoEmitido_v1` con `DiagnosticoFinal="Inspección sin hallazgos críticos"`, `EmitidoPor="tecnico-01"`.
  2. `DictamenEstablecido_v1` con `Dictamen=PuedeOperar`, `Justificacion="Equipo en buen estado"`, `EmitidoPor="tecnico-01"`.
  3. `InspeccionFirmada_v1` con `FirmadoPor="tecnico-01"`, `FirmaUri="https://blobs/firma-01.png"`, `UbicacionFirma.Latitud=4.7`.
- El aggregate tiene `Estado=Firmada`, `DiagnosticoFinal="Inspección sin hallazgos críticos"`, `Dictamen=PuedeOperar`, `FirmaUri` poblado.

### 6.2 Happy path — firma con hallazgo RequiereIntervencion (NoPuedeOperar)

**Given**
- Aggregate con:
  - `InspeccionIniciada_v1` (`tecnico-01`)
  - `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereIntervencion`, TipoFallaId=1, CausaFallaId=2, Eliminado=false)
  - `AdjuntoSubido_v1` (AdjuntoId=`adj1`, HallazgoId=`h1`, no eliminado)

**When**
- `FirmarInspeccion(..., Dictamen=NoPuedeOperar, JustificacionDictamen="Falla estructural", TecnicoId="tecnico-01")`

**Then**
- Emite los 3 eventos en orden causal.
- `DictamenEstablecido_v1.Dictamen = NoPuedeOperar`.
- `InspeccionFirmada_v1` emitido como tercer evento.

### 6.3 Happy path — firma con hallazgo RequireSeguimiento (ConRestriccion) — V-F8 válido

**Given**
- Aggregate con:
  - `InspeccionIniciada_v1` (`tecnico-01`)
  - `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereSeguimiento`, Eliminado=false)

**When**
- `FirmarInspeccion(..., Dictamen=ConRestriccion, JustificacionDictamen="Monitoreo requerido", TecnicoId="tecnico-01")`

**Then**
- Emite los 3 eventos en orden causal.
- No lanza excepción.

### 6.4 Violacion PRE-2 — inspección ya firmada (V-F7)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `DiagnosticoEmitido_v1` + `DictamenEstablecido_v1` + `InspeccionFirmada_v1` (estado=`Firmada`)

**When**
- `FirmarInspeccion(...)` sobre el mismo `InspeccionId`

**Then**
- Lanza `InspeccionNoEnEjecucionException` con mensaje indicando que la inspección no está en ejecución.

### 6.5 Violacion PRE-3 — sin hallazgos (V-F1)

**Given**
- Aggregate con `InspeccionIniciada_v1` solamente (Hallazgos = vacío)

**When**
- `FirmarInspeccion(...)`

**Then**
- Lanza `SinHallazgosException`.

### 6.6 Violacion PRE-3 — todos los hallazgos eliminados (V-F1)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1) + `HallazgoEliminado_v1` (h1, Eliminado=true)

**When**
- `FirmarInspeccion(...)`

**Then**
- Lanza `SinHallazgosException` (todos los hallazgos están eliminados; ninguno vigente).

### 6.7 Violacion PRE-5 — dictamen PuedeOperar con hallazgo RequiereSeguimiento (V-F8)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereSeguimiento`, Eliminado=false)

**When**
- `FirmarInspeccion(..., Dictamen=PuedeOperar, ...)`

**Then**
- Lanza `DictamenIncoherenteException` con mensaje que indica hallazgos con seguimiento/intervención presentes.

### 6.8 Violacion PRE-5 — dictamen PuedeOperar con hallazgo RequiereIntervencion (V-F8)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereIntervencion`, TipoFallaId=1, CausaFallaId=2, Eliminado=false) + `AdjuntoSubido_v1` (adj1, h1)

**When**
- `FirmarInspeccion(..., Dictamen=PuedeOperar, ...)`

**Then**
- Lanza `DictamenIncoherenteException`.

### 6.9 Caso borde V-F8 — dictamen PuedeOperar permitido cuando solo hay NoRequiereIntervencion

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`NoRequiereIntervencion`, Eliminado=false)

**When**
- `FirmarInspeccion(..., Dictamen=PuedeOperar, ...)`

**Then**
- NO lanza excepción. Emite los 3 eventos.

### 6.10 Violacion PRE-6 — hallazgo RequiereIntervencion sin TipoFallaId (V-F3)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereIntervencion`, **TipoFallaId=null**, CausaFallaId=2, Eliminado=false) + `AdjuntoSubido_v1` (adj1, h1)

**When**
- `FirmarInspeccion(..., Dictamen=NoPuedeOperar, ...)`

**Then**
- Lanza `HallazgoIntervencionIncompletoException` indicando que falta `TipoFallaId` en el hallazgo `h1`.

### 6.11 Violacion PRE-6 — hallazgo RequiereIntervencion sin CausaFallaId (V-F3)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereIntervencion`, TipoFallaId=1, **CausaFallaId=null**, Eliminado=false) + `AdjuntoSubido_v1` (adj1, h1)

**When**
- `FirmarInspeccion(..., Dictamen=NoPuedeOperar, ...)`

**Then**
- Lanza `HallazgoIntervencionIncompletoException` indicando que falta `CausaFallaId` en el hallazgo `h1`.

### 6.12 Violacion PRE-6 — hallazgo RequiereIntervencion sin adjuntos (V-F3)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, AccionRequerida=`RequiereIntervencion`, TipoFallaId=1, CausaFallaId=2, Eliminado=false) + **sin AdjuntoSubido_v1**

**When**
- `FirmarInspeccion(..., Dictamen=NoPuedeOperar, ...)`

**Then**
- Lanza `HallazgoIntervencionIncompletoException` indicando que falta al menos un adjunto de evidencia en el hallazgo `h1`.

### 6.13 Violacion PRE-6 — hallazgo RequiereIntervencion con todos los adjuntos eliminados (V-F3)

**Given**
- Aggregate con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1` (h1, RequiereIntervencion, TipoFallaId=1, CausaFallaId=2) + `AdjuntoSubido_v1` (adj1, h1) + `AdjuntoEliminado_v1` (adj1, h1)

**When**
- `FirmarInspeccion(..., Dictamen=NoPuedeOperar, ...)`

**Then**
- Lanza `HallazgoIntervencionIncompletoException` (ningún adjunto activo en h1).

### 6.14 Violacion PRE-7 — FirmaUri vacío (V-F5)

**Given**
- Aggregate en estado válido para firmar (≥1 hallazgo vigente, dictamen coherente)

**When**
- `FirmarInspeccion(..., FirmaUri="", ...)`

**Then**
- Lanza `FirmaRequeridaException`.

### 6.15 Violacion PRE-8 — UbicacionFirma nula (V-F6)

**Given**
- Aggregate en estado válido para firmar

**When**
- `FirmarInspeccion(..., UbicacionFirma=null, ...)`

**Then**
- Lanza `GpsRequeridoException`.

### 6.16 Violacion PRE-9 — técnico no contribuyente intenta firmar

**Given**
- Aggregate con `InspeccionIniciada_v1` (TecnicoIniciador=`tecnico-01`, TecnicosContribuyentes={`tecnico-01`}) + ≥1 hallazgo no eliminado

**When**
- `FirmarInspeccion(..., TecnicoId="tecnico-99")` (no contribuyente, no supervisor)

**Then**
- Lanza `TecnicoNoContribuyenteException`.

### 6.17 Rebuild desde stream (obligatorio — 3 eventos)

**Given**
- Aggregate vacío (sin eventos)

**When**
- Se reproyectan en orden causal los eventos emitidos por el happy path §6.1:
  1. `InspeccionIniciada_v1`
  2. `HallazgoRegistrado_v1` (h1, NoRequiereIntervencion)
  3. `DiagnosticoEmitido_v1`
  4. `DictamenEstablecido_v1` (PuedeOperar)
  5. `InspeccionFirmada_v1`

**Then**
- `aggregate.Estado == Firmada`
- `aggregate.DiagnosticoFinal == "Inspección sin hallazgos críticos"`
- `aggregate.Dictamen == PuedeOperar`
- `aggregate.FirmaUri == "https://blobs/firma-01.png"`
- `aggregate.UbicacionFirma.Latitud == 4.7m`
- `aggregate.FirmadaEn != null`
- Ningún `Apply` lanza excepción.

> Justificacion: garantiza que los 3 eventos emitidos por `FirmarInspeccion` son puros en sus `Apply` y que el orden causal (Diagnostico → Dictamen → Firmada) es correcto. Si algún `Apply` tuviera validaciones o si el orden fuera incorrecto, la reproyeccion producirá un estado diferente o lanzará excepción.

### 6.18 Inmutabilidad post-firma — no se puede agregar hallazgo tras firmar (I-F1)

**Given**
- Aggregate reproyectado con `InspeccionFirmada_v1` (Estado=`Firmada`)

**When**
- Se intenta emitir `RegistrarHallazgo(...)` sobre el mismo aggregate

**Then**
- Lanza excepción de dominio (la invariante I-F1 está cubierta por PRE de `RegistrarHallazgo` que exige `Estado=EnEjecucion`). Este test verifica que el estado post-firma bloquea correctamente el comando hermano. (Es un test cross-slice que puede incluirse en el suite 1g o referenciarse a los tests de 1c.)

---

## 7. Idempotencia / retries

**El comando `FirmarInspeccion` NO es naturalmente idempotente.** Un segundo intento sobre una inspección ya firmada falla con `InspeccionNoEnEjecucionException` (PRE-2) — la respuesta de error 409 Conflict es la semantica correcta.

**Idempotencia de red (ADR-008):** el cliente PWA envía `X-Client-Command-Id` (UUID v7 generado en el dispositivo). El endpoint lee el header y lo propaga como `MessageId` del envelope Wolverine. Wolverine detecta replays por `MessageId` duplicado y devuelve la respuesta cacheada del envelope original sin re-ejecutar el handler. Si el replay llega antes de que el primer intento committee, el handler natural rechazará por PRE-2 y devuelve 409 — el cliente lo interpreta como idempotencia exitosa (el recurso ya existe en el estado esperado).

**No cruza Sinco on-prem:** este slice no emite `POST` hacia el ERP. Las sagas posteriores (`CerrarInspeccionSaga`, `SincronizarDictamenVigenteSaga`) se disparan desde `InspeccionFirmada_v1` en slices separados y llevan su propio `Idempotency-Key=InspeccionId` (ADR-003 + ADR-006).

---

## 8. Impacto en proyecciones / read models

### 8.1 `InspeccionAbiertaPorEquipoView` — migración FU-13

Este es el primer slice que maneja `InspeccionFirmada_v1`. De acuerdo con FU-13 (`FOLLOWUPS.md #13`), este es el momento natural para migrar la proyección de `session.Insert(view)` directo en el handler a `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>` registrada `Inline` en Marten.

**Alcance de la migración FU-13 en este slice:**

1. Registrar `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>` en `StoreOptions` de `Program.cs`.
2. Registrar la misma proyección en `StoreOptions` de `PostgresFixture` en `Application.Tests`.
3. Agregar `DeleteEvent<InspeccionAbiertaPorEquipoView>` (keyed por `EquipoId`) en respuesta a `InspeccionFirmada_v1` dentro de la proyección. Cuando se firma una inspección, la fila del equipo sale de la vista (el equipo queda libre para nueva inspección).
4. Eliminar el `session.Insert(view)` del handler de `IniciarInspeccion` (slice 1b) — la proyección lo maneja inline.
5. El handler de `FirmarInspeccion` **no** inserta ni borra la vista directamente. La proyección inline lo hace automáticamente en el mismo `SaveChangesAsync`.

**Resultado:** el equipo queda disponible inmediatamente para nueva inspección tras la firma. Las sagas post-firma (`CerrarInspeccionSaga`, `EjecutarOTSaga`) no necesitan tocar la vista — se disparan después del `SaveChangesAsync` del handler.

**Eventos que maneja la proyección migrada:**
- `InspeccionIniciada_v1` → upsert fila (comportamiento existente, sin cambio lógico).
- `InspeccionFirmada_v1` → **delete** fila (nuevo — disparador FU-13).
- `InspeccionCancelada_v1` → delete fila (existente en semántica, ahora centralizado en proyección).

### 8.2 `BandejaTecnicoView`

La proyección `BandejaTecnicoView` (§15.12.3) debe actualizar `Estado=Firmada`, `UltimoDictamen`, y timestamps al consumir `DiagnosticoEmitido_v1`, `DictamenEstablecido_v1` e `InspeccionFirmada_v1`. Esta proyección es un slice separado de lectura (`DetalleInspeccion` / `BandejaTecnico`) — **no está en alcance de este slice**. El agente `infra-wire` de este slice debe dejar un comentario `// TODO: actualizar BandejaTecnicoView en slice de proyecciones` en el código de proyección.

---

## 9. Impacto en endpoints HTTP

### Endpoint principal

| Campo | Valor |
|---|---|
| Método + ruta | `POST /api/v1/inspecciones/{id}/firmar` |
| Path param | `{id}` = `InspeccionId` (Guid) |
| Content-Type | `application/json` |
| Authorization | JWT del host PWA; el middleware extrae `TecnicoId` y capabilities del token |

**DTO de request (body JSON):**

```csharp
public sealed record FirmarInspeccionRequest(
    string         Diagnostico,
    DictamenOperacion Dictamen,
    string         JustificacionDictamen,
    string         FirmaUri,             // URI del blob de firma ya subido por el cliente
    UbicacionGpsDto UbicacionFirma);

public sealed record UbicacionGpsDto(
    decimal       Latitud,
    decimal       Longitud,
    decimal       PrecisionMetros,
    DateTimeOffset CapturadoEn);
```

**DTO de response (body JSON):**

```csharp
public sealed record FirmarInspeccionResponse(
    Guid            InspeccionId,
    string          Estado,              // "Firmada"
    DateTimeOffset  FirmadaEn,
    DictamenOperacion Dictamen);
```

**Códigos HTTP:**

| Escenario | Código | Notas |
|---|---|---|
| Happy path | `200 OK` | Con body `FirmarInspeccionResponse` |
| Capability ausente | `403 Forbidden` | `CapabilityRequeridaException` |
| Inspección no existe | `404 Not Found` | Marten devuelve `null` al cargar el stream |
| Inspección no en EnEjecucion | `409 Conflict` | `InspeccionNoEnEjecucionException` |
| Sin hallazgos | `422 Unprocessable Entity` | `SinHallazgosException` |
| Dictamen incoherente (V-F8) | `422 Unprocessable Entity` | `DictamenIncoherenteException` |
| Hallazgo intervención incompleto (V-F3) | `422 Unprocessable Entity` | `HallazgoIntervencionIncompletoException` |
| Firma vacía (V-F5) | `422 Unprocessable Entity` | `FirmaRequeridaException` |
| GPS nulo (V-F6) | `422 Unprocessable Entity` | `GpsRequeridoException` |
| Técnico no contribuyente | `403 Forbidden` | `TecnicoNoContribuyenteException` |

**Permiso requerido:** capability `ejecutar-inspeccion` extraída del JWT del host PWA Sinco MYE. El handler recibe el `ClaimsTecnico` construido desde los claims del JWT por el middleware del host; el dominio nunca conoce JWTs directamente.

**Header de idempotencia (ADR-008):** `X-Client-Command-Id: {UUID v7}` — obligatorio. El endpoint lo propaga como `MessageId` Wolverine. El cliente debe generarlo en el dispositivo y conservarlo para retries.

---

## 10. Impacto en SignalR / push (si aplica)

**Este slice no emite eventos SignalR.** El evento `InspeccionFirmada_v1` dispara las sagas `CerrarInspeccionSaga` y `SincronizarDictamenVigenteSaga` en slices posteriores. Son esas sagas, no este handler, las que eventualmente producen los eventos SignalR `OTGenerada`, `InspeccionCerradaSinOT` u `OTGeneracionFallida` (ADR-005 §14).

El hub `InspeccionesHub` existe por ADR-005 pero este slice no lo usa ni lo instancia.

**No aplica para este slice.**

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

**Este slice no invoca ningún endpoint Sinco on-prem.** La firma es una operación puramente de dominio local. Los POSTs hacia el ERP (P-5 verificar novedades preop, M-1 crear OT, M-W-1 actualizar dictamen vigente del equipo) los realizan las sagas `CerrarInspeccionSaga`, `EjecutarOTSaga` y `SincronizarDictamenVigenteSaga` en slices separados, usando outbox Wolverine (ADR-006).

**No aplica para este slice.**

---

## 12. Preguntas abiertas

- **P-1: V-F2 — verificación de novedades preop.** La invariante V-F2 ("Todas las novedades preop están verificadas o descartadas") aparece en §15.5 del modelo pero no está implementada en el aggregate en los slices 1a-1f: el aggregate no mantiene un contador de novedades pendientes porque el módulo no sabe cuántas hay en el preop (la lista es viva y se consulta vía REST al ERP). Opciones: (a) mantenerla como validación soft en la UX (el botón "Firmar" se deshabilita en el frontend mientras la pantalla "Importar desde preop" muestre pendientes) sin enforcement en el backend — aceptable para MVP si el frontend es confiable; (b) agregar un campo `NovedesPreopPendientesContadas: int` al aggregate que se decrementa con cada `HallazgoRegistrado_v1` de Origen PreOperacional o `NovedadPreopDescartada_v1` — requiere que el cliente envíe el conteo al iniciar; (c) diferir V-F2 al slice de novedades preop. **Decisión requerida del usuario antes de que `red` escriba el test de V-F2.** Para este spec, V-F2 se modela como **no implementada en el backend MVP** (solo UX enforcement). Si el usuario confirma esta decisión, se elimina V-F2 de las precondiciones del handler y se deja nota en el código. Si decide implementarla, el spec se actualiza antes de pasar a `red`.

- **P-2: Capability de supervisor para firmar (PRE-9).** El modelo histórico (I8 §2.1) menciona que un "supervisor" (claim `sinco_roles` = `supervisor`) puede firmar aunque no sea contribuyente. En el sistema vigente, la autorización por capabilities (no por roles) es la convención (ADR-007). ¿Existe una capability `firmar-inspeccion-ajena` o similar para supervisores? ¿O la invariante PRE-9 se restringe solo a contribuyentes y cualquier supervisor usa el mismo flow? **Decisión requerida del usuario.** Para este spec, PRE-9 permite firmar solo a contribuyentes del stream. Si el usuario confirma capability de supervisor, se agrega al `ClaimsTecnico` y a PRE-9.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-1..PRE-9) mapean a al menos un escenario Then en §6.
- [x] Todas las invariantes tocadas (V-F1, V-F3..V-F8, I-F1) mapean a un escenario Then.
- [x] El happy path (§6.1) está presente.
- [x] El escenario de rebuild desde stream (§6.17) está presente con los 3 eventos en orden causal.
- [x] §7 (idempotencia) está decidido: no es naturalmente idempotente; ADR-008 `X-Client-Command-Id` cubre replay de red; segundo intento humano recibe 409.
- [x] §10 (SignalR) marcado explícitamente como "no aplica para este slice".
- [x] §11 (adapters Sinco on-prem) marcado explícitamente como "no aplica para este slice".
- [x] FU-13 (`InspeccionAbiertaPorEquipoView` → `MultiStreamProjection`) incluido en §8.1 con alcance detallado.
- [ ] §12 Preguntas abiertas: 2 preguntas que requieren decisión del usuario (P-1 V-F2 enforcement, P-2 capability supervisor). **El spec no puede firmarse hasta que el usuario responda P-1 y P-2.**
