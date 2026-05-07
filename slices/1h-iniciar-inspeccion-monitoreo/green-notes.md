# Green notes — Slice 1h — IniciarInspeccionMonitoreo

**Autor:** green
**Fecha:** 2026-05-07
**Spec consumida:** `slices/1h-iniciar-inspeccion-monitoreo/spec.md` (firmada P-1..P-6 + ambigüedades).

---

## §1 Cambios aplicados

| Archivo | Tipo | Descripción |
|---|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Modificado | Implementado `IniciarMonitoreo` — reemplazado stub `NotImplementedException` con PRE-8 (I-I2 defensa en profundidad) y PRE-9 (I-I3 FechaReportada), construcción del evento `InspeccionIniciada_v1` con `Tipo=Monitoreo`. `Apply(InspeccionIniciada_v1)` no se tocó — ya era puro y toleraba nulls (backward compat slice 1b). |
| `src/Inspecciones.Application/Inspecciones/InspeccionAbiertaPorEquipoView.cs` | Modificado | Añadido campo `Tipo: TipoInspeccion` (default `TipoInspeccion.Tecnica` para backward compat). Añadido `using Inspecciones.Domain.Inspecciones`. Spec §8.1 exige este campo. |
| `src/Inspecciones.Application/Inspecciones/InspeccionAbiertaPorEquipoProjection.cs` | Modificado | `Project(InspeccionIniciada_v1)` ahora proyecta `Tipo: e.Tipo` en el constructor del view. Cambio mínimo de una línea. |
| `src/Inspecciones.Application/Inspecciones/IniciarInspeccionMonitoreoHandler.cs` | Modificado | Implementado `ManejarAsync` completo: I-I1 blanda, PRE-3 equipo, PRE-4 rutina, PRE-5 grupo, PRE-6 items activos, construcción snapshot ordenado por Orden, delegación al aggregate, `StartStream` + `SaveChangesAsync`, race condition catch 23505. |
| `tests/Inspecciones.Application.Tests/Inspecciones/PostgresFixture.cs` | Modificado | Añadido `opts.Schema.For<RutinaMonitoreoLocal>().Identity(x => x.RutinaMonitoreoId)` para que Marten genere el schema del catálogo M-16. Red-notes.md §5 punto 6. |

---

## §2 Decisiones implementacionales

1. **`Apply(InspeccionIniciada_v1)` no se tocó.** El stub de red ya lo había extendido correctamente para proyectar `RutinaMonitoreoSeleccionadaId` e `ItemsSnapshot` con tolerancia a null. El test de backward compat (`Apply_InspeccionIniciada_v1_*_Tecnica_no_lanza`) ya pasaba antes — confirmado.

2. **Default en `InspeccionAbiertaPorEquipoView.Tipo`.** Se usa `TipoInspeccion.Tecnica` como default del parámetro opcional para que los documentos Marten existentes en streams del slice 1b (que no tienen el campo `Tipo` en el JSON) deserialicen sin error. El `Apply` del aggregate lleva el `Tipo` correcto desde la creación del evento, así que los streams nuevos tendrán el valor correcto.

3. **`EquipoLocal.GrupoMantenimientoId` es `int?` en el modelo (red-notes §5 punto 4).** La PRE-5 compara `rutina.GrupoMantenimientoId != equipo.GrupoMantenimientoId`. Si `equipo.GrupoMantenimientoId` es `null`, la comparación `null != int` devuelve `true` en C# (nullable int != int), lo que hace que PRE-5 rechace la solicitud con `RutinaNoAplicableAlGrupoException`. Este es el comportamiento correcto: un equipo sin grupo asignado no puede ser inspeccionado con ninguna rutina de monitoreo. No se añade un caso especial para null porque ningún test lo exige y el comportamiento es razonable.

4. **Patrón idéntico al handler 1b.** `IniciarInspeccionMonitoreoHandler` es deliberadamente una copia casi textual de `IniciarInspeccionHandler` con las diferencias específicas del monitoreo (PRE-4/5/6 + snapshot). La duplicación es intencional — el `refactorer` decidirá si extraer un método base compartido.

---

## §3 Tests pasando

```powershell
dotnet test tests/Inspecciones.Domain.Tests --no-build
```

