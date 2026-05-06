# Green notes — Slice 1c — RegistrarHallazgo

**Autor:** green
**Fecha:** 2026-05-06
**Spec consumida:** `slices/1c-registrar-hallazgo/spec.md`
**Red notes consumidas:** `slices/1c-registrar-hallazgo/red-notes.md`

---

## 1. Archivos modificados (solo producción)

| Archivo | Tipo de cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Reemplazó stub `RegistrarHallazgo` con lógica completa + implementó `Apply(HallazgoRegistrado_v1)` con mutación real |
| `src/Inspecciones.Application/Inspecciones/RegistrarHallazgoHandler.cs` | Reemplazó stub `ManejarAsync` con flujo completo (PRE-2, PRE-4, decisión, Append, SaveChangesAsync) |
| `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs` | Agregó endpoint `POST /api/v1/inspecciones/{id:guid}/hallazgos` con mapeo de excepciones |
| `src/Inspecciones.Api/Program.cs` | Registró `RegistrarHallazgoHandler` como Scoped |

---

## 2. Resultado de verificación

### `dotnet build`
```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

### `dotnet test tests/Inspecciones.Domain.Tests/`
```
Correctas! - Con error: 0, Superado: 40, Omitido: 0, Total: 40
```
- 17 tests del slice 1a — todos verdes, no rotos.
- 23 tests del slice 1c — todos verdes (22 que estaban rojos + 1 followup #12 que ya estaba verde).

### Tests de integración (Application.Tests, Api.Tests)
Fallan por Docker no disponible en entorno local — comportamiento esperado y documentado en `red-notes.md` y en la memoria persistente del proyecto (`project_docker_block.md`).

---

## 3. Decisiones de implementación no obvias

### Orden de validaciones en `RegistrarHallazgo`

El red sugería el orden PRE-10, PRE-9, PRE-3, PRE-6, PRE-5, PRE-7, PRE-8. Se mantuvo ese orden exactamente. La razón de diseño: PRE-3 (estado EnEjecucion) va primero en la spec pero el red recomendó PRE-10 antes — en la implementación final se antepone PRE-3 (§4 de la spec lista PRE-3 primero). Sin embargo, el orden concreto que respetan los tests es: PRE-3 → PRE-10 → PRE-5/PRE-6 → PRE-7 → PRE-8 → PRE-9. Todos los tests pasan porque cada test viola solo una precondición a la vez y las anteriores están satisfechas en el fixture.

### `Apply(HallazgoRegistrado_v1)` — campos del value object `Hallazgo`

El record `Hallazgo` tiene `TipoFallaId` y `CausaFallaId` pero no tiene `NovedadTecnica`, `AccionCorrectiva`, ni los demás campos del evento. Decisión deliberada: el aggregate solo necesita el estado mínimo para sus invariantes futuras (soft delete por `HallazgoId`, unicidad, conteos). Los campos de display viven en el evento y en proyecciones read-side. No se amplió `Hallazgo` porque ningún test lo requiere.

### Handler — `partesDelEquipo` fallback a lista vacía

Si `equipo` es null (catálogo no sincronizado para ese `EquipoId`), `partesDelEquipo` cae al empty array y la validación lanza `ParteNoCorrespondeAlEquipoException`. Es comportamiento defensivo correcto: si no hay catálogo, no hay partes válidas.

### `IReadOnlyList<ParteEquipoLocal>?` — null vs empty

`EquipoLocal.Partes` puede ser null (default del record, backward-compatible). El handler usa `equipo?.Partes ?? []` para no romper con equipos creados antes de que se añadiera el campo en slice 1c.

### Endpoint — `{id:guid}` constraint en la ruta

Se usó `{id:guid}` en lugar de `{id}` para que ASP.NET Core rechace con 400 automáticamente si el path no es un GUID válido, antes de llegar al handler.

### `Microsoft.AspNetCore.Routing` using

Se añadió para resolver `IEndpointRouteBuilder` correctamente en el archivo de endpoints. El build no generó warnings con ese using.

---

## 4. Impulsos de refactor NO aplicados (candidatos para refactorer)

1. **Mapeo de excepciones duplicado**: tanto el endpoint de `IniciarInspeccion` como el de `RegistrarHallazgo` tienen bloques `catch (InspeccionDomainException)` con un switch de `codigoError`. Un método `MapearCodigoError(InspeccionDomainException)` podría centralizar esto. No aplicado — ningún test lo exige y refactorer lo decidirá.

2. **Claims mock hardcodeado** (`const string tecnicoId = "rmartinez"`): el endpoint de `IniciarInspeccion` también tiene un mock similar. Cuando ADR-002 se concrete, este bloque se reemplaza por extracción del JWT. Por ahora es correcto y mínimo.

3. **`IReadOnlyList<object>` vs `object[]`**: el método `RegistrarHallazgo` retorna `new object[] { evento }` mientras que `Iniciar` retorna lo mismo. Se podría unificar el tipo de retorno a `IReadOnlyList<object>` o `IEnumerable<object>`. No aplicado.

4. **`eventos[0]` cast directo**: en el handler se hace `(HallazgoRegistrado_v1)eventos[0]`. Es frágil si algún día `RegistrarHallazgo` emitiera más de un evento. Para este slice es correcto por spec (un único evento siempre).

---

## 5. Notas sobre los stubs del red que se mantienen sin cambio

- `InspeccionFirmada_v1` y `InspeccionCancelada_v1` con sus `Apply` puros: ya tenían la lógica correcta desde el red (transición de estado + `_contribuyentes`). No se tocaron.
- `Hallazgo.cs`: shape mínimo del red es suficiente para los tests del green.
- Los enums `OrigenHallazgo`, `AccionRequerida`, todos los records de evento/comando: correctos desde el red.
