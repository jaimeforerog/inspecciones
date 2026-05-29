# Metodología de trabajo — Inspecciones Sinco MYE

**Estado:** vigente desde 2026-04-29 · última revisión 2026-05-29
**Alcance:** aplica a todo código de producción del módulo de Inspecciones Técnicas y fases siguientes.
**Tipo de metodología:** TDD estricto (Given/When/Then sobre eventos) + squad multiagente especializado de 5 roles.
**Origen:** lift-and-shift desde el proyecto hermano `sinco presupuesto` (52 slices probados). Ver `Inspecciones/docs/desarrollo/00-origen-metodologia.md`.

---

## 1. Principios rectores

1. **Ni una línea de producción sin un test rojo que la justifique.** Si el código existe sin test previo que lo fuerce, se borra o se marca como deuda técnica.
2. **Los tests hablan de comportamiento, no de estado interno.** En un dominio event-sourced, el comportamiento observable es el conjunto de eventos emitidos (o la excepción de invariante). Los tests se escriben en esa misma forma.
3. **Un test rojo, un test verde, un refactor.** No se escribe un segundo test sin haber refactorizado el código que pasó el anterior. El refactor puede ser "ninguno" (y así se registra), pero la decisión es consciente.
4. **Los handoffs entre agentes son con artefactos, no con contexto implícito.** Cada agente produce un archivo concreto que el siguiente consume. Nada de "seguí trabajando sobre lo que dejé hablado".
5. **La unidad de trabajo es un comando del dominio.** Un commit = un comando con su spec, sus tests, su implementación y su review firmada. Las sagas son una variante (ver §7).

---

## 2. Ciclo TDD aplicado a Event Sourcing

### 2.1 Forma canónica Given / When / Then

```csharp
[Fact]
public void RegistrarHallazgo_en_inspeccion_iniciada_emite_HallazgoRegistrado()
{
    // GIVEN: historial de eventos previos
    var dados = new object[]
    {
        new InspeccionIniciada(/*...*/),
    };

    // WHEN: un comando
    var cmd = new RegistrarHallazgo(/*...*/);
    var resultado = CasoDeUso.Decidir(dados, cmd, ahora);

    // THEN: eventos esperados (o excepción de invariante)
    resultado.Should().ContainSingle().Which.Should()
        .BeOfType<HallazgoRegistrado>()
        .Which.RequiereOT.Should().BeTrue();
}
```

El `CasoDeUso.Decidir(historial, comando, timeProvider)` es una función pura derivada del agregado: hace `fold` de los eventos para reconstruir el estado, ejecuta el comando, y devuelve los eventos nuevos. No toca Marten ni base de datos — esos tests viven en el nivel de integración.

Para escenarios de violación de precondición o invariante:

```csharp
[Fact]
public void RegistrarHallazgo_en_inspeccion_firmada_lanza_EstadoInvalido()
{
    var dados = new object[] { /* InspeccionIniciada + InspeccionFirmada ... */ };
    var cmd = new RegistrarHallazgo(/*...*/);

    var act = () => CasoDeUso.Decidir(dados, cmd, ahora);

    act.Should().Throw<EstadoInspeccionInvalidoException>().WithMessage("*firmada*");
}
```

### 2.2 Criterios de paso entre fases

| Transición | Criterio | Cómo verificarlo |
|---|---|---|
| Spec → Red | `spec.md` firmado explícitamente por el usuario | El usuario dice "listo", "firmo" o equivalente — sin esto no se avanza |
| Red → Green | Tests compilan y **fallan por la razón correcta** | `dotnet build && dotnet test --filter "FullyQualifiedName~{Slice}"`; los tests rojos no fallan por compilación ni timeout |
| Green → Refactor | **Todos** los tests del repo pasan | `dotnet test`; cero rojos, cero warnings |
| Refactor → Review | Tests en verde, warnings en cero, `refactor-notes.md` presente | `dotnet test` + `dotnet build` + verificación del archivo |
| Review → Cierre | Veredicto `approved` o `approved-with-followups` en `review-notes.md §4` | Lectura del archivo producido por el reviewer |

Se pasa a la siguiente fase solo cuando se cumple el criterio. Nunca se solapan.

### 2.3 Rebuild test (obligatorio por slice con eventos)

