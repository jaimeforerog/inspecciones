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



### #11 — `CapabilityRequeridaException`: dominio vs capa HTTP 🟢

**Origen:** slice 1a review hallazgo #1
**Fecha:** 2026-05-05
**Tipo:** deuda técnica · domain extension
**Descripción:** `CapabilityRequeridaException` está definida en `Inspecciones.Domain/Inspecciones/Excepciones.cs` junto al resto de excepciones del agregado, pero nunca es lanzada por el dominio — PRE-1 (capability) vive en la capa HTTP. Cobertura 0 %. Evaluar en slice 1b si debe mantenerse en el dominio (para que la capa HTTP la referencie con semántica clara) o moverse a la capa de aplicación/API donde realmente se lanza.
**Disparador para abrir slice:** cierre de slice 1b (handler HTTP). El handler es quien valida PRE-1; en ese momento quedará claro si la excepción pertenece al dominio o a la capa de aplicación.
**Notas:** sin cambio de código hasta slice 1b. Si el handler decide lanzarla desde el dominio delegando la verificación al agregado, covertura sube sola. Si la sigue lanzando la capa HTTP, mover la definición.

### #13 — Migrar `InspeccionAbiertaPorEquipoView` a `MultiStreamProjection` inline 🟢

**Origen:** slice 1b refactorer — candidato §4.1 green-notes
**Fecha:** 2026-05-06 · **Actualizado:** 2026-05-07 (slice 1g refactorer)
**Tipo:** deuda técnica · Marten
**Descripción:** El handler escribe la view con `session.Insert(view)` directamente en lugar de usar `MultiStreamProjection<InspeccionAbiertaPorEquipoView, int>` registrada `Inline` como describe la spec §8.1. La causa es que el `PostgresFixture` de `Application.Tests` no registra proyecciones en su `StoreOptions`. El refactor requiere: (1) registrar la proyección en `StoreOptions` del fixture, (2) registrar la proyección en `Program.cs`, (3) quitar el `_session.Insert(view)` del handler. El beneficio principal es que los eventos terminales (`InspeccionFirmada_v1`, `InspeccionCancelada_v1`) podrán hacer `DeleteEvent<T>` centralizado en la proyección sin que cada handler recuerde borrar la fila.
**Disparador para abrir slice:** el primer slice que maneje `InspeccionFirmada_v1` o `InspeccionCancelada_v1` — ese slice necesita eliminar la fila de `InspeccionAbiertaPorEquipoView` y es el momento natural de migrar a `MultiStreamProjection`.
**Estado en slice 1g (2026-05-07):** el disparador se alcanzó. Green migró la proyección de `session.Insert(view)` en el handler a `EventProjection` registrada inline (`InspeccionAbiertaPorEquipoProjection`). Se optó por `EventProjection` (no `MultiStreamProjection`) porque `InspeccionFirmada_v1` e `InspeccionCancelada_v1` no contienen `EquipoId` — la proyección necesita cargar la fila existente por `InspeccionId` para obtener la PK del documento a eliminar. Para poder usar `MultiStreamProjection` puro con `DeleteEvent<T>` keyed por `EquipoId`, habría que agregar `EquipoId: int` a esos dos eventos. Eso es un cambio de contrato de evento que requiere decisión del dominio.
**Bloqueo pendiente:** confirmar con el orquestador si agregar `EquipoId` a `InspeccionFirmada_v1` e `InspeccionCancelada_v1` es aceptable (el campo es parte del aggregate state, su inclusión en el evento no cambia lógica de negocio — solo enriquece el payload para proyecciones). Si se aprueba, el refactor es: (1) agregar `EquipoId: int` a los dos eventos, (2) actualizar los `Apply` correspondientes del aggregate, (3) reemplazar `EventProjection` por `MultiStreamProjection` en `InspeccionAbiertaPorEquipoProjection`, (4) actualizar los fixtures de test que construyen esos eventos.
**Notas:** la `EventProjection` actual es funcionalmente correcta; no es deuda bloqueante. La migración a `MultiStreamProjection` mejora rendimiento (elimina la query de lookup al delete) pero no es urgente.

### #14 — Claims reales desde JWT cuando ADR-002 se resuelva 🟢

