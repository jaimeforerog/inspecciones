# Review notes — Slice 1a — IniciarInspeccion (aggregate puro)

**Autor:** reviewer
**Fecha:** 2026-05-05
**Slice auditado:** `slices/1a-iniciar-inspeccion-aggregate/`.
**Veredicto:** `approved-with-followups`

---

## 1. Resumen ejecutivo

El slice está bien ejecutado. 16/16 tests pasan, 0 warnings, build limpio. El método de decisión `Inspeccion.Iniciar` cubre los 7 PRE-* en el orden correcto; `Apply` es puro; el rebuild test está presente y completo. La cobertura de ramas del agregado `Inspeccion` es 97.6 % (líneas) / 94.4 % de ramas globales. Los dos hallazgos son followups menores: la rama `default` del switch de `AplicarEvento` no puede cubrirse en este slice sin fabricar eventos sintéticos, y `CapabilityRequeridaException` vive en el dominio pero su superficie es 0 % porque PRE-1 está explícitamente fuera del método de decisión. Ninguno es bloqueante. El orquestador puede hacer el commit de 1a.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] **Cada escenario de `spec.md §6` tiene un test.**
  - §6.1 → `IniciarInspeccion_sobre_stream_vacio_emite_InspeccionIniciada_v1` + `IniciarInspeccion_emite_evento_con_payload_completo`
  - §6.2 → `IniciarInspeccion_con_lecturas_de_ambos_medidores_las_propaga_al_evento`
  - §6.3 → `IniciarInspeccion_con_FechaReportada_dos_dias_atras_es_aceptada_I_I3`
  - §6.4 → `IniciarInspeccion_con_proyecto_fuera_de_los_asignados_lanza_ProyectoNoAutorizado_PRE_2`
  - §6.5 → `IniciarInspeccion_con_equipo_de_otro_proyecto_lanza_EquipoNoPerteneceAProyecto_PRE_4`
  - §6.6 → `IniciarInspeccion_con_equipo_sin_rutina_tecnica_lanza_EquipoSinRutinaTecnica_I_I2`
  - §6.7 → `IniciarInspeccion_con_rutina_referenciada_inconsistente_lanza_RutinaTecnicaNoSincronizada_I_I2`
  - §6.8 → `IniciarInspeccion_con_FechaReportada_futura_lanza_FechaReportadaFueraDeRango_I_I3`
  - §6.9 → `IniciarInspeccion_con_FechaReportada_mas_de_30_dias_atras_lanza_FechaReportadaFueraDeRango_I_I3`
  - §6.10 y §6.11 → diferidos correctamente a slice 1b (escenarios del handler, no del agregado).
  - §6.12 → `IniciarInspeccion_rebuild_desde_stream_reproduce_estado`
  - Boundary I-I3 → tres tests adicionales: `hoy`, `hoy-30`, `hoy-31`. Supracobertura válida; atrapa off-by-one.

- [x] **Cada precondición tiene un test que la viola.** PRE-2 (§6.4), PRE-4 (§6.5), PRE-5 (§6.6), PRE-6 (§6.7), PRE-7 (§6.8 y §6.9). PRE-1 y PRE-3 no tienen test en el agregado puro — correcto: la spec §4 los posiciona explícitamente fuera del método de decisión (capa HTTP y resolución del handler, respectivamente).

- [x] **Cada invariante tocada tiene un test que la viola.** I-I2 → §6.6 y §6.7 (con literal `I_I2` en el nombre). I-I3 → §6.8 y §6.9 (con literal `I_I3`). I-I1 → escenarios del handler, diferidos a 1b, documentado.

- [x] **Nombres de tests en español, frases descriptivas.** Todos siguen el patrón `Comando_condicion_resultado_(invariante)`. Ejemplos: `IniciarInspeccion_con_FechaReportada_31_dias_atras_lanza_FechaReportadaFueraDeRango_I_I3_off_by_one`. Convenio cumplido.

