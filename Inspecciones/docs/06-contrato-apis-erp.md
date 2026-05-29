# Contrato de APIs del ERP Sinco — fuente consolidada

**Propósito:** lista canónica de todos los endpoints del ERP Sinco (módulos Preoperacional, MYE núcleo, Inventario, User master) que el módulo de Inspecciones Técnicas consume. Reemplaza la enumeración dispersa en:

- `00-investigacion-mercado.md §9.13` (histórica)
- `03-sow-consultor.md §7` (resumen, ahora apunta aquí)
- `roadmap.md Fase 4` (mapeo por equipo dueño, ahora apunta aquí)

A partir de la fecha de este documento, los slices del módulo deben referenciar **este archivo** como contrato. Los anteriores quedan como histórico.

**Última revisión:** 2026-05-19 (§0.8 agregado tras reconciliación bilateral 2026-05-13 + slices erp-1..erp-4 acoplados; §0.1..§0.7 mantienen la verificación swagger 2026-05-16)
**Estado del contrato:** propuesta del módulo, **parcialmente reconciliada con el microservicio ERP Maquinaria_V4** (ver §0). Verificación 2026-05-16 reveló gaps materiales (§0.1..§0.7). Reconciliación bilateral 2026-05-13 + acople real del módulo (§0.8). Lo no reconciliado sigue pendiente de firma cross-team con cada equipo Sinco.

