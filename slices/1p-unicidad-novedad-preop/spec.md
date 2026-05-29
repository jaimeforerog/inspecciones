# Slice 1p — Unicidad de novedad preop en `RegistrarHallazgo` (INV-ND1 + dedupe)

**Autor:** domain-modeler
**Fecha:** 2026-05-29
**Estado:** draft
**Agregado afectado:** `Inspeccion` (aplica solo a `TipoInspeccion.Tecnica` — las novedades preop no existen en monitoreo).
**Tipo:** refinamiento de comando existente (`RegistrarHallazgo`, slice 1c) — añade dos guardas de pre-condición. No introduce eventos ni comandos nuevos. No toca `Apply`.

**Cierra:**
- **FU-40** — `RegistrarHallazgo` no verifica `_novedadesDescartadas` (INV-ND1 asimétrica). Lado simétrico de `Descartar` PRE-6 (slice 1n).
- **Gap 6b** del contrato de la PWA (`importar-preoperacional-hallazgo` §2.3 / requerimientos backend §2.3 punto #8): "que el comando rechace importar dos veces el mismo `novedadPreopOrigenId`".
- **FU-41** — INV-ND1 sin entrada formal en `01-modelo-dominio.md §15.3` (doc, en el mismo PR).

**Decisiones previas relevantes:**
- `slices/1n-descartar-novedad-preop/spec.md §5 + §12 P-3` — propuso INV-ND1 y dejó explícito que el lado `RegistrarHallazgo` quedaba pendiente. Recomendó: "añadir guardia en `Inspeccion.RegistrarHallazgo` cuando `cmd.Origen == PreOperacional && cmd.NovedadPreopOrigenId.HasValue` — si `_novedadesDescartadas.Contains(...)` → excepción simétrica".
- `slices/1c-registrar-hallazgo/spec.md` — `HallazgoRegistrado_v1` con `Origen=PreOperacional` lleva `NovedadPreopOrigenId: int`; es el vínculo novedad→hallazgo.
- `01-modelo-dominio.md §15.3 I-H2 / I-H6` — I-H6 permite múltiples hallazgos sobre la misma `ParteEquipoId`, pero **no** sobre la misma novedad preop (una novedad es un reporte único del operador).
- `Inspeccion.cs` ya expone el estado necesario: `_novedadesDescartadas: HashSet<int>` (slice 1n) y `_hallazgos: List<Hallazgo>` con `Origen` + `NovedadPreopOrigenId` + `Eliminado`. **No requiere nuevos campos en el aggregate.**

---

## 1. Intención

Una novedad de preoperacional es un reporte único del operador. Dentro de una
inspección, esa novedad puede seguir exactamente uno de dos caminos terminales
mutuamente excluyentes: **importarse como hallazgo** (`HallazgoRegistrado_v1` con
`Origen=PreOperacional`) **o descartarse** (`NovedadPreopDescartada_v1`). Nunca
ambos, y nunca el mismo camino dos veces.

El slice 1n ya cerró un lado de la exclusión (no se puede descartar una novedad ya
importada). Faltan los dos lados que viven en `RegistrarHallazgo`:

1. **No importar una novedad ya descartada** (simetría INV-ND1 — FU-40).
2. **No importar dos veces la misma novedad** (dedupe — Gap 6b del front).

Hoy el backend acepta ambos flujos defectuosos (la UI los previene, pero el
aggregate los aceptaría → corrupción silenciosa del read model de novedades y doble
hallazgo sobre el mismo reporte).

---

## 2. Comando

Sin cambios. Reusa `RegistrarHallazgo` (slice 1c). Las guardas se evalúan en el
método de decisión `Inspeccion.RegistrarHallazgo(cmd, ahora)` para `Origen=PreOperacional`.

---

## 3. Evento(s) emitido(s)

Sin cambios. En el camino feliz sigue emitiendo `HallazgoRegistrado_v1` (un evento,
un `SaveChangesAsync`). Este slice solo **añade rechazos**; no añade eventos.

---

## 4. Precondiciones (nuevas)

Se insertan en `RegistrarHallazgo` **después** de PRE-5/I-H2 (que garantiza que un
comando PreOperacional trae `NovedadPreopOrigenId` no nulo) y antes de las
validaciones de completitud de campos (I-H4/PRE-8/PRE-9). Solo aplican a
`Origen=PreOperacional`.

- **PRE-11 / INV-ND1** (novedad no descartada): si `cmd.Origen == PreOperacional` y
  `_novedadesDescartadas.Contains(cmd.NovedadPreopOrigenId.Value)` →
  `NovedadDescartadaNoImportableException`. HTTP **422**, `codigoError = "INV-ND1"`.
- **PRE-12 / I-H13** (novedad no ya importada como hallazgo activo): si
  `cmd.Origen == PreOperacional` y existe un hallazgo **no eliminado** con
  `Origen=PreOperacional` y `NovedadPreopOrigenId == cmd.NovedadPreopOrigenId` →
  `NovedadPreopYaImportadaException`. HTTP **422**, `codigoError = "I-H13"`.

> **Capa:** ambas viven en el método de decisión, nunca en `Apply`. No se añade
> evento ni se modifica `Apply`, por lo que el rebuild es estructuralmente
> inalterado; el test §6.6 lo verifica de todos modos (la guarda debe leer estado
> reconstruido).

---

## 5. Invariantes tocadas

- **INV-ND1** (formalizada en §15.3 en este PR — cierra FU-41): "Una novedad preop NO
  puede estar simultáneamente descartada (`_novedadesDescartadas`) y referenciada como
  `NovedadPreopOrigenId` por un `HallazgoRegistrado_v1` no eliminado". Enforcada por:
  `Descartar` PRE-5 + PRE-6 (slice 1n) **y** `RegistrarHallazgo` PRE-11 (este slice).
- **I-H13** (nueva — formalizada en §15.3): "Una novedad preop puede tener a lo sumo
  **un** hallazgo activo (no eliminado) por inspección". Enforcada por `RegistrarHallazgo`
  PRE-12 (este slice).
- **I-H6** (§15.3, sin cambio): múltiples hallazgos sobre la misma `ParteEquipoId`
  siguen permitidos — I-H13 acota por **novedad**, no por parte. Dos novedades distintas
  sobre la misma parte se importan ambas; la misma novedad no.

---

## 6. Escenarios Given / When / Then

### 6.1 (FU-40 / INV-ND1) Importar una novedad ya descartada → rechazo

**Given** stream con `InspeccionIniciada_v1` (EnEjecucion) + `NovedadPreopDescartada_v1(NovedadId=1042)`.
**When** `RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=1042, ...)`.
**Then** lanza `NovedadDescartadaNoImportableException` ("…1042 ya fue descartada…"); ningún evento; HTTP 422 `INV-ND1`.

### 6.2 (Gap 6b / I-H13) Importar una novedad ya importada (hallazgo activo) → rechazo

**Given** stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1(HallazgoId=G2, Origen=PreOperacional, NovedadPreopOrigenId=1042)` activo.
**When** `RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=1042, HallazgoId=G3, ...)`.
**Then** lanza `NovedadPreopYaImportadaException` ("…1042 ya fue importada como hallazgo activo…"); ningún evento; HTTP 422 `I-H13`.

### 6.3 (decisión D-1) Re-importar tras eliminar el hallazgo previo → permitido

**Given** stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1(G2, PreOperacional, 1042)` + `HallazgoEliminado_v1(G2)` (soft delete).
**When** `RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=1042, HallazgoId=G3, ...)`.
**Then** NO lanza; emite `HallazgoRegistrado_v1(G3)`. La importación previa fue deshecha; re-importar es legítimo.

