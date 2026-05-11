# Fix FU-36 — JsonStringEnumConverter ausente en Minimal APIs (enums deserializados como int)

**Autor:** domain-modeler
**Fecha:** 2026-05-11
**Estado:** draft
**Agregado afectado:** ninguno — fix puramente de configuración del serializer HTTP
**Decisiones previas relevantes:** comentario `Program.cs:28-30` ("El detalle de configuración del serializer (System.Text.Json + casing + enum como string) se cierra en un slice posterior cuando emerja necesidad concreta") — FU-36 es ese slice posterior.

---

## 0. Contexto del bug

### Causa raíz

ASP.NET Core Minimal APIs usa `System.Text.Json` como serializer por defecto. En `System.Text.Json`, el comportamiento por defecto para enums es serialización/deserialización como enteros (`int`). El módulo no registra `ConfigureHttpJsonOptions` ni `JsonStringEnumConverter` en ningún punto de `Program.cs`.

Los tests del slice 1c (`RegistrarHallazgo`) envían el campo `origen` como el string `"Manual"` (valor del enum `OrigenHallazgo`) y `accionRequerida` como `"NoRequiereIntervencion"` (valor del enum `AccionRequerida`). Cuando `RequestDelegateFactory` intenta deserializar el body JSON, `System.Text.Json` espera un número entero en esos campos y encuentra un string — lo que produce:

```
Microsoft.AspNetCore.Http.BadHttpRequestException: Failed to read parameter
"RegistrarHallazgoRequest request" from the request body as JSON.
 ---> System.Text.Json.JsonException: The JSON value could not be converted to
      Inspecciones.Api.Inspecciones.RegistrarHallazgoRequest.
      Path: $.origen | LineNumber: 0 | BytePositionInLine: 70.
```

`RequestDelegateFactory` captura la `JsonException` y retorna `400 BadRequest` **antes de invocar el handler**. El `try/catch` del endpoint nunca se ejecuta; el handler de dominio no recibe el comando.

### Reconocimiento previo en el código

`Program.cs` líneas 28-30 contienen el comentario:

```
// JSON serializer — usa el default de Marten (Newtonsoft.Json). El detalle de
// configuración del serializer (System.Text.Json + casing + enum como string) se
// cierra en un slice posterior cuando emerja necesidad concreta.
```

FU-36 es la necesidad concreta que el comentario anticipaba. Al aplicar el fix, ese comentario se elimina.

### Impacto directo vs. latente

| DTO / endpoint | Enums directos | Impacto |
|---|---|---|
| `RegistrarHallazgoRequest` | `OrigenHallazgo`, `AccionRequerida` | FALLA — tests rojos visibles |
| `ActualizarHallazgoRequest` | `AccionRequerida` | BUG LATENTE — sin test E2E que lo exponga hoy |
| `FirmarInspeccionRequest` | `DictamenOperacion` | BUG LATENTE — sin test E2E que lo exponga hoy |
| `GenerarOTRequest` | usa `string + Enum.TryParse` en endpoint | SIN IMPACTO — workaround explícito |
| `RegistrarEvaluacionCualitativaRequest` | usa `string + Enum.TryParse` en endpoint | SIN IMPACTO — workaround explícito |

El fix global cierra también los 2 bugs latentes como efecto colateral. No se escriben tests nuevos para ellos en este slice.

### Decisión arquitectónica: por qué `ConfigureHttpJsonOptions` global y no DTO-specific

Tres alternativas fueron consideradas:

| Alternativa | Por qué se descarta |
|---|---|
| `[JsonConverter(typeof(JsonStringEnumConverter))]` por campo en cada DTO | Requiere decorar cada propiedad enum en cada DTO presente y futuro. Error propenso a omisión. El comportamiento "enums como string en HTTP" es una convención de toda la API, no de DTOs individuales. |
| `[JsonConverter(typeof(JsonStringEnumConverter))]` a nivel de clase DTO | Requiere decorar cada record DTO. Mismo problema de mantenimiento + no cubre futuros DTOs. |
| `ConfigureHttpJsonOptions` global en `Program.cs` | Una sola declaración cubre todos los endpoints presentes y futuros. Consistente con el patrón de configuración centralizado ya presente en `Program.cs` (`AddMarten`, `UseWolverine`, `AddSignalR`). No requiere tocar ningún DTO. |

