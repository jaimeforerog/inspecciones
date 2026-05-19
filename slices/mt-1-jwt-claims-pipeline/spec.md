# Slice mt-1 — Pipeline de identidad del host PWA + extracción de claims (`IdEmpresa`)

**Autor:** orquestador (asumiendo rol domain-modeler — Agent tool no disponible en runtime; ver §12 nota A)
**Fecha:** 2026-05-19
**Estado:** **Firmado: 2026-05-19** — autor firma: Usuario (Santiago Ramirez)
**Agregado afectado:** ninguno (no hay aggregate event-sourced). El slice opera sobre la capa HTTP: middleware de auth + puerto `ISessionService` + refactor de endpoints. No emite eventos de dominio; tampoco muta documentos Marten (los catálogos por-empresa entran en mt-2).
**Decisiones previas relevantes:**
- `00-investigacion-mercado.md §9.14` — ADR-002 (estado tentativo a cerrar en este slice).
- `06-contrato-apis-erp.md §0.B.5` — contrato de 5 claims del JWT del host (`UsuarioId`, `NomUsuario`, `IdEmpresa`, `IdSucursal`, `IdProyecto`).
- Proyecto referencia `C:\Fuentes\FuentesNET3.0\AzureV4\Attachment` — patrón `MiddlewareAuthorizationToken` + `SessionService` con bypass `("TestUser", "TestCompany")` en env `Test`.
- `FOLLOWUPS.md #14` (claims reales del JWT) y `#52` (capability PRE-1 en `/catalogos/sync`) — ambos se cierran con este slice.
- `FOLLOWUPS.md #44` — propagación del JWT entrante a `MaquinariaErpClient` y sagas: **NO entra en mt-1** (rolla a mt-3 según plan Fase 1).
- Decisiones del usuario 2026-05-19 (Fase 1 cerrada):
  - **D1:** claim canonical `IdEmpresa` tipo `int` (paridad 1:1 con Attachment).
  - **D2:** Marten multi-tenancy `Conjoined` (todos los documentos en una sola tabla discriminada por `tenant_id`).
  - **D3:** ETag de catálogos en envelope (no bump de evento `_v1`).
  - **D4:** reset del schema en dev, backfill en staging — sin migración cross-empresa.
  - **D5:** **todos los catálogos son por-empresa** — sin excepciones single-tenanted (siempre que Marten lo permita).
  - **D6:** agregar NuGets corporativos `SincoSoft.MYE.Common 1.5.1` + `SincoSoft.MYE.Middleware 1.1.6` al proyecto (paridad 1:1 con Attachment, no wrapper OSS).
  - **D7:** cerrar ADR-002 (de "tentativa" a "aceptada") + crear ADR-009 (multi-tenancy Marten conjoined). Va dentro del slice vía rol `doc-writer`.

---

## 1. Intención

Inspecciones Sinco MYE vive como módulo embebido en la PWA Sinco MYE móvil. La identidad — usuario, empresa, sucursal, proyecto — viaja en el JWT que el host firma y propaga en cada request al backend cloud del módulo. Hoy todos los endpoints del slice tienen un `// Claims mock — ADR-002 tentativo` que hardcodea `tecnicoId = "rmartinez"` y construye un `ClaimsTecnico` artificial, lo que (a) deja el dominio funcionalmente cooperante pero impide cualquier tenancy real, (b) deja el endpoint `POST /api/v1/catalogos/sync` sin verificación de capability (FU-52), y (c) bloquea a mt-2 (proyecciones por-empresa) y mt-3 (propagación del JWT a Maquinaria_V4).

Este slice **cablea el pipeline de auth de extremo a extremo** sin tocar dominio: registra el middleware corporativo `MiddlewareAuthorizationToken` que valida el JWT del host, expone un puerto `ISessionService` que devuelve las 5 claims del contrato (`UsuarioId`, `NomUsuario`, `IdEmpresa`, `IdSucursal`, `IdProyecto`), refactoriza los 15 endpoints HTTP existentes para reemplazar el mock por lectura desde `ISessionService`, y deja un bypass `FakeSessionService` para tests E2E controlado por env `Test` (paridad con el patrón de Attachment).

Con mt-1 cerrado, FU-14 y FU-52 se cierran y mt-2 (multi-tenancy Marten conjoined) puede arrancar sobre una base sólida: `IdEmpresa` confiable en cada request.

---

## 2. Comando

Este slice **no es event-sourced**. No hay aggregate ni comando de dominio. El "comando" lógico es la lectura de claims que cada handler HTTP hace al inicio de su pipeline.

Por convención de la metodología (spec §2 para slices no-aggregate, ver erp-4), el "comando" se modela como **el contrato del puerto** que reemplaza el mock:

