# Followups — Inspecciones Sinco MYE

Backlog de deuda técnica sin slice propio. Cada item lo abre `reviewer` con veredicto `approved-with-followups` (o el orquestador en `azure-ops` / `infra-wire`).

**Convenciones:**

- ID: `#N` secuencial.
- **Estado:** 🟢 abierto · 🟡 en progreso · ✅ cerrado · ❄️ congelado.
- Cada followup cierra cuando entra a un slice o se decide explícitamente que no aplica.

## Abiertos

### #1 — Reportería de seguimientos sin tipo/causa 🟢

**Origen:** review consultor 2026-04-29 sobre `HallazgoRegistrado_v1` con `RequiereSeguimiento`. Decisión: relajar I-H4 (§15.3) para no exigir `TipoFallaId` / `CausaFallaId` en hallazgos con `AccionRequerida ∈ {NoRequiereIntervencion, RequiereSeguimiento}`. Trade-off aceptado: reportería degradada sobre seguimientos hasta que escalen.
**Fecha:** 2026-04-29
**Tipo:** doc · domain extension
**Descripción:** Medir en piloto si la falta de tipo/causa en hallazgos `RequiereSeguimiento` degrada la utilidad de los reports al supervisor. Si sí, evaluar dos opciones: (a) capturar `TipoFallaId` en mini-modal/wizard paso 1 (volver a endurecer I-H4 con MoreFields en UI); (b) inferir tipo/causa desde la cadena de escalación cuando exista, en el read model de seguimientos.
**Disparador para abrir slice:** ≥3 supervisores reportan en feedback de piloto que necesitan filtrar/agrupar seguimientos por tipo de falla, o que falta de clasificación impide priorizar revisión de seguimientos vencidos.
**Notas:** Este followup queda congelado hasta tener datos del piloto (Fase 9). No abrir slice hasta entonces.



### #5 — Endpoints de listado de hallazgos importables con filtros 🟢

**Origen:** notas reunión diseño 2026-04-29 — "filtros para importar hallazgos deben estar disponibles antes de mostrar el listado, permitiendo filtrar por parte, fecha y usuario".
**Fecha:** 2026-04-29 · **Refinado:** 2026-05-04
**Tipo:** API · doc
**Descripción:** Definir y agregar al roadmap dos endpoints (o variantes filtrables de los existentes): `GET /inspecciones/{id}/importables-preop?parte=&desde=&hasta=&usuario=` (lista novedades preop verificables del equipo, filtrable) y `GET /equipos/{equipoId}/seguimientos-importables?parte=&desde=&hasta=&usuario=` (lista seguimientos abiertos importables a la inspección actual). El segundo puede ser la misma proyección de §15.12.4 con filtros adicionales. El primero requiere proyección lateral del catálogo de novedades preop pendientes — confirmar si es lectura directa al ERP (`GET /api/v1/preop/novedades?...`) o read model local.
**Cross-references (2026-05-04):**
- **`02f-flujo-inspeccion-tecnica-manual.md` Hallazgo 3**: el flow review confirmó que P-1 hoy solo soporta `q=` libre + paginación. Si followup #5 se materializa, P-1 necesita extender query params (parte, fecha, usuario).
- **`07-preguntas-destrabar-followups.md` pregunta 7**: redactada y pendiente de enviar a David — confirma forma exacta del filtro server-side y si los filtros aplican al endpoint del preop directamente.
**Disparador para abrir slice:** previo al slice del wizard de hallazgo (paso 3.37 `POST /hallazgos`). Resolver con David si es lectura directa o local antes de codear el adapter.
**Notas:** No bloqueante hasta que Santiago llegue al slice de importación. Daniel ya está iterando UX en Figma.



### #9 — Prefetch-by-proyecto vs sync completo de catálogos grandes 🟡 disparador alcanzado (piloto grande)

