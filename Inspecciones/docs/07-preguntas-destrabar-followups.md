# Preguntas para destrabar followups #2–#5

**Fecha:** 2026-04-30
**Propósito:** disparar respuestas concretas a 4 followups abiertos en `FOLLOWUPS.md` antes de iniciar Fase 3 (slice 3.36 `POST /inspecciones`).
**Audiencias:** Daniel (UX), David (devs ERP/APIs), Sergio (consultor producto).

> Cada bloque está redactado para copiar/pegar tal cual a Slack, email o llevar a reunión. Cada pregunta declara **qué desbloquea** para que el destinatario priorice.

---

## Para Daniel (UX) — preguntas pendientes derivadas del mock 2026-04-30

> **Followup #2 (cambiar fecha) — RESUELTO** el 2026-04-30 con respuesta (b) a partir del mock del diseño (image2 de `Plantillas Excel/mock del diseño.docx`): la fecha es input al iniciar inspección. Modelado en §15.4 con campo `FechaReportada` y validación I-I3 (§15.7). Sin pregunta abierta.

> **Followup #3 (medidores) — RESUELTO** el 2026-04-30: el mock confirma 2 medidores como norma (image4). Modelado en §12.7 con `MedidorPrimario` + `MedidorSecundario`. Sin pregunta abierta.

### Pregunta nueva — Atajo "Seguimiento" inline en lista de novedades preop

**Contexto:** el icono "ojo tachado" del mock (image12) ya está cerrado: descarte rápido individual con motivo autogenerado (decisión Jaime 2026-04-30, ver §15.9 "Descarte rápido inline" del modelo). Pero **el botón inline 🟠 Seguimiento que estaba previsto en §15.9 NO aparece en el mock**. Solo hay 2 acciones por novedad: Importar (botón principal) + ojo tachado (descartar).

**Pregunta:** ¿cómo marca el técnico una novedad como "requiere seguimiento" en este diseño?

Hipótesis: el técnico tap "Importar" → entra al wizard → elige "Requiere seguimiento" en paso 1 (radio naranja del image7) → guarda directo (sin paso 2 porque seguimiento no requiere análisis técnico).

Si esa es la intención, la pregunta operativa: **¿qué pasa cuando el técnico tiene 10 novedades repetidas que solo quiere dejar en seguimiento?** El descarte rápido tiene 1 tap. Importar → wizard → elegir radio → guardar tiene ~4 taps. Para 10 novedades son 40 taps + tiempo de carga del wizard cada vez.

Tres opciones a evaluar:
- **(A) Mantener como está**: seguimiento solo dentro del wizard, asumiendo caso operativo dominante es "1-2 seguimientos por inspección" (no decenas).
- **(B) Agregar icono de seguimiento inline** (algo tipo 🟠 banderita) al lado del ojo-tachado. El técnico mete una novedad en seguimiento con 1 tap, motivo autogenerado igual que el descarte.
- **(C) Wizard con paso "qué hacer con esta" simplificado** — un solo paso con 3 radios (intervención / seguimiento / descartar como reemplazo del ojo-tachado).

**Qué desbloquea:**
- Cierre del modelo §15.9.
- UX final de la pantalla "Importar".

**Bloquea:** slice 3.37 (wizard de hallazgo) si la respuesta es (B) o (C) — afecta los comandos disponibles y el modelo del wizard.

### Pregunta nueva — Badge SLA del seguimiento: color naranja a los 20 días (image10)

**Contexto:** §15.8.6 del modelo define los buckets de antigüedad del seguimiento por color:
- Azul: `< 30 días`
- Naranja: `30 – 90 días`
- Rojo: `≥ 90 días`

Sin embargo, el mock (image10) muestra un seguimiento con badge **"Con seguimiento | Hace 20 días"** en color **naranja** — incoherente con el bucket azul que correspondería a < 30 días.

**Pregunta:** dos interpretaciones posibles:

- **(A) Color por estado, no por SLA**: el naranja en el mock representa "estado=Seguimiento" (categoría visual del badge), no antigüedad. El SLA visual del modelo (§15.8.6) sería un badge **adicional** o un detalle dentro del badge. Bajo esta interpretación, el modelo y el mock NO son contradictorios — solo representan dimensiones distintas.
- **(B) Recalibración del SLA**: los buckets `<30 / 30-90 / 90+` son demasiado lejanos para la operación real. Quizás `<7 / 7-30 / 30+` describe mejor la realidad del usuario. Si es así, hay que ajustar §15.8.6 + `BadgeSla` derivado en `SeguimientosAbiertosPorEquipoView` (§15.12.4).

