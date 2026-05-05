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

### Pregunta nueva — UX cuando el técnico busca un SKU fuera de su prefetch (decisión 2026-05-05, piloto grande)

**Contexto:** el cliente piloto será **uno grande** (decisión Jaime 2026-05-05). Followup #9 (`prefetch-by-proyecto + lookup on-demand` para insumos) se activó. El patrón funciona así:

- Al login, la PWA descarga **un subset de SKUs del catálogo total**, filtrado por proyecto del técnico (ej. ~5K de los 30K totales). Eso es el **prefetch**.
- Cuando el técnico, dentro del wizard de hallazgo, busca un SKU **que sí existe en el catálogo total pero NO está en su prefetch** (caso típico: SKU raro que su proyecto rara vez usa, o SKU agregado al ERP después de su login del día), el selector no lo encuentra localmente.

**Pregunta para Daniel:** ¿qué hace la UX en ese momento?

Tres opciones a evaluar:

- **(a) Bloqueo con red:** el selector dice "este SKU no está disponible offline. Conectate a internet para buscarlo." El técnico tap "buscar online" → si tiene red, request al server → muestra el SKU → lo selecciona. Si no tiene red, no puede asignar el SKU; debe dejar el hallazgo sin asignación de repuesto y completarlo después con red. Más simple de implementar; pone fricción al técnico cuando está offline.

- **(b) Hallazgo sin SKU:** la app permite guardar el hallazgo registrando texto libre del repuesto (ej. *"sello hidráulico cat 8t-9123"*) sin `SkuId`. Cuando vuelve la red, otro flujo lo resuelve (UI de reconciliación, o se le pide al técnico volver y elegir el SKU correcto). Más permisivo offline pero genera **deuda de datos** — el ERP recibe hallazgos con repuestos sin código que requieren conciliación posterior.

- **(c) Cache "recientes":** además del prefetch del proyecto, la app mantiene una **lista local de los últimos N SKUs que el técnico usó en cualquier proyecto** (ej. 200 SKUs recientes). Si ya usó `RP-998` antes en otra obra, está en cache. Si nunca lo usó, sigue sin estarlo y cae a (a) o (b). Más sofisticado, alivia el caso "técnico rota entre proyectos" pero requiere lógica adicional cliente-side.

**Combinaciones:** las opciones no son excluyentes. Por ejemplo (a) + (c) es viable: cache recientes + bloqueo cuando no está + opción de buscar online si hay red.

**Qué desbloquea:** UX del paso "asignar repuesto" del wizard de hallazgo. Slices del frontend que consumen `RepuestoLocal`.

**Bloquea:** Fase 3 frontend del wizard de hallazgo (slice 3.37 `POST /hallazgos` y siguientes que asignan repuestos).

**Datos que ayudan a la decisión:**

- Frecuencia esperada de "SKU fuera del prefetch" en operación real → depende de respuesta a Pregunta 9 a David sobre cardinalidad SKU-Proyecto.
- Si los SKUs son scoped por proyecto y el prefetch cubre 100% del caso típico → opción (a) basta.
- Si son centrales y "fuera del prefetch" es común → conviene (a) + (c), o evaluar (b).

**Cross-refs:** FOLLOWUPS.md #9, Pregunta 9 a David (cardinalidad SKU-Proyecto), Pregunta 10 a David (endpoint individual de lookup).

---

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

### Pregunta 5 — Asignación equipo↔rutina técnica + outliers de catálogo (análisis 2026-04-30, refinada 2026-05-04)

**Decisiones cerradas 2026-05-04 (Jaime):**
- Cardinalidad: **1 rutina técnica por equipo** (única). Diferente de monitoreo Fase 2 que admite 2-3 por equipo.
- Mecanismo: **asignación explícita per-equipo en el ERP** (opción β confirmada). El campo `Equipo.rutinaTecnicaId: Guid` viaja en M-3b (`GET /equipos/{id}`).
- Modelo de `Rutina`: ver §12.11.1 — sin `RutinaPadreId`, con `ParteId` + `ParteCodigo` + `Tipo: TipoRutina` discriminador.

**Contexto restante:** del análisis de los 27 clientes ERP (`08-volumenes-clientes-erp.md`) emergen 3 outliers donde el ratio rutinas/equipos ≥ 1.7. Si la cardinalidad confirmada es 1 rutina técnica por equipo, ratio ~1 sería esperado. Los outliers tienen ratios ~2 — la diferencia necesita explicación:

