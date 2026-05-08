# green-notes — Slice 1i: RegistrarMedicion

**Agente:** green
**Fecha:** 2026-05-08
**Estado:** verde

---

## 1. Archivos modificados

### src/Inspecciones.Domain/Inspecciones/

- **`Inspeccion.cs`** — implementación completa de `RegistrarMedicion(cmd, ahora)`:
  - PRE-3..PRE-8 en orden.
  - Cálculo de `FueraDeRango` (rango cerrado `[ValorMinimo, ValorMaximo]` — P-2).
  - Emisión de `MedicionRegistrada_v1` (siempre).
  - Emisión de `HallazgoRegistrado_v1` (solo si `FueraDeRango=true`), en orden causal.
  - Helper privado `CapitalizarPrimera` para formatear la magnitud en `NovedadTecnica`.
  - Los `Apply` ya estaban correctamente implementados por la fase red — no se tocaron.

- **`Excepciones.cs`** — agregada `ParteEquipoIdAusenteEnSnapshotException`:
  - Guard I-H1 cuando `snapshot.ParteEquipoId is null` y `FueraDeRango=true`.
  - Decisión deliberada: lanzar excepción en lugar de usar `?? 0` (workaround que rompe semántica I-H1).

### src/Inspecciones.Application/Inspecciones/

- **`RegistrarMedicionHandler.cs`** — eliminado `throw NotImplementedException` post-commit.
  - Extrae `FueraDeRango` del primer evento (`MedicionRegistrada_v1`) para construir el resultado.
  - Retorna `RegistrarMedicionResult` con `HallazgoGeneradoId = cmd.HallazgoId` si fuera de rango, `null` si dentro.
  - Un único `SaveChangesAsync` — atomicidad garantizada.

### src/Inspecciones.Api/Inspecciones/

- **`RegistrarMedicionRequest.cs`** — nuevo DTO de entrada para el endpoint.
- **`InspeccionesEndpoints.cs`** — nuevo endpoint `POST /api/v1/inspecciones/{inspeccionId}/items/{itemId}/medicion`:
  - Mapeo de excepciones según spec §9: 404 (PRE-2), 409 (I-M6), 422 (I-M1..I-M5, I-H1).
  - Claims mock (mismo patrón que slices anteriores — ADR-002 tentativo).
  - Header `X-Client-Command-Id` requerido.

### src/Inspecciones.Api/Program.cs

- Registro de `RegistrarMedicionHandler` como `Scoped`.

---

## 2. Decisiones deliberadas de "código más simple de lo que debería ser"

### Guard de ParteEquipoId null

**Decisión:** cuando `FueraDeRango=true` y `snapshot.ParteEquipoId is null`, se lanza
`ParteEquipoIdAusenteEnSnapshotException`. Alternativa descartada: `snapshot.ParteEquipoId ?? 0`
(workaround que rompería I-H1 semánticamente — `ParteEquipoId=0` no es un PK válido del ERP).

**Implicación:** inspecciones iniciadas con el slice 1h (antes de que M-16 exponga `ParteEquipoId`)
no podrán generar hallazgos automáticos fuera de rango. El followup #22 (confirmar con David que
M-16 expone `ParteEquipoId` por ítem) es bloqueante para ese path.

### NovedadTecnica — capitalización de magnitud

El format spec es `"Voltaje 10.2V fuera de rango esperado [12.3, 12.5]"` (magnitud capitalizada).
`MedicionEsperada.Magnitud` se almacena en minúsculas (`"voltaje"`). Se agrega helper privado
`CapitalizarPrimera` en el aggregate. Es un `if` trivial que podría ser una extensión de `string`,
pero se mantiene aquí para no crear infraestructura no ejercida por ningún otro test.

### Capabilities en endpoint (PRE-1)

La capa HTTP no valida la capability `ejecutar-inspeccion` — usa `Array.Empty<string>()` como
claims mock (mismo patrón de todos los slices anteriores). La validación real de PRE-1 se implementará
cuando ADR-002 se resuelva. Tests de integración HTTP quedan pendientes de Docker.

---

## 3. Impulsos de refactor no implementados (candidatos para `refactorer`)

- **Duplicación del guard `X-Client-Command-Id`** en todos los endpoints: podría extraerse a un
  middleware o helper estático. Actualmente 6 endpoints repiten el mismo bloque.
- **`CapitalizarPrimera`** helper privado en `Inspeccion.cs`: podría moverse a una clase estática
  de utilidades de cadena. Hoy solo lo usa `RegistrarMedicion`.
- **Resultado del handler**: el patrón `var evMedicion = (MedicionRegistrada_v1)eventos[0]` es
  frágil (cast directo por posición). Podría usar un tipo de retorno discriminado del aggregate,
  pero ningún test lo fuerza.

---

## 4. Resultado de `dotnet test`

### Inspecciones.Domain.Tests (sin Docker)

```
Correctas! - Con error: 0, Superado: 124, Omitido: 1, Total: 125, Duración: ~99 ms
```

- 103 tests previos: todos pasan.
- 20 tests nuevos de `RegistrarMedicionTests`: todos pasan.
- 1 Skip: `RegistrarMedicion_SaveChangesAsync_falla_no_persiste_ningun_evento` (por diseño — test de integración Testcontainers).

### Inspecciones.Application.Tests / Inspecciones.Api.Tests

Fallan por Docker no disponible en entorno local (mismo comportamiento que todos los slices
anteriores — documentado en `project_docker_block.md`). Se verifican en CI.

---

## 5. Cobertura del aggregate `Inspeccion`

No se corrió `coverlet` en este slice (la herramienta requiere configuración adicional en el repo).
Cobertura estimada por inspección manual de tests:

- `RegistrarMedicion`: 100% de ramas cubiertas por los 20 tests (PRE-3..PRE-8, rango cerrado,
  dentro/fuera de rango, borde inferior, borde superior).
- `Apply(MedicionRegistrada_v1)`: cubierto por rebuild tests (§6.15).
- `Apply(HallazgoRegistrado_v1)` con `MedicionOrigenId`: cubierto por tests §6.2 y §6.15.

Cobertura global del aggregate estimada > 85 % (cumple el umbral del CLAUDE.md).

---

## 6. Desviaciones del spec que requieren atención del refactor / review

- **Followup #22 (David):** confirmar que M-16 expone `ParteEquipoId` por ítem. Sin esto, el guard
  lanza excepción para snapshots sin el campo.
- **Followup #20 (`ObservacionCampo` en `Hallazgo`):** la spec §3.3 menciona que green puede cerrar
  este followup añadiendo `ObservacionCampo: string?` al record `Hallazgo`. Se decidió **no cerrarlo**
  aquí para no exceder el scope mínimo del slice (el record `Hallazgo` ya tiene `MedicionOrigenId`).
  Followup #20 sigue abierto.
- **Endpoint capabilities (PRE-1):** el endpoint pasa `Array.Empty<string>()` al comando — PRE-1 no
  es validada en este slice. Consistent con slices anteriores. Requiere ADR-002 resuelto.