**Qué desbloquea:**
- Cierre de §15.8.6 + §15.12.4.
- Definición visual unificada para el frontend.

**Bloquea:** slice del frontend de bandeja de seguimientos (5.21) y posibles ajustes a la proyección §15.12.4. **No bloquea** los slices de backend del aggregate `SeguimientoHallazgo` — el `BadgeSla` es derivación de presentación, no estado persistido.

---

## Para David (devs ERP)

> **Followup #3 (medidores) — RESUELTO** el 2026-04-30 con respuesta afirmativa (mock del diseño image4 muestra 2 medidores como norma). Pregunta menor pendiente para David: ¿el ERP define la **semántica de cuál medidor es primario** por grupo de mantenimiento (BULLDOZER → primario=Hr, MOTONIVELADORA → primario=Km), o lo decide el técnico al iniciar inspección? El módulo asume "lo define el ERP" y trae primario+secundario en `EquipoLocal` desde el sync de catálogo. Si la respuesta es "decisión del técnico", se ajusta el contrato del endpoint `POST /inspecciones` (paso 3.36) para aceptar identificador de cuál es primario.

### Pregunta 1 — Campo `responsableCosto` en `POST /api/v1/mye/ot-correctivas` (decisión 2026-04-30)

**Contexto:** la generación de OT en MYE requiere indicar **quién asume el costo**. Decisión confirmada con Jaime el 2026-04-30: enum cerrado con dos valores — `Proyecto` (el proyecto donde está el equipo asignado) o `DepartamentoEquipos` (el área que administra los equipos como activo). El aprobador con capability `generar-ot` lo elige al disparar el comando `GenerarOT` (paso 3.42b del roadmap, ADR-007).

**El campo ya existe del lado ERP MYE** según Jaime, pero no conocemos el nombre exacto del DTO ni los valores literales que MYE acepta.

**Pregunta concreta:**
1. ¿Cuál es el **nombre exacto** del campo en el body de `POST /api/v1/mye/ot-correctivas`? (`responsableCosto`, `centroCosto`, `tipoResponsable`, `cuentaCosto`, otro)
2. ¿Los valores son **strings literales** (`"Proyecto"`, `"DepartamentoEquipos"`), **códigos cortos** (`"PROY"`, `"DEPEQ"`), o **identificadores numéricos** (1, 2)?
3. ¿Es **obligatorio** en el body o admite default?
4. ¿Existe algún caso adicional que MYE soporte que valga la pena considerar (ej. `Garantia`, otro proyecto distinto al del equipo)? Jaime confirmó que para el módulo son solo 2 — pero conviene saber si MYE acepta más valores que ignoraríamos.

**Qué desbloquea:** payload final del adapter `POST /mye/ot-correctivas` (paso 3.27 del roadmap) y test de adapter con WireMock. Por ahora el modelo (§17 ADR-007) y el contrato (§4.9 de `06-contrato-apis-erp.md`) usan los strings `"Proyecto"` / `"DepartamentoEquipos"` como placeholder.

**Bloquea:** test de integración del adapter MYE en Fase 3 — pero **no bloquea** el slice 3.42b (`POST /generar-ot`), que solo modela el comando con el enum interno.

---

### Pregunta 2 — Endpoint de dictamen vigente del equipo en MYE (decisión 2026-04-30, observación Sergio)

**Contexto:** Sergio observó el 2026-04-30 que MYE necesita un servicio para actualizar el dictamen del equipo (`PuedeOperar` / `ConRestriccion` / `NoPuedeOperar`) cada vez que se firma una inspección, **incluso cuando no se genera OT**. Hoy el dictamen viaja a MYE solo embebido en `POST /api/v1/mye/ot-correctivas` — pero ese flujo solo aplica cuando hay hallazgos con `RequiereIntervencion`. Para inspecciones que cierran sin OT, el dictamen no llega a MYE actualmente.

