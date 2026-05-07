# Red notes — Slice 1h — IniciarInspeccionMonitoreo

**Autor:** red
**Fecha:** 2026-05-07
**Spec consumida:** `slices/1h-iniciar-inspeccion-monitoreo/spec.md` (firmada P-1..P-6 + ambigüedades el 2026-05-07).

---

## 1. Tests escritos

### Archivo 1 — Domain puro (8 tests)

`tests/Inspecciones.Domain.Tests/Inspecciones/IniciarInspeccionMonitoreoTests.cs`

| Test | Escenario spec §6.X |
|---|---|
| `IniciarMonitoreo_sobre_stream_vacio_emite_InspeccionIniciada_v1_con_Tipo_Monitoreo` | §6.1 happy path |
| `IniciarMonitoreo_emite_evento_con_payload_completo_incluyendo_snapshot_y_rutina` | §6.1 happy path payload |
| `IniciarMonitoreo_con_FechaReportada_futura_lanza_FechaReportadaFueraDeRangoException_I_I3` | §6.9 PRE-9 |
| `IniciarMonitoreo_con_FechaReportada_mas_de_30_dias_atras_lanza_FechaReportadaFueraDeRangoException_I_I3` | §6.10 PRE-9 |
| `IniciarMonitoreo_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizadoException_PRE_8` | §6.11 PRE-8 |
| `IniciarMonitoreo_snapshot_con_dos_items_activos_los_propaga_al_evento_sin_modificar` | §6.12 snapshot filtrado |
| `IniciarMonitoreo_rebuild_desde_stream_reproduce_estado_sin_lanzar_excepciones` | §6.14 rebuild obligatorio |
| `Apply_InspeccionIniciada_v1_con_campos_monitoreo_null_Tecnica_no_lanza` | §6.14 backward compat |

### Archivo 2 — Handler integración (9 tests)

`tests/Inspecciones.Application.Tests/Inspecciones/IniciarInspeccionMonitoreoHandlerTests.cs`

| Test | Escenario spec §6.X |
|---|---|
| `IniciarMonitoreo_happy_path_evento_y_proyeccion_persisten_atomicos_seccion_6_1` | §6.1 happy path integración |
| `IniciarMonitoreo_equipo_con_activa_retorna_existente_I_I1_seccion_6_2` | §6.2 I-I1 shortcut |
| `Dos_IniciarMonitoreo_concurrentes_sobre_mismo_equipo_un_solo_evento_persiste_I_I1_seccion_6_3` | §6.3 race condition |
| `IniciarMonitoreo_con_equipo_no_sincronizado_lanza_EquipoNoEncontrado_PRE_3_seccion_6_5` | §6.5 PRE-3 |
| `IniciarMonitoreo_con_rutina_no_sincronizada_lanza_RutinaMonitoreoNoSincronizada_PRE_4_seccion_6_6` | §6.6 PRE-4 |
| `IniciarMonitoreo_con_rutina_de_grupo_distinto_lanza_RutinaNoAplicableAlGrupo_PRE_5_seccion_6_7` | §6.7 PRE-5 |
| `IniciarMonitoreo_con_rutina_sin_items_activos_lanza_EquipoSinRutinasMonitoreo_PRE_6_seccion_6_8` | §6.8 PRE-6 |
| `IniciarMonitoreo_snapshot_excluye_items_inactivos_en_evento_persistido_seccion_6_12` | §6.12 snapshot integración |
| `IniciarMonitoreo_evento_y_proyeccion_son_atomicos_seccion_6_13` | §6.13 atomicidad |

**Total: 17 tests** (8 dominio puro + 9 integración).

**Nota sobre §6.4 (idempotencia Wolverine envelope dedup):** este escenario requiere infraestructura Wolverine con envelope storage real. No tiene test unitario — la idempotencia es una responsabilidad del middleware de Wolverine, no del aggregate ni del handler. El green documentará si añade un test de integración al conectar Wolverine. Se registra como observación §5 de este archivo.

---

## 2. Mapeo escenario §6 → test

