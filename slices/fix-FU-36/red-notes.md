# Red notes — fix-FU-36 — JsonStringEnumConverter ausente en Minimal APIs

**Autor:** red
**Fecha:** 2026-05-11
**Spec consumida:** `slices/fix-FU-36/spec.md`

---

## 1. Tests escritos

No se escribieron tests nuevos. Los 2 tests preexistentes son los que constituyen el estado rojo de este fix.

| Test | Escenario spec | Archivo |
|---|---|---|
| `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created` | §2 — Test 1 Happy path E2E | `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs` |
| `POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008` | §2 — Test 2 Idempotencia ADR-008 | `tests/Inspecciones.Api.Tests/RegistrarHallazgoEndpointTests.cs` |

---

## 2. Verificación de estado rojo

Comando ejecutado:

```
POSTGRES_TEST_CONNSTRING="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=inspecciones_test" \
dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj \
  --filter "FullyQualifiedName~RegistrarHallazgo" \
  --logger "console;verbosity=minimal"
```

Resultado: **Con error: 2, Superado: 0, Omitido: 0, Total: 2, Duración: 6 s**

### Fallo Test 1 — `POST_inspecciones_id_hallazgos_happy_path_responde_201_Created`

```
Mensaje de error:
 Expected response.StatusCode to be HttpStatusCode.Created {value: 201},
 but found HttpStatusCode.BadRequest {value: 400}.

Stack:
 at Inspecciones.Api.Tests.RegistrarHallazgoEndpointTests
   .POST_inspecciones_id_hallazgos_happy_path_responde_201_Created()
   in RegistrarHallazgoEndpointTests.cs:line 108
```

**Causa raíz:** `RequestDelegateFactory` falla al deserializar el campo `origen: "Manual"` (string) porque `System.Text.Json` por defecto espera un entero para el enum `OrigenHallazgo`. La excepción interna es:

```
Microsoft.AspNetCore.Http.BadHttpRequestException: Failed to read parameter
"RegistrarHallazgoRequest request" from the request body as JSON.
 ---> System.Text.Json.JsonException: The JSON value could not be converted to
      Inspecciones.Api.Inspecciones.RegistrarHallazgoRequest.
      Path: $.origen | LineNumber: 0 | BytePositionInLine: 70.
```

El endpoint retorna 400 antes de invocar el handler. El handler de dominio nunca se ejecuta.

### Fallo Test 2 — `POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008`

```
Mensaje de error:
 Expected primeraRespuesta.StatusCode to be HttpStatusCode.Created {value: 201},
 but found HttpStatusCode.BadRequest {value: 400}.

Stack:
 at Inspecciones.Api.Tests.RegistrarHallazgoEndpointTests
   .POST_inspecciones_id_hallazgos_replay_con_mismo_ClientCommandId_no_duplica_evento_ADR_008()
   in RegistrarHallazgoEndpointTests.cs:line 142
```

**Causa raíz:** idéntica al Test 1. El primer POST del escenario de replay ya falla con 400 en `primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created)` (línea 142). La lógica de dedup ADR-008 nunca se alcanza porque el body JSON no puede deserializarse.

---

## 3. Código de producción tocado

- [x] Sin cambios en `src/` — el rojo documentado es preexistente y no requiere stubs adicionales para compilar.

Los tests compilan correctamente. El estado rojo es por fallo en ejecución (HTTP 400 en lugar de 201), no por error de compilación.

---

## 4. Desviaciones respecto a la spec

- [x] Sin desviaciones.

Los fallos observados coinciden exactamente con lo descrito en el spec §0 (causa raíz) y §2 (falla actual de cada test). El mensaje de error capturado en ejecución confirma la ruta del error `$.origen` mencionada en el spec.

---

## 5. Hand-off a green

- Spec firmada: sí.
- Todos los tests rojos: sí — 2/2 fallan por la razón correcta (HTTP 400 por `JsonException` en desearialización de enum).
- Sin cambios de comportamiento accidentales: sí — no se modificó ningún archivo de `src/` ni `tests/`.
- Acción requerida en green: añadir `builder.Services.ConfigureHttpJsonOptions(...)` con `JsonStringEnumConverter` en `src/Inspecciones.Api/Program.cs` y eliminar el comentario de las líneas 28-30.
