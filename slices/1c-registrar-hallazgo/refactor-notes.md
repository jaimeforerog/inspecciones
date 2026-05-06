# Refactor notes — Slice 1c — RegistrarHallazgo

**Autor:** refactorer
**Fecha:** 2026-05-06
**Spec consumida:** `slices/1c-registrar-hallazgo/spec.md`
**Green notes consumidas:** `slices/1c-registrar-hallazgo/green-notes.md`

---

## Cero cambios

El código de green quedó ya dentro de los criterios de calidad. Motivo: cada candidato evaluado no superó el umbral de justificación (duplicación real, comportamiento, claridad neta).

---

## Evaluación de los 7 puntos de la misión

### 1. Orden de precondiciones en `RegistrarHallazgo`

Orden actual: PRE-3 → PRE-10 → PRE-5/PRE-6 → PRE-7 → PRE-8 → PRE-9.

Evaluación: correcto y óptimo. PRE-3 (estado) es la comprobación más barata (campo en memoria) y la que falla más frecuentemente en producción — falla rápido ante inspecciones cerradas. PRE-10 (origen no soportado) guarda el dominio de error de una rama incompleta. Las PRE-5/PRE-6 son mutuamente excluyentes por `Origen`, así que su posición relativa no afecta nada. PRE-7 y PRE-8 comparten la condición `AccionRequerida == RequiereIntervencion` — PRE-7 va antes porque verificar nulidad de IDs enteros es más barato que evaluar `IsNullOrWhiteSpace`. PRE-9 al final porque falla raramente (la UI valida primero). No hay reordenación que mejore la defensividad.

**Resultado: sin cambio.**

### 2. `Apply(HallazgoRegistrado_v1)` — pureza

Verificado. El método solo ejecuta `_hallazgos.Add(new Hallazgo(...))` y `_contribuyentes.Add(e.EmitidoPor)`. Sin condicionales, sin lanzar excepciones, sin acceder a estado fuera del evento. Cumple el contrato de rebuild puro.

**Resultado: sin cambio.**

### 3. Excepciones — nombres y mensajes

Todos los mensajes son descriptivos e incluyen los valores que fallaron:

- `InspeccionNoEnEjecucionException`: incluye el valor de `Estado` actual (`*Firmada*`, `*Cancelada*`) — cubierto por los tests.
- `ParteNoCorrespondeAlEquipoException`: incluye `ParteEquipoId` y `EquipoId` concretos.
- `NovedadPreopOrigenIdRequeridoException`: mensaje `"*obligatorio*"` cubierto por test.
- `NovedadPreopOrigenIdNoPermitidoException`: mensaje `"*null*"` cubierto por test.
- `TipoYCausaFallaRequeridosException`: incluye `"*RequiereIntervencion*"` cubierto por tests.
- `OrigenNoSoportadoException`: incluye el valor del origen rechazado (`*Seguimiento*`, `*Monitoreo*`).

**Resultado: sin cambio.**

### 4. `EquipoLocal.Partes` — tipo

Tipo actual: `IReadOnlyList<ParteEquipoLocal>?`. Correcto. La inmutabilidad del catálogo está garantizada — ningún código del dominio o handler modifica la lista; solo se consulta con `.Any()`.

**Resultado: sin cambio.**

### 5. Mapeo de excepciones en el endpoint — duplicación

El endpoint de `IniciarInspeccion` mapea 4 tipos de excepción específicos del slice 1b. El endpoint de `RegistrarHallazgo` mapea 8 tipos del slice 1c. No hay tipos en común: las familias son disjuntas por diseño (cada slice define sus propias excepciones de dominio). Un método helper centralizado mezclaría ambas familias sin reducir líneas de código por slice — la abstracción sería especulativa. La duplicación "real" sería el patrón `catch (InspeccionDomainException) → switch → UnprocessableEntity`, que es un bloque de 3 líneas más el switch; no alcanza el umbral de DRY real.

**Resultado: sin cambio. Candidato diferido — ver §Candidatos descartados.**

### 6. `Hallazgo` value object — campos para futuros slices

Campos actuales: `HallazgoId`, `Origen`, `ParteEquipoId`, `NovedadPreopOrigenId`, `AccionRequerida`, `TipoFallaId`, `CausaFallaId`, `Eliminado`.

