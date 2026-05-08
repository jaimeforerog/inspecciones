# Green notes — Slice 1j — OmitirItemMonitoreo

**Autor:** green
**Fecha:** 2026-05-08

---

## 1. Archivos modificados

- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` — método `OmitirItem` implementado
  (reemplazado el stub con `NotImplementedException` por la lógica completa).
  Líneas tocadas: región `// ── Slice 1j — OmitirItemMonitoreo ──`.

---

## 2. Decisiones de diseño (código más simple posible)

- **Orden de validaciones:** PRE-3/PRE-4 (motivo) primero, luego PRE-5 (tipo), PRE-6 (estado),
  PRE-7 (snapshot), PRE-8 (ya procesado — medido y evaluado como dos `if` separados para distinguir
  el mensaje), PRE-9 (ya omitido). Sigue exactamente el orden recomendado en red-notes §6 hand-off.

- **PRE-8 dos `if` separados:** el test espera mensaje con el ItemId y la razón ("medición" vs
  "evaluación"). Se usaron dos `if` en lugar de un `if (A || B)` para poder incluir el mensaje
  descriptivo exacto en cada caso. Candidato a refactor si se quiere unificar en un helper.

- **`snapshotIds` calculado antes del `FirstOrDefault`:** se materializa antes del `throw` para
  incluir la lista de IDs válidos en el mensaje de excepción (idéntico al patrón de
  `RegistrarMedicion`). Sin overhead en el happy path porque el `throw` es la rama excepcional.

- **Emisión:** exactamente un evento `ItemMonitoreoOmitido_v1`. Sin condicional, sin hallazgo
  automático, sin segundo evento. Hardcodeado por diseño de dominio (§12.11.5 punto 6).

- **`Apply(ItemMonitoreoOmitido_v1)` no tocado:** ya existía del slice 1i con la implementación
  correcta (`_itemsOmitidos.Add(e.ItemId)` + `_contribuyentes.Add(e.EmitidoPor)`). El green no
  lo modificó.

---

## 3. Impulsos de refactor no implementados (candidatos para `refactorer`)

- Los métodos `RegistrarMedicion`, `RegistrarEvaluacionCualitativa` y `OmitirItem` comparten el
  patrón de validación PRE-5/PRE-6/PRE-7 (tipo Monitoreo, estado EnEjecucion, ítem en snapshot).
  Candidato para extraer un método helper `ValidarContextoMonitoreo(itemId)` en la fase `refactorer`.

- Los tres métodos también comparten la materialización de `snapshotIds` para el mensaje de error
  de PRE-7. Podría unificarse con el helper anterior.

- PRE-8 usa dos `if` separados. Un helper `EstaItemProcesado(itemId)` podría unificarlos si en
  el futuro se añade un tercer tipo de procesamiento (ej. medición actualizada).

---

## 4. Output del `dotnet test` final

```
dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj

Correctas! - Con error: 0, Superado: 167, Omitido: 6, Total: 173, Duración: 75 ms
```

- 167 tests verdes (151 previos + 16 nuevos activos del slice 1j).
- 0 tests rojos.
- 6 omitidos (4 previos de slices anteriores + 2 nuevos del slice 1j: PRE-2 handler y
  idempotencia Wolverine).
- 0 regresiones en tests previos.
