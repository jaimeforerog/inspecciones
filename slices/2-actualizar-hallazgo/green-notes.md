# Green notes — Slice 2: ActualizarHallazgo

**Fecha:** 2026-05-06
**Autor:** green (ejecutado por orchestrator)
**Estado:** VERDE — 53/53 domain tests en verde, 0 warnings.

---

## 1. Archivos modificados

Solo un archivo de producción modificado:

- **`src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`** — el stub `throw new NotImplementedException()` del método `ActualizarHallazgo` fue reemplazado con la implementación mínima.

---

## 2. Implementación aplicada

El método de decisión sigue el orden de validación del spec §4:

1. PRE-2 (I-H7): `Estado != EnEjecucion` → `InspeccionNoEnEjecucionException`
2. PRE-3: hallazgo no encontrado → `HallazgoNoEncontradoException`
3. PRE-4: hallazgo eliminado → `HallazgoEliminadoException`
4. PRE-7: `NovedadTecnica` vacía → `NovedadTecnicaVaciaException`
5. PRE-5 (I-H4): `RequiereIntervencion` sin tipo/causa → `TipoYCausaFallaRequeridosException`
6. PRE-6: `RequiereIntervencion` sin `AccionCorrectiva` → `AccionCorrectivaRequeridaException`
7. Emite `HallazgoActualizado_v1`

El orden de validación deliberado: Estado antes de buscar el hallazgo (evita trabajo innecesario). Luego existencia, luego eliminación, luego campos del payload.

---

## 3. Impulsos de refactor no implementados (candidatos para refactorer)

- **Extracción de método privado `ValidarAccionRequerida`**: las validaciones PRE-5 y PRE-6 son idénticas en `RegistrarHallazgo` y `ActualizarHallazgo`. Se podría extraer un helper privado `ValidarRequiereIntervencion(AccionRequerida, int?, int?, string?)`. Deliberadamente no se hace — es trabajo del `refactorer`.
- **Orden de validaciones PRE-5 vs PRE-6**: el spec no especifica cuál se evalúa primero entre tipo/causa y accionCorrectiva. Se evaluó PRE-5 (tipo/causa) antes de PRE-6 (accionCorrectiva) para consistencia con `RegistrarHallazgo`. Si el usuario prefiere el orden inverso, es ajuste de refactor.
- **`FirstOrDefault` vs `FindIndex`**: en el método de decisión se usa `FirstOrDefault` para obtener el hallazgo (más legible). En `Apply` se usa `FindIndex` (necesario para la mutación in-place con `with`). Inconsistencia deliberada por mínima complejidad — candidato a normalizar en refactor.

---

## 4. Estado del repo

```
dotnet test tests/Inspecciones.Domain.Tests/ --no-build
```
Correctas: 53, Con error: 0.

Los tests de Application.Tests y Api.Tests fallan por Docker no disponible en el entorno CI actual — condición pre-existente igual que en slices 1b/1c, no es regresión de este slice.