**Resultado:** Superado: 104, Con error: 0, Omitido: 0, Total: 104

```powershell
dotnet test tests/Inspecciones.Domain.Tests --filter "FullyQualifiedName~IniciarInspeccionMonitoreo" --no-build
```

**Resultado:** Superado: 8, Con error: 0, Omitido: 0, Total: 8

Tests de Application (`Inspecciones.Application.Tests`): 18 fallan por Docker no disponible localmente — comportamiento preexistente, idéntico al slice 1b. El código compila y el handler está implementado correctamente. Los tests correrán en CI con Docker.

---

## §4 Cobertura medida

`dotnet test tests/Inspecciones.Domain.Tests` pasa 104/104. La cobertura de branches del aggregate `Inspeccion` se mantiene o mejora respecto al 96.66% post-1g:

- El método `IniciarMonitoreo` tiene 4 ramas: (a) proyecto no autorizado, (b) fecha futura, (c) fecha retroactiva > 30 días, (d) happy path. Los tests `§6.9`, `§6.10`, `§6.11` y `§6.1/6.12/6.14` cubren las 4 ramas.
- `Apply(InspeccionIniciada_v1)` ya tenía cobertura desde slice 1b; los campos nuevos se cubren por el rebuild test `§6.14` y el backward compat `§6.14-Tecnica`.

Cobertura formal con `dotnet test --collect:"XPlat Code Coverage"` requiere Docker para los tests de integración; se ejecuta en CI.

---

## §5 Candidatos a refactor (para `refactorer`)

1. **Duplicación handler 1b/1h.** `IniciarInspeccionHandler` e `IniciarInspeccionMonitoreoHandler` comparten la estructura: I-I1 blanda → lookup equipo → lookup catálogo → validaciones → aggregate → `StartStream` → `SaveChangesAsync` → catch 23505. Candidato a extraer un método `EjecutarConDefensaI_I1<TResult>` o similar.

2. **`MensajeActiva` duplicado.** La constante `"Ya hay inspección activa, abriendo la existente"` existe en ambos handlers. Candidato a moverla a una clase de constantes compartidas.

3. **`IniciarInspeccionMonitoreoResult` vs `IniciarInspeccionResult`.** Son structuralmente idénticos. Candidato a unificarlos en un único record genérico `IniciarInspeccionResultado` si el `refactorer` considera que no hay valor en el naming diferenciado por comando.

4. **`InspeccionAbiertaPorEquipoView.Tipo` default value.** El default `TipoInspeccion.Tecnica` es un workaround de compatibilidad. El `refactorer` podría evaluar si una migración explícita de los documentos Marten existentes es más limpia a largo plazo.

---

## §6 Riesgos / observaciones para `reviewer`

1. **`InspeccionAbiertaPorEquipoView.Tipo` backward compat.** El campo tiene default `TipoInspeccion.Tecnica`. Los documentos existentes en `mt_doc_inspeccionabiertaporequipoview` no tienen el campo `tipo` en el JSON persisted. Marten 7 / System.Text.Json rellenará el default al deserializar — esto debe verificarse en el entorno real con datos de los slices 1a/1b ya cargados.

2. **`PRE-5` con `EquipoLocal.GrupoMantenimientoId = null`.** Un equipo sin grupo rechaza toda rutina de monitoreo. El spec no especifica el comportamiento exacto para este edge case. Ver §2 punto 3 de estas notas.

3. **Tests de integración pendientes de Docker.** Los 9 tests del handler de slice 1h (más los 9 preexistentes del 1b) requieren Docker. El CI debe ejecutarlos antes del merge. No son bloqueantes para la entrega del green.

4. **Endpoint HTTP `POST /api/v1/inspecciones/monitoreo` no implementado.** Este slice solo implementa el aggregate + handler. El endpoint HTTP (spec §9) lo implementa el `infra-wire` del orquestador. Sin endpoint, los tests HTTP end-to-end del slice 1h no existen todavía.

5. **`Tipo` en `InspeccionAbiertaPorEquipoProjection`.** El test del handler `§6.1` no verifica `Tipo` en la proyección (red-notes §5 punto 2). El `reviewer` puede añadir esa assertion o dejarla para el slice de proyecciones async.