| Escenario | Test(s) | Archivo |
|---|---|---|
| §6.1 happy path | `IniciarMonitoreo_sobre_stream_vacio_*` + `IniciarMonitoreo_emite_evento_con_payload_completo_*` + `IniciarMonitoreo_happy_path_*_seccion_6_1` | Domain + Application |
| §6.2 I-I1 shortcut | `IniciarMonitoreo_equipo_con_activa_*_seccion_6_2` | Application |
| §6.3 race condition | `Dos_IniciarMonitoreo_concurrentes_*_seccion_6_3` | Application |
| §6.4 idempotencia Wolverine | (sin test separado — ver §5) | — |
| §6.5 PRE-3 equipo no encontrado | `IniciarMonitoreo_con_equipo_no_sincronizado_*_seccion_6_5` | Application |
| §6.6 PRE-4 rutina no sincronizada | `IniciarMonitoreo_con_rutina_no_sincronizada_*_seccion_6_6` | Application |
| §6.7 PRE-5 rutina grupo distinto | `IniciarMonitoreo_con_rutina_de_grupo_distinto_*_seccion_6_7` | Application |
| §6.8 PRE-6 rutina sin items activos | `IniciarMonitoreo_con_rutina_sin_items_activos_*_seccion_6_8` | Application |
| §6.9 PRE-9 FechaReportada futura | `IniciarMonitoreo_con_FechaReportada_futura_*` | Domain |
| §6.10 PRE-9 FechaReportada >30 días | `IniciarMonitoreo_con_FechaReportada_mas_de_30_dias_*` | Domain |
| §6.11 PRE-8 proyecto no autorizado | `IniciarMonitoreo_con_proyecto_fuera_de_los_asignados_*` | Domain |
| §6.12 snapshot solo items activos | `IniciarMonitoreo_snapshot_*` (×2, dominio + integración) | Domain + Application |
| §6.13 atomicidad | `IniciarMonitoreo_evento_y_proyeccion_son_atomicos_*_seccion_6_13` | Application |
| §6.14 rebuild desde stream | `IniciarMonitoreo_rebuild_*` + `Apply_InspeccionIniciada_v1_*_Tecnica_*` | Domain |

---

## 3. Stubs mínimos creados para compilar

Todos los stubs están en `src/` y lanzan `NotImplementedException` o tienen campo `nullable` backward-compat.

