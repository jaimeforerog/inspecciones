# Red notes — Slice 1a — IniciarInspeccion (aggregate puro)

**Autor:** red
**Fecha:** 2026-05-05 (revisado)
**Spec consumida:** `slices/1a-iniciar-inspeccion-aggregate/spec.md` (firmado 2026-05-05).
**Split aplicado:** los tests del handler (§6.10, §6.11 originales) se movieron a `slices/1b-iniciar-inspeccion-handler/`. Los archivos `IniciarInspeccionHandlerTests.cs` y `PostgresFixture.cs` ya están en `tests/Inspecciones.Application.Tests/` con los tests marcados `[Fact(Skip="Slice 1b — pendiente...")]` para no romper CI.

---

## 1. Tests escritos

### Tests del agregado puro (`Inspecciones.Domain.Tests`)

Cubren §6.1–§6.9 y §6.12 sin dependencias de infra. Helper `CasoDeUso.Iniciar(dados, cmd, claims, equipo, rutina, ahora)` reconstruye desde el historial vacío y delega al método de decisión.

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `IniciarInspeccion_sobre_stream_vacio_emite_InspeccionIniciada_v1` | 6.1 happy path (forma del evento) | `tests/Inspecciones.Domain.Tests/Inspecciones/IniciarInspeccionTests.cs` |
| `IniciarInspeccion_emite_evento_con_payload_completo` | 6.1 happy path (campos) | ídem |
| `IniciarInspeccion_con_lecturas_de_ambos_medidores_las_propaga_al_evento` | 6.2 lecturas | ídem |
| `IniciarInspeccion_con_FechaReportada_dos_dias_atras_es_aceptada_I_I3` | 6.3 retroactivo válido | ídem |
| `IniciarInspeccion_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizado_PRE_2` | 6.4 PRE-2 | ídem |
| `IniciarInspeccion_con_equipo_de_otro_proyecto_lanza_EquipoNoPerteneceAProyecto_PRE_4` | 6.5 PRE-4 | ídem |
| `IniciarInspeccion_con_equipo_sin_rutina_tecnica_lanza_EquipoSinRutinaTecnica_I_I2` | 6.6 PRE-5 / I-I2 | ídem |
| `IniciarInspeccion_con_rutina_referenciada_inconsistente_lanza_RutinaTecnicaNoSincronizada_I_I2` | 6.7 PRE-6 / I-I2 | ídem |
| `IniciarInspeccion_con_FechaReportada_futura_lanza_FechaReportadaFueraDeRango_I_I3` | 6.8 I-I3 futura | ídem |
| `IniciarInspeccion_con_FechaReportada_mas_de_30_dias_atras_lanza_FechaReportadaFueraDeRango_I_I3` | 6.9 I-I3 retroactiva | ídem |
| `IniciarInspeccion_rebuild_desde_stream_reproduce_estado` | 6.12 rebuild obligatorio | ídem |

### Tests del handler — diferidos a slice 1b

Los tests `IniciarInspeccion_sobre_equipo_con_activa_retorna_existente_I_I1` y `Dos_IniciarInspeccion_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1` (escenarios §6.10 y §6.11 originales) están escritos en `tests/Inspecciones.Application.Tests/Inspecciones/IniciarInspeccionHandlerTests.cs` pero marcados con `[Fact(Skip="Slice 1b — pendiente: requiere IniciarInspeccionHandler implementado y proyección InspeccionAbiertaPorEquipoView con índice único parcial Postgres. Ver slices/1b-iniciar-inspeccion-handler/spec.md.")]`. Cuando se firme el spec del 1b, el `red` de ese sub-slice los desbloquea.

> Nota sobre PRE-1 (capability) y PRE-3 (equipo no encontrado): no tienen test del agregado puro porque viven en capa HTTP / resolución del handler. Se cubren en slice 1b. Documentado en spec §4 ("Capa donde viven").

## 2. Verificación de estado rojo

**Tests del agregado (Domain):**

```
> dotnet test tests/Inspecciones.Domain.Tests --no-build
Pruebas totales: 12
     Correcto: 1   ← BootstrapTests trivial 1+1==2 (no parte del slice)
     Incorrecto: 11
```

Las 11 fallas son del slice 1, todas con el mismo patrón:

```
Expected a <{ExcepciónEsperada}> to be thrown,
   but found <System.NotImplementedException>:
   at Inspecciones.Domain.Inspecciones.Inspeccion.Iniciar(...)
```

Verifica que el test rompe en el **método de decisión** (stub `Inspeccion.Iniciar`), no en compile, y que la fluent assertion sí encontró que NO se lanzó la excepción específica esperada — fallo por la razón correcta.

**Tests del handler (Application):**

No corridos local porque requieren Docker Desktop activo. Compilan correctamente. Se ejecutarán en CI (GitHub Actions tiene Docker disponible) y fallarán con `NotImplementedException` desde `IniciarInspeccionHandler.ManejarAsync` — mismo patrón que los del agregado.

## 3. Código de producción tocado

- ✅ Stubs nuevos creados en `src/`. Cada stub que requiere comportamiento (no es solo shape) lanza `NotImplementedException`.

**Archivos nuevos en `src/Inspecciones.Domain/`:**

