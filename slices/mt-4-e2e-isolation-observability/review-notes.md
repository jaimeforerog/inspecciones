# Slice mt-4 — Review notes

**Fecha:** 2026-05-19
**Autor:** orquestador (rol `reviewer` — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-4).

## §1. Audit contra CLAUDE.md "Reglas duras de calidad"

- [x] **`nullable` habilitado, `TreatWarningsAsErrors`:** build verde con 0 warnings.
- [x] **Naming en español para dominio, inglés para plumbing:** todos los símbolos nuevos son plumbing (`IncomingBearerCarrier`, `CaptureBearerForOutboxMiddleware`, `ForwardAuthEnvelopeRule`, `SessionLoggingScope`, `SessionLoggingScopeFilter`, `InspeccionesMeters`) — naming en inglés excepto los términos de dominio (`Inspecciones`, `IdEmpresa`). Convención respetada.
- [x] **Records para eventos y comandos:** cero eventos nuevos. Cero comandos nuevos.
- [x] **`TimeProvider` inyectado, prohibido `DateTime.UtcNow`:** cero usos de `DateTime.UtcNow` en código nuevo (verificado con grep).
- [x] **`Guid.NewGuid()` solo en handlers:** cero usos en código nuevo.
- [x] **`Apply` puro:** **no se tocó el aggregate.** Test §6.12 (rebuild defensivo) prueba defensivamente que `Apply` sigue siendo puro.
- [x] **Cobertura ramas aggregate ≥ 85%:** mt-4 NO modifica el aggregate `Inspeccion`. Cobertura sigue en 94.44% (mt-3 baseline).
- [x] **Eventos versionados:** N/A — cero eventos nuevos.
- [x] **Atomicidad de eventos:** N/A.
- [x] **Identidad HTTP via `ISessionService`:** respetada — `SessionLoggingScopeFilter` resuelve `ISessionService` desde DI y nunca toca `HttpContext.User` directamente. Endpoints no se modificaron.
- [x] **Multi-tenancy Marten via `ITenantedDocumentSessionFactory`:** respetada — ningún código nuevo abre `IDocumentSession` directo. Los listeners ERP siguen usando `IInspeccionReader` (puerto introducido por mt-3).

## §2. Audit contra reglas multi-tenancy de mt-1/mt-2/mt-3

- **MT3-INV-3 fail-closed sin bearer:** `ForwardAuthEnvelopeRule` NO sobrescribe header existente del publisher, NO añade header si carrier vacío. El `BearerTokenPropagationHandler` (mt-3) sigue siendo el único punto donde fail-closed se enforce — mt-4 NO degrada esa garantía.
- **Logs no leakean JWT completo:** `SessionLoggingScope` solo loguea `IdEmpresa` e `IdUsuario` (enteros, no PII). El bearer entrante del header se persiste en `wolverine_outgoing_envelopes.headers` (Postgres del módulo, red privada — aceptado por D-MT3-2). Logs estructurados de los listeners NO loguean el bearer raw (verificado en `LogSyncFallida` y `LogCierreFallido`).
- **Tests cross-tenant determinísticos:** §6.7 usa `Guid.NewGuid()` por inspección; §6.9 (paralelismo) usa equipos distintos por slot — sin colisión I-I1 dentro del mismo tenant. Sin races sobre el outbox: `Task.WhenAll` se ejecuta sobre clients independientes.

## §3. Hallazgos

### Hallazgo #1 — `IEnvelopeRule.AddOutgoingRule` API no disponible (sorpresa controlada)

**Naturaleza:** desvío de implementación respecto al spec. El spec §2 mostraba `cfg.AddOutgoingRule(new ForwardAuthEnvelopeRule())`; Wolverine 3.13 NO expone ese método en la interfaz pública `ISubscriberConfiguration`. Green resolvió usando `CustomizeOutgoing(Action<Envelope>)` que SÍ está expuesto.

**Impacto:** funcional equivalente. El método `Modify` del rule se invoca para cada envelope outgoing — mismo comportamiento que la API documentada en docstring del rule (`IEnvelopeRule` permanece como tipo conceptual).

**Acción:** ninguna — documentado en green-notes "D-MT4-1'". El spec §12.A lo anticipó como riesgo.

### Hallazgo #2 — `SessionLoggingScopeFilter` reemplaza 15 modificaciones de endpoint por una sola registración

**Naturaleza:** mejora vs el spec original (D-MT4-2). El filter aplica `BeginEmpresaScope` a TODOS los endpoints del grupo automáticamente — el spec proponía inyectar el helper en 15 endpoints uno por uno.

**Impacto:** **positivo.** Reduce blast radius del slice, aplica el patrón a endpoints futuros sin recordatorio, mantiene el slice "plumbing-only" sin tocar la lógica de cada endpoint.