**Origen:** conversación de diseño 2026-05-05 sobre eficiencia de la sincronización de catálogos. Usuario propuso reemplazar el sync nocturno completo (ADR-004) por cache on-demand puro, argumentando que el técnico solo usa una fracción mínima del catálogo (~1 %).
**Fecha:** 2026-05-05
**Tipo:** doc · ADR-004 · perf
**Descripción:** Evaluar si los catálogos grandes (`equipos` ~10K, `repuestos`/`SKUs` ~190K) deberían moverse de sync nocturno completo (ADR-004 vigente) a un patrón **prefetch-by-proyecto-asignado + lookup on-demand**. Los catálogos chicos (causas, tipos de falla, partes, actividades, ubicaciones — ~5K entidades, <500 KB total) seguirían con sync nocturno completo porque es imposible predecir cuáles usará el técnico. La propuesta es híbrida, no reemplazo total: al login, prefetch de equipos/SKUs del proyecto del técnico (~500-1000 equipos). Si el técnico es reasignado, el host PWA dispara re-prefetch. Lookup on-demand cubre casos fuera del prefetch cuando hay red.
**Razones para no actuar ahora (mantener ADR-004 vigente):**
- Volumen real cabe holgado: 260K entidades comprimidas en JSON ≈ 5-15 MB en IndexedDB (vs 90 MB de fotos OPFS por jornada). El "derroche" es marginal.
- Cache on-demand puro **rompe el caso de uso central** — técnico llega a obra sin red en equipo nunca tocado, no puede iniciar.
- Equipos nuevos (altas) no se descubrirían hasta que alguien los busque con red.
- iOS ITP 7 días borra cache; con sync nocturno se reconstruye sola, con on-demand el técnico recarga durante la jornada (potencialmente sin red).
- Reasignación de técnico a otro proyecto un lunes: con cache on-demand puro, ese día queda bloqueado hasta que tome red.
**Disparador para abrir slice:** datos reales del piloto (Fase 9) que evidencien que el sync completo de un catálogo grande está saturando alguno de los **thresholds objetivos (decisión 2026-05-05, análisis específico de insumos + rutinas monitoreo)**:

- Tamaño promedio del response al sync de un catálogo > **2 MB en wire (gzip)** sostenidamente.
- Cuota IndexedDB en iOS Safari por encima del **50 %** atribuible a un solo catálogo (riesgo de eviction prematura por ITP).
- Bandwidth de sync por técnico > **5 MB/día sostenido**.
- Quejas de técnicos por "lentitud al arrancar la app" trazables al sync de catálogos.

**Caso concreto identificado (insumos):** clientes con > 20K SKUs (volumen máx observado: 34,428 — ver `08-volumenes-clientes-erp.md` §2). En piloto chico (avg 7K SKUs ≈ 400 KB wire) ADR-004 estándar es suficiente; en piloto intensivo (>20K SKUs ≈ >1.5 MB wire) **activar followup pre-Fase 3 frontend**. Rutinas de monitoreo (Fase 2) tienen volumen <1 MB wire incluso en outliers — caso resuelto por ADR-004 estándar.

**Disparador alcanzado (decisión 2026-05-05):** **el cliente piloto será uno grande** (decisión Jaime). Confirmado el caso problemático antes de Fase 3 frontend. Followup pasa de diferido (esperar piloto) a **agenda activa** — el patrón híbrido para insumos debe estar diseñado e implementado antes de que el primer slice del frontend consuma `RepuestoLocal`.

**Decisiones bloqueantes pendientes** (preguntas 9, 10 y nueva a Daniel en `07-preguntas-destrabar-followups.md`):

1. Cardinalidad real SKU-Proyecto en el ERP (catálogo central vs scoped). Make-or-break del approach: si los SKUs son centrales y los técnicos efectivamente acceden a cualquiera del catálogo entero, el filtro `?proyecto=` no reduce nada y el patrón falla.
2. ¿Puede el ERP exponer `GET /api/v1/insumos?proyecto={id}` (lista filtrada) y `GET /api/v1/insumos/{id}` (lookup individual)?
3. UX cuando un técnico necesita un SKU "fuera de su prefetch" sin red — opciones (a) bloqueo, (b) hallazgo sin SKU, (c) cache de SKUs recientes.

Sin las tres respuestas, redactar el ADR de extensión a ADR-004 es prematuro.

**Cliente piloto exacto:** TBD — preguntar al equipo de Sinco. Saber el cliente determina volumen real de SKUs (data agregada por cliente para insumos no estaba en el corte 2026-04-30, solo total).

