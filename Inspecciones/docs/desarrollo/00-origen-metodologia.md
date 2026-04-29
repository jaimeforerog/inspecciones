# Origen de la metodología

**Fecha de adopción:** 2026-04-29
**Origen:** lift-and-shift desde `C:\Users\jaime.forero\RiderProjects\sinco presupuesto`.

## Por qué

El proyecto hermano `sinco-presupuesto` lleva 52 slices implementados con la misma metodología (TDD estricto sobre Event Sourcing + squad de 5 agentes). Mismo stack base (.NET + Marten + Wolverine + ASP.NET + React/MUI). Inspecciones reusa metodología en lugar de inventar la propia.

## Qué se trajo verbatim

- `METHODOLOGY.md` (estructura completa).
- `templates/slice-spec.md`.
- `templates/test-red.md`.
- `templates/review-notes.md`.
- `templates/agent-personas/{domain-modeler, red, green, refactorer, reviewer}.md`.
- Estructura `slices/{N}-{slug}/`.
- Convención de commits: `feat(slice-{N}): {comando}`.
- Quality gates: `nullable` on, `TreatWarningsAsErrors`, `TimeProvider` inyectado, naming dominio español / plumbing inglés, records para eventos/comandos, cobertura ramas ≥85 %.
- DoD de 10 ítems base.

## Qué se adaptó

| Cambio | Razón |
|---|---|
| `Presupuesto`, `Rubro`, `Dinero`, `Moneda` → `InspeccionTecnica`, `Hallazgo`, `Repuesto`, `UbicacionGps`, etc. | Dominio distinto. |
| WorkOS (IdP propio standalone) → auth heredada del host | Inspecciones es **módulo dentro de la PWA Sinco MYE existente**, no app standalone. No tiene IdP propio: recibe el contexto del usuario del host (técnico, obras asignadas, rol). Mecanismo concreto del host a confirmar — ADR-002 está en estado tentativo. |
| Multi-tenant conjoint → multi-obra (claim `sinco_obras`) | Inspecciones no es SaaS multi-cliente; es módulo del ERP corporativo Sinco. Sin currency. |
| Multimoneda → mono-moneda implícita | El módulo no transa dinero; los repuestos tienen costo informativo, no presupuestal. |
| Mockup MD3 frontend → wireframes propios `02*.html` | Wireframes ya producidos en Fase 0 del roadmap. |
| Spec §10 (SignalR) y §11 (adapters Sinco) — secciones nuevas | ADR-005 (SignalR para push del cierre de inspección) y ADR-001 (REST sobre VPN hacia Sinco on-prem). |
| DoD: ítems extra para sagas (§7.1) y catálogos (§7.2) | Inspecciones tiene `CerrarInspeccionSaga` y 7 catálogos sincronizados; slices tipo distintos del comando puro. |
| Variante de slice "frontend" sin red phase formal (§7.3) | Política heredada del refactor de slice 25 en sinco-presupuesto: smoke visual + lint + build verde. |
| Cobertura ramas objetivo: idéntica (≥85 %) | Sin razón para relajar. |

## Qué quedó fuera

- ADRs específicos de sinco-presupuesto (multimoneda, AACE pivot, RubroTipo) — no aplican.
- Backlog `FOLLOWUPS.md` del proyecto hermano — Inspecciones arranca con backlog vacío.
- Estructura `web/` del frontend — Inspecciones se monta como módulo en la PWA Sinco MYE existente; no hay frontend standalone.

## Mantenimiento

- Si emerge un patrón en `sinco-presupuesto` que aplica a Inspecciones (p. ej. el `JsonStringEnumConverter` global del slice 17), se evalúa adoptarlo y se documenta como entry en este archivo.
- Si Inspecciones genera una variante metodológica que `sinco-presupuesto` puede aprovechar, se notifica (informalmente) al equipo del proyecto hermano.