**Lo que estamos proponiendo del lado del módulo:** invocar `PUT /api/v1/equipos/{equipoCodigo}/dictamen-vigente` en toda firma (con o sin OT) desde una nueva saga `SincronizarDictamenVigenteSaga`. Detalle en §3.4 de `06-contrato-apis-erp.md` (M-W-1) y §17 ADR-007 del modelo.

**Pregunta concreta:**
1. ¿El campo "dictamen vigente" o equivalente **ya existe** en la entidad `Equipo` de MYE? ¿Cómo se llama exactamente?
2. Si existe, ¿hay endpoint de actualización ya disponible? ¿Cuál es el path real?
3. Si NO existe, ¿es viable que MYE núcleo agregue:
   - Campo `DictamenVigente` (o nombre equivalente) en `Equipo`.
   - Endpoint `PUT /api/v1/equipos/{equipoCodigo}/dictamen-vigente` con el body propuesto en §3.4 M-W-1.
   - Idempotencia con `Idempotency-Key=InspeccionId` (replay inocuo).
4. ¿Qué valores admite el campo? (mismas tres preguntas que `responsableCosto`: literal vs código corto vs id numérico).
5. ¿Hay restricciones de transición? Ej. ¿MYE rechaza pasar de `NoPuedeOperar` a `PuedeOperar` directamente sin que pase por mantenimiento? Si sí, el adapter debe manejar esos 4xx.
6. ¿Existe lectura análoga (`GET /equipos/{id}/dictamen-vigente` o el dictamen vigente viene en el detalle del equipo `GET /equipos/{id}`)? — Útil para reconciliación y para mostrar el último dictamen en pantalla 1 (selector de equipo).

**Qué desbloquea:**
- Slice 3.27c del roadmap (adapter del PUT) y la saga `SincronizarDictamenVigenteSaga`.
- Cierra la regla #11 del brief consultor (V-F4 §15.5 confirma dictamen siempre obligatorio — Sergio 2026-04-30).

**Bloquea:** test de adapter del paso 3.27c y completar §17 con el contrato real (hoy es propuesto). **No bloquea** el comando interno `FirmarInspeccion`, que solo emite `DictamenEstablecido_v1` y `InspeccionFirmada_v1` sin importar el estado del sync con MYE.

---

### Pregunta 3 — Endpoint de descarte de novedades preop (decisión final 2026-04-30)

**Contexto:** la observación inicial de Sergio (caso de duplicados) llevó a modelar primero un comando bulk con motivo manual, pero **fue superseded el mismo día** tras revisar el mock del diseño (image12 de `Plantillas Excel/mock del diseño.docx`). El flujo final es **descarte individual rápido con motivo autogenerado**: tap en icono "ojo tachado" → motivo plantilla `"Cerrado por {usuario} el {fecha} UTC desde Inspecciones"` → cierra una sola novedad. El módulo solo invoca con **array de 1 novedad** en MVP, pero el contrato bulk del ERP se preserva por flexibilidad futura (sagas de limpieza, batch admin).

**Lo que estamos proponiendo del lado del módulo:** ver §3.2 P-6 de `06-contrato-apis-erp.md`. Endpoint: `POST /api/v1/preop/novedades/descartar` (sin `{id}` en path) con body `{inspeccionId, novedadIds: [], motivo, descartadaPor}`. El módulo **siempre** envía `novedadIds` con un solo elemento.

**Pregunta concreta:**

1. **Path final**: ¿es `/preop/novedades/descartar` (sin id) o el ERP prefiere otra forma (ej. `/preop/novedades/descartar-bulk`, `/inspecciones/{id}/preop/descartar`, otro)?
2. **Idempotency-Key**: opciones:
   - `{inspeccionId}-{novedadId}` — determinístico, simple para arrays de 1.
   - `{comandoId}` (UUID generado por el módulo) — opaco, escala a futuros bulk.
   Por defecto vamos con la primera (`{inspeccionId}-{novedadId}`) por simpleza en MVP.
3. **¿El ERP exige array, acepta valor escalar, o ambos?** El módulo siempre enviará array, pero conviene saber si el ERP rechaza arrays con un solo elemento.
4. **¿El endpoint actual `/preop/novedades/{id}/descartar` (singular) sigue existiendo o se reemplaza?** Si ya está en producción para otros consumidores del ERP, no lo deprecamos — el módulo solo usa el nuevo path bulk-capable.

**Qué desbloquea:**
- Slice 3.29 (adapter Preop) y 3.49 (endpoint REST del módulo).

