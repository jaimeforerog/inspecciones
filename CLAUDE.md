# CLAUDE.md — Inspecciones Sinco MYE

Este archivo orienta a Claude Code para trabajar en el repo. Las reglas duras de proceso viven en `METHODOLOGY.md`; este archivo solo apunta y fija convenciones de calidad.

## Estado del proyecto

- **Fase actual:** Fase 0 (diseño) al 95 %. Sin código aún. Solo docs y plantillas Excel.
- **Próximo trabajo:** primer slice de Fase 3 (backend core) cuando el usuario lo apruebe.
- **Roadmap:** `Inspecciones/docs/roadmap.md` (fases 0..10).
- **Modelo de dominio:** `Inspecciones/docs/01-modelo-dominio.md` §15 (fuente de verdad).
- **ADRs:** ADR-001 a ADR-005 en `00-investigacion-mercado.md §9`; ADR-003 ampliado en `01-modelo-dominio.md §13`; ADR-005 en `§14`; **ADR-006 (resiliencia outbox para integraciones ERP) en `§16`**.

## Metodología (resumen — ver `METHODOLOGY.md` para detalle)

- **TDD estricto** sobre Event Sourcing: Given/When/Then sobre eventos.
- **Squad de 5 agentes** secuencial: `domain-modeler` → `red` → `green` → `refactorer` → `reviewer`.
- **Unidad de trabajo:** un comando = un slice = una carpeta `slices/{N}-{slug}/{spec, red-notes, green-notes, refactor-notes, review-notes}.md` = un commit `feat(slice-{N}): {comando}`.
- **Plantillas:** `templates/slice-spec.md`, `templates/test-red.md`, `templates/review-notes.md`.
- **Personas de agente:** `templates/agent-personas/`.
- **Followups:** `FOLLOWUPS.md` en raíz.

## Stack (decidido en Fase 0)

| Capa | Tecnología | Notas |
|---|---|---|
| Event store / CQRS | Marten 7 sobre PostgreSQL 16 | |
| Mediator + outbox | Wolverine 3 | |
| Runtime | .NET 8+ | (aceptable .NET 9 si se valida en este repo) |
| Compute Azure | Azure Container Apps | scale-to-zero |
| DB Azure | Azure Database for PostgreSQL Flexible | |
| Identidad | **Heredada de la PWA Sinco MYE móvil** (host) | El módulo no se autentica solo; recibe el contexto del usuario del host. Mecanismo concreto a confirmar — ver ADR-002 (estado tentativo). |
| Push frontend | Azure SignalR (Standard tier) | ADR-005 |
| Integración Sinco on-prem | REST sobre VPN site-to-site | ADR-001 |
| Frontend | PWA React + MUI v6 (heredada de Sinco MYE) | módulo nuevo dentro de la PWA existente |

## Reglas duras de calidad (no negociables)

- `nullable` habilitado, `TreatWarningsAsErrors=true` en todos los proyectos.
- **Naming:** español para dominio (`InspeccionTecnica`, `Hallazgo`, `Repuesto`, `Seguimiento`), inglés para plumbing (`Program`, `Handler`, `Projection`, `Adapter`).
- Records para eventos y comandos; clases para agregados.
- `TimeProvider` inyectado — **prohibido `DateTime.UtcNow` en dominio**.
- `Guid.NewGuid()` solo en handlers; el dominio recibe el id desde fuera.
- `UbicacionGps(Latitud, Longitud, PrecisionMetros, CapturadoEn)` para coordenadas — prohibido `double` pelado.
- `BlobUri` para adjuntos — el dominio nunca firma SAS (ADR-005, pattern SAS upload).
- Identidad: el handler recibe claims por parámetro; el dominio nunca conoce JWTs.
- Cobertura de ramas del agregado afectado **≥ 85 %** por slice.
- Eventos versionados con sufijo `_v1`, `_v2` cuando emerja segunda versión.
- Soft delete: hallazgos y repuestos emiten `*Eliminado`; nunca borran del stream.
- **`Apply` puro:** los métodos `Apply(Evt)` del agregado son mutaciones puras de estado — sin validaciones, sin lanzar excepciones. Las pre-condiciones (estado actual, "ya firmado", invariantes I-*) viven en los métodos de decisión que producen los eventos. Re-validar en `Apply` rompe el rebuild desde stream.
- **Rebuild test obligatorio:** todo slice que toque comportamiento del agregado incluye un test que reproyecta los eventos emitidos sobre un agregado vacío y verifica que el estado resultante es el mismo que tras la decisión original. Atrapa validaciones intrusas en `Apply` y eventos fuera de orden causal.
- **Atomicidad de eventos:** múltiples eventos al mismo stream en el mismo handler son atómicos por construcción (un único `IDocumentSession.SaveChangesAsync()`). Prohibido partir un comando en dos `SaveChangesAsync`. Orden de los eventos = orden causal (p. ej. `Diagnostico → Dictamen → Firmada`).

## Convenciones de tests

- xUnit + FluentAssertions.
- Cero mocks del dominio.
- Marten embebido (Testcontainers Postgres) para tests de integración.
- `WebApplicationFactory<Program>` para tests HTTP end-to-end.
- WireMock (o equivalente) para tests de adapters Sinco on-prem cuando los endpoints reales no estén disponibles.
- Naming en español, frase completa, referenciando código de invariante cuando aplique:
  - ✅ `FirmarInspeccion_sin_GPS_lanza_GpsRequeridoException` (V-F3)
  - ❌ `Test1`, `ShouldWork`

## Convención de commits

- Un commit por slice cerrado: `feat(slice-{N}): {comando}`.
- Refinamientos mantienen sufijo: `feat(slice-{N}b): {refinamiento}`.
- Fixes transversales: `fix(slice-{N}): {descripción}`.
- Docs/ADRs aislados: `docs: ...`.

## Arranque del trabajo

1. Cuando el usuario diga "vamos con `XComando`":
2. Invocar `domain-modeler` con `templates/agent-personas/domain-modeler.md` y la referencia a `01-modelo-dominio.md §15` correspondiente.
3. Esperar firma del usuario en `spec.md`.
4. Invocar `red` → `green` → `refactorer` → `reviewer` en orden.
5. Como orquestador (`infra-wire`): registrar handler en Wolverine, proyección en Marten, endpoint HTTP, hub SignalR si aplica.
6. Commit único `feat(slice-{N}): {comando}` con referencia al `spec.md`.

## Memoria persistente del proyecto

- `Proyecto Inspecciones Sinco` — contexto del módulo y stack.
- `Proyecto hermano sinco-presupuesto` — referencia metodológica (mismo stack, 52 slices probados).

Ver `~/.claude/projects/.../memory/MEMORY.md` para el índice.
