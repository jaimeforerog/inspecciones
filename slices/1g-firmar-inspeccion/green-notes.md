# Green notes — Slice 1g — FirmarInspeccion

**Autor:** green
**Fecha:** 2026-05-07
**Estado:** BLOQUEADO — 1 test con bug en helper devuelto a RED para corrección

---

## Resumen ejecutivo

La implementación completa del slice está lista (dominio, handler, endpoint HTTP, migración FU-13). 21/22 tests del slice pasan en verde. 1 test (`§6.15 PRE-8 V-F6`) no puede pasar porque el helper del test tiene un bug que impide pasar `null` al aggregate.

---

## Bug en test §6.15 — devolver a RED para corrección

### Test afectado

`FirmarInspeccion_con_UbicacionFirma_nula_lanza_GpsRequeridoException_PRE_8_V_F6`
(`FirmarInspeccionTests.cs` línea 588)

### Descripción del bug

El test usa el helper `ComandoFirmarBasico(ubicacionFirma: null)`. El helper tiene:

```csharp
private static FirmarInspeccion ComandoFirmarBasico(
    ...
    UbicacionGps? ubicacionFirma = null) =>
    new(...,
        UbicacionFirma: ubicacionFirma ?? UbicacionFirmaEjemplo(),  // BUG: null → valor real
        ...)
```

El `??` substituye `null` con `UbicacionFirmaEjemplo()`. El record `FirmarInspeccion` recibe un valor no-nulo en `UbicacionFirma`. PRE-8 del aggregate verifica `cmd.UbicacionFirma is null` — nunca se activa.

### Corrección necesaria (para RED)

El test §6.15 debe construir el record directamente sin el helper `??`:

```csharp
var cmd = new FirmarInspeccion(
    InspeccionId: InspeccionIdNueva,
    Diagnostico: "Inspección sin hallazgos críticos",
    Dictamen: DictamenOperacion.PuedeOperar,
    JustificacionDictamen: "Equipo en buen estado",
    FirmaUri: "https://blobs/firma-01.png",
    UbicacionFirma: null,  // null explícito — no pasar por el helper
    TecnicoId: "rmartinez");
```

Alternativamente, quitar el `??` del helper y actualizar todos los callers que no pasan `ubicacionFirma` explícitamente.

---

## Implementación completada

### Archivos creados (nuevos)

| Archivo | Contenido |
|---|---|
| `src/Inspecciones.Application/Inspecciones/FirmarInspeccionHandler.cs` | Handler con PRE-1, PRE-4, carga aggregate, delega PRE-2..PRE-9, un único SaveChangesAsync |
| `src/Inspecciones.Application/Inspecciones/InspeccionAbiertaPorEquipoProjection.cs` | EventProjection inline FU-13: InspeccionIniciada_v1→Insert, InspeccionFirmada_v1→Delete, InspeccionCancelada_v1→Delete |
| `src/Inspecciones.Api/Inspecciones/FirmarInspeccionRequest.cs` | DTO de entrada del endpoint HTTP |
| `src/Inspecciones.Api/Inspecciones/FirmarInspeccionResponse.cs` | DTO de salida del endpoint HTTP |

