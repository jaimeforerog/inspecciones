# Green Notes — Slice 1f: AsignarRepuesto

**Agente:** green
**Fecha:** 2026-05-07
**Estado al entregar:** build limpio, 74 tests en verde (62 previos + 12 nuevos), 0 warnings

---

## 1. Archivos modificados

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Implementado `AsignarRepuesto(...)` y `Apply(RepuestoEstimado_v1)`. Implementado PRE-D real en `EliminarHallazgo`. |

No se crearon archivos nuevos. Los artefactos de dominio (`AsignarRepuesto.cs`, `RepuestoEstimado_v1.cs`, `Repuesto.cs`, `Excepciones.cs`) ya los había creado el agente `red`.

---

## 2. Decisiones deliberadas

### PRE-D antes de PRE-A (idempotencia primero)

El orden de evaluación en `AsignarRepuesto` es: **PRE-D → PRE-A → PRE-B1/B2 → PRE-C → PRE-E → PRE-G**.

Esto no es el orden "natural" de validación (normalmente se verificaría el estado antes que la idempotencia), pero es correcto según spec §7 y el test §6.3 lo exige explícitamente. La razón: un retry con el mismo `RepuestoId` debe ser silencioso incluso si en el ínterin el estado de la inspección cambió (p. ej. fue firmada después del primer intento). Evaluar PRE-D primero garantiza que el retry nunca falle por una razón secundaria.

### `Array.Empty<object>()` para idempotencia

Retorno más explícito y sin allocación que `new List<object>()`. Satisface `IReadOnlyList<object>` porque `T[]` implementa `IReadOnlyList<T>`.

### PRE-D en EliminarHallazgo — código mínimo

La verificación `_repuestos.Any(r => r.HallazgoId == cmd.HallazgoId)` es la implementación más simple que pasa el test. No distingue entre repuestos "activos" vs "eliminados" porque en este slice los repuestos no tienen soft delete — eso llega con `RemoverRepuesto`.

---

## 3. Candidatos para refactor (refactorer)

- **Patrón PRE-A / PRE-B1 / PRE-B2 repetido tres veces.** Los métodos `ActualizarHallazgo`, `EliminarHallazgo` y `AsignarRepuesto` arrancan con la misma secuencia: verificar `EnEjecucion` y luego llamar a `ObtenerHallazgoActivo`. Candidato a un método privado `VerificarInspeccionEjecucionYHallazgoActivo(Guid hallazgoId)` que consolide ambas verificaciones. El `refactorer` puede evaluar si la legibilidad mejora suficiente para justificarlo — por ahora la duplicación es deliberada y los tres métodos tienen mensajes de error ligeramente distintos.

- **Mensaje de error en PRE-A es inconsistente entre métodos.** `RegistrarHallazgo`, `ActualizarHallazgo`, `EliminarHallazgo` y `AsignarRepuesto` tienen mensajes distintos en la excepción `InspeccionNoEnEjecucionException`. Si se consolida en un helper, se podría parametrizar el verbo ("registrar", "actualizar", "eliminar", "asignar repuesto a"). Candidato para refactor.

---

## 4. Resultado de build y test

```
dotnet build --no-incremental -warnaserror
→ Compilación correcta. 0 Advertencia(s), 0 Errores.

dotnet test tests/Inspecciones.Domain.Tests --no-build
→ Correctas! - Con error: 0, Superado: 74, Omitido: 0, Total: 74
```