### 2.2 Tests como documentación

- [x] **Given/When/Then visible.** Los happy paths (§6.1, §6.2, §6.3) y el rebuild test (§6.12) tienen comentarios de sección `// Given`, `// When`, `// Then`. Los tests de excepción omiten el bloque `// Given` explícito pero el `var act = () => ...` con FluentAssertions es la forma idiomática aceptada para ese patrón — legible sin comentario.

- [x] **Cero mocks del dominio.** Confirmado: `Fixtures.cs` construye instancias reales (`EquipoLocal`, `RutinaTecnicaLocal`, `ClaimsTecnico`, `UbicacionGps`); `CasoDeUso.cs` llama directamente a `Inspeccion.Iniciar` e `Inspeccion.Reconstruir`. Sin interfaz mockeada.

- [x] **Eventos usados en Given son reales.** Los fixtures usan coordenadas GPS plausibles para Colombia (Latitud: 4.711, Longitud: -74.072 — Bogotá), valores realistas para el equipo (`CARGADOR-EX-201`, proyecto 3, rutina id=18), y `DateTimeOffset` fijo (`2026-05-05T08:30:12-05:00`). No hay `(0,0)` ni valores absurdos.

### 2.3 Implementación

- [x] **Código de producción mínimo.** Cada propiedad pública de `Inspeccion` es asignada por `Apply` y verificada en el rebuild test (§6.12). `CapabilityRequeridaException` está definida en `Excepciones.cs` pero no es invocada por `Inspeccion.Iniciar` — ver hallazgo #1 (followup).

- [x] **Sin `DateTime.UtcNow`, `Guid.NewGuid()`, ni APIs de navegador en el dominio.** `ahora` llega como parámetro `DateTimeOffset` desde el handler; el dominio solo hace `DateOnly.FromDateTime(ahora.UtcDateTime)`.

- [x] **Tipos de IDs correctos.** `EquipoId`, `RutinaId`, `ProyectoId` son `int` (PKs del ERP). `InspeccionId` es `Guid`. Convenio §15.4 cumplido.

- [x] **`UbicacionGps` y `LecturaMedidor` usados como value objects.** Sin primitivos pelados para coordenadas ni para lecturas. `Latitud`/`Longitud` son `decimal`, no `double`. `UbicacionGps` tiene `CapturadoEn: DateTimeOffset`. Cumplido.

- [x] **Events y comandos son records inmutables.** `InspeccionIniciada_v1` y `IniciarInspeccion` son `sealed record` con constructor posicional; sin setters públicos.

- [x] **`Apply(InspeccionIniciada_v1 e)` puro.** El método solo asigna propiedades desde el evento. Sin `if`, sin `throw`, sin llamadas a `Iniciar`. El comentario XML lo documenta explícitamente. El rebuild test confirma que `Reconstruir` sobre los eventos del happy path no lanza.

- [x] **Rebuild test presente y completo.** `IniciarInspeccion_rebuild_desde_stream_reproduce_estado` verifica: `InspeccionId`, `Estado=EnEjecucion`, `EquipoId`, `RutinaId`, `RutinaCodigo`, `ProyectoId`, `FechaReportada`, `IniciadaEn`. Cubre el estado observable completo post-Apply. Solo omite `Ubicacion`, `LecturaMedidorPrimario`, `LecturaMedidorSecundario` — ver hallazgo #2 (nit).

- [x] **Atomicidad del handler.** El handler (`IniciarInspeccionHandler.cs`) está stubbeado con `throw new NotImplementedException()` — fuera del alcance de este slice. La atomicidad se verifica en 1b.

### 2.4 Cobertura

