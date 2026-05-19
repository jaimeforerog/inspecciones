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

### #14 — Claims reales desde JWT cuando ADR-002 se resuelva ✅ MOVIDO A CERRADOS

(Ver sección `## Cerrados` abajo — cerrado por slice mt-1 el 2026-05-19.)

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

### #29 — Registrar invariantes I-M1..I-M9 de Monitoreo en `01-modelo-dominio.md §15` 🟢

**Origen:** slice 1j review hallazgo #1
**Fecha:** 2026-05-08
**Tipo:** doc · domain model
**Descripción:** Las invariantes I-M1..I-M9 del contexto Monitoreo están definidas en las specs de los slices 1i, 1i' y 1j, y están implementadas en el aggregate `Inspeccion`, pero nunca fueron incorporadas al modelo de dominio como sección canónica. La sección §15.3 es "Invariantes del Hallazgo" (I-H*) y no es el lugar semántico correcto. No existe subsección §15.X dedicada a invariantes de monitoreo. La deuda es acumulativa: I-M1..I-M7 deberían haberse registrado en slices 1i/1i'; I-M8 e I-M9 fueron anunciadas por la spec del slice 1j pero tampoco aparecen en el documento. Acción: crear subsección `§15.13 Invariantes de Monitoreo (I-M*)` con el catálogo I-M1..I-M9 y sus textos canónicos extraídos de las specs de slices 1i, 1i' y 1j. Sin cambio de código.
**Disparador para abrir slice:** primer slice que extienda o modifique una invariante de monitoreo, o cualquier tarea de doc-writer que toque §15.
**Notas:** no bloqueante. Las invariantes existen en código y specs; solo falta la sección canónica en el modelo de dominio como referencia oficial.

### #27 — Extraer guard `X-Client-Command-Id` duplicado en 7 endpoints a helper o middleware 🟢

**Origen:** slice 1i refactorer — candidato §3 green-notes
**Fecha:** 2026-05-08 · **Actualizado:** 2026-05-08 (slice 1j)
**Tipo:** deuda técnica · DRY
**Descripción:** el bloque de validación del header `X-Client-Command-Id` se repite en los 9 endpoints de `InspeccionesEndpoints.cs` (IniciarInspeccion, IniciarInspeccionMonitoreo, RegistrarHallazgo, ActualizarHallazgo, AsignarRepuesto, FirmarInspeccion, EliminarHallazgo, RegistrarMedicion, RegistrarEvaluacionCualitativa, OmitirItemMonitoreo). Cada endpoint repite el mismo `TryGetValue` + `IsNullOrWhiteSpace` + `Results.BadRequest`. Puede extraerse a un método estático de extensión `RequiereClientCommandId(this HttpContext ctx)` que devuelva `IResult?` (null = ok, not-null = bad request a retornar), o a un filtro de endpoint Minimal API registrado globalmente con `.AddEndpointFilter`. La duplicación es acumulativa desde el slice 1b; el 1j la lleva a 9 instancias — umbral de DRY alcanzado con holgura.
**Disparador para abrir slice:** cualquier slice que añada un endpoint nuevo, o un slice de limpieza previo a la integración al host. El umbral de DRY (3+ instancias) ya se superó.
**Notas:** no bloqueante. El comportamiento es idéntico en todos los endpoints; el riesgo de inconsistencia por edición manual aumenta con cada nuevo endpoint.

### #28 — Cast directo por posición `(MedicionRegistrada_v1)eventos[0]` en `RegistrarMedicionHandler` 🟢

**Origen:** slice 1i refactorer — candidato §3 green-notes
**Fecha:** 2026-05-08
**Tipo:** deuda técnica · robustez
**Descripción:** `RegistrarMedicionHandler.Handle` asume que `eventos[0]` es siempre `MedicionRegistrada_v1` mediante cast directo. El contrato del aggregate lo garantiza hoy, pero no hay un tipo que lo exprese. Alternativa más robusta: hacer que `Inspeccion.RegistrarMedicion` retorne un tipo discriminado `(MedicionRegistrada_v1 Medicion, HallazgoRegistrado_v1? Hallazgo)` o una clase `RegistrarMedicionEvento` con las dos propiedades. El cambio afectaría el tipo de retorno del aggregate y todos los tests que destructuran `resultado[0]`/`resultado[1]` — scope amplio.
**Disparador para abrir slice:** emerge una regresión donde `eventos[0]` ya no es `MedicionRegistrada_v1` (p. ej. si se añade un evento de auditoría antes), o cuando se diseñe el patrón canónico de retorno del aggregate para slices de monitoreo.
**Notas:** riesgo muy bajo mientras `RegistrarMedicion` siga emitiéndo exactamente 1 o 2 eventos en ese orden.

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


### #31 — Shape canónico de `OTSolicitada_v1` en §17 usa `DateTime` en lugar de `DateTimeOffset` y carece de `Prioridad`, `Observaciones`, `ComentarioJefe` 🟢

**Origen:** slice 1k review — hallazgo de coherencia con §17
**Fecha:** 2026-05-08
**Tipo:** doc · model
**Descripción:** el shape canónico de `OTSolicitada_v1` en `01-modelo-dominio.md §17` (línea ~4596) usa `DateTime SolicitadaEn` en lugar de `DateTimeOffset` (convención del módulo), y el record mínimo del ADR no incluye los campos `PrioridadOT Prioridad`, `string? Observaciones` y `string? ComentarioJefe` añadidos en el spec del slice 1k (decisiones P-1 opción B y P-2). La implementación en código es correcta; es el documento que quedó desfasado. También el `GenerarOT` canónico en §17 no incluye `Prioridad`, `Observaciones` ni `ComentarioJefe`. Acción: actualizar ambos records en §17 para reflejar la implementación real. Ningún cambio de código necesario.
**Disparador para abrir slice:** cualquier doc-writer que toque §17 o §15.4, o previo al slice `EjecutarOTSaga` (3.24b) que consume el evento.
**Notas:** no bloqueante. La spec del slice 1k ya documenta la desviación con justificación en §3.1 y §12 P-1/P-2.

### #35 — `spec.md §8.1` del slice 1l afirma incorrectamente que la proyección consume `InspeccionCerradaSinOT_v1` 🟢