| Cliente | Equipos | Rutinas | Ratio |
|---|---:|---:|---:|
| FUNDACIONES Y PILOTAJES | 38 | 75 | 1.97 |
| PAVIMENTOS COLOMBIA | 990 | **1941** | 1.96 |
| EXPLANAN | 92 | 159 | 1.73 |

**Hipótesis nuevas (post 2026-05-04):**
- **(i)** Las cifras incluyen **rutinas de monitoreo Fase 2** ya cargadas en el catálogo (típico 2-3 por equipo). Si PAVIMENTOS tiene 990 rutinas técnicas (1 por equipo) + ~1000 rutinas monitoreo (~1 por equipo en promedio si solo algunos tienen) ≈ 1990. Ratio ~2.
- **(ii)** El catálogo **mezcla tipos** que el módulo no debería consumir (preoperacional, mantenimiento). El endpoint M-17 debe filtrar server-side `tipo=Tecnica` para que el módulo solo reciba lo que aplica.
- **(iii)** Hay **rutinas históricas / inactivas** que el sync debe filtrar (mismo patrón que causas-falla descontinuadas — ADR-004).

**Pregunta concreta:**

1. ¿Confirmás la **cardinalidad 1 rutina técnica por equipo** en MYE? Es decir, ¿la entidad `Equipo` tiene exactamente UN `rutinaTecnicaId` (puede ser null si no asignada)? Si la realidad es que un equipo puede tener N rutinas técnicas asignadas, hay que cambiar el contrato de M-3b a `rutinaTecnicaIds: Guid[]` (plural) — análogo a monitoreo.
2. ¿El campo de asignación rutina técnica en `Equipo` **ya existe** en MYE, o es a construir? Si existe, ¿cómo se llama (ej. `RutinaTecnicaId`, `RutinaInspeccionId`, `RutinaPrincipalId`)?
3. ¿Cómo se gestiona la asignación operativamente? ¿UI administrativa en la web del ERP, sync con sistema externo, intervención técnica en BD?
4. ¿El catálogo de rutinas del ERP **mezcla tipos** (técnica + preoperacional + monitoreo + mantenimiento + certificación) bajo una sola entidad con discriminador, o cada tipo está en una entidad propia? Esto define la forma del endpoint M-17 (`?tipo=Tecnica` filtro vs entidad `RutinaTecnica` separada).
5. ¿Cómo explicas los outliers (PAVIMENTOS 1.96, FUNDACIONES 1.97, EXPLANAN 1.73)? ¿Hipótesis (i)/(ii)/(iii) arriba, o algo distinto?
6. ¿El catálogo incluye rutinas inactivas / legacy? Si sí, ¿qué campo del DTO indica "activa"? (Sería análogo al `activo=false` de ADR-004.)

**Qué desbloquea:**
- Confirmación final del modelo `Rutina` técnica (§12.11.1) y del shape de M-3b.
- Definición precisa del filtro server-side de M-17 (`tipo=Tecnica` implícito o explícito).
- Selección informada del cliente piloto (Fase 9.1). Si los outliers son casos atípicos, conviene que el piloto sea un cliente "promedio" (ratio cercano a 1).

**Bloquea:** validación final del modelo de rutina técnica antes del slice de sync de catálogos (paso 3.32). **No bloquea** el slice 3.36 (`POST /inspecciones`), que opera sobre la rutina ya sincronizada.

---

### Pregunta 6 — Catálogo de rutinas de monitoreo + asignación por grupo de mantenimiento (decisión 2026-04-30, refinada 2026-05-04, revertida 2026-05-05, **promovida a MVP 2026-05-05**)

**Contexto:** el **MVP** ahora incorpora inspección de tipo `Monitoreo` (decisión Jaime 2026-05-05 — antes era Fase 2 / roadmap 10.4 diferido). Detalle del modelo en §12.11.5; implementación en roadmap §3.B' (aggregate), §3.E (`RutinaMonitoreoLocal`), §3.F (endpoints), §4.B (M-16 🚧 obligatorio MVP), §5.B' (pantallas — wireframes en `02e-wireframes-monitoreo.html`). Cada **grupo de mantenimiento** tiene **N rutinas de monitoreo (típicamente 2–3)**; los equipos del grupo "heredan" todas las rutinas activas del grupo (decisión Jaime 2026-05-05 — revierte la asignación per-equipo planteada el 2026-05-04). El técnico al iniciar elige cuál rutina inspeccionar entre las del grupo del equipo. Cada rutina tiene una lista de items con `EvaluacionEsperada` (numérica con rango min/max O cualitativa Bueno/Regular/Malo).

