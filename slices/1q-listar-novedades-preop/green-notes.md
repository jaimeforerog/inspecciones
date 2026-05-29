# Slice 1q — green-notes

**Fecha:** 2026-05-29

## Implementación

Endpoint de lectura pasa-piso al ERP + `estado` derivado por el aggregate.

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/EstadoNovedadImportacion.cs` | nuevo enum `{ Disponible, Importada, Descartada }`. |
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | + método de lectura puro `EstadoNovedadPreop(int novedadId)`. |
| `src/Inspecciones.Api/Inspecciones/ListarNovedadesPreopResponse.cs` | nuevos DTOs `NovedadPreopImportableDto` (+ factory `Desde`) y `ListarNovedadesPreopResponse`. |
| `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | + `GET /api/v1/inspecciones/{id}/novedades-preop` (capability `ejecutar-inspeccion`). |
| `tests/.../EstadoNovedadPreopTests.cs` | 5 tests del método de derivación. |

## Estado de verificación

- ✅ **`Domain.Tests`: 260/0/19** con SDK .NET 9.0.314. Los 5 tests de
  `EstadoNovedadPreopTests` pasan.
- ✅ **Api compila limpio** (`0 Advertencias, 0 Errores`) con `TreatWarningsAsErrors`
  activo — el endpoint, los DTOs y el binding del enum `?estado=` compilan.
- ⏸️ **E2E HTTP** (`Api.Tests`, WebApplicationFactory + Postgres): gated por falta de
  Docker en esta sesión. El núcleo (derivación de `estado`) está cubierto a nivel
  dominio; el endpoint es pasa-piso delgado. Tests E2E documentados en spec §6, no
  ejecutados.

> Build offline: `--no-restore --ignore-failed-sources -p:NuGetAudit=false
> -p:NoWarn=NU1801%3BNU1900` (feeds privados de Sinco dan 401 en esta máquina; paquetes
> en caché global de sesiones previas).

## Notas de diseño

- `parteEquipoId` y responsable **no** se exponen (sin cambio en Maquinaria_V4). El front
  resuelve `parteEquipoId` con el selector de parte manual del Paso 1 (fallback acordado
  2026-05-29). Ver `Inspecciones/docs/09-solicitud-cambio-maquinaria-preop-fallas.md`.
- `codigoPreoperacional` sintetizado como `PREOP-{registroPreoperacionalId}`.
- Pendiente: `inspecciones-api-liaison` sincroniza `api-contract.md §8` con el shape de
  respuesta y los campos ausentes.