**Notas:** Si emerge en piloto, el camino es híbrido (no on-demand puro): catálogos chicos siguen con sync completo + catálogos grandes pasan a prefetch-by-proyecto (`GET /api/v1/<catalogo>?proyecto={id}`) + lookup on-demand individual (`GET /api/v1/<catalogo>/{id}`). Ese diseño preserva offline duro mientras reduce el footprint. **Mitigación complementaria sin cambio de arquitectura (Opción 2 de la decisión 2026-05-05):** pedir a David consolidar bumps de ETag de insumos en un único `version++` diario (ver pregunta nueva en `07-preguntas-destrabar-followups.md`). Cross-ref: ADR-008 sección "Comportamiento por plataforma" (riesgo iOS) y `08-volumenes-clientes-erp.md` §2 (volúmenes reales) y §3 hallazgo 7 (dimensionamiento Azure).



## Cerrados

### #10 — Limpiar residuo de `Items[]` + `ActividadId` en definición §12.11.1 de Rutina técnica ✅

**Origen:** revisión del ADR-004 punto 1 (cobertura M-17), 2026-05-05.
**Fecha apertura / cierre:** 2026-05-05 / 2026-05-05
**Resolución:** §12.11.1 limpiado — eliminados `IReadOnlyList<ItemRutina> Items` y el record `ItemRutina(ItemId, ActividadId, Instruccion, Obligatorio)` del shape vigente de `Rutina` técnica. Definición ahora coherente con §12.10.5 (rutina = filtro del catálogo de partes, no checklist navegable). Lista de "Cambios respecto a versión previa" actualizada para documentar la eliminación con cross-ref a §12.10.5 y a ADR-004 §9.15 "Refinamientos posteriores" punto 1. Las referencias residuales a `ItemRutina` en §12.7 (líneas 1311, 1448) **no se tocaron** — están dentro de secciones marcadas como históricas/superseded (banner línea 1409 + 1437) y son audit trail legítimo del proceso de reconciliación con plantillas Excel del ERP. Las notas en §12.10 (1586, 1590, 2260) que explican por qué `ItemRutinaId` se eliminó del Hallazgo siguen siendo contenido válido de trazabilidad. La rutina de monitoreo (§12.11.5) **sí** mantiene `IReadOnlyList<ItemRutinaMonitoreo>` con `EvaluacionEsperada` — entidad distinta con flujo distinto (Fase 2), no afectada por este followup.

### #8 — Push SignalR: ¿cuándo exactamente? ✅

**Origen:** revisión por flujos 2026-05-04 — `02f-flujo-inspeccion-tecnica-manual.md` Hallazgo 4.
**Fecha apertura / cierre:** 2026-05-04 / 2026-05-05
**Resolución:** opción **(a)** confirmada — push `OTGenerada` apenas M-1 completa (sin esperar al PDF), silencio durante M-1b si va bien, push `AdjuntoPdfFallido` solo si M-1b falla. Razones: el técnico necesita el número de OT en segundos tras firma para validación inmediata; esperar al PDF (~minutos) genera percepción de bloqueo; el PDF en sí es retroactivo (visible al consultar la OT en MYE), solo el caso de error requiere notificación reactiva. Aplicado en ADR-005 (`01-modelo-dominio.md §14`): tabla del catálogo de eventos SignalR ampliada con columna "Notas" + nuevo evento `AdjuntoPdfFallido` mapeado a `AdjuntoPdfFallido_v1`. Nueva sub-sección "Patrón de timing M-1 vs M-1b" documenta la decisión y rechaza explícitamente las opciones (b) y (c) con razones.

### #7 — Inconsistencia P-5: ¿al asignar o al firmar? ✅

**Origen:** revisión por flujos 2026-05-04 — `02f-flujo-inspeccion-tecnica-manual.md` Hallazgo 2.
**Fecha apertura / cierre:** 2026-05-04 / 2026-05-05
**Resolución:** opción **"al asignar"** confirmada como fuente de verdad. Razones: (a) consistencia con P-6 descartar que también es "al tocar el icono ojo tachado, no al firmar"; (b) la irreversibilidad documentada en §3.1 P-5 ("queda verificada incluso si la inspección se cancela") solo tiene sentido si la verificación se sincroniza en tiempo real, no al cierre; (c) reduce la atomicidad de `CerrarInspeccionSaga` (un POST menos por hallazgo). Aplicado: dos inconsistencias corregidas en `06-contrato-apis-erp.md` — línea 82 (resumen §1.8 outbox) y línea 115 (tabla catálogo P-5) ahora dicen "Adapter del comando `RegistrarHallazgo` con `Origen=PreOperacional`" en lugar de "Saga `CerrarInspeccionSaga`". Detalle §3.1 P-5 ya estaba correcto y queda como fuente de verdad. Patrón unificado §15.9 del modelo confirmado consistente con esta decisión.