> **Convención de naming "obra" vs "proyecto" (decisión 2026-04-30, followup #4 cerrado):** el módulo de Inspecciones usa internamente el término **`Proyecto`** en su modelo de dominio (`ProyectoId`, `ProyectoLocal`, etc.). El ERP Sinco mantiene **`Obra`** en sus URLs y DTOs (`/api/v1/catalogos/obras`, query param `?obra=`, claim `sinco_obras`). El **adapter del módulo traduce `Proyecto` ↔ `Obra`** al hablar con MYE. Este documento conserva los nombres del ERP (obra) tal como están porque define el contrato del lado ERP. Los nombres del módulo (proyecto) viven en `01-modelo-dominio.md`. Si en el futuro Sinco corporativo estandariza a "proyecto" en sus URLs/DTOs (pregunta abierta para David en doc 07), este documento se actualizará y el adapter eliminará la traducción.

---

## 0. ⚠️ Verificación contra `Maquinaria API v1` (2026-05-16) + reconciliación bilateral 2026-05-13

> **Lectura:** §0.1..§0.7 reflejan el análisis directo del swagger en vivo de Maquinaria_V4 publicado el 2026-05-16. **§0.8** complementa con la reconciliación bilateral 2026-05-13 (acuerdos cross-team) y el estado de los slices Inspecciones (`erp-1..erp-4`) que ya acoplan contra esta API. Si una afirmación de §0.1..§0.7 contradice §0.8, prima la fecha más reciente (2026-05-19 / §0.8) salvo que se trate de un detalle puramente técnico del swagger.

## 0.A Verificación contra `Maquinaria API v1` (2026-05-16)

> **Origen:** el 2026-05-16 se inspeccionó el swagger en vivo de la API real publicada por David en `http://localhost:5289/api/v4/Maquinaria/swagger/v1/swagger.json` (title `Maquinaria API v1`, description "APIs de integración con el módulo SincoMyE del ERP SincoErp"). El análisis reveló que **el contrato propuesto en este documento (§1..§8) divergió de la implementación real** en aspectos materiales. Esta sección consolida los gaps, sin reescribir las secciones detalladas — esas siguen siendo "lo que el módulo necesita". Esta sección dice "lo que el ERP ofrece hoy".

### 0.1 Inventario de lo que la API real expone (11 endpoints)

| Método | Path real (`Maquinaria API v1`) | Notas |
|---|---|---|
| GET | `/api/equipos?filtro=` | Lista equipos visibles por obras del usuario, tope 5000, ETag diario, sentinel `-1` |
| PUT | `/api/equipos/{codigo}/dictamen-vigente` | **Slice 11** del ERP. Acepta dictamen `0=opera, 1=restricción, 2=no opera`. Last-write-wins |
| GET | `/api/partes-equipos?idEquipo=` | Lista partes filtradas, ETag diario, sentinel `-1` |
| GET | `/api/causas-falla?texto=` | Catálogo global `EQV4.FallaCausa`, ETag diario |
| GET | `/api/tipos-falla?texto=` | Catálogo global con `prioridad`, ETag diario |
| GET | `/api/productos?texto=` | Catálogo `EQV4.Productos` con `unidadContable`, ETag diario |
| GET | `/api/rutinas-monitoreo?equipoId=` | Rutinas activas asignadas al equipo (modelo **per-equipo server-side**). **Slice 10** del ERP |
| GET | `/api/rutinas-monitoreo/items?equipoId=&rutinaId=` | Items de una rutina concreta para un equipo. Sin ETag. **Slice 12** del ERP |
| POST | `/api/rutinas-monitoreo/migrar` | Bulk import desde Excel (admin). Soporta `Idempotency-Key` |
| GET | `/api/preoperacional-fallas?desde=&hasta=&equipoId=&texto=` | Lista fallas preop visibles |
| POST | `/api/preoperacional-fallas/cerrar` | Bulk close (`PODIds[]` + `observaciones`). **Slice 9** del ERP |

**Total: 11 endpoints** vs **25 contratados** en §4. Cobertura ~44%, con divergencias semánticas adicionales.

### 0.2 Mapping vs contrato §3

| Endpoint contratado (§3) | Equivalente en API real | Veredicto |
|---|---|---|
| **M-1** `POST /mye/ot-correctivas` | ❌ **No existe** | 🔴 **Bloqueador crítico ADR-007/ADR-003.** Sin endpoint, las sagas de los slices 1k/1l (`GenerarOT`/`RechazarGenerarOT`) emiten `OTSolicitada_v1` sin destino real |
| **M-1b** `POST /mye/ot-correctivas/{id}/adjuntos` | ❌ **No existe** | 🔴 Dependiente de M-1 — sin M-1 no aplica |
| **M-2** `GET /mye/ot-correctivas?inspeccionId=` (fallback) | ❌ **No existe** | 🔴 Fallback inútil sin M-1 |
| **M-W-1** `PUT /equipos/{equipoCodigo}/dictamen-vigente` | ✅ `PUT /api/equipos/{codigo}/dictamen-vigente` | 🟢 **Match exacto** — reclasificar de 🚧 a 🟢. Detalle de divergencia: el path real es `/api/...` (sin `/v1`); el `codigo` es `int32` no string (el contrato asumía string del catálogo MYE) |
| **M-3** `GET /equipos?q=&page=&size=` (selector liviano paginado) | ⚠️ `GET /api/equipos?filtro=` (sin paginación, tope 5000) | 🟡 Trae todo bajo el tope; sin paginación REST estándar. Query param es `filtro`, no `q`. Sentinel `-1` para "trae todo" |
| **M-3b** `GET /equipos/{equipoCodigo}` (detalle consolidado con `partes[]`, `rutinaTecnicaId`, `grupoMantenimientoId`) | ⚠️ Fragmentado: `GET /api/equipos` (lista solamente) + `GET /api/partes-equipos?idEquipo={n}` (separado) | 🟡 **El cliente debe componer 2-3 llamadas.** Y crítico: `EquipoDto` **no trae `rutinaTecnicaId`** — solo `rutinaMantenimientoId` (entidad distinta, ver §0.4 abajo). `grupoMantenimientoId` sí presente ✅ |
| **M-10** `GET /catalogos/causas-falla` | ✅ `GET /api/causas-falla?texto=` | 🟢 Match. Path sin `/catalogos/` |
| **M-11** `GET /catalogos/tipos-falla` | ✅ `GET /api/tipos-falla?texto=` | 🟢 Match. `TipoFallaDto` agrega campo `prioridad: string?` no anticipado |
| **M-13** `GET /catalogos/obras` | ❌ **No existe** | 🔴 No hay endpoint de obras. Implicación: el sync de `ProyectoLocal` (§3.4 M-13) no puede ejecutarse. La visibilidad ya filtra server-side por obras del usuario en cada GET — quizá no se necesita el catálogo plano si las inspecciones derivan `ProyectoId` desde el equipo (FU a explorar). **Notar también:** este endpoint fue marcado como "candidato a eliminar del MVP" en commit `0ffaa8a` (docs M-13). |
| **M-16** `GET /catalogos/rutinas-monitoreo` (catálogo global + cliente filtra por `grupoMantenimientoId`) | ⚠️ `GET /api/rutinas-monitoreo?equipoId=` + `GET /api/rutinas-monitoreo/items?equipoId=&rutinaId=` | 🟡 **Modelo arquitectural distinto.** Maquinaria expone "consulta per-equipo, server resuelve asignación"; el contrato (decisión 2026-05-05) asumía "catálogo global + filtro client-side por grupo". **No existe endpoint que devuelva el catálogo plano con `grupoMantenimientoId` por rutina.** La decisión 2026-05-05 de "asignación derivada por grupo client-side" **no es viable contra esta API tal cual está hoy**. Hay tres opciones: (a) pedir a David un endpoint catálogo plano, (b) cambiar el módulo para consultar online por equipo, (c) cliente itera `GET /rutinas-monitoreo?equipoId=` por cada equipo del usuario y compone catálogo local |
| **M-17** `GET /catalogos/rutinas` (rutinas técnicas — crítico MVP) | ❌ **No existe** | 🔴 **Bloqueador.** El handler `IniciarInspeccion` (slice 1b) no puede resolver `Equipo.RutinaTecnicaId` contra una proyección local — porque ni el campo viene en `EquipoDto` ni hay catálogo de rutinas técnicas. Decisión 2026-05-04 ADR-004 sobre rutinas técnicas queda en el aire |
| **M-8, M-9, M-12, M-14, M-15** | ❌ No existen (estaban ⏸ diferidos) | Esperado — diferidos a post-MVP |
| **P-6** `POST /preop/novedades/descartar` (bulk-first 1..N) | ⚠️ `POST /api/preoperacional-fallas/cerrar` (`PODIds[]` bulk) | 🟡 **Granularidad desalineada.** El slice 1n `DescartarNovedadPreop` descarta una sola a la vez por aggregate; la API solo expone bulk. Saga puede invocar bulk de 1 (correcto pero subóptimo) o agrupar |
| **P-1** `GET /preop/novedades?...` (lista para verificar/importar) | ⚠️ `GET /api/preoperacional-fallas?desde=&hasta=&equipoId=&texto=` | 🟡 Existe equivalente con filtros distintos. `PreoperacionalFallaDto` lleva `arbolDescripcion` + `actividadDescripcion` desnormalizadas pero **no `ActividadId`** — bloquea la correlación con catálogo de actividades. Solo queda `id: int` (PODId) para reabrir/correlacionar |
| **P-2..P-5** (resto de preop) | ❌ No existen en el swagger Maquinaria | 🟠 **Posible que vivan en otra API.** Maquinaria expone solo *consulta y cierre bulk*. La creación de novedad preop (P-1 si es POST), update (P-2), upload de adjuntos (P-3), verificación inline (P-5) no aparecen. Confirmar con David si esos endpoints están en otra API o en otra ruta |
| **I-1, I-2** (Inventario / SKUs) | ❌ No existen en el swagger Maquinaria | 🟠 Probablemente en una API separada del equipo de Inventario. `GET /api/productos` cubre el caso de productos pero no se documentó como SKU/insumo en el contrato |
| **U-1, U-2** | N/A — eliminados 2026-05-05 | ✅ Identidad heredada del host PWA confirmada (la API filtra por `EQV4.UsuariosObra` del JWT) |

### 0.3 Convenciones globales — divergencias

| Convención §1 | Realidad `Maquinaria API v1` | Acción |
|---|---|---|
| §1.1 Prefix `/api/v1/...` obligatorio | Prefix real: `/api/...` (sin `/v1`). El swagger está en `/api/v4/Maquinaria/` pero los endpoints debajo de él son `/api/{recurso}` | Actualizar §1.1 o pedirle a David si va a versionar `/v1`. Hoy el adapter debe usar `/api/{recurso}` |
| §1.3 Paginación con `page`/`size` + headers `X-Total-Count`/`Link` | ❌ No hay paginación. Sentinel `-1` para "trae todo", tope hardcoded 5000 | Cuando un cliente exceda 5000, no hay forma de paginar. Vincula con FU-9 (prefetch-by-proyecto). **Hablar con David sobre paginación real** antes del primer cliente con >5000 equipos |
| §1.4 Idempotencia header `Idempotency-Key` | ✅ Maquinaria usa `Idempotency-Key` (POST migrar) | Match. **Pero**: ADR-008 del módulo usa `X-Client-Command-Id` en el cliente PWA. Decisión: el adapter HTTP traduce `X-Client-Command-Id` → `Idempotency-Key` al cruzar al ERP |
| §1.5 Error envelope `{code, message, details, traceId}` | ⚠️ Real: `{codigo, mensaje}` (sin `details` ni `traceId`) | Adapter debe tolerar envelope minimal. Updateable en §1.5 o pedirle a David que extienda |
| §1.6 Cache ETag | ✅ 5/11 endpoints (los catálogos) lo soportan con revalidación diaria | Match. Endpoints sin ETag: `PUT dictamen`, `POST cerrar`, `POST migrar`, `GET preoperacional-fallas`, `GET rutinas-monitoreo/items` — esperado (writes + endpoints "online puros") |
| §1.7 IDs inmutables | Asumido pero no verificado contra esta API | Pendiente |

### 0.4 Discrepancias semánticas críticas (más allá de paths)

1. **`equipoCodigo` vs `equipoId`.** El contrato usa `<X>Codigo: string` para URLs y `<X>Id: int` para PKs (§1.7). `Maquinaria API v1` colapsa: `equipoId: int32` aparece tanto en queries como en path params (`PUT /api/equipos/{codigo}/dictamen-vigente` toma `codigo: int32`, no string). **La convención de §15.4 del modelo (`<X>Codigo: string`) no aplica a esta API.**
2. **`EquipoDto` rico pero falta `rutinaTecnicaId`.** 17 campos incluyen placa, M1/M2 lecturas, sucursal, obraEquipo vs obraProyecto, `grupoMantenimientoId` ✅, `rutinaMantenimientoId`. **No hay `rutinaTecnicaId`**, y `rutinaMantenimientoId` NO es lo mismo que rutina técnica de inspección — riesgo de confusión semántica.
3. **`ItemRutinaMonitoreoDto` sin discriminador `tipoEvaluacion`.** El modelo Inspecciones (§12.11.5) espera `EvaluacionEsperada ∈ {Medicion(min,max,UM), Cualitativa(calificaciones[])}`. El DTO real trae `ridValorMin/Max` (nullable), `ridUM`, `ridAplicaEstado: bool`, `riCodigoCalidad`. **El cliente debe inferir el tipo** del shape devuelto (¿`ridValorMin/Max` no nulos → Medicion?, ¿`ridAplicaEstado=true` → Cualitativa?). No es trivial.
4. **`PreoperacionalFallaDto` sin `ActividadId`.** Solo trae `actividadDescripcion: string` desnormalizado. Para reabrir/correlacionar contra catálogo de actividades, hay que parsear strings o usar solo `id: int` (PODId).
5. **`RutinaMonitoreoDto` mínimo** (solo `codigo: int32 + descripcion`) cuando el contrato esperaba items embebidos por rutina. Para obtener items hay que hacer una segunda llamada a `/api/rutinas-monitoreo/items?equipoId=&rutinaId=`.

### 0.5 Hallazgo positivo

`PUT /api/equipos/{codigo}/dictamen-vigente` (M-W-1 del contrato) **existe y funciona**. Esto desbloquea un slice de integración de bajo riesgo: **saga `PublicarDictamenAlERP` reactiva a `InspeccionFirmada_v1`**. Cierra el loop firma → ERP sin esperar a la saga OT. Es candidato natural al próximo slice del módulo, antes de seguir cerrando comandos del aggregate.

### 0.6 Resumen de gaps por severidad

| Severidad | Cuenta | Endpoints/conceptos |
|---|---|---|
| 🔴 Bloqueante | 4 | M-1 (OT), M-1b (adjuntos OT), M-2 (fallback OT), M-17 (rutinas técnicas) |
| 🟠 Probable-vive-en-otra-API | 6 | P-2..P-5, I-1, I-2 |
| 🟡 Existe con divergencia material | 6 | M-3, M-3b, M-16, P-1, P-6, M-13 |
| 🟢 Match limpio | 4 | M-W-1, M-10, M-11, identidad heredada |
| ✅ Diferidos correctamente | 7 | M-5..M-9, M-12, M-14, M-15 |

### 0.7 Decisiones a destrabar con David (urgente — antes del próximo slice de integración)

1. **¿Dónde se crean las OTs correctivas?** No están en `Maquinaria API v1`. ¿Otra API del MYE núcleo? ¿Está planeada? Sin esta respuesta, ADR-007 es ficción.
2. **¿Cómo obtiene el módulo las rutinas técnicas asignadas al equipo?** `EquipoDto` no las trae y M-17 no existe. Sin endpoint, el flujo técnico (§12.11.1, slices 1a-1g en main) no puede arrancar rutina automáticamente.
3. **¿Se puede agregar `rutinaTecnicaId` al `EquipoDto`, o exponer un detalle por equipo?** El `GET /api/equipos` actual lista; falta el detalle (M-3b).
4. **Rutinas-monitoreo: ¿modelo per-equipo (Maquinaria) o catálogo plano (decisión Inspecciones 2026-05-05)?** Son dos arquitecturas client-side incompatibles. Si queda como Maquinaria, el sync on-app-open de `RutinaMonitoreoLocal` no aplica — pasa a ser online por equipo, y `ItemsSnapshot` se construye desde respuesta runtime.
5. **¿P-2..P-5 (creación/update/adjuntos/verificación de novedad preop) viven en otra API o no existen?** Sin ellos, el flujo preop del módulo se queda en "consulta y cierre bulk".
6. **¿I-1/I-2 (Inventario/SKUs) viven en otra API?** `GET /api/productos` cubre productos pero no se documentó como SKU/insumo del contrato.
7. **¿Versionado `/v1` planeado o se queda `/api/{recurso}`?** Define qué prefix codifica el adapter.
8. **¿Paginación REST estándar planeada antes de exceder 5000 equipos/registros?** Hoy hay tope hardcoded.
## 0.B Reconciliación bilateral 2026-05-13 + slices Inspecciones acoplados

> **Fecha:** 2026-05-19. Complementa §0.A (verificación 2026-05-16 contra el swagger en vivo). Mientras §0.A documenta el **gap técnico** detectado por inspección directa de la API, esta §0.B documenta los **acuerdos cross-team de 2026-05-13** (qué endpoints quedan descartados bilateralmente vs cuáles requieren adaptación) y el **estado de acople real del módulo** tras cerrar los slices `erp-1..erp-4`.

**Fuente de la reconciliación:** `Maquinaria_V4/docs/endpoints-faltantes-inspecciones.md` (proyecto hermano `C:\Fuentes\FuentesNET3.0\AzureV4\Maquinaria_V4`).

### 0.B.1 Decisiones cross-team que cierran los gaps de §0.A

Algunos gaps de §0.A no se cierran agregando endpoints sino descartando bilateralmente:

- **M-2 (`GET /mye/ot-correctivas?inspeccionId=`):** descartado bilateralmente — el ERP no almacena `inspeccionId`, así que no puede indexar por ese campo. Implicación: el fallback ADR-003 queda inaplicable y M-1 debe cumplir idempotencia real estricta (no hay "consultar antes de crear").
- **M-3b (detalle consolidado de equipo):** descartado bilateralmente — la UI cubre el caso con `M-3 + M-5 + M-16` por separado.
- **M-13 (`GET /catalogos/obras`):** descartado bilateralmente — el catálogo lo gestiona el host PWA aparte, el módulo no lo sincroniza.
- **M-9 / I-2 / P-2..P-5:** NO aplican al módulo — viven en otro punto del ecosistema (M-9, I-2) o son cubiertos por endpoints bulk (P-5 cubierto por P-6, P-2 por shape completo de P-1) o redirigidos al **Document Service externo** (P-3, P-4, M-1b).
- **M-17 (rutinas técnicas globales):** no requiere endpoint dedicado. Workaround acordado: el adapter sintetiza el catálogo desde `EquipoErpDto.RutinaMantenimientoId` que viene en M-3, dentro de `SincronizarEquipoDesdeErpHandler`.

Tras estos descartes, los **bloqueadores reales** de §0.A se reducen a uno: **M-1** (slice 8 de Maquinaria_V4 pausado por DDL DBA).

### 0.B.2 Mapa final tras reconciliación

| Categoría | Cuenta | Endpoints |
|---|---|---|
| ✅/⚠️ Acoplables (cubiertos por adapter erp-1..erp-4) | 9 | `P-1, P-6, M-3, M-5, M-7, M-8, M-16, M-W-1`, + `M-17 (sintetizado)`, + `M-4/I-1 (productos)` |
| ❌ NO aplica (con razón documentada) | 8 | `M-1b, M-9, I-2, P-2, P-3, P-4, P-5` + 1 categoría diferida histórica |
| ❌ Descartado bilateral | 3 | `M-2, M-3b, M-13` |
| ❌ Bloqueante real | 1 | `M-1` (DDL DBA pendiente) |

### 0.B.3 Slices Inspecciones que ya acoplan

| Slice | Commit | Comando / responsabilidad | Endpoints Maquinaria_V4 consumidos |
|---|---|---|---|
| **erp-1** | `4c2ef4e` | Adapter base (HTTP client tipado `IMaquinariaErpClient`, retry, ETag, error envelope, 11 DTOs espejo, 14 tests WireMock) | — (infra) |
| **erp-2** | `63082fa` | `DescartarNovedadPreop` con outbox Wolverine → `P-6`. Idempotencia natural por PODId (`200 yaCerradas` / `409 YA_CERRADO` = éxito silencioso). Política ADR-006: 5xx retry 5s→30s→2m→10m, 4xx + `ArgumentException` → dead-letter inmediato. 11 tests. | `POST /api/preoperacional-fallas/cerrar` |
| **erp-3** | `28de25b` | `SincronizarDictamenVigenteListener` reactivo a `InspeccionFirmada_v1` → `M-W-1`. Mapeo `PuedeOperar→0`, `ConRestriccion→1`, `NoPuedeOperar→2`. Puerto `IInspeccionReader` + `MartenInspeccionReader` (`AggregateStreamAsync`). 11 tests con `FakeInspeccionReader`. | `PUT /api/equipos/{codigo}/dictamen-vigente` |
| **erp-4** | `fb44741` | Endpoint `POST /api/v1/catalogos/sync` (ADR-004 canonical, sin cron). Wipe-and-replace de 3 catálogos globales puros: `causas-falla`, `tipos-falla`, `productos`. ETag por catálogo en document Marten `CatalogoSyncState`. `If-None-Match` → cache local intacto si `304`. Body vacío → `"vaciado-sospechoso"`, cache intacto. Partial-failure por catálogo. Atomicidad cross-catálogo via `LightweightSession` propia por catálogo (`MartenCatalogoSyncRepository` recibe `IDocumentStore`). 23 tests con `FakeCatalogoSyncRepository`. | `GET /api/causas-falla`, `GET /api/tipos-falla`, `GET /api/productos` |

Slices `erp-5..erp-N` (consumo de productos para BOM, sync monitoreo per-equipo, integración OT correctiva M-1) quedan en backlog hasta que (a) emerja el comando del módulo que los requiere y (b) se desbloquee M-1.

### 0.B.4 Decisiones que aún requieren input cross-team (D-3..D-5 del análisis erp-1)

Subset de §0.A.7 que no se cerró bilateralmente. Estado actualizado 2026-05-21:

| Decisión | Pregunta abierta | Owner | Estado | Slices bloqueados | Notas / próximo paso |
|---|---|---|---|---|---|
| **D-3** Document Service externo | URL base + contrato + identidad. ¿Reusa JWT del host o tiene credencial propia? ¿Tiering Cool tras 90 días confirmado? | David (ERP) | 🔴 Abierto | `AdjuntarArchivo` (slice ~3.11). Toda la línea de adjuntos de hallazgos + PDFs de inspección (presupuesto ≥1 TB / 7 años retención, roadmap §1.10). | Necesita reunión cross-team David + IT. Sin esto el slice 3.11 no puede arrancar. Probablemente Fase 2. |
| **D-4** Matriz parte ↔ producto | `M-4` no expone `ParteIdsCompatibles`. ¿Dónde vive la matriz parte↔producto (compatibilidad de SKUs por parte de equipo)? ¿Endpoint dedicado en `Maquinaria_V4` o tabla cliente-side? | David (ERP) | 🟢 **Cerrada — descartada (2026-05)** | `AsignarRepuesto` (slice 1f) ya **no valida** compatibilidad. | **Decisión de negocio:** no hay limitante sobre qué insumo se gasta en un hallazgo — el módulo acepta cualquier SKU del catálogo para cualquier parte (a propósito). Se retiró el hard-error PRE-H2 del handler (ver `01-modelo-dominio.md §12.10.12` + `05-catalogo-eventos.md`). No se requiere matriz parte↔producto del ERP. Si en el futuro se quisiera una validación opcional, reabrir. |
| **D-5** M-17 política definitiva | ¿Basta con sintetizar el catálogo desde `EquipoErpDto.RutinaMantenimientoId` o se necesita endpoint dedicado `GET /catalogos/rutinas-tecnicas`? | Jaime (Módulo) | 🟢 **Cerrada bilateral 2026-05-13** — síntesis cliente-side desde equipo. Implementada en `SincronizarEquipoDesdeErpHandler` (slice erp-1). | Ninguno (resuelta). | Mantener entrada por trazabilidad histórica. Si emerge segundo cliente que requiera el endpoint dedicado, reabrir. |

**Resumen del impacto en roadmap:** D-3 y D-4 son cross-team con David y necesitan agendar reunión antes de iniciar Fase 2 (adjuntos + repuestos con verificación). D-5 está cerrada y sólo se conserva por trazabilidad.

### 0.B.5 Convenciones diferenciadas (canónica vs real)

| Aspecto | Canónica (§1) | Maquinaria_V4 real |
|---|---|---|
| Path base | `/api/v1/...` | `/api/v4/Maquinaria/api/...` |
| Versionado | URL (v1, v2) | URL (v4 a nivel gateway) |
| Auth | JWT del host vía `Authorization: Bearer` + capability | JWT con 5 claims validado por `MiddlewareAuthorizationToken` (`SincoSoft.MYE.Common 1.5.3`); claims: `UsuarioId, NomUsuario, IdEmpresa, IdSucursal, IdProyecto` |
| Idempotency | Header `Idempotency-Key`, ventana ≥30 días | Por confirmar slice por slice — slice 9 (P-6) sí soporta; M-1 pendiente |
| ETag / `If-None-Match` | Obligatorio en catálogos | Implementado en slices 2/3/5/6; ausente en slice 7 (preoperacional-fallas) |
| Error envelope | `{ code, message, details, traceId }` | `{ codigo, mensaje }` minimal; adapter normaliza al shape canónico |

---

## 1. Convenciones globales

Aplican a **todos** los endpoints listados aquí, salvo que se indique excepción explícita.

### 1.1 Path prefix y versionado

- Prefix obligatorio: `/api/v1/...`. El versionado va en URL.
- Cuando emerja v2 de un endpoint, conviven `/api/v1/...` y `/api/v2/...` durante ventana de migración (mínimo 6 meses).

### 1.2 Autenticación

- El cliente móvil llega autenticado por el host PWA Sinco MYE — propaga el token en `Authorization: Bearer {jwt}`.
- Cada API valida el token contra el IdP que se acuerde en ADR-002 (tentativo).
- El módulo no asume perfiles ERP fijos: razona por **capabilities** (verbos como `ejecutar-inspeccion`, `auditar-inspecciones`). Ver roadmap paso 2.5.

### 1.3 Paginación

- Aplica a todos los `GET` que devuelven listas.
- Headers estándar: `X-Total-Count`, `X-Page`, `X-Page-Size`, `Link` (RFC 5988).
- Query params: `page` (1-based), `size` (default 50, máx 200).

### 1.4 Idempotencia

- Todos los `POST` no-naturalmente-idempotentes deben aceptar `Idempotency-Key` como header.
- **Idempotencia real, no solo aceptación del header**: ante una segunda llamada con la misma key, el endpoint devuelve `200 OK` con el **mismo body** de la primera respuesta exitosa, **sin** crear el recurso de nuevo y **sin** devolver `409 Conflict`. El mapeo `key → respuesta` sobrevive a reinicios y dura **≥30 días**.
- Aplica con prioridad a `POST /api/v1/mye/ot-correctivas` (compromiso vinculante para el equipo MYE — ver ADR-003 §13).

### 1.5 Error envelope

Forma esperada de respuestas 4xx/5xx (a confirmar con cada equipo):

```json
{
  "code": "string",
  "message": "string",
  "details": { ... opcional ... },
  "traceId": "string"
}
```

### 1.6 Cache (catálogos)

- `GET` de catálogos sincronizados (ADR-004 canonical 2026-05-05: sync on-app-open, sin cron) debe soportar `ETag` (`If-None-Match`) y devolver `304 Not Modified` cuando aplique. Ver ADR-004.

### 1.7 Convenciones de IDs

- Catálogos sincronizados: IDs/códigos **inmutables**. Renombrar = cambia descripción, no ID. Descontinuar = `activo=false`, no delete (ADR-004).

### 1.8 Resiliencia: outbox + retry desde el módulo

> **Fuente canónica:** ADR-006 — Resiliencia y outbox para integraciones ERP (`01-modelo-dominio.md §16`). Esta sub-sección es resumen para lectores del contrato.

**Regla general**: ningún `POST` al ERP se ejecuta sincrónicamente desde un handler HTTP. Todos pasan por **Wolverine outbox** persistido en PostgreSQL, con retry exponencial (5s → 30s → 2m → 10m → dead-letter).

**Por qué importa para el contrato (lado ERP)**:

- **Idempotencia real obligatoria** (§1.4): el mismo `Idempotency-Key` puede llegar varias veces — primera por intento original, las siguientes por reintentos de Wolverine. El ERP debe retornar la misma respuesta a todas, sin crear duplicados.
- **Volúmenes esperados**: en operación normal ~1 llamada por evento; en degradación hasta 5 llamadas con la misma key durante una hora. Dimensionar capacidad acorde.
- **Latencia tolerable**: hasta ~30s por llamada antes de timeout y reintentar.

**Endpoints sujetos al patrón** (vigente al 2026-04-29):

| Endpoint | Slice consumidor |
|---|---|
| P-5 `POST /preop/novedades/{id}/verificar` | Adapter del comando `RegistrarHallazgo` cuando `Origen=PreOperacional` — al asignar la novedad, **no** desde la saga de cierre (§3.1 detalle P-5 + §15.9 modelo) |
| P-6 `POST /preop/novedades/descartar` (bulk-first, 1..N — decisión 2026-04-30) | Adapter del comando `DescartarNovedadesPreop` (paso 3.29) |
| M-1 `POST /mye/ot-correctivas` | Saga (paso 3.27) |
| M-1b `POST /mye/ot-correctivas/{id}/adjuntos` (multipart, decisión 2026-04-30) | `EjecutarOTSaga` (paso 3.27d) tras éxito de M-1 |

**No aplica a `GET`** (lectura sincrónica). Detalle completo del patrón, atomicidad outbox + stream, observabilidad, métricas y alertas en ADR-006.

---

## 2. Estados (leyenda)

| Estado | Significado |
|---|---|
| 🚧 | Bloqueado — endpoint no existe aún en el ERP, equipo Sinco no se ha comprometido |
| 🟡 | Mock-only — el módulo trabaja contra mock (WireMock) hasta que el endpoint real esté disponible |
| 🟢 | Disponible — endpoint existe en el ERP, validado funcionalmente |
| 🟣 | Condicional — requerido solo si se cumple cierta condición (ej. fallback ADR-003) |
| ⏸ | Diferido — fuera de MVP, se reactiva cuando emerja la condición que lo requiere (ej. tipo "Monitoreo") |

---

## 3. Endpoints por módulo dueño

### 3.1 Preoperacional (equipo del preop)

> **Backend del preop:** **SQL Server relacional on-prem.** Sin event store, sin event sourcing. La idempotencia real exigida en §1.4 para P-5 y P-6 se implementa del lado preop con una tabla `idempotency_key → (response_status, response_body, expires_at)` consultada antes de procesar el comando — **no es comportamiento automático como en Marten/event-store**, requiere desarrollo explícito en el preop. Confirmado decisión 2026-05-04.

| # | Método | Path | Estado | Slice consumidor | Roadmap |
|---|---|---|---|---|---|
| P-1 | GET | `/api/v1/preop/novedades?q=&page=&size=` | 🚧 | Pantalla 2 (importar novedades) | §4.1 |
| P-2 | GET | `/api/v1/preop/novedades/{id}` | 🚧 | Detalle de novedad cuando el técnico la expande | §4.1 (implícito) |
| P-3 | GET | `/api/v1/preop/novedades/{id}/adjuntos` | 🚧 | Lista metadata de adjuntos de la novedad | §4.1 (implícito) |
| P-4 | GET | `/api/v1/preop/adjuntos/{id}` | 🚧 | Descarga binario de un adjunto específico | §4.1 (implícito) |
| P-5 | POST | `/api/v1/preop/novedades/{id}/verificar` | 🚧 | Adapter del comando `RegistrarHallazgo` con `Origen=PreOperacional` — al asignar la novedad (no al firmar) | §4.1 + adapter Preop |
| P-6 | POST | `/api/v1/preop/novedades/descartar` | 🚧 | Adapter de `DescartarNovedadesPreop` (1..N novedades en JSON, decisión 2026-04-30) | §4.3 + §3.29 |

#### P-1 `GET /api/v1/preop/novedades`

> **Estado 2026-05-13:** ⚠️ Alineado con shape distinto — Maquinaria_V4 slice 7 expone `GET /api/preoperacional-fallas?desde&hasta&equipoId&texto` (path real `/api/v4/Maquinaria/api/preoperacional-fallas`). **Sin ETag** (a diferencia de los otros slices de lectura). Adapter del módulo mapea query params (`q` → `texto`) y normaliza shape.

Lista viva de novedades **pendientes** (no snapshot). Consultada cuando el técnico abre el flujo "Importar novedades". El endpoint solo devuelve novedades en estado pendiente — los otros estados (verificada, descartada) no son consultables vía este endpoint en MVP.

- **Query params**:
  - `q` (opcional): búsqueda libre — el ERP busca el texto en código, id y descripción del equipo. Sirve para autocomplete y filtro rápido.
  - `page` (1-based, default 1), `size` (default 50, máx 200).
- **Sin filtro explícito de `obra`**: el ERP deriva las obras autorizadas del JWT (capability `ejecutar-inspeccion` sobre obras donde el usuario tiene permiso). Row-level security server-side. El cliente no puede pedir novedades de obras donde no tiene acceso.
- **Sin filtro explícito de `estado`**: el endpoint siempre devuelve solo pendientes. Decisión dura del lado del ERP.
- **Response**: array de novedades. Por cada novedad: `id`, `equipoId` (string código), `equipoDescripcion` (denormalizado), `equipoGrupo`, `obraId`, `obraDescripcion`, `parteId`, `parteDescripcion`, `operadorId` (username), `operadorNombre`, `reportadaEn` (iso-8601), `descripcion` (corta), `tieneAdjuntos: bool`, `cantidadAdjuntos: int`.
- **Sin `adjuntos[]` ni metadata pesada en la lista**: el cliente solo sabe si la novedad **tiene** adjuntos y cuántos. La metadata completa de adjuntos se trae con P-3 únicamente cuando el técnico expande la novedad (algunas novedades nunca se procesan — no vale la pena cargarlas).
- **Headers de paginación obligatorios**: `X-Total-Count`, `X-Page`, `X-Page-Size`.
- **Auth**: capability `ejecutar-inspeccion`.
- **Notas**: la lista cambia entre llamadas (otros operadores pueden haber agregado novedades). El UI refresca al volver a la pantalla.

#### P-2 `GET /api/v1/preop/novedades/{id}`

> **Estado 2026-05-13:** ❌ NO aplica — P-1 (`GET /api/preoperacional-fallas`) ya devuelve el shape completo de cada novedad; no se requiere endpoint de detalle separado. Maquinaria_V4 no implementa este endpoint y el módulo no lo necesita.

Detalle textual de una novedad. Se invoca cuando el técnico expande una novedad de la lista P-1. **No incluye contenido ni metadata de adjuntos** (eso es responsabilidad de P-3).

- **Path param**: `{id}` = `int` (PK de la novedad) obtenido de P-1.
- **Auth**: capability `ejecutar-inspeccion`. El ERP valida que la novedad pertenezca a una obra accesible al usuario; si no, devuelve `404` (mismo código que "no existe" — no revela existencia).
- **Response 200**:
  ```json
  {
    "id": 9001,
    "equipoId": 1234,
    "equipoCodigo": "D11T-001",
    "equipoDescripcion": "Caterpillar D11T Bulldozer",
    "equipoGrupo": "MAQ-PESADA",
    "obraId": 5678,
    "obraCodigo": "OB-2026-CALI-001",
    "obraDescripcion": "Vía Cali-Buenaventura tramo 3",
    "parteId": 12,
    "parteCodigo": "HIDR-BOMBA",
    "parteDescripcion": "Sistema hidráulico — bomba principal",
    "operadorId": "joperalta",
    "operadorNombre": "Juan Peralta",
    "reportadaEn": "2026-04-28T06:45:23-05:00",
    "medidores": [
      { "numero": 1, "unidad": "horas", "valor": 4287.5 },
      { "numero": 2, "unidad": "kilometros", "valor": 32500.0 }
    ],
    "descripcion": "Detecté goteo de aceite en la bomba hidráulica al iniciar turno...",
    "observaciones": "Llené el reservorio con 2 L antes de continuar la jornada...",
    "estado": "pendiente",
    "rutinaPreopId": 401,
    "itemRutinaId": 4012,
    "tieneAdjuntos": true,
    "cantidadAdjuntos": 2
  }
  ```
- **Mapping al ERP** (la API usa nombres limpios; el equipo del preop hace el mapping internamente):
  | Campo API | Campo ERP |
  |---|---|
  | `descripcion` | `PODActividad` |
  | `observaciones` | `POObservaciones` |
  | `medidores[i].valor` | `POMedidor{i}Final` |
  | `medidores[i].unidad` | derivada de la configuración del equipo (catálogo de unidades del ERP — ver M-15) |
  | `itemRutinaId` | id de la actividad de la rutina preoperacional |
- **Notas sobre `medidores`**:
  - Array de 0, 1 o 2 elementos según cuántos medidores tenga el equipo configurado en MYE núcleo.
  - `unidad` es un código del catálogo cerrado de unidades del ERP (ver M-15 — `horas`, `kilometros`, `m3`, `ciclos`, etc.). Catálogo extensible; cambia poco. Sincronizado on-app-open como los demás catálogos (ADR-004 canonical 2026-05-05).
- **Errors**:
  - `404 Not Found` — la novedad no existe O no es accesible al usuario.
  - `401 Unauthorized` / `403 Forbidden` — token inválido o capability ausente.

#### P-3 `GET /api/v1/preop/novedades/{id}/adjuntos`

> **Estado 2026-05-13:** ❌ NO aplica — los adjuntos de novedades preoperacionales viven en un **Document Service externo** al ERP. Maquinaria_V4 no expone metadata de adjuntos. Cuando el módulo requiera adjuntos, consumirá el Document Service directamente (fuera de este contrato).

Lista metadata de adjuntos de una novedad. **Endpoint barato** que se invoca solo cuando el técnico decide procesar una novedad (verificar / seguimiento / descartar con evidencia visual). Si nunca se invoca P-3, el ERP no carga metadata pesada — patrón lazy.

- **Path param**: `{id}` = `int` (PK de la novedad).
- **Auth**: capability `ejecutar-inspeccion`. ERP valida acceso a la obra (404 si no).
- **Response 200**:
  ```json
  {
    "novedadId": 9001,
    "adjuntos": [
      {
        "id": 70001,
        "tipo": "foto",
        "mime": "image/jpeg",
        "tamano": 2458920,
        "urlPreview": "https://cdn.sinco.local/preop/preview/70001.jpg",
        "subidoEn": "2026-04-28T06:46:01-05:00"
      },
      {
        "id": 70002,
        "tipo": "foto",
        "mime": "image/jpeg",
        "tamano": 1987234,
        "urlPreview": "https://cdn.sinco.local/preop/preview/70002.jpg",
        "subidoEn": "2026-04-28T06:46:34-05:00"
      }
    ]
  }
  ```
- **`urlPreview`**: thumbnail comprimido (~50KB) servido por CDN del ERP. El binario completo se baja con P-4.
- **Si la novedad no tiene adjuntos**: `200 OK` con `adjuntos: []`.

#### P-4 `GET /api/v1/preop/adjuntos/{id}`

> **Estado 2026-05-13:** ❌ NO aplica — Document Service externo (mismo razonamiento que P-3). Maquinaria_V4 no sirve binarios de adjuntos preoperacionales.

Descarga el contenido binario de un adjunto específico (foto en resolución completa, PDF, etc.). Se invoca cuando el técnico abre un adjunto a pantalla completa.

- **Path param**: `{id}` = `int` (PK del adjunto) obtenido de P-3.
- **Auth**: capability `ejecutar-inspeccion`. ERP valida acceso a la obra de la novedad parent.
- **Response 200**: binario con `Content-Type` apropiado (`image/jpeg`, `application/pdf`, etc.) y `Content-Disposition: inline`.
- **Cache**: el ERP debe servir con `Cache-Control: private, max-age=86400` y `ETag` para que el cliente cachee el contenido (los adjuntos no cambian — son inmutables una vez subidos). Cliente puede revalidar con `If-None-Match` y recibir `304 Not Modified`.
- **Topes del preop** (lado upload, documentados aquí para que el cliente los anticipe):
  - Máximo **5 adjuntos** por novedad → `tamano` del array en P-3 nunca excede 5.
  - Máximo **4 MB** por adjunto → `Content-Length` en P-4 nunca excede ~4 194 304 bytes.
- **Errors**: `404` si no existe o no es accesible.

#### P-5 `POST /api/v1/preop/novedades/{id}/verificar`

> **Estado 2026-05-13:** ❌ NO aplica — Maquinaria_V4 no expone un endpoint unitario de verificación. El flujo bulk-first de P-6 (`POST /api/preoperacional-fallas/cerrar`) cubre el caso enviando un array de 1 novedad. Adapter del módulo siempre usa P-6 (sea individual o bulk).

Marca la novedad como verificada por la inspección técnica. Cierra el ciclo del operador respecto a esa novedad.

- **Cuándo se invoca**: cuando el técnico **asigna** la novedad a su inspección (clic en "Verificar" del wizard o "Seguimiento" del mini-modal en variante B). **No** al firmar la inspección. La asignación dispara el outbox; la firma no necesita re-emitir.
- **Idempotency-Key**: `{inspeccionId}-{novedadId}`. Ventana ≥30 días.
- **Resiliencia**: invocado vía Wolverine outbox + retry exponencial (ADR-006 + §1.8). El ERP puede recibir la misma key hasta ~5 veces durante una hora en degradación.
- **Auth**: capability `ejecutar-inspeccion`.
- **Body**:
  ```json
  {
    "inspeccionId": "5e7c9a31-4b2d-4f8a-9c1e-3d5e7f9a1b3c",
    "accionRequerida": "RequiereIntervencion",
    "novedadTecnica": "Confirmado: holgura en valvulería del cilindro de levante. Requiere ajuste de torque y posible cambio de empaque.",
    "asignadoPor": "rmartinez"
  }
  ```
- **Restricciones del body**:
  - `accionRequerida ∈ {"RequiereIntervencion", "RequiereSeguimiento"}` — únicos valores válidos. `NoRequiereIntervencion` no aplica aquí (eso es descarte → P-6).
  - `novedadTecnica`: texto plano libre, máximo **4000 caracteres**, sin markdown ni HTML.
  - `asignadoPor`: username del técnico (string opaco; el ERP no valida nombre real).
  - **Sin `verificadaEn` en el body**: el ERP genera el timestamp al recibir la solicitud (autoridad única de tiempo).
- **Response 200 OK**:
  ```json
  {
    "novedadId": 9001,
    "estado": "verificada",
    "inspeccionId": "5e7c9a31-4b2d-4f8a-9c1e-3d5e7f9a1b3c",
    "accionRequerida": "RequiereIntervencion",
    "verificadaEn": "2026-04-29T14:32:11-05:00"
  }
  ```
- **Errors**:
  - `409 Conflict` si la novedad ya fue verificada/descartada por OTRA inspección (no es replay del mismo cliente — es colisión real entre técnicos).
  - `404 Not Found` si la novedad no existe o la obra está fuera del scope del usuario.
  - `400 Bad Request` si `accionRequerida` es inválido o `novedadTecnica` excede 4000 chars.
- **⚠️ Irreversibilidad**: la verificación es **vinculante**. Si la inspección se cancela posteriormente (`InspeccionCancelada_v1`), la novedad **queda como verificada** en el ERP con la `inspeccionId` cancelada como referencia. **No hay endpoint de revert**. Decisión documentada como invariante: una vez asignada, la novedad pertenece a la inspección que la asignó (vivos o cancelada).

#### P-6 `POST /api/v1/preop/novedades/descartar` (bulk-capable, 1..N en JSON)

> **Estado 2026-05-13:** ✅ Alineado — Maquinaria_V4 slice 9 expone `POST /api/preoperacional-fallas/cerrar` (path real `/api/v4/Maquinaria/api/preoperacional-fallas/cerrar`) con semántica bulk-first 1..N, consistente con la decisión 2026-04-30. **Acoplado en Inspecciones slice erp-2** (adapter `DescartarNovedadPreop` con outbox Wolverine). Adapter normaliza el body y mapea el response al shape canónico.

> **Decisión final 2026-04-30:** path `/preop/novedades/descartar` (sin id en path) acordado con David soporta arrays de 1 a N novedades. **El módulo solo emite arrays de 1** en MVP (descarte rápido individual con motivo autogenerado, ver §15.9 del modelo). El contrato bulk se mantiene por flexibilidad futura (sagas de limpieza, reusa del endpoint sin nuevo trabajo cross-team). **🚧 Confirmar path final con David** en `07-preguntas-destrabar-followups.md`.

Cierra **una o varias novedades** como **descartadas** por el técnico (decisión de gobernanza — el técnico contradice al operador). Disparado por el comando individual `DescartarNovedadPreop` (§15.9 del modelo) que emite UN evento `NovedadPreopDescartada_v1` y una sola llamada a este endpoint con array de 1 elemento.

- **Cuándo se invoca**: al tocar el icono "ojo tachado" en la lista de novedades importables (image12 del mock del diseño). **Sin modal** — motivo es autogenerado del lado del módulo. **No** al firmar la inspección.
- **Idempotency-Key**: `{inspeccionId}-{hash(novedadIds ordenados)}`. Ventana ≥30 días. Replay con misma key + mismo body devuelve mismo `200 OK` con mismas `descartadaEn` por novedad. **🚧 Confirmar con David** la forma exacta de la key (puede ser un comandoId UUID generado por el módulo, más simple).
- **Resiliencia**: invocado vía Wolverine outbox + retry exponencial (ADR-006 + §1.8).
- **Auth**: capability `ejecutar-inspeccion`.
- **Body**:
  ```json
  {
    "inspeccionId": "5e7c9a31-4b2d-4f8a-9c1e-3d5e7f9a1b3c",
    "novedadIds": [9001, 9002],
    "motivo": "Repetidas de la novedad MOTOR-VALV (HD-001) ya verificada en esta inspección. Falsa alarma replicada por el operador en turnos consecutivos.",
    "descartadaPor": "rmartinez"
  }
  ```
- **Restricciones del body**:
  - `novedadIds`: array no vacío de `int` (PKs), sin duplicados, todas pertenecientes a `inspeccionId`. **El módulo MVP siempre envía arrays de 1**. Capacidad bulk preservada en el contrato del ERP por flexibilidad futura (máx **🚧 N a confirmar con David** — sugerencia 100).
  - `motivo`: **obligatorio**, texto plano libre, máximo **4000 caracteres**, sin markdown ni HTML. **Autogenerado por el módulo** con plantilla `"Cerrado por {usuario} el {fecha} UTC desde Inspecciones"` — el técnico no lo escribe. (Decisión 2026-04-30 final: el icono "ojo tachado" descarta sin modal.) En descartes futuros desde otros canales (sagas de limpieza, batch admin), el motivo puede ser distinto.
  - `descartadaPor`: username del técnico (string opaco).
  - **Sin `descartadaEn` en el body**: el ERP genera el timestamp al recibir la solicitud.
- **Response 200 OK**:
  ```json
  {
    "inspeccionId": "5e7c9a31-4b2d-4f8a-9c1e-3d5e7f9a1b3c",
    "descartadaEn": "2026-04-30T14:35:42-05:00",
    "motivo": "Repetidas de la novedad MOTOR-VALV...",
    "novedadesDescartadas": [
      { "novedadId": 9001, "estado": "descartada" },
      { "novedadId": 9002, "estado": "descartada" }
    ]
  }
  ```
- **Errors (todo-o-nada)**:
  - `409 Conflict` si **alguna** novedad del array ya fue verificada/descartada por OTRA inspección. Body del 409 incluye `novedadIdsConflictivos: [...]` para que el cliente reintente sin esos. **Ninguna** del array se procesa si hay conflicto en al menos una (atomic).
  - `404 Not Found` si **alguna** novedad no existe o no pertenece a `inspeccionId`. Mismo principio atomic.
  - `400 Bad Request` si `motivo` está vacío o excede 4000 chars, o `novedadIds` vacío/contiene duplicados.
- **Sin precondiciones del lado ERP** (igual que antes): el ERP no valida que el técnico haya "inspeccionado" cada novedad antes de descartar. La responsabilidad de tener evidencia recae en el técnico/módulo.
- **⚠️ Irreversibilidad**: igual que P-5. La asignación es vinculante — cancelar la inspección **no** revierte el descarte en el ERP. Una vez descartadas, las novedades quedan fuera del flujo del operador.
- **Notas**: el motivo queda en el ERP para audit del operador. En MVP, el módulo invoca este endpoint **una vez por novedad descartada** (un evento `NovedadPreopDescartada_v1` en el stream por cada llamada) — el contrato bulk se mantiene en el ERP por flexibilidad futura.

---

### 3.2 MYE núcleo — operaciones (equipo de MYE)

| # | Método | Path | Estado | Slice consumidor | Roadmap |
|---|---|---|---|---|---|
| M-1 | POST | `/api/v1/mye/ot-correctivas` | 🚧 | Saga de cierre con intervención | §4.9 + §3.27 |
| M-1b | POST | `/api/v1/mye/ot-correctivas/{otCorrectivaIdSinco}/adjuntos` | 🚧 | Adjuntar PDF de inspección a OT (multipart, decisión 2026-04-30) — ver §3.2 abajo | §4.9c + §3.27d |
| M-2 | GET | `/api/v1/mye/ot-correctivas?inspeccionId={id}` | 🟣 | Fallback ADR-003 si M-1 no es idempotente real | §4.10 |
| M-W-1 | PUT | `/api/v1/equipos/{equipoCodigo}/dictamen-vigente` | 🚧 | Sync de dictamen al equipo en MYE en cada firma (decisión 2026-04-30) — ver §3.4 abajo | §4.9b + §3.27c |

#### M-1 `POST /api/v1/mye/ot-correctivas`

> **Estado 2026-05-13:** ❌ **Bloqueante real** — slice 8 de Maquinaria_V4 (creación de OT correctiva) está **pausado por DDL DBA** (cambios de esquema en la tabla de OTs del ERP Sinco on-prem pendientes de aprobación). Es el único bloqueo de implementación pendiente tras la reconciliación bilateral. Cuando se desbloquee, Inspecciones cierra el slice `erp-5` (saga generación OT) contra el endpoint real.

> **🚧 Revisión de detalle diferida (2026-04-29)**: el shape exacto del body/response, la lista final de campos y el catálogo de prioridad/unidades quedan pendientes para una iteración posterior cuando el producto esté más maduro y haya conversación con MYE núcleo. El contrato canónico **vigente** (idempotencia real, matriz 200/4xx/5xx/409, fallback GET, tests requeridos del adapter, ventana ≥30 días) ya está consolidado en **ADR-003 §13** del modelo de dominio. Este endpoint sigue siendo el más crítico del contrato, pero su detalle granular se trabaja después.

Crea OT correctiva en MYE con BOM consolidado de la inspección. **Crítico** — es la integración más importante del módulo.

- **Idempotency-Key**: `InspeccionId`. Ventana ≥30 días. **Idempotencia real obligatoria** (compromiso vinculante — ver ADR-003 §13).
- **Resiliencia**: invocado vía Wolverine outbox + retry exponencial (§1.8). Detalle de matriz 200/4xx/5xx/409 en ADR-003.
- **Body**:
  ```json
  {
    "inspeccionId": "guid (interno del módulo)",
    "equipoId": 1234,
    "prioridad": "Baja | Normal | Alta | Urgente",
    "descripcionTrabajo": "string consolidado",
    "hallazgosRelacionados": ["guid (interno del módulo)", ...],
    "bom": [
      { "skuId": 4501, "codigoSku": "EMP-HID-D11T-001", "cantidadTotal": 2.0, "unidad": "unidad", "hallazgosOrigen": ["guid (interno del módulo)", ...] }
    ],
    "dictamen": "PuedeOperar | ConRestriccion | NoPuedeOperar",
    "responsableCosto": "Proyecto | DepartamentoEquipos",
    "inspeccionFirmadaEn": "iso-8601",
    "tecnicoFirmante": "username",
    "solicitadaPor": "username"
  }
  ```
  > **Tipos de IDs:** `inspeccionId` y `hallazgosRelacionados` son **Guid** (generados internamente por el módulo, identifican streams del aggregate). `equipoId` y `skuId` son **int** (PKs del ERP). El ERP recibe ambos formatos en el mismo body.
- **`responsableCosto`** (decisión 2026-04-30): enum cerrado de 2 valores. `Proyecto` = la obra donde está el equipo asume el costo; `DepartamentoEquipos` = el área que administra los equipos como activo asume el costo. Capturado por el aprobador en la pantalla del paso 3.42b (capability `generar-ot`). **🚧 TODO con David**: confirmar el nombre exacto del campo del DTO en MYE y los valores literales admitidos (`"Proyecto"`/`"DepartamentoEquipos"` vs identificadores numéricos / códigos cortos). Mientras se confirma, asumimos los strings de arriba.
- **`solicitadaPor`** (decisión 2026-04-30 ADR-007): username del aprobador con capability `generar-ot` que disparó el comando `GenerarOT`. Distinto de `tecnicoFirmante` (puede ser otra persona). Si MYE no acepta el campo, omitir y mantenerlo solo del lado del módulo en `OTSolicitada_v1`.
- **Response 200**:
  ```json
  {
    "otCorrectivaIdSinco": 88001,
    "otCorrectivaNumero": "OT-123456",
    "creadaEn": "iso-8601"
  }
  ```
- **Comportamiento esperado por código**:
  - `200 OK` (fresco o replay) → adapter procede a `InspeccionCerrada_v1`
  - `4xx` (validación: BOM inválido, equipo desconocido, etc.) → NO retry, emite `OTGeneracionFallida_v1`, estado `CierrePendienteOT`
  - `5xx`/timeout → Wolverine retry con backoff (5s, 30s, 2m, 10m); tras agotar, `OTGeneracionFallida_v1` tipo `Transitorio`
  - `409 Conflict` → **anti-patrón del lado MYE**, viola contrato; ver fallback M-2
- **BOM puede ser vacío**: la intervención puede ser solo mano de obra (V-F3 §15.5).
- **Auth**: capability `ejecutar-inspeccion`.

#### M-1b `POST /api/v1/mye/ot-correctivas/{otCorrectivaIdSinco}/adjuntos`

> **Estado 2026-05-13:** ❌ NO aplica — los adjuntos de OT correctiva se redirigen al **Document Service externo**, no a Maquinaria_V4. La saga `EjecutarOTSaga` apuntará al Document Service cuando se reactive (fuera de este contrato). El PDF de inspección no entra al ERP por este path.

> **Origen:** decisión 2026-04-30 a partir de observación de Sergio — *"cuando se genere una OT, debe llegar como adjunto a esta, el PDF de la inspección"*. Detalle del modelo en §17 ADR-007 sub-sección "Generación de PDF de inspección y adjunto a OT".

Sube el PDF de la inspección como adjunto de la OT correctiva ya creada en MYE. Invocado por `EjecutarOTSaga` tras éxito de M-1.

- **Cuándo se invoca**: tras éxito del POST a M-1 (la saga ya tiene el `otCorrectivaIdSinco`). Si el PDF aún no se generó (race con `GenerarPdfInspeccionSaga`), la saga espera con backoff (max 5 min) y luego emite `AdjuntoPdfFallido_v1`.
- **Idempotency-Key**: `{InspeccionId}-pdf`. Replay debe ser inocuo; si el adjunto ya existe, MYE devuelve el `AdjuntoIdSinco` original.
- **Resiliencia**: Wolverine outbox + retry exponencial. **Fallo NO revierte la OT** — la OT ya existe; el adjunto se reintenta o se sube manualmente desde MYE web.
- **Auth**: capability `ejecutar-inspeccion`.
- **Path param**: `otCorrectivaIdSinco` — el id que devolvió M-1 al crear la OT.
- **Content-Type**: `multipart/form-data`.
- **Body (multipart)**:
  - `file`: archivo PDF binario (`application/pdf`).
  - `descripcion`: `"PDF de inspección {numeroCorrelativo} firmada el {fechaIso}"` (string).
  - `tipo`: `"InspeccionTecnica"` (enum del lado MYE — confirmar valor exacto con David).
  - `inspeccionId`: guid del módulo, para reconciliación cross-sistema.
  - `sha256`: hash hex del archivo, para que MYE valide integridad.
- **Response 200 OK**:
  ```json
  {
    "adjuntoIdSinco": 90001,
    "otCorrectivaIdSinco": 88001,
    "tamanoBytes": 2458920,
    "subidoEn": "iso-8601"
  }
  ```
- **Errors**:
  - `404 Not Found` si la OT no existe (no hay carrera previa a M-1 que pueda llevar a esto si los stubs están bien).
  - `413 Payload Too Large` si el PDF excede el límite del ERP (sugerencia confirmar con David: ≥10 MB).
  - `415 Unsupported Media Type` si el `tipo` no es admitido.
  - `409 Conflict` solo si MYE no permite múltiples adjuntos del mismo `tipo`. **Pendiente confirmar con David** — para el caso retry/replay queremos que MYE haga upsert por (`otCorrectivaIdSinco`, `inspeccionId`, `tipo`).
- **🚧 TODO con David** (ver `07-preguntas-destrabar-followups.md` pregunta 4):
  1. ¿El endpoint ya existe en MYE o es a construir?
  2. Tamaño máximo del adjunto.
  3. Lista de `tipo` admitidos — confirmar valor literal para `InspeccionTecnica`.
  4. Comportamiento ante replay con misma `Idempotency-Key`: ¿upsert por `inspeccionId`+`tipo`, o crea otro adjunto?
  5. ¿Hay endpoint de descarga del adjunto (`GET /mye/ot-correctivas/{id}/adjuntos/{adjuntoId}`) que el módulo pueda usar para verificar integridad post-upload?

#### M-2 `GET /api/v1/mye/ot-correctivas?inspeccionId={id}`

> **Estado 2026-05-13:** ❌ **Descartado bilateralmente** — imposibilidad técnica: el ERP Sinco **no almacena el `inspeccionId` del módulo** en la entidad de OT, por lo que es imposible indexar OTs por ese campo. El fallback ADR-003 (consultar antes de crear) queda inaplicable. **Implicación dura:** M-1 debe cumplir idempotencia real estricta — no hay segunda red de seguridad. Si M-1 devuelve `409`/`5xx`, el adapter debe reintentar con la misma `Idempotency-Key` y confiar en que MYE no duplica.

> **🚧 Revisión de detalle diferida** — par lógico de M-1; cuando se reabra M-1 se reabre este. Contrato actual en ADR-003 §13.

Fallback condicional. **Opcional si M-1 cumple idempotencia real; obligatorio si no.**

- **Query**: `inspeccionId` (guid).
- **Response 200**: misma forma que M-1 si la OT existe; `404 Not Found` si no.
- **Uso**: el adapter llama a M-2 después de un `5xx`/`409` de M-1 para verificar si la OT ya fue creada antes de reintentar (patrón "consulta-antes-de-crear", ADR-003 §13).
- **Auth**: capability `ejecutar-inspeccion`.

#### M-W-1 `PUT /api/v1/equipos/{equipoCodigo}/dictamen-vigente`

> **Estado 2026-05-13:** ✅ Alineado — Maquinaria_V4 slice 11 expone `PUT /api/equipos/{codigo}/dictamen-vigente` (path real `/api/v4/Maquinaria/api/equipos/{codigo}/dictamen-vigente`). **Acoplado en Inspecciones slice erp-3** (`SincronizarDictamenVigenteSaga`).
>
> **Divergencia 2026-05-13:** el contrato canónico define `dictamen: "PuedeOperar" | "ConRestriccion" | "NoPuedeOperar"` (string enum), pero Maquinaria_V4 acepta `Estado: int` con códigos `0=PuedeOperar`, `1=ConRestriccion`, `2=NoPuedeOperar`. **El adapter mapea string↔int en ambas direcciones.** El contrato canónico se conserva tal cual (string enum es la forma idealmente expresiva); la traducción vive en `SincronizarDictamenVigenteHandler`.

> **Origen:** decisión 2026-04-30 a partir de observación de Sergio (consultor producto) — *"debe existir un servicio para actualizar este campo en el ERP"*. Ver §17 ADR-007 sub-sección "Integración con MYE: dictamen vigente del equipo" del modelo de dominio.

Actualiza el dictamen vigente del equipo en MYE. Invocado en **toda firma** de inspección (con OT y sin OT) por `SincronizarDictamenVigenteSaga`. Redundante con el dictamen embebido en M-1 cuando hay OT, pero unifica la lógica del adapter del módulo.

- **Path param**: `equipoCodigo` (string del catálogo MYE).
- **Idempotency-Key**: `InspeccionId`. Replay con misma key + mismo body debe ser inocuo (idem-MYE recomendado, no obligatorio si MYE acepta múltiples PUTs sucesivos sin efecto colateral indeseado).
- **Body**:
  ```json
  {
    "dictamen": "PuedeOperar | ConRestriccion | NoPuedeOperar",
    "inspeccionOrigenId": "guid",
    "firmadaEn": "iso-8601",
    "tecnicoFirmante": "username"
  }
  ```
- **Response 200**: vacío o eco del dictamen recién aplicado. **Response 4xx**: error permanente (equipo desconocido, dictamen no permitido por estado del equipo en MYE) → `DictamenVigenteSyncFallida_v1` candidato; NO bloquea el cierre de la inspección. **Response 5xx**: Wolverine retry con backoff estándar (5s, 30s, 2m, 10m). Tras agotar, `DictamenVigenteSyncFallida_v1` y queue manual.
- **Auth**: capability `ejecutar-inspeccion` (suficiente — es consecuencia de una firma válida).

**🚧 TODO con David** (ver `07-preguntas-destrabar-followups.md` pregunta 2 — dictamen vigente):
1. ¿El campo `DictamenVigente` ya existe en la entidad `Equipo` de MYE, o es a construir?
2. ¿Nombre exacto del path? ¿`dictamen-vigente`, `estado-operacion`, `condicion-actual`, otro?
3. ¿Valores admitidos (literal `"PuedeOperar"` vs código corto vs id numérico)? — Misma pregunta que para `responsableCosto` en M-1.
4. ¿Existe ya un endpoint análogo (lectura) que devuelva el dictamen vigente para histórico/auditoría? Útil para reconciliación si el sync falla.

---

### 3.3 MYE núcleo — lecturas (equipo de MYE)

| # | Método | Path | Estado | Propósito |
|---|---|---|---|---|
| M-3 | GET | `/api/v1/equipos?q=&page=&size=` | 🚧 | Lista de equipos liviana para selector / autocomplete. NO trae partes ni rutinas (eso vive en M-3b). |
| M-3b | GET | `/api/v1/equipos/{equipoCodigo}` | 🚧 | **Detalle del equipo** con `partes[]` (árbol plano), `rutinaTecnicaId` (singular, MVP) y `grupoMantenimientoId` (resuelve rutinas-monitoreo client-side — monitoreo MVP desde 2026-05-05). Absorbe M-4. Decisión 2026-05-04, refinada 2026-05-05. |
| M-4 | GET | `/api/v1/equipos/{equipoCodigo}/partes` | ❌ | **Eliminado / absorbido por M-3b (decisión 2026-05-04).** Conservado en la tabla solo como referencia histórica. |
| M-5 | GET | `/api/v1/equipos/{equipoCodigo}/rutinas-aplicables?tipo=tecnica` | ⏸ | **Diferido a post-MVP**. La asignación de rutinas-monitoreo se resuelve client-side por grupo (M-3b trae `grupoMantenimientoId`, M-16 trae rutinas con `grupoMantenimientoId` — decisión 2026-05-05). M-5 quedaría útil si emerge necesidad de filtros server-side adicionales. |
| M-6 | GET | `/api/v1/rutinas?grupo=&tipo=&page=&size=` | ⏸ | **Diferido** — bloque rutinas-driven (junto con M-5, M-7). Reactivar con tipo "Monitoreo" post-MVP |
| M-7 | GET | `/api/v1/rutinas/{rutinaCodigo}` | ⏸ | **Diferido** — bloque rutinas-driven (junto con M-5, M-6) |

Consumidos por: pantalla de inicio de inspección (selector de equipo, carga de rutina), wizard de hallazgo (selector de parte/actividad), proyección Reporting.

#### M-3 `GET /api/v1/equipos`

> **Estado 2026-05-13:** ✅ Alineado — Maquinaria_V4 slice 2 expone `GET /api/equipos?filtro=` (path real `/api/v4/Maquinaria/api/equipos`) con ETag y soporte de `If-None-Match`. **Acoplado en Inspecciones slice erp-4** (sync on-app-open). Adapter mapea `q` → `filtro`. El campo `EquipoErpDto.RutinaMantenimientoId` sirve además como fuente sintetizada del catálogo de rutinas técnicas (workaround M-17 — ver §0.4).

Lista **liviana** de equipos del usuario para el selector / autocomplete al iniciar inspección. Diseñada para latencia baja en `q=` autocomplete.

- **Query params**:
  - `q` (opcional): búsqueda libre por código / id / descripción del equipo.
  - `page`, `size`: paginación estándar.
- **Sin filtro de `obra` ni `grupo`**: el ERP filtra por las obras del usuario via JWT (row-level security, mismo patrón que P-1). `grupo` se difiere a refinamiento post-piloto si emerge necesidad.
- **NO incluye `partes`, `rutinaTecnicaId` ni `grupoMantenimientoId`**: estos viven en M-3b (detalle por equipo). M-3 mantiene el response liviano para que `q=` autocomplete sea responsivo en redes 4G de obra.
- **Auth**: capability `ejecutar-inspeccion`.
- **Response 200**:
  ```json
  {
    "items": [
      {
        "equipoId": 1234,
        "codigo": "D11T-001",
        "descripcion": "Caterpillar D11T Bulldozer",
        "marca": "Caterpillar",
        "modelo": "D11T",
        "anio": 2018,
        "numeroSerie": "CAT-D11T-2018-7234",
        "numeroEconomico": "ECON-1145",
        "grupo": "MAQ-PESADA",
        "obraId": 5678,
        "obraCodigo": "OB-2026-CALI-001",
        "obraDescripcion": "Vía Cali-Buenaventura tramo 3",
        "estado": "operativo",
        "medidores": [
          { "numero": 1, "unidad": "horas", "valorActual": 4287.5 },
          { "numero": 2, "unidad": "kilometros", "valorActual": 32500.0 }
        ]
      }
    ]
  }
  ```
- **`equipoId` (int) vs `codigo` (string)** (decisión 2026-05-04, opción b): el `equipoId` es la PK int del ERP — usado por el módulo para todas las referencias internas. El `codigo` es la cadena legible para UI (mostrar al técnico, path params de URLs como `/equipos/{equipoCodigo}`). Mismo patrón aplica a `obraId` (int) + `obraCodigo` (string).
- **Headers**: `X-Total-Count`, `X-Page`, `X-Page-Size`.
- **Sin contadores `novedadesPreopPendientes` ni `seguimientosAbiertos`**: el módulo los calcula de sus proyecciones locales (no responsabilidad del ERP).
- **Notas**:
  - `estado`: catálogo cerrado del ERP — los valores reales se confirman con MYE núcleo (pendiente §8).
  - Lista de campos del item es propuesta inicial; se refina con MYE núcleo cuando avance la integración.

#### M-3b `GET /api/v1/equipos/{equipoCodigo}` (decisión 2026-05-04)

> **Estado 2026-05-13:** ❌ **Descartado bilateralmente** — Maquinaria_V4 no expone un endpoint de detalle agregado por equipo. La UI cubre el caso combinando **M-3 (lista/búsqueda de equipos) + M-5 (`/api/partes-equipos?idEquipo=`) + M-16 (`/api/rutinas-monitoreo?equipoId=`)** en llamadas paralelas. El árbol de partes y la rutina de monitoreo aplicable se resuelven por separado en lugar de viajar embebidos en un único response.

**Detalle completo del equipo.** Invocado cuando el técnico **selecciona un equipo** desde la lista (M-3) para iniciar inspección. Incluye partes (absorbe M-4), `rutinaTecnicaId` (asignación per-equipo, MVP) y `grupoMantenimientoId` (resuelve rutinas-monitoreo client-side, Fase 2). Una sola llamada → todo el contexto operativo del equipo cargado.

- **Path param**: `equipoCodigo` (string MYE — ej. `D11T-001`).
- **Auth**: capability `ejecutar-inspeccion`. ERP valida acceso al equipo (404 si no — no revela existencia).
- **Sin paginación**: el árbol de partes cabe completo (profundidad máx 3 niveles, ~10–40 partes).
- **Cache**: `ETag` + `Last-Modified` recomendados. El detalle de un equipo cambia poco (alta de equipos esporádica, partes y asignación de rutina técnica estables). Cliente puede revalidar con `If-None-Match`.
- **Response 200**:
  ```json
  {
    "equipoId": 1234,
    "codigo": "D11T-001",
    "descripcion": "Caterpillar D11T Bulldozer",
    "marca": "Caterpillar",
    "modelo": "D11T",
    "anio": 2018,
    "numeroSerie": "CAT-D11T-2018-7234",
    "numeroEconomico": "ECON-1145",
    "grupoMantenimientoId": 7,
    "grupoMantenimiento": "BULLDOZER",
    "obraId": 5678,
    "obraCodigo": "OB-2026-CALI-001",
    "obraDescripcion": "Vía Cali-Buenaventura tramo 3",
    "estado": "operativo",
    "medidores": [
      { "numero": 1, "unidad": "horas", "valorActual": 4287.5 },
      { "numero": 2, "unidad": "kilometros", "valorActual": 32500.0 }
    ],
    "partes": [
      {
        "parteId": 12,
        "codigo": "MOTOR",
        "descripcion": "Motor — Cat C32",
        "padreId": null,
        "nivel": 0
      },
      {
        "parteId": 34,
        "codigo": "MOTOR-INYECCION",
        "descripcion": "Sistema de inyección",
        "padreId": 12,
        "nivel": 1
      }
    ],
    "rutinaTecnicaId": 101
  }
  ```
- **Convenciones del shape**:
  - **Partes**: estructura **plana con `padreId`** (no árbol anidado JSON). `padreId=null` → raíz. `nivel` (0..2) para indentar sin caminar el árbol. Sin filtro de estado — todas las partes devueltas son válidas para inspección. Mismo formato que tenía M-4.
  - **`rutinaTecnicaId`** (decisión 2026-05-04, opción β): id (singular) de la rutina técnica asignada al equipo en el ERP. Cardinalidad **1 por equipo** (única). Resuelta contra `RutinaTecnicaLocal` (sync vía M-17 §3.4). El handler `IniciarInspeccion` la lee directamente (técnico no elige). Si el equipo no tiene rutina técnica asignada, el campo es `null` y el inicio de inspección queda bloqueado por invariante I-I2 (`01-modelo-dominio.md §15.7`).
  - **`grupoMantenimientoId` + `grupoMantenimiento`** (decisión 2026-05-05): PK del grupo + descriptor legible. **Es el mecanismo de asignación de rutinas-monitoreo**: el cliente filtra el catálogo local `RutinaMonitoreoLocal` (sync vía M-16) por `r.GrupoMantenimientoId == equipo.GrupoMantenimientoId`. **No hay tabla intermedia** equipo↔rutinas-monitoreo en el ERP. Ver §12.11.5 punto 9 del modelo.
  - **Asimetría intencional**: rutina técnica = asignación explícita per-equipo (`rutinaTecnicaId`). Rutinas de monitoreo = derivadas client-side por grupo. Refleja distintos lifecycles operativos de cada tipo de rutina.
- **Errors**: `404` si no existe o no es accesible al usuario.
- **🚧 TODO con David** (ver `07-preguntas-destrabar-followups.md` pregunta 6 actualizada): confirmar que `Equipo.grupoMantenimientoId` ya existe en el ERP (probable — el módulo de mantenimiento ya usa "grupos") y que se puede exponer en M-3b. La asignación `rutinaTecnicaId` per-equipo sigue siendo la pregunta abierta.

#### M-4 `GET /api/v1/equipos/{equipoCodigo}/partes` ❌ ELIMINADO

> **Estado 2026-05-13:** ✅ El árbol de partes vive en Maquinaria_V4 slice 3 como `GET /api/partes-equipos?idEquipo=` (path real `/api/v4/Maquinaria/api/partes-equipos`) con ETag. Como M-3b fue descartado bilateralmente, el adapter llama a este endpoint per-equipo cuando el técnico lo selecciona (la "absorción en M-3b" no aplica en la práctica). **Importante:** en este documento "M-4" originalmente refería a `/equipos/.../partes`; en el mapa §0.4 el ID "M-4" refiere al catálogo de insumos/productos. Ambos usos son históricos — usar siempre la ruta real para evitar ambigüedad.

> **Eliminado el 2026-05-04 — absorbido por M-3b.** El árbol de partes ahora viaja embebido en el detalle del equipo (M-3b). Mantener M-4 implicaría dos llamadas para la misma operación (selección de equipo). Esta entrada queda solo como referencia histórica para slices que aún apunten a M-4 — todos deben migrar a M-3b.

---

### 3.4 MYE núcleo — catálogos sincronizados (equipo de MYE)

Sincronizados **on-app-open** vía `If-None-Match`/`ETag` (ADR-004 canonical 2026-05-05 — sin cron nocturno). Soporte de `304 Not Modified` obligatorio.

| # | Método | Path | Estado | Sync | Catálogo local |
|---|---|---|---|---|---|
| M-8 | GET | `/api/v1/catalogos/partes?q=` | ⏸ | **Diferido a post-MVP** — el wizard usa el árbol de partes del equipo (M-4); no se requiere catálogo global cross-equipo en MVP | (sin proyección local en MVP) |
| M-9 | GET | `/api/v1/catalogos/actividades?q=` | ⏸ | **Diferido** — junto con M-5..M-8. Reactivar con tipo "Monitoreo" / inspecciones con mediciones (post-MVP). En MVP el técnico escribe `ActividadDescripcion` como texto libre | (sin proyección local en MVP) |
| M-10 | GET | `/api/v1/catalogos/causas-falla` | 🚧 | On-app-open | `CausaFallaLocal` |
| M-11 | GET | `/api/v1/catalogos/tipos-falla` | 🚧 | On-app-open | `TipoFallaLocal` |
| M-12 | GET | `/api/v1/catalogos/ubicaciones` | ⏸ | **Diferido** — ningún campo del modelo referencia `UbicacionId`; la ubicación física puede viajar denormalizada como string en M-3 si se requiere | (sin proyección local en MVP) |
| M-13 | GET | `/api/v1/catalogos/obras` | 🚧 | On-app-open | `ProyectoLocal` |
| M-14 | GET | `/api/v1/catalogos/grupos` | ⏸ | **Diferido** — filtro por grupo en M-3 ya está diferido; M-3 trae `grupo` como string denormalizado | (sin proyección local en MVP) |
| M-15 | GET | `/api/v1/catalogos/unidades-medidor` | ⏸ | **Diferido** — ningún campo del modelo referencia `UnidadMedidorId`; en MVP `unidad` viaja como string denormalizado en `medidores[]` (P-2). Reactivar con bloque rutinas-driven | (sin proyección local en MVP) |
| M-16 | GET | `/api/v1/catalogos/rutinas-monitoreo` | 🚧 | **Crítico MVP — promovido 2026-05-05.** Catálogo completo de definiciones de rutinas de monitoreo. Cada rutina trae `grupoMantenimientoId`. El cliente filtra por grupo del equipo seleccionado (sin tabla intermedia equipo↔rutina en el ERP). Habilita el flujo monitoreo del MVP (roadmap §3.B'). | `RutinaMonitoreoLocal` |
| M-17 | GET | `/api/v1/catalogos/rutinas` | 🚧 | **Crítico MVP — agregado 2026-05-04.** Catálogo de definiciones de rutinas técnicas. Cierra el gap de sync que el modelo asumía pero no estaba en el contrato. La asignación equipo↔rutina técnica vive en M-3b (`rutinaTecnicaId`); este endpoint trae las definiciones para resolver el id contra `RutinaTecnicaLocal`. | `RutinaTecnicaLocal` |

#### M-10 `GET /api/v1/catalogos/causas-falla`

> **Estado 2026-05-13:** ✅ Alineado — Maquinaria_V4 slice 5 expone `GET /api/causas-falla?texto=` (path real `/api/v4/Maquinaria/api/causas-falla`) con ETag y `304 Not Modified`. **Acoplado en Inspecciones slice erp-4** (sync on-app-open → `CausaFallaLocal`).

Catálogo cerrado de causas de falla. **Crítico MVP** — referenciado por cada hallazgo con `RequiereIntervencion` (invariante I-H4 §15.3).

- **Estructura**: catálogo **plano** (sin jerarquía padre-hijo).
- **Response 200**:
  ```json
  {
    "items": [
      { "causaFallaId": 1, "codigo": "CAU-DESGASTE", "descripcion": "Desgaste normal de operación" },
      { "causaFallaId": 2, "codigo": "CAU-FATIGA", "descripcion": "Fatiga del material" },
      { "causaFallaId": 3, "codigo": "CAU-CONTAM", "descripcion": "Contaminación / suciedad" }
    ],
    "totalCount": 47
  }
  ```
- **Solo causas activas**: el ERP filtra server-side y nunca devuelve descontinuadas. **Sin campo `activo`** en el response.
- **Sin paginación**: volumen razonable (<500 entries esperados).
- **Cache headers obligatorios**: `ETag` + `Last-Modified`. Cliente usa `If-None-Match` / `If-Modified-Since` → `304 Not Modified` cuando no hay cambios.
- **Sync local**: cliente PWA puebla `CausaFallaLocal` on-app-open con `If-None-Match` (ADR-004 canonical 2026-05-05). Stale-while-revalidate sirve si la app abre sin red.
- **Causas históricas descontinuadas**: el ERP no las devuelve en sync, pero `CausaFallaLocal` **conserva** los items previamente sincronizados (no se eliminan al desaparecer del response). Los hallazgos históricos siguen resolviendo el `causaFallaId` contra la copia local. Esto cumple ADR-004: IDs son inmutables, descontinuar no rompe audit histórico.

#### M-11 `GET /api/v1/catalogos/tipos-falla`

> **Estado 2026-05-13:** ✅ Alineado — Maquinaria_V4 slice 6 expone `GET /api/tipos-falla?texto=` (path real `/api/v4/Maquinaria/api/tipos-falla`) con ETag. Campo `Prioridad: string` confirmado en el shape. **Acoplado en Inspecciones slice erp-4** (sync on-app-open → `TipoFallaLocal`).

Catálogo cerrado de tipos de falla. **Crítico MVP** — referenciado por cada hallazgo con `RequiereIntervencion` (invariante I-H4 §15.3). Ortogonal a M-10: causa = *por qué* falló; tipo = *qué tipo* de falla (mecánica, hidráulica, eléctrica, etc.).

- **Estructura**: catálogo **plano** (sin jerarquía).
- **Response 200**:
  ```json
  {
    "items": [
      { "tipoFallaId": 1, "codigo": "TIP-MECANICA", "descripcion": "Mecánica" },
      { "tipoFallaId": 2, "codigo": "TIP-HIDRAULICA", "descripcion": "Hidráulica" },
      { "tipoFallaId": 3, "codigo": "TIP-ELECTRICA", "descripcion": "Eléctrica" }
    ],
    "totalCount": 8
  }
  ```
- **Solo tipos activos**, sin paginación, cache headers, sync on-app-open (ADR-004 canonical 2026-05-05) → `TipoFallaLocal`. Mismas reglas que M-10 (incluyendo conservación de descontinuados en proyección local para audit histórico — ADR-004).

#### M-13 `GET /api/v1/catalogos/obras`

> **Estado 2026-05-13:** ❌ **Descartado bilateralmente** — NO aplica. El catálogo de obras lo gestiona el host PWA SincoMyE aparte; el módulo no sincroniza obras desde Maquinaria_V4. Si el módulo necesita resolver `ObraId` → descripción, lo hará vía el host o denormalizando en el response de equipos.

Catálogo de obras (que el módulo internamente conoce como "proyectos"). **Crítico MVP** — `ProyectoId` aparece en cada inspección, hallazgo, novedad preop, equipo.

- **Estructura**: catálogo plano.
- **Response 200**:
  ```json
  {
    "items": [
      {
        "obraId": 5678,
        "codigo": "OB-2026-CALI-001",
        "descripcion": "Vía Cali-Buenaventura tramo 3",
        "tipo": "Carretera"
      }
    ],
    "totalCount": 47
  }
  ```
- **`tipo`**: texto libre (no enum cerrado del ERP).
- **Solo obras activas**, sin paginación, cache headers, **sync on-app-open** (ADR-004 canonical 2026-05-05) → `ProyectoLocal`.
- **Sin filtrado por usuario en la sync**: el ERP devuelve **todas las obras** (= proyectos) de Sinco. La proyección local `ProyectoLocal` es completa para resolver `proyectoId` en historial cross-usuario (auditoría de inspecciones de proyectos donde el usuario actual ya no tiene permiso). El filtrado por capability del usuario ocurre **en runtime** del cliente cuando se muestra una lista interactiva.
- **Conserva descontinuadas en proyección local** (ADR-004): hallazgos / inspecciones históricas siguen resolviendo `obraId` aunque la obra ya haya cerrado.

#### M-15 `GET /api/v1/catalogos/unidades-medidor`

Catálogo cerrado de unidades para los medidores de equipos (referenciado por P-2 `medidores[i].unidad`).

- **Response**: array de unidades con `codigo`, `descripcion`, `activo: bool`.
  ```json
  [
    { "codigo": "horas", "descripcion": "Horas de operación", "activo": true },
    { "codigo": "kilometros", "descripcion": "Kilómetros recorridos", "activo": true },
    { "codigo": "m3", "descripcion": "Metros cúbicos", "activo": true },
    { "codigo": "ciclos", "descripcion": "Ciclos completos", "activo": true }
  ]
  ```
- **Notas**: igual que el catálogo de partes — el ERP puede agregar unidades pero los códigos existentes son inmutables (ADR-004). Ventana de staleness aceptada: 24h.
- **Sync**: on-app-open + `ETag` (`If-None-Match`) con `304 Not Modified` cuando aplique. Decisión 2026-05-05 ADR-004 canonical (sin cron).

#### M-17 `GET /api/v1/catalogos/rutinas` (decisión 2026-05-04 — crítico MVP)

> **Estado 2026-05-13:** ⚠️ **NO existe endpoint dedicado** en Maquinaria_V4. **Workaround acordado:** el adapter sintetiza el catálogo de rutinas técnicas desde el campo `EquipoErpDto.RutinaMantenimientoId` que viene en M-3 (`/api/equipos`), agregando per-equipo en `SincronizarEquipoDesdeErpHandler`. No se requiere endpoint nuevo en Maquinaria_V4. La proyección local `RutinaTecnicaLocal` se puebla incrementalmente a partir del sync de equipos.
>
> **Divergencia 2026-05-13:** el contrato canónico asume un catálogo plano con `items[]` por rutina (instrucciones, obligatoriedad). El workaround **solo trae el id** de la rutina por equipo; los items quedan **sin sincronizar** en MVP (consistente con la nota canónica de que "items son metadata sugerida no obligatoria"). Si el flujo monitoreo requiere items, se reabrirá la conversación cross-team.

> **Origen:** la revisión por flujos del 2026-05-04 detectó que el modelo (§12.11.1) asumía la existencia de un sync de rutinas técnicas, pero el contrato lo tenía diferido a post-MVP (M-6/M-7 ⏸). Sin sync, el handler `IniciarInspeccion` no podía resolver `Equipo.RutinaTecnicaId` contra una proyección local. Este endpoint cierra el gap.

Catálogo de definiciones de rutinas técnicas. Sincronizado **on-app-open** con el patrón estándar (ADR-004 canonical 2026-05-05: sin cron, stale-while-revalidate, ETag `If-None-Match`). Alimenta `RutinaTecnicaLocal`.

- **Estructura**: catálogo plano. Items embebidos por rutina (parte fijada a nivel rutina, items son actividades sobre esa parte).
- **Sin filtro por grupo ni por equipo**: trae **todas** las rutinas técnicas activas. La asignación equipo↔rutina vive en M-3b (`rutinaTecnicaId` singular, decisión 2026-05-04 opción β).
- **Response 200**:
  ```json
  {
    "items": [
      {
        "rutinaId": 101,
        "codigo": "INSP. BULL.MOTOR",
        "nombre": "Inspección técnica Bulldozer — Motor",
        "tipo": "Tecnica",
        "grupoMantenimiento": "BULLDOZER",
        "parteId": 12,
        "parteCodigo": "MOTOR",
        "items": [
          {
            "itemId": 5001,
            "actividadId": 301,
            "instruccion": "Verificar nivel de aceite en mirilla con motor frío",
            "obligatorio": true
          },
          {
            "itemId": 5002,
            "actividadId": 302,
            "instruccion": null,
            "obligatorio": false
          }
        ]
      }
    ],
    "totalCount": 47
  }
  ```
- **`tipo`**: discriminador escalable. En MVP siempre `"Tecnica"`. Si el ERP tiene rutinas con otros tipos (preoperacional, mantenimiento), **el endpoint debe filtrarlos server-side y devolver solo `Tecnica`** — el módulo solo consume rutinas técnicas. Alternativamente, `GET /catalogos/rutinas?tipo=tecnica` con filtro explícito; el módulo asume el primer caso (filtrado implícito server-side).
- **Solo rutinas activas**: el ERP filtra server-side y nunca devuelve descontinuadas. Módulo conserva descontinuadas en `RutinaTecnicaLocal` (no las borra al desaparecer del response) para resolver historiales — ADR-004.
- **Cache headers obligatorios**: `ETag` (`If-None-Match`) → `304 Not Modified` cuando no hay cambios. Sync on-app-open (ADR-004 canonical 2026-05-05 — sin cron); stale-while-revalidate sirve si la app abre sin red.
- **Inmutabilidad de IDs**: tanto `rutinaId` como `itemId` son inmutables una vez publicados (ADR-004). El historial de `InspeccionIniciada_v1` snapshotea `RutinaId` + `RutinaCodigo` para audit; cambiar el id rompe la trazabilidad.
- **Items son metadata sugerida en MVP libre**: el flujo MVP (§15) no recorre items — el técnico decide qué inspeccionar libremente. Los items existen para reportería futura ("partes/actividades aplicables al equipo según la rutina") pero no son obligatorios al ejecutar la inspección. El sync puede aceptar `items: []` vacío sin impacto operativo en MVP.
- **🚧 TODO con David** (ver `07-preguntas-destrabar-followups.md` pregunta 5 + nueva entrada): confirmar que el ERP soporta el filtrado por `tipo=Tecnica` (server-side o vía query) y que la cardinalidad real es 1 rutina técnica por equipo.

#### M-16 `GET /api/v1/catalogos/rutinas-monitoreo` (Fase 2 — diferido)

> **Estado 2026-05-13:** ⚠️ Alineado con shape distinto — Maquinaria_V4 slice 10 expone `GET /api/rutinas-monitoreo?equipoId=` (path real `/api/v4/Maquinaria/api/rutinas-monitoreo`). **Filtro per-equipo server-side**, no catálogo global.
>
> **Divergencia 2026-05-13:** el contrato canónico (decisión 2026-05-05) asume un catálogo global con `grupoMantenimientoId` por rutina, y el cliente filtra client-side por el grupo del equipo. Maquinaria_V4 hace el filtrado **server-side por `equipoId`** — el cliente no necesita resolver el grupo. Implicación: la proyección local `RutinaMonitoreoLocal` ya no es un catálogo global sino un mapa `equipoId → [rutinas aplicables]`, cargado bajo demanda cuando el técnico selecciona el equipo. Sin sync masivo on-app-open para este recurso. El razonamiento del modelo §12.11.5 punto 9 (asignación derivada por grupo) se preserva conceptualmente, pero el ERP lo encapsula y el cliente solo consume la lista resuelta.

> **Decisión 2026-05-04:** este endpoint reemplaza al `GET /api/v1/rutinas-monitoreo?grupo={g}` planteado inicialmente. La asignación equipo↔rutinas se movió al detalle del equipo (M-3b); este catálogo solo trae **definiciones** de rutinas, sin filtro por grupo. Detalle del modelo en `01-modelo-dominio.md` §12.11.5 punto 9.

Catálogo completo de definiciones de rutinas de monitoreo. Sincronizado **on-app-open** (ADR-004 canonical 2026-05-05). **Crítico MVP — promovido 2026-05-05** (antes era Fase 2). La inspección de monitoreo necesita los items + rangos + calificaciones snapshotados al iniciar (§12.11.5 punto 7 del modelo).

- **Sin filtro server-side por grupo**: trae todas las rutinas activas, cada una con su `grupoMantenimientoId` + `grupoMantenimiento`. **El cliente filtra** por el grupo del equipo seleccionado (`r.grupoMantenimientoId == equipo.grupoMantenimientoId`) — decisión 2026-05-05.
- **Estructura**: catálogo plano con items embebidos por rutina.
- **Response 200**:
  ```json
  {
    "items": [
      {
        "rutinaMonitoreoId": 201,
        "nombre": "Sistema eléctrico",
        "grupoMantenimientoId": 7,
        "grupoMantenimiento": "BULLDOZER",
        "items": [
          {
            "itemId": 6001,
            "parte": "Batería",
            "actividad": "Medición de voltaje",
            "tipoEvaluacion": "Numerica",
            "magnitud": "voltaje",
            "unidad": "V",
            "valorMin": 12.3,
            "valorMax": 12.5
          },
          {
            "itemId": 6002,
            "parte": "Conectores batería",
            "actividad": "Revisar estado",
            "tipoEvaluacion": "Cualitativa"
          }
        ]
      }
    ],
    "totalCount": 47
  }
  ```
- **`grupoMantenimientoId` + `grupoMantenimiento`** (decisión 2026-05-05): PK del grupo (mecanismo de asignación) + descriptor legible. **Es el mecanismo de asignación** equipo↔rutina-monitoreo: cliente filtra el catálogo local por el grupo del equipo. Sin tabla intermedia en el ERP. Ver §12.11.5 punto 9 del modelo.
- **`tipoEvaluacion`**: discriminador explícito (`"Numerica"` con `valorMin`/`valorMax`/`magnitud`/`unidad` o `"Cualitativa"` sin esos campos).
- **Solo rutinas activas**: el ERP filtra server-side. Módulo conserva descontinuadas en `RutinaMonitoreoLocal` (no las borra al desaparecer del response) para resolver historiales — ADR-004.
- **Cache headers obligatorios**: `ETag` (`If-None-Match`) → `304 Not Modified` cuando no hay cambios. Sync on-app-open (ADR-004 canonical 2026-05-05 — sin cron); stale-while-revalidate sirve si la app abre sin red.
- **Inmutabilidad de IDs**: tanto `rutinaMonitoreoId` como `itemId` son inmutables una vez publicados (ADR-004). Renombrar = cambiar descripción, no id. Esto es crítico porque `InspeccionIniciada_v1` snapshotea los items con su `ItemId` para calcular `FueraDeRango` contra el rango vigente al iniciar — si los IDs cambian se rompe la trazabilidad histórica.
- **🚧 TODO con David** (ver `07-preguntas-destrabar-followups.md` pregunta 6): confirmar existencia del catálogo en MYE y el endpoint real.

---

### 3.5 Inventario / Almacén (equipo de inventario)

| # | Método | Path | Estado | Slice consumidor | Roadmap |
|---|---|---|---|---|---|
| I-1 | GET | `/api/v1/insumos?q=&page=&size=` | 🚧 | Wizard hallazgo paso 2 (selector de repuestos) — incluye detalle completo de cada item, no requiere endpoint de detalle separado | §4.15 |
| I-2 | GET | `/api/v1/catalogos/insumos` | 🚧 | Sync on-app-open (ADR-004 canonical 2026-05-05) — alimenta `InsumoLocal` | §4.17 |

#### I-1 `GET /api/v1/insumos`

> **Estado 2026-05-13:** ⚠️ Alineado con shape distinto — Maquinaria_V4 slice 4 expone `GET /api/productos?texto=` (path real `/api/v4/Maquinaria/api/productos`). Adapter mapea `q` → `texto` y normaliza `producto` → `insumo` en el shape de respuesta.
>
> **Divergencia 2026-05-13:** el response **no incluye `ParteIdsCompatibles`** (campo de compatibilidad por parte). El módulo MVP tampoco lo usa — la búsqueda es por texto libre cross-catálogo (consistente con la nota canónica "sin filtro `parteId`"). El followup §8 sobre filtrado por `parteId` queda **descartado** post-reconciliación.

Búsqueda interactiva durante el wizard de hallazgo. **Critical UX** — latencia debe ser <500ms para autocomplete (preferiblemente atendida por proyección local `InsumoLocal` poblada por I-3).

- **Query params**:
  - `q`: búsqueda libre por código / descripción del insumo. **Required en práctica** — sin `q` la lista completa puede ser enorme.
  - `page`, `size`: paginación obligatoria.
- **Sin filtro `parteId`**: en MYE los insumos **no tienen amarre a partes**. La búsqueda es por texto libre cross-catálogo.
- **Auth**: capability `ejecutar-inspeccion`.
- **Response 200**:
  ```json
  {
    "items": [
      {
        "insumoId": 4501,
        "codigoSku": "EMP-HID-D11T-001",
        "descripcion": "Empaque bomba hidráulica D11T",
        "unidad": "unidad"
      },
      {
        "insumoId": 4502,
        "codigoSku": "ACE-HID-15W40",
        "descripcion": "Aceite hidráulico 15W40",
        "unidad": "litros"
      }
    ],
    "totalCount": 28
  }
  ```
- **Headers**: `X-Total-Count`, `X-Page`, `X-Page-Size`.
- **`unidad`**: string libre denormalizado (catálogo M-15 está diferido — el módulo no resuelve `UnidadMedidorId`).
- **Sin campo `aplicaMye`**: aunque MYE tiene este flag internamente, no viaja en el response — el módulo no toma decisiones sobre él (decisión §1.4 del modelo).
- **Sin `partesCompatibles`**: insumos no se vinculan a partes en MYE.
- **Notas**: el módulo originalmente usaba `/repuestos`; consolidado a `/insumos` siguiendo nomenclatura del ERP.

#### I-2 `GET /api/v1/catalogos/insumos`

> **Estado 2026-05-13:** ❌ NO aplica como endpoint independiente — el detalle del insumo ya existe en otro punto del ecosistema (consistente con el patrón "no duplicamos catálogos que el host ya gestiona"). El sync masivo on-app-open de insumos se sirve por el **mismo endpoint `/api/productos`** que I-1 (paginando exhaustivamente), no por un catálogo distinto. Adapter usa un cliente unificado para ambos roles.
>
> **Divergencia 2026-05-13:** el contrato separa I-1 (búsqueda interactiva) e I-2 (sync masivo); la realidad consolida ambos en `/api/productos`. El handler diferencia rol por presencia de paginación + `If-None-Match`.

Sync on-app-open del catálogo completo de insumos (ADR-004 canonical 2026-05-05 — sin cron). Alimenta la proyección local `InsumoLocal` que sirve la búsqueda I-1 con baja latencia.

- **Frecuencia**: por apertura de la PWA (sync delta con `If-None-Match`).
- **Auth**: capability `ejecutar-inspeccion` o equivalente para el sync (job-level).
- **Sin filtro por usuario**: trae el catálogo completo de Sinco (mismo razonamiento que M-13 obras).
- **Solo activos**: el ERP filtra server-side y nunca devuelve descontinuados. Módulo conserva descontinuados en `InsumoLocal` (no los borra al desaparecer del response) para resolver hallazgos históricos — ADR-004.
- **Paginación obligatoria**: volumen esperado ~miles. Cliente itera todas las páginas durante el sync.
- **Cache headers obligatorios**: `ETag` + `Last-Modified` → `304 Not Modified` cuando no hay cambios.
- **Response 200**:
  ```json
  {
    "items": [
      { "insumoId": 4501, "codigoSku": "EMP-HID-D11T-001", "descripcion": "Empaque bomba hidráulica D11T", "unidad": "unidad" },
      { "insumoId": 4502, "codigoSku": "ACE-HID-15W40", "descripcion": "Aceite hidráulico 15W40", "unidad": "litros" }
    ],
    "totalCount": 8472,
    "ultimaModificacion": "2026-04-29T03:14:00-05:00"
  }
  ```
- **Headers de paginación**: `X-Total-Count`, `X-Page`, `X-Page-Size`. Mismo shape de item que I-1.

---

### 3.6 User master / RRHH — ❌ NO APLICA (decisión 2026-05-05)

**Eliminado el 2026-05-05 (decisión Jaime).** El módulo **no consume endpoints de usuarios** — toda la identidad viene del host PWA vía JWT. Sin sync de usuarios, sin catálogo local, sin app registration propio. El endpoint U-1 (`GET /api/v1/admin/usuarios?desde={lastSync}`) y U-2 (`GET /api/v1/admin/usuarios/{userId}`) quedan **fuera del contrato del módulo**. Si el host PWA necesita esos endpoints para su propio sync de identidad, los gestiona aparte — no es responsabilidad de este módulo.

---

## 4. Resumen ejecutivo

| Módulo | Endpoints | Equipo Sinco dueño | Estado |
|---|---|---|---|
| Preoperacional | 6 (P-1..P-6) | Equipo del preop | 🚧 todos bloqueados |
| MYE núcleo (operaciones) | 2 (M-1, M-2) | Equipo MYE | 🚧 + 🟣 fallback |
| MYE núcleo (lecturas) | 5 (M-3, M-3b, M-5..M-7) | Equipo MYE | M-3 + M-3b activos (🚧); M-4 ❌ eliminado/absorbido por M-3b (decisión 2026-05-04); M-5, M-6, M-7 ⏸ diferidos al bloque rutinas-driven (post-MVP) |
| MYE núcleo (catálogos) | 10 (M-8..M-17) | Equipo MYE | M-10, M-11, M-13, M-16, M-17 activos (🚧 — M-17 nuevo crítico MVP 2026-05-04; M-16 promovido a MVP 2026-05-05 por inclusión de monitoreo); M-8, M-9, M-12, M-14, M-15 ⏸ diferidos |
| Inventario | 2 (I-1, I-2) | Equipo inventario | 🚧 todos bloqueados |
| ~~User master / RRHH~~ | ~~2 (U-1, U-2)~~ | ❌ eliminado 2026-05-05 | Identidad 100% del host PWA — sin endpoints de usuarios |

**Total:** 25 endpoints activos (M-3b nuevo + M-16 nuevo + M-17 nuevo; M-4 eliminado; U-1/U-2 eliminados 2026-05-05) — **16 obligatorios MVP** (incluye M-17 + M-16 promovido 2026-05-05) + 1 condicional (M-2 fallback ADR-003) + 8 diferidos al post-MVP (M-5, M-6, M-7 — bloque rutinas-driven; M-8, M-9 — catálogos globales de partes/actividades; M-12 — ubicaciones; M-14 — grupos; M-15 — unidades-medidor).

**Cuellos de botella esperados:**

1. **Equipo MYE**: 15 endpoints (M-1..M-15). Probable cuello principal — concentra el grueso del trabajo.
2. **POST /mye/ot-correctivas (M-1)**: idempotencia real es compromiso crítico. Sin ella, el módulo entra en patrón degradado con M-2.
3. **DDL del preoperacional**: bloquea P-1..P-5 hasta que el equipo del preop comparta el shape de la novedad (paso 0.18 del roadmap).

---

## 5. Discrepancias resueltas con docs anteriores

Este archivo unifica versiones contradictorias previas:

| Discrepancia | Resolución |
|---|---|
| `/repuestos` (roadmap §4.15) vs `/insumos` (00-investigacion-mercado §1380, SOW §213) | **Canónico: `/insumos`**. Decisión del 2026-04-27 — ERP unificó nomenclatura en "insumos". |
| ~~`/admin/usuarios`~~ vs ~~`/usuarios`~~ | **❌ Eliminado 2026-05-05** — el módulo no consume endpoints de usuarios; identidad viene del host PWA. |
| `/preop/novedades/{id}/descartar` (roadmap §4.3) ausente en SOW §7 | **Canónico: existe** — emergió con `NovedadPreopDescartada_v1` (§15.4 del modelo). |
| `/equipos/{id}/rutinas-aplicables` (modelo §1349) ausente en roadmap §4 | **Canónico: existe** — necesario para cargar rutinas técnicas al iniciar inspección. |
| `/catalogos/grupos` (roadmap §4.13) ausente en otros docs | **Canónico: existe** — usado para filtrar equipos por grupo. |
| `Idempotency-Key` opcional vs idempotencia real obligatoria | **Canónico: idempotencia real obligatoria** para `POST` no-naturalmente-idempotentes. Ver §1.4. |

---

## 6. Riesgos y deuda técnica

| # | Riesgo | Mitigación |
|---|---|---|
| R-API-1 | 17 endpoints activos para MVP en 3 equipos cross-Sinco (16 obligatorios + 1 condicional M-2). Coordinación es bloqueante. **Refinado 2026-05-05:** equipo Seguridad/IT eliminado del cross-team (sin U-1/U-2). | SOW interno con cada equipo; escalación a CTO si bloqueos persisten >2 semanas |
| R-API-1b | VPN inestable o ERP momentáneamente caído rompe sagas en curso | Wolverine outbox + retry exponencial (§1.8); idempotencia real obligatoria por contrato |
| R-API-2 | Equipo MYE no compromete idempotencia real en M-1 | Activar fallback degradado con M-2; followup post-piloto para migrar |
| R-API-3 | DDL del preop bloqueado | Mock con WireMock contra shape inferido; validar contra DDL real al desbloquearse |
| R-API-4 | Catálogos cambian IDs en Sinco rompiendo audit histórico | Reglas operativas vinculantes (ADR-004): IDs inmutables, descontinuar = `activo=false` |
| R-API-5 | Latencia de I-1 (búsqueda de insumos) >500ms degrada UX del wizard | Catálogo local sincronizado on-app-open (ADR-004 canonical 2026-05-05) + búsqueda local; on-demand solo si miss |
| ~~R-API-6~~ | ❌ Cerrado 2026-05-05 — U-1/U-2 eliminados del contrato. Identidad viene del host PWA, sin sync de usuarios | — |
| R-API-7 | **Descubierto 2026-05-16:** los endpoints de OT correctiva (M-1/M-1b/M-2) no existen en `Maquinaria API v1`. Los slices 1k/1l cerrados en `main` emiten `OTSolicitada_v1` sin destino real. ADR-007 queda en el aire | Pregunta urgente a David — ¿OTs en otra API del MYE núcleo? ¿planeadas? Mientras tanto, las sagas `EjecutarOTSaga` no pueden codearse |
| R-API-8 | **Descubierto 2026-05-16:** M-17 (rutinas técnicas) no existe; `EquipoDto` no trae `rutinaTecnicaId`. El handler `IniciarInspeccion` (slice 1b) no puede resolver la rutina contra proyección local | Pregunta urgente a David — exponer `rutinaTecnicaId` en `EquipoDto` o crear endpoint `GET /api/rutinas-tecnicas`. Mientras tanto, el módulo trabaja contra stub local |
| R-API-9 | **Descubierto 2026-05-16:** M-16 expone modelo per-equipo (`/api/rutinas-monitoreo?equipoId=`), incompatible con decisión 2026-05-05 de filtrado client-side por `grupoMantenimientoId`. `RutinaMonitoreoDto` mínimo no incluye items embebidos | Decidir: (a) pedir endpoint catálogo plano, (b) cambiar módulo a consulta online por equipo, (c) iterar per-equipo y componer local. Bloquea el ADR-004 sync on-app-open de `RutinaMonitoreoLocal` |
| R-API-10 | **Descubierto 2026-05-16:** convenciones globales del contrato divergen de la API real (prefix `/api/` sin `/v1`, sin paginación REST, error envelope minimal `{codigo, mensaje}`, header de idempotencia `Idempotency-Key` vs `X-Client-Command-Id` del ADR-008) | El adapter del módulo absorbe las diferencias (traduce header, tolera envelope minimal, hardcodea prefix). Documentar en `Inspecciones.Erp.Clientes` cuando se cree |

---

## 7. Histórico de cambios al contrato

| Fecha | Cambio | Disparador |
|---|---|---|
| 2026-04-29 | Documento creado consolidando enumeraciones previas | Solicitud del usuario |
| 2026-04-29 | Idempotencia real exigida en §1.4 + M-1 | Review consultor sobre ADR-003 |
| 2026-04-29 | M-2 marcado 🟣 condicional (fallback) | Review consultor sobre ADR-003 |
| 2026-04-27 | Consolidación `/repuestos` → `/insumos` | Decisión del 2026-04-27 (modelo §1.4) |
| 2026-04-28 | P-5 (`/preop/novedades/{id}/descartar`) emerge | Refactor §15: `NovedadPreopDescartada_v1` evento dedicado |
| 2026-05-04 | M-3b (detalle equipo con partes + rutinasMonitoreoIds) creado, M-4 eliminado/absorbido | Consolidación equipo+partes+rutinas en una sola llamada |
| 2026-05-04 | M-16 (catálogo `rutinas-monitoreo` sin filtro) reemplaza el `?grupo=` planteado inicialmente | Corrección Jaime 2026-05-04: rutinas se asignan per-equipo en ERP, no por grupo |
| 2026-05-04 | M-3b extendido con `rutinaTecnicaId: Guid` (singular). Cardinalidad técnica = 1 rutina por equipo, asignación per-equipo (opción β confirmada 2026-05-04) | Refinamiento Jaime: rutina técnica única por equipo, no derivada del grupo |
| 2026-05-05 | M-3b: `rutinasMonitoreoIds` retirado. Equipo trae `grupoMantenimientoId` + `grupoMantenimiento`. M-16: cada rutina trae `grupoMantenimientoId`. Cliente deriva la asignación monitoreo por grupo | Decisión Jaime 2026-05-05: rutinas-monitoreo se comparten entre equipos del mismo grupo. Sin tabla intermedia equipo↔rutina-monitoreo |
| 2026-05-05 | M-16 promovido de Fase 2 a obligatorio MVP. Conteos: 16 obligatorios MVP + 8 diferidos | Decisión Jaime 2026-05-05: monitoreo entra al MVP (roadmap 10.4 → §3.B'). M-16 se vuelve crítico para el flujo de selección de rutina al iniciar inspección de monitoreo |
| 2026-05-05 | U-1, U-2 eliminados (sección 3.6 marcada NO APLICA). Equipo Seguridad/IT sale del cross-team. Total: 27 → 25 endpoints; condicionales: 3 → 1. R-API-6 cerrado | Decisión Jaime 2026-05-05: el módulo no maneja identidad — toda la auth/identidad viene del host PWA. Sin sync de usuarios, sin catálogo local, sin app registration propio |
| 2026-05-05 | ADR-004 sync on-app-open canonical (sin cron nocturno). Catálogos M-10/M-11/M-13/M-16/M-17/I-2 cambian "Diario nocturno" → "On-app-open" en sus notas | Decisión Jaime 2026-05-05: el cliente PWA dispara sync delta cada apertura con `If-None-Match`. Persistencia IndexedDB. Sin scheduler en backend. Sin red al abrir → último cached (modo degradado). Botón admin "refrescar ahora" promovido a v1.0 |
| 2026-05-04 | M-17 (catálogo `/catalogos/rutinas`) creado como crítico MVP. Cierra gap detectado en revisión por flujos: el modelo asumía sync que el contrato no tenía | Hallazgo 1 de la revisión por flujos del 2026-05-04 |
| 2026-05-16 | **Sección §0 agregada — verificación contra `Maquinaria API v1`.** Mapping completo de 11 endpoints reales vs 25 contratados. Reclasificaciones: M-W-1 🚧→🟢 (existe), M-13 🚧→❌ (no existe). Cinco riesgos nuevos R-API-7..10. Ocho preguntas urgentes a David. Cobertura real ~44% del contrato | Inspección del swagger en vivo `http://localhost:5289/api/v4/Maquinaria/swagger/v1/swagger.json` el 2026-05-16. Detectó 4 bloqueadores (M-1/M-1b/M-2/M-17), 6 endpoints "probablemente en otra API" (P-2..P-5, I-1, I-2), 6 endpoints con divergencias materiales y 4 matches limpios |

---

## 8. Pendientes para cerrar el contrato

Lista de cosas que faltan acordar con cada equipo Sinco antes de implementar:

- [ ] **MYE**: confirmar que M-1 implementará idempotencia real con ventana ≥30 días y persistencia.
- [ ] **MYE**: validar shape exacto del response de M-1 (`OTCorrectivaIdSinco` vs `Numero` — ¿ambos campos? ¿solo número humano?).
- [ ] **Preop**: compartir DDL/shape de la novedad para definir P-1 response shape.
- [ ] **Preop**: confirmar que P-5 y P-6 aceptan el `Idempotency-Key` propuesto.
- [ ] **Preop**: confirmar catálogo cerrado de `tipo` en P-3 (asumido: `foto | pdf`; podría incluir `video`, `audio`, `documento`).
- [ ] **Preop**: confirmar que P-3 expone `urlPreview` (thumbnail) o solo guarda binario completo.
- [ ] **Preop**: confirmar campos adicionales por adjunto en P-3 (GPS de la foto, dispositivo, hash de integridad).
- [ ] **MYE núcleo**: confirmar valores del catálogo cerrado de `estado` en M-3 (asumido `operativo | en-mantenimiento | fuera-de-servicio | inactivo` — adivinanza).
- [ ] **MYE núcleo**: refinar lista de campos del item de M-3 (`marca`, `modelo`, `anio`, `numeroSerie`, `numeroEconomico` propuestos — pueden faltar o sobrar).
- [ ] **MYE núcleo (M-3b + M-16)** (actualizado 2026-05-05): confirmar que `Equipo.grupoMantenimientoId` ya existe en MYE (probable — el módulo de mantenimiento usa "grupos") y que el catálogo de rutinas-monitoreo lleva `grupoMantenimientoId` por rutina. **Sin tabla intermedia equipo↔rutina-monitoreo** — la asignación se deriva por grupo client-side. Ver `07-preguntas-destrabar-followups.md` pregunta 6 actualizada.
- [ ] **MYE núcleo (M-3b + M-17)**: confirmar la relación equipo↔rutina técnica en MYE. **Hipótesis del módulo (2026-05-04 opción β)**: campo `Equipo.rutinaTecnicaId: Guid` con cardinalidad 1 (única rutina técnica por equipo). Preguntas concretas en `07-preguntas-destrabar-followups.md` pregunta 5 actualizada.
- [ ] **MYE núcleo (M-17)**: confirmar que el ERP soporta el filtro `tipo=Tecnica` en el catálogo de rutinas (server-side implícito o vía query string). El módulo solo consume rutinas técnicas; rutinas preoperacionales y de mantenimiento no deben aparecer en el response.
- [ ] **Inventario**: validar que I-1 soporta filtrado por `parteId` (compatibilidad).
- [ ] **Inventario**: cuál es el catálogo cerrado de unidades (`Unidad` field).
- [x] ~~**Seguridad/IT**: confirmar si U-1/U-2 son necesarios o el host PWA ya propaga lo requerido.~~ **Cerrado 2026-05-05** — decisión Jaime: identidad 100% del host PWA, sin U-1/U-2.
- [ ] **Todos**: convenciones de paginación y error envelope unificadas (§1.3, §1.5).
- [ ] **Todos**: shape del `Authorization` header y formato del JWT (cierre ADR-002).

### Nuevos pendientes — verificación 2026-05-16 (ver §0.7)

- [ ] **David / MYE — bloqueante:** ¿dónde se crean las OTs correctivas? `Maquinaria API v1` no las expone (M-1/M-1b/M-2 ausentes). ¿Otra API? ¿Planeada? Sin respuesta, ADR-007 y los slices 1k/1l quedan sin destino real (R-API-7).
- [ ] **David / MYE — bloqueante:** ¿cómo obtiene el módulo la rutina técnica asignada a un equipo? `EquipoDto` no trae `rutinaTecnicaId` y M-17 (catálogo rutinas técnicas) no existe. Opciones: (a) agregar `rutinaTecnicaId` al `EquipoDto`, (b) crear `GET /api/rutinas-tecnicas`, (c) exponer endpoint detalle `GET /api/equipos/{id}` (R-API-8).
- [ ] **David / MYE — bloqueante:** modelo de rutinas-monitoreo. Maquinaria expone `/api/rutinas-monitoreo?equipoId=` (per-equipo server-side) + `/items?equipoId=&rutinaId=`. El contrato (decisión 2026-05-05) asume catálogo plano + filtro client-side por `grupoMantenimientoId`. **Son incompatibles.** Decidir cuál arquitectura queda (R-API-9).
- [ ] **David / MYE:** ¿se versionará `/v1` o se queda `/api/{recurso}` sin versión? `Maquinaria API v1` hoy expone sin `/v1`.
- [ ] **David / MYE:** ¿paginación REST estándar planeada antes de exceder 5000 registros/equipos/catálogos? Hoy hay tope hardcoded + sentinel `-1`.
- [ ] **David / MYE:** confirmar que el error envelope evolucionará a `{code, message, details, traceId}` (§1.5) o se queda en el `{codigo, mensaje}` actual. El adapter del módulo necesita saber qué tolerar.
- [ ] **David / Preop:** ¿P-2..P-5 (creación/update/adjuntos/verificación de novedad preop) viven en otra API o no existen? `Maquinaria API v1` solo expone *consulta y cierre bulk* de fallas preop.
- [ ] **David / Inventario:** ¿I-1/I-2 (búsqueda de insumos/SKUs) viven en otra API? `GET /api/productos` cubre productos pero no se documentó como SKU/insumo del contrato.
- [ ] **Módulo:** reclasificar inline los estados de los endpoints en §3.2/§3.3/§3.4 según hallazgos §0 (M-W-1 🚧→🟢, M-13 🚧→❌, etc.). Pendiente de un slice de cleanup del contrato cuando se confirmen las preguntas a David — antes de eso, mantener doble fuente (§3 = lo contratado, §0 = lo real).

---

## Referencias cruzadas

- **Modelo de dominio**: `01-modelo-dominio.md` §15 (fuente de verdad del comportamiento).
- **ADRs aplicables**: ADR-001 (REST sobre VPN), ADR-002 (auth heredada del host PWA, tentativo), ADR-003 (idempotencia OT correctiva), ADR-004 (sincronización de catálogos), ADR-005 (SignalR, no afecta este contrato).
- **Slices que dependen**: ver columna "Roadmap" de cada endpoint.
- **SOW interno**: `03-sow-consultor.md` §7 (referencia este archivo como fuente).