### Archivos modificados

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Implementación de `Firmar(FirmarInspeccion cmd, DateTimeOffset ahora)` — precondiciones PRE-2..PRE-9 en orden, 3 eventos atómicos |
| `src/Inspecciones.Application/Inspecciones/Excepciones.cs` | Agregada `DiagnosticoRequeridoException` (PRE-4 handler) |
| `src/Inspecciones.Application/Inspecciones/IniciarInspeccionHandler.cs` | FU-13: quitado `session.Insert(view)` directo — la proyección lo maneja inline |
| `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Agregado endpoint `POST /api/v1/inspecciones/{id}/firmar` |
| `src/Inspecciones.Api/Program.cs` | Registrada `InspeccionAbiertaPorEquipoProjection` inline + `FirmarInspeccionHandler` como Scoped |
| `tests/Inspecciones.Application.Tests/Inspecciones/PostgresFixture.cs` | FU-13: registrada `InspeccionAbiertaPorEquipoProjection` inline en el fixture |

---

## Estado de tests

```
dotnet test tests/Inspecciones.Domain.Tests/ --verbosity minimal
Con error! - Con error: 1, Superado: 95, Omitido: 0, Total: 96
```

- 95/96 tests pasan.
- 1 test falla: `FirmarInspeccion_con_UbicacionFirma_nula_lanza_GpsRequeridoException_PRE_8_V_F6` — bug en helper del test (no en código de producción).
- Tests de Application (Testcontainers) fallan por Docker no disponible en entorno local — fallo preexistente, no relacionado con este slice.

```
dotnet build: 0 warnings, 0 errors
```

---

## Decisiones de implementación

### 1. Orden de precondiciones en `Firmar`

Implementado exactamente como el orden documentado en red-notes §5:
PRE-2 → PRE-3 → PRE-7 → PRE-8 → PRE-9 → PRE-5 → PRE-6

### 2. EventProjection en lugar de MultiStreamProjection (FU-13)

La spec §8.1 pide `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>`. Pero `InspeccionFirmada_v1` y `InspeccionCancelada_v1` no contienen `EquipoId` — no hay forma de resolver la clave del documento para el `DeleteEvent` sin hacer una query de estado.

Se usó `EventProjection` con métodos `async Task Project(InspeccionFirmada_v1 e, IQuerySession session, IDocumentOperations ops)` que carga la fila existente y la elimina. La defensa I-I1 se preserva usando `ops.Insert(view)` (INSERT puro, no upsert) en el handler de `InspeccionIniciada_v1`.

**Candidato para refactorer:** Si se requiere `MultiStreamProjection` puro, agregar `EquipoId: int` a `InspeccionFirmada_v1` e `InspeccionCancelada_v1`. Decisión de dominio — requiere cambio en los eventos.

### 3. `ops.Insert` vs `ops.Store` en la proyección

Se usó `ops.Insert(view)` (equivalente al `session.Insert(view)` que tenía el handler) para preservar la semántica INSERT-puro de la defensa dura I-I1. Si se usara `ops.Store(view)` (upsert), una segunda `InspeccionIniciada_v1` para el mismo equipo sobreescribiría silenciosamente la fila y no lanzaría `23505`. La defensa I-I1 dura se basa en que Postgres lanza la violación de unicidad.

### 4. Claims mock en endpoint

El endpoint usa un claims mock fijo (`tecnicoId = "rmartinez"`), igual que los endpoints existentes 1c-1f. Este es el patrón del proyecto hasta que ADR-002 se implemente.

---

## Notas para refactorer

1. Los métodos `Firmar`, `RegistrarHallazgo`, `ActualizarHallazgo`, `EliminarHallazgo` comparten el patrón `Estado != EnEjecucion → throw InspeccionNoEnEjecucionException`. Candidato a método helper privado `VerificarEnEjecucion()`.
2. `_hallazgos.Where(h => !h.Eliminado).ToList()` se repite en `Firmar` y potencialmente en otros lugares. Candidato a propiedad privada `HallazgosVigentes`.
3. El switch `(tieneSeguimiento, tieneIntervencion)` para el mensaje de `DictamenIncoherenteException` podría ser un método estático privado.
4. `InspeccionAbiertaPorEquipoProjection` usa `EventProjection` en lugar de `MultiStreamProjection` — ver decisión §2 arriba.
5. El loop PRE-6 en `Firmar` verifica 3 condiciones por hallazgo (TipoFallaId → CausaFallaId → adjuntos). Candidato a método privado `VerificarHallazgoIntervencionCompleto(Hallazgo h)`.
6. `DiagnosticoRequeridoException` en `Application.Excepciones` — algunos equipos podrían argumentar que debería estar en `Domain.Excepciones` para consistencia. Decisión de diseño para refactorer.