Todo slice que emita ≥1 evento incluye un test que reproyecta el stream sobre un agregado vacío y verifica que el estado resultante es coherente. Detecta validaciones intrusas en `Apply` y eventos emitidos fuera de orden causal:

```csharp
[Fact]
public void {Accion}_rebuild_desde_stream_reproduce_estado()
{
    var dados = new object[] { /* historial previo */ };
    var cmd = new {NombreComando}(/*...*/);
    var emitidos = CasoDeUso.Decidir(dados, cmd, ahora);

    var stream = dados.Concat(emitidos).ToArray();
    var act = () => {Agregado}.Reconstruir(stream);

    var agregado = act.Should().NotThrow().Subject;
    agregado.{propiedadClave}.Should().Be({esperado});
}
```

Este test es **blocker** en la fase review si falta (regla del reviewer §5.3).

### 2.3 Mocks y dobles de prueba

- **Agregados**: cero mocks. Siempre Given/When/Then sobre eventos reales.
- **Handlers**: cero mocks de `IDocumentSession`. Se prueban con **Marten embebido** (Testcontainers Postgres) en nivel de integración, no unitario.
- **Endpoints HTTP**: `WebApplicationFactory<Program>` con BD efímera.
- **Servicios externos** (APIs Sinco on-prem, blob storage, SignalR, email): interfaz en Domain, mock solo en test del handler que los consume.
- **TimeProvider** y **GPS**: inyectados; el dominio nunca llama a `DateTime.UtcNow` ni a la API del navegador.

---

## 3. Squad de agentes (5 roles + orquestador)

Cada rol tiene un **prompt persona** estable en `templates/agent-personas/`, consume artefactos específicos y produce artefactos específicos. El **orquestador** es la conversación principal con el usuario (no se invoca vía Agent tool) — su contrato vive en `templates/agent-personas/orchestrator.md` y define cómo identifica el siguiente comando, valida criterios de paso entre fases, invoca a cada sub-persona, y maneja los veredictos del reviewer. Los cinco roles especializados (`domain-modeler`, `red`, `green`, `refactorer`, `reviewer`) sí se invocan vía Agent.

### 3.1 `domain-modeler`

**Consume:** pregunta del usuario o decisión previa (event storming, §15 del modelo de dominio, ADRs).
**Produce:** `slices/{N}-{comando}/spec.md` siguiendo la plantilla `templates/slice-spec.md`.
**Regla:** nunca escribe código ni tests. Si la spec requiere descubrir más del negocio, lo nota explícitamente.

### 3.2 `red`

**Consume:** `slices/{N}-{comando}/spec.md`.
**Produce:** archivos de test nuevos/modificados bajo `tests/`, más `slices/{N}-{comando}/red-notes.md`.
**Regla:** todos los tests deben compilar y fallar con mensaje claro. Si un test falla por "no compila" se considera no-rojo y se corrige.

### 3.3 `green`

**Consume:** tests rojos.
**Produce:** cambios en `src/` mínimos para pasar el último test rojo, sin tocar otros.
**Regla:** prohibido refactorizar. Prohibido agregar código que ningún test ejerza. Si el impulso aparece, se anota en `green-notes.md` como candidato para refactor.

### 3.4 `refactorer`

**Consume:** código que pasa todos los tests, notas de green.
**Produce:** diff de refactor + `refactor-notes.md` (qué cambió y por qué).
**Regla:** los tests no se tocan (salvo renombrar). Cero cambios de comportamiento.

### 3.5 `reviewer`

**Consume:** todo el slice (spec + tests + impl + notas).
**Produce:** `review-notes.md` con uno de tres veredictos: **approved**, **approved-with-followups**, **request-changes**.
**Regla:** si `request-changes`, vuelve al rol correspondiente (red, green o refactorer) con los puntos específicos.

### 3.6 Orquestador (conversación principal con el usuario)

**Persona en `templates/agent-personas/orchestrator.md`.** Coordina las cinco fases del ciclo TDD, identifica el siguiente comando del catálogo, valida criterios de paso, invoca sub-personas vía Agent y maneja veredictos del reviewer. No se invoca vía Agent tool — es el rol del modelo principal.

**Roles que asume directamente** (no son sub-personas):

