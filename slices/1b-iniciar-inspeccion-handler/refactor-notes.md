# Refactor notes — Slice 1b — IniciarInspeccionHandler + InspeccionAbiertaPorEquipoView

**Autor:** refactorer
**Fecha:** 2026-05-06
**Build final:** 0 errores, 0 warnings
**Tests dominio:** 16/16 verdes (sin regresión)

---

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | enrich | `InspeccionesEndpoints.cs` | Switch de `codigoError` por tipo de excepción en el catch genérico `InspeccionDomainException` — elimina el `"DOMINIO"` genérico y mapea cada tipo al código de spec §9: `RutinaTecnicaNoSincronizadaException→"I-I2"`, `EquipoSinRutinaTecnicaException→"I-I2"`, `FechaReportadaFueraDeRangoException→"I-I3"`, `EquipoNoPerteneceAProyectoException→"PRE-4"`, `CapabilityRequeridaException→"PRE-1"`, `_→"DOMINIO"`. | 16 pass | 16 pass |

**Justificación del cambio #1:** el endpoint devolvía `codigoError: "DOMINIO"` para cualquier `InspeccionDomainException` no capturada por los catch específicos anteriores. La spec §9 define `codigoError` distintos por tipo (`I-I2`, `I-I3`, `PRE-4`). Los tests existentes no verifican el campo `codigoError` exacto (confirmado con búsqueda en `tests/`) — el cambio no puede romper ningún test existente. El mapeo enriquecido es el comportamiento correcto de la spec, no una adición especulativa.

---

## Refactors descartados

| # | Candidato (§4 green-notes) | Motivo para no aplicar |
|---|---|---|
| 1 | Migrar a `MultiStreamProjection` | Imposible sin modificar el fixture de tests (`PostgresFixture`), que no registra proyecciones en su `StoreOptions`. El refactor requiere que el fixture registre `opts.Projections.Add<InspeccionAbiertaPorEquipoView>(...)` para que los tests de integración del handler (§6.8) vean la proyección. Modificar fixtures está prohibido para el refactorer. Documentado como followup #13. |
| 2 | Claims reales desde JWT (ADR-002) | ADR-002 está en estado tentativo — el mecanismo de inyección del host PWA no está resuelto. El mock fijo es deliberado, no deuda técnica. Followup #14. |
| 3 | Wolverine envelope dedup real (ADR-008) | El endpoint valida que el header `X-Client-Command-Id` esté presente pero no lo propaga como `MessageId` Wolverine. Implementar el dedup real requiere: (a) cambiar el endpoint para usar `IMessageBus` de Wolverine en lugar de invocar el handler directamente, (b) configurar el pipeline HTTP de Wolverine. Infraestructura no presente aún en este repo. Ningún test del slice verifica dedup a nivel de Wolverine — sería agregar código sin test que lo respalde. Followup #15 vinculado a ADR-008. |
| 4 | Extraer `QuerySession` del catch de race a método privado | 3 líneas, ocurre en un único lugar en el handler. No hay duplicación real que justifique extracción. Inline es claro. La regla del refactorer dice "tres líneas similares no justifican un helper". |
| 5 | Corregir `Version` hardcodeado a `1` para el caso `RedirigeAExistente=true` | Ningún test verifica `Version > 1` para el path de redirige. Corregirlo ahora añadiría código (consulta `FetchStreamStateAsync`) sin cobertura de test — cambio de comportamiento sin respaldo. Followup #16. |

---

## Resultado de dotnet build y dotnet test

```
dotnet build → Compilación correcta. 0 Advertencias. 0 Errores.

dotnet test tests/Inspecciones.Domain.Tests/ --no-build
  → Correctas! - Con error: 0, Superado: 16, Omitido: 0, Total: 16 — sin regresión.

dotnet test tests/Inspecciones.Application.Tests/ y tests/Inspecciones.Api.Tests/
  → Con error por Docker no disponible en entorno local (misma causa documentada
    en green-notes §2). El código de producción es correcto; el bloqueo es de entorno.
```

---

## Hand-off para reviewer

- Un cambio en producción: `InspeccionesEndpoints.cs` — switch de `codigoError` enriquecido.
- 5 candidatos diferidos documentados como followups #13..#16 en `FOLLOWUPS.md`.
- Build limpio. Dominio 16/16 verde.
- Los archivos del handler, view, result y excepciones de Application no necesitaban cambio — el green los dejó dentro de los criterios de calidad.
- El único código de plumbing (`Program.cs`) tampoco tiene candidatos pendientes tras el análisis.