### 6.4 (alcance) Importar una novedad distinta cuando otra está descartada/importada → permitido

**Given** stream con `InspeccionIniciada_v1` + `NovedadPreopDescartada_v1(1042)` + `HallazgoRegistrado_v1(G2, PreOperacional, 2000)`.
**When** `RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=3000, HallazgoId=G3, ...)`.
**Then** NO lanza; emite `HallazgoRegistrado_v1(G3)`. Las guardas son por-novedad.

### 6.5 (no-regresión I-H6) Dos novedades distintas sobre la misma parte → ambas permitidas

**Given** stream con `InspeccionIniciada_v1` + `HallazgoRegistrado_v1(G2, PreOperacional, novedad=1042, parte=88)`.
**When** `RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=2042, parteEquipoId=88, HallazgoId=G3, ...)`.
**Then** NO lanza (I-H6 intacto: la cota es por novedad, no por parte).

### 6.6 (obligatorio) La guarda lee estado reconstruido desde stream

**Given** se reconstruye el aggregate (`Inspeccion.Reconstruir`) desde `[InspeccionIniciada_v1, NovedadPreopDescartada_v1(1042)]`.
**When** sobre ese aggregate reconstruido se invoca `RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=1042, ...)`.
**Then** lanza `NovedadDescartadaNoImportableException`. Confirma que `_novedadesDescartadas` se rehidrata correctamente y que la guarda no depende de estado in-process.

### 6.7 (no-regresión) Origen=Manual no se ve afectado