```
ISessionService
  ├── int IdEmpresa { get; }          // claim "IdEmpresa" — int (D1)
  ├── int IdUsuario { get; }          // claim "UsuarioId" — int (alias del nombre del proyecto Attachment)
  ├── string NomUsuario { get; }      // claim "NomUsuario" — string
  ├── int IdSucursal { get; }         // claim "IdSucursal" — int (puede ser 0 si no aplica)
  ├── int IdProyecto { get; }         // claim "IdProyecto" — int (puede ser 0 si no aplica)
  └── IReadOnlyCollection<string> Capabilities { get; }
                                       // claim "capabilities" — lista de strings (ej. "ejecutar-inspeccion",
                                       // "generar-ot", "administrar-catalogos") propagada por el host.
                                       // Si el JWT del host no incluye capabilities aún, se interpreta
                                       // como conjunto vacío y los endpoints que requieren capability
                                       // devuelven 403 (consistente con el patrón actual).
```

Implementación dual:

- **`SincoMiddlewareSessionService`** (Infrastructure, prod) — lee de `MiddlewareAuthorizationToken.SessionVariables()` del paquete `SincoSoft.MYE.Common` exactamente como hace el `SessionService` del proyecto Attachment.
- **`FakeSessionService`** (Api.Tests, bypass) — devuelve valores fijos (`IdEmpresa=1`, `IdUsuario=1`, `NomUsuario="TestUser"`, `IdSucursal=0`, `IdProyecto=0`, `Capabilities=["ejecutar-inspeccion","generar-ot","administrar-catalogos"]`) cuando `ASPNETCORE_ENVIRONMENT=Test`. Equivalente al `("TestUser", "TestCompany")` del proyecto Attachment, ajustado al contrato de 5 claims del módulo Inspecciones.

El header HTTP que activa la auth real es **`Authorization: Bearer {jwt}`** propagado por el host PWA, idéntico al contrato actual del adapter `MaquinariaErpClient` (mismo JWT, validado por el mismo paquete corporativo en ambos lados de la cadena — paridad con Maquinaria_V4).

---

## 3. Evento(s) emitido(s)

Este slice **no emite eventos de dominio**. No toca aggregates. No persiste estado nuevo.

Tabla de mutaciones (ninguna):

| Documento/stream mutado | Cuándo |
|---|---|
| — | — |

Si en mt-2 o mt-3 se decide capturar `claimsSnapshot` (ej. en eventos para auditoría offline, ADR-008), se hará aditivamente sin tocar `_v1` (envelope, D3 aplicado por analogía).

---

## 4. Precondiciones

Como no hay aggregate, las precondiciones son del middleware/handler HTTP:

- **`PRE-AUTH-1`** — header `Authorization: Bearer {jwt}` presente. Si falta o el formato es inválido, el middleware corporativo (`MiddlewareAuthorizationToken`) rechaza la request con `401 Unauthorized` antes de llegar al endpoint. Excepción: env `Test` con `FakeSessionService` registrado — el bypass no requiere header.
- **`PRE-AUTH-2`** — JWT válido según validación del paquete corporativo (firma, issuer, expiración). Si falla, `401 Unauthorized` con body propio del middleware Sinco.
- **`PRE-AUTH-3`** — claim `IdEmpresa` presente y deserializable a `int`. Si falta o no es entero, el handler que consume `ISessionService.IdEmpresa` lanza `ClaimRequeridaException("IdEmpresa")` → mapeo HTTP `401 Unauthorized` con `codigoError = "CLAIM-IDEMPRESA-AUSENTE"`. (No es `403` porque el problema es el token, no la autorización.)
- **`PRE-AUTH-4`** — claim `UsuarioId` presente y deserializable a `int`. Mismo comportamiento que PRE-AUTH-3 con `codigoError = "CLAIM-USUARIOID-AUSENTE"`.
- **`PRE-CAP-1`** — los endpoints que requieren capability (todos los que hoy usan los headers `X-Sin-Capability-*` en Api.Tests, más `POST /api/v1/catalogos/sync` — FU-52) leen `ISessionService.Capabilities` y validan que la capability esperada esté presente. Si no, `403 Forbidden` con el helper `Forbidden403(codigoError, mensaje)` ya existente (fix FU-38).

> **Capa donde viven:** PRE-AUTH-1/2 en el middleware corporativo (transparente al endpoint). PRE-AUTH-3/4 en `ISessionService` (el getter lanza si la claim falta). PRE-CAP-1 en el endpoint, igual que hoy — solo cambia la fuente de `capabilities` (de header de test a `ISessionService.Capabilities`).

---

## 5. Invariantes tocadas / decisiones de diseño

No aplican invariantes de aggregate (I-H*, I-F*, V-F*). Se documentan las decisiones de diseño del slice:

**D-MT1-1 — `IdEmpresa` es `int` con paridad 1:1 a Attachment.**
El claim se llama `IdEmpresa` (mismo nombre, misma capitalización, mismo tipo `int` que expone `MiddlewareAuthorizationToken.SessionVariables()` en el host). La razón: cualquier desviación rompe la paridad con el resto de los módulos Sinco que ya consumen el mismo JWT. Si el host cambia el contrato, todos los módulos se actualizan en coordinación cross-team — no aquí.

**D-MT1-2 — Bypass de tests vía env `Test`, no vía DI manual en cada test.**
El switch entre `SincoMiddlewareSessionService` y `FakeSessionService` se decide en `Program.cs` por `builder.Environment.IsEnvironment("Test")`. `InspeccionesAppFactory` ya setea `ASPNETCORE_ENVIRONMENT=Test` (verificar — si no, agregarlo). Razón: igual que Attachment, evita que cada test individual tenga que registrar el fake. Los tests que necesiten un `IdEmpresa` o capabilities distintos al default lo hacen vía `HttpRequestMessage.Headers` o reemplazo del servicio en `InspeccionesAppFactory.ConfigureWebHost`.

**D-MT1-3 — Capabilities vienen del JWT (no del header de test).**
Los headers `X-Sin-Capability-Generar-OT` y `X-Sin-Capability-Ejecutar` desaparecen de los endpoints de producción. En tests, el comportamiento equivalente se logra construyendo un `FakeSessionService` con la lista de capabilities deseada por test (o usando el default que las incluye todas y haciendo override puntual). Esto rompe el bypass actual de tests pero alinea producción con el contrato real del JWT.

**D-MT1-4 — `Forbidden403` se mantiene; `Results.Forbid()` sigue prohibido.**
Fix FU-38 se preserva. La razón original — no registramos `AddAuthentication()` porque el módulo no tiene IdP propio — sigue vigente. `MiddlewareAuthorizationToken` valida el JWT por si mismo sin necesidad de `AddAuthentication` (lo hace el proyecto Attachment exactamente igual).

**D-MT1-5 — `ClaimsTecnico` (record del dominio) se conserva pero se construye desde `ISessionService`.**
El record `Inspecciones.Domain.Inspecciones.ClaimsTecnico(TecnicoIniciador, ProyectosAsignados, TieneCapabilityEjecutarInspeccion)` no se elimina. Los handlers `IniciarInspeccionHandler` y `FirmarInspeccionHandler` siguen recibiendo `ClaimsTecnico` por parámetro (regla CLAUDE.md: el dominio nunca lee del contexto HTTP). Lo que cambia es **cómo se construye** en el endpoint: en vez del mock fijo, se construye desde `ISessionService.IdUsuario` + `ISessionService.Capabilities`. Mapeo:
- `TecnicoIniciador` ← `ISessionService.IdUsuario.ToString(CultureInfo.InvariantCulture)` (el dominio lo trata como string opaco, decisión histórica).
- `ProyectosAsignados` ← deriva del request actual (el `ProyectoId` que viene en el body), porque el claim `IdProyecto` del JWT es **el proyecto activo de la sesión**, no la lista completa de proyectos del técnico. La lista completa no existe en el JWT — esto es consistente con la decisión `2026-05-05` (identidad 100% del host, sin sync de usuarios). En la práctica, el handler valida que `request.ProyectoId == session.IdProyecto` cuando ambos están presentes (regla nueva PRE-PROY-1, opcional, ver §12.A).
- `TieneCapabilityEjecutarInspeccion` ← `ISessionService.Capabilities.Contains("ejecutar-inspeccion")`.

**D-MT1-6 — `TecnicoId` opaco como string sigue siendo el contrato del dominio.**
El dominio nunca ve el `IdUsuario` como `int`. Se serializa a string para mantener compatibilidad con los eventos ya emitidos en producción (los 15 slices cerrados usan `string TecnicoId` / `EmitidoPor`). Cambiar el shape de los eventos `_v1` es out-of-scope (D3 aplicado).

**D-MT1-7 — NuGets corporativos en el repo, restore-friendly desde caché local.**
Se agregan `SincoSoft.MYE.Common 1.5.1` y `SincoSoft.MYE.Middleware 1.1.6` a `Directory.Packages.props` + `Inspecciones.Api.csproj`. El `NuGet.Config` del repo se actualiza para conservar los dos feeds corporativos (Sinco + Maquinaria) además de `nuget.org`, con `packageSourceMapping` que dirige `SincoSoft.*` a los feeds corporativos y el resto a `nuget.org`. **Hallazgo verificado pre-firma (ver §12.C):** los paquetes corporativos ya están en la caché global del usuario (`~/.nuget/packages/sincosoft.mye.{common,middleware}/{1.5.1,1.1.6}`) — el restore funciona sin auth a los feeds Azure DevOps siempre que la caché esté caliente. Para CI/contribuidores sin caché, los feeds corporativos requerirán auth (PAT en variable de entorno o credential provider, fuera de scope para mt-1). FU-32 cerrado: el bloqueo NuGet local se levanta con esta config + alineación `Microsoft.Extensions.Logging` a `9.0.3` (requerido por `SincoSoft.MYE.Common`).