| Archivo | Tipo | Descripción |
|---|---|---|
| `src/Inspecciones.Domain/Inspecciones/IniciarInspeccionMonitoreo.cs` | Record nuevo | Comando del slice 1h — parámetros completos según spec §2 |
| `src/Inspecciones.Domain/Inspecciones/EvaluacionEsperada.cs` | Records nuevos | Abstract `EvaluacionEsperada` + `MedicionEsperada` + `EvaluacionCualitativaEsperada` — spec §3 P-1/D1 |
| `src/Inspecciones.Domain/Inspecciones/ItemRutinaMonitoreoSnapshot.cs` | Record nuevo | Snapshot de item de rutina de monitoreo — spec §3 P-2/D2 |
| `src/Inspecciones.Domain/Catalogos/RutinaMonitoreoLocal.cs` | Records nuevos | `RutinaMonitoreoLocal` + `ItemRutinaMonitoreoLocal` — catálogo M-16 local |
| `src/Inspecciones.Domain/Inspecciones/InspeccionIniciada_v1.cs` | Extensión backward-compat | Añadidos `RutinaMonitoreoSeleccionadaId: int?` e `ItemsSnapshot: IReadOnlyList<ItemRutinaMonitoreoSnapshot>?` con default `null` (D6) |
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Stub `IniciarMonitoreo` + props | Propiedades `RutinaMonitoreoSeleccionadaId` e `ItemsSnapshot`; método estático `IniciarMonitoreo` lanza `NotImplementedException`; `Apply(InspeccionIniciada_v1)` extendido para proyectar campos nuevos |
| `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | Comentario de sección | Anotado que PRE-3..PRE-6 viven en Application |
| `src/Inspecciones.Application/Inspecciones/IniciarInspeccionMonitoreoResult.cs` | Record nuevo | Resultado del handler — spec §2 |
| `src/Inspecciones.Application/Inspecciones/IniciarInspeccionMonitoreoHandler.cs` | Stub handler | `ManejarAsync` lanza `NotImplementedException` |
| `src/Inspecciones.Application/Inspecciones/Excepciones.cs` | Clases nuevas | `RutinaMonitoreoNoSincronizadaException`, `RutinaNoAplicableAlGrupoException`, `EquipoSinRutinasMonitoreoException` |
| `tests/Inspecciones.Domain.Tests/Inspecciones/MonitoreoFixtures.cs` | Fixture test | Datos de fixture realistas para slice 1h — GPS Colombia, equipo 4521 BULLDOZER, rutina 42 |
| `tests/Inspecciones.Domain.Tests/Inspecciones/CasoDeUso.cs` | Extensión | Método `IniciarMonitoreo` añadido al helper existente |

---

## 4. Comandos para correr los tests y razón de fallo esperada

### Tests de dominio puro

```powershell
dotnet test tests/Inspecciones.Domain.Tests --filter "FullyQualifiedName~IniciarInspeccionMonitoreo" --no-build
```

**Resultado esperado:** 7 fallos, 1 pasa.

| Test | Razón de fallo esperada |
|---|---|
| `IniciarMonitoreo_sobre_stream_vacio_*` | `NotImplementedException` — `Inspeccion.IniciarMonitoreo` lanza stub |
| `IniciarMonitoreo_emite_evento_con_payload_completo_*` | `NotImplementedException` — ídem |
| `IniciarMonitoreo_con_FechaReportada_futura_*` | Expected `FechaReportadaFueraDeRangoException` but found `NotImplementedException` |
| `IniciarMonitoreo_con_FechaReportada_mas_de_30_dias_*` | Expected `FechaReportadaFueraDeRangoException` but found `NotImplementedException` |
| `IniciarMonitoreo_con_proyecto_fuera_de_los_asignados_*` | Expected `ProyectoNoAutorizadoException` but found `NotImplementedException` |
| `IniciarMonitoreo_snapshot_con_dos_items_activos_*` | `NotImplementedException` — ídem |
| `IniciarMonitoreo_rebuild_desde_stream_*` | `NotImplementedException` — `IniciarMonitoreo` no devuelve eventos para reproyectar |
| `Apply_InspeccionIniciada_v1_*_Tecnica_*` | **PASA** — backward compat: el `Apply` ya existe y ya proyecta los nuevos campos null correctamente |

### Tests de handler (integración, requieren Docker)

```powershell
dotnet test tests/Inspecciones.Application.Tests --filter "FullyQualifiedName~IniciarInspeccionMonitoreo" --no-build
```

**Resultado esperado sin Docker:** 9 fallos por `Docker is either not running or misconfigured` — consistente con el comportamiento preexistente de los tests del slice 1b en este entorno (verificado: 15 tests del 1b también fallan por la misma causa).

**Resultado esperado con Docker disponible:** los 9 tests fallarán por `NotImplementedException` al invocar `IniciarInspeccionMonitoreoHandler.ManejarAsync` — razón correcta para fase red de handler.

---

## 5. Riesgos / observaciones para `green`

1. **§6.4 idempotencia Wolverine no tiene test dedicado.** El escenario §6.4 (replay con mismo `clientCommandId`) depende de `Wolverine.Persistence.IEnvelopeStorageSource` — no es testeable con `IDocumentSession` solo. El green debe evaluar si añade un test de integración Wolverine end-to-end al conectar el handler al bus. Si no, el escenario queda cubierto por documentación del ADR-008 y el test §6.3 (race condition) verifica la invariante I-I1 desde el punto de vista del aggregate.

2. **`InspeccionAbiertaPorEquipoView` necesita campo `Tipo`** (spec §8.1). El green debe extender el record con `TipoInspeccion Tipo` y actualizar `InspeccionAbiertaPorEquipoProjection` para proyectar `e.Tipo`. El test `IniciarMonitoreo_happy_path_*` del slice 1h no verifica `Tipo` en la proyección (para no fallar por una cosa no implementada); el green puede añadir esa assertion al pasar al verde.

3. **Decisión de firma del método `IniciarMonitoreo`.** El stub tiene la firma `Inspeccion.IniciarMonitoreo(IniciarInspeccionMonitoreo cmd, ClaimsTecnico claims, string rutinaNombre, IReadOnlyList<ItemRutinaMonitoreoSnapshot> itemsSnapshot, DateTimeOffset ahora)`. El green puede ajustar si prefiere una variante parametrizada (p. ej. recibir la rutina completa). Lo importante es que el handler resuelva los catálogos y construya el snapshot antes de llamar al aggregate.

4. **`EquipoLocal.GrupoMantenimientoId` es `int?` (nullable).** Si el catálogo tiene equipos sin `GrupoMantenimientoId` (datos del ERP incompletos), PRE-5 no puede validarse. El green debe decidir si tratar `null == null` como mismatch o como bypass de PRE-5. Recomendación: tratar `null` en equipo como error 422 con mensaje claro.

5. **Tests de integración — Docker no disponible localmente.** Los 9 tests del handler fallan por `Docker is either not running or misconfigured` — idéntico al comportamiento de los tests del slice 1b en este entorno. No es un problema del slice 1h. Documentado para que CI (con Docker) los ejecute en el pipeline.

6. **`PostgresFixture` reutilizado sin cambios.** Los tests de integración del handler usan el fixture existente de la colección `PostgresFixtureCollection`. No se registró `RutinaMonitoreoLocal` en `StoreOptions` del fixture. El green debe añadir `opts.Schema.For<RutinaMonitoreoLocal>().Identity(x => x.RutinaMonitoreoId)` al `PostgresFixture.InitializeAsync` para que Marten genere el schema correcto.

---

## Notas de hand-off a green

- **Spec firmada:** sí (P-1..P-6 + ambigüedades el 2026-05-07).
- **Compilación:** limpia — `0 Advertencias, 0 Errores` con `TreatWarningsAsErrors=true`.
- **Tests dominio puro rojos:** 7/8 fallan por `NotImplementedException` (razón correcta). 1/8 pasa (backward compat `Apply` — esperado).
- **Tests handler:** 9/9 fallan (Docker no disponible localmente — consistente con slice 1b).
- **Tests slices anteriores:** 97/97 siguen pasando — sin regresiones.