**Urgencia (decisión 2026-05-05):** esta pregunta ahora es **bloqueante para el MVP** (antes era diferida a Fase 2). M-16 entró a la lista de endpoints críticos cross-team con David — coordinar con la misma prioridad que M-17.

El modelo separa dos responsabilidades (decisión 2026-05-05):

- **Catálogo de definiciones** (`GET /api/v1/catalogos/rutinas-monitoreo`, M-16): trae **todas** las rutinas activas, cada una con su `grupoMantenimientoId`. Sincronizado nocturnamente (ADR-004). Alimenta `RutinaMonitoreoLocal`.
- **Asignación equipo↔rutinas**: derivada client-side. El equipo trae `grupoMantenimientoId` en M-3b; el cliente filtra el catálogo local: `rutinasDelEquipo = catalogo.filter(r => r.grupoMantenimientoId == equipo.grupoMantenimientoId)`. **Sin tabla intermedia** equipo↔rutina-monitoreo en el ERP.

**Pregunta concreta — sobre el catálogo de definiciones (M-16):**

1. **¿Existe el catálogo de "rutinas de monitoreo" en MYE hoy o es a construir?** El archivo `inspeccion.xlsx` define el formato — confirmar si Sinco MYE ya tiene una entidad equivalente o solo documentación operativa en hojas Excel sueltas.
2. Si existe, ¿cuál es el endpoint de lectura? **Propuesta del módulo**: `GET /api/v1/catalogos/rutinas-monitoreo` con `grupoMantenimientoId` por rutina (filtro client-side). Shape propuesto:
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
3. **Discriminador del item**: ¿el ERP usa `tipoEvaluacion` como discriminador, o es implícito (si hay min/max → numérico, si no → cualitativo)? El módulo asume discriminador explícito por claridad.
4. **¿Estabilidad de los `itemId`?** El modelo snapshotea los items en `InspeccionIniciada_v1` para calcular `FueraDeRango` contra el rango vigente al iniciar. Si los `itemId` cambian entre syncs (ej. por re-cargar el catálogo), se rompe la trazabilidad. Confirmar regla operativa de inmutabilidad de IDs (análoga a ADR-004 catálogos generales).
5. **Catálogo de calificaciones cualitativas**: ¿el ERP define localmente "Bueno/Regular/Malo" o admite valores arbitrarios por cliente? El módulo asume enum cerrado de 3 valores. Si el ERP es flexible, hay que adaptar.

**Pregunta concreta — sobre la asignación por grupo (M-3b + M-16):**

6. ¿`Equipo.grupoMantenimientoId` ya existe en MYE como entidad/columna? El módulo de mantenimiento de Sinco MYE ya usa "grupos" (campo `grupo` denormalizado en M-3 vigente — ej. `"BULLDOZER"`, `"MAQ-PESADA"`); confirmar que existe la PK `int` correspondiente y que se puede exponer en M-3b junto con el descriptor.
7. ¿Cada `RutinaMonitoreo` en el catálogo MYE ya lleva un `grupoMantenimientoId`, o solo el descriptor textual `grupoMantenimiento`? Si solo es textual, ¿se puede poblar la PK al exponer M-16?
8. **No se necesita tabla intermedia equipo↔rutina-monitoreo**. Confirmar que el ERP no requiere modelar esa relación explícita — basta con el grupo en cada lado para derivar la asignación client-side.
9. ¿El catálogo de grupos de mantenimiento es estable (no cambian PKs)? Importante porque equipos y rutinas se "encuentran" únicamente por ese id.

**Qué desbloquea:**
- Slices del MVP para el flujo de monitoreo (roadmap §3.B' + §3.F + §5.B' — decisión 2026-05-05).
- Slice de sync de catálogos (paso 3.32 + 3.33) que ahora incluye `RutinaMonitoreoLocal` desde MVP.
- Implementación de M-3b en MVP — `grupoMantenimientoId` se incluye desde MVP (aplica a rutina técnica para reportería + a rutina monitoreo como mecanismo de asignación).

