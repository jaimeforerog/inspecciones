# Refactor notes — Slice 1g — FirmarInspeccion

**Autor:** refactorer
**Fecha:** 2026-05-07

---

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | move | `Application/Inspecciones/Excepciones.cs` → `Domain/Inspecciones/Excepciones.cs` | `DiagnosticoRequeridoException` movida de la capa Application a Domain. Ver justificación abajo. | 96 pass | 96 pass |

### Justificación del cambio #1

`DiagnosticoRequeridoException` era la única excepción en `Application.Excepciones` que no es de infraestructura de handler (lookup fallido, catálogo ausente): representa una invariante de dominio del comando (`Diagnostico` es campo obligatorio de negocio), análoga a `FirmaRequeridaException` y `GpsRequeridoException` que ya residen en `Domain.Excepciones`. Moverla al Domain restaura la coherencia: todas las excepciones que mapean a reglas de negocio del comando `FirmarInspeccion` quedan en el mismo archivo. Las excepciones que permanecen en `Application.Excepciones` son exclusivamente de lookup de infraestructura (`EquipoNoEncontradoException`, `InspeccionNoEncontradaException`, `ParteNoCorrespondeAlEquipoException`, `RepuestoNoEncontradoEnCatalogoException`, `SkuIncompatibleConParteException`).

El handler en `FirmarInspeccionHandler.cs` ya importaba `using Inspecciones.Domain.Inspecciones;` — el move no requirió cambios en los usings. El endpoint en `InspeccionesEndpoints.cs` también importaba `Inspecciones.Domain.Inspecciones` — sin cambios. Compilación: 0 warnings, 0 errores.

---

## Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §1 | `VerificarEnEjecucion()` helper privado — descartado. Memoria de slices anteriores (1b–1f) confirma que cada método lanza `InspeccionNoEnEjecucionException` con mensaje diferente por verbo de operación ("registrar", "actualizar", "eliminar", "asignar repuestos", "firmar"). Parametrizar el verbo haría el helper igual de verboso que la línea in-place. No es DRY real. Se aplica la misma decisión tomada en slices anteriores. |
| 2 | green-notes §2 | `HallazgosVigentes` como propiedad privada — descartado. La expresión `_hallazgos.Where(h => !h.Eliminado).ToList()` aparece una sola vez en `Firmar()`. Los demás métodos de decisión trabajan con `ObtenerHallazgoActivo()` (hallazgo individual, no lista filtrada). No hay duplicación real — una sola ocurrencia es abstracción especulativa, no DRY. |
| 3 | green-notes §3 | `EventProjection` vs `MultiStreamProjection` — mantenida `EventProjection`. `InspeccionFirmada_v1` e `InspeccionCancelada_v1` no contienen `EquipoId`; sin ese campo no es posible usar `DeleteEvent<T>` keyed. Agregar `EquipoId` a los eventos terminales es cambio de contrato de evento — requiere decisión del orquestador, fuera del alcance del refactorer. Followup #13 actualizado con el análisis completo. |
| 4 | green-notes §5 | `VerificarHallazgoIntervencionCompleto(Hallazgo h)` como método privado — descartado. El loop PRE-6 tiene ~15 líneas con 3 condiciones inline, dentro del umbral de 20 líneas de la persona. El método existe en un único contexto (`Firmar`). No hay duplicación ni complejidad que justifique la extracción ahora. |
| 5 | green-notes §4 | `DiagnosticoRequeridoException` — aplicado como cambio #1 (move a Domain). |

---

## Output final de `dotnet test`

```
dotnet test tests/Inspecciones.Domain.Tests/ --verbosity minimal

Correctas! - Con error:     0, Superado:    96, Omitido:     0, Total:    96, Duración: 453 ms
```

```
dotnet build — 0 Advertencia(s), 0 Errores
```

---

## Notas para reviewer

1. **Único cambio de producción:** `DiagnosticoRequeridoException` movida de `Application.Excepciones` a `Domain.Excepciones`. Ningún test de dominio la referencia directamente (PRE-4 es del handler, no del aggregate — no hay test de dominio que la ejercite). La cobertura de la excepción reside en `Application.Tests` (tests de integración con Docker), que fallan por Docker no disponible — preexistente, no relacionado con este slice.

2. **`EventProjection` es la decisión correcta dado el contrato actual de eventos.** La alternativa (`MultiStreamProjection`) requiere enriquecer `InspeccionFirmada_v1` e `InspeccionCancelada_v1` con `EquipoId`. Eso no es una decisión del refactorer — se documentó en followup #13.

3. **Helper `VerificarEnEjecucion()`:** la decisión de no extraerlo fue tomada en slices 1b–1f y se reconfirma aquí con la misma lógica. Si en un slice futuro el squad acuerda un mensaje genérico uniforme (sin verbo específico por operación), ahí sí tiene sentido.

4. **Sin riesgos residuales identificados.** El move de la excepción es mecánico y compilación limpia lo confirma.
