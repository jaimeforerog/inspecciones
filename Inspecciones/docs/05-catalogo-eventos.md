# 05 — Catálogo de eventos del módulo Inspecciones (MVP)

> **Fuente única de verdad**: §15 de `01-modelo-dominio.md`. Si este catálogo y `01-modelo-dominio.md` divergen, gana §15. Las divergencias respecto a versiones anteriores del modelo se anotan inline con ⚠️.
>
> **Total MVP: 20 eventos** = 16 (aggregate `Inspeccion`) + 1 (NovedadPreopDescartada) + 3 (aggregate `SeguimientoHallazgo`). El evento de integración con MYE (`OTGeneracionFallida_v1`) cuenta dentro de los 16 del aggregate `Inspeccion`.
>
> **Mediciones diferidas a fase 2**: `MedicionRegistrada_v1` y `MedicionActualizada_v1` NO entran en el MVP (§15.4).

---

## 1. Tabla maestra

| #  | Evento                          | Aggregate            | Comando que lo dispara                | Side-effects (saga / proyección)                                          |
|----|---------------------------------|----------------------|---------------------------------------|---------------------------------------------------------------------------|
| 1  | `InspeccionIniciada_v1`         | Inspeccion           | `IniciarInspeccion`                   | Crea aggregate. Proyección bandeja "En curso".                            |
| 2  | `HallazgoRegistrado_v1`         | Inspeccion           | `RegistrarHallazgo`                   | Acumula en `_hallazgos`. Refresca contadores en pantalla 3.               |
| 3  | `HallazgoActualizado_v1`        | Inspeccion           | `EditarHallazgo`                      | `record with` sobre el hallazgo. Audit en stream.                         |
| 4  | `HallazgoEliminado_v1`          | Inspeccion           | `EliminarHallazgo`                    | Soft delete (flag `Eliminado`). Filtra de UI. Bloquea si tiene hijos.     |
| 5  | `NovedadPreopDescartada_v1`     | Inspeccion           | `DescartarNovedadPreop`               | NO crea hallazgo. Saga: `POST /preop/novedades/{id}/descartar`.           |
| 6  | `RepuestoEstimado_v1`           | Inspeccion           | `EstimarRepuesto`                     | Acumula en `_repuestos`. Saga consolida BOM por SKU al cerrar.            |
| 7  | `RepuestoActualizado_v1`        | Inspeccion           | `EditarRepuestoEstimado`              | `record with` sobre el repuesto. Cambia cantidad/justificación.           |
| 8  | `RepuestoRemovido_v1`           | Inspeccion           | `RemoverRepuestoEstimado`             | Hard delete del HashSet. Stream conserva `Estimado` + `Removido`.         |
| 9  | `AdjuntoSubido_v1`              | Inspeccion           | `AdjuntarArchivo`                     | Hashset de adjuntos del hallazgo. SAS upload directo a Blob Storage.      |
| 10 | `AdjuntoEliminado_v1`           | Inspeccion           | `EliminarAdjunto`                     | Soft delete (HashSet `_adjuntosEliminados`). Blob físico no se borra.     |
| 11 | `DiagnosticoEmitido_v1`         | Inspeccion           | `Firmar` (atómico con #12 y #13)      | Snapshot de diagnóstico final del técnico.                                |
| 12 | `DictamenEstablecido_v1`        | Inspeccion           | `Firmar` (atómico)                    | Apto / AptoConRestricciones / NoApto. V-F4 obliga seleccionarlo.          |
| 13 | `InspeccionFirmada_v1`          | Inspeccion           | `Firmar` (atómico)                    | Sella aggregate (I-F1 inmutable). Dispara `CerrarInspeccionSaga`.         |
| 14 | `InspeccionCerrada_v1`          | Inspeccion           | (saga, tras éxito MYE)                | Push SignalR `OTGenerada`. Cierra. Lleva `OTCorrectivaIdSinco` + número.  |
| 15 | `InspeccionCerradaSinOT_v1`     | Inspeccion           | (saga, sin hallazgos con intervención)| Push SignalR. Cierra. NO contacta MYE.                                    |
| 16 | `InspeccionCancelada_v1`        | Inspeccion           | `CancelarInspeccion`                  | Solo válido en `EnEjecucion`. Estado terminal.                            |
| 17 | `OTGeneracionFallida_v1`        | Inspeccion           | (saga, fallo de MYE)                  | Estado `CierrePendienteOT`. Wolverine reintenta. Email a supervisor.      |
| 18 | `SeguimientoAbierto_v1`         | SeguimientoHallazgo  | (saga al firmar, si AccionRequerida=RequiereSeguimiento) | Crea aggregate transversal al equipo. Estado `Abierto`. |
| 19 | `SeguimientoResuelto_v1`        | SeguimientoHallazgo  | `ResolverSeguimiento`                 | Cierra "Sin intervención". Inmutable.                                     |
| 20 | `SeguimientoEscalado_v1`        | SeguimientoHallazgo  | `EscalarSeguimiento`                  | Cierra "Con intervención". Coemite `HallazgoRegistrado_v1` en inspección actual. |

**Convención de naming** (§8 modelo de dominio): los eventos de dominio usan `<Sustantivo><Verbo>Pasado_vN`. Los DTOs de request HTTP al adapter MYE usan `<Verbo><Sustantivo>Request_vN` (ej. `CrearOTCorrectivaRequest_v1`) y NO se persisten.

---

## 2. Fichas por evento — aggregate `Inspeccion`

### 1. `InspeccionIniciada_v1`

**Crea el aggregate.** Único evento que arranca el stream. El comando `IniciarInspeccion` lo emite tras validar que el técnico tenga acceso al proyecto del equipo (claim `sinco_obras` del JWT, mantenido literal del lado ERP — el módulo lo consume como "proyectos") y que no exista otra inspección activa para ese equipo (I1b — bloqueo por equipo).

```csharp
public sealed record InspeccionIniciada_v1(
    Guid InspeccionId,
    int EquipoId,
    int RutinaId,                 // derivada del Grupo del equipo (no la elige el técnico)
    string RutinaCodigo,           // legible para UI ("INSP. BULL.MOTOR")
    string TecnicoIniciador,
    int ProyectoId,
    UbicacionGps Ubicacion,        // OBLIGATORIO — GPS del teléfono al iniciar
    DateTime IniciadaEn);
```

**Invariantes / validaciones**: I1b (no haya otra inspección `EnEjecucion` para el equipo), técnico autorizado para el proyecto, GPS no vacío.

**Apply**: setea identidad del aggregate, estado `EnEjecucion`, agrega al técnico iniciador a `TecnicosContribuyentes`.

**Camino aditivo a Monitoreo (§12.11)**: cuando se priorice, el evento gana campo `Tipo: TipoInspeccion` (Tecnica | Monitoreo). En MVP solo hay Técnica, así que el campo se omite.

---

### 2. `HallazgoRegistrado_v1`

Único evento de hallazgo en el MVP. Reemplazó a `HallazgoEnRutina_v2`, `HallazgoFueraDeRutina_v2`, `HallazgoDescubierto_v1` y a `NovedadPreopVerificada_v1` (consolidación 2026-04-27, §12.10.9 + §15).

```csharp
public sealed record HallazgoRegistrado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    OrigenHallazgo Origen,                  // PreOperacional | Manual
    int? NovedadPreopId,                   // !=null si Origen=PreOperacional (I-H2)
    int ParteEquipoId,                     // SIEMPRE obligatorio (I-H1)
    int? ActividadId,                      // del catálogo si Origen=PreOperacional (xor I12)
    string? ActividadDescripcion,           // texto libre si Origen=Manual (xor I12)
    string NovedadTecnica,                  // texto libre obligatorio (I11)
    AccionRequerida AccionRequerida,        // NoRequiereIntervencion | RequiereSeguimiento | RequiereIntervencion
    string? AccionCorrectiva,               // !=null si AccionRequerida=RequiereIntervencion (I-H4)
    int? CausaFallaId,                     // !=null si AccionRequerida∈{Seguimiento, Intervencion} (I-H4)
    int? TipoFallaId,                      // !=null si AccionRequerida∈{Seguimiento, Intervencion} (I-H4)
    string? ObservacionCampo,
    UbicacionGps? Ubicacion,                // GPS al registrar (opcional)
    string EmitidoPor,
    DateTime RegistradoEn);
```

> ⚠️ **Cambio respecto a versión intermedia**: el campo `ResultadoVerificacion` fue eliminado del payload (§15.2). La decisión Verificar/Seguimiento/Descartar se expresa por el botón de la variante B (§15.9). Para Descartar, NO se emite este evento — se emite `NovedadPreopDescartada_v1`.

**Cuándo se emite**:
- Botón "+ Agregar hallazgo" (`Origen=Manual`) → wizard 1 o 2 pasos según AccionRequerida.
- Botón "✓ Verificar" en lista preop (`Origen=PreOperacional`) → wizard heredando ParteEquipoId/ActividadId de la novedad.
- Botón "↻ Seguimiento" en lista preop → mini-modal con motivo, emite con `AccionRequerida=RequiereSeguimiento`.
- Botón "⚠ Intervención" en seguimientos previos → coemitido con `SeguimientoEscalado_v1` (§15.8.4).

**Invariantes principales**:
- I-H1: `ParteEquipoId` siempre presente.
- I-H4: si `AccionRequerida = RequiereIntervencion` → `TipoFallaId` y `CausaFallaId` obligatorios.
- I-H5: si `AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}` → tipo/causa pueden ser null (opcionales).
- I12 / I12b: xor de Actividad y NovedadPreop según Origen.
- I13: `ParteEquipoId` debe estar en partes aplicables a `RutinaId` del aggregate.

**Validaciones extra pre-firma** (V-F3): si AccionRequerida=RequiereIntervencion, también requiere ≥1 adjunto. Repuestos son opcionales — la intervención puede ser solo mano de obra (ajuste, calibración, limpieza).

---

### 3. `HallazgoActualizado_v1`

Edita campos de un hallazgo ya registrado. Solo válido mientras la inspección esté `EnEjecucion`.

```csharp
public sealed record HallazgoActualizado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    int ParteEquipoId,
    int? ActividadId,
    string? ActividadDescripcion,
    string NovedadTecnica,
    AccionRequerida AccionRequerida,
    string? AccionCorrectiva,
    int? CausaFallaId,
    int? TipoFallaId,
    string? ObservacionCampo,
    string EmitidoPor,
    DateTime ActualizadoEn);
```

**Lo que NO se puede cambiar** (I-H8): `Origen`, `NovedadPreopId`, `HallazgoId`. Para "cambiar origen" se elimina el hallazgo y se crea uno nuevo.

**Casos de uso**: subir foto que faltó, cambiar de "RequiereIntervencion" a "RequiereSeguimiento" antes de firmar para evitar que la saga genere OT (§15.6).

**Apply**: `record with` sobre el hallazgo en `_hallazgos`; agrega `EmitidoPor` a `TecnicosContribuyentes`.

---

### 4. `HallazgoEliminado_v1`

Soft delete. Marca el hallazgo como eliminado pero conserva la entrada en `_hallazgos` para audit.

```csharp
public sealed record HallazgoEliminado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    string Motivo,
    string EmitidoPor,
    DateTime EliminadoEn);
```

**Invariantes**:
- I2: inspección en `EnEjecucion`.
- I-H9: bloqueado si el hallazgo tiene repuestos o adjuntos no eliminados (el técnico debe limpiarlos antes).

**Proyección UI**: filtra eliminados al renderizar pantalla 3.

---

### 5. `NovedadPreopDescartada_v1`

Único evento del aggregate `Inspeccion` que **no** crea ni edita un hallazgo. Solo registra que el técnico desestimó una novedad del operario al revisar la lista (variante B, botón rojo "✗ Descartar").

```csharp
public sealed record NovedadPreopDescartada_v1(
    Guid InspeccionId,
    int NovedadPreopId,
    string Motivo,                  // texto libre obligatorio
    string EmitidoPor,
    DateTime DescartadaEn);
```

**Razón de existir**: en versiones intermedias el descarte se modelaba como un `HallazgoRegistrado_v1` con `ResultadoVerificacion=Descartada` y `AccionRequerida=NoRequiereIntervencion`. Era ruido — el técnico no está describiendo un hallazgo, está negando que exista. El §15 lo separó.

**Side-effect (saga al cerrar)**: `POST /api/v1/preop/novedades/{NovedadPreopId}/descartar` con motivo y EmitidoPor. La novedad queda cerrada en el ERP y NO aparece en próximas inspecciones.

**Apply**: agrega `NovedadPreopId` al HashSet `_novedadesDescartadas` para cumplir V-F2 (todas las novedades preop deben estar verificadas o descartadas antes de firmar).

---

### 6. `RepuestoEstimado_v1`

Antes `RepuestoEstimadoAgregado_v1`; renombrado en §15.4 para uniformar con `RepuestoActualizado_v1` / `RepuestoRemovido_v1`.

```csharp
public sealed record RepuestoEstimado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid RepuestoEstimadoId,
    int SkuId,
    decimal Cantidad,                // > 0 (permite galones, litros, fracciones)
    string Unidad,                   // derivada del catálogo SKU al guardar (NO viene del comando)
    string Justificacion,            // texto libre obligatorio
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime EstimadoEn);
```

**Validaciones del handler**:
- I2: estado = `EnEjecucion`.
- `Cantidad > 0`.
- `Justificacion` no vacía.
- **Hard error de compatibilidad SKU↔Parte**: el handler rechaza si `ParteEquipoId` del hallazgo NO está en `RepuestoLocal.ParteIdsCompatibles[SkuId]`. Asume catálogo bien mantenido (confirmado §3.2 brief).
- I10: si `AccionRequerida ≠ RequiereIntervencion` para el hallazgo, rechaza (UI también bloquea).
- SKU no duplicado entre repuestos **activos** (los removidos no cuentan, permite re-agregar).

**Saga de cierre**: consolida BOM agrupando por `SkuId` para enviar a MYE.

---

### 7. `RepuestoActualizado_v1`

Antes `RepuestoEstimadoActualizado_v1`. Edita cantidad y/o justificación sin remover (§12.10.14).

```csharp
public sealed record RepuestoActualizado_v1(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    decimal Cantidad,                // valor nuevo
    string Justificacion,            // valor nuevo
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime ActualizadoEn);
```

**No se permite cambiar**: `SkuId`, `Unidad`, `HallazgoId`. Para cambiar SKU se remueve y se vuelve a agregar.

**Razón**: caso operativo común "estimé 1, eran 2"; antes requería remover y re-agregar (5+ taps), ahora 3.

---

### 8. `RepuestoRemovido_v1`

Antes `RepuestoEstimadoRemovido_v1`. Hard delete del estado (`_repuestos.RemoveAll`); el stream conserva `Estimado_v1` + `Removido_v1` para audit.

```csharp
public sealed record RepuestoRemovido_v1(
    Guid InspeccionId,
    Guid RepuestoEstimadoId,
    string? Motivo,                  // OPCIONAL — el técnico puede remover sin justificar
    UbicacionGps? Ubicacion,
    string EmitidoPor,
    DateTime RemovidoEn);
```

**Detalle**: la UI muestra modal corto con motivo opcional (Cancelar / Eliminar). Re-agregar el mismo SKU está permitido (el validador de "no duplicado" solo cuenta repuestos activos).

---

### 9. `AdjuntoSubido_v1`

Antes `AdjuntoAgregado_v1`. Renombrado en §15.4.

```csharp
public sealed record AdjuntoSubido_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid AdjuntoId,
    string BlobUri,                  // ya subido por el cliente vía SAS
    string MimeType,                 // image/jpeg, image/png, image/heic, image/webp, application/pdf
    int TamanoBytes,                 // ≤ 3 MB tras compresión cliente
    string Sha256,                   // integridad — el handler verifica
    UbicacionGps? Ubicacion,         // GPS al capturar (refuerza EXIF)
    string EmitidoPor,
    DateTime SubidoEn);
```

**Patrón de upload** (§12.10.11): cliente solicita SAS al backend → sube directo al Blob Storage → envía comando `AdjuntarArchivo` con BlobUri + sha256. Backend solo valida, no procesa binarios.

**Validaciones**:
- Tipos permitidos: JPEG, PNG, HEIC, WebP, PDF. Video diferido.
- ≤ 3 MB. Cliente comprime imágenes a 1920x1920 + JPEG 75%.
- ≤ 5 adjuntos no eliminados por hallazgo (hard limit).
- EXIF preservado (no se sanitiza geolocalización embedded — refuerza el GPS del evento como doble dato).

---

### 10. `AdjuntoEliminado_v1`

Soft delete del adjunto. El blob físico NO se borra (innecesario y costoso).

```csharp
public sealed record AdjuntoEliminado_v1(
    Guid InspeccionId,
    Guid HallazgoId,
    Guid AdjuntoId,
    string Motivo,
    string EmitidoPor,
    DateTime EliminadoEn);
```

**Apply**: agrega `AdjuntoId` al HashSet `_adjuntosEliminados`. Cuenta adjuntos activos = `_adjuntos.Except(_adjuntosEliminados)`.

**Acceso post-cierre**: solo lectura. Adjuntos eliminados no aparecen en proyección UI.

---

### 11. `DiagnosticoEmitido_v1`

**Atómico con `DictamenEstablecido_v1` y `InspeccionFirmada_v1`** — los tres se emiten en el mismo `Apply` cuando el técnico firma. Capturan el snapshot del juicio técnico justo antes de sellar la inspección.

```csharp
public sealed record DiagnosticoEmitido_v1(
    Guid InspeccionId,
    string DiagnosticoFinal,         // texto libre escrito por el técnico al firmar
    string EmitidoPor,
    DateTime EmitidoEn);
```

---

### 12. `DictamenEstablecido_v1`

Atómico con #11 y #13.

```csharp
public sealed record DictamenEstablecido_v1(
    Guid InspeccionId,
    DictamenOperacion Dictamen,      // Apto | AptoConRestricciones | NoApto
    string Justificacion,
    string EmitidoPor,
    DateTime EstablecidoEn);
```

**V-F4** obliga a seleccionarlo antes de habilitar el botón firmar. La invariante histórica I7 ("severidad crítica → dictamen NoApto") fue suavizada en §15: ahora es **sugerencia** (UI advierte) pero no bloquea — el técnico puede firmar Apto aunque haya hallazgo `RequiereIntervencion` si tiene justificación.

---

### 13. `InspeccionFirmada_v1`

El gran sello. Atómico con #11 y #12. Después de este evento el aggregate es **inmutable** (I-F1).

```csharp
public sealed record InspeccionFirmada_v1(
    Guid InspeccionId,
    string FirmadoPor,               // debe ser técnico contribuyente o supervisor (I8)
    string FirmaUri,                 // blob URI de la imagen manuscrita capturada en pantalla
    DateTime FirmadaEn);
```

**Validaciones pre-firma V-F1 a V-F7** (todas bloqueantes, revalidadas backend):
- V-F1: ≥1 hallazgo registrado.
- V-F2: todas las novedades preop verificadas o descartadas.
- V-F3: para cada hallazgo con `AccionRequerida=RequiereIntervencion` → tipo + causa + ≥1 adjunto. Repuestos opcionales (BOM puede ser vacío; MYE acepta OT correctiva sin repuestos).
- V-F4: dictamen seleccionado.
- V-F5: firma manuscrita capturada (`FirmaUri` no vacío).
- V-F6: `UbicacionFirma` capturada (GPS al firmar).
- V-F7: estado actual = `EnEjecucion`.

**Side-effect**: dispara `CerrarInspeccionSaga` (§15.6).

---

### 14. `InspeccionCerrada_v1`

Emitido **por la saga**, no por un comando del técnico, tras éxito de la integración con MYE.

```csharp
public sealed record InspeccionCerrada_v1(
    Guid InspeccionId,
    int OTCorrectivaIdSinco,        // identificador técnico interno
    string OTCorrectivaNumero,       // ej. "OT-123456" — visible al usuario
    DateTime CerradaEn);
```

**Cuándo**: solo si EXISTE hallazgo no eliminado con `AccionRequerida=RequiereIntervencion` y MYE responde 200 con `OTCorrectivaId`.

**Side-effect SignalR (ADR-005)**: el proyector lateral publica `OTGenerada` al hub `InspeccionesHub` con `OTId+OTNumero`. Latencia típica <100ms vs polling.

---

### 15. `InspeccionCerradaSinOT_v1`

Emitido por la saga cuando NO hay hallazgos con intervención (todos `NoRequiereIntervencion` o `RequiereSeguimiento`). NO contacta MYE.

```csharp
public sealed record InspeccionCerradaSinOT_v1(
    Guid InspeccionId,
    DateTime CerradaEn);
```

**Side-effect SignalR**: publica `InspeccionCerradaSinOT` al hub. Pantalla 7b muestra check verde "Cerrada — sin OT".

**Side-effect Seguimientos**: la saga abre un `SeguimientoAbierto_v1` por cada hallazgo con `AccionRequerida=RequiereSeguimiento` (§15.8.3).

---

### 16. `InspeccionCancelada_v1`

Cancelación explícita por el técnico (o supervisor) antes de firmar. Estado terminal alterno a Cerrada/CerradaSinOT.

```csharp
public sealed record InspeccionCancelada_v1(
    Guid InspeccionId,
    string Motivo,
    string CanceladaPor,
    DateTime CanceladaEn);
```

**Validaciones**: I2 (solo desde `EnEjecucion`). Motivo no vacío. No se puede cancelar una inspección firmada (I-F1).

---

### 17. `OTGeneracionFallida_v1`

Emitido por la saga cuando MYE responde 4xx/5xx o timeout al intentar crear la OT correctiva.

```csharp
public sealed record OTGeneracionFallida_v1(
    Guid InspeccionId,
    string TipoFallo,                // "ValidacionMYE" | "Timeout" | "ServerError" | ...
    string MensajeError,             // detalle devuelto por MYE o exception
    int IntentoNumero,
    string? CodigoErrorMYE,          // si MYE devolvió código clasificable
    DateTime FallidaEn);
```

**Comportamiento**:
- 4xx (validación, equipo no existe): `CierrePendienteOT`, NO reintenta automático, notifica supervisor.
- 5xx / timeout: Wolverine reintenta con backoff exponencial. Tras N intentos, dead-letter.
- 200 con OT ID: emite #14 `InspeccionCerrada_v1`.

**Idempotency-Key** = `InspeccionId` (la saga reintenta con la misma key).

**Estados permitidos en `CierrePendienteOT`** (I-F2):
- Reintentar OT (encolar saga otra vez, máx 1 vez técnico + N veces back-office).
- Back-office puede reasignar / corregir payload.

**SignalR**: publica `OTGeneracionFallida` con motivo. Pantalla 7c warning rojo + nota a supervisor.

---

## 3. Fichas por evento — aggregate `SeguimientoHallazgo`

> **Justificación del aggregate** (§15.8.1): antes del 2026-04-28, un hallazgo con `AccionRequerida=RequiereSeguimiento` quedaba enterrado en una inspección cerrada (inmutable) sin mecanismo para cerrarlo después. El aggregate `SeguimientoHallazgo` es **transversal al equipo** (sigue al equipo cross-proyecto) y resuelve este gap.

### 18. `SeguimientoAbierto_v1`

Lo emite **la saga** `CerrarInspeccionSaga` al firmar la inspección que originó el seguimiento. Crea el aggregate. NO lo dispara un comando del técnico.

```csharp
public sealed record SeguimientoAbierto_v1(
    Guid SeguimientoId,
    int EquipoId,
    Guid HallazgoOrigenId,           // ref al hallazgo que originó (en inspección cerrada)
    Guid InspeccionOrigenId,
    int ParteEquipoId,
    string DescripcionOrigen,        // copia del NovedadTecnica del hallazgo
    string AbiertoPor,               // técnico que firmó la inspección original
    DateTime AbiertoEn);
```

**SLA visual** (§15.8.6):
- 0–30 días: badge azul "Abierto".
- 30–90 días: badge naranja "Atención".
- +90 días: badge rojo "Vencido" + email diario al supervisor del equipo (job nocturno Wolverine).

---

### 19. `SeguimientoResuelto_v1`

Lo dispara `ResolverSeguimiento` cuando un técnico (no necesariamente el que lo abrió) decide que el problema ya no requiere intervención. Estado terminal `Resuelto`.

```csharp
public sealed record SeguimientoResuelto_v1(
    Guid SeguimientoId,
    Guid InspeccionCierreId,         // inspección desde la que se cierra
    string CerradoPor,
    DateTime CerradoEn,
    string MotivoCierre);            // texto libre obligatorio
```

**Validaciones**: estado actual = `Abierto`. `MotivoCierre` no vacío.

---

### 20. `SeguimientoEscalado_v1`

Lo dispara `EscalarSeguimiento` cuando un técnico decide que el problema sí requiere OT. **Coemite** un `HallazgoRegistrado_v1` con `Origen=Manual` y `AccionRequerida=RequiereIntervencion` en la inspección actual, y deja el seguimiento en estado terminal `Escalado`.

```csharp
public sealed record SeguimientoEscalado_v1(
    Guid SeguimientoId,
    Guid InspeccionCierreId,         // inspección actual donde se crea el nuevo hallazgo
    Guid HallazgoEscaladoId,         // ref al nuevo hallazgo creado
    string EscaladoPor,
    DateTime EscaladoEn);
```

**Validaciones**: estado = `Abierto`. La inspección referenciada por `InspeccionCierreId` debe estar `EnEjecucion`.

> "Seguimiento" en el botón naranja **NO emite evento** (§15.8.5 decisión #2). Es no-op silencioso — solo feedback visual (toast + card resaltada). Si más adelante se requiere reportería de "¿hace cuánto nadie revisa?", se agrega `SeguimientoRevisadoSinCambio_v1` como cambio aditivo.

---

## 4. Patrón unificado de las 3 opciones (§15.9)

Las **mismas 3 opciones de `AccionRequerida`** se usan en los 3 lugares donde el técnico decide qué hacer con un hallazgo, novedad o seguimiento:

| Contexto                         | "Sin intervención"                          | "Seguimiento"                                | "Intervención"                                                              |
|----------------------------------|---------------------------------------------|----------------------------------------------|-----------------------------------------------------------------------------|
| **Hallazgo manual** (wizard)     | `HallazgoRegistrado_v1` + `NoRequiereIntervencion` | `HallazgoRegistrado_v1` + `RequiereSeguimiento` | `HallazgoRegistrado_v1` + `RequiereIntervencion` (paso 2 obligatorio)       |
| **Novedad preop** (variante B)   | ✗ Descartar — `NovedadPreopDescartada_v1` (sin hallazgo) | ↻ Seguimiento — `HallazgoRegistrado_v1` + `RequiereSeguimiento` | ✓ Verificar — `HallazgoRegistrado_v1` + `RequiereIntervencion`             |
| **Seguimiento previo** (nuevo)   | ✓ Sin intervención — `SeguimientoResuelto_v1` | ↻ Seguimiento — **no-op silencioso**         | ⚠ Intervención — `SeguimientoEscalado_v1` + `HallazgoRegistrado_v1`        |

Mismos colores (verde/amarillo/rojo) en los 3 contextos.

---

## 5. Eventos diferidos a fase 2 (NO MVP)

| Evento                       | Razón del diferimiento                                                                 |
|------------------------------|----------------------------------------------------------------------------------------|
| `MedicionRegistrada_v1`      | Más natural en flujo de inspección de monitoreo (futuro). Para inspección técnica MVP, info crítica ya está en hallazgo + repuestos + diagnóstico. |
| `MedicionActualizada_v1`     | Sigue al diferimiento de mediciones.                                                   |
| `SeguimientoRevisadoSinCambio_v1` | Solo se agrega si emerge necesidad de reportería de "hace cuánto nadie revisa". Cambio aditivo. |
| `TecnicoSeIncorporo_v1`      | Eliminado. La lista `TecnicosContribuyentes` se deriva del campo `EmitidoPor` de los demás eventos. |
| Eventos de programación previa (`InspeccionProgramada_v1`, etc.) | Flujo MVP es ad-hoc. Programación es agregable aditivamente cuando se priorice. |

---

## 6. Eventos eliminados durante el modelado (NO existen)

Para evitar confusión al leer commits viejos o el histórico §2.1-§14:

| Eliminado                       | Reemplazado por                                                                        |
|---------------------------------|----------------------------------------------------------------------------------------|
| `HallazgoDescubierto_v1`        | `HallazgoRegistrado_v1` (§12.10.2)                                                     |
| `HallazgoEnRutina_v1` / `_v2`   | `HallazgoRegistrado_v1` (rutina ya no es checklist con items)                          |
| `HallazgoFueraDeRutina_v1` / `_v2` | `HallazgoRegistrado_v1`                                                                |
| `NovedadPreopVerificada_v1`     | `HallazgoRegistrado_v1` con `Origen=PreOperacional` (§12.10.9) + `NovedadPreopDescartada_v1` para descarte |
| `HallazgoVinculadoComoDuplicado_v1` | Se prefiere `HallazgoEliminado_v1` con motivo "duplicado".                             |
| `OTCorrectivaSugerida_v1`       | Renombrado a DTO `CrearOTCorrectivaRequest_v1` (no es evento de dominio, §8 naming).   |

---

## 7. Notas de versionado y publicación

- Todos los eventos del MVP son `_v1`. No hay `_v2` en producción.
- Cuando un evento necesite cambio incompatible: crear `_v2` y registrar **upcaster** en Marten que lea `_v1` y produzca `_v2` para el aggregate. Productores nuevos emiten `_v2` directo.
- Para cambios aditivos compatibles (campo opcional, enum nuevo): mantener `_v1` y agregar el campo. Marten tolera campos faltantes en JSON antiguo.
- **Publicación a integración (REST sobre VPN)**: el módulo no publica eventos a un bus corporativo en MVP (ADR-001). La integración hacia afuera se hace desde la saga `CerrarInspeccionSaga` con llamadas REST específicas. Si en el futuro aparece un bus corporativo Sinco o un segundo consumidor, se agrega outbox + publisher como capa aditiva.

---

## 8. Referencias cruzadas

- §15 de `01-modelo-dominio.md` — fuente de verdad final.
- §8 de `01-modelo-dominio.md` — convención naming evento-de-dominio vs DTO-de-request.
- §12.10 de `01-modelo-dominio.md` — historia de la consolidación del evento de hallazgo.
- §12.11 de `01-modelo-dominio.md` — eliminación de `TipoRutina` y nuevo enum `TipoInspeccion`.
- §13 de `01-modelo-dominio.md` — ADR-003 (generación de OT correctiva en MYE).
- §14 de `01-modelo-dominio.md` — ADR-005 (SignalR para push real-time del cierre).
- §9.11 de `00-investigacion-mercado.md` — ADR-001 (REST sobre VPN vs CDC + Service Bus).
- §9.15 de `00-investigacion-mercado.md` — ADR-004 (sincronización de catálogos de referencia).
- `Plantillas Excel/mock del diseño.docx` — fuente visual vigente desde 2026-04-30 (mock de Daniel, 13 pantallas en 4 secciones).
- `02d-wireframes-seguimientos.html` — flujo nuevo del aggregate Seguimiento (sigue vigente).
- ~~`02-wireframes-mobile.html`~~ y ~~`02b-wireframes-novedades-preop.html`~~ — eliminados el 2026-04-30 (superseded por mock de Daniel).
