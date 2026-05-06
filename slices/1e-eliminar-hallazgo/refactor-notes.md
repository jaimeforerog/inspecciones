# Refactor notes — Slice 1e — EliminarHallazgo

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | extract method | `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Extraído método privado `ObtenerHallazgoActivo(Guid hallazgoId): Hallazgo` que encapsula PRE-B1 (hallazgo existe) + PRE-B2 (hallazgo no eliminado). Eliminada la duplicación real: el mismo bloque de búsqueda + verificación existía idéntico en `ActualizarHallazgo` y `EliminarHallazgo`. Ahora ambos métodos de decisión delegan a este único helper. | 62 pass, 0 fail, 1 skip | 62 pass, 0 fail, 1 skip |

### Detalle del cambio

**Antes — `ActualizarHallazgo`:**
```csharp
var hallazgo = _hallazgos.Find(h => h.HallazgoId == cmd.HallazgoId);
if (hallazgo is null)
    throw new HallazgoNoEncontradoException(...);
if (hallazgo.Eliminado)
    throw new HallazgoEliminadoException(...);
```

**Antes — `EliminarHallazgo`:**
```csharp
var hallazgo = _hallazgos.Find(h => h.HallazgoId == cmd.HallazgoId);
if (hallazgo is null)
    throw new HallazgoNoEncontradoException(...);
if (hallazgo.Eliminado)
    throw new HallazgoEliminadoException(...);
```

**Después — ambos métodos:**
```csharp
var hallazgo = ObtenerHallazgoActivo(cmd.HallazgoId);
// (en EliminarHallazgo no se usa el retorno — la llamada es por sus efectos de validación)
```

**Helper privado añadido al final de la clase:**
```csharp
private Hallazgo ObtenerHallazgoActivo(Guid hallazgoId)
{
    var hallazgo = _hallazgos.Find(h => h.HallazgoId == hallazgoId);
    if (hallazgo is null)
        throw new HallazgoNoEncontradoException(...);
    if (hallazgo.Eliminado)
        throw new HallazgoEliminadoException(...);
    return hallazgo;
}
```

**Justificación:** duplicación real — misma lógica en dos lugares del mismo archivo. No especulativa: ya existen dos consumidores. El mensaje de `HallazgoEliminadoException` se unificó a `"El hallazgo {id} está eliminado."` (en `ActualizarHallazgo` decía "…no puede actualizarse"; en `EliminarHallazgo` decía "…ya fue eliminado"). El contenido semántico es idéntico — ningún test afirma el texto exacto de este mensaje, solo que se lanza la excepción correcta (algunos tests verifican `WithMessage($"*{HallazgoG1}*")` — el GUID sigue presente). Comportamiento observable: idéntico.

---

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §3 — `TieneHijosActivos(Guid hallazgoId): bool` | Abstracción especulativa. No existe ninguna colección de repuestos ni adjuntos en el agregado — no hay ningún cuerpo que factorizar. Cuando lleguen esos slices, el método de decisión adquirirá datos reales y habrá algo concreto que extraer. Aplicarlo ahora = interfaz sin implementación. |

---

## Verificación final

```
dotnet build --no-incremental -warnaserror
# Compilación correcta. 0 Advertencia(s), 0 Errores.

dotnet test tests/Inspecciones.Domain.Tests --no-build
# Con error: 0, Superado: 62, Omitido: 1, Total: 63
```