**Origen:** slice 1l review — hallazgo #2
**Fecha:** 2026-05-08
**Tipo:** doc · correctitud
**Descripción:** `slices/1l-rechazar-generar-ot/spec.md §8.1` afirma: "el stub de 1k incluyó el case para `InspeccionCerradaSinOT_v1`" en `InspeccionAbiertaPorEquipoProjection`. La proyección real (`InspeccionAbiertaPorEquipoProjection.cs`) solo consume `InspeccionIniciada_v1`, `InspeccionFirmada_v1` e `InspeccionCancelada_v1` — no tiene handler para `InspeccionCerradaSinOT_v1`. La afirmación es incorrecta. Funcionalmente no es un bug: en el stream del aggregate, `InspeccionFirmada_v1` siempre precede a `InspeccionCerradaSinOT_v1` (el equipo ya queda libre al firmar), por lo que la proyección ya libera el equipo antes de que llegue el evento de cierre. Sin embargo, la afirmación falsa puede inducir a error en slices futuros de proyecciones.
**Cierre parcial 2026-05-08 (infra-wire slice 1l):** se aplicó el punto (2) — `InspeccionAbiertaPorEquipoProjection.cs` ahora documenta explícitamente en su xmldoc por qué no consume `InspeccionCerradaSinOT_v1` (el flujo canónico garantiza que `InspeccionFirmada_v1` precede al cierre). Queda pendiente el punto (1): corregir la afirmación falsa en `slices/1l-rechazar-generar-ot/spec.md §8.1` — los `spec.md` cerrados típicamente se mantienen como audit trail, así que la corrección puede hacerse como nota de erratum al pie de §8.1 o como ADR-cross-reference si se prefiere preservar el spec original sin tocarlo.
**Disparador para abrir slice:** cualquier doc-writer que toque `spec.md §8.1` o el modelo de dominio §15.12.6.
**Notas:** no bloqueante. La proyección es funcionalmente correcta y ahora también documentada correctamente; solo la afirmación del spec es incorrecta.

### #33 — Cambio cross-slice no documentado en `InspeccionAbiertaPorEquipoProjection.cs` durante slice 1k 🟢

**Origen:** slice 1k green — modificación detectada en diff sobre archivo fuera del scope declarado del slice
**Fecha:** 2026-05-08
**Tipo:** deuda técnica · auditoría · proyección
**Descripción:** Durante la fase `green` del slice 1k se modificó `src/Inspecciones.Application/Inspecciones/InspeccionAbiertaPorEquipoProjection.cs` para resolver un race condition entre `IQuerySession` (no soportado por Marten 7 en proyecciones inline) y un race en el mismo batch del event store. El cambio fue necesario para que los handlers del slice 1k (`GenerarOTHandler`) funcionaran correctamente sin colisionar con la proyección. El refactorer del slice 1k **no documentó este cambio en `refactor-notes.md`** como modificación cross-slice fuera del scope. La proyección fue introducida en slice 1g (`FirmarInspeccion` migración a `EventProjection`) y este ajuste afecta a los slices que la usan en lectura (1b, 1g, 1h y todos los que cargan `InspeccionAbiertaPorEquipoView`). Acción: auditoría posterior para (a) confirmar que el cambio es semánticamente correcto, (b) verificar que no introduce regresiones en los flujos de los slices 1a-1f que ya estaban verdes, (c) documentar formalmente el cambio en `slices/1k-generar-ot/refactor-notes.md` o en una nota agregada al review-notes del slice 1g si la corrección pertenece a ese contexto. Severidad media porque el cambio destrabó el slice 1k pero introduce riesgo de regresión silenciosa en proyecciones consumidas por slices previos.
**Disparador para abrir slice:** auditoría como parte del cierre de Fase 1, o cuando emerja un test de integración que verifique el shape completo de `InspeccionAbiertaPorEquipoView` tras el batch del slice 1k. Idealmente vinculado al slice de fix de FU-32 (mismo ciclo de saneamiento de la capa Api/proyecciones).
**Notas:** vinculado a followup #13 (migración a `MultiStreamProjection` inline). Si #13 se materializa, este cambio queda absorbido en el rediseño de la proyección. Si #13 sigue diferido, el cambio del slice 1k debe quedar documentado y testeado en aislamiento.

### #39 — `Application.Tests` requiere Docker — falta switch a Postgres local + tests con IDs hardcoded colisionan 🟢

**Origen:** slice 1m — auditoría tras extender slice para correr Application.Tests local (decisión usuario revertida tras descubrir deuda mayor)
**Fecha:** 2026-05-11
**Tipo:** deuda técnica · test infra · cobertura
**Descripción:** El fix-FU-32 introdujo el switch `POSTGRES_TEST_CONNSTRING` en `InspeccionesAppFactory` (Api.Tests), permitiendo correr esa suite sin Docker. La misma fixture `PostgresFixture.cs` de `Application.Tests` quedó con Testcontainers hardcoded — sin Docker los 43 tests fallan con `ArgumentException: Docker is either not running or misconfigured`. **Bug secundario descubierto al intentar ampliar el slice 1m:** al portar el switch, 5 tests de `RegistrarHallazgoHandlerTests` y `RegistrarMedicionHandlerTests` fallan con `DocumentAlreadyExistsException` por `InspeccionAbiertaPorEquipoView` con `EquipoId=4521`. Causa raíz: los tests del mismo archivo siembran el mismo `EquipoId` hardcoded sin reset entre tests; Testcontainers enmascaraba la colisión levantando container limpio por corrida, pero con DB persistente compartida los tests pelean. Adicionalmente, las corridas combinadas Application+Api contaminan la misma DB cuando comparten `inspecciones_test`.
**Disparador para abrir slice:** antes de migrar el repo a CI sin Docker, o cuando emerja necesidad de iterar en `Application.Tests` localmente sin levantar Docker Desktop.
**Acciones requeridas:**
1. Replicar el patrón de `InspeccionesAppFactory` en `PostgresFixture.cs` (env var + `EnsureDatabaseExists` + `DROP SCHEMA inspecciones CASCADE`).
2. Agregar `xunit.runner.json` con `maxParallelThreads: 1` en `Application.Tests` (igual que Api.Tests).
3. Refactorizar los tests con IDs hardcoded para usar `Guid.NewGuid()` / IDs aleatorios per-test, o introducir reset de schema entre tests.
4. Considerar DBs separadas (`inspecciones_test_api`, `inspecciones_test_application`) o limpieza más agresiva entre suites cuando corren combinadas.

**Notas:** Slice 1m verificado a nivel Domain (213/228 verde, cobertura 94.9% del aggregate) + Api (38/42 verde con `POSTGRES_TEST_CONNSTRING`). Application.Tests del slice 1m (`CancelarInspeccionHandlerTests`) compilan pero requieren Docker hasta cerrar este followup.

### #40 — `RegistrarHallazgo` no verifica `_novedadesDescartadas` — INV-ND1 asimétrica 🟢