### #6 — Daniel — rol formal en el equipo ✅

**Origen:** notas reunión diseño 2026-04-29 — Daniel presenta wireframes y posee Figma.
**Fecha apertura / cierre:** 2026-04-29 / 2026-04-29
**Resolución:** Jaime confirmó que Daniel es **diseñador UX** encargado de presentar mockups en Figma. `project_equipo.md` actualizado.

### #2 — Semántica de "cambiar fecha" en pantalla inicial de inspecciones ✅

**Origen:** notas reunión diseño 2026-04-29 — Daniel mencionó "permitiendo cambiar la fecha y siguiendo la lógica del preoperacional" en pantalla 1.
**Fecha apertura / cierre:** 2026-04-29 / 2026-04-30
**Resolución:** Mock del diseño (image2 de `Plantillas Excel/mock del diseño.docx`) muestra explícitamente un **calendario para elegir fecha distinta a hoy** en pantalla 2 ("¿Con qué equipo vas a trabajar?" con calendario de Abril 2026). Resuelto con opción **(b) — input al iniciar inspección**, no filtro de bandeja. Activa amendment a §15.4 con campo `FechaReportada` separado del timestamp del sistema. Detalle modelado en `01-modelo-dominio.md` §15.4 (entrada del evento `InspeccionIniciada_v1`).

### #3 — Equipos con múltiples medidores (medidor 1 + medidor 2) ✅

**Origen:** notas reunión diseño 2026-04-29 — pantalla 1 muestra "medidor uno" y "medidor dos". `EquipoLocal.HorometroActual` actual es singular (`decimal?`).
**Fecha apertura / cierre:** 2026-04-29 / 2026-04-30
**Resolución:** Mock del diseño (image4 de `Plantillas Excel/mock del diseño.docx`) muestra explícitamente **Medidor 1: Km 123.456,78** y **Medidor 2: Hr 12.43** como norma en la pantalla "Previsualización equipo" (modelo Caterpillar D11T Custom). Confirmado: 2 medidores son la norma. Activa amendment a §12.7 `EquipoLocal` (extendido a `MedidorPrimario` + `MedidorSecundario`) y a `InspeccionIniciada_v1` con lecturas de ambos medidores. Detalle modelado en `01-modelo-dominio.md` §12.7 y §15.4.

### #4 — "Proyecto" vs "Obra" — naming ✅

**Origen:** notas reunión diseño 2026-04-29 — Jaime/Sergio sugirieron "el campo 'proyecto' se mantendría solo si es relevante". Modelo usaba `ObraId` / `ObraLocal`.
**Fecha apertura / cierre:** 2026-04-29 / 2026-04-30
**Resolución:** opción **(a) sinónimos**, palabra que queda = **"proyecto"** (decisión Jaime 2026-04-30). Sub-decisión: **(B) módulo usa `Proyecto` interno; ERP mantiene `Obra` en URLs/DTOs**. El adapter del módulo traduce `Proyecto` ↔ `Obra` al hablar con MYE. Aplicado: rename masivo de identificadores (`ObraId` → `ProyectoId`, `ObraLocal` → `ProyectoLocal`, etc.) en `01-modelo-dominio.md`, `04-brief-consultor-producto.md`, `roadmap.md`, `02d-wireframes-seguimientos.html`, `05-catalogo-eventos.md`, `03-sow-consultor.md`. Endpoints del módulo: `?obra=` → `?proyecto=`. URLs del ERP `/api/v1/catalogos/obras` y `?obra=` se mantienen literales con nota de aclaración. **Pregunta menor pendiente con David**: ¿el ERP corporativo planea estandarizar a "proyecto" en algún momento? — ver doc 07.

---

## Plantilla de entry

```markdown
### #N — {título corto} 🟢

**Origen:** slice {N}-{slug} review §3 hallazgo #X
**Fecha:** YYYY-MM-DD
**Tipo:** deuda técnica · domain extension · perf · seguridad · doc
**Descripción:** una o dos frases que digan qué hay que hacer y por qué.
**Disparador para abrir slice:** condición que justifica priorizarlo.
**Notas:** opcional.
```
