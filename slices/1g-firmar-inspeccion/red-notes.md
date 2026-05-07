# Red notes — Slice 1g — FirmarInspeccion

**Autor:** red
**Fecha:** 2026-05-07
**Spec consumida:** `slices/1g-firmar-inspeccion/spec.md`

---

## 1. Tests escritos

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `FirmarInspeccion_happy_path_NoRequiereIntervencion_PuedeOperar_emite_tres_eventos_en_orden` | §6.1 — happy path, orden causal | `FirmarInspeccionTests.cs` |
| `FirmarInspeccion_happy_path_payload_DiagnosticoEmitido_v1_correcto` | §6.1 — payload evento 1 | ídem |
| `FirmarInspeccion_happy_path_payload_DictamenEstablecido_v1_correcto` | §6.1 — payload evento 2 | ídem |
| `FirmarInspeccion_happy_path_payload_InspeccionFirmada_v1_correcto` | §6.1 — payload evento 3 | ídem |
| `FirmarInspeccion_happy_path_RequiereIntervencion_NoPuedeOperar_emite_tres_eventos` | §6.2 — happy path NoPuedeOperar | ídem |
| `FirmarInspeccion_happy_path_RequiereSeguimiento_ConRestriccion_no_lanza` | §6.3 — happy path ConRestriccion V-F8 válido | ídem |
| `FirmarInspeccion_en_inspeccion_ya_firmada_lanza_InspeccionNoEnEjecucionException_PRE_2` | §6.4 — PRE-2 V-F7 | ídem |
| `FirmarInspeccion_sin_hallazgos_registrados_lanza_SinHallazgosException_PRE_3_V_F1` | §6.5 — PRE-3 V-F1 sin hallazgos | ídem |
| `FirmarInspeccion_con_todos_hallazgos_eliminados_lanza_SinHallazgosException_PRE_3_V_F1` | §6.6 — PRE-3 V-F1 todos eliminados | ídem |
| `FirmarInspeccion_dictamen_PuedeOperar_con_hallazgo_RequiereSeguimiento_lanza_DictamenIncoherenteException_PRE_5_V_F8` | §6.7 — PRE-5 V-F8 | ídem |
| `FirmarInspeccion_dictamen_PuedeOperar_con_hallazgo_RequiereIntervencion_lanza_DictamenIncoherenteException_PRE_5_V_F8` | §6.8 — PRE-5 V-F8 | ídem |
| `FirmarInspeccion_dictamen_PuedeOperar_con_solo_NoRequiereIntervencion_no_lanza_V_F8` | §6.9 — caso borde V-F8 | ídem |
| `FirmarInspeccion_hallazgo_RequiereIntervencion_sin_TipoFallaId_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3` | §6.10 — PRE-6 V-F3 sin TipoFallaId | ídem |
| `FirmarInspeccion_hallazgo_RequiereIntervencion_sin_CausaFallaId_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3` | §6.11 — PRE-6 V-F3 sin CausaFallaId | ídem |
| `FirmarInspeccion_hallazgo_RequiereIntervencion_sin_adjuntos_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3` | §6.12 — PRE-6 V-F3 sin adjuntos | ídem |
| `FirmarInspeccion_hallazgo_RequiereIntervencion_con_adjunto_eliminado_lanza_HallazgoIntervencionIncompletoException_PRE_6_V_F3` | §6.13 — PRE-6 V-F3 adjunto eliminado | ídem |
| `FirmarInspeccion_con_FirmaUri_vacio_lanza_FirmaRequeridaException_PRE_7_V_F5` | §6.14 — PRE-7 V-F5 FirmaUri vacío | ídem |
| `FirmarInspeccion_con_FirmaUri_solo_espacios_lanza_FirmaRequeridaException_PRE_7_V_F5` | §6.14 — PRE-7 V-F5 FirmaUri whitespace | ídem |
| `FirmarInspeccion_con_UbicacionFirma_nula_lanza_GpsRequeridoException_PRE_8_V_F6` | §6.15 — PRE-8 V-F6 | ídem |
| `FirmarInspeccion_tecnico_no_contribuyente_lanza_TecnicoNoContribuyenteException_PRE_9` | §6.16 — PRE-9 (P-2 confirmado) | ídem |
| `FirmarInspeccion_rebuild_desde_stream_reproduce_estado` | §6.17 — rebuild obligatorio | ídem |
| `RegistrarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I_F1` | §6.18 — inmutabilidad post-firma | ídem |