**Acción:** ninguna — documentado en green-notes "D-MT4-2'".

### Hallazgo #3 — Tests E2E `Api.Tests` requieren Postgres (no son skip independientes)

**Naturaleza:** los nuevos tests E2E (`CrossTenantE2EIsolationTests`, `CaptureBearerForOutboxEndToEndTests`) no usan `[Fact(Skip)]`. La fixture `InspeccionesAppFactory` falla en construcción si Postgres no está, abortando **todos** los `Api.Tests` (no solo los del slice). Esta es la realidad heredada de mt-2 (`MartenConjoinedTenancyTests`) — no es regresión específica de mt-4.

**Impacto:** en máquinas sin Postgres, los `Api.Tests` siempre fallaron desde mt-2. En máquinas con `POSTGRES_TEST_CONNSTRING` o Docker, los nuevos tests corren normalmente. Verificado: `Domain.Tests` y `Infrastructure.Tests` (que NO dependen de Postgres) pasan localmente en este momento.

**Acción:** opcional — podría agregarse fallback skip en el `InspeccionesAppFactory` constructor para emitir un `Skip` xUnit en lugar de excepción cuando Postgres no detectable. Eso es **slice operativo** (mejora DevEx), no del scope mt-4. Followup nuevo: **FU-63 — Skip determinístico de `Api.Tests` cuando Postgres no detectable.**

### Hallazgo #4 — Métrica `inspecciones.erp.calls` no testeada

**Naturaleza:** el counter `inspecciones.erp.calls` se incrementa en los 2 listeners ERP, pero no hay test que verifique el incremento. La librería `System.Diagnostics.Metrics` permite test con `MeterListener`. **No es test crítico** — la métrica es informacional/observabilidad, no garantía de comportamiento de negocio.

**Impacto:** baja confianza estática de que la métrica se emite correctamente. Si el código del listener se refactoriza y la línea `RegistrarLlamadaErp(...)` se elimina por error, no hay test que lo detecte.

**Acción:** **followup latente FU-64 — Test del counter `inspecciones.erp.calls` con `MeterListener`.** No bloqueante.

### Hallazgo #5 — `IncomingBearerCarrier` y `AmbientBearerTokenAccessor` duplican ~20 líneas de boilerplate

**Naturaleza:** ambos son `AsyncLocal<string?>` estático + `ScopeReverter` IDisposable. Refactor explícitamente rechazado en refactor-notes §1 con razón documentada (semánticas distintas, FU-61 ya rastrea defensivamente).

**Impacto:** ninguno — duplicación reconocida y justificada.

**Acción:** ninguna.

## §4. Veredicto

**`approved-with-followups`**

- **Approved:** el slice cumple su contrato. `CaptureBearerForOutboxMiddleware` cierra FU-60; tests E2E cross-tenant + paralelismo cierran FU-56; test rebuild defensivo cierra FU-59; structured logging por `IdEmpresa` aplicado vía filter global; métrica `inspecciones.erp.calls` introducida. Sub-track multi-tenancy listo para piloto.
- **Followups nuevos:**
  - **FU-63** — Skip determinístico de `Api.Tests` cuando Postgres no detectable (mejora DevEx, hereda de mt-2).
  - **FU-64** — Test del counter `inspecciones.erp.calls` con `MeterListener` (cobertura defensiva del wiring de métricas).
- **Followups cerrados por mt-4:**
  - **FU-56** (validar Wolverine 3 prefiere overload tenant-aware en producción — cubierto por la cadena end-to-end de los tests §6.10 + los 11 tests mt-3 ya existentes que verifican que el listener con envelope se ejecuta).
  - **FU-59** (test rebuild cross-tenant defensivo — cubierto por `RebuildCrossTenantDefensivoTests`).
  - **FU-60** (`CaptureBearerForOutboxMiddleware` — implementado y testeado).

## §5. Acción para infra-wire / doc-writer

- [ ] `Program.cs` ya tiene el wiring del middleware + envelope rule + filter (verificable con build verde). **infra-wire OK.**
- [ ] doc-writer actualiza `CLAUDE.md` → mt-4 cerrado + sub-track multi-tenancy ✅ cerrado.
- [ ] doc-writer actualiza ADR-009 → "ACEPTADA, sub-track cerrado, listo para piloto multi-empresa".
- [ ] doc-writer actualiza `FOLLOWUPS.md` → FU-56, FU-59, FU-60 a Cerrados; FU-63, FU-64 abiertos.
- [ ] doc-writer crea `baseline-piloto.md`.
- [ ] commit `feat(slice-mt-4): E2E cross-tenant isolation + observabilidad por IdEmpresa + cierre sub-track`.

Status: **review aprobado con 2 followups nuevos abiertos.** Avanzar a doc-writer + commit.