**D-MT1-8 — Sin cambios al dominio ni a los eventos.**
mt-1 es 100% capa HTTP + Infrastructure (puerto). Cero cambios a `Inspecciones.Domain.*`. Cero cambios a eventos `*_v1`. Cero re-emisiones. La regla de pureza `Apply` del CLAUDE.md ni se toca.

**D-MT1-9 — Endpoint `/api/v1/catalogos/sync` gana capability check.**
Cierre de FU-52: el handler `SincronizarCatalogosHandler` ya existe; el endpoint agrega validación de `Capabilities.Contains("ejecutar-inspeccion") || Capabilities.Contains("administrar-catalogos")`. Sin la capability → `403`.

**D-MT1-10 — `MaquinariaErpClient` no se toca en mt-1.**
El cliente seguirá usando `MaquinariaErpOptions.JwtToken` (token fijo de config). FU-44 (propagación del JWT entrante al cliente HTTP) rolla a **mt-3**, junto con la decisión de qué hacer para sagas que corren fuera de scope HTTP (token de servicio dedicado vs. otra estrategia). mt-1 no degrada la integración ERP — sigue funcionando como hoy.

---

## 6. Escenarios Given / When / Then

> Nota: como mt-1 no tiene aggregate event-sourced, los escenarios son HTTP end-to-end sobre `WebApplicationFactory<Program>`. El "rebuild desde stream" no aplica (§6.X omitido — el slice no emite eventos).

### 6.1 Happy path — endpoint existente sigue funcionando con `FakeSessionService` (sin JWT en env Test)

**Given**
- `ASPNETCORE_ENVIRONMENT=Test` (default de `InspeccionesAppFactory`).
- `FakeSessionService` registrado en DI con valores default (`IdEmpresa=1`, `IdUsuario=1`, `Capabilities=["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]`).
- Setup canónico de slice 1b (equipo `100020` activo en proyecto `42`, rutina sembrada).

**When**
- `POST /api/v1/inspecciones` con header `X-Client-Command-Id: {guid}` y body válido. **Sin** header `Authorization`.

**Then**
- Status `201 Created`.
- `InspeccionIniciada_v1` emitida con `TecnicoIniciador = "1"` (string del `IdUsuario` default del fake).
- Los 56 tests existentes de `Api.Tests` (excluyendo los 6 ADR-008 ya skip) siguen verde sin modificar su setup explícito de claims.

### 6.2 Sin JWT en producción (env `Development` o `Production`) → 401

**Given**
- `ASPNETCORE_ENVIRONMENT=Development` (forzado en este test específico via `InspeccionesAppFactory.WithEnvironment("Development")`).
- `SincoMiddlewareSessionService` registrado.

**When**
- `POST /api/v1/inspecciones` con header `X-Client-Command-Id` pero **sin** `Authorization: Bearer ...`.

**Then**
- Status `401 Unauthorized`.
- Body del middleware corporativo (no se valida shape exacto — depende del paquete `SincoSoft.MYE.Common`).
- Ningún evento emitido (verificado consultando el stream del `InspeccionId` propuesto).

### 6.3 JWT con `IdEmpresa` ausente → 401 con código específico

**Given**
- `ASPNETCORE_ENVIRONMENT=Development`.
- `SincoMiddlewareSessionService` registrado.
- JWT generado en test con todas las claims excepto `IdEmpresa` (firmado con clave de test).

**When**
- `POST /api/v1/inspecciones` con `Authorization: Bearer {jwt-sin-idempresa}` + `X-Client-Command-Id`.

**Then**
- Status `401 Unauthorized`.
- Body `{ codigoError: "CLAIM-IDEMPRESA-AUSENTE", mensaje: "..." }`.

> **Nota técnica:** el JWT del host se firma con una clave que solo el host conoce. Para este test, se inyecta un `FakeSessionService` que **lanza** `ClaimRequeridaException("IdEmpresa")` cuando se accede a `IdEmpresa`. El test verifica el comportamiento del **handler de excepción** (middleware de error en `Program.cs` que mapea `ClaimRequeridaException` → 401), no el comportamiento del paquete corporativo. Validación real del JWT se cubre con un test de integración out-of-band en mt-2 cuando ya hay datos por empresa.

### 6.4 Sin capability `ejecutar-inspeccion` → 403

**Given**
- Env `Test`. `FakeSessionService` registrado con `Capabilities = ["generar-ot"]` (sin `ejecutar-inspeccion`).
- Setup canónico de slice 1b.

**When**
- `POST /api/v1/inspecciones` con `X-Client-Command-Id` y body válido.

**Then**
- Status `403 Forbidden`.
- Body `{ codigoError: "PRE-1", mensaje: "Capability 'ejecutar-inspeccion' requerida." }` (igual al patrón actual fix-FU-38).