- [x] **Cobertura de ramas del agregado `Inspeccion`: 97.6 % de líneas, 94.4 % de ramas globales del ensamblado.** Supera holgadamente el umbral del 85 %.

  Desglose de lo no cubierto:

  | Líneas | Elemento | Razón |
  |---|---|---|
  | 128-129 (`Inspeccion.cs`) | `default: throw new InvalidOperationException(...)` en `AplicarEvento` | La rama solo se alcanza pasando un tipo de evento no reconocido. En slice 1a el único evento válido es `InspeccionIniciada_v1`. Fabricar un evento dummy sintético para cubrir la rama sería un test de plomería, no de dominio. La red de seguridad opera correctamente cuando lleguen eventos de slices futuros. |
  | `CapabilityRequeridaException` (0 %) | Clase de excepción sin instanciación en el dominio | PRE-1 vive en capa HTTP. La excepción se define en el dominio para que la capa HTTP pueda referenciarla. Correcto que el agregado nunca la lance. |

  Ambas razones son aceptables y alineadas con la decisión de diseño documentada en la spec §4.

- [x] **Ramas descubiertas justificadas.** La rama `default` del switch no aparece en `refactor-notes.md` de forma explícita. Sin embargo, green-notes §5.4 lo documenta como decisión deliberada. Se agrega followup #11 para que el `refactorer` del primer slice que agregue un segundo evento al agregado añada un test negativo de evento desconocido.

### 2.5 Refactor

- [x] **`refactor-notes.md` presente.** Incluye tabla de 5 impulsos descartados con justificación para cada uno y análisis propio adicional (orden de miembros, docstrings). Claro y trazable.

- [x] **Tests no cambiaron entre green y refactor.** `refactor-notes.md` declara "Cero cambios" y la sección de verificación muestra que el archivo de producción llegó intacto al reviewer. Confirmado: los 16 tests que pasan hoy son los mismos 11 escritos en red + 5 boundary/guard adicionales.

  Nota: el `red-notes.md` dice 11 tests y el `green-notes.md` referencia 14 en el encabezado. Los 16 reales (11 nominados + 3 boundary de I-I3 + 1 guard de no-efecto + 1 BootstrapTest preexistente) son coherentes — los 3 boundary y el guard estaban en los tests pero no en la tabla del `red-notes.md`. No es un problema porque la tabla era ilustrativa.

- [x] **Sin warnings de compilación.** `dotnet build` confirma 0 advertencias, 0 errores (`TreatWarningsAsErrors=true` activo).

### 2.6 Invariantes cross-slice

- [x] **`dotnet test` del proyecto `Inspecciones.Domain.Tests` completo en verde.** 16/16 tests pasan (incluye `BootstrapTests` preexistente). Los tests del handler (`Inspecciones.Application.Tests`) siguen con `[Fact(Skip="...")]` — no compiten con CI.

  Nota: no se corrió el suite completo de la solución porque los proyectos que necesitan Postgres real (`Inspecciones.Application.Tests`, `Inspecciones.Api.Tests`) están en Skip o tienen tests de integración que requieren Docker. El slice 1a solo añade código al dominio puro; no hay riesgo de romper tests de otros proyectos por cambios en `Inspeccion.cs`, un archivo nuevo.

### 2.7 Coherencia con decisiones previas

- [x] **Alineado con §15.4 del modelo.** Payload de `InspeccionIniciada_v1` coincide campo a campo: `InspeccionId (Guid)`, `Tipo=Tecnica`, `EquipoId (int)`, `RutinaId (int)`, `RutinaCodigo (string)`, `TecnicoIniciador (string)`, `ProyectoId (int)`, `Ubicacion (UbicacionGps)`, `IniciadaEn (DateTimeOffset)`, `FechaReportada (DateOnly)`, `LecturaMedidorPrimario?`, `LecturaMedidorSecundario?`.

- [x] **Alineado con §15.7 (invariantes I-I1, I-I2, I-I3).** I-I1 fuera de alcance (1b), documentado. I-I2 cubierto por PRE-5 y PRE-6. I-I3 cubierto por PRE-7 con boundary tests.

