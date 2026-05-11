# Green Notes — Slice 1n: DescartarNovedadPreop

**Fase:** green
**Fecha:** 2026-05-11
**Estado:** Verde — 13 Domain superados, 0 errores, 3 omitidos (skips D-2/PRE-4/PRE-1 esperados).

---

## 1. Archivos modificados

| Archivo | Tipo de cambio | Descripción |
|---------|----------------|-------------|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Método implementado | `Descartar(cmd, motivoDescarte, descartadaEn)` — PRE-2, PRE-5, PRE-6, emite `NovedadPreopDescartada_v1` |
| `src/Inspecciones.Application/Inspecciones/DescartarNovedadPreopHandler.cs` | Clase implementada | `Handle(cmd, ct)` — PRE-1 (stream null), motivo D-4, append + `SaveChangesAsync` |

---

## 2. Decisiones de "código más simple de lo que debería ser"

- **PRE-6 (INV-ND1) vía LINQ:** `_hallazgos.Any(h => h.Origen == OrigenHallazgo.PreOperacional && h.NovedadPreopOrigenId == cmd.NovedadId)`. No se añadió un `HashSet<int>` `_novedadesConvertidas` separado. El conjunto `_hallazgos` ya existe y la búsqueda es O(n) sobre una lista pequeña (cantidad de hallazgos por inspección en MVP). Podría optimizarse con un `HashSet<int>` cacheado, pero ningún test lo exige y la spec §12 P-1 elige opción A (sin tracking extra). Candidato para `refactorer` si el aggregate escala en número de hallazgos.

- **Motivo autogenerado en el handler:** el motivo se construye con `$"Cerrado por {cmd.DescartadaPor} el {descartadaEn:yyyy-MM-dd HH:mm} UTC desde Inspecciones"` directamente en el handler. No se extrae a una clase/método separado. Consistente con la decisión D-4 — plantilla documentada, sin necesidad de abstracción adicional en MVP.

---

## 3. Impulsos de refactor no implementados (candidatos para `refactorer`)

- **`_novedadesConvertidas: HashSet<int>`:** si el número de hallazgos por inspección crece (p. ej. >50), el `_hallazgos.Any(...)` de PRE-6 podría convertirse en un set derivado poblado por `Apply(HallazgoRegistrado_v1)`, igual que `_novedadesDescartadas`. No se implementa porque ningún test lo exige hoy y violaría la regla de "no código sin test".

- **Método privado para plantilla D-4:** el formato del motivo autogenerado está hardcoded en el handler. Si otros comandos usaran la misma plantilla, podría extraerse a un método estático. Por ahora es único a este slice.

---

## 4. Observación sobre P-3 (simetría INV-ND1 en RegistrarHallazgo)

El spec §12 P-3 recomienda también verificar en `RegistrarHallazgo` que la novedad no haya sido descartada (lado opuesto de INV-ND1). Esta verificación no tiene test en el slice 1n ni en el slice 1c existente, por lo que no se implementó aquí (regla: "prohibido agregar código que ningún test ejerza"). Se documenta como followup para un fix-FU separado o como tarea del slice 1c en refactor.

---

## 5. Output `dotnet test` final

### Domain (sin Docker requerido)

```
Correctas! - Con error: 0, Superado: 226, Omitido: 18, Total: 244, Duración: 714 ms
```

- 13 tests de `DescartarNovedadPreopTests` superados (12 que estaban rojos + 1 rebuild que ya pasaba).
- 3 skips del slice 1n confirmados (PRE-7 D-2, PRE-4 capability, PRE-1 Marten).
- 0 regresiones en los 213 tests de slices anteriores.

### Api/E2E

Los 8 tests activos de `DescartarNovedadPreopEndpointTests` fallan con `DockerEndpointAuthConfig` — Docker no disponible en entorno local. Comportamiento idéntico al de todos los slices previos; se verifica en CI. Ver memoria `project_docker_block.md`.

---

## 6. Build

```
Compilación correcta. 0 Advertencia(s), 0 Errores.
```

`TreatWarningsAsErrors=true` — ningún warning suprimido.
