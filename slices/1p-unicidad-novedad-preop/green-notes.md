# Slice 1p — green-notes

**Fecha:** 2026-05-29

## Implementación

Refinamiento de `RegistrarHallazgo` (slice 1c) — dos guardas nuevas, sin eventos ni
`Apply` nuevos. No se tocó el contrato de entrada del endpoint.

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Excepciones.cs` | + `NovedadDescartadaNoImportableException` (INV-ND1 / FU-40), + `NovedadPreopYaImportadaException` (I-H13 / Gap 6b). |
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | `RegistrarHallazgo`: PRE-11 (novedad descartada → rechazo) + PRE-12 (novedad ya importada activa → rechazo), insertadas tras I-H2. Solo aplican a `Origen=PreOperacional`. PRE-12 filtra `!h.Eliminado` (D-1). |
| `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Switch del `catch (InspeccionDomainException)` de `RegistrarHallazgo`: `NovedadDescartadaNoImportableException => "INV-ND1"`, `NovedadPreopYaImportadaException => "I-H13"`. Heredan 422 del bloque existente. |
| `tests/.../RegistrarHallazgoUnicidadNovedadPreopTests.cs` | 7 tests (§6.1–§6.7). |
| `Inspecciones/docs/01-modelo-dominio.md §15.3` | + I-H13 + INV-ND1 canónicos (cierra FU-41). |
| `FOLLOWUPS.md` | FU-40 ✅, FU-41 ✅ con Resolución. |

## Estado de verificación — ✅ GREEN

Se instaló el SDK .NET 9 (9.0.314, per-user en `%LOCALAPPDATA%\Microsoft\dotnet`) en
esta sesión y se corrió la suite de dominio:

```
Correctas! - Con error: 0, Superado: 260, Omitido: 19, Total: 279  (Domain.Tests)
```

- Los **7 tests** del slice 1p (`RegistrarHallazgoUnicidadNovedadPreopTests`) pasan.
- Sin regresión en el resto del dominio.
- El proyecto **Api compila limpio** (`0 Advertencias, 0 Errores`) con
  `TreatWarningsAsErrors` activo — el switch de mapeo (`INV-ND1` / `I-H13`) compila.

> **Nota de entorno:** el build offline requirió `--no-restore --ignore-failed-sources
> -p:NuGetAudit=false -p:NoWarn=NU1801%3BNU1900` porque los feeds privados de Sinco
> (Azure DevOps) responden 401 en esta máquina. Los paquetes ya estaban en el caché
> global de NuGet de sesiones previas. `TreatWarningsAsErrors` se mantuvo activo para el
> build (solo se silenciaron los códigos de red de NuGet, no diagnósticos de código).

El E2E HTTP (`Api.Tests`, WebApplicationFactory + Postgres) sigue gated por falta de
Docker en esta sesión — no es regresión (limitación heredada mt-2 / FU-63).

## No-regresión esperada

- `RegistrarHallazgoTests` (slice 1c): sin cambios — las guardas solo disparan para
  `Origen=PreOperacional` con novedad descartada/ya-importada; los happy paths preop
  existentes (novedad 1042 sobre stream limpio) no las activan.
- Cobertura de ramas del aggregate: +2 ramas nuevas, ambas cubiertas por §6.1/§6.2 +
  caminos negativos §6.3/§6.4/§6.5/§6.7.