### 6.5 `POST /api/v1/catalogos/sync` sin capability → 403 (cierre FU-52)

**Given**
- Env `Test`. `FakeSessionService` con `Capabilities = []` (lista vacía).

**When**
- `POST /api/v1/catalogos/sync` con `X-Client-Command-Id` y body vacío (el endpoint no requiere body).

**Then**
- Status `403 Forbidden`.
- Body `{ codigoError: "PRE-1", mensaje: "Capability 'ejecutar-inspeccion' o 'administrar-catalogos' requerida." }`.
- Cero llamadas a `MaquinariaErpClient` (verificable con un `FakeCatalogoSyncRepository` que registra calls).

### 6.6 `POST /api/v1/catalogos/sync` con capability → 200 (existing behavior preserved)

**Given**
- Env `Test`. `FakeSessionService` con `Capabilities = ["administrar-catalogos"]`.

**When**
- `POST /api/v1/catalogos/sync` con `X-Client-Command-Id`.

**Then**
- Status `200 OK` (idéntico al comportamiento actual de erp-4).
- Los 23 tests existentes de erp-4 (`SincronizarCatalogosEndpointTests`) siguen verde.

### 6.7 `Authorization` con JWT válido → claims pasan al handler (regression test futuro mt-2)

**Given**
- Env `Development` con `SincoMiddlewareSessionService` real.
- JWT válido con `IdEmpresa=7`, `UsuarioId=42`, `NomUsuario="rmartinez"`, capabilities ok.

**When**
- `POST /api/v1/inspecciones` con `Authorization: Bearer {jwt}` + body.

**Then**
- Status `201 Created`.
- `InspeccionIniciada_v1` emitida con `TecnicoIniciador = "42"`.
- (En mt-2, este test se extenderá para validar que el documento Marten quedó marcado con `tenant_id = 7`.)

> **Nota:** este escenario requiere que el paquete `SincoSoft.MYE.Middleware` se pueda configurar con una clave de prueba (o que se mockee `MiddlewareAuthorizationToken.SessionVariables()`). Si el paquete corporativo no expone una forma testeable de inyectar claims, el escenario se reduce a §6.3 + §6.4 y se difiere a mt-2. Ver §12.B.

### 6.8 Rebuild desde stream — no aplica

Este slice no emite eventos. §6.X omitido por convención (template §6.X solo aplica si hay ≥1 evento emitido).

---

## 7. Idempotencia / retries

- **Autenticación es naturalmente idempotente** — validar un JWT N veces produce el mismo resultado (asumiendo que el JWT no expira entre intentos).
- **No hay POSTs nuevos al ERP** — el slice solo refactoriza el código de read claims. FU-44 (propagación del JWT entrante a `MaquinariaErpClient`) rolla a mt-3.
- **Cola offline cliente (ADR-008)** — el cliente PWA puede reintentar comandos con el mismo `X-Client-Command-Id`. El comportamiento de mt-1 es transparente para esa cola: si el primer intento falló con `401` por JWT expirado, el cliente refresca el token (política propia del cliente) y reintenta — el módulo lo trata como cualquier request nueva.

---

## 8. Impacto en proyecciones / read models

- **Cero impacto en proyecciones existentes** (`InspeccionAbiertaPorEquipoView`, `CatalogoSyncState`, etc.). No se introduce `tenant_id` en este slice (eso es mt-2).
- **Cero migraciones de schema.** Marten sigue con `inspecciones` schema en single-tenant mode.

---

## 9. Impacto en endpoints HTTP

### 9.1 Endpoints existentes refactorizados (15)

Todos los endpoints de `InspeccionesEndpoints.cs` (14) + `CatalogosEndpoints.cs` (`POST /api/v1/catalogos/sync`) cambian:

**Antes (patrón actual, ej. `POST /api/v1/inspecciones`):**
```
HttpContext ctx, ... =>
{
    // Claims mock — ADR-002 tentativo.
    var claims = new ClaimsTecnico(
        TecnicoIniciador: "rmartinez",
        ProyectosAsignados: new HashSet<int> { request.ProyectoId },
        TieneCapabilityEjecutarInspeccion: true);
    ...
}
```

**Después (mt-1):**
```
ISessionService session, ... =>
{
    if (!session.Capabilities.Contains("ejecutar-inspeccion"))
        return Forbidden403("PRE-1", "Capability 'ejecutar-inspeccion' requerida.");

    var claims = new ClaimsTecnico(
        TecnicoIniciador: session.IdUsuario.ToString(CultureInfo.InvariantCulture),
        ProyectosAsignados: new HashSet<int> { request.ProyectoId },
        TieneCapabilityEjecutarInspeccion: true);
    ...
}
```