Análisis para slices futuros: `ActualizarHallazgo` y `EliminarHallazgo` necesitarán `HallazgoId` (presente) y `Eliminado` (presente) para sus invariantes. `SeguimientoOrigenId` aparece en §15.2 del modelo pero solo aplica cuando `Origen=Seguimiento`, cuyo slice aún no existe. Agregar `SeguimientoOrigenId` ahora sería anticipación especulativa sin test que lo valide. La regla del refactorer prohíbe abstracciones "por si acaso mañana".

**Resultado: sin cambio.**

### 7. Comentarios redundantes

Los XML docs de los `Apply` explican el *por qué* del patrón puro ("Si validás aquí, rompés el rebuild histórico"), lo cual es información no obvia para desarrolladores que no conozcan el contrato de Event Sourcing. Estos comentarios tienen valor como documentación en-código del invariante. Los que dicen "Stub para soporte de tests PRE-3 del slice 1c" en `Apply(InspeccionFirmada_v1)` y `Apply(InspeccionCancelada_v1)` son información de estado temporal valiosa para el reviewer y para quien atienda esos métodos en el slice correspondiente. No son redundantes.

**Resultado: sin cambio.**

---

## Candidatos descartados

| # | Sugerido por | Descripción | Motivo para no aplicar |
|---|---|---|---|
| 1 | green-notes §4.1 | Centralizar `catch (InspeccionDomainException) → switch` en un método helper | Cada endpoint maneja excepciones disjuntas; unificar crearía acoplamiento entre slices sin reducción real de líneas. Esperar a que emerja un tercer endpoint con tipos en común. |
| 2 | green-notes §4.3 | Unificar tipo de retorno de decisión a `IEnumerable<object>` o `IReadOnlyList<object>` | Ya es `IReadOnlyList<object>` en la firma; la implementación usa `new object[]` que satisface la interfaz. Sin problema de tipo. |
| 3 | green-notes §4.4 | Reemplazar `(HallazgoRegistrado_v1)eventos[0]` por pattern matching | El cast directo es correcto por spec (un único evento siempre). Pattern matching añadiría una rama de error que no corresponde a ningún escenario definido — abstracción especulativa. |
| 4 | misión §6 | Añadir `SeguimientoOrigenId` al value object `Hallazgo` | Campo solo relevante para `Origen=Seguimiento`, cuyo slice no existe. Agregar ahora es anticipación sin test que justifique. |

---

## Resultado final

| Verificación | Resultado |
|---|---|
| `dotnet build src/Inspecciones.Api/` | 0 warnings, 0 errores |
| `dotnet test tests/Inspecciones.Domain.Tests/` | 40/40 Superado, 0 Con error |
| Archivos de producción modificados | **Ninguno** |
| Archivos de tests modificados | **Ninguno** |

---

## Hand-off para el reviewer

El slice 1c entra al reviewer en estado idéntico al que dejó el green. Los puntos de atención para la revisión:

1. **Orden de precondiciones** — verificar que el orden PRE-3 → PRE-10 → PRE-5/PRE-6 → PRE-7 → PRE-8 → PRE-9 es el correcto según el contrato de la spec y que los tests de aislamiento (cada test viola una sola precondición) no ocultan dependencias de orden.
2. **Mensajes de excepción** — confirmar que los mensajes de `ParteNoCorrespondeAlEquipoException` y `InspeccionNoEnEjecucionException` son informativos para el cliente de la API (incluyen los valores concretos que fallaron).
3. **Mapeo de excepciones en endpoint** — confirmar que todos los tipos de `InspeccionDomainException` posibles del slice 1c tienen cobertura en el switch, incluyendo el fallback `"DOMINIO"` para tipos futuros.
4. **`EquipoLocal.Partes == null` → `[]` fallback** — el handler usa `equipo?.Partes ?? []` que rechaza con `ParteNoCorrespondeAlEquipoException` cuando el catálogo no está sincronizado. Confirmar que este comportamiento defensivo es el correcto para el MVP.
5. **Followup #12 resuelto** — el test `Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12` cubre el segundo `case` en `AplicarEvento`. Confirmar que el FOLLOWUPS.md refleja el cierre.