**Origen:** slice 1n review hallazgo #1
**Fecha:** 2026-05-11
**Tipo:** deuda técnica · invariante · simetría
**Descripción:** El slice 1n añadió `_novedadesDescartadas: HashSet<int>` al aggregate `Inspeccion` y PRE-6 en `Descartar` (bloquea descartar una novedad ya convertida en hallazgo). La invariante recíproca INV-ND1 propuesta en spec §5 dice "una novedad NO puede estar descartada Y convertida en hallazgo a la vez" — pero `RegistrarHallazgo` (slice 1c) no verifica `_novedadesDescartadas` antes de aceptar `Origen=PreOperacional`. Resultado: el flujo `Descartar(NovedadId=X) → RegistrarHallazgo(Origen=PreOperacional, NovedadPreopOrigenId=X)` no es rechazado por el backend (la UI lo previene pero el aggregate aceptaría). No es corrupción en producción inmediata pero rompe la simetría declarada de INV-ND1.
**Disparador para abrir slice:** antes de cerrar Fase 1, o cuando emerja un test de integración cross-slice que ejercite el flujo inverso.
**Acción:** añadir guardia en `Inspeccion.RegistrarHallazgo` cuando `cmd.Origen == OrigenHallazgo.PreOperacional && cmd.NovedadPreopOrigenId.HasValue` — si `_novedadesDescartadas.Contains(cmd.NovedadPreopOrigenId.Value)` → `NovedadYaDescartadaException` (o nueva excepción simétrica). Test correspondiente.
**Notas:** vinculado a FU-41 (documentar INV-ND1 canónicamente en §15.3).

### #41 — INV-ND1 sin entrada formal en `01-modelo-dominio.md §15.3` 🟢

**Origen:** slice 1n review hallazgo #2
**Fecha:** 2026-05-11
**Tipo:** documentación · invariante · catálogo canónico
**Descripción:** El spec 1n §5 propuso agregar INV-ND1 ("una novedad preop NO puede estar simultáneamente en `_novedadesDescartadas` y referenciada como `NovedadPreopOrigenId` por un `HallazgoRegistrado_v1` no eliminado") al catálogo de invariantes §15.3 del modelo de dominio. La invariante existe operacionalmente (PRE-6 del slice 1n la verifica en `Descartar`) pero no tiene entrada canónica en §15.3. Futuros tests no pueden referenciarla por código (`INV_ND1` en nombre de test) sin definición formal.
**Disparador para cerrar:** PR de documentación junto con FU-40 (fix simetría) o antes del cierre de Fase 1.
**Acción:** agregar entrada INV-ND1 a `Inspecciones/docs/01-modelo-dominio.md §15.3` con shape canónico de invariantes (enunciado, comandos que la enforcean, slices relacionados).

### #42 — Test E2E §6.9 (`PRE-5 cross-hallazgo`) ausente en `ActualizarRepuestoEndpointTests` 🟢

**Origen:** slice 1o review hallazgo #1
**Fecha:** 2026-05-11
**Tipo:** cobertura de tests · E2E
**Descripción:** El escenario §6.9 del spec 1o (PRE-5: RepuestoId en hallazgo incorrecto → 404 `PRE-5`) está documentado en el docblock de `ActualizarRepuestoEndpointTests.cs` pero no tiene método `[Fact]` propio. El escenario está cubierto a nivel dominio. Riesgo bajo porque §6.8 y §6.9 producen idéntica respuesta HTTP (`404 + "PRE-5"`), pero el contrato HTTP queda sin verificación explícita en E2E.
**Disparador para abrir slice:** cuando emerja necesidad de auditar cobertura E2E completa, o como nit al cierre de Fase 1.
**Acción:** agregar test `PATCH_repuesto_repuesto_en_hallazgo_incorrecto_responde_404_PRE5` en `tests/Inspecciones.Api.Tests/ActualizarRepuestoEndpointTests.cs` siguiendo el patrón de §6.8.

### #44 — `InvalidOperationException` (PRE-L1) sin política `OnException<T>().MoveToErrorQueue()` en Wolverine 🟢

**Origen:** slice erp-3 review hallazgo #1
**Fecha:** 2026-05-19
**Tipo:** deuda técnica · resiliencia · Wolverine
**Descripción:** El listener `SincronizarDictamenVigenteListener` lanza `InvalidOperationException` en PRE-L1 (aggregate nulo → stream no existe, o Dictamen nulo → stream corrupto). La spec §4 PRE-L1 requiere "dead-letter inmediato". Sin embargo, en `Program.cs` la política de Wolverine para dead-letter inmediato solo cubre `ArgumentException` (línea 107), que captura `ArgumentOutOfRangeException` (PRE-L3) por herencia, pero no `InvalidOperationException`. En producción, Wolverine aplicará comportamiento default (retry con backoff hasta agotar intentos antes de dead-letter), no "dead-letter inmediato". El efecto final es el mismo (dead-letter), pero el camino difiere y cada intento de retry para PRE-L1 relanzará la excepción con la misma causa raíz (el stream o el Dictamen siguen siendo inválidos). Los reintentos son inútiles y demoran el dead-letter ~20 min.
**Disparador para abrir slice:** antes del primer despliegue en entorno staging con Wolverine durables activos. La corrección es de 1 línea en `Program.cs`.
**Acción:** añadir `opts.Policies.OnException<InvalidOperationException>().MoveToErrorQueue()` en el bloque de políticas de Wolverine en `Program.cs`, verificando que no afecta handlers que puedan lanzar `InvalidOperationException` por razones retriables (poco probable — las excepciones de infraestructura retriables usan `MaquinariaErpException`).

### #45 — `MartenInspeccionReader` sin tests de integración contra Marten real 🟢

**Origen:** slice erp-3 review hallazgo #2
**Fecha:** 2026-05-19
**Tipo:** deuda técnica · cobertura · integración
**Descripción:** `MartenInspeccionReader` (implementación de producción de `IInspeccionReader`) es un wrapper de una línea sobre `IQuerySession.Events.AggregateStreamAsync<Inspeccion>`. No tiene tests propios en el slice erp-3 — la decisión Opción B (FakeInspeccionReader) fue pragmática y coherente con el patrón erp-2. El cobertura del path producción (Marten real → aggregate reconstruido) queda a cero. Los escenarios críticos sin test son: (a) stream existente → aggregate con Dictamen y EquipoId correctos; (b) stream inexistente → null (PRE-L1). Bajo riesgo porque la implementación es trivial, pero la interfaz `IInspeccionReader` puede ser reutilizada por futuros listeners — un test de integración protege que el adapter se comporta como el fake.
**Disparador para abrir slice:** primer slice que añada un segundo listener usando `IInspeccionReader`, o cuando se implemente la suite de integración end-to-end de los listeners ERP (slice de test de integración erp-all o similar). Alternativamente, cuando FU-39 (Application.Tests sin Docker) se cierre y se pueda correr Testcontainers localmente.
**Acción:** añadir una clase `MartenInspeccionReaderTests` en `Inspecciones.Infrastructure.Tests` (con Testcontainers Postgres + Marten) que verifique los dos escenarios mencionados.

