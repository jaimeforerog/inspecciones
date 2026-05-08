# Refactor notes — Slice 1i' — RegistrarEvaluacionCualitativa

## Cambios aplicados

| # | Tipo | Archivo | Descripción | Tests antes | Tests después |
|---|---|---|---|---|---|
| 1 | cleanup | `Inspeccion.cs` | Eliminado texto residual "Stub mínimo fase red — Apply vacío es intencionalmente mínimo para que el rebuild test falle solo cuando NotImplementedException sea la razón esperada." del docstring de `Apply(EvaluacionCualitativaRegistrada_v1)`. Reemplazado por docstring consistente con el patrón de los demás Apply del aggregate. | 151 pass | 151 pass |
| 2 | cleanup | `RegistrarEvaluacionCualitativaResult.cs` | Eliminado "Stub mínimo fase red." del summary. Agregado descripción útil del campo `HallazgoGeneradoId` directamente en el summary del record (en lugar de solo en el comentario de propiedad). | 151 pass | 151 pass |
| 3 | cleanup | `RegistrarEvaluacionCualitativa.cs` | Eliminado "stub mínimo fase red. Versión final definida en spec §2." del summary del comando. Reemplazado por descripción canónica con nota sobre `HallazgoId`. | 151 pass | 151 pass |

## Refactors deliberadamente NO aplicados

| # | Sugerido por | Candidato | Motivo para no aplicar |
|---|---|---|---|
| 1 | green-notes §5 + misión | Extraer PRE-3/4/5/6 comunes a `RegistrarMedicion` y `RegistrarEvaluacionCualitativa` a un helper privado del aggregate | Los 4 guards tienen mensajes de texto distintos (mencionan el nombre del comando). Un helper requeriría pasar el nombre como string — parámetro que no agrega valor de abstracción más allá de simplificar 4 líneas similares. La regla: "tres líneas similares es mejor que una abstracción prematura." Con mensajes distintos, la duplicación es superficial, no DRY real. |
| 2 | green-notes §5 + misión | Factory privado para `HallazgoRegistrado_v1` compartido entre `RegistrarMedicion` y `RegistrarEvaluacionCualitativa` | Difieren en 2 campos (`MedicionOrigenId`/`EvaluacionOrigenId` — mutuamente excluyentes) y en `NovedadTecnica` (texto calculado distinto). Un factory necesitaría ≥8 parámetros para ser general, o una lambda para el texto — ambas señales de abstracción especulativa. Descartado. |
| 3 | misión | Guard `ParteEquipoIdAusenteEnSnapshotException` idéntico en ambos métodos | Solo 3 líneas, mensaje idéntico, contexto de uso distinto (rama `fueraDeRango` vs rama `Calificacion==Malo`). No vale la extracción. |
| 4 | green-notes §5 | Header guard duplicado en endpoints | Atañe a todos los endpoints, no solo al slice 1i'. Relacionado con followup #27 (dedup de header). No se resuelve aquí para no expandir el scope. Anotado en FOLLOWUPS. |
| 5 | green-notes §5 | Mapeadores de excepción a `codigoError` en endpoints | Mismo argumento que #4. Scope transversal — followup #27. |

## Verificación

```
dotnet build tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj
→ Compilación correcta. 0 advertencias. 0 errores.

dotnet test tests/Inspecciones.Domain.Tests/Inspecciones.Domain.Tests.csproj --no-build
→ Superado: 151, Omitido: 4, Error: 0
```

## Cobertura del aggregate `Inspeccion` (post-refactor)

Medida con `dotnet test --collect:"XPlat Code Coverage"` sobre `Inspecciones.Domain.Tests`:

| Clase | Lines | Branches |
|---|---|---|
| `Inspeccion` | **98.9 %** | **95.1 %** |
| `EvaluacionCualitativaRegistrada_v1` | 100 % | 100 % |
| `RegistrarEvaluacionCualitativa` (cmd) | 87.5 % | 100 % |
| `ItemNoEsCualitativoException` | 100 % | 100 % |
| `ItemYaEvaluadoException` | 100 % | 100 % |

Umbral requerido ≥ 85 % — cumplido con margen.

## Followups creados

Ninguno nuevo. Los candidatos #4 y #5 ya están cubiertos por el followup #27 existente.
