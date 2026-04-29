# Followups — Inspecciones Sinco MYE

Backlog de deuda técnica sin slice propio. Cada item lo abre `reviewer` con veredicto `approved-with-followups` (o el orquestador en `azure-ops` / `infra-wire`).

**Convenciones:**

- ID: `#N` secuencial.
- **Estado:** 🟢 abierto · 🟡 en progreso · ✅ cerrado · ❄️ congelado.
- Cada followup cierra cuando entra a un slice o se decide explícitamente que no aplica.

## Abiertos

_(vacío — el primer followup se abre con el primer slice cerrado)_

## Cerrados

_(vacío)_

---

## Plantilla de entry

```markdown
### #N — {título corto} 🟢

**Origen:** slice {N}-{slug} review §3 hallazgo #X
**Fecha:** YYYY-MM-DD
**Tipo:** deuda técnica · domain extension · perf · seguridad · doc
**Descripción:** una o dos frases que digan qué hay que hacer y por qué.
**Disparador para abrir slice:** condición que justifica priorizarlo.
**Notas:** opcional.
```