### #43 — Colisión de EquipoIds en Api.Tests entre slices (manifestación expandida de FU-39) 🟢

**Origen:** slice 1o green/orquestador — fix aplicado durante el slice
**Fecha:** 2026-05-11
**Tipo:** deuda técnica · test infra · IDs hardcoded
**Descripción:** El slice 1o nació usando EquipoIds `80001-80010` que colisionaron con los hardcoded de slices 1m (`CancelarInspeccionEndpointTests`, `80001-80009`) y 1n (`DescartarNovedadPreopEndpointTests`, `90001-90007`). Al sembrar `InspeccionAbiertaPorEquipoView` con EquipoId duplicado en tests de distintos archivos, el segundo en correr fallaba con `DocumentAlreadyExistsException`. Fix temporal aplicado por orquestador: renombrar IDs slice 1o a `100001-100010`. **Es la misma causa raíz que FU-39** (IDs hardcoded sin reset entre tests), pero ya alcanza `Api.Tests` (no solo `Application.Tests`).
**Disparador para abrir slice:** próximo slice de Fase 1 que agregue tests E2E (slice 1p `RemoverRepuesto` o `AdjuntarArchivo` van a chocar también si no se resuelve). O cuando proliferación de rangos `80000+, 90000+, 100000+, 110000+...` sea insostenible.
**Acción sugerida:** implementar `Func<int> NextEquipoId()` en `InspeccionesAppFactory` o helper compartido que devuelva un `int` único por test (counter atómico). Reemplazar IDs hardcoded en todos los tests E2E. Alternativa: schema reset entre tests (más costoso pero más simple).
**Notas:** vinculado a FU-39 (Application.Tests sin Docker). Ambos cierran probablemente en el mismo slice de saneamiento de test infra.

### #52 — Endpoint `POST /api/v1/catalogos/sync` sin verificacion de capability PRE-1 (ADR-002) ✅ MOVIDO A CERRADOS

(Ver sección `## Cerrados` abajo — cerrado por slice mt-1 el 2026-05-19 vía D-MT1-9.)

### #53 — Auth a feeds NuGet corporativos Azure DevOps en CI 🟢

