# Red notes — Slice {N} — {NombreComando}

**Autor:** red
**Fecha:** YYYY-MM-DD
**Spec consumida:** `slices/{N}-{slug}/spec.md` (commit `{sha corto}`).

---

## 1. Tests escritos

Listar cada test nuevo o modificado, con el escenario de la spec al que corresponde.

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `RegistrarHallazgo_en_inspeccion_iniciada_emite_HallazgoRegistrado` | 6.1 happy path | `tests/…/RegistrarHallazgoTests.cs` |
| `RegistrarHallazgo_en_inspeccion_firmada_lanza_EstadoInvalido` | 6.2 PRE-1 | ídem |
| `RegistrarHallazgo_sin_GPS_lanza_GpsRequeridoException` | 6.3 I-H4 | ídem |
| … | | |

## 2. Verificación de estado rojo

Evidencia de que todos los tests nuevos fallan por la razón correcta (no por "no compila").

```
dotnet test --filter "FullyQualifiedName~RegistrarHallazgo"
# salida esperada: X failed, 0 passed, 0 skipped
# razón de fallo: método/clase no existe, o excepción esperada no se lanza.
```

## 3. Código de producción tocado

- [ ] Sin cambios en `src/` (caso puro: el dominio ya tiene las piezas necesarias, solo falta el comportamiento).
- [ ] Agregadas firmas/stubs mínimas para que compile (listar archivos).

Si se agregaron stubs, cada stub debe lanzar `NotImplementedException()` — nunca devolver un valor que enmascare el rojo.

## 4. Desviaciones respecto a la spec

Si durante escribir los tests se descubrió un gap en la spec, se documenta aquí y se lleva al domain-modeler. El slice se pausa hasta que la spec se actualice y vuelva a firmarse.

- [ ] Sin desviaciones.
- [ ] Desviaciones (detalle): …

## 5. Hand-off a green

- Spec firmada: sí / no.
- Todos los tests rojos: sí / no.
- Sin cambios de comportamiento accidentales: sí / no.
