# Red Notes — Slice 1f: AsignarRepuesto

**Agente:** red
**Fecha:** 2026-05-07
**Estado al entregar:** compilación limpia, 12 tests en rojo (11 nuevos + 1 skip levantado)

---

## 1. Stubs creados

| Artefacto | Archivo | Descripción |
|---|---|---|
| `AsignarRepuesto` (record) | `src/Inspecciones.Domain/Inspecciones/AsignarRepuesto.cs` | Comando del slice 1f. |
| `RepuestoEstimado_v1` (record) | `src/Inspecciones.Domain/Inspecciones/RepuestoEstimado_v1.cs` | Evento emitido al asignar. |
| `Repuesto` (record) | `src/Inspecciones.Domain/Inspecciones/Repuesto.cs` | Value object de estado interno del aggregate. |
| `HallazgoNoRequiereIntervencionException` | `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | PRE-C / I-H12. |
| `CantidadInvalidaException` | `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | PRE-E. |
| `SkuDuplicadoEnHallazgoException` | `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | PRE-G. |
| `_repuestos` + `Repuestos` prop | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Colección privada para fold de repuestos. |
| `Inspeccion.AsignarRepuesto(...)` | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Stub que lanza `NotImplementedException`. |
| `Inspeccion.Apply(RepuestoEstimado_v1)` | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Stub vacío (referencia `_repuestos` para compilar). |
| `case RepuestoEstimado_v1` en `AplicarEvento` | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Dispatch al Apply stub. |
| `CasoDeUso.AsignarRepuesto(...)` | `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` | Helper Given/When/Then para los tests. |

### Fixtures añadidos en `HallazgoFixtures.cs`

- `RepuestoR1` — Guid del primer repuesto de ejemplo.
- `RepuestoR2` — Guid del segundo repuesto (para PRE-G).
- `StreamConHallazgoParaRepuesto()` — stream base para happy path (InspeccionIniciada + HallazgoG1 RequiereIntervencion).
- `StreamConHallazgoConRepuestoActivo()` — stream con HallazgoG5 + RepuestoEstimado_v1(R1, SkuId=501). Usado por §6.9 y por el skip levantado de EliminarHallazgo §6.7.
- `RepuestoEstimadoEjemplo(...)` — factory de `RepuestoEstimado_v1` para poblar streams.
- `ComandoAsignarRepuesto(...)` — factory del comando con defaults del escenario §6.1.

---

## 2. Tests escritos — mapping a §6 de la spec

| Test | Escenario spec | Razón de fallo en rojo |
|---|---|---|
| `AsignarRepuesto_en_inspeccion_en_ejecucion_con_RequiereIntervencion_emite_RepuestoEstimado_v1` | §6.1 | `NotImplementedException` — método stub. |
| `AsignarRepuesto_con_cantidad_fraccionaria_emite_RepuestoEstimado_v1_con_fraccion` | §6.2 | `NotImplementedException` — método stub. |
| `AsignarRepuesto_con_RepuestoId_ya_existente_devuelve_lista_vacia_sin_lanzar_PRE_D` | §6.3 | `NotImplementedException` en vez de lista vacía. |
| `AsignarRepuesto_en_inspeccion_Firmada_lanza_InspeccionNoEnEjecucionException_I_H7` | §6.4 | `NotImplementedException` en vez de `InspeccionNoEnEjecucionException`. |
| `AsignarRepuesto_con_HallazgoId_inexistente_lanza_HallazgoNoEncontradoException_PRE_B1` | §6.5 | `NotImplementedException` en vez de `HallazgoNoEncontradoException`. |
| `AsignarRepuesto_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException_PRE_B2` | §6.6 | `NotImplementedException` en vez de `HallazgoEliminadoException`. |
| `AsignarRepuesto_en_hallazgo_NoRequiereIntervencion_lanza_HallazgoNoRequiereIntervencionException_I_H12` | §6.7 | `NotImplementedException` en vez de `HallazgoNoRequiereIntervencionException`. |
| `AsignarRepuesto_con_Cantidad_cero_lanza_CantidadInvalidaException_PRE_E` | §6.8 | `NotImplementedException` en vez de `CantidadInvalidaException`. |
| `AsignarRepuesto_con_SkuId_duplicado_en_hallazgo_distinto_RepuestoId_lanza_SkuDuplicadoEnHallazgoException_PRE_G` | §6.9 | `NotImplementedException` en vez de `SkuDuplicadoEnHallazgoException`. |
| `EliminarHallazgo_con_repuesto_activo_lanza_HallazgoTieneHijosActivosException_I_H9` | §6.13 | `Apply(RepuestoEstimado_v1)` stub no popula `_repuestos` → `EliminarHallazgo` no ve hijos → no lanza. |
| `AsignarRepuesto_rebuild_desde_stream_reproduce_estado_con_repuesto` | §6.14 | `NotImplementedException` — el método de decisión falla antes de llegar al rebuild. |

### Test skip levantado en EliminarHallazgoTests.cs

| Test | Slice origen | Razón de fallo en rojo |
|---|---|---|
| `EliminarHallazgo_con_hijos_activos_lanza_HallazgoTieneHijosActivosException_I_H9` | slice 1e §6.7 (followup #21) | `Apply(RepuestoEstimado_v1)` stub no popula `_repuestos` → `EliminarHallazgo.PRE-D` no lanza. |

---

## 3. Tests omitidos — son de integración (handler)

| Escenario spec | Razón de omisión |
|---|---|
| §6.10 PRE-H1 — SkuId no existe en catálogo local | Requiere Marten document store con `RepuestoLocal` proyectado. Sin infra en tests de dominio puro. |
| §6.11 PRE-H2 — SKU incompatible con parte del hallazgo | Requiere `RepuestoLocal.ParteIdsCompatibles` del catálogo local en Marten. Sin infra en tests de dominio puro. |
| §6.12 PRE-F — InspeccionId no existe en Marten | Requiere `IDocumentSession.Events.AggregateStreamAsync` que retorna null. Sin infra en tests de dominio puro. |

Estos tres escenarios se implementan en tests de integración del handler del slice 1f, con Testcontainers Postgres + Marten embebido.

---

## 4. Decisión de diseño sobre Apply stub

El `Apply(RepuestoEstimado_v1)` stub referencia `_repuestos` (sin añadir nada) para satisfacer el analizador CA1822 (`TreatWarningsAsErrors=true`). El green agent debe reemplazar el cuerpo completo con:

```csharp
public void Apply(RepuestoEstimado_v1 e)
{
    _repuestos.Add(new Repuesto(
        RepuestoId: e.RepuestoId,
        HallazgoId: e.HallazgoId,
        SkuId: e.SkuId,
        SkuCodigo: e.SkuCodigo,
        Cantidad: e.Cantidad,
        Justificacion: e.Justificacion,
        Unidad: e.Unidad));
    _contribuyentes.Add(e.AsignadoPor);
}
```

El `Apply` es puro por definición (CLAUDE.md): sin validaciones, sin excepciones.

---

## 5. Impacto en slices anteriores

- `EliminarHallazgoTests.cs`: skip levantado en el test §6.7 (I-H9). Stream cambiado de `StreamConHallazgoRegistrado(hallazgoId: HallazgoG1)` a `StreamConHallazgoConRepuestoActivo()` (usa HallazgoG5 con repuesto activo). Los 7 tests restantes del slice 1e siguen en verde — confirmado en la corrida.
- Ningún otro test de slices anteriores fue tocado.

---

## 6. Verificación del estado rojo

```shell
dotnet test tests/Inspecciones.Domain.Tests --filter "FullyQualifiedName~AsignarRepuesto" --no-build
```

Resultado: **11 Con error, 0 Correcto** (todos los tests del slice 1f fallan).

```shell
dotnet test tests/Inspecciones.Domain.Tests --no-build
```

Resultado al entregar: **12 Con error, 62 Correcto, 0 Omitido** (74 total).
- 11 nuevos (AsignarRepuestoTests).
- 1 skip levantado (EliminarHallazgoTests §6.7).
- 62 tests de slices 1a..1e que siguen en verde.
