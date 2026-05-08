# Green notes — slice 1i' RegistrarEvaluacionCualitativa

**Fecha:** 2026-05-08
**Autor:** green
**Estado:** verde

---

## 1. Archivos modificados

### Producción

- `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`
  - Reemplazado el stub `RegistrarEvaluacionCualitativa` (que lanzaba `NotImplementedException`) con la implementación completa: PRE-3 / I-M1, PRE-4 / I-M2, PRE-5 / I-M3, PRE-6 / I-M4, PRE-7 / I-M5b, PRE-8 / I-M7, guard I-H1, emisión de 1 o 2 eventos en orden causal.
  - El `Apply(EvaluacionCualitativaRegistrada_v1)` ya estaba implementado por el red (añade a `_itemsEvaluados` + `Contribuyentes`). Sin tocar.
  - El `Apply(HallazgoRegistrado_v1)` ya proyectaba `EvaluacionOrigenId` desde el evento. Sin tocar.

- `src/Inspecciones.Application/Inspecciones/RegistrarEvaluacionCualitativaHandler.cs`
  - Implementado `Handle`: carga aggregate, delega PRE al aggregate, append + `SaveChangesAsync` único, retorna `RegistrarEvaluacionCualitativaResult` con `HallazgoGeneradoId` poblado solo si `Calificacion=Malo`.

- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs`
  - Agregado endpoint `POST /api/v1/inspecciones/{inspeccionId:guid}/items/{itemId:int}/evaluacion`.
  - Mapeo de excepciones: 404 (InspeccionNoEncontrada), 409 (ItemYaEvaluado → `I-M7`), 422 para el resto.
  - Enum `CalificacionCualitativa` parseado desde string del request.

- `src/Inspecciones.Api/Inspecciones/RegistrarEvaluacionCualitativaRequest.cs` (nuevo)
  - DTO `RegistrarEvaluacionCualitativaRequest(Guid HallazgoId, string Calificacion, string? Observacion)`.

- `src/Inspecciones.Api/Program.cs`
  - Registrado `RegistrarEvaluacionCualitativaHandler` como `Scoped`.

---

## 2. Tests

**Resultado:** 151 superados, 4 omitidos, 0 errores (`Inspecciones.Domain.Tests`).

Los 4 omitidos son los mismos que antes de este slice (3 del slice 1i' marcados como Skip por infra + 1 del slice 1i). Los tests de `Inspecciones.Application.Tests` e `Inspecciones.Api.Tests` fallan por Docker no disponible en entorno local — condición preexistente documentada en memoria de proyecto.

---

## 3. Decisiones deliberadas de código mínimo

- `HallazgoGeneradoId` en el handler se determina directamente por `cmd.Calificacion == CalificacionCualitativa.Malo`, sin releer el evento emitido. Simple `if` en lugar de pattern match sobre la lista de eventos — suficiente para los tests actuales.
- El guard I-H1 (`ParteEquipoIdAusenteEnSnapshotException`) se evalúa dentro del bloque `if (cmd.Calificacion == CalificacionCualitativa.Malo)` — mismo patrón que en `RegistrarMedicion`.

---

## 4. Followup #20 — ObservacionCampo en record Hallazgo

El spec indica que este slice es oportunidad para cerrar followup #20. El red ya propagó `ObservacionCampo` desde el evento `HallazgoRegistrado_v1` al record `Hallazgo` (campo preexistente en el constructor). No se requirió intervención adicional en este aspecto.

---

## 5. Impulsos de refactor no implementados (candidatos para refactorer)

- El bloque de "guard header X-Client-Command-Id" se repite en cada endpoint. Candidato para middleware o extension method.
- La construcción del `HallazgoRegistrado_v1` en `RegistrarEvaluacionCualitativa` y en `RegistrarMedicion` comparten estructura; candidato para un factory method privado en el aggregate.
- Los mapeadores de excepción a `codigoError` en los endpoints podrían extraerse a un helper compartido.