**Total tests nuevos:** 22 (uno por escenario spec + el extra de §6.14 para whitespace, que es borde natural de V-F5).

**Decisión P-1 (V-F2 solo UX):** no hay test de V-F2 al nivel del aggregate. Documentado.

**Decisión P-2 (solo contribuyentes):** PRE-9 verifica `Contribuyentes` sin capability de supervisor.

---

## 2. Verificación de estado rojo

```
dotnet test tests/Inspecciones.Domain.Tests/ --filter "FullyQualifiedName~FirmarInspeccion" --verbosity minimal

Correctas! - Con error: 21, Superado: 1, Omitido: 0, Total: 22, Duración: 114 ms
```

**Tests que fallan (21):** todos los tests de happy path y de invariantes fallan con `NotImplementedException` desde `Inspeccion.Firmar(...)` — el método stub aún no implementado.

**Test que pasa (1):** `RegistrarHallazgo_en_inspeccion_firmada_lanza_InspeccionNoEnEjecucionException_I_F1` — correcto porque `RegistrarHallazgo` ya implementa PRE-3 (`Estado != EnEjecucion → throw`). Este test verifica la invariante I-F1 post-firma usando código ya existente.

**Razón de fallo de cada categoría:**

| Categoría | Razón del fallo |
|---|---|
| Happy paths (§6.1 × 4, §6.2, §6.3, §6.9) | `NotImplementedException` — `Firmar` no implementado |
| §6.3, §6.9 (`Should().NotThrow()`) | `NotImplementedException` — el método lanza antes de validar |
| §6.4 (PRE-2 inspección ya firmada) | `NotImplementedException` lanzada antes de `InspeccionNoEnEjecucionException` |
| §6.5, §6.6 (PRE-3 sin hallazgos) | `NotImplementedException` lanzada antes de `SinHallazgosException` |
| §6.7, §6.8 (PRE-5 dictamen incoherente) | `NotImplementedException` lanzada antes de `DictamenIncoherenteException` |
| §6.10, §6.11, §6.12, §6.13 (PRE-6 intervención incompleta) | `NotImplementedException` lanzada antes de `HallazgoIntervencionIncompletoException` |
| §6.14 (PRE-7 firma vacía) | `NotImplementedException` lanzada antes de `FirmaRequeridaException` |
| §6.15 (PRE-8 GPS nulo) | `NotImplementedException` lanzada antes de `GpsRequeridoException` |
| §6.16 (PRE-9 no contribuyente) | `NotImplementedException` lanzada antes de `TecnicoNoContribuyenteException` |
| §6.17 (rebuild) | `NotImplementedException` — `Firmar` no emite eventos, imposible rebuildar |

---

## 3. Código de producción tocado

- [x] Modificaciones en `src/` — cambios incidentales por requerimientos del slice y corrección del stub:

| Archivo | Cambio |
|---|---|
| `src/…/InspeccionFirmada_v1.cs` | Payload expandido de 3 a 5 campos (`FirmaUri`, `UbicacionFirma` añadidos). El stub original del slice 1c solo tenía `InspeccionId`, `FirmadaEn`, `FirmadoPor`. |
| `src/…/Inspeccion.cs` | Añadidos: `Apply(DiagnosticoEmitido_v1)`, `Apply(DictamenEstablecido_v1)`, `Apply(AdjuntoSubido_v1)`, `Apply(AdjuntoEliminado_v1)`, `Apply(InspeccionFirmada_v1)` actualizado al nuevo payload, `Firmar(...)` stub con `throw new NotImplementedException()`, propiedades `DiagnosticoFinal`, `Dictamen`, `FirmaUri`, `UbicacionFirma`, `FirmadaEn`, campo `_adjuntosPorHallazgo`, switch `AplicarEvento` extendido. |
| `src/…/Excepciones.cs` | Añadidas 6 excepciones: `SinHallazgosException`, `DictamenIncoherenteException`, `HallazgoIntervencionIncompletoException`, `FirmaRequeridaException`, `GpsRequeridoException`, `TecnicoNoContribuyenteException`. |
| `src/…/FirmarInspeccion.cs` | Nuevo — record de comando. |
| `src/…/DictamenOperacion.cs` | Nuevo — enum. |
| `src/…/DiagnosticoEmitido_v1.cs` | Nuevo — evento. |
| `src/…/DictamenEstablecido_v1.cs` | Nuevo — evento. |
| `src/…/AdjuntoSubido_v1.cs` | Nuevo — evento stub para PRE-6. |
| `src/…/AdjuntoEliminado_v1.cs` | Nuevo — evento stub para PRE-6 §6.13. |

