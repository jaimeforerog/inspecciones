# Green notes — Slice 1e — EliminarHallazgo

**Autor:** green
**Fecha:** 2026-05-06
**Spec consumida:** `slices/1e-eliminar-hallazgo/spec.md` (firmada 2026-05-06).

---

## 1. Archivos modificados

- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — únicos dos métodos tocados:
  - `EliminarHallazgo(EliminarHallazgo cmd, DateTimeOffset ahora)` — reemplazado stub `NotImplementedException` con implementación completa de PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-D (estructura sin lanzar) y emisión de `HallazgoEliminado_v1`.
  - `Apply(HallazgoEliminado_v1 e)` — reemplazado stub con mutación pura: `_hallazgos[idx] with { Eliminado = true, MotivoEliminacion = e.Motivo }` y `_contribuyentes.Add(e.EliminadoPor)`.

---

## 2. Verificación final

```
dotnet build --no-incremental -warnaserror
# Compilación correcta. 0 Advertencias, 0 Errores.

dotnet test tests/Inspecciones.Domain.Tests --no-build
# Con error: 0, Superado: 62, Omitido: 1, Total: 63
```

Los 8 tests que estaban en rojo pasan en verde. El test del followup #21
(`ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException`) también pasó
al implementar `Apply(HallazgoEliminado_v1)`.

El único omitido es `EliminarHallazgo_con_hijos_activos_lanza_HallazgoTieneHijosActivosException_I_H9`
— skip deliberado de la spec §6.7 (requiere slices de repuestos/adjuntos).

---

## 3. Candidatos de refactor detectados (no implementados)

- **PRE-D estructura vacía:** el comentario en `EliminarHallazgo` documenta la intención pero no hay
  un bloque `if` real porque no existen colecciones de repuestos/adjuntos. Cuando lleguen esos slices,
  `refactorer` podría extraer un método privado `TieneHijosActivos(Guid hallazgoId): bool` para
  mantener el método de decisión limpio.

- **Patrón PRE-B1/PRE-B2 duplicado:** el mismo bloque de búsqueda + verificación de eliminación
  aparece idéntico en `ActualizarHallazgo` y `EliminarHallazgo`. Candidato para extracción a
  método privado `ObtenerHallazgoActivo(Guid hallazgoId): Hallazgo` que encapsule ambas verificaciones.
  No se implementó porque hacerlo aquí sería refactor preventivo — ningún test lo exige en este slice.

---

## 4. Decisiones deliberadas de código simple

- `Apply(HallazgoEliminado_v1 e)`: el `if (idx < 0) { return; }` es coherente con el mismo patrón
  ya establecido en `Apply(HallazgoActualizado_v1 e)` — no es código nuevo, es consistencia.

- PRE-D se implementó como comentario estructural (`// I-H9: verificar cuando existan slices...`)
  sin bloque `if` activo, conforme a la instrucción explícita de la spec §4 nota sobre PRE-D y
  al hand-off de red-notes §5.

---

## 5. Sin cambios de comportamiento accidentales

- Los 14 tests de `ActualizarHallazgoTests` (slice 1d) siguen pasando — no se tocó `ActualizarHallazgo`.
- Los tests de `RegistrarHallazgoTests`, `IniciarInspeccionTests` y `CierreAdministrativoTests` siguen
  pasando — no se tocó ningún otro método.
- El único cambio de comportamiento intencionado: `Apply(HallazgoEliminado_v1)` ya no lanza
  `NotImplementedException` — efecto esperado y requerido por el slice.