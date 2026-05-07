# Refactor notes — Slice 1h — IniciarInspeccionMonitoreo

**Autor:** refactorer
**Fecha:** 2026-05-07

---

## §1 Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | fix inconsistencia de tipo | `src/Inspecciones.Application/Inspecciones/IniciarInspeccionMonitoreoResult.cs` | `Version: long` → `Version: int`. El tipo `long` era un residuo de la fase red (stub mínimo). `IniciarInspeccionResult` (slice 1b) usa `int`; el handler siempre escribe `Version: 1`. La inconsistencia no tenía cobertura de test pero producía un tipo diferente al hermano sin justificación semántica. | 104 pass | 104 pass |
| 2 | cleanup docstring | `src/Inspecciones.Application/Inspecciones/IniciarInspeccionMonitoreoResult.cs` | Eliminado comentario residual "Slice 1h — stub mínimo fase red" del docstring del record. El stub fue implementado en green; el comentario ya no era verdadero. | 104 pass | 104 pass |

---

## §2 Refactors descartados

| # | Sugerido por | Motivo para no aplicar |
|---|---|---|
| 1 | green-notes §5.1 | Duplicación estructural entre `IniciarInspeccionHandler` (1b) e `IniciarInspeccionMonitoreoHandler` (1h) — refactor transversal: toca el handler del slice 1b. Regla del orquestador: preferir followup antes que refactor cross-slice. Registrado como #23 en `FOLLOWUPS.md`. Disparador: tercer handler con el mismo patrón. |
| 2 | green-notes §5.2 | Constante `MensajeActiva` duplicada en ambos handlers — mismo motivo que #1: extraerla a una clase compartida requiere tocar `IniciarInspeccionHandler` del slice 1b. Incluido en followup #23. |
| 3 | green-notes §5.3 | Unificar `IniciarInspeccionMonitoreoResult` con `IniciarInspeccionResult` — tocaría el tipo del slice 1b y los tests de integración/API que lo referencian. Registrado como #24 en `FOLLOWUPS.md`. Disparador: cuando el orquestador implemente el endpoint HTTP y confirme que el mapeo es idéntico. |
| 4 | green-notes §5.4 | Default `TipoInspeccion.Tecnica` en `InspeccionAbiertaPorEquipoView.Tipo` — workaround de backward compat con documentos Marten previos a 1b. Reemplazarlo por una migración explícita es un cambio de infraestructura Marten, no un refactor de código de producción del slice 1h. Descartar; evaluar en el slice de migraciones si emerge. |

---

## §3 Followups nuevos abiertos

| # | Título | Origen |
|---|---|---|
| #23 | Extraer `MensajeActiva` a constante compartida entre handlers y alinear duplicación estructural 1b/1h | Candidatos §5.1 + §5.2 de green-notes |
| #24 | Evaluar unificar `IniciarInspeccionResult` e `IniciarInspeccionMonitoreoResult` en un tipo canónico | Candidato §5.3 de green-notes |

---

## §4 Estado tests + build

### dotnet test

```
Correctas! - Con error: 0, Superado: 104, Omitido: 0, Total: 104, Duración: 115 ms
```

Tests de Application (`Inspecciones.Application.Tests`): 18 fallan por Docker no disponible localmente — comportamiento preexistente desde slice 1b. No atribuible a este refactor.

### dotnet build

```
Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

---

## §5 Cobertura: branch-rate del aggregate `Inspeccion` post-refactor

Sin cambios en `Inspeccion.cs` ni en ningún método de decisión o Apply. La cobertura de ramas del aggregate no puede haber disminuido respecto al 96.66 % post-1g: el único archivo modificado es `IniciarInspeccionMonitoreoResult.cs`, que es un record de datos sin ramas condicionales. La cobertura formal con `dotnet test --collect:"XPlat Code Coverage"` requiere Docker para los tests de integración y se ejecuta en CI.