**Origen:** slice 1b refactorer — candidato §4.2 green-notes
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · seguridad · ADR-002
**Descripción:** El endpoint `POST /api/v1/inspecciones` construye un `ClaimsTecnico` mock fijo (`TecnicoIniciador="rmartinez"`, `ProyectosAsignados={request.ProyectoId}`, `TieneCapabilityEjecutarInspeccion=true`). Cuando ADR-002 se resuelva (mecanismo de inyección de claims del host PWA), reemplazar por extracción desde `HttpContext.User` o claims del middleware del host.
**Disparador para abrir slice:** decisión sobre ADR-002 (mecanismo concreto de identidad del host PWA confirmado con Jaime/IT Seguridad) + slice de integración del módulo al host.
**Notas:** el mock es correcto para el MVP en aislamiento; no bloquea ningún slice hasta integración al host.

### #15 — Wolverine envelope dedup real para `X-Client-Command-Id` (ADR-008) 🟢

**Origen:** slice 1b refactorer — candidato §4.4 green-notes
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · ADR-008 · Wolverine
**Descripción:** El endpoint valida que el header `X-Client-Command-Id` esté presente pero no lo propaga como `MessageId` Wolverine. La idempotencia real de ADR-008 §9.16 requiere que el endpoint use `IMessageBus.InvokeAsync` con el header mapeado al `MessageId` del envelope, para que Wolverine detecte replays y devuelva la respuesta cacheada. Actualmente la idempotencia funciona por I-I1 (equipo ya tiene activa → `RedirigeAExistente=true`) pero no por dedup de envelope, por lo que el test §6.4 pasa por un mecanismo diferente al descrito en la spec §7.
**Disparador para abrir slice:** emerge un test o requerimiento que distinga entre "replay idempotente de envelope" y "I-I1 shortcut" en la respuesta (p. ej. el cliente necesita saber si la inspección `W` es nueva o es la activa del equipo para distinguir UX).
**Notas:** vinculado a ADR-008. Requiere que el pipeline HTTP esté configurado para Wolverine como mediator (no solo como outbox).

### #17 — Docstring del test §6.4 afirma mecanismo Wolverine dedup no implementado 🟢