**Origen:** slice mt-1 spec §12.C — decisión firmada 2026-05-19.
**Fecha:** 2026-05-19
**Tipo:** infra · CI · seguridad
**Descripción:** `NuGet.Config` del repo lista los feeds corporativos Sinco (`pkgs.dev.azure.com/sincosoftsas/...`) además de `nuget.org`. Los paquetes `SincoSoft.MYE.Common 1.5.1` y `SincoSoft.MYE.Middleware 1.1.6` introducidos por mt-1 solo viven en esos feeds. Localmente el restore funciona porque los paquetes ya están en `%USERPROFILE%\.nuget\packages\` (caché caliente), pero en CI un `dotnet restore` fresco falla con `NU1301 (401 Unauthorized)` contra los feeds Azure DevOps. No hay credentials provider configurado en el pipeline.
**Disparador para abrir slice:** previo al primer merge de mt-1 a `main`, o cuando se cablee el pipeline GitHub Actions de Inspecciones (mismo slice que el primer despliegue real a Azure Container Apps).
**Acción sugerida:** registrar un secret `AZURE_DEVOPS_NUGET_PAT` en el repo GitHub + agregar paso al workflow que configure `NuGet.Config` con `<packageSourceCredentials>` desde la env var, o usar `Azure Artifacts Credential Provider` (`microsoft/setup-msbuild` + `nuget setApiKey`). Alternativa: vendoring/mirroring local de los dos paquetes corporativos a un feed accesible sin auth.
**Notas:** vinculado al embargo NuGet local cerrado por mt-1. Localmente queda destrabado siempre que la caché esté caliente. Riesgo: contribuidor nuevo sin caché necesita acceso al PAT o asistencia del usuario para hidratar caché.

### #54 — Confirmar con Sergio/David si el JWT del host PWA emite claim `capabilities` 🟢

**Origen:** slice mt-1 spec §12.D — decisión firmada 2026-05-19.
**Fecha:** 2026-05-19
**Tipo:** integración · cross-team · seguridad · ADR-002
**Descripción:** mt-1 implementa `SincoMiddlewareSessionService.Capabilities` con fallback "always-allow" (`["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"]`) cuando el JWT del host no expone la claim `capabilities`. El contrato canonical `06-contrato-apis-erp.md §0.B.5` lista 5 claims (`UsuarioId, NomUsuario, IdEmpresa, IdSucursal, IdProyecto`) — capabilities NO está confirmada. El roadmap §2.5 dice que "el host PWA mapea su catálogo de perfiles ERP a capabilities", pero el formato exacto del claim (string CSV, array, claim repetida) no está acordado.
**Disparador para abrir slice:** cuando Sergio/David confirmen el contrato. Acciones según respuesta:
- Si el host emite `capabilities`: apretar el default de `SincoMiddlewareSessionService.Capabilities` de "always-allow" a `[]` (denegar por default) y actualizar §0.B.5 del contrato + ADR-002 con el shape exacto.
- Si el host NO emite `capabilities` aún: definir si el módulo Inspecciones lo infiere de otra claim (p. ej. `Permisos`/`Roles` del JWT actual) o si se introduce una nueva claim cross-team en coordinación con el equipo del host.
**Acción cross-team:** llevar la pregunta a la próxima ronda con Sergio/David (`07-preguntas-destrabar-followups.md`). Pregunta concreta: ¿el JWT del host PWA emite hoy una claim que mapee a "capabilities" del módulo (ej. `Permisos`, `Roles`), y si no, en qué horizonte se puede agregar?
**Notas:** no bloquea mt-1 (always-allow es comportamiento histórico equivalente al mock). FU-54 cierra cuando el contrato esté confirmado y los defaults aprieten.

## Cerrados

### #14 — Claims reales desde JWT cuando ADR-002 se resuelva ✅

**Origen:** slice 1b refactorer — candidato §4.2 green-notes
**Fecha apertura \ cierre:** 2026-05-06 / 2026-05-19
**Cierre:** slice `mt-1-jwt-claims-pipeline` (commit `feat(slice-mt-1): JWT claims pipeline + ISessionService + bypass env Test`). Spec firmada `slices/mt-1-jwt-claims-pipeline/spec.md`. ADR-002 cerrado en `Inspecciones/docs/00-investigacion-mercado.md §9.14`.
**Tipo:** deuda técnica · seguridad · ADR-002
**Resolución:** los 15 endpoints HTTP del módulo ahora leen el `tecnicoId` desde el puerto `ISessionService` (`session.IdUsuario.ToString(CultureInfo.InvariantCulture)` — D-MT1-6) en vez del mock `"rmartinez"` hardcodeado. En producción `SincoMiddlewareSessionService` extrae los 5 claims canónicos del JWT del host (`MiddlewareAuthorizationToken.SessionVariables()` del paquete corporativo `SincoSoft.MYE.Common 1.5.1` — paridad 1:1 con proyecto Attachment). Capabilities también se leen del puerto. En env Test, `TestHeaderAwareSessionService` mantiene backward-compat con los ~57 tests legacy; tests nuevos del slice usan `FakeSessionService` puro vía `factory.WithSessionService(fake)`.
**Followups derivados:** FU-44 (propagación JWT al ERP — rola a mt-3), FU-53 (auth feeds NuGet en CI — abierto), FU-54 (cross-team Sergio/David sobre claim `capabilities` — abierto).

### #52 — Endpoint `POST /api/v1/catalogos/sync` sin verificacion de capability PRE-1 (ADR-002) ✅

**Origen:** slice erp-4 review hallazgo #2
**Fecha apertura \ cierre:** 2026-05-19 / 2026-05-19
**Cierre:** slice `mt-1-jwt-claims-pipeline` D-MT1-9 (mismo commit que FU-14).
**Tipo:** seguridad · ADR-002 · endpoint
**Resolución:** el endpoint ahora valida `if (!session.Capabilities.Contains("ejecutar-inspeccion") && !session.Capabilities.Contains("administrar-catalogos")) return 403;` antes de invocar el handler. Body 403: `{ codigoError: "PRE-1", mensaje: "Capability 'ejecutar-inspeccion' o 'administrar-catalogos' requerida." }`. Cubierto por dos tests E2E nuevos en `SessionServicePipelineTests`: §6.5 (sin capability → 403) y §6.6 (con `administrar-catalogos` → no-403, preserva 23 tests existentes de erp-4).

### #36 — Endpoint `POST /inspecciones/{id}/hallazgos` retorna 400 BadRequest en happy path ✅

**Origen:** fix-FU-32 review hallazgo §4 — destrabe FU-32 expuso bug preexistente
**Fecha apertura \ cierre:** 2026-05-11 / 2026-05-11
**Cierre:** commit `629ece0 fix(FU-36): JsonStringEnumConverter en Minimal APIs`. Spec firmado: `slices/fix-FU-36/spec.md`. Debug session: `.planning/debug/fu-36-registrar-hallazgo-400.md`.
**Tipo:** bug · endpoint · alta severidad
**Causa raíz:** `System.Text.Json` (serializer default de ASP.NET Core Minimal APIs) deserializa enums como `int` por default. `RegistrarHallazgoRequest` tiene campos `OrigenHallazgo` y `AccionRequerida` tipados como enums; el test envía strings (`"Manual"`, `"NoRequiereIntervencion"`) y el binding falla con `JsonException`. `RequestDelegateFactory` retorna 400 BadRequest antes de invocar el handler — el `try/catch` del endpoint nunca ve la excepción. El comentario en `Program.cs:28-30` ya reconocía explícitamente que esta configuración quedaba pendiente: "El detalle de configuración del serializer (System.Text.Json + casing + enum como string) se cierra en un slice posterior cuando emerja necesidad concreta." FU-36 fue ese momento.
**Fix aplicado:** `builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))` global en `Program.cs`. Cierra 2 tests rojos del slice 1c + 2 bugs latentes en `ActualizarHallazgo` (`AccionRequerida`) y `FirmarInspeccion` (`DictamenOperacion`) sin tests nuevos.
**Scope ampliado aprobado por usuario:** armonización `CapturadoEn` del test happy path a `2026-05-08T15:00:00Z` (mismo patrón que FU-37 aplicó a slice 1k) + Skip del test ADR-008 replay (consistente con otros tests ADR-008 — Wolverine envelope dedup pendiente FU-15/FU-25).
**Tests:** 28/32 → 29/32 passing + 3 skip + 0 failing en `Inspecciones.Api.Tests`. Domain.Tests sin regresión (197/197).
**Notas:** los 3 skip restantes son todos ADR-008 idempotencia (depende de Wolverine envelope dedup, no de este fix). Los DTOs `GenerarOTRequest` y `RegistrarEvaluacionCualitativaRequest` que usan `string + Enum.TryParse` quedan como deuda separada (no rompen, son inconsistencia estilística).

### #38 — Endpoints `GenerarOT` y `RechazarGenerarOT` retornan 500 en vez de 403 cuando falta capability `generar-ot` ✅

**Origen:** fix-FU-32 review hallazgo §4 — destrabe FU-32 expuso bug preexistente
**Fecha apertura \ cierre:** 2026-05-11 / 2026-05-11
**Cierre:** commit `6cf1ead fix(FU-38): Results.Forbid -> Forbidden403 helper en endpoints`. Spec firmado: `slices/fix-FU-38/spec.md`.
**Tipo:** bug · endpoint · capability handling
**Causa raíz reclasificada:** la hipótesis original (dereferencia null en `ClaimsTecnico.TieneCapabilityGenerarOT`) era incorrecta. Causa real: `Results.Forbid()` requiere `IAuthenticationService` del pipeline ASP.NET Core auth, pero el módulo NO registra `AddAuthentication()` (ADR-002 — identidad 100% del host PWA). La llamada lanza `InvalidOperationException: Unable to find the required 'IAuthenticationService' service` que el `DeveloperExceptionPageMiddleware` traduce a HTTP 500.
**Fix aplicado:** helper estático `Forbidden403(codigoError, mensaje)` que retorna `Results.Json(new { codigoError, mensaje }, statusCode: 403)`, sin depender del pipeline auth. Reemplaza las **6 ocurrencias** de `Results.Forbid()` en `InspeccionesEndpoints.cs` (2 con test rojo visible + 4 latentes en `IniciarInspeccion`, `IniciarInspeccionMonitoreo`, `FirmarInspeccion` x2).
**Tests:** 26/32 → 28/32 passing en `Inspecciones.Api.Tests`. Domain.Tests sin regresión (197/197).
**Notas:** vinculado al FU-11 (`CapabilityRequeridaException` dominio vs HTTP). FU-11 sigue abierto — trata de dónde vive la verificación de capability (dominio vs HTTP), ortogonal al mecanismo de respuesta HTTP 403 que cubrió este slice.

### #32 — Bug preexistente: `Api.Tests` rotos por `RunOaktonCommands(args)` que rompe `WebApplicationFactory<Program>` ✅

**Origen:** slice 1k green/refactorer — diagnóstico transversal al ejecutar `Inspecciones.Api.Tests`
**Fecha apertura / cierre:** 2026-05-08 / 2026-05-11
**Tipo:** deuda técnica · test infra · alta severidad
**Descripción:** Todos los tests de `Inspecciones.Api.Tests` (incluidos los del slice 1k `GenerarOTEndpointTests`) fallaban con `InvalidOperationException: The server has not been started or no web application was configured.` cuando se intentaba usar `WebApplicationFactory<Program>`. La causa raíz era que `src/Inspecciones.Api/Program.cs` llamaba a `RunOaktonCommands(args)` como única rama de arranque, método que consume el lifecycle del host y, al ser invocado durante la construcción del `TestServer`, impedía que `WebApplicationFactory` arrancara el pipeline HTTP. El bug era preexistente desde el slice 1g.
**Resolución (slice `fix-FU-32`, commit pendiente):** Fix consolidado de 6 cambios — (1) `Program.cs` condicional `args.Length > 0 && !args[0].StartsWith("--")` antes de `RunOaktonCommands`, fallback a `RunAsync`; (2) switch local Postgres vs Testcontainers en `InspeccionesAppFactory` via env var `POSTGRES_TEST_CONNSTRING`; (3) `UseSetting` para overrider connection string sobre `appsettings.Development.json`; (4) supresión de `EventLogLoggerProvider` que Wolverine usaba en `DisposeAsync`; (5) override `InvariantGlobalization=false` en `Api.Tests.csproj` para que FluentAssertions formatee mensajes de fallo correctamente; (6) `xunit.runner.json` con `maxParallelThreads: 1`. Resultado: 24/32 tests pass (de 0/32). Los 6 fallos remanentes son bugs preexistentes en handlers/endpoints — registrados como FU-36, FU-37, FU-38.

### #37 — Tests E2E happy path de `GenerarOT` y `RechazarGenerarOT` fallan por timestamp ✅

**Origen:** fix-FU-32 review hallazgo §4 — destrabe FU-32 expuso bug preexistente
**Fecha apertura / cierre:** 2026-05-11 / 2026-05-11
**Tipo:** bug · test infra · alta severidad
**Causa raíz reclasificada:** la descripción original afirmaba que los handlers `GenerarOTHandler` y `RechazarGenerarOTHandler` usaban `DateTime.UtcNow` directo violando la regla CLAUDE.md. **Esa hipótesis era incorrecta** — auditoría confirmó que ambos handlers ya recibían `TimeProvider` por DI y llamaban a `_time.GetUtcNow()` correctamente (`GenerarOTHandler.cs:18-21,37`, `RechazarGenerarOTHandler.cs:18-21,37`). `grep -r "DateTime.UtcNow" src/Inspecciones.{Domain,Application}` devolvió cero coincidencias. El bug real era plumbing: `Program.cs` registra `TimeProvider.System` (wall-clock real) en el contenedor DI, y `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` no reemplazaba ese descriptor por un `FakeTimeProvider` determinístico. Al correr, `_time.GetUtcNow()` devolvía la fecha del sistema (2026-05-11) en vez del `CapturadoEn` canónico de los tests (2026-05-08T15:00:00Z), produciendo el delta de 3 días en `BeCloseTo(CapturadoEn, ...)`.
**Resolución (slice `fix-FU-37`, commit `152e080`):** (1) `tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj` — añadida `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` (versión heredada de Central Package Management). (2) `tests/Inspecciones.Api.Tests/InspeccionesAppFactory.cs` — agregado bloque que remueve los descriptores `ServiceType == typeof(TimeProvider)` y registra un `FakeTimeProvider(new DateTimeOffset(2026, 5, 8, 15, 0, 0, TimeSpan.Zero))` como singleton. (3) Armonización colateral aprobada por el usuario: `tests/Inspecciones.Api.Tests/GenerarOTEndpointTests.cs` línea 29 — la constante `CapturadoEn` se alineó de `14:00Z` a `15:00Z` para coincidir con el timestamp canónico del spec y con el ya usado en `RechazarGenerarOTEndpointTests.cs`. Cero código de producción tocado.
**Resultado:** suite `Inspecciones.Api.Tests` pasa de 24/32 a **26/32 verdes** — los 2 happy paths de slice 1k (`POST_generar_ot_happy_path_responde_202_Accepted_con_body_correcto`) y slice 1l (`POST_rechazar_generar_ot_happy_path_responde_200_OK_con_body_correcto`) ahora verdes. Los 4 rojos remanentes son FU-36 (×2) y FU-38 (×2), out-of-scope. El test latente `RegistrarHallazgo.happy_path` (slice 1c) sigue rojo por FU-36 — el endpoint retorna 400 BadRequest antes de llegar al aserto de timestamp, así que el fix de FU-37 no lo destrababa por sí solo.

### #34 — FU-30 cerrado en código pero no marcado cerrado en FOLLOWUPS.md ✅

**Origen:** slice 1l review — hallazgo #1
**Fecha apertura / cierre:** 2026-05-08 / 2026-05-08
**Tipo:** deuda técnica · doc
**Descripción:** El refactorer del slice 1l cerró FU-30 (comentario incorrecto en switch `AplicarEvento`) en código mediante el refactor #2 del slice, pero no actualizó `FOLLOWUPS.md` para mover la entrada a la sección `## Cerrados`. Detectado en la auditoría del reviewer.
**Resolución:** el reviewer del slice 1l aplicó el cierre en `FOLLOWUPS.md` (entrada FU-30 marcada con ✅ con descripción de resolución) y el orquestador `infra-wire` del slice 1l completó la convención moviendo tanto FU-30 como FU-34 a la sección `## Cerrados`. Este followup se cierra junto con su apertura.