- **infra-wire**: registrar handler en Wolverine, proyección en Marten, endpoint HTTP, DTOs, hub SignalR si aplica. Se ejecuta **después** de que el slice pasó review.
- **azure-ops**: bicep/Terraform, pipelines, observabilidad, Azure landing zone. Cadencia por hito, no por slice.
- **doc-writer**: ADR o actualización del README cuando hay una decisión arquitectónica o cambio de contrato público.

---

## 4. Workflow por comando (ejemplo completo)

Supongamos que toca implementar `RegistrarHallazgo`:

```
1. Usuario:     "Sigamos con RegistrarHallazgo."
2. Orquestador: invoca domain-modeler con §15 del modelo y los hallazgos
                de la sesión con el consultor mecánico como input.
                → produce slices/03-registrar-hallazgo/spec.md
                → usuario firma.
3. Orquestador: invoca red con la spec.
                → produce tests/.../RegistrarHallazgoTests.cs (rojo)
                → verifica que compilan y fallan.
4. Orquestador: invoca green con los tests.
                → produce cambios en src/
                → verifica que todos los tests pasan.
5. Orquestador: invoca refactorer.
                → produce diff limpio + notas.
                → verifica que los tests siguen pasando.
6. Orquestador: invoca reviewer.
                → si approved → commit.
                → si request-changes → vuelve a (3) o (4) según aplique.
7. Orquestador: como infra-wire, registra el handler, expone el endpoint,
                actualiza OpenAPI, escribe test de integración HTTP→PG.
8. Orquestador: presenta el slice cerrado al usuario.
```

Estructura de carpeta del slice:

```
slices/
  03-registrar-hallazgo/
    spec.md
    red-notes.md
    green-notes.md
    refactor-notes.md
    review-notes.md
```

Los slices son archivos vivos dentro del repo y se preservan como trazabilidad. Se borran solo si un slice se abandona.

---

## 5. Prompts persona de los agentes

Los prompts completos viven en `templates/agent-personas/`. Resumen del contrato:

- **domain-modeler**: produce `spec.md` siguiendo `templates/slice-spec.md`. No escribe código. No propone implementación. Nota dudas en `# Preguntas abiertas`.

- **red**: escribe tests Given/When/Then en xUnit + FluentAssertions. Prohibido escribir código de producción. Prohibido tocar tests que no correspondan al slice. Prohibidos los mocks del dominio.

- **green**: hace pasar el último test rojo con el código más simple posible. Prohibido refactorizar, prohibido anticipar requerimientos futuros, prohibido añadir código que ningún test ejerza.

- **refactorer**: limpia tras green sin cambiar comportamiento. Los tests deben seguir pasando idénticos. Si nota un test mal diseñado, lo anota en `refactor-notes.md` pero no lo toca.

- **reviewer**: emite uno de tres veredictos: approved / approved-with-followups / request-changes. Audita cobertura de ramas del agregado, claridad de tests como documentación, completitud de invariantes, coherencia con `01-modelo-dominio.md` §15 y los ADRs.

---

## 6. Definition of Done de un slice

Un slice se considera cerrado cuando **todos** estos ítems están marcados:

- [ ] `spec.md` firmado por el usuario.
- [ ] Tests Given/When/Then cubren todos los escenarios de la spec (happy path + cada invariante + cada precondición).
- [ ] `dotnet test` en verde, sin warnings tratados como error.
- [ ] Cobertura de ramas del agregado afectado reportada (objetivo inicial **≥ 85 %**; se ajusta por ADR si hay rama genuinamente inalcanzable).
- [ ] `refactor-notes.md` presente (aunque diga "sin cambios").
- [ ] `review-notes.md` con veredicto **approved** o **approved-with-followups** (en el segundo caso los follow-ups van a `FOLLOWUPS.md` del repo).
- [ ] Handler registrado en Wolverine; proyección en Marten si aplica.
- [ ] Endpoint HTTP expuesto y documentado en OpenAPI si el slice lo implica.
- [ ] Hub SignalR registrado si el slice emite eventos hacia el frontend (ADR-005).
- [ ] Test de integración HTTP → Postgres pasa para el happy path.
- [ ] Si el slice toca un adapter Sinco on-prem (REST sobre VPN), tiene mock de contrato + un test de integración con WireMock o equivalente.
- [ ] Commit único con mensaje `feat(slice-{N}): {comando}` y referencia al `spec.md`.

