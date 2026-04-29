# Slices

Cada slice es la unidad de trabajo del proyecto: **un comando del dominio**, modelado, testeado, implementado, refactorizado y revisado en cinco fases discretas.

## Estructura de un slice

```
slices/
  {N}-{slug}/
    spec.md             # producido por domain-modeler (firmado por el usuario)
    red-notes.md        # producido por red
    green-notes.md      # producido por green
    refactor-notes.md   # producido por refactorer (incluso si dice "sin cambios")
    review-notes.md     # producido por reviewer (veredicto final)
```

## Convenciones de nombrado

- `{N}` es secuencial (`01`, `02`, `03`, `06b` para refinamientos).
- `{slug}` en kebab-case y refleja el comando o la temática:
  - `01-iniciar-inspeccion`
  - `02-registrar-hallazgo`
  - `12-saga-cerrar-inspeccion`
  - `_obs-visor-eventos` (transversal — prefijo `_`)

## Plantillas

Las plantillas vivas están en `templates/`:

- `templates/slice-spec.md` — contrato del `spec.md`.
- `templates/test-red.md` — contrato del `red-notes.md`.
- `templates/review-notes.md` — contrato del `review-notes.md`.
- `templates/agent-personas/` — prompts de los 5 roles.

## Flujo

Ver `METHODOLOGY.md §4` (workflow por comando) en la raíz del repo.