La opción global es la correcta y es la que el comentario original de `Program.cs` anticipaba.

### Decisión arquitectónica: por qué no especificar `JsonNamingPolicy` ahora

El casing de los campos en las responses y requests funciona correctamente con los tests actuales usando el default (PascalCase en serialización, con `PropertyNameCaseInsensitive` implícito en la deserialización de Minimal APIs). Añadir `JsonNamingPolicy.CamelCase` o `JsonNamingPolicy.SnakeCaseLower` en este fix sería oportunista: podría romper asserts en los 28 tests que ya pasan si alguno verifica los nombres exactos de los campos de respuesta en PascalCase. El scope mínimo (solo `JsonStringEnumConverter`) está respaldado por los tests actuales. Si emerge necesidad de naming policy, se abre un FU separado.

El único riesgo conocido del default case-sensitive documentado en §5.

### Decisión arquitectónica: por qué los DTOs con `string + Enum.TryParse` quedan como están

`GenerarOTRequest` y `RegistrarEvaluacionCualitativaRequest` usan `string + Enum.TryParse` explícitamente en el cuerpo del endpoint (no en el DTO). Refactorearlos a enums directos requeriría cambiar el DTO y el endpoint, añadir cobertura de tests para el nuevo error path, y posiblemente tocar un handler. Todo eso es scope extra que no tiene test rojo asociado hoy. FU-36 es el fix mínimo que pone los 2 tests rojos en verde. El refactor de esos DTOs se registra como deuda separada si el usuario la prioriza.

---

## 1. Scope

### 1.1 Archivo modificado

**Único archivo:** `src/Inspecciones.Api/Program.cs`

Cambios:

1. Añadir bloque `builder.Services.ConfigureHttpJsonOptions(...)` con `JsonStringEnumConverter` antes de `var app = builder.Build()`.
2. Eliminar el comentario de las líneas 28-30 ("El detalle de configuración del serializer... se cierra en un slice posterior") que ya no aplica.

Estimación: ~5 líneas añadidas, ~3 líneas de comentario removidas.

### 1.2 Fuera de scope