### #30 — Comentario de slice incorrecto en `AplicarEvento` switch: `ItemMonitoreoOmitido_v1` bajo "Slice 1i" ✅

**Origen:** slice 1k refactorer — observación transversal
**Fecha apertura / cierre:** 2026-05-08 / 2026-05-08
**Tipo:** deuda técnica · naming
**Descripción:** En `Inspeccion.cs` el switch `AplicarEvento` agrupaba `MedicionRegistrada_v1` e `ItemMonitoreoOmitido_v1` bajo el comentario `// Slice 1i — RegistrarMedicion`. `ItemMonitoreoOmitido_v1` pertenece al slice 1j (`OmitirItemMonitoreo`), no al 1i.
**Resolución:** el refactorer del slice 1l (refactor #2) separó los comentarios: `// Slice 1i — RegistrarMedicion` para `MedicionRegistrada_v1` y `// Slice 1j — OmitirItemMonitoreo` para `ItemMonitoreoOmitido_v1`. También actualizó el bloque 1k para mencionar 1l: `// Slice 1k — GenerarOT / Slice 1l — RechazarGenerarOT`. Verificado en `Inspeccion.cs` — 197 tests pasan sin regresión.

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

### #42 — Slice 1o: test E2E faltante para escenario PRE-5 cross-hallazgo (§6.9) 🟢

**Origen:** slice 1o-actualizar-repuesto review §3 hallazgo #1
**Fecha:** 2026-05-11
**Tipo:** deuda técnica · cobertura
**Descripción:** `ActualizarRepuestoEndpointTests` lista §6.9 (RepuestoId en hallazgo distinto → 404 PRE-5) en el docblock de clase pero no tiene método `[Fact]` propio. El escenario está cubierto a nivel de dominio en `ActualizarRepuestoTests`. A nivel HTTP la distinción §6.8 / §6.9 produce la misma respuesta `404 + "PRE-5"` — no afecta la verificación de contrato. Agregar `PATCH_repuesto_repuesto_en_hallazgo_incorrecto_responde_404_PRE5` en Api.Tests para completar la cobertura E2E del escenario.

### #43 — Colisión de EquipoIds hardcoded en Api.Tests (manifestación FU-39) 🟢

**Origen:** slice 1o-actualizar-repuesto review §3 hallazgo #2 (reportado por orquestador durante fase green)
**Fecha:** 2026-05-11
**Tipo:** deuda técnica · infraestructura de tests
**Descripción:** Los EquipoIds de siembra del slice 1o originalmente asignados (80001-80010) colisionaron con slices 1m/1n. El orquestador los renombró a 100001-100010. La causa raíz es FU-39: IDs hardcoded en tests de integración sin aislamiento por colección. Cada nuevo slice requiere elegir rangos manualmente. Solución: cuando el número de suites haga inviable la asignación manual, implementar generación dinámica de IDs de siembra (`Guid.NewGuid()` para streams Marten, o rangos con offset generado por colección) para eliminar la necesidad de coordinación manual.

### #44 — JWT del request entrante reemplaza `MaquinariaErpOptions.JwtToken` (ADR-002) 🟢

**Origen:** slice erp-1 acople Maquinaria_V4 (2026-05-19)
**Fecha:** 2026-05-19
**Tipo:** seguridad · ADR-002 · adapter
**Descripción:** El `MaquinariaErpClient` actual usa un JWT fijo desde `appsettings.Maquinaria:JwtToken` configurado al startup. La identidad real del módulo es 100% del host PWA (decisión 2026-05-05) — el JWT que valida Maquinaria_V4 debe ser **el mismo** que entrega el host en la request entrante. Solución: agregar `IHttpContextAccessor` al cliente y propagar `HttpContext.Request.Headers["Authorization"]` en cada llamada (DelegatingHandler con `TypedHttpClientFactory`), reemplazando el `DefaultRequestHeaders.Authorization` fijo.
**Disparador para abrir slice:** cierre de ADR-002 (mecanismo de identidad del host PWA) o primer slice de saga que invoque Maquinaria_V4 fuera del request-scope (ej. `SincronizarDictamenVigenteSaga` desde Wolverine outbox — ese caso requiere otra estrategia: token de servicio dedicado o "service principal").
**Notas:** vinculado a FU-14. Mientras tanto el adapter sirve para QA, smoke testing y endpoints `/admin/*` con token configurado manualmente.

### #45 — Adapters de catálogo: cache de ETag cliente-side + persistencia en Marten (ADR-004) 🟢

**Origen:** slice erp-1 acople Maquinaria_V4 (2026-05-19)
**Fecha:** 2026-05-19
**Tipo:** infra · ADR-004
**Descripción:** El `MaquinariaErpClient` ya soporta `If-None-Match` / `304 Not Modified` (verificado por test). Pero la cadena completa "GET catálogo → cache ETag → persistir en Marten → emitir cuando staleness > N" todavía no existe. Necesita: (a) tabla/documento Marten `CatalogoMetadataLocal { Codigo, EtagUltimoSync, UltimoSyncEn }` por catálogo; (b) job de sync que lea el ETag, llame al adapter con `If-None-Match`, y si responde 304 solo actualice `UltimoSyncEn`; si responde 200, upserte los documentos individuales (`CausaFallaLocal`, `TipoFallaLocal`, `RepuestoLocal`, etc.) y actualice el ETag; (c) endpoint admin `/api/v1/admin/sincronizar-catalogos` que dispare todos los syncs en paralelo; (d) modo degradado: si la última sync es >7 días, bloquear arranque de inspección con error claro.
**Disparador para abrir slice:** cuando se integre la PWA cliente y se confirme el flujo de sync on-app-open (ADR-004 canonical 2026-05-05). Ahora bloquea: la PWA puede consumir directamente los endpoints `/admin/*-erp` para QA, sin cache.
**Notas:** el shape de `RepuestoLocal` (campo `ParteIdsCompatibles: IReadOnlyList<int>`) NO viene del endpoint `/api/productos` de Maquinaria_V4 (que solo expone Codigo/Descripcion/UnidadContable). La compatibilidad parte ↔ producto vive en otro punto del ERP — confirmar con David antes del slice.

### #46 — Endpoints de Inspecciones que aún no acoplan a Maquinaria_V4 (sagas + comandos de escritura) 🟢

**Origen:** slice erp-1 acople Maquinaria_V4 (2026-05-19)
**Fecha:** 2026-05-19
**Tipo:** integración · sagas ADR-006
**Descripción:** El adapter HTTP está completo para los 8 endpoints expuestos por Maquinaria_V4 (M-3 equipos, M-5 partes, M-7 causas-falla, M-8 tipos-falla, M-W-1 dictamen-vigente, P-6 cerrar-preop, M-16 rutinas-monitoreo por equipo, M-4 productos). Falta el cableado **dentro** de los handlers/sagas existentes para que invoquen al adapter:
- `DescartarNovedadPreop` (slice 1n) NO invoca `CerrarPreoperacionalFallasAsync` — hoy solo emite el evento local. Pendiente: handler outbox que llame a Maquinaria_V4 con `PodIds=[NovedadId]`.
- `FirmarInspeccion` (slice 1g) NO invoca `ActualizarDictamenEquipoAsync` (M-W-1). Pendiente: `SincronizarDictamenVigenteSaga`.
- `GenerarOT` (slice 1k) NO invoca POST `/api/ordenes-trabajo` — el endpoint **no existe** en Maquinaria_V4 (slice 8 Maquinaria pausado por DDL DBA). Bloqueante real ❌.
- Catálogos: no hay sync periódico cliente-side hoy. Cada PWA-open debe disparar sync de Causas/Tipos/Productos/Rutinas-Monitoreo/Equipos/Partes con If-None-Match. Ver FU-45.
**Disparador para abrir slice:** decidido por orquestador caso a caso. La integración mínima de `DescartarNovedadPreop`→P-6 es de bajo riesgo y desbloquea piloto. La saga M-W-1 es el siguiente paso natural tras `FirmarInspeccion`.
**Notas:** Cada acople es un slice nuevo `feat(slice-erp-{X})` con TDD ceremonial completo (domain-modeler opcional si no extiende dominio, red→green→refactor→reviewer). El adapter ya está testeado — los nuevos slices solo cubren handler/saga + integración outbox.

### #47 — Application.Tests requiere Docker (Testcontainers Postgres) 🟢

**Origen:** validación de regresión slice erp-1 (2026-05-19)
**Fecha:** 2026-05-19
**Tipo:** deuda técnica · tests infraestructura
**Descripción:** `tests/Inspecciones.Application.Tests/Inspecciones/PostgresFixture.cs` levanta Postgres con `Testcontainers.PostgreSql`. Sin Docker corriendo, los 40 tests fallan inmediatamente. Api.Tests ya tiene fallback a Postgres local vía env var `POSTGRES_TEST_CONNSTRING` (fix FU-32 commit `48b8575`) — Application.Tests no se beneficia. Replicar el mismo fallback acá permite correr en estaciones sin Docker. **No bloquea** slices ni el adapter ERP — Domain.Tests (246) y Infrastructure.Tests (14) cubren sin Postgres.
**Disparador para abrir slice:** primera vez que un desarrollador con Postgres local pero sin Docker necesite iterar en Application.Tests.
**Notas:** copy/paste del patrón de `InspeccionesAppFactory` a `PostgresFixture` — bajo riesgo, baja prioridad mientras CI tenga Docker disponible.

### #48 — Política Wolverine `ArgumentException → MoveToErrorQueue` es global 🟢

**Origen:** slice erp-2-descartar-novedad-preop-outbox refactor
**Fecha:** 2026-05-19
**Tipo:** deuda técnica · infra
**Descripción:** La regla `opts.Policies.OnException<ArgumentException>().MoveToErrorQueue()` registrada en `Program.cs` aplica a todos los handlers, no solo a `DescartarNovedadPreopErpListener`. Si algún handler futuro usa `ArgumentException` para indicar un error de negocio recuperable (poco probable pero posible), esta política lo enviaría a dead-letter sin retry. La solución limpia es acotar la política al tipo de mensaje: `opts.HandlerFor<NovedadPreopDescartada_v1>().OnException<ArgumentException>().MoveToErrorQueue()` — cambio trivial si surge la colisión.
**Disparador para abrir slice:** primer handler que necesite retry en `ArgumentException`.
**Notas:** Riesgo bajo en MVP. Los handlers de dominio actuales no usan `ArgumentException` para flujos recuperables.

### #49 — `NovedadPreopErpCierreFallido_v1` record no ejercido por tests 🟢

**Origen:** slice erp-2-descartar-novedad-preop-outbox review §3 hallazgo #2
**Fecha:** 2026-05-19
**Tipo:** deuda técnica · observabilidad
**Descripción:** El record `NovedadPreopErpCierreFallido_v1` (5 campos: `InspeccionId`, `NovedadId`, `IntentosAgotados`, `UltimoError`, `EsReintentable`) existe en producción pero ningún test lo instancia directamente. La señal de observabilidad se emite vía `LoggerMessage` (que sí cubre el caso), pero el record en sí no tiene cobertura de constructor. Si a futuro se extiende para alimentar una proyección Marten (spec §8), el contrato del record puede divergir del `LoggerMessage` silenciosamente.
**Disparador para abrir slice:** cuando se cree la proyección "novedades con cierre ERP fallido" (spec §8). En ese momento alinear el record con el `LoggerMessage` o reemplazar el record por la struct del log.
**Notas:** No bloqueante en MVP mientras sea solo log estructurado.

### #50 — Assertions de tipo demasiado genéricas en tests 4xx del listener ✅

**Origen:** slice erp-2-descartar-novedad-preop-outbox review §3 hallazgo #3
**Fecha:** 2026-05-19 · **Cerrado:** 2026-05-19
**Tipo:** deuda técnica · tests
**Descripción:** Los tests `Listener_erp_400_no_reintenta_va_a_dead_letter_INV_L3` y `Listener_erp_404_no_reintenta_va_a_dead_letter_INV_L3` verifican `ThrowAsync<Exception>()` (tipo base) en lugar de `ThrowAsync<MaquinariaErpException>()`. Si el adapter cambia a lanzar un tipo de excepción diferente, los tests seguirían en verde aunque la política de retry de Wolverine (que filtra por `MaquinariaErpException`) pudiera verse afectada.
**Disparador para abrir slice:** primera vez que se refactorice el contrato de excepciones del adapter `MaquinariaErpClient`.
**Notas:** Cerrado en la iteración 2 post-review del refactorer (2026-05-19). Ambos tests cambiados a `ThrowAsync<MaquinariaErpException>()` con aserción adicional sobre `StatusCode` exacto (`BadRequest` y `NotFound` respectivamente).

### #51 — Test E2E de `POST /api/v1/catalogos/sync` con Postgres real 🟢

**Origen:** slice erp-4 refactor-notes §Refactors descartados
**Fecha:** 2026-05-19
**Tipo:** deuda técnica (cobertura de test)
**Descripción:** El endpoint `POST /api/v1/catalogos/sync` tiene cobertura del handler vía `Infrastructure.Tests` con fake repo y WireMock, pero carece de test E2E que ejercite `MartenCatalogoSyncRepository` contra Postgres real. Un test en `Inspecciones.Api.Tests` con `WebApplicationFactory` + `WireMock` + Testcontainers verificaría la atomicidad wipe+replace+state de `MartenCatalogoSyncRepository` y el mapeo completo del response DTO.
**Disparador para abrir slice:** cuando se añada el segundo catálogo al sync-all (equipos o rutinas técnicas) o cuando un bug de atomicidad sea reportado en producción.
**Notas:** requiere agregar WireMock al proyecto `Inspecciones.Api.Tests` (ya tiene Testcontainers). Un solo happy-path es suficiente — los escenarios 304/error/vaciado-sospechoso ya están cubiertos por `Infrastructure.Tests`.

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
