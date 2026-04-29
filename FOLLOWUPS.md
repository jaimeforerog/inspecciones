# Followups — Inspecciones Sinco MYE

Backlog de deuda técnica sin slice propio. Cada item lo abre `reviewer` con veredicto `approved-with-followups` (o el orquestador en `azure-ops` / `infra-wire`).

**Convenciones:**

- ID: `#N` secuencial.
- **Estado:** 🟢 abierto · 🟡 en progreso · ✅ cerrado · ❄️ congelado.
- Cada followup cierra cuando entra a un slice o se decide explícitamente que no aplica.

## Abiertos

### #1 — Reportería de seguimientos sin tipo/causa 🟢

**Origen:** review consultor 2026-04-29 sobre `HallazgoRegistrado_v1` con `RequiereSeguimiento`. Decisión: relajar I-H4 (§15.3) para no exigir `TipoFallaId` / `CausaFallaId` en hallazgos con `AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}`. Trade-off aceptado: reportería degradada sobre seguimientos hasta que escalen.
**Fecha:** 2026-04-29
**Tipo:** doc · domain extension
**Descripción:** Medir en piloto si la falta de tipo/causa en hallazgos `RequiereSeguimiento` degrada la utilidad de los reports al supervisor. Si sí, evaluar dos opciones: (a) capturar `TipoFallaId` en mini-modal/wizard paso 1 (volver a endurecer I-H4 con MoreFields en UI); (b) inferir tipo/causa desde la cadena de escalación cuando exista, en el read model de seguimientos.
**Disparador para abrir slice:** ≥3 supervisores reportan en feedback de piloto que necesitan filtrar/agrupar seguimientos por tipo de falla, o que falta de clasificación impide priorizar revisión de seguimientos vencidos.
**Notas:** Este followup queda congelado hasta tener datos del piloto (Fase 9). No abrir slice hasta entonces.



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