Headers eliminados de los endpoints (eran simulación de tests, no parte del contrato real):
- `X-Sin-Capability-Generar-OT`
- `X-Sin-Capability-Ejecutar`
- `X-Tecnico-Id`

Los tests que usaban estos headers se actualizan para construir un `FakeSessionService` con las capabilities deseadas.

### 9.2 Endpoint `POST /api/v1/catalogos/sync` gana capability check (cierre FU-52)

Antes: público. Después: requiere `ejecutar-inspeccion` o `administrar-catalogos`.

### 9.3 Códigos HTTP nuevos

- `401 Unauthorized` — JWT ausente / inválido (middleware corporativo) o claim crítica ausente (`IdEmpresa`, `UsuarioId`).
- `403 Forbidden` — JWT válido pero sin capability (sigue usando `Forbidden403` helper).
- Resto inalterado.

### 9.4 OpenAPI

- Agregar `Authorization: Bearer {jwt}` como header obligatorio en todas las rutas (excepto `/health/live`, `/health/ready` y `/` informativo).
- Documentar respuestas 401 y 403 en cada endpoint.

### 9.5 Rol/permiso requerido

| Endpoint | Capability requerida |
|---|---|
| `POST /api/v1/inspecciones` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/monitoreo` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/{id}/hallazgos` | `ejecutar-inspeccion` |
| `PUT/DELETE /api/v1/inspecciones/{id}/hallazgos/{hId}` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/{id}/hallazgos/{hId}/repuestos` | `ejecutar-inspeccion` |
| `PATCH /api/v1/inspecciones/{id}/hallazgos/{hId}/repuestos/{rId}` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/{id}/firmar` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/{id}/items/{iId}/{medicion\|evaluacion\|omitir}` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/{id}/{generar-ot\|rechazar-generar-ot}` | `generar-ot` |
| `POST /api/v1/inspecciones/{id}/cancelar` | `ejecutar-inspeccion` |
| `POST /api/v1/inspecciones/{id}/novedades-preop/{nId}/descartar` | `ejecutar-inspeccion` |
| `POST /api/v1/catalogos/sync` | `ejecutar-inspeccion` o `administrar-catalogos` |

---

## 10. Impacto en SignalR / push (si aplica)

- **Cero impacto** en SignalR. mt-1 no toca el hub.
- **Diferido a mt-2:** la autenticación del hub via JWT (paso 3.52 del roadmap) usa el mismo `ISessionService` pattern una vez establecido — followup #14b si emerge.

---

## 11. Impacto en adapters Sinco on-prem (si aplica)

- **`MaquinariaErpClient` no se toca en mt-1.** Sigue usando `MaquinariaErpOptions.JwtToken` (token fijo de config) para sus 8 endpoints. Los 4 slices erp-1..erp-4 cerrados (commit `4c2ef4e`..`fb44741`) siguen funcionando idéntico.
- **FU-44** (propagación del JWT entrante al cliente HTTP) rolla explícitamente a **mt-3**.
- Estado de disponibilidad: 🟢 disponible (no aplica al slice — sin cambios al adapter).

---

## 12. Preguntas abiertas

Preguntas que el modelador no resolvió y requieren confirmación del usuario antes de pasar a `red`:

### A. ¿Validamos `request.ProyectoId == session.IdProyecto` en `IniciarInspeccion`?

El JWT del host trae `IdProyecto` (proyecto activo de la sesión). El body de `IniciarInspeccion` también trae `ProyectoId`. Hoy el dominio valida `ProyectoNoAutorizado` contra `claims.ProyectosAsignados` que en el mock se hardcodea con `{request.ProyectoId}` — efectivamente always-allow.

**Opciones:**
- **A.1 — Pasar `session.IdProyecto` como único miembro de `ProyectosAsignados`** (sin cambiar dominio). Si `request.ProyectoId != session.IdProyecto`, el dominio lanza `ProyectoNoAutorizadoException` → `403`. Estricto pero alineado con "una sesión = un proyecto activo".
- **A.2 — Pasar `request.ProyectoId` siempre (preservar mock).** Mantiene comportamiento actual. El JWT del host queda como dato informativo no enforced en mt-1.
- **A.3 — Diferir a mt-2** y dejar mt-1 con A.2 (always-allow), documentando explícitamente que la validación cross-proyecto vendrá con el switch a `Conjoined` en mt-2.

**Recomendación del modelador:** **A.3.** mt-1 ya tiene mucha superficie (15 endpoints refactorizados + middleware + ADRs). Agregar enforcement cross-proyecto en este slice expande el blast radius de regresión. mt-2 es el lugar natural cuando `IdEmpresa` ya está enforced.

**Decisión firmada 2026-05-19 — A.3 aceptada.** El enforcement `request.ProyectoId == session.IdProyecto` se difiere a mt-2. mt-1 preserva el comportamiento mock actual (always-allow): `ClaimsTecnico.ProyectosAsignados` se construye con `{ request.ProyectoId }` (mismo shape que el mock vigente), de modo que el dominio nunca lanza `ProyectoNoAutorizadoException` por discrepancia con la claim. La regla nueva PRE-PROY-1 **no se introduce en este slice** — queda en backlog implícito para mt-2.