---

## 7. Variantes del slice

### 7.1 Saga / process manager

Algunos comandos disparan una saga (p. ej. `CerrarInspeccionSaga` orquesta apertura de seguimientos, generación de OT en MYE y push SignalR). Para sagas, además del DoD §6:

- [ ] Test de saga con bus en memoria de Wolverine que verifica la secuencia de mensajes.
- [ ] Idempotencia documentada: qué pasa si el adapter del ERP se reintenta (`Idempotency-Key=InspeccionId` por defecto — ADR-003 — o justificación de otra clave en `spec.md §7`).
- [ ] Política ADR-006 verificada: 5xx → retry exponencial 5s→30s→2m→10m; 4xx + `ArgumentException` → dead-letter inmediato.
- [ ] Si el listener lee Marten: usa overload tenant-aware (`LeerAsync(id, envelope.TenantId, ct)`) — ver regla §9.2.
- [ ] Propagación JWT al ERP via `X-Forwarded-Authorization` en envelope — ver regla §9.3.

### 7.2 Sincronización de catálogos (ADR-004)

Para slices que sincronizan catálogos Sinco (equipos, partes, repuestos, obras, rutinas, causas de falla, tipos de falla, productos):

- [ ] Sync on-app-open con `If-None-Match`/`ETag` — sin cron nocturno (ADR-004 canonical).
- [ ] Body vacío → `"vaciado-sospechoso"`, cache local intacto (regla de seguridad ADR-004).
- [ ] Stale-while-revalidate documentado en `spec.md §8`.
- [ ] Health check por catálogo registrado en App Insights.
- [ ] Reglas operativas verificadas: IDs inmutables, descontinuar = `activo=false`.
- [ ] Atomicidad cross-catálogo: `LightweightSession` propia por catálogo vía `ITenantedDocumentSessionFactory.OpenSessionForTenant` — partial-failure por catálogo documentado.

### 7.3 Frontend slice

Para slices de la PWA (Fase 5 del roadmap), la spec se reformula:

- [ ] Mock/wireframe de referencia citado (mock vigente: `Plantillas Excel/mock del diseño.docx` imageN; secundarios: `02c-variantes-ux-novedades.html` para análisis comparativo histórico, `02d-wireframes-seguimientos.html` para el aggregate Seguimiento).
- [ ] Principios MD3 invocados explicitados (lista corta).
- [ ] Smoke visual del usuario antes de marcar como `approved`.
- [ ] Sin red phase formal (se trabaja con verificación visual + lint clean + build verde). Tests E2E con Playwright son slice aparte.

---

## 8. Excepciones y reglas de pragmatismo

1. **Spike de exploración.** Cuando un problema requiere entender una API externa (p. ej. una API de Sinco recién expuesta), se hace un spike en rama aparte. Se tira todo y se reimplementa en TDD. El spike nunca se mergea.
2. **Refactor sin cambio de comportamiento en otro slice.** Si el refactorer necesita tocar código fuera del slice actual, abre un slice `refactor-{N}` separado con sus propios tests de regresión.
3. **Bug en producción.** TDD al revés: test que reproduce el bug (rojo) → fix (verde) → refactor. Mismo workflow, distinta semántica.
4. **Bloqueo cross-team con Sinco on-prem.** Si un slice depende de un endpoint Sinco aún no expuesto (Fase 4 del roadmap), se trabaja contra el contrato acordado vía mock + WireMock. El slice se marca `🟡 mock-only` hasta que el equipo Sinco entregue el endpoint real.
5. **Embargo de docs (LEVANTADO 2026-05-07).** Pausa de edits NO-triviales a `Inspecciones/docs/*` estuvo vigente entre 2026-05-05 y 2026-05-07. Levantado al cerrarse 6 slices reales. Si se necesita uno nuevo, registrarlo en `CLAUDE.md` con fecha y disparador de levantamiento.

---

## 9. Contratos de calidad del código