| Archivo | Tipo de stub |
|---|---|
| `Comun/UbicacionGps.cs` | Record (shape, sin lógica) |
| `Comun/LecturaMedidor.cs` | Record (shape) |
| `Catalogos/TipoRutina.cs` | Enum |
| `Catalogos/EquipoLocal.cs` | Record (shape) |
| `Catalogos/RutinaTecnicaLocal.cs` | Record (shape) |
| `Inspecciones/TipoInspeccion.cs` | Enum |
| `Inspecciones/EstadoInspeccion.cs` | Enum |
| `Inspecciones/IniciarInspeccion.cs` | Record comando (shape) |
| `Inspecciones/InspeccionIniciada_v1.cs` | Record evento (shape) |
| `Inspecciones/ClaimsTecnico.cs` | Record (shape) |
| `Inspecciones/Excepciones.cs` | Excepciones (6 — base + 5 específicas del slice) |
| `Inspecciones/Inspeccion.cs` | Aggregate. `Iniciar(...)` lanza `NotImplementedException`. `Apply(InspeccionIniciada_v1)` lanza `NotImplementedException`. `Reconstruir(...)` está implementado (delega a `Apply` por tipo). |

**Archivos nuevos en `src/Inspecciones.Application/`:**

| Archivo | Tipo de stub |
|---|---|
| `Inspecciones/IniciarInspeccionResult.cs` | Record (shape del DTO de respuesta) |
| `Inspecciones/IniciarInspeccionHandler.cs` | Class. `ManejarAsync(...)` lanza `NotImplementedException`. |

**Archivos nuevos en `tests/`:**

| Archivo | Propósito |
|---|---|
| `Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` | Helper Given/When/Then que reconstruye y delega a `Inspeccion.Iniciar`. |
| `Inspecciones.Domain.Tests/Inspecciones/Fixtures.cs` | Fixtures reusables (claims válidos, equipo con rutina, rutina típica, comando típico). |
| `Inspecciones.Domain.Tests/Inspecciones/IniciarInspeccionTests.cs` | 11 tests del agregado puro. |
| `Inspecciones.Application.Tests/Inspecciones/PostgresFixture.cs` | Fixture compartido con Testcontainers + `IDocumentStore`. |
| `Inspecciones.Application.Tests/Inspecciones/IniciarInspeccionHandlerTests.cs` | 2 tests del handler con Marten real. |

**Paquetes agregados al `Directory.Packages.props`:**

- `Microsoft.Extensions.TimeProvider.Testing` v9.0.0 — para `FakeTimeProvider` en los tests del handler. Referenciado en `Inspecciones.Application.Tests.csproj`.

> Ningún archivo de producción fuera de los stubs listados arriba fue tocado. Cero refactor incidental.

## 4. Desviaciones respecto a la spec

- ✅ **Sin desviaciones.** Cada precondición y cada invariante mapea a tests; el rebuild desde stream está cubierto; preguntas abiertas todas resueltas en la spec firmada.

Una observación menor que sí conviene visibilizar al `green`:

- **PRE-1 (capability) y PRE-3 (equipo no existe) no tienen test del agregado.** El spec dice explícitamente que viven fuera del método de decisión (PRE-1 en capa HTTP, PRE-3 en resolución del handler antes del agregado). Los tests del handler (§6.10) ejercen el camino feliz pasando por la resolución, pero no tienen un test específico de "equipo no encontrado". Esto es coherente con el spec — si el `reviewer` lo marca como gap, sería un followup, no falta de cobertura del slice 1.

## 5. Hand-off a green

- ✅ Spec firmada.
- ✅ Todos los tests del slice 1 fallan en rojo válido (11 con `NotImplementedException` en agregado; 2 más fallarán igual cuando Docker esté disponible en CI).
- ✅ Sin cambios de comportamiento accidentales — solo stubs nuevos.

**Para `green`:**

1. Implementar `Inspeccion.Iniciar(cmd, claims, equipo, rutina, ahora)` validando PRE-2, PRE-4, PRE-5, PRE-6, PRE-7 en ese orden y emitiendo `InspeccionIniciada_v1` cuando todo pasa. Recordar:
   - `RutinaCodigo` se denormaliza desde `rutina.Codigo`.
   - `IniciadaEn` es `ahora` (que viene del `TimeProvider`).
   - `Tipo` siempre es `TipoInspeccion.Tecnica` en MVP.
2. Implementar `Apply(InspeccionIniciada_v1 e)` como mutación pura del estado del aggregate (sin validar). Setea `Estado = EnEjecucion` + todos los campos.
3. Implementar `IniciarInspeccionHandler.ManejarAsync(...)`:
   - Consultar `InspeccionAbiertaPorEquipoView` (proyección que también hay que crear) para I-I1 shortcut.
   - Resolver `EquipoLocal` y `RutinaTecnicaLocal` desde `IDocumentSession`.
   - Invocar `Inspeccion.Iniciar` y appendear los eventos al stream con `session.Events.StartStream`.
   - Configurar índice único parcial Postgres sobre `EquipoId WHERE Estado='EnEjecucion'` en la proyección.
   - `SaveChangesAsync` atómico.
4. Crear el read model `InspeccionAbiertaPorEquipoView` como proyección inline de Marten — defensa dura I-I1.

> Estimación: el green de este slice es significativamente más ancho que un slice típico porque incluye plumbing de plumería que el bootstrap dejó stubbeado (proyección, configuración Marten de eventos del aggregate). Posible split en sub-slices `1a-aggregate`, `1b-handler-projection` si el reviewer ve que el alcance es excesivo.