- [x] **Alineado con ADR-004 (catálogos).** `EquipoLocal` y `RutinaTecnicaLocal` son los read models del catálogo local; el dominio los recibe como parámetros del handler, sin acceso directo a IDocumentSession.

- [x] **ADR-007 (capability `ejecutar-inspeccion`).** `ClaimsTecnico.TieneCapabilityEjecutarInspeccion` está modelado en el record; PRE-1 delegado a capa HTTP. Correcto.

- [x] **Refinamiento β 2026-05-04 (rutina técnica 1:1 por equipo).** `EquipoLocal.RutinaTecnicaId: int?` (singular); el handler resuelve y pasa la rutina, el técnico no elige. Implementado.

- [x] **Followup #2 y #3 cerrados.** `FechaReportada` como campo independiente (`DateOnly`); `LecturaMedidorPrimario?` y `LecturaMedidorSecundario?` en comando y evento. Implementados.

### 2.8 Integración cross-team Sinco

No aplica. El slice no consume ni publica hacia endpoints Sinco on-prem.

### 2.9 SignalR / push

No aplica. El spec §10 lo documenta explícitamente: `InspeccionIniciada_v1` no genera push en el catálogo vigente de ADR-005.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | followup | `CapabilityRequeridaException` está definida en `Excepciones.cs` (dominio) con 0 % de cobertura. Es correcto que el agregado no la lance (PRE-1 vive en capa HTTP), pero al estar en el mismo archivo que las demás excepciones del dominio, el informe de cobertura la contabiliza como código no ejercido del ensamblado. Si en el futuro el handler delega la verificación al dominio, esta excepción estará lista; si no, puede moverse a la capa de aplicación o API. | `src/Inspecciones.Domain/Inspecciones/Excepciones.cs:7-8` | Followup #11: evaluar en slice 1b si `CapabilityRequeridaException` debe vivir en el dominio o en la capa HTTP. Sin cambio ahora. |
| 2 | nit | El test `IniciarInspeccion_rebuild_desde_stream_reproduce_estado` no verifica `Ubicacion`, `LecturaMedidorPrimario` ni `LecturaMedidorSecundario` tras el rebuild. El happy path base usa `UbicacionInicio` fijada en `Fixtures.UbicacionTipo()` y lecturas `null`. La omisión es razonablemente baja en riesgo (`Apply` asigna esos campos directamente desde el evento), pero un futuro cambio a `Apply` podría silenciar un bug en esos tres campos. | `tests/Inspecciones.Domain.Tests/Inspecciones/IniciarInspeccionTests.cs:264-283` | Asumido como nit. Si el reviewer del slice de `RegistrarLecturaMedidor` ve que el rebuild test de ese slice cubre las lecturas, este nit se cierra. De lo contrario, reforzar en el primer slice que toque `Apply` con lecturas. |
| 3 | nit | La rama `default: throw new InvalidOperationException(...)` en `AplicarEvento` (líneas 128-129) no tiene test directo. Está justificada por la decisión de diseño (un test de plomería no aporta valor de documentación), pero no está mencionada en `refactor-notes.md`. | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs:127-130` | Followup #12: cuando el primer evento nuevo llegue al agregado (slice 2+), el `red` de ese slice debe añadir un test negativo que pase un tipo de evento desconocido a `Reconstruir` y verifique que lanza `InvalidOperationException`. Ese es el momento natural de ejercitar la rama. |

---

## 4. Veredicto final

- [x] **approved-with-followups** — dos followups registrados en `FOLLOWUPS.md` (hallazgos #1 y #3). El nit #2 se asume sin followup formal; se anota para el reviewer de `RegistrarLecturaMedidor`.

El orquestador puede proceder al commit `feat(slice-1a): IniciarInspeccion aggregate puro` y a la fase de infra-wire del slice 1b.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
