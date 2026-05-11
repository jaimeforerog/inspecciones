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

## Cerrados

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