**Bloquea (decisión 2026-05-05):** desarrollo del flujo de monitoreo en MVP. M-16 es ahora **obligatorio MVP**, mismo nivel de criticidad que M-17. Si M-16 no está listo a tiempo, los slices de monitoreo se posponen pero el resto del MVP (técnica) puede avanzar.

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

### Pregunta 8 — Consolidación diaria del bump de ETag para `/catalogos/insumos` (decisión 2026-05-05)

**Contexto:** ADR-004 (refinamientos posteriores 2026-05-05, punto 4) define ETag canonical como mecanismo de cache HTTP entre cliente PWA y ERP. La forma propuesta del ETag es un **contador incremental por catálogo** (ej. `version` en una tabla `catalogos_metadata`). El cliente envía `If-None-Match: "v42"`; el ERP compara y responde 304 si nada cambió, o 200 OK con body completo si la versión avanzó.

El catálogo de **insumos / SKUs** es el caso problemático identificado en el análisis 2026-05-05 (ver `08-volumenes-clientes-erp.md` §3 hallazgo 7 actualizado): clientes intensivos pueden tener > 30K SKUs (~10 MB raw, ~2 MB con gzip) y agregan SKUs **diariamente** (compras nuevas, materiales nuevos por proyecto). Con la implementación literal del ETag, **cada INSERT bumpa la versión** → si admin agrega 5 SKUs en un día, la versión va `v42 → v47` y cada bump invalida el cache de los técnicos que sincronicen entre uno y otro.

**Pregunta concreta:**

¿Pueden implementar el bump del ETag de `insumos` como un **proceso consolidado diario** (cron 23:55 hora Bogotá) en lugar de un trigger por INSERT/UPDATE? La forma propuesta:

```sql
-- Trigger por INSERT/UPDATE: actualiza last_modified de la fila pero NO bumpa version
-- Cron diario 23:55: si hay cambios pendientes desde el último bump, ejecuta:
UPDATE catalogos_metadata
SET version = version + 1, last_modified = NOW()
WHERE catalog_name = 'insumos'
  AND EXISTS (
    SELECT 1 FROM insumos
    WHERE updated_at > (SELECT last_modified FROM catalogos_metadata WHERE catalog_name = 'insumos')
  );
```

**Beneficio cuantificable:**

- Sin consolidación: 5 cambios al día = 5 invalidaciones del ETag. 30 técnicos × 5 syncs × 2 MB = **300 MB de bandwidth desperdiciado/día**.
- Con consolidación: 5 cambios al día = 1 invalidación del ETag. 30 técnicos × 1 sync × 2 MB = **60 MB/día**.
- **Ratio de mejora: 5x reducción de bandwidth** sin tocar el módulo Azure ni la PWA, solo el ERP.

**Trade-off aceptado:**

- Latencia de hasta 24h entre alta del SKU y disponibilidad para el técnico (consistente con la latencia ya aceptada en ADR-004 §9.15 original para todos los catálogos — hasta 24h entre cambio admin y propagación).
- El ERP debe distinguir "fila nueva insertada" (last_modified de la fila) vs "version bump aplicado" (last_modified del metadata row).

**Qué desbloquea:** mitigación operativa cero-código del caso problemático de insumos. Permite mantener ADR-004 estándar para piloto chico (≤10K SKUs) sin activar el followup #9 (`prefetch-by-proyecto + lookup on-demand`) preventivamente.

**Bloquea:** ningún slice del módulo Azure depende de esto — es optimización del lado ERP. Pero si no se implementa, el piloto puede generar bandwidth innecesario que precipite la activación del followup #9 antes de tiempo.

**Cross-refs:** ADR-004 §9.15 punto 4 (refinamientos 2026-05-05), `08-volumenes-clientes-erp.md` §3 hallazgo 7, FOLLOWUPS.md #9.

---

### Pregunta 9 — Cardinalidad SKU-Proyecto en el ERP (URGENTE — decisión 2026-05-05, piloto grande)

**Contexto:** el cliente piloto será **uno grande** (decisión Jaime 2026-05-05). Followup #9 pasa de diferido a agenda activa — antes de Fase 3 frontend hay que cerrar el diseño del patrón híbrido para insumos (`prefetch-by-proyecto + lookup on-demand`). Esta pregunta es **make-or-break** del approach.