### B. ¿`MiddlewareAuthorizationToken` del paquete corporativo es testeable o requiere mock?

El paquete `SincoSoft.MYE.Common 1.5.1` expone `MiddlewareAuthorizationToken.SessionVariables()` como **método estático**. Esto es difícil de testear sin (a) inyectar claims via `HttpContext.Items` en un middleware de test, o (b) abstraer el acceso detrás del puerto `ISessionService` (que es lo que ya proponemos en §2).

**Si la respuesta es "abstraer detrás del puerto" (recomendado):** §6.7 funciona porque el puerto `ISessionService` es el que se mockea en tests, no `MiddlewareAuthorizationToken`. La implementación real `SincoMiddlewareSessionService` solo encapsula la llamada estática.

**Riesgo:** si el paquete tiene side-effects al instanciarse (config, logging, etc.), arrancar `Program.cs` en `Test` sin el host real puede fallar. Mitigación: en env `Test`, **no** registrar el middleware corporativo en el pipeline ASP.NET — solo registrar `FakeSessionService`. Es lo que hace el proyecto Attachment.

**Recomendación del modelador:** asumir abstracción vía puerto + skip del middleware en env `Test`. Si emerge un side-effect bloqueante al integrar, abrir followup.

**Decisión firmada 2026-05-19 — bypass vía `ISessionService` aceptado.** En env `Test`, `Program.cs` registra `FakeSessionService` y **omite** el registro del `MiddlewareAuthorizationToken` del paquete corporativo en el pipeline ASP.NET (paridad con Attachment). Tests E2E nunca instancian ni mockean `MiddlewareAuthorizationToken` directamente — el bypass del puerto es suficiente. El escenario §6.7 (JWT real) se reduce a un test de comportamiento del `FakeSessionService` con valores no-default; la validación real del JWT (firma/issuer/exp) se cubre en mt-2 con un test integración out-of-band, o se difiere a piloto.

### C. Bloqueo NuGet local — ¿el slice sigue avanzable sin auth a feeds Azure DevOps?

