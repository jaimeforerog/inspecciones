---
status: diagnosed
trigger: "FU-36: POST /inspecciones/{id}/hallazgos retorna 400 en happy path"
created: 2026-05-11T00:00:00Z
updated: 2026-05-11T00:00:00Z
---

## Current Focus

hypothesis: System.Text.Json no tiene JsonStringEnumConverter registrado y rechaza el string "Manual" → falla binding del body con 400 antes de llegar al handler. CONFIRMADO con stack trace.
test: ejecutado dotnet test con verbosity=normal — capturado log de excepción.
expecting: ya confirmado.
next_action: reportar diagnóstico (NO aplicar fix — modo find_root_cause_only).

## Symptoms

expected: POST /api/v1/inspecciones/{id}/hallazgos retorna 201 Created
actual: retorna 400 BadRequest en happy path
errors: (pendiente capturar body del 400)
reproduction: dotnet test --filter "FullyQualifiedName~RegistrarHallazgo"
started: existente desde slice 1c; FU-36 abierto post FU-37/FU-38

## Eliminated

(none yet)

## Evidence

- timestamp: 2026-05-11T00:00:00Z
  checked: tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs
  found: 2 tests; payload con campos snake/camel: hallazgoId, origen, parteEquipoId, novedadPreopOrigenId, actividadId, actividadDescripcion, novedadTecnica, accionRequerida, accionCorrectiva, tipoFallaId, causaFallaId, observacionCampo, ubicacion. Header X-Client-Command-Id seteado. Ruta /api/v1/inspecciones/{id}/hallazgos.
  implication: el header se setea, así que NO falla por HEADER-REQUERIDO. Probable falla en binding del body o validación previa.

- timestamp: 2026-05-11T00:00:00Z
  checked: src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs líneas 177-250
  found: endpoint mapea {id:guid}, recibe RegistrarHallazgoRequest del body, valida header, construye command RegistrarHallazgo y llama handler.ManejarAsync(cmd, ct). NO hay validation attributes visibles en endpoint (solo header check).
  implication: el 400 probablemente viene de model binding fail (JSON no matchea record), no del header check.

## Evidence (continuado)

- timestamp: 2026-05-11T00:00:30Z
  checked: dotnet test --filter RegistrarHallazgo con verbosity=normal
  found: log de excepción del servidor: "System.Text.Json.JsonException: The JSON value could not be converted to Inspecciones.Api.Inspecciones.RegistrarHallazgoRequest. Path: $.origen | LineNumber: 0 | BytePositionInLine: 70. at System.Text.Json.Serialization.Converters.EnumConverter`1.Read(...)". Response: 400 BadRequest text/plain.
  implication: System.Text.Json intenta deserializar "Manual" como entero (default para enums) → falla → RequestDelegate retorna 400 antes de entrar al handler. Tests fallan idénticamente para happy_path e idempotencia (ambos envían "Manual"/"NoRequiereIntervencion" como strings).

- timestamp: 2026-05-11T00:00:40Z
  checked: Program.cs — configuración JSON
  found: no hay AddJsonOptions ni ConfigureHttpJsonOptions registrados. Comentario línea 30: "JSON serializer — usa el default de Marten (Newtonsoft.Json). El detalle de configuración del serializer (System.Text.Json + casing + enum como string) se cierra en un slice posterior cuando emerja necesidad concreta." Marten usa Newtonsoft, pero Minimal APIs usan System.Text.Json por separado.
  implication: el comentario reconocía que la config emergería con necesidad. FU-36 es esa necesidad emergente.

- timestamp: 2026-05-11T00:00:50Z
  checked: DTOs de los demás slices
  found:
    - RegistrarHallazgoRequest: 2 enums directos (OrigenHallazgo, AccionRequerida) → FALLA (FU-36).
    - ActualizarHallazgoRequest: 1 enum directo (AccionRequerida) → bug latente, sin test E2E que lo exponga.
    - FirmarInspeccionRequest: 1 enum directo (DictamenOperacion) → bug latente, sin test E2E.
    - GenerarOTRequest: usa string + Enum.TryParse en endpoint (líneas 797-813) → funciona.
    - RegistrarEvaluacionCualitativaRequest: usa string + Enum.TryParse (líneas 630-637) → funciona.
    - EliminarHallazgo / AsignarRepuesto / IniciarInspeccion / IniciarInspeccionMonitoreo: sin enums → funcionan.
  implication: el bug es generalizado en el contrato HTTP; FU-36 solo es la cara visible. Hay 2 endpoints más (ActualizarHallazgo, FirmarInspeccion) con la misma exposición aunque sus tests E2E aún no los ejercen.

## Resolution

root_cause: System.Text.Json (serializer default de ASP.NET Core Minimal APIs) deserializa enums como enteros por default. El payload del test envía "Manual" (string) para el campo `origen` tipado como `OrigenHallazgo` (enum). Sin `JsonStringEnumConverter` registrado en `Program.cs`, el binding del body falla con `JsonException` y RequestDelegate retorna 400 BadRequest antes de invocar el handler. Esto ocurre en `RegistrarHallazgoRequest` y queda latente en `ActualizarHallazgoRequest` y `FirmarInspeccionRequest`.

fix: (no aplicado — find_root_cause_only)
verification: (pendiente fix)
files_changed: []
