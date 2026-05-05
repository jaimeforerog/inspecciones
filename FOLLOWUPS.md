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



### #7 — Inconsistencia P-5: ¿al asignar o al firmar? 🟢

**Origen:** revisión por flujos 2026-05-04 — `02f-flujo-inspeccion-tecnica-manual.md` Hallazgo 2.
**Fecha:** 2026-05-04
**Tipo:** doc · consistencia
**Descripción:** Dos lugares del contrato `06-contrato-apis-erp.md` dicen cosas distintas sobre cuándo se invoca P-5 `POST /preop/novedades/{id}/verificar`:
- **Detalle P-5 (§3.1)** dice: *"Cuándo se invoca: cuando el técnico asigna la novedad... No al firmar la inspección. La asignación dispara el outbox; la firma no necesita re-emitir."*
- **Resumen §1.8** (resiliencia outbox) dice: *"P-5 ... | Saga `CerrarInspeccionSaga` (paso 3.28)"* — el paso 3.28 corre al firmar.
Mi lectura: el detalle de P-5 ("al asignar") es la versión vigente y el resumen §1.8 quedó desactualizado. Razones: (a) P-6 descartar también dice "al tocar el ojo tachado, no al firmar" — patrón consistente entre verificar y descartar. (b) La irreversibilidad documentada en P-5 ("queda verificada incluso si la inspección se cancela") solo tiene sentido si la verificación es en tiempo real. (c) Reduce la atomicidad de la saga `CerrarInspeccionSaga` (un POST menos por hallazgo, más rápida).
**Decisión necesaria:** elegir una de las dos (sugerencia: "al asignar") y limpiar el otro lugar. Si vamos con "al asignar", §1.8 debe quitar P-5 de la columna "consumida por saga" y poner "Adapter del comando `RegistrarHallazgo` cuando Origen=PreOperacional". Si vamos con "al firmar", el detalle de P-5 debe rectificarse.
**Disparador para abrir slice:** previo al slice 3.28 / 3.49 (adapter Preop). Es decisión doc-only — al elegir cuál es la fuente de verdad, alinear ambos lugares y confirmar con el patrón unificado §15.9.
**Notas:** No bloqueante en el corto plazo. Antes de codear el adapter Preop hay que tener una sola fuente de verdad sobre el momento de invocación.



### #8 — Push SignalR: ¿cuándo exactamente? 🟢

**Origen:** revisión por flujos 2026-05-04 — `02f-flujo-inspeccion-tecnica-manual.md` Hallazgo 4.
**Fecha:** 2026-05-04
**Tipo:** doc · ADR-005
**Descripción:** ADR-005 dice "push al cliente cuando termina la integración", pero hay **dos integraciones en pipeline** tras la firma con OT: M-1 (POST OT) y M-1b (PDF adjunto). Tres opciones para el timing del push SignalR:
- **(a)** Push tras éxito de M-1 (OT generada con número visible inmediatamente; PDF puede tardar más, se notifica aparte si falla). UX más reactiva.
- **(b)** Push solo tras M-1 + M-1b ambos exitosos (estado "OT con PDF lista"). UX más conservadora — usuario espera más.
- **(c)** Dos pushes separados (uno por cada integración). UX más informativa pero potencialmente ruidosa.
Mi sugerencia: **(a)** explicitada en ADR-005 — push tras M-1 con `OTGenerada` + push opcional tras M-1b solo si falla. El técnico ve el número de OT lo antes posible (lo más útil); el adjunto PDF es retroactivo y no bloquea operaciones.
**Decisión necesaria:** elegir entre (a)/(b)/(c) y actualizar ADR-005 (`01-modelo-dominio.md §14`) + slice 3.51 (`InspeccionesHub`) + slice 3.27d (saga PDF).
**Disparador para abrir slice:** previo al slice 3.51 o 3.27d. Decisión doc-only — actualizar ADR-005 antes de codear el hub.
**Notas:** No es blocker técnico — el técnico puede ver el número de OT inmediatamente tras M-1 (lo más útil), y el PDF adjunto es retroactivo.



### #9 — Prefetch-by-proyecto vs sync nocturno completo de catálogos grandes 🟢

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
**Disparador para abrir slice:** datos reales del piloto (Fase 9) que evidencien que el sync nocturno completo está saturando (cuota IndexedDB en iOS, bandwidth VPN, tiempo de sync nocturno >X minutos, o quejas de técnicos por lentitud al arrancar). Antes de eso, NO abrir — la complejidad de cachear "lo correcto" es alta y el ahorro chico.
**Notas:** Si emerge en piloto, el camino es híbrido (no on-demand puro): catálogos chicos siguen con sync completo + catálogos grandes pasan a prefetch-by-proyecto + lookup on-demand. Ese diseño preserva offline duro mientras reduce el footprint. Cross-ref: ADR-008 sección "Comportamiento por plataforma" (riesgo iOS) y `08-volumenes-clientes-erp.md` (volúmenes reales).



### #10 — Limpiar residuo de `Items[]` + `ActividadId` en definición §12.11.1 de Rutina técnica 🟢

**Origen:** revisión del ADR-004 punto 1 (cobertura M-17), 2026-05-05 — detectada inconsistencia entre §12.10.3-§12.10.5 (rutina técnica = filtro del catálogo de partes, sin items con actividades) y §12.11.1 (refinamiento mayo 2026 que reintrodujo `IReadOnlyList<ItemRutina> Items` con `ActividadId` en cada item).
**Fecha:** 2026-05-05
**Tipo:** doc · consistencia
**Descripción:** §12.10.3 elimina `ItemRutinaId` del Hallazgo y §12.10.5 explicita que la rutina técnica es **filtro del catálogo de partes**, no checklist con items. Sin embargo §12.11.1 (refinamiento mayo 2026) presenta `Rutina` con `IReadOnlyList<ItemRutina> Items` y `ItemRutina(ItemId, ActividadId, Instruccion, Obligatorio)`. Es residuo del modelo previo no limpiado en el refinamiento de IDs (Guid→int). La verdad vigente confirmada (2026-05-05) es §12.10. Hay que limpiar §12.11.1 para que el shape de Rutina técnica sea coherente: solo metadata + ParteId mayor (id, codigo, nombre, tipo, grupoMantenimiento, parteId, parteCodigo, sincronizadoEn), sin `Items[]`.
**Disparador para abrir slice:** previo al slice del adapter M-17 o del handler `IniciarInspeccion`. Decisión doc-only — actualizar §12.11.1 antes de codear el adapter.
**Notas:** ADR-004 ya documenta el shape mínimo correcto (post-patch 2026-05-05 §9.15 "Refinamientos posteriores"). Este followup es solo limpieza del modelo §12.11.1 para no dejar definición contradictoria entre dos secciones del mismo doc. Cross-ref: ADR-004 punto 1.




## Cerrados

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