**Stubs de producción:** `Inspeccion.Firmar(...)` lanza `NotImplementedException`. Los `Apply` nuevos son mutaciones puras (vacías o mínimas), no stubs con `NotImplementedException`.

---

## 4. Desviaciones respecto a la spec

- [x] **Cambio incidental en tests de otros slices** (documentado per regla de persona):
  - `AsignarRepuestoTests.cs` línea 130: `InspeccionFirmada_v1` actualizado al nuevo constructor (5 parámetros).
  - `EliminarHallazgoTests.cs` línea 89: ídem.
  - `HallazgoFixtures.cs` `StreamConInspeccionFirmada()`: ahora incluye `DiagnosticoEmitido_v1 + DictamenEstablecido_v1 + InspeccionFirmada_v1` completo (más `HallazgoRegistrado_v1` necesario para que PRE-3 del test de `RegistrarHallazgo` pruebe correctamente el estado `Firmada` sin que sea bloqueado por la falta de hallazgos en el stream).
  - Causa: el stub del slice 1c usaba solo 3 campos en `InspeccionFirmada_v1`; el slice 1g requiere el payload completo del spec §3.

- [ ] Sin desviaciones en la lógica de negocio respecto a la spec.

---

## 5. Hand-off a green

**Spec firmada:** sí (con decisiones P-1 y P-2 confirmadas).
**Todos los tests rojos:** sí (21/22 fallan; §6.18 pasa correctamente por diseño).
**Sin cambios de comportamiento accidentales:** sí.

### Clases / métodos que green debe implementar

#### Método principal (dominio)

```csharp
// src/Inspecciones.Domain/Inspecciones/Inspeccion.cs
public IReadOnlyList<object> Firmar(FirmarInspeccion cmd, DateTimeOffset ahora)
```

**Orden de pre-condiciones a implementar:**
1. **PRE-2** — `Estado != EnEjecucion` → `throw InspeccionNoEnEjecucionException("*Firmada*")`
2. **PRE-3** — `!_hallazgos.Any(h => !h.Eliminado)` → `throw SinHallazgosException(...)`
3. **PRE-7** — `string.IsNullOrWhiteSpace(cmd.FirmaUri)` → `throw FirmaRequeridaException(...)`
4. **PRE-8** — `cmd.UbicacionFirma == null` → `throw GpsRequeridoException(...)`
5. **PRE-9** — `!_contribuyentes.Contains(cmd.TecnicoId)` → `throw TecnicoNoContribuyenteException(...)`
6. **PRE-5 (V-F8)** — `cmd.Dictamen == PuedeOperar && hallazgos_vigentes.Any(h => h.AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion})` → `throw DictamenIncoherenteException("*seguimiento*" | "*intervención*")`
7. **PRE-6 (V-F3)** — Para cada hallazgo vigente con `RequiereIntervencion`: (a) `TipoFallaId == null` → `throw HallazgoIntervencionIncompletoException("*TipoFallaId*")`, (b) `CausaFallaId == null` → `throw HallazgoIntervencionIncompletoException("*CausaFallaId*")`, (c) `!_adjuntosPorHallazgo.TryGetValue(h.HallazgoId, out var set) || set.Count == 0` → `throw HallazgoIntervencionIncompletoException("*adjunto*")`

**Emisión de eventos (mismo `ahora`):**
```csharp
return new object[]
{
    new DiagnosticoEmitido_v1(InspeccionId, cmd.Diagnostico, cmd.TecnicoId, ahora),
    new DictamenEstablecido_v1(InspeccionId, cmd.Dictamen, cmd.JustificacionDictamen, cmd.TecnicoId, ahora),
    new InspeccionFirmada_v1(InspeccionId, cmd.TecnicoId, cmd.FirmaUri, cmd.UbicacionFirma!, ahora)
};
```