**Origen:** slice 1b review hallazgo #3
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · test · ADR-008
**Descripción:** El test `POST_inspecciones_replay_con_mismo_ClientCommandId_no_duplica_evento_idempotencia_ADR_008` documenta en su body que verifica "ADR-008 §9.16 — Wolverine replay devuelve 200 con la respuesta cacheada del envelope". El mecanismo real es I-I1: el segundo request ve la fila en `InspeccionAbiertaPorEquipoView` y devuelve `RedirigeAExistente=true` + `200 OK`. El resultado observable es correcto (no hay duplicación de evento, el cliente recibe `200 OK` con el `InspeccionId` original), pero el docstring documenta un mecanismo que no existe en el código. Cuando se implemente el dedup real de Wolverine (followup #15), el green de ese slice debe: (a) corregir el docstring del test para reflejar el mecanismo real, (b) asegurarse de que el test siga pasando por el mecanismo correcto y no por I-I1 como colateral.
**Disparador para abrir slice:** cierre de followup #15 (implementación de Wolverine envelope dedup real).
**Notas:** el test está en `tests/Inspecciones.Api.Tests/IniciarInspeccionEndpointTests.cs:122-165`.

### #16 — `Version` real en `IniciarInspeccionResult` para el caso `RedirigeAExistente=true` 🟢

**Origen:** slice 1b refactorer — candidato §4.6 green-notes
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · correctitud
**Descripción:** `IniciarInspeccionResult.Version` retorna siempre `1`, incluso en el path `RedirigeAExistente=true` donde la versión real del stream existente puede ser mayor. La versión real requeriría consultar `session.Events.FetchStreamStateAsync(existente.InspeccionId)` tras el shortcut de I-I1. Ningún test actual verifica `Version > 1` en el path de redirige — corregirlo ahora añadiría consulta extra sin cobertura.
**Disparador para abrir slice:** emerge un test o cliente que necesite conocer la versión actual del stream para optimistic concurrency en el path `RedirigeAExistente=true`.
**Notas:** valor `1` en ese path es técnicamente incorrecto pero inofensivo para los consumidores actuales (el frontend abre la inspección existente por `InspeccionId`, no necesita la versión para ese flujo).

### #18 — `ParteEquipoLocal` sin cobertura en tests de dominio 🟢

**Origen:** slice 1c review §2 FU-18
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · cobertura
**Descripción:** `ParteEquipoLocal` tiene `line-rate=0` en `Inspecciones.Domain.Tests`. Su instanciación solo ocurre en los fixtures de tests de integración (Docker). Añadir una instanciación del record en los fixtures de dominio puro (p. ej. `HallazgoFixtures.cs`) para que la cobertura local lo cubra. Alternativa: documentar explícitamente en una próxima `refactor-notes.md` que la cobertura de este record está delegada a los tests de integración.
**Disparador para abrir slice:** primer slice que modifique `ParteEquipoLocal` o que añada lógica al record.
**Notas:** no bloqueante. El record es un DTO sin lógica; cobertura 0 no implica bug.

### #20 — `ObservacionCampo` presente en `HallazgoActualizado_v1` pero ausente del record `Hallazgo` del aggregate 🟢

**Origen:** slice 1d review hallazgo #1
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · domain extension
**Descripción:** `HallazgoActualizado_v1` y `ActualizarHallazgo` tienen el campo `ObservacionCampo`, pero el record `Hallazgo` del aggregate no lo expone. `Apply(HallazgoActualizado_v1)` lo ignora silenciosamente. No es bug para el MVP (ninguna proyección activa lo consulta), pero el gap entre el evento y el state debe cerrarse antes del primer slice de proyecciones de detalle.
**Disparador para abrir slice:** slice que implemente `DetalleInspeccionView` o cualquier proyección que proyecte `ObservacionCampo` del hallazgo. En ese slice: añadir `ObservacionCampo: string?` al record `Hallazgo`, incluirlo en el `with { ... }` de `Apply(HallazgoActualizado_v1)`, y añadir test que verifique el campo en el state del aggregate.
**Notas:** el compilador forzará la actualización del `with` automáticamente una vez se añada el campo al record `Hallazgo`.

### #23 — Extraer `MensajeActiva` a constante compartida entre handlers y alinear duplicación estructural 1b/1h 🟢

**Origen:** slice 1h refactorer — candidatos §5.1 y §5.2 de green-notes
**Fecha:** 2026-05-07
**Tipo:** deuda técnica · DRY
**Descripción:** `IniciarInspeccionHandler` (slice 1b) e `IniciarInspeccionMonitoreoHandler` (slice 1h) comparten: (a) la constante `MensajeActiva = "Ya hay inspección activa, abriendo la existente"`, y (b) estructura casi idéntica — I-I1 blanda → lookup equipo → lookup catálogo → validaciones → aggregate → `StartStream` → `SaveChangesAsync` → catch 23505. La duplicación es deliberada (green la dejó para que el refactorer decidiera). La constante puede moverse a una clase `MensajesHandler` en `Application/Inspecciones/` y la estructura podría extraerse a un helper estático `EjecutarConDefensaI_I1<TResult>`. El refactor de este followup toca `IniciarInspeccionHandler` (slice 1b) — es transversal y requiere decisión del orquestador.
**Disparador para abrir slice:** tercer handler que repita el patrón (p. ej. `IniciarInspeccionPreopHandler`) — en ese momento la duplicación es DRY real con tres instancias y vale la extracción. Alternativamente, hacerlo como slice de limpieza antes de la integración al host.
**Notas:** no bloqueante. La duplicación actual es 2 instancias — el umbral típico de DRY. Esperar un tercer caso antes de abstraer.

### #24 — Evaluar unificar `IniciarInspeccionResult` e `IniciarInspeccionMonitoreoResult` en un tipo canónico 🟢

**Origen:** slice 1h refactorer — candidato §5.3 de green-notes
**Fecha:** 2026-05-07
**Tipo:** deuda técnica · naming · simplificación
**Descripción:** los dos records son structuralmente idénticos tras la corrección del tipo `Version` en slice 1h refactor. La unificación en un record `IniciarInspeccionResultado` (o similar) eliminaría un tipo redundante, pero toca el resultado del slice 1b (`IniciarInspeccionResult`) y los tests de integración y API que lo referencian. El naming diferenciado actual tiene valor si el endpoint del orquestador mapea ambos de forma distinta a la respuesta HTTP; si mapea idéntico, la unificación es obvia.
**Disparador para abrir slice:** cuando el orquestador (`infra-wire`) implemente el endpoint `POST /api/v1/inspecciones/monitoreo` del slice 1h y pueda verificar si el mapeo HTTP es idéntico al de `POST /api/v1/inspecciones`. Si es idéntico → unificar en ese slice.
**Notas:** no bloqueante.

### #25 — Añadir aserción `fila.Tipo == TipoInspeccion.Monitoreo` en test de integración §6.1 🟢

**Origen:** slice 1h review — hallazgo #1
**Fecha:** 2026-05-07
**Tipo:** deuda técnica · test
**Descripción:** El test `IniciarMonitoreo_happy_path_evento_y_proyeccion_persisten_atomicos_seccion_6_1` verifica la existencia de la fila en `InspeccionAbiertaPorEquipoView` y su `InspeccionId`, pero no verifica `fila.Tipo == TipoInspeccion.Monitoreo`. La spec §6.1 lo requiere explícitamente. La proyección `InspeccionAbiertaPorEquipoProjection.cs:42` proyecta `Tipo: e.Tipo` correctamente, pero sin la aserción el test no documenta ni defiende ese comportamiento. El campo `Tipo` en la view fue añadido en este slice (spec §8.1) precisamente para que el frontend distinga el tipo activo al redirigir.
**Disparador para abrir slice:** primera corrida de tests de integración con Docker disponible (previa al primer merge que toque `InspeccionAbiertaPorEquipoProjection`). Añadir `fila.Tipo.Should().Be(TipoInspeccion.Monitoreo)` en el bloque Then del test §6.1.
**Notas:** baja criticidad. La proyección es correcta; falta la aserción defensiva.

### #26 — Unificar fuente de `TecnicoIniciador` en `Inspeccion.IniciarMonitoreo` para alinear con `Iniciar` 🟢

**Origen:** slice 1h review — hallazgo #2
**Fecha:** 2026-05-07
**Tipo:** deuda técnica · coherencia interna
**Descripción:** `Inspeccion.IniciarMonitoreo` (slice 1h) asigna `TecnicoIniciador: cmd.IniciadaPor` (línea 179), mientras `Inspeccion.Iniciar` (slice 1b) usa `TecnicoIniciador: claims.TecnicoIniciador` (línea 126). Son semánticamente equivalentes hoy (el handler construye `ClaimsTecnico` con `TecnicoIniciador = cmd.IniciadaPor`), pero la asimetría es una trampa latente: si en algún momento el handler normaliza o enriquece el campo en `ClaimsTecnico` antes de pasarlo al aggregate, `IniciarMonitoreo` quedaría desfasado al leer del comando directamente. El patrón correcto y consistente es que el aggregate siempre lea el técnico desde los claims.
**Disparador para abrir slice:** cualquier slice que modifique `Inspeccion.IniciarMonitoreo` o el handler `IniciarInspeccionMonitoreoHandler`. En ese momento, cambiar `cmd.IniciadaPor` por `claims.TecnicoIniciador` y actualizar el test que verifica el campo `TecnicoIniciador` del evento.
**Notas:** no bloqueante. La corrección es de una línea.

### #22 — Confirmar con David que M-16 expone `Activo: bool` y `Orden: int` por item de rutina monitoreo 🟢

**Origen:** slice 1h spec §12 — usuario asumió `sí` a ambas ambigüedades el 2026-05-07 para destrabar `red`.
**Fecha:** 2026-05-07
**Tipo:** integración · contrato API ERP · M-16
**Descripción:** la spec del slice 1h asume que el endpoint `M-16` (`GET /catalogos/rutinas-monitoreo`) expone por cada item: (a) `bool Activo` para distinguir items deprecados sin romper inspecciones en curso, y (b) `int Orden` para que la UI recorra los items en el orden canónico de la rutina. El record `ItemRutinaMonitoreo` en `01-modelo-dominio.md §12.11.5` no incluye estos campos explícitamente. La asunción se aceptó como optimista: si M-16 los expone, el adapter los propaga; si no, el adapter materializa `Activo=true` y `Orden=índice de la lista` (defaults inertes), sin cambio en el dominio. El handler del slice 1h ya filtra por `Activo=true` y ordena por `Orden ASC` antes de pasar el snapshot al aggregate — esa lógica queda inerte si los defaults aplican.
**Disparador para abrir slice:** previo al slice del adapter `RutinasMonitoreoAdapter` (paso 4.x del roadmap, equipo MYE). Confirmar contrato con David antes de implementar el adapter; si M-16 no soporta los campos, decidir si (i) se solicita extensión al ERP, (ii) el adapter usa defaults, o (iii) §12.11.5 del modelo se actualiza.
**Notas:** no bloquea el slice 1h ni los slices siguientes de monitoreo (3.16f/g/h). El módulo Azure es robusto a ambos caminos. Pregunta para `07-preguntas-destrabar-followups.md` cuando se prepare la siguiente ronda con David.

### #19 — Test `RegistrarHallazgo_con_parte_valida_del_equipo_no_lanza_INV_PartePerteneceAlEquipo` aserta excepción del stub en lugar del happy path del handler 🟢

**Origen:** slice 1c review §2 FU-19
**Fecha:** 2026-05-06
**Tipo:** deuda técnica · test
**Descripción:** el test en `RegistrarHallazgoHandlerTests.cs` fue escrito en fase `red` asertando `ThrowAsync<NotImplementedException>` como evidencia de que la validación INV pasa antes que el stub explota. En fase `green` el handler fue implementado completamente; la aserción correcta ahora es `NotThrowAsync()` o verificar el resultado exitoso (`HallazgoId`, `AccionRequerida`, etc.). El test debe corregirse para documentar el happy path de la validación INV, no el comportamiento de un stub que ya no existe.
**Disparador para abrir slice:** primer corrida de tests de integración con Docker disponible. Si el test falla en ese contexto (handler retorna resultado exitoso sin excepción), corregir la aserción antes de marcar el slice como completo en CI.
**Notas:** en entornos sin Docker el test se omite; el riesgo es latente hasta que CI tenga Docker. Vinculado a la deuda de Docker documentada desde slice 1b.

### #9 — Prefetch-by-proyecto vs sync completo de catálogos grandes 🟡 disparador alcanzado (piloto grande)

**Origen:** conversación de diseño 2026-05-05 sobre eficiencia de la sincronización de catálogos. Inicialmente sobre reemplazar el sync nocturno por cache on-demand puro. **Resuelto parcialmente 2026-05-05 (ADR-004 canonical):** el cron nocturno se eliminó en favor de sync on-app-open con `If-None-Match`. Este followup queda activo solo para evaluar el patrón **prefetch-by-proyecto + lookup on-demand** para catálogos grandes (insumos ~190K) — el sync on-app-open delta sigue trayendo el catálogo completo en la primera carga post-eviction iOS.
**Fecha:** 2026-05-05
**Tipo:** doc · ADR-004 · perf
**Descripción:** Evaluar si los catálogos grandes (`equipos` ~10K, `repuestos`/`SKUs` ~190K) deberían moverse de sync nocturno completo (ADR-004 vigente) a un patrón **prefetch-by-proyecto-asignado + lookup on-demand**. Los catálogos chicos (causas, tipos de falla, partes, actividades, ubicaciones — ~5K entidades, <500 KB total) seguirían con sync nocturno completo porque es imposible predecir cuáles usará el técnico. La propuesta es híbrida, no reemplazo total: al login, prefetch de equipos/SKUs del proyecto del técnico (~500-1000 equipos). Si el técnico es reasignado, el host PWA dispara re-prefetch. Lookup on-demand cubre casos fuera del prefetch cuando hay red.
**Razones para no actuar ahora (mantener ADR-004 vigente):**
- Volumen real cabe holgado: 260K entidades comprimidas en JSON ≈ 5-15 MB en IndexedDB (vs 90 MB de fotos OPFS por jornada). El "derroche" es marginal.
- Cache on-demand puro **rompe el caso de uso central** — técnico llega a obra sin red en equipo nunca tocado, no puede iniciar.
- Equipos nuevos (altas) no se descubrirían hasta que alguien los busque con red.
- iOS ITP 7 días borra cache; con sync on-app-open la siguiente apertura post-eviction descarga el catálogo completo (no incremental) — sigue siendo problema para catálogos grandes pero el técnico tiene la app abierta cuando vuelve, momento natural de reconstruir.
- Reasignación de técnico a otro proyecto un lunes: con cache on-demand puro, ese día queda bloqueado hasta que tome red.
**Disparador para abrir slice:** datos reales del piloto (Fase 9) que evidencien que el sync completo de un catálogo grande está saturando alguno de los **thresholds objetivos (decisión 2026-05-05, análisis específico de insumos + rutinas monitoreo)**:

- Tamaño promedio del response al sync de un catálogo > **2 MB en wire (gzip)** sostenidamente.
- Cuota IndexedDB en iOS Safari por encima del **50 %** atribuible a un solo catálogo (riesgo de eviction prematura por ITP).
- Bandwidth de sync por técnico > **5 MB/día sostenido**.
- Quejas de técnicos por "lentitud al arrancar la app" trazables al sync de catálogos.

**Caso concreto identificado (insumos):** clientes con > 20K SKUs (volumen máx observado: 34,428 — ver `08-volumenes-clientes-erp.md` §2). En piloto chico (avg 7K SKUs ≈ 400 KB wire) ADR-004 estándar es suficiente; en piloto intensivo (>20K SKUs ≈ >1.5 MB wire) **activar followup pre-Fase 3 frontend**. Rutinas de monitoreo (MVP desde 2026-05-05) tienen volumen <1 MB wire incluso en outliers — caso resuelto por ADR-004 estándar.

**Disparador alcanzado (decisión 2026-05-05):** **el cliente piloto será uno grande** (decisión Jaime). Confirmado el caso problemático antes de Fase 3 frontend. Followup pasa de diferido (esperar piloto) a **agenda activa** — el patrón híbrido para insumos debe estar diseñado e implementado antes de que el primer slice del frontend consuma `RepuestoLocal`.

**Decisiones bloqueantes pendientes** (preguntas 9, 10 y nueva a Daniel en `07-preguntas-destrabar-followups.md`):

1. Cardinalidad real SKU-Proyecto en el ERP (catálogo central vs scoped). Make-or-break del approach: si los SKUs son centrales y los técnicos efectivamente acceden a cualquiera del catálogo entero, el filtro `?proyecto=` no reduce nada y el patrón falla.
2. ¿Puede el ERP exponer `GET /api/v1/insumos?proyecto={id}` (lista filtrada) y `GET /api/v1/insumos/{id}` (lookup individual)?
3. UX cuando un técnico necesita un SKU "fuera de su prefetch" sin red — opciones (a) bloqueo, (b) hallazgo sin SKU, (c) cache de SKUs recientes.

Sin las tres respuestas, redactar el ADR de extensión a ADR-004 es prematuro.

**Cliente piloto exacto:** TBD — preguntar al equipo de Sinco. Saber el cliente determina volumen real de SKUs (data agregada por cliente para insumos no estaba en el corte 2026-04-30, solo total).

**Notas:** Si emerge en piloto, el camino es híbrido (no on-demand puro): catálogos chicos siguen con sync completo + catálogos grandes pasan a prefetch-by-proyecto (`GET /api/v1/<catalogo>?proyecto={id}`) + lookup on-demand individual (`GET /api/v1/<catalogo>/{id}`). Ese diseño preserva offline duro mientras reduce el footprint. **Mitigación complementaria sin cambio de arquitectura (Opción 2 de la decisión 2026-05-05):** pedir a David consolidar bumps de ETag de insumos en un único `version++` diario (ver pregunta nueva en `07-preguntas-destrabar-followups.md`). Cross-ref: ADR-008 sección "Comportamiento por plataforma" (riesgo iOS) y `08-volumenes-clientes-erp.md` §2 (volúmenes reales) y §3 hallazgo 7 (dimensionamiento Azure).



## Cerrados

### #21 — Test §6.7 (`PRE-B2 — HallazgoId eliminado`) en skip hasta slice `EliminarHallazgo` ✅

**Origen:** slice 1d review hallazgo #2
**Fecha apertura / cierre:** 2026-05-06 / 2026-05-06
**Tipo:** deuda técnica · test
**Descripción:** El test `ActualizarHallazgo_con_HallazgoId_eliminado_lanza_HallazgoEliminadoException` (§6.7) estaba marcado `[Fact(Skip="...")]` porque `HallazgoEliminado_v1` no existía aún.
**Resolución:** slice 1e (`EliminarHallazgo`) implementó `HallazgoEliminado_v1` y `Apply(HallazgoEliminado_v1)`. `StreamConHallazgoEliminado()` en `HallazgoFixtures.cs` fue completado con el evento real; el `[Fact(Skip=...)]` fue eliminado de `ActualizarHallazgoTests.cs`; el test pasa en verde (62 pass totales en el suite de dominio). DoD del slice 1e cumplido.

### #12 — Test de evento desconocido en `AplicarEvento` ✅

**Origen:** slice 1a review hallazgo #3
**Fecha apertura / cierre:** 2026-05-05 / 2026-05-06
**Tipo:** deuda técnica · test
**Descripción:** La rama `default: throw new InvalidOperationException(...)` en `Inspeccion.AplicarEvento` no tenía test directo en slice 1a porque el único evento válido era `InspeccionIniciada_v1`.
**Resolución:** slice 1c (`RegistrarHallazgo`) añadió `case HallazgoRegistrado_v1` como segundo case en `AplicarEvento` y el test `Reconstruir_con_evento_desconocido_lanza_InvalidOperationException_followup_12` cubre la rama `default`. Test verde desde fase red del slice 1c. Cobertura de ramas del dominio 100 % (52/52).

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