- `src/Inspecciones.Domain/**` — sin cambios.
- `src/Inspecciones.Application/**` — sin cambios a handlers.
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` — sin cambios.
- `tests/**` — no se escriben tests nuevos. Los 2 tests rojos ya existen y se volverán verdes.
- `GenerarOTRequest` y `RegistrarEvaluacionCualitativaRequest` (string + Enum.TryParse) — quedan como deuda separada.
- Eventos, agregados, proyecciones, adapters Sinco, hub SignalR.

---

## 2. Tests target (rojos que se vuelven verdes)

Los 2 tests ya existen en `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs`. Ambos fallan con HTTP 400 en lugar de 201/200 porque el binding del body falla antes de llegar al handler.

### Test 1 — Happy path E2E

**Clase:** `RegistrarHallazgoEndpointTests`
**Método:** `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created`
**Falla actual:** HTTP 400 (`JsonException: The JSON value could not be converted to RegistrarHallazgoRequest. Path: $.origen`)
**Comportamiento esperado tras fix:** HTTP 201 Created con body `{ hallazgoId, inspeccionId, accionRequerida: "NoRequiereIntervencion", registradoEn }`

### Test 2 — Idempotencia ADR-008

**Clase:** `RegistrarHallazgoEndpointTests`
**Método:** `POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008`
**Falla actual:** HTTP 400 en el primer POST (mismo JsonException) — nunca llega a ejecutar la lógica de dedup
**Comportamiento esperado tras fix:** el fix libera el camino al handler; el comportamiento de dedup Wolverine (ADR-008) es una dependencia de infraestructura — este test puede quedar en estado "pasa solo con Wolverine envelope dedup configurado" y podría mantenerse en skip si esa infraestructura no está activa en el entorno de test. Ver §4 para el conteo final esperado.

---

## 3. No toca

- `src/Inspecciones.Domain/**`
- `src/Inspecciones.Application/**`
- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs`
- `tests/**` — cero tests nuevos escritos
- DTOs con `string + Enum.TryParse` (`GenerarOTRequest`, `RegistrarEvaluacionCualitativaRequest`) — deuda separada
- Eventos, agregados, proyecciones, adapters Sinco, hub SignalR, `appsettings`

**Único archivo modificado:** `src/Inspecciones.Api/Program.cs`

---

## 4. Resultado esperado

### 4.1 Conteo de tests

| Estado | Antes del fix (FU-36) | Después del fix |
|---|---|---|
| Passing | 28 | 30 |
| Failing | 2 | 0 |
| Skipped | 2 | 2 |
| Total | 32 | 32 |

Los 2 failing que se corrigen son exactamente los 2 tests rojos del slice 1c (`RegistrarHallazgo`). Los 2 skipped que permanecen son los tests de idempotencia ADR-008 — estos dependen de la infraestructura de Wolverine envelope dedup, no del bug de serialización, y su estado de skip es independiente de este fix.

Nota: si el Test 2 (`replay...ADR_008`) actualmente está contado como "failing" (no skipped), el conteo final es igualmente 30 passing + 2 skipped — el fix lo pone en la categoría que corresponda según la infraestructura del entorno. En cualquier caso, los 28 tests que pasaban antes del fix deben seguir pasando (cero regresiones).

### 4.2 Working tree tras el fix

```
M src/Inspecciones.Api/Program.cs
```

Un solo archivo modificado. Sin archivos nuevos.

---

## 5. Riesgos conocidos

### R-1 — Casing case-sensitive por defecto

`JsonStringEnumConverter` sin configuración adicional es case-sensitive: acepta `"Manual"` pero no `"manual"` ni `"MANUAL"`. Los tests actuales usan PascalCase exacto (`"Manual"`, `"NoRequiereIntervencion"`), que coincide con los nombres de los valores del enum. El riesgo emerge si en el futuro un test o cliente PWA envía un valor en lowercase o uppercase.

**Mitigación:** documentado aquí como riesgo conocido. Si emerge un test con casing distinto, el fix correcto es añadir `JsonStringEnumConverter` con `JsonNamingPolicy.CamelCase` (o la policy correspondiente) — no silenciar el error en el test. No se añade policy ahora para no romper los 28 tests verdes actuales.

### R-2 — Responses: enums en JSON pasan de int a string

Con `JsonStringEnumConverter` global, las propiedades enum en los response bodies que antes se serializaban como enteros (p. ej. `accionRequerida: 0`) ahora se serializarán como strings (`accionRequerida: "NoRequiereIntervencion"`).

**Verificación:** los asserts de los tests E2E actuales que leen propiedades enum del response body fueron revisados:

- `RegistrarHallazgoEndpointTests.cs:114` — el assert es `resultado!.AccionRequerida.Should().Be("NoRequiereIntervencion")` sobre un `string` (DTO de lectura `RespuestaRegistrarHallazgo` tiene `AccionRequerida: string`). El assert pasa tanto antes como después del fix. Sin riesgo.
- Los restantes 28 tests que pasan actualmente fueron revisados: ninguno aserta sobre el valor numérico de un enum en el response. Sin riesgo de regresión.

### R-3 — Compatibilidad con DTOs que usan `string + Enum.TryParse`

`GenerarOTRequest` y `RegistrarEvaluacionCualitativaRequest` usan `string + Enum.TryParse` en el cuerpo del endpoint (el DTO tiene un campo `string`, no un `enum`). `JsonStringEnumConverter` global solo afecta campos tipados como `enum` en el DTO; los campos tipados como `string` no se ven afectados. Sin impacto.

---

## 6. Definición de Done

- [ ] Los 2 tests rojos del slice 1c (`POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` y `POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008`) pasan en verde (o el segundo queda correctamente skipped por la razón Wolverine, no por el 400).
- [ ] Los 28 tests que estaban verdes antes del fix siguen verdes (cero regresiones).
- [ ] El comentario de `Program.cs` líneas 28-30 ("El detalle de configuración del serializer... se cierra en un slice posterior") está eliminado.
- [ ] `ConfigureHttpJsonOptions` con `JsonStringEnumConverter` está registrado en `Program.cs` antes de `builder.Build()`.
- [ ] La app compila sin warnings (`nullable` habilitado, `TreatWarningsAsErrors=true`).
- [ ] Suite completa: 30 passing, 0 failing, 2 skipped.
- [ ] Commit: `fix(FU-36): JsonStringEnumConverter en Minimal APIs — enums deserializados como string`.
- [ ] No se modificó ningún archivo fuera de `src/Inspecciones.Api/Program.cs`.

---

## 7. Idempotencia / retries

No aplica. Este fix no modifica comportamiento del dominio ni introduce rutas de escritura. La configuración del serializer es determinista e idempotente por construcción — registrar `JsonStringEnumConverter` una vez en el DI no tiene efectos acumulativos en retries.

---

## 8. Impacto en proyecciones / read models

No aplica. El fix no emite ni consume eventos. No toca proyecciones ni read models.

---

## 9. Impacto en endpoints HTTP

No hay endpoints nuevos ni cambios en rutas, DTOs o métodos HTTP. El único cambio observable desde el cliente es:

- Campos enum en request bodies se pueden enviar como strings (p. ej. `"Manual"`) en lugar de requerir integers — que es el comportamiento correcto esperado.
- Campos enum en response bodies se serializan como strings en lugar de integers. Verificado en §5 R-2 que los asserts existentes no se rompen.

---

## 10. Impacto en SignalR / push

No aplica. El fix solo afecta la desearialización del request body antes de llegar al handler. El flujo SignalR (ADR-005) opera sobre los eventos emitidos por el handler — que con este fix sí se ejecuta correctamente.

---

## 11. Impacto en adapters Sinco on-prem

No aplica. El bug ocurre en la capa de binding HTTP, antes de cualquier interacción con el ERP.

---

## 12. Preguntas abiertas

Ninguna. El diagnóstico es concluyente, el fix es mínimo y está respaldado por evidencia del stack trace. El usuario aprobó la Opción A (registro global en `Program.cs`) en la sesión de diagnóstico.

---

## 13. Checklist pre-firma

- [x] Causa raíz documentada con evidencia (stack trace exacto, ruta del error `$.origen`).
- [x] Reconocimiento del comentario previo en `Program.cs:28-30` y justificación de su eliminación.
- [x] Decisión arquitectónica documentada: por qué global vs. por-DTO, por qué no `JsonNamingPolicy` ahora, por qué los DTOs con string+TryParse quedan como deuda separada.
- [x] Scope delimitado: 1 archivo (`Program.cs`), ~5 líneas añadidas, comentario removido.
- [x] Bugs latentes (ActualizarHallazgo, FirmarInspeccion) documentados y su corrección como efecto colateral justificada.
- [x] Tests target identificados con clase, método y falla actual.
- [x] Tests que NO se escriben justificados (2 bugs latentes sin test rojo en este slice).
- [x] Resultado esperado con conteo de tests antes/después (28→30).
- [x] Riesgos documentados (casing, response shapes, compatibilidad DTOs string-based) con verificación o mitigación.
- [x] DoD con criterios verificables y commit message canónico.
- [x] §§ 7-11 resueltos (no aplica / justificado con evidencia).
- [x] §12 sin preguntas abiertas — el usuario aprobó la opción de fix antes de modelar.