#### Handler (aplicación)

```csharp
// src/Inspecciones.Application/Inspecciones/FirmarInspeccionHandler.cs
public sealed class FirmarInspeccionHandler(IDocumentSession session, TimeProvider time)
{
    public async Task<FirmarInspeccionResult> ManejarAsync(
        FirmarInspeccion cmd,
        ClaimsTecnico claims,
        CancellationToken ct = default)
}
```

**PRE-1** (capability) y **PRE-4** (Diagnostico no vacío) se validan en el handler antes de cargar el aggregate.

#### Endpoint HTTP

```
POST /api/v1/inspecciones/{id}/firmar
```

- Request: `FirmarInspeccionRequest` (body JSON)
- Response: `FirmarInspeccionResponse` con `200 OK`
- `403` para `CapabilityRequeridaException` y `TecnicoNoContribuyenteException`
- `404` para inspección inexistente
- `409` para `InspeccionNoEnEjecucionException`
- `422` para el resto de domain exceptions

#### Proyección FU-13 (migración `InspeccionAbiertaPorEquipoView`)

Ver spec §8.1 — el handler no toca la vista directamente. La proyección debe:
- Manejar `InspeccionFirmada_v1` → `DeleteEvent<InspeccionAbiertaPorEquipoView>` (keyed por `EquipoId`)
- Registrarse como `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>` `Inline` en `StoreOptions`
- El `session.Insert(view)` del handler `IniciarInspeccion` debe ser reemplazado por la proyección

---

## 6. Corrección post-green — bug en helper del test §6.15

**Detectado por:** agente green (confirmado por orquestador y usuario).
**Fecha:** 2026-05-07

### Bug

El helper `ComandoFirmarBasico` en `FirmarInspeccionTests.cs` tenía la firma:

```csharp
private static FirmarInspeccion ComandoFirmarBasico(
    ...
    UbicacionGps? ubicacionFirma = null) =>
    new(
        ...
        UbicacionFirma: ubicacionFirma ?? UbicacionFirmaEjemplo(),  // BUG
        ...);
```

Cuando el test §6.15 llamaba `ComandoFirmarBasico(ubicacionFirma: null)`, el operador `??` sustituía `null` por la ubicación válida del fixture. El comando llegaba al dominio con una `UbicacionFirma` válida y la guardia PRE-8 de `Inspeccion.Firmar(...)` no se disparaba nunca. El test afirmaba `Should().Throw<GpsRequeridoException>()` pero no llegaba ninguna excepción — estado verde falso.

El fallo era en la fase RED (helper mal escrito), no en la implementación de green: la guardia PRE-8 estaba correctamente implementada en `Inspeccion.Firmar` (línea 616-620).

### Cambio aplicado

Se reemplazó el cuerpo del test §6.15 para construir el record `FirmarInspeccion` directamente, sin usar el helper:

```csharp
// ANTES
var cmd = ComandoFirmarBasico(ubicacionFirma: null);
// → UbicacionFirma quedaba sustituida por UbicacionFirmaEjemplo() — invariante nunca se disparaba

// DESPUÉS
var cmd = new FirmarInspeccion(
    InspeccionId: InspeccionIdNueva,
    Diagnostico: "Inspección sin hallazgos críticos",
    Dictamen: DictamenOperacion.PuedeOperar,
    JustificacionDictamen: "Equipo en buen estado",
    FirmaUri: "https://blobs/firma-01.png",
    UbicacionFirma: null!,          // null real llega al dominio
    TecnicoId: "rmartinez");
// → PRE-8 se dispara, GpsRequeridoException se lanza, test verde correcto
```

No se modificó el helper `ComandoFirmarBasico` — funciona bien para todos los demás tests donde `null` significa "usar el default". No se tocó ningún otro test ni código de producción.

### Output de confirmación — todos verdes (22/22)

```
dotnet test tests/Inspecciones.Domain.Tests/ --filter "FullyQualifiedName~FirmarInspeccion"

Correctas! - Con error: 0, Superado: 22, Omitido: 0, Total: 22, Duración: 76 ms
```