**Given** stream con `NovedadPreopDescartada_v1(1042)`.
**When** `RegistrarHallazgo(Origen=Manual, NovedadPreopOrigenId=null, ...)` (happy path manual).
**Then** NO lanza por las guardas nuevas (solo aplican a PreOperacional); el flujo manual sigue gobernado por I-H3.

---

## 7. Idempotencia / retries

Reintento de red (ADR-008 `X-Client-Command-Id`): Wolverine deduplica por `MessageId`
antes de llegar al handler — sin segundo evento. El doble-import **con distinto**
`X-Client-Command-Id` (reintento humano / dos dispositivos) es exactamente lo que
PRE-12 ataca: el segundo recibe 422 `I-H13`. PRE-11/PRE-12 son determinísticas sobre
el estado del stream, por lo que reproyectar no cambia el veredicto.

---

## 8. Impacto en proyecciones / read models

Ninguno. No se emiten eventos nuevos. Las proyecciones existentes
(`DetalleInspeccionView`, `InspeccionResumenView`) no cambian.

---

## 9. Impacto en endpoints HTTP

`POST /api/v1/inspecciones/{id}/hallazgos` (slice 1c) — sin cambio de ruta/contrato
de entrada. Se añaden dos casos al `switch` de mapeo de excepciones del bloque
`catch (InspeccionDomainException ex)` ya existente:

```csharp
NovedadDescartadaNoImportableException => "INV-ND1",
NovedadPreopYaImportadaException       => "I-H13",
```

Ambas heredan el mapeo a `422 Unprocessable Entity` del bloque existente. El front
ya esperaba "422 … YA-IMPORTADA" (Gap 6b); `inspecciones-api-liaison` sincroniza
`api-contract.md §9` con los códigos `INV-ND1` / `I-H13`.

---

## 10. SignalR / push

No aplica. Son rechazos de comando; no hay evento ni notificación.

---

## 11. Adapters Sinco on-prem

No aplica. No hay integración nueva con el ERP.

---

## 12. Preguntas abiertas

- **P-1 (no bloqueante):** ¿el dedupe debe considerar hallazgos **eliminados**?
  Decisión D-1: **no** — re-importar tras eliminar es legítimo (la importación previa
  fue deshecha) y es coherente con `estado=Importada` del endpoint FU-5, que se deriva
  de hallazgos activos. Queda una asimetría conocida con `Descartar` PRE-6 (slice 1n),
  que bloquea descartar aunque el hallazgo de origen esté eliminado; ver D-2.

---

## 13. Decisiones documentadas

| # | Decisión | Valor | Justificación |
|---|---|---|---|
| D-1 | Hallazgos eliminados en el dedupe (PRE-12) | Se ignoran (`!h.Eliminado`) | Re-importar tras soft-delete es UX legítima; coherente con la derivación de `estado=Importada` (FU-5) sobre hallazgos activos. |
| D-2 | Asimetría con `Descartar` PRE-6 | Aceptada, no se toca en este slice | `Descartar` PRE-6 (slice 1n) bloquea descartar si existe cualquier hallazgo de la novedad (incl. eliminado). Alinearla a "solo activos" sería un cambio de comportamiento del 1n fuera del alcance de este slice; se documenta como nota, no como bug. |
| D-3 | Excepciones nuevas vs reusar `NovedadYaDescartadaException` | Nuevas (`NovedadDescartadaNoImportableException`, `NovedadPreopYaImportadaException`) | `NovedadYaDescartadaException` ya tiene semántica "doble descarte" en `Descartar`; reusarla en import confundiría el mensaje y el código de error HTTP. |
| D-4 | Códigos HTTP | `INV-ND1` y `I-H13`, ambos 422 | Consistente con el estilo del switch existente (`I-H2`, `I-H4`, `INV-PartePerteneceAlEquipo`) y con la expectativa del front (422 para "ya importada"). |
| D-5 | Orden de las guardas | Tras I-H2, antes de I-H4/PRE-8/PRE-9 | La identidad/conflicto de la novedad se reporta antes que la completitud de campos; da el mensaje más útil ("ya importada/descartada"). |

---

## 14. Checklist pre-firma

- [x] PRE-11 y PRE-12 mapean a escenarios Then (§6.1, §6.2).
- [x] Camino "permitido" cubierto: re-import tras delete (§6.3), otra novedad (§6.4), I-H6 intacto (§6.5).
- [x] Rebuild-driven guard (§6.6) presente.
- [x] No-regresión Manual (§6.7).
- [x] §8/§10/§11 marcados "no aplica" con justificación.
- [x] INV-ND1 + I-H13 propuestas para §15.3 (cierra FU-41) en el mismo PR.
- [x] Cierra FU-40 y Gap 6b del contrato front.