**Pregunta concreta:** en el ERP Sinco, ¿los SKUs del catálogo de insumos son **catálogo central corporativo** (todas las obras/proyectos los ven) o están **scoped por proyecto** (cada proyecto tiene su lista de SKUs aprobados)?

Concretamente para clientes intensivos como EXPLANAN, FUNDACIONES, REDES, PAVIMENTOS:

1. ¿Existe una tabla del estilo `proyecto_insumos (proyecto_id, sku_id)` que define qué SKUs aplican a cada proyecto, o cualquier técnico puede asignar cualquier SKU del catálogo total a cualquier inspección?
2. Si existe ese scoping per-proyecto: ¿qué proporción típica del catálogo total tiene un proyecto promedio? (ej. 5%, 20%, 50%).
3. Si NO existe scoping: ¿hay una heurística aproximada de "SKUs frecuentes de un proyecto" que el ERP pueda materializar (ej. SKUs usados en hallazgos del último año)?

**Por qué importa:**

- Si los SKUs son **scoped por proyecto** → prefetch al login con `?proyecto={id}` cubre 100% del caso operativo típico. Patrón funciona. Bandwidth se reduce de 2 MB a ~400 KB.
- Si son **centrales sin scoping** → prefetch no reduce volumen significativamente, hay que implementar "SKUs frecuentes recientes" o aceptar lookup on-demand para una proporción alta de casos.
- Si son **centrales pero con uso concentrado** (ej. 80% de los hallazgos usan el mismo 10% de SKUs) → heurística top-N por frecuencia es viable.

**Qué desbloquea:** diseño del adapter M-X (catálogo de insumos) y del read model `RepuestoLocal` en el módulo Azure. Define la UX del wizard de hallazgo cuando se asigna repuesto.

**Bloquea:** Fase 3 frontend (slices del wizard de hallazgo que consumen `RepuestoLocal`). Sin esta respuesta, no se puede cerrar el contrato del adapter ni la UX del selector de SKU.

---

### Pregunta 10 — Endpoints filtrados de insumos (decisión 2026-05-05, piloto grande)

**Contexto:** dependiente de la respuesta a Pregunta 9. Si los SKUs son scoped por proyecto (o el ERP puede materializar "SKUs frecuentes del proyecto"), necesitamos exponer dos endpoints en el ERP para soportar el patrón prefetch-by-proyecto.

**Endpoints propuestos:**

```http
GET /api/v1/insumos?proyecto={proyectoId}        # subset filtrado, prefetch al login
GET /api/v1/insumos/{skuId}                      # lookup individual on-demand
```

**Preguntas concretas:**

1. ¿El ERP puede exponer `GET /api/v1/insumos?proyecto={id}`? Si los SKUs son scoped, el filtro es un JOIN simple. Si no son scoped pero hay heurística de "frecuentes del proyecto", ¿la pueden materializar en una vista o tabla auxiliar?
2. ¿El endpoint individual `GET /api/v1/insumos/{skuId}` ya existe o es trabajo nuevo? Lo necesitamos para el lookup on-demand cuando un técnico busca un SKU fuera de su prefetch (con red).
3. ¿Soportan ETag (canonical en ADR-004 punto 4) por cliente individual del filtro `?proyecto=`? Es decir, dos técnicos del mismo proyecto comparten ETag, dos técnicos de proyectos distintos tienen ETags distintos.

**Trade-off aceptado:** el endpoint filtrado por proyecto es más complejo del lado ERP que el endpoint sin filtro (ese ya está cubierto por sync on-app-open con `If-None-Match` — ADR-004 canonical 2026-05-05). Pero la mejora bandwidth/cuota IndexedDB en piloto grande lo justifica.

**Qué desbloquea:** contrato exacto del adapter del módulo Azure que consume insumos, dimensionamiento del prefetch al login, y la UX del selector de SKU (con o sin botón "buscar online" para SKUs fuera de prefetch).

**Bloquea:** mismo que Pregunta 9 — Fase 3 frontend que consume `RepuestoLocal`.

**Cross-refs:** Pregunta 9 (depende), FOLLOWUPS.md #9, ADR-004 §9.15.

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
