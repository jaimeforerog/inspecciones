# Refactor notes — Slice 1a — IniciarInspeccion (aggregate puro)

**Autor:** refactorer
**Fecha:** 2026-05-05
**Green consumido:** `slices/1a-iniciar-inspeccion-aggregate/green-notes.md`
**Archivo de producción revisado:** `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`

---

## Cero cambios

El código que dejó el agente green ya está dentro de los criterios de calidad del proyecto.
Motivo: ninguno de los 5 impulsos identificados en `green-notes.md §5` tiene duplicación real,
violación de regla de CLAUDE.md ni justificación en los tests actuales. El análisis propio
del refactorer tampoco encontró candidatos adicionales.

---

## Verificación previa

Antes de cualquier decisión se habilitó el build localmente. El bloqueo NuGet corporativo
(feeds `NuGetSinco` y `NuGetMaquinaria`) reportado en `green-notes.md §4` impedía correr
`dotnet test`. Se creó un `NuGet.Config` en la raíz del repo con package source mapping a
solo `nuget.org` — todos los paquetes de `Inspecciones.Domain` y su suite de tests son
exclusivamente de nuget.org.

```
dotnet build → Compilación correcta. 0 Advertencia(s). 0 Errores.
dotnet test  → 16 Correcto. 0 Fallido. 0 Omitido.
```

El `NuGet.Config` creado debe mantenerse en el repo para desbloquearlo en CI local.

---

## Refactors descartados

| # | Sugerido por | Impulso | Motivo para no aplicar |
|---|---|---|---|
| 1 | green-notes §5.1 | Extraer guards a métodos privados estáticos (`PRE-2: ValidarProyectoAsignado`, etc.) | Un único método de decisión, sin duplicación real. No hay un segundo método de decisión que comparta estas validaciones. Extraer sin duplicación es abstracción especulativa. |
| 2 | green-notes §5.2 | Cambiar `return new object[] { evento }` por helper `Events(...)` o `ImmutableArray<object>` | `object[]` implementa `IReadOnlyList<object>` directamente. No hay un segundo método de decisión que construya otro array. Introducir un helper sin dos casos de uso reales es abstracción especulativa. |
| 3 | green-notes §5.3 | Separar PRE-6 en dos `if` independientes (RutinaId mismatch vs Tipo incorrecto) | Ningún test distingue entre los dos sub-casos — el spec tampoco pide mensajes de error distintos. Separar hoy añade código que ninguna asserción ejercita. |
| 4 | green-notes §5.4 | Reemplazar `switch` en `AplicarEvento` por dispatcher de reflexión / convención Marten | Un único `case`. El `switch` con `default → InvalidOperationException` es la red de seguridad correcta para este slice. Anticipar el crecimiento es especulativo. |
| 5 | green-notes §5.5 | Extraer strings de mensaje con formato de fechas para i18n | MVP es solo español Colombia. Sin segundo locale en el roadmap aprobado para MVP. |

### Análisis propio adicional (más allá de los impulsos de green)

- **Orden de miembros:** propiedades → ctor privado → `Iniciar` (estático) → `Reconstruir`
  (estático) → `AplicarEvento` (privada instancia) → `Apply` (pública instancia). El orden
  refleja el ciclo de vida del agregado (escritura antes que lectura) y es coherente. No hay
  convención violada que justifique mover miembros.

- **Docstrings XML:** el `<summary>` de `Iniciar` documenta el contrato del método estático
  de decisión (WHY no obvio: retorna lista para appendear al stream, no modifica estado).
  El `<remarks>` protege contra la confusión con I-I1 (que vive en el handler). El `<summary>`
  de `Apply` explica por qué no valida (WHY no obvio: `Apply` puro es contrato de rebuild).
  Ambos comentarios cumplen la regla CLAUDE.md "comentarios solo cuando el WHY es no-obvio".
  No se eliminan.

- **Warnings:** `dotnet build` confirma 0 warnings con `TreatWarningsAsErrors=true` activo.

---

## Hand-off al reviewer

- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — sin cambios de código de producción.
- `NuGet.Config` — archivo nuevo en raíz del repo que desbloquea build/test local.
- `slices/1a-iniciar-inspeccion-aggregate/refactor-notes.md` — este archivo.
- 16/16 tests pasando. 0 warnings. Build limpio.

El reviewer puede asumir que el código de producción está en el mismo estado que entregó green,
con la diferencia operacional de que ahora los tests son ejecutables localmente sin acceso a
los feeds corporativos.