**Bloquea:** test del adapter (paso 3.29) con WireMock — necesitamos el contrato real para los stubs. **No bloquea** el comando interno `DescartarNovedadPreop`, que opera sobre el aggregate sin saber del ERP.

---

### Pregunta 4 — Endpoint de adjuntos para OT correctivas (decisión 2026-04-30, observación Sergio)

**Contexto:** Sergio observó el 2026-04-30 que el PDF de la inspección debe llegar como adjunto a la OT correctiva en MYE — *"cuando se genere una OT, debe llegar como adjunto a esta, el PDF de la inspección"*. El módulo genera el PDF localmente al firmar (QuestPDF, decisión 2026-04-30), lo guarda en Azure Blob, y la `EjecutarOTSaga` lo adjunta a la OT tras crearla en MYE. Detalle en §17 ADR-007 sub-sección "Generación de PDF de inspección y adjunto a OT" del modelo, y M-1b de `06-contrato-apis-erp.md`.

**Lo que estamos proponiendo del lado del módulo:** `POST /api/v1/mye/ot-correctivas/{otCorrectivaIdSinco}/adjuntos` con `multipart/form-data`, body con `file` (PDF), `descripcion`, `tipo="InspeccionTecnica"`, `inspeccionId`, `sha256`. Idempotency-Key `{InspeccionId}-pdf` con upsert recomendado.

**Pregunta concreta:**

1. **¿El endpoint ya existe en MYE?** ¿Hay endpoint de adjuntos para OT correctivas hoy? Si sí, ¿cuál es el path real y el contrato del body?
2. Si NO existe, ¿es viable que MYE núcleo lo agregue con el contrato propuesto en M-1b? Dependencia bloqueante para que el PDF llegue al ERP.
3. **Tamaño máximo del adjunto**: ¿qué límite admite el ERP? El PDF estándar de inspección con miniaturas de fotos pesa entre 1-5 MB. Sugerencia: ≥ 10 MB.
4. **Tipos MIME y catálogo de `tipo`**: ¿el ERP soporta `application/pdf`? ¿Existe catálogo de tipos de adjunto donde "InspeccionTecnica" sea valor admitido (string literal, código, id numérico)?
5. **Comportamiento ante replay con misma Idempotency-Key**: necesitamos **upsert por (`otCorrectivaIdSinco`, `inspeccionId`, `tipo`)** — replay no debe duplicar adjuntos. ¿MYE puede garantizarlo?
6. **¿Hay endpoint de descarga (`GET /mye/ot-correctivas/{id}/adjuntos/{adjuntoId}` o similar)?** Útil para verificar integridad post-upload (módulo descarga, calcula sha256, compara) y para uso futuro del módulo.
7. **¿Cuántos adjuntos por OT acepta MYE?** El módulo solo enviará 1 PDF por inspección, pero saber el límite ayuda al diseño de tests.

**Qué desbloquea:**
- Slice 3.27d (adapter PUT del adjunto).
- Slice 3.24d (saga `EjecutarOTSaga` extendida) y 3.27e (servicio QuestPDF).

**Bloquea:** test de adapter del paso 3.27d con WireMock — necesitamos el contrato real para los stubs. **No bloquea** la generación local del PDF (paso 3.27e) ni la saga `GenerarPdfInspeccionSaga` (3.24d), que son internas al módulo.

---

### Pregunta 5 — Granularidad real de rutinas técnicas en clientes outlier (análisis 2026-04-30)

**Contexto:** del análisis de los 27 clientes ERP (`08-volumenes-clientes-erp.md`) emergen 3 outliers donde el ratio rutinas/equipos ≥ 1.7 — incoherente con la decisión §12.10/§12.11 del modelo ("una rutina técnica por grupo de mantenimiento"):

| Cliente | Equipos | Rutinas | Ratio |
|---|---:|---:|---:|
| FUNDACIONES Y PILOTAJES | 38 | 75 | 1.97 |
| PAVIMENTOS COLOMBIA | 990 | **1941** | 1.96 |
| EXPLANAN | 92 | 159 | 1.73 |

**Pregunta concreta:**

1. ¿Cómo es posible que PAVIMENTOS COLOMBIA tenga **1,941 rutinas técnicas** para 990 equipos? ¿Tienen rutinas por modelo/marca dentro del grupo (ej. una para D11T, otra para D65PX2 dentro del grupo BULLDOZER)?
2. ¿El catálogo de rutinas del ERP **incluye rutinas inactivas / legacy** que el módulo debería filtrar? Si sí, ¿qué campo del DTO indica "activa"? (Sería análogo al `activo=false` de ADR-004 reglas operativas.)
3. ¿La cifra "Rutinas" de la hoja Excel **mezcla tipos** (técnica + monitoreo + post-mantenimiento + certificación + otros), o ya viene filtrada por `tipo=tecnica`? Esto define qué endpoint del ERP usa el módulo: §9.13 `GET /api/v1/rutinas-tecnicas?grupo={g}` o `GET /api/v1/rutinas?tipo=tecnica&grupo={g}`.
4. ¿Estos clientes (FUNDACIONES, PAVIMENTOS, EXPLANAN) tienen un **modo operativo distinto** del resto (más maquinaria especializada, normativa diferente, certificaciones que requieren rutinas múltiples por equipo)?

**Qué desbloquea:**
- Confirmación o ajuste de la decisión §12.10 ("una rutina técnica por grupo"). Si la respuesta es (1) — rutinas por modelo dentro de grupo — el modelo debe extender `Rutina` con campo de aplicabilidad por modelo, o cambiar la cardinalidad. Si es (2) o (3), basta con filtrar correctamente del lado ERP.
- Selección informada del cliente piloto (Fase 9.1). Si los outliers son casos atípicos, conviene que el piloto sea un cliente "promedio" (ratio < 1) para validar el flujo principal antes de exponer el módulo a casos complejos.

**Bloquea:** validación final del modelo de rutina técnica antes de slice 3.32 (sync de catálogos). **No bloquea** el slice 3.36 (`POST /inspecciones`), que opera sobre la rutina ya sincronizada sin asumir cardinalidad.

---

### Pregunta 6 — Endpoint de rutinas de monitoreo (decisión 2026-04-30, archivo `inspeccion.xlsx`)

**Contexto:** la Fase 2 incorpora inspección de tipo `Monitoreo` (§12.11.5 del modelo, roadmap 10.4). Cada grupo de mantenimiento tiene **N rutinas de monitoreo** (Sistema eléctrico, Transmisión, Sistema de frenos, …) — distinto de las rutinas técnicas del MVP que son una sola por grupo. El técnico al iniciar elige cuál rutina inspeccionar. Cada rutina tiene una lista de items con `EvaluacionEsperada` (numérica con rango min/max O cualitativa Bueno/Regular/Malo).

El módulo necesita sincronizar este catálogo desde el ERP (decisión 5 confirmada por Jaime el 2026-04-30: sync, no gestión local).

**Pregunta concreta:**

1. **¿Existe el catálogo de "rutinas de monitoreo" en MYE hoy o es a construir?** El archivo `inspeccion.xlsx` define el formato — confirmar si Sinco MYE ya tiene una entidad equivalente o solo documentación operativa en hojas Excel sueltas.
2. Si existe, ¿cuál es el endpoint de lectura? Propuesta del módulo: `GET /api/v1/rutinas-monitoreo?grupo={g}` con response shape:
   ```json
   {
     "rutinas": [
       {
         "rutinaMonitoreoId": "guid",
         "nombre": "Sistema eléctrico",
         "grupoMantenimiento": "Camioneta",
         "items": [
           {
             "itemId": "guid",
             "parte": "Batería",
             "actividad": "Medición de voltaje",
             "tipoEvaluacion": "Numerica",
             "magnitud": "voltaje",
             "unidad": "V",
             "valorMin": 12.3,
             "valorMax": 12.5
           },
           {
             "itemId": "guid",
             "parte": "Conectores batería",
             "actividad": "Revisar estado",
             "tipoEvaluacion": "Cualitativa"
           }
         ]
       }
     ]
   }
   ```
3. **Discriminador del item**: ¿el ERP usa `tipoEvaluacion` como discriminador, o es implícito (si hay min/max → numérico, si no → cualitativo)? El módulo asume discriminador explícito por claridad.
4. **¿Estabilidad de los `itemId`?** El modelo snapshotea los items en `InspeccionIniciada_v1` para calcular `FueraDeRango` contra el rango vigente al iniciar. Si los `itemId` cambian entre syncs (ej. por re-cargar el catálogo), se rompe la trazabilidad. Confirmar regla operativa de inmutabilidad de IDs (análoga a ADR-004 catálogos generales).
5. **Catálogo de calificaciones cualitativas**: ¿el ERP define localmente "Bueno/Regular/Malo" o admite valores arbitrarios por cliente? El módulo asume enum cerrado de 3 valores. Si el ERP es flexible, hay que adaptar.

**Qué desbloquea:**
- Slice de Fase 2 para el flujo de monitoreo (roadmap 10.4).
- Slice de extensión de sync de catálogos (paso 3.32 Fase 3 podría preparar la infra para que en Fase 2 solo se agregue el adapter).

**Bloquea:** desarrollo de Fase 2 cuando se priorice. **No bloquea** ningún slice del MVP.

---

### Pregunta 7 — Endpoints de listado de hallazgos importables con filtros (followup #5)

**Contexto:** en la reunión 2026-04-29 surgió *"filtros para importar hallazgos deben estar disponibles antes de mostrar el listado, permitiendo filtrar por parte, fecha y usuario"*. Necesitamos definir dos endpoints o variantes filtrables:

**Endpoint A — Novedades preop importables filtrables** (paso 3.37 del roadmap, wizard de hallazgo desde preop):
- Forma propuesta: `GET /inspecciones/{id}/importables-preop?parte=&desde=&hasta=&usuario=`
- Decisión a tomar: ¿estos filtros se resuelven con **lectura directa al ERP** (`GET /api/v1/preop/novedades?...` con los filtros mapeados), o se mantiene un **read model local** del catálogo de novedades preop pendientes?

**Endpoint B — Seguimientos abiertos importables filtrables** (extiende §15.12.4):
- Forma propuesta: `GET /equipos/{equipoId}/seguimientos-importables?parte=&desde=&hasta=&usuario=`
- Probablemente la misma proyección Marten existente de §15.12.4 con los filtros adicionales — confirmar.

**Pregunta concreta:**
1. ¿El endpoint del preop puede devolver con esos filtros (parte, fecha, usuario)? Si sí, ¿cuál es la forma exacta del query string del lado ERP?
2. ¿Confirmas que los filtros **parte / fecha / usuario** son los correctos, o falta alguno (ej. tipo de novedad)?

**Qué desbloquea:** slice del wizard de hallazgo (paso 3.37 `POST /hallazgos`).

**Bloquea:** no hay bloqueo inmediato; se necesita antes de Santiago llegar al slice de importación.

---

## ✅ Followup #4 — "Proyecto" vs "Obra" — CERRADO 2026-04-30

**Resolución:** opción **(a) sinónimos**, palabra que queda = **"proyecto"** (Jaime, 2026-04-30). Sub-decisión: **(B) módulo usa `Proyecto` interno; ERP mantiene `Obra` en URLs/DTOs**. El adapter del módulo traduce `Proyecto` ↔ `Obra` cuando habla con MYE.

Aplicado: rename masivo de identificadores en el modelo (`ObraId` → `ProyectoId`, `ObraLocal` → `ProyectoLocal`, etc.), endpoints del módulo (`?obra=` → `?proyecto=` solo en endpoints del módulo, NO en URLs del ERP), brief consultor §3 entidades, roadmap, wireframes 02d. URLs del ERP `/api/v1/catalogos/obras` y `?obra=` se mantienen literales (con nota de aclaración en cada referencia).

---

## Pregunta menor pendiente con David sobre el rename

¿El ERP corporativo Sinco va a estandarizar a "proyecto" en algún momento (cambiar el endpoint `/catalogos/obras` → `/catalogos/proyectos` y los DTOs)? Si sí, conviene saber el calendario para alinear el adapter del módulo y eliminar la traducción ↔. Si no, el adapter mantiene la traducción permanentemente. **No bloquea nada** — el módulo ya funciona con la traducción.

---

## Cómo cerrar cada followup tras recibir respuesta

1. Editar `FOLLOWUPS.md`: mover de "Abiertos" a "Cerrados" con la resolución concreta.
2. Si la respuesta requiere amendment al modelo: aplicarlo en la sección correspondiente de `01-modelo-dominio.md` con fecha 2026-04-30.
3. Si desbloquea un slice del roadmap: confirmar con el squad para arrancar.
