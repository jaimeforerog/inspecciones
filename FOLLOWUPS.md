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
**Fecha:** 2026-04-29
**Tipo:** API · doc
**Descripción:** Definir y agregar al roadmap dos endpoints (o variantes filtrables de los existentes): `GET /inspecciones/{id}/importables-preop?parte=&desde=&hasta=&usuario=` (lista novedades preop verificables del equipo, filtrable) y `GET /equipos/{equipoId}/seguimientos-importables?parte=&desde=&hasta=&usuario=` (lista seguimientos abiertos importables a la inspección actual). El segundo puede ser la misma proyección de §15.12.4 con filtros adicionales. El primero requiere proyección lateral del catálogo de novedades preop pendientes — confirmar si es lectura directa al ERP (`GET /api/v1/preop/novedades?...`) o read model local.
**Disparador para abrir slice:** previo al slice del wizard de hallazgo (paso 3.37 `POST /hallazgos`). Resolver con David si es lectura directa o local antes de codear el adapter.
**Notas:** No bloqueante hasta que Santiago llegue al slice de importación. Daniel ya está iterando UX en Figma.




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