- `nullable` habilitado, `TreatWarningsAsErrors=true` en todos los proyectos.
- Naming en español para conceptos de dominio (`InspeccionTecnica`, `Hallazgo`, `Repuesto`, `Seguimiento`, `Equipo`) y en inglés para plumbing (`Program`, `Handler`, `Projection`, `Adapter`).
- Records para eventos y comandos; clases para agregados.
- `TimeProvider` inyectado — prohibido `DateTime.UtcNow` en dominio.
- `Guid.NewGuid()` solo en handlers; en dominio se recibe el id desde fuera.
- `UbicacionGps` y firma manuscrita (`bytes` PNG) son value objects del dominio; se reciben por parámetro, no se generan dentro.
- Adjuntos vía SAS upload pattern (ADR-005): el dominio solo conoce el `BlobUri` final; nunca firma SAS.
- **Apply puro:** los métodos `Apply(Evt)` del agregado son mutaciones de estado sin validaciones ni excepciones. Las pre-condiciones viven en el método de decisión. Re-validar en `Apply` rompe el rebuild desde stream.

### 9.1 Identidad HTTP (regla mt-1 — ADR-002)

Todo endpoint HTTP lee identidad vía `ISessionService` (`Inspecciones.Infrastructure.Auth`). **Prohibido leer `HttpContext.User` o claims directamente en endpoints o handlers.** El `TecnicoId` del comando se construye como `session.IdUsuario.ToString(CultureInfo.InvariantCulture)`. Las capabilities se validan en el endpoint con `if (!session.Capabilities.Contains("xxx")) return Forbidden403(...)` antes de despachar al handler.

### 9.2 Multi-tenancy Marten (regla mt-2 — MT2-INV-1)

Toda apertura de sesión Marten en código de producción pasa por `ITenantedDocumentSessionFactory` (`Inspecciones.Infrastructure.Auth`). **Prohibido `store.LightweightSession()` o `store.QuerySession()` directo sin tenant en `src/`.** El `IDocumentSession` y `IQuerySession` scoped en DI ya vienen tenant-aware vía el factory — los handlers que los reciben por DI heredan tenancy sin tocar constructores.

Bypass legal de `OpenSessionForTenant(tenantId)`: listeners Wolverine que leen el tenant del `Envelope.TenantId`, tests E2E cross-tenant, y operaciones de bootstrap/admin sin contexto HTTP.

Para listeners Wolverine que escriben/leen Marten: aceptar `Wolverine.Envelope` como parámetro de `HandleAsync` y propagar el tenant explícitamente al puerto (ej. `IInspeccionReader.LeerAsync(id, tenantId, ct)`).

### 9.3 Propagación JWT al ERP (regla mt-3)

Las llamadas HTTP al ERP van con el bearer del usuario original (no un token global de servicio). El bearer viaja en el envelope del outbox bajo `X-Forwarded-Authorization` y los listeners lo extraen con `EnvelopeBearerExtractor`. El token se persiste solo en `wolverine_outgoing_envelopes.headers` (Postgres del módulo, red privada — aceptado por D-MT3-2). **Prohibido loguear el JWT raw** — solo `IdEmpresa`/`IdUsuario` (enteros, no PII).

### 9.4 Observabilidad por `IdEmpresa` (regla mt-4 — MT4-INV-3)

Los logs de handlers/listeners críticos incluyen `IdEmpresa` como propiedad estructurada para filtrado en App Insights / Azure Monitor. Para endpoints HTTP: el `SessionLoggingScopeFilter` ya lo aplica globalmente — los nuevos endpoints lo heredan sin código adicional. Para listeners Wolverine ERP: patrón `[LoggerMessage]` template con `TenantId={TenantId}` enriquecido desde `envelope.TenantId`. Métricas via `Meter("Inspecciones")` BCL (`InspeccionesMeters`) con tag `id_empresa`.

---

## 10. Referencias

- `Inspecciones/docs/01-modelo-dominio.md` §15 — fuente de verdad del modelo.
- `Inspecciones/docs/00-investigacion-mercado.md` §9 — ADRs y decisiones técnicas.
- `Inspecciones/docs/roadmap.md` — fases y secuencia de slices.
- `templates/slice-spec.md` — plantilla del spec.
- `templates/test-red.md` — plantilla del red-notes.
- `templates/review-notes.md` — plantilla del review.
- `templates/agent-personas/` — prompts del orquestador + 5 sub-agentes.
- `FOLLOWUPS.md` — backlog de deuda técnica sin slice propio.