**Verificación hecha durante la spec (2026-05-19):**
- Los paquetes `sincosoft.mye.common/1.5.1` y `sincosoft.mye.middleware/1.1.6` **ya están en la caché global** del usuario (`%USERPROFILE%\.nuget\packages\`).
- `dotnet restore src/Inspecciones.Api/Inspecciones.Api.csproj -p:NuGetAudit=false --source https://api.nuget.org/v3/index.json --source "$env:USERPROFILE\.nuget\packages"` resuelve correctamente.
- `dotnet build src/Inspecciones.Api/Inspecciones.Api.csproj --no-restore` da verde con 0 warnings, 0 errores (con `Microsoft.Extensions.Logging` bumped a 9.0.3 — requerido por `SincoSoft.MYE.Common`).
- Sin el override `--source`, el restore intenta validar contra los feeds Azure DevOps y falla con `NU1301` (401 unauthorized) porque no hay credentials provider configurado para `pkgs.dev.azure.com/sincosoftsas/*`.

**Implicación:**
- **Localmente** (caché caliente): el slice avanza. `red` y `green` pueden iterar.
- **CI**: requerirá que el pipeline GitHub Actions tenga credentials para los feeds Azure DevOps (PAT en secret) o restaure desde una caché propia. **Followup nuevo (FU-53) abierto en review:** documentar cómo configurar el credential provider en CI antes del primer merge a `main`.
- **Contribuyentes nuevos**: necesitarán acceso a la caché del usuario o credentials para los feeds. No es bloqueante para mt-1 (el usuario y el orquestador trabajan con la caché caliente).

**Recomendación del modelador:** avanzar con la config actual (`NuGet.Config` del repo incluye los feeds corporativos pero el restore funciona offline si la caché está caliente). FU-53 cubre CI.

**Decisión firmada 2026-05-19 — restore con caché caliente aceptado para local; FU-53 abierto para CI.** El slice avanza con la config actual del `NuGet.Config`. Para iterar localmente, `red` y `green` usan `dotnet restore --source https://api.nuget.org/v3/index.json --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false` (o equivalente — la caché global ya contiene los dos paquetes Sinco). FU-53 abierto en `FOLLOWUPS.md` para resolver auth de feeds Azure DevOps en CI antes del primer merge a `main`.

### D. ¿`Capabilities` viene del JWT o se sigue infiriendo del rol del usuario?

El contrato `06-contrato-apis-erp.md §0.B.5` no lista `capabilities` como una claim entregada por el host hoy — solo lista `UsuarioId, NomUsuario, IdEmpresa, IdSucursal, IdProyecto`. Pero el roadmap §2.5 dice "el host PWA mapea su catálogo de perfiles ERP a capabilities".

**Opciones:**
- **D.1 — Asumir que el JWT trae `capabilities` (array de strings) y el host PWA es responsable de incluirlo.** Cross-team con el equipo del host (David/Sergio).
- **D.2 — En mt-1, hacer que `ISessionService.Capabilities` devuelva siempre `["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]`** (always-allow) y dejar el wire real para un slice futuro.

**Recomendación del modelador:** **D.2 con followup FU-54.** El JWT real probablemente aún no tenga `capabilities` (Sergio/David no lo han confirmado). En mt-1 hacemos que `SincoMiddlewareSessionService.Capabilities` lea de la claim `capabilities` si existe, y si no, devuelva el conjunto completo (compat). Cuando el host empiece a emitir la claim, el comportamiento cambia automáticamente. `FakeSessionService` siempre devuelve el set completo por default.

**Decisión firmada 2026-05-19 — D.2 aceptada (always-allow en mt-1).** `SincoMiddlewareSessionService.Capabilities` devuelve `["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]` cuando el JWT no expone la claim `capabilities`. Si en el futuro el host la emite (array de strings), el lector la prefiere sobre el default. `FakeSessionService` mantiene el set completo por default; tests específicos que necesiten denegar capability (§6.4, §6.5) construyen un `FakeSessionService` con `Capabilities = []` o un subconjunto via constructor override. FU-54 abierto en `FOLLOWUPS.md` para confirmar cross-team con Sergio/David si el JWT del host emite la claim, y para apretar el default a `[]` cuando el contrato esté confirmado.

---

## 13. Checklist pre-firma

- [x] Todas las precondiciones (PRE-AUTH-1..4, PRE-CAP-1) mapean a un escenario Then (§6.2..§6.5).
- [x] No hay invariantes de aggregate (slice no event-sourced). Las decisiones de diseño (D-MT1-1..D-MT1-10) están explícitas en §5.
- [x] El happy path está presente (§6.1).
- [x] Rebuild desde stream **no aplica** (slice no emite eventos — §6.8 lo documenta explícitamente).
- [x] Preguntas abiertas (§12.A..§12.D) están todas formuladas con recomendación del modelador para que el usuario confirme o desvíe.
- [x] Slice no toca endpoints Sinco on-prem nuevos (§11). FU-44 explícitamente rolleado a mt-3.
- [x] DoD de infra plumbing identificado:
  - [x] NuGets corporativos agregados a `Directory.Packages.props` + `Inspecciones.Api.csproj` (verificado, build verde).
  - [x] `NuGet.Config` del repo actualizado (verificado, restore funciona con caché caliente).
  - [x] `Microsoft.Extensions.Logging` bumped a 9.0.3 (alineación con dep transitive de `SincoSoft.MYE.Common`).
  - [ ] Puerto `ISessionService` declarado en `Inspecciones.Infrastructure/Auth/ISessionService.cs` (pendiente — fase green).
  - [ ] `SincoMiddlewareSessionService` real implementado (pendiente — fase green).
  - [ ] `FakeSessionService` en `tests/Inspecciones.Api.Tests/Auth/FakeSessionService.cs` (pendiente — fase green).
  - [ ] 15 endpoints refactorizados (pendiente — fase green).
  - [ ] ADR-002 reescrito en `00-investigacion-mercado.md §9.14` (pendiente — fase doc-writer post-aprobación).
  - [ ] ADR-009 (multi-tenancy Marten conjoined) creado en `00-investigacion-mercado.md §9.17` (pendiente — fase doc-writer post-aprobación; nota: ADR-009 se redacta acá pero su enforcement es mt-2).
  - [ ] FU-14, FU-52 marcados ✅ cerrados, FU-53 (CI credentials), FU-54 (claim `capabilities` cross-team) abiertos (pendiente — post-review).

---

## Nota final del modelador

mt-1 es deliberadamente plumbing-heavy y dominio-zero. Esa es la forma correcta de desbloquear mt-2 (proyecciones por-empresa) y mt-3 (propagación a ERP) sin meter regresión al dominio que ya tiene 246 tests verde + 94.44% cobertura en el aggregate. Si el usuario quiere acelerar y meter D5 (catálogos `Conjoined`) en este slice, lo aviso — pero la recomendación firme es no hacerlo: mt-1 = pipeline de claims; mt-2 = `Conjoined` + migración de datos; mt-3 = JWT propagation al ERP. Cada slice cierra un commit limpio.

Status: **firmado 2026-05-19** — usuario autorizó los defaults del modelador en §12.A (A.3), §12.B (bypass `ISessionService`), §12.C (caché caliente local + FU-53 para CI) y §12.D (D.2 + FU-54). Próxima fase: `red` (tests rojos en `tests/Inspecciones.Api.Tests/Auth/`).
