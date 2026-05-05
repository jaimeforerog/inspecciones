# Roadmap — Módulo de Inspecciones Técnicas

**Última actualización:** 2026-05-04
**Estado general:** Diseño 98% · Pendiente arranque de implementación · 2 pasos bloqueados por externos (0.13 ADR-002 + 0.18 DDL preop)
**Documentos clave:** `00-investigacion-mercado.md`, `01-modelo-dominio.md` (§15 fuente de verdad), `02c..e` wireframes UX, `02f/g/h` flowcharts narrativos por flujo, `02i/j/k` workflows basados en nodos (Markdown + HTML interactivos), `03-sow-consultor.md`, `04-brief-consultor-producto.md`, `05-catalogo-eventos.md`, `06-contrato-apis-erp.md`, `07-preguntas-destrabar-followups.md`, `08-volumenes-clientes-erp.md`

---

## Leyenda de estados

| Estado | Significado |
|---|---|
| ✅ | Completado |
| 🔄 | En curso |
| ⏳ | Pendiente — próximo en la cola |
| 🚧 | Bloqueado por dependencia externa |
| ⚪ | Futuro — no priorizado para MVP |
| ⏸ | Diferido a fase posterior |

---

## Fase 0 — Investigación y diseño

| # | Paso | Estado | Notas |
|---|---|---|---|
| 0.1 | Investigación de mercado (SaaS, ERPs, open-source, LATAM) | ✅ | `00-investigacion-mercado.md` |
| 0.2 | Selección de referente visual y funcional | ✅ | Tractian + patrones nativos Sinco MYE |
| 0.3 | Brief para consultor de producto (ingeniero mecánico) | ✅ | `04-brief-consultor-producto.md` |
| 0.4 | Modelo de dominio inicial | ✅ | `01-modelo-dominio.md` §1–§14 |
| 0.5 | Reconciliación con plantillas Excel del ERP | ✅ | §12 del modelo |
| 0.6 | Refinamiento final del modelo (review event-by-event) | ✅ | §15 — fuente de verdad |
| 0.7 | Wireframes flujo principal del técnico | ✅ | **Fuente vigente 2026-04-30:** `Plantillas Excel/mock del diseño.docx` (mock de Daniel — 13 pantallas en 4 secciones). Los wireframes HTML previos `02-wireframes-mobile.html` se eliminaron el 2026-04-30; el mock de Daniel es la única referencia visual. |
| 0.8 | Wireframes flujo novedades preop (variante B) | ✅ | **Fuente vigente 2026-04-30:** sección "Importar hallazgo" del mock de Daniel (image11–13 con tabs Preoperacional/Seguimiento + wizard heredado). HTML previo `02b-wireframes-novedades-preop.html` eliminado el 2026-04-30. Patrón final: 2 acciones por novedad (📥 Importar + 🗑 Descartar con motivo autogenerado), sin botón "Seguimiento" inline (se accede vía wizard). Detalle modelado en §15.9 de `01-modelo-dominio.md`. |
| 0.9 | Comparativa de variantes UX (A/B/C) | ✅ | `02c-variantes-ux-novedades.html` |
| 0.10 | Wireframes flujo seguimientos del equipo | ✅ | `02d-wireframes-seguimientos.html` |
| 0.11 | SOW / arquitectura resumida para equipo interno | ✅ | `03-sow-consultor.md` |
| 0.12 | ADR-001: REST sobre VPN (no CDC) | ✅ | `00-investigacion-mercado.md` §9.11 |
| 0.13 | ADR-002: estrategia de identidad | 🟡 | Tentativo. Recomendación original (Entra ID) revisada el 2026-04-29 — el módulo no elige IdP, hereda del host PWA. Pendiente confirmar mecanismo del host. |
| 0.14 | ADR-003: Generación de OT correctiva en MYE | ✅ | `01-modelo-dominio.md` §13 |
| 0.15 | ADR-004: Sincronización de catálogos | ✅ | `00-investigacion-mercado.md` §9.15 |
| 0.16 | ADR-005: Azure SignalR para push del cierre | ✅ | `01-modelo-dominio.md` §14 |
| 0.17 | Validar modelo con consultor mecánico | ✅ | **Cerrado 2026-04-30.** Sesión con Sergio (consultor producto, ingeniero mecánico) entregó 4 observaciones que ya están aplicadas al modelo: (1) **Dictamen siempre obligatorio + sync al equipo en MYE** — confirmó regla #11 del brief y agregó nuevo endpoint M-W-1 `PUT /equipos/{id}/dictamen-vigente` (modelado en §17 ADR-007 + brief regla #11 actualizada). (2) **Cancelar generación de OT** post-firma — comando `RechazarGenerarOT` + evento `GeneracionOTRechazada_v1` + estado terminal `RechazadaPorAprobador` (modelado en §17 + brief regla #13 + invariante I-F6). (3) **Una sola inspección abierta por equipo** — invariante I-I1 + proyección `InspeccionAbiertaPorEquipoView` con índice único Postgres (§15.7 + §15.12.6 + brief regla #12). (4) **PDF de inspección como adjunto a OT** — saga `GenerarPdfInspeccionSaga` + endpoint M-1b + eventos `PdfInspeccionGenerado_v1` y `PdfAdjuntadoAOT_v1` (§17 + brief regla #14). Adicionalmente: descarte rápido de novedades preop (§15.9 — superseded la propuesta inicial de bulk con modal). Ver brief consultor §6 reglas 11–14 para resumen ejecutivo. |
| 0.18 | Recibir DDL del preoperacional + contratar shape DTO | 🚧 | Espera por equipo del preop |
| 0.19 | Confirmar alcance de rutina técnica única por grupo | ✅ | Decidido 2026-04-27 (§12.10 + §12.11): una sola rutina técnica por grupo de mantenimiento (no se subdivide motor/hidráulica/post-mantenimiento). MVP usa único `TipoInspeccion = Tecnica`. Monitoreo (con `MedicionEsperada` rango min/max) queda en 10.4 — refinado 2026-04-30 §12.11.5. |
| 0.20 | Limpieza de referencias obsoletas en §2.1, §6, I7 del modelo | ✅ | Banners `⚠️ SECCIÓN HISTÓRICA` y notas inline `⚠️ OBSOLETO` añadidos en §2.1, §3, §6, §7, §7.4.5, §12.10.8, §12.10.9, §12.10.10 e I7 (2026-04-28). Tabla §15.11 marcada como completada. |
| 0.21 | Refinamientos finales del modelo y contrato (sesión 2026-05-04, refinada 2026-05-05) | ✅ | **(a) Tipos de IDs:** PKs del ERP migran de `Guid` a `int` (System.Int32) con `<X>Codigo: string` para UI/URLs; IDs internos del módulo siguen `Guid` v7. Aplicado a modelo, eventos, comandos, contrato y followups (~470 líneas modificadas). **(b) Rutina técnica per-equipo (β):** cardinalidad 1 rutina/equipo, asignación explícita en ERP — `M-3b` trae `rutinaTecnicaId: int`. Técnico no elige (auto-resuelta — UX MVP preservada). **(c) Rutinas monitoreo por grupo de mantenimiento (refinado 2026-05-05):** asignación **derivada por grupo**, no per-equipo. M-3b trae `grupoMantenimientoId` del equipo; M-16 trae cada rutina con `grupoMantenimientoId`. Cliente filtra `r.grupoMantenimientoId == equipo.grupoMantenimientoId`. Sin tabla intermedia en ERP. Técnico elige entre las rutinas activas del grupo. **(d) Consolidación M-3b:** absorbe partes + `rutinaTecnicaId` + `grupoMantenimientoId` en una sola llamada. M-4 eliminado. **(e) M-17 nuevo (crítico MVP):** `GET /catalogos/rutinas` — sync de rutinas técnicas (on-app-open desde 2026-05-05 ADR-004 canonical), cierra gap detectado en revisión por flujos. **(f) M-16 promovido a MVP 2026-05-05** (antes Fase 2): `GET /catalogos/rutinas-monitoreo` — sync on-app-open. **(g) Adjuntos en monitoreo cerrados:** anclaje xor (`ItemId` o `HallazgoId`), siempre opcional, límite 5/entidad. **(h) Backend preop:** confirmado SQL Server relacional on-prem (era "asumido"). **(i) ADR-004 sync on-app-open canonical (2026-05-05):** sin cron nocturno; cliente PWA dispara sync delta cada apertura con `If-None-Match`; persistencia IndexedDB; sin red al abrir → último cached. |
| 0.22 | Diagramas de flujo y workflows visuales (sesión 2026-05-04) | ✅ | Creados 6 docs nuevos + 3 HTMLs interactivos: **flowcharts narrativos** `02f` técnica MVP, `02g` monitoreo Fase 2, `02h` seguimientos. **Workflows basados en nodos** (BPMN/n8n style con carriles por actor) `02i` técnica, `02j` monitoreo, `02k` seguimientos. **HTMLs interactivos** equivalentes con Mermaid v10 + svg-pan-zoom para mejor visualización. Cubren ciclo completo: sync de catálogos (cambiado a on-app-open en 2026-05-05), inicio, importar preop, hallazgos, repuestos, adjuntos, firma, sagas post-firma, aprobación OT, monitoreo (promovido a MVP 2026-05-05), ciclo del aggregate `SeguimientoHallazgo`. |
| 0.23 | EDA Sinco — alineación §3.2 + §5 (sesión 2026-05-03/04) | ✅ | Modelo §8 aclara que el patrón aplicado es §3.2 "Comando asíncrono" del guide EDA Sinco (no §3.4 "Evento de integración") por restricción de infraestructura (ERP on-prem sin bus). ADR-006 mapea los 3 mecanismos del guide §5 (clave de idempotencia, detección por estado, concurrencia optimista) a su implementación concreta en el módulo. |

---

## Fase 1 — Fundaciones cloud (Azure)

> **Crítico**: este es el primer despliegue Azure de Sinco. Sin landing zone existente. Es workstream paralelo y bloqueante para Fase 2-3.

| # | Paso | Estado | Notas |
|---|---|---|---|
| 1.1 | Aprovisionar suscripción Azure | ⏳ | Sinco corporate |
| 1.2 | Definir naming convention y tagging strategy | ⏳ | Estándar Microsoft CAF |
| 1.3 | Resource Groups por ambiente (dev/stg/prod) | ⏳ | |
| 1.4 | Configurar VPN site-to-site Sinco on-prem ↔ Azure | ⏳ | Bloqueante para integración |
| 1.5 | Configurar networking (VNets, subnets, NSGs) | ⏳ | |
| 1.6 | Azure Active Directory + Entra ID tenant | ⏳ | Para auth de aplicación |
| 1.7 | RBAC y policies de gobierno | ⏳ | |
| 1.8 | Provisionar Azure Container Apps | ⏳ | Hosting del backend |
| 1.9 | Provisionar Azure Database for PostgreSQL Flexible | ⏳ | Marten event store. **Dimensionamiento 2026-04-30** (basado en volúmenes de los 27 clientes ERP — `08-volumenes-clientes-erp.md` hallazgo 7): tier inicial **B2s** suficiente para catálogos (~260K entidades totales: 10K equipos + 53K partes + 4.5K rutinas + 190K SKUs). Carga real esperada: eventos de inspecciones — ~100-300 inspecciones/día por cliente intensivo (EXPLANAN, FUNDACIONES). Plan de escalado vertical antes de exponer a clientes con > 30K SKUs. |
| 1.10 | Provisionar Azure Blob Storage | ⏳ | Adjuntos de inspecciones + PDFs de inspección (decisión 2026-04-30 §17 ADR-007). **Cuota 2026-04-30**: ≥ 1 TB para primer año asumiendo retención 7 años. Containers separados: `adjuntos-hallazgos` (fotos/PDFs subidos por técnicos en el wizard) y `inspecciones-pdf` (PDFs generados por `GenerarPdfInspeccionSaga`). Lifecycle policy con tiering a Cool tras 90 días sin acceso. |
| 1.11 | Provisionar Azure Key Vault | ⏳ | Secretos y connection strings |
| 1.12 | Provisionar Azure Application Insights | ⏳ | Observabilidad |
| 1.13 | Provisionar Azure SignalR Service (Standard tier) | ⏳ | Push del cierre de inspección |
| 1.14 | Provisionar Azure API Management | ⏳ | Gateway hacia APIs cloud |
| 1.15 | Configurar GitHub Actions / Azure DevOps pipelines | ⏳ | CI/CD |
| 1.16 | Setup de logging centralizado y alertas | ⏳ | App Insights + Log Analytics |
| 1.17 | Documentar runbook operativo inicial | ⏳ | Cómo desplegar, troubleshooting básico |

---

## Fase 2 — Autenticación y autorización

> **⚠️ Aclaración 2026-04-29 + decisión 2026-05-05:** Inspecciones es módulo dentro de la PWA Sinco MYE móvil existente. El módulo **no maneja identidad** — hereda 100% del host PWA y consume el contexto (token + claims) tal como llega. **Decisión 2026-05-05 (Jaime):** se descarta cualquier sync de usuarios al IdP, app registration propio o catálogo local de usuarios. El módulo solo: (a) valida cloud-side el token que llega del host, (b) autoriza por capability (no por perfil), (c) usa el `tecnicoId` que viaja en el JWT como dato opaco. Cualquier gestión de identidad — alta, baja, asignación de proyectos, perfiles — vive en el ERP/host.

| # | Paso | Estado | Notas |
|---|---|---|---|
| 2.1 | Confirmar mecanismo actual de auth del host PWA Sinco MYE móvil | ⏳ | **Bloqueante real de ADR-002**. Necesario para definir cómo se valida el token cloud-side (issuer, JWKS, claims esperadas) |
| 2.5 | Definir matriz comando → capability requerida (least privilege) | ⏳ | **No predefine perfiles del ERP** (técnico, supervisor, etc.). La matriz define **capabilities** (verbos como `ejecutar-inspeccion`, `generar-ot`, `auditar-inspecciones`, `recibir-alertas-sla`, `recibir-alertas-ot-fallida`, `recibir-alertas-ot-rechazada` —última agregada 2026-04-30) que el host PWA mapea a su catálogo de perfiles ERP. El módulo nunca consulta "eres técnico" — consulta "tienes capability X". Independiente del IdP elegido (ADR-002). |
| 2.6 | Implementar JWT validation middleware en backend cloud | ⏳ | Configurable según IdP del host |
| 2.7 | Implementar JWT validation en APIs Sinco on-prem | ⏳ | Cross-team con equipos Sinco |
| 2.8 | Política de gestión de claims (proyectos del técnico) | ⏳ | Mapeo del claim que use el host (p. ej. `sinco_obras` literal del ERP si Entra ID; el módulo lo expone internamente como `proyectos` siguiendo decisión 2026-04-30 followup #4). |
| 2.9 | Tests de auth end-to-end | ⏳ | |

---

## Fase 3 — Backend core (.NET 8 + Marten + Wolverine)

### 3.A Setup base

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.1 | Setup solución .NET con estructura modular | ⏳ | `01-modelo-dominio.md` §9 estructura sugerida |
| 3.2 | Configurar Marten event store | ⏳ | PostgreSQL connection, snapshots policy |
| 3.3 | Configurar Wolverine (mediator + outbox) | ⏳ | |
| 3.4 | Instrumentación con OpenTelemetry / App Insights | ⏳ | Tracing distribuido |
| 3.5 | Repositorio + CI/CD inicial | ⏳ | GitHub Actions |

### 3.B Aggregate `InspeccionTecnica`

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.6 | Implementar value objects (`UbicacionGps`, `Hallazgo`, etc.) | ⏳ | |
| 3.7 | Eventos lifecycle (5): `InspeccionIniciada`, `Firmada`, `Cerrada`, `CerradaSinOT`, `Cancelada` | ⏳ | |
| 3.8 | Eventos hallazgos (3): `HallazgoRegistrado`, `Actualizado`, `Eliminado` | ⏳ | Soft delete. `HallazgoRegistrado_v1` lleva `Origen ∈ {PreOperacional, Manual, Seguimiento}` + `NovedadPreopOrigenId?` + `SeguimientoOrigenId?` (trazabilidad cross-inspección). Invariantes I-H10/I-H11 (§15.3). |
| 3.9 | Evento `NovedadPreopDescartada` | ⏳ | NO crea hallazgo |
| 3.10 | Eventos repuestos (3): `RepuestoEstimado`, `Actualizado`, `Removido` | ⏳ | |
| 3.11 | Eventos adjuntos (2): `AdjuntoSubido`, `Eliminado` | ⏳ | Pattern SAS upload |
| 3.12 | Eventos firma atómicos (3): `Diagnostico`, `Dictamen`, `Firmada` | ⏳ | Comando consolidado `FirmarInspeccion` |
| 3.13 | Evento `OTGeneracionFallida` | ⏳ | Estado `CierrePendienteOT` |
| 3.14 | Comandos + handlers (15) | ⏳ | Validan invariantes I-H1 a I-H9, I-F1 a I-F3 |
| 3.15 | Validaciones pre-firma V-F1 a V-F7 | ⏳ | En el handler `FirmarInspeccion` |
| 3.16 | Tests unitarios del aggregate | ⏳ | Cobertura ≥80% en lógica de dominio |

### 3.B' Extensión Monitoreo del aggregate `Inspeccion` (NUEVO MVP — decisión 2026-05-05)

> **Decisión 2026-05-05 (Jaime):** monitoreo entra al MVP — antes estaba en Fase 10.4 diferido. Aggregate **unificado** con discriminador `Tipo: TipoInspeccion ∈ {Tecnica, Monitoreo}` (no aggregate separado). Reusa firma, dictamen, sagas OT, seguimientos. Modelo en `01-modelo-dominio.md §12.11.5`.

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.16a | Enum `TipoInspeccion = {Tecnica, Monitoreo}` + value objects `MedicionEsperada`, `EvaluacionCualitativaEsperada`, `ItemRutinaMonitoreoSnapshot` | ⏳ | §12.11.5 puntos 1, 3, 7 |
| 3.16b | Eventos monitoreo (3): `MedicionRegistrada_v1`, `EvaluacionCualitativaRegistrada_v1`, `ItemMonitoreoOmitido_v1` | ⏳ | §12.11.5 punto 5. Soft delete no aplica — items no se borran, se omiten con motivo |
| 3.16c | Extender `InspeccionIniciada_v1` con `RutinaMonitoreoSeleccionadaId` + `ItemsSnapshot` (nullable cuando `Tipo=Tecnica`) | ⏳ | §12.11.5 punto 7. Snapshot necesario porque `FueraDeRango` se calcula contra `MedicionEsperada` snapshoteada |
| 3.16d | Extender `OrigenHallazgo` con valor `Monitoreo` + invariantes (Origen=Monitoreo → MedicionOrigenId obligatorio + AccionRequerida=RequiereSeguimiento + Tipo del stream=Monitoreo) | ⏳ | §12.11.5 punto 8 |
| 3.16e | Comando + handler `IniciarInspeccionMonitoreo` (hermano de `IniciarInspeccion`) | ⏳ | §12.11.5 punto 11. Valida I-I1, I-I2 (adaptado: equipo cuyo grupo no tiene rutinas-monitoreo activas → 422), I-I3 + nueva regla `rutina.GrupoMantenimientoId == equipo.GrupoMantenimientoId` (decisión 2026-05-05). Emite `InspeccionIniciada_v1` con `Tipo=Monitoreo` + items snapshoteados |
| 3.16f | Comando + handler `RegistrarMedicion` — emite atómicamente `MedicionRegistrada_v1` (con `FueraDeRango` calculado) + `HallazgoRegistrado_v1` (con `Origen=Monitoreo`, `RequiereSeguimiento`) cuando fuera de rango | ⏳ | §12.11.5 punto 6. Tests obligatorios: dentro de rango (1 evento), fuera (2 eventos atómicos), revalidación que `RutinaMonitoreoLocal` tiene rango snapshoteado |
| 3.16g | Comando + handler `RegistrarEvaluacionCualitativa` — emite `EvaluacionCualitativaRegistrada_v1` + atómicamente `HallazgoRegistrado_v1` cuando `Calificacion=Malo` | ⏳ | §12.11.5 punto 6. Decisión 2026-04-30: solo `Malo` dispara hallazgo, no `Regular` |
| 3.16h | Comando + handler `OmitirItemMonitoreo` — emite `ItemMonitoreoOmitido_v1` con motivo (texto libre del técnico) | ⏳ | Cubre el caso "no pude medir" (multímetro descargado, sensor inaccesible, etc.). No dispara hallazgo. La firma valida que items omitidos no quedan sin justificar |
| 3.16i | Extender comando `AdjuntarArchivo` para anclar adjunto a `ItemId` (xor con `HallazgoId`) | ⏳ | §12.11.5 punto 12. Invariante `(ItemId == null) XOR (HallazgoId == null)`. En `Tipo=Tecnica`, `ItemId` siempre null |
| 3.16j | Tests del aggregate para flujo monitoreo completo: iniciar → medir N items (mix dentro/fuera de rango + cualitativos) → omitir uno → adjuntar fotos a items → firmar | ⏳ | Cobertura ≥85% rama monitoreo. Rebuild test obligatorio (CLAUDE.md) — replay de eventos sobre stream vacío reproduce estado |

### 3.C Aggregate `SeguimientoHallazgo`

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.17 | Implementar aggregate + value objects | ⏳ | `01-modelo-dominio.md` §15.8 |
| 3.18 | Evento `SeguimientoAbierto` | ⏳ | Disparado por saga al firmar |
| 3.19 | Evento `SeguimientoResuelto` | ⏳ | "Sin intervención" |
| 3.20 | Evento `SeguimientoEscalado` | ⏳ | "Intervención" + nuevo `HallazgoRegistrado` atómico (mismo handler, único `SaveChangesAsync` cruzando dos streams). El nuevo hallazgo lleva `Origen=Seguimiento` + `SeguimientoOrigenId={id}`; el evento `SeguimientoEscalado_v1` lleva `HallazgoEscaladoId` + `InspeccionCierreId` (trazabilidad bidireccional, §15.8.4). Invariantes I-S1..I-S3 (§15.8.7). |
| 3.21 | Comandos `ResolverSeguimiento`, `EscalarSeguimiento` | ⏳ | |
| 3.22 | Job nocturno SLA: alertar +90 días | ⏳ | Wolverine scheduled task |
| 3.23 | Tests unitarios | ⏳ | |

### 3.D Sagas e integración (ACL)

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.24 | Saga `CerrarInspeccionSaga` (simplificada por ADR-007) | ⏳ | Reacciona a `InspeccionFirmada_v1`. Si **no** hay `RequiereIntervencion` → emite `InspeccionCerradaSinOT_v1`. Si sí → no-op (espera comando humano `GenerarOT`). Ver §17 (ADR-007). |
| 3.24b | Saga `EjecutarOTSaga` (NUEVA por ADR-007) | ⏳ | Reacciona a `OTSolicitada_v1` → invoca paso 3.27 (POST MYE) vía outbox. En éxito emite `InspeccionCerrada_v1`; en fallo agotado emite `OTGeneracionFallida_v1`. Ver §17. |
| 3.25 | Saga: derivación Cerrada vs CerradaSinOT vs EsperandoAprobacionOT | ⏳ | Cerrada/CerradaSinOT siguen siendo eventos persistidos; `EsperandoAprobacionOT` es estado derivado en proyección §15.12.5. Ver §15.6 (histórico) + §17 (vigente). |
| 3.26 | Saga: apertura de seguimientos al firmar | ⏳ | Para hallazgos `RequiereSeguimiento`. Independiente del flujo OT (corre siempre al firmar). |
| 3.27 | Adapter MYE: POST /mye/ot-correctivas | ⏳ | Invocado desde **`EjecutarOTSaga`** (3.24b), no desde `CerrarInspeccionSaga`. Idempotency-Key=InspeccionId. Tres tests WireMock obligatorios (replay 200, 4xx sin retry, 5xx con backoff). Cuarto test si se adopta fallback `GET` (ver 4.10). Detalle en ADR-003 §13. |
| 3.27c | Adapter MYE: PUT /equipos/{id}/dictamen-vigente (NUEVO 2026-04-30) | ⏳ | Invocado desde **`SincronizarDictamenVigenteSaga`** (nueva, reactiva sobre `InspeccionFirmada_v1`). Corre en toda firma (con OT y sin OT). Idempotency-Key=InspeccionId. Tres tests WireMock: replay 200, 4xx sin retry (`DictamenVigenteSyncFallida_v1` candidato, NO bloquea cierre), 5xx con backoff. Origen: observación Sergio 2026-04-30 — ver §17 ADR-007 y M-W-1 en `06-contrato-apis-erp.md`. |
| 3.27d | Adapter MYE: POST /mye/ot-correctivas/{id}/adjuntos (NUEVO 2026-04-30) | ⏳ | Invocado desde **`EjecutarOTSaga` extendida** tras éxito de M-1 (paso 3.27). Multipart con PDF generado por `GenerarPdfInspeccionSaga` (paso 3.27e). Idempotency-Key=`{InspeccionId}-pdf`. Tests WireMock: éxito post-OT, race con PDF aún no generado → backoff + reintento, 4xx no retry → `AdjuntoPdfFallido_v1` (NO revierte OT), 5xx con backoff. Detalle en M-1b de `06-contrato-apis-erp.md`. |
| 3.27e | Servicio: generación de PDF con QuestPDF (NUEVO 2026-04-30) | ⏳ | Renderiza el PDF localmente con QuestPDF (`Sinco.Inspecciones.PdfRendering`). Layout definido en §17 ADR-007 sub-sección "Generación de PDF". Tests: snapshot del layout (regresión visual), edge cases (sin adjuntos, hallazgos eliminados, cierre sin OT). |
| 3.24c | Saga `SincronizarDictamenVigenteSaga` (NUEVA 2026-04-30) | ⏳ | Reacciona a `InspeccionFirmada_v1`. Invoca paso 3.27c vía outbox. Independiente del flujo OT: corre en toda firma. |
| 3.24d | Saga `GenerarPdfInspeccionSaga` (NUEVA 2026-04-30) | ⏳ | Reacciona a `InspeccionFirmada_v1`. Invoca paso 3.27e (renderizar PDF), sube a Azure Blob (`inspecciones-pdf` container), emite `PdfInspeccionGenerado_v1` con `BlobUri` + `Sha256`. Independiente del flujo OT — el PDF queda disponible aun si la inspección cierra `SinOT`. |
| 3.28 | Adapter Preop: POST /preop/novedades/{id}/verificar | ⏳ | Por cada novedad procesada |
| 3.29 | Adapter Preop: POST /preop/novedades/descartar (1 novedad por llamada en MVP) | ⏳ | Para comando individual `DescartarNovedadPreop` (decisión final 2026-04-30: motivo autogenerado, sin modal). El módulo siempre envía array de 1; el ERP soporta N por flexibilidad futura. Tests WireMock: éxito 1 novedad, 409 si ya procesada, replay con misma Idempotency-Key. Detalle en §15.9 "Descarte rápido inline" del modelo y P-6 de `06-contrato-apis-erp.md`. |
| 3.30 | Outbox + reintento exponencial | ⏳ | Wolverine built-in |
| 3.31 | Tests de integración con mocks de Sinco | ⏳ | |

### 3.E Sincronización de catálogos

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.32 | Sync inicial al primer login + sync delta on-app-open (sin cron nocturno) | ⏳ | ADR-004 (decisión canonical 2026-05-05 — sin cron). Bootstrap de la PWA dispara `GET /api/v1/catalogos/<X>` con `If-None-Match: "{etag-cliente}"` por catálogo, en paralelo. Response típico = `304 Not Modified`. Sin scheduler en backend. **Decisión 2026-04-30**: para clientes con > 10K SKUs (CONCRESCOL ~34K, PAVIMENTOS, CASS, REDES) seguir evaluando `?updatedSince=` para deltas más finos en M-16/I-2 — confirmar con David. **Decisión 2026-05-04**: alimenta `RutinaTecnicaLocal` desde **M-17**. **Decisión 2026-05-05**: alimenta también `RutinaMonitoreoLocal` desde **M-16** (monitoreo MVP). Detalle de volúmenes en `08-volumenes-clientes-erp.md` hallazgo 7. |
| 3.33 | Proyección local: `EquipoLocal`, `ParteLocal`, `RepuestoLocal`, `ProyectoLocal`, `RutinaTecnicaLocal`, `RutinaMonitoreoLocal`, `CausaFallaLocal`, `TipoFallaLocal` | ⏳ | 8 catálogos para MVP (decisión 2026-05-05: monitoreo entra al MVP, agrega `RutinaMonitoreoLocal`). Persistencia en **IndexedDB cliente** (object store `catalogos`, ver ADR-008 §9.16). `ProyectoLocal` se sincroniza desde `GET /api/v1/catalogos/obras` del ERP — adapter traduce. **`EquipoLocal`** trae el detalle vía **M-3b** (decisión 2026-05-04, refinada 2026-05-05) con `rutinaTecnicaId: int` (singular, asignación per-equipo) y `grupoMantenimientoId: int` (mecanismo de asignación rutinas-monitoreo por grupo). **`RutinaTecnicaLocal`** se popula vía M-17 on-app-open; resuelve client-side el id del equipo contra el catálogo. **`RutinaMonitoreoLocal`** se popula vía M-16 on-app-open; cliente filtra por `grupoMantenimientoId` para resolver rutinas del equipo (sin tabla intermedia en ERP). |
| 3.34 | Estrategia stale-while-revalidate (sin red al abrir → último cached) | ⏳ | Decisión 2026-05-05: si la app abre sin red, el bootstrap usa la cache local (banner discreto "modo offline"). Sync se reintenta cuando vuelva la red. Bloqueo por staleness extrema (>7 días sin sync) sigue aplicando — ver ADR-004 Punto 3 vigente |
| 3.35 | Health checks por catálogo | ⏳ | App Insights |

### 3.F APIs cloud (REST hacia el frontend)

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.36 | Endpoint `POST /inspecciones` (iniciar — `Tipo=Tecnica` implícito) | ⏳ | Body lleva: `equipoId`, `proyectoId`, `ubicacion` (GPS), `fechaReportada` (DateOnly, decisión 2026-04-30 cierre #2), `lecturaMedidorPrimario?` y `lecturaMedidorSecundario?` (decisión 2026-04-30 cierre #3). Valida invariantes I-I1, I-I2, I-I3 (§15.7). **I-I1**: una sola inspección abierta por equipo. Si ya hay activa, response retorna `200 OK` con `inspeccionId` existente y flag `redirected: true`. **I-I2**: equipo cuyo grupo no tiene rutina → 422 `GRUPO_SIN_RUTINA`. **I-I3**: `FechaReportada` fuera del rango `[hoy-30d, hoy]` → 422 `FECHA_REPORTADA_FUERA_DE_RANGO`. Tests obligatorios: (a) equipo libre con rutina → 200, (b) equipo con activa → 200 redirect, (c) concurrente → race resuelto por §15.12.6, (d) equipo sin rutina → 422, (e) `fechaReportada` futura → 422, (f) `fechaReportada` 31+ días atrás → 422, (g) `fechaReportada=hoy` → 200, (h) `fechaReportada=hoy-30d` (borde) → 200, (i) lecturas de medidores capturadas correctamente, (j) lecturas de medidores ausentes (equipo sin medidores) → 200. |
| 3.36b | Endpoint `POST /inspecciones/monitoreo` (NUEVO MVP — decisión 2026-05-05) | ⏳ | Body extiende 3.36 con `rutinaMonitoreoId: int` (selección del técnico). Despacha al mismo aggregate `Inspeccion` con `Tipo=Monitoreo`. Valida I-I1, I-I3 + nueva regla §12.11.5: `RutinaMonitoreoLocal[id].GrupoMantenimientoId == EquipoLocal[id].GrupoMantenimientoId` → 422 `RUTINA_FUERA_DE_GRUPO` si no coincide. I-I2 adaptado: equipo cuyo grupo no tiene rutinas-monitoreo activas → 422 `GRUPO_SIN_RUTINAS_MONITOREO`. Tests obligatorios: (a) éxito feliz, (b) rutina de otro grupo → 422, (c) grupo sin rutinas activas → 422, (d) `rutinaMonitoreoId` inexistente → 404, (e) inicio concurrente con técnica del mismo equipo → solo una gana por I-I1, (f) snapshot de items se serializa completo en `InspeccionIniciada_v1.ItemsSnapshot`. |
| 3.36c | Endpoint `POST /inspecciones/{id}/items/{itemId}/medicion` (NUEVO MVP) | ⏳ | Body `{ valorMedido, observacion? }` para items numéricos. Handler invoca `RegistrarMedicion` (3.16f). Valida que `Tipo` del aggregate sea `Monitoreo`, `itemId ∈ ItemsSnapshot`, item es numérico (no cualitativo). Tests: dentro de rango → 200 (1 evento), fuera de rango → 200 (2 eventos atómicos: medición + hallazgo), item cualitativo → 422, item ya medido → 409, item omitido → 422. |
| 3.36d | Endpoint `POST /inspecciones/{id}/items/{itemId}/evaluacion` (NUEVO MVP) | ⏳ | Body `{ calificacion: "Bueno"|"Regular"|"Malo", observacion? }`. Handler invoca `RegistrarEvaluacionCualitativa` (3.16g). Valida `Tipo=Monitoreo`, item es cualitativo. Tests: Bueno/Regular → 1 evento, Malo → 2 eventos atómicos, item numérico → 422, item ya evaluado → 409. |
| 3.36e | Endpoint `POST /inspecciones/{id}/items/{itemId}/omitir` (NUEVO MVP) | ⏳ | Body `{ motivo }` (mínimo 10 chars). Handler invoca `OmitirItemMonitoreo` (3.16h). No dispara hallazgo. Tests: éxito feliz, item ya medido/evaluado → 422, motivo vacío → 400. |
| 3.37 | Endpoint `POST /inspecciones/{id}/hallazgos` | ⏳ | |
| 3.38 | Endpoint `PATCH /inspecciones/{id}/hallazgos/{hid}` | ⏳ | |
| 3.39 | Endpoint `DELETE /inspecciones/{id}/hallazgos/{hid}` | ⏳ | Soft delete |
| 3.40 | Endpoints repuestos (3) | ⏳ | |
| 3.41 | Endpoints adjuntos: solicitar SAS + confirmar upload | ⏳ | Pattern blob upload |
| 3.42 | Endpoint `POST /inspecciones/{id}/firmar` | ⏳ | Comando consolidado. **Por ADR-007**: la firma ya NO dispara POST a MYE. Solo emite `InspeccionFirmada_v1` (+ `DiagnosticoEmitido_v1` y `DictamenEstablecido_v1` atómicos). Si no hay `RequiereIntervencion`, la saga 3.24 cierra como `CerradaSinOT`. Si sí hay, la inspección queda en estado derivado `EsperandoAprobacionOT` esperando comando humano. **Nuevo V-F8 (decisión 2026-05-04 Jaime)**: dictamen `PuedeOperar` ("Apto") no se permite si hay ≥1 hallazgo no eliminado con `AccionRequerida ∈ {RequiereSeguimiento, RequiereIntervencion}` — solo `ConRestriccion` o `NoPuedeOperar`. Tests obligatorios: (a) firmar `PuedeOperar` + RequiereSeguimiento → 422 V-F8, (b) firmar `PuedeOperar` + RequiereIntervencion → 422 V-F8, (c) firmar `PuedeOperar` + solo NoRequiereIntervencion → 200, (d) firmar `ConRestriccion` con cualquier combinación → 200, (e) firmar `NoPuedeOperar` con cualquier combinación → 200. Por V-F8 + cierre automático sin OT, el caso `PuedeOperar` siempre cierra inmediato como `InspeccionCerradaSinOT_v1`. |
| 3.42b | Endpoint `POST /inspecciones/{id}/generar-ot` (NUEVO ADR-007) | ⏳ | Capability gate: requiere `generar-ot`. Body lleva `responsable: "Proyecto" \| "DepartamentoEquipos"` (enum cerrado, decisión 2026-04-30 §17). Comando `GenerarOT` valida I-F4 (§15.7) y emite `OTSolicitada_v1` con el `Responsable`. Saga `EjecutarOTSaga` (3.24b) propaga el campo al payload de MYE. Tests obligatorios: (a) usuario sin capability → 403, (b) inspección sin `RequiereIntervencion` → 422, (c) re-emisión sobre stream con `OTSolicitada` previo → 422, (d) `responsable` fuera del enum → 400, (e) éxito feliz por cada valor del enum, (f) re-emisión sobre stream con `OTRechazada` previo → 422 (no se solicita lo que se rechazó). |
| 3.42c | Endpoint `POST /inspecciones/{id}/rechazar-ot` (NUEVO 2026-04-30) | ⏳ | Capability gate: requiere `generar-ot` (misma que aprobar — quien aprueba puede rechazar). Body `{ motivo }`. Comando `RechazarGenerarOT` valida I-F6 (§15.7) y emite atómicamente `GeneracionOTRechazada_v1` + `InspeccionCerradaSinOT_v1` con `MotivoCierreSinOT=RechazadaPorAprobador`. Tests obligatorios: (a) usuario sin capability → 403, (b) inspección sin `RequiereIntervencion` → 422, (c) `OTSolicitada` previo → 422 (fuera de alcance MVP), (d) doble rechazo → 422, (e) motivo vacío → 400, (f) motivo <10 chars → 400, (g) éxito feliz: dispara `SincronizarDictamenVigenteSaga` y libera el equipo (proyección `InspeccionAbiertaPorEquipoView` borra fila al recibir `InspeccionCerradaSinOT_v1`). |
| 3.43 | Endpoint `POST /inspecciones/{id}/cancelar` | ⏳ | |
| 3.44 | Endpoint `GET /inspecciones?equipo=&estado=` (bandeja) | ⏳ | Sirve `BandejaTecnicoView` (§15.12.3). Autorización: capability `ejecutar-inspeccion`; filtros `equipo` y `estado` opcionales. |
| 3.45 | Endpoint `GET /inspecciones/{id}` (detalle) | ⏳ | Sirve `DetalleInspeccionView` (§15.12.1). **Debe** exponer: hallazgos eliminados con `MotivoEliminacion`, novedades preop descartadas con `MotivoDescarte` + `DescartadaPor` + timestamp, trazabilidad de hallazgos escalados (`SeguimientoOrigenId`). **Por ADR-007**: además expone `EstadoOT` derivado (`NoAplica` / `EsperandoAprobacion` / `EnProceso` / `Generada` / `Fallida`) y las capabilities del usuario consultante para que el frontend muestre/oculte botón "Generar OT". Autorización: capability `ejecutar-inspeccion` para contribuyente del stream o capability `auditar-inspecciones` para acceso amplio. |
| 3.45b | Endpoint `GET /inspecciones/pendientes-ot?proyecto=&firmada-desde=&firmada-hasta=` (NUEVO ADR-007) | ⏳ | Sirve `BandejaInspeccionesPendientesOTView` (§15.12.5) — cola de aprobación. Audiencia: capability `generar-ot`. Incluye también filas con `EstadoOT=EnProceso` (post-OTSolicitada) y `EstadoOT=Fallida` (post-OTGeneracionFallida). Proyección Marten dedicada — instanciarla con tests de derivación de estado. |
| 3.46 | Endpoint `GET /equipos/{id}/seguimientos?estado=Abierto` | ⏳ | Sirve `SeguimientosAbiertosPorEquipoView` (§15.12.4). Audiencia: `ejecutar-inspeccion` (banner Pantalla 1 / lista Pantalla 2 del flujo seguimientos) + `auditar-inspecciones`. Filtro `?estado=` permite recuperar histórico (Resuelto/Escalado). |
| 3.47 | Endpoint `POST /seguimientos/{id}/resolver` | ⏳ | |
| 3.48 | Endpoint `POST /seguimientos/{id}/escalar` | ⏳ | |
| 3.49 | Endpoint `POST /inspecciones/{id}/novedades-preop/{novedadId}/descartar` (individual) | ⏳ | Body `{ descartadoPor }`. Comando individual `DescartarNovedadPreop` emite UN evento `NovedadPreopDescartada_v1` con motivo autogenerado por el handler (`"Cerrado por {usuario} el {fecha} UTC desde Inspecciones"`). Decisión final 2026-04-30: sin modal, sin motivo manual, sin bulk con motivo único. Tests obligatorios: éxito feliz, novedad ya procesada → 409, novedad no pertenece a la inspección → 404, usuario sin capability `ejecutar-inspeccion` → 403. |
| 3.50 | Documentación OpenAPI/Swagger | ⏳ | |

### 3.G SignalR Hub

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.51 | Implementar `InspeccionesHub` | ⏳ | ADR-005 |
| 3.52 | Autenticación del hub via JWT | ⏳ | Validar contra TecnicosContribuyentes |
| 3.53 | Proyector lateral: emite `OTGenerada`, `CerradaSinOT`, `OTGeneracionFallida` | ⏳ | |
| 3.54 | Fallback HTTP polling (5s) | ⏳ | Si SignalR no disponible |

### 3.H Auditoría de inspecciones (read model + endpoints)

> **Audiencia**: usuarios con capability `auditar-inspecciones`. El módulo no asume perfiles ERP fijos — el host PWA mapea su catálogo de perfiles a esta capability (ver paso 2.5). Distinto de la bandeja "Mis inspecciones" del usuario contribuyente (paso 3.44). Detalle de la proyección en `01-modelo-dominio.md` §15.12.2.

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.55 | Proyección Marten `AuditoriaInspeccionesView` (MVP shape mínimo, decisión 2026-05-04) | ⏳ | **Shape final §15.12.2.** Consume eventos del aggregate `InspeccionTecnica` (16 eventos MVP) + joins con `ProyectoLocal` y `EquipoLocal`. Materializa: lifecycle, dictamen, `EstadoOT` derivado, conteos de hallazgos por `AccionRequerida`, conteos de novedades preop verificadas/descartadas, `RepuestosEstimadosCount`, `HallazgosEliminadosCount`, indicador `DecisionContradiceReporteOperador` con regla (a) sola (≥1 `NovedadPreopDescartada_v1`). **NO** consume `SeguimientoHallazgo` en MVP (decisión 2026-05-04 punto 2 — diferido a Fase 10). **NO** materializa la regla (b) del indicador (diferida — requiere `severidad` consistente en preop). Tests obligatorios: rebuild desde stream con todas las combinaciones de eventos + verificación de derivación correcta de `EstadoOT` por cada motivo de cierre. |
| 3.56 | Endpoint `GET /auditoria/inspecciones?proyecto=&desde=&hasta=&autor=&equipo=&dictamen=&estadoOT=&contradiceReporte=` | ⏳ | Bandeja filtrable de inspecciones cerradas. Paginada (§1.3 contrato). Autorización: capability `auditar-inspecciones` (matriz en paso 2.5). KPIs del header agregados sobre el rango filtrado: total, con OT / sin OT / rechazadas / fallidas, contradicen reporte, tiempo medio, tasa de descarte preop. Mock UX en `02l-mock-auditoria-inspecciones.html` (decisión 2026-05-04). Tests obligatorios: cada filtro individualmente + combinaciones, paginación, sort por fecha de firma descendente por defecto. |
| 3.57 | Endpoint `GET /auditoria/inspecciones/{id}` | ⏳ | Atajo al `DetalleInspeccionView` (paso 3.45) con autorización de auditoría. Misma proyección §15.12.1, control de acceso por capability. |
| 3.58 | Tests de autorización (usuario sin capability `auditar-inspecciones` recibe 403) | ⏳ | Cross-cutting con paso 2.5. Tests adicionales: usuario con capability ve solo proyectos de su scope, no todas las obras del cliente. |

> **Diferido a Fase 10** (post-MVP):
> - **Regla (b)** de `DecisionContradiceReporteOperador` ("≥1 hallazgo donde el firmante bajó la urgencia respecto al reporte original"). Requiere `severidad` consistente en preop + mapeo `severidadPreop ↔ AccionRequerida`. Aditivo — no rompe shape del read model.
> - **`SeguimientosAbiertosCount` por fila** — requiere consumir stream `SeguimientoHallazgo`.
> - **Métricas agregadas** por usuario firmante / por operador que reportó (tasa de descarte, etc.).
> - **Workflow de notificación** al operador cuando su novedad es descartada.
>
> Ver `FOLLOWUPS.md` cuando aparezca disparador (típicamente piloto Fase 9 con feedback de supervisores).

---

## Fase 4 — APIs en Sinco (cross-team, paralelo)

> **Fuente canónica del contrato**: [`Inspecciones/docs/06-contrato-apis-erp.md`](Inspecciones/docs/06-contrato-apis-erp.md). Las tablas debajo son mapeo de pasos del roadmap a endpoints; el detalle de request/response/idempotencia/shape vive en el archivo 06.

> **Riesgo de programa**: 25 endpoints reconciliados (16 obligatorios MVP + 1 condicional + 8 diferidos al post-MVP) en 3 módulos de Sinco con 3 equipos distintos (Preop, MYE núcleo, Inventario). Equipo Seguridad/IT eliminado del cross-team (decisión 2026-05-05 — toda la identidad viene del host PWA, sin endpoints U-* propios). Coordinación cross-team sigue siendo bloqueante.

### 4.A Equipo del Preoperacional

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.1 | `GET /preop/novedades?equipo=&estado=pendiente` | 🚧 | Lista viva, no snapshot |
| 4.2 | `POST /preop/novedades/{id}/verificar` | 🚧 | Body con `AccionRequerida` (RequiereIntervencion o RequiereSeguimiento) + `NovedadTecnica` (diagnóstico) |
| 4.3 | `POST /preop/novedades/descartar` (bulk-capable, 1..N en JSON — decisión 2026-04-30) | 🚧 | Body con `inspeccionId` + `novedadIds: []` (array de 1 en MVP) + `motivo` (autogenerado del lado módulo) + `descartadaPor`. Capacidad bulk preservada por flexibilidad futura (sagas de limpieza, batch admin). Detalle en P-6 de `06-contrato-apis-erp.md`. **🚧 Confirmar con David** path final, idempotency-key y máximo de N por request. |
| 4.4 | DDL del preoperacional compartido | 🚧 | Bloqueante para 0.18 |

### 4.B Equipo MYE núcleo

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.5 | `GET /equipos?q=&page=&size=` — lista liviana M-3 (autocomplete) | 🚧 | El ERP filtra por obras del usuario via JWT. Sin `?obra=` explícito (decisión 2026-04-30 + refinada 2026-05-04). NO incluye partes ni rutinas (esas viven en M-3b). |
| 4.6 | `GET /equipos/{equipoCodigo}` — detalle M-3b (CONSOLIDADO 2026-05-04, refinado 2026-05-05) | 🚧 | **Crítico MVP refactorizado**: incluye `partes[]` (absorbe el viejo M-4) + `rutinaTecnicaId: int` (singular, asignación per-equipo) + `grupoMantenimientoId: int` + `grupoMantenimiento: string` (mecanismo de asignación rutinas-monitoreo por grupo, Fase 2 — sin `rutinasMonitoreoIds[]`) + `obraId: int` con `obraCodigo: string`. Una sola llamada al seleccionar equipo. Detalle en M-3b de `06-contrato-apis-erp.md`. |
| 4.7 | ~~`GET /equipos/{id}/partes`~~ — M-4 ELIMINADO | ❌ | Absorbido por M-3b (decisión 2026-05-04). El árbol de partes viaja embebido en el detalle del equipo. Slices que apuntaban a M-4 deben migrar a M-3b. |
| 4.8 | ~~`GET /partes/{id}`~~ — DIFERIDO | ⏸ | Las partes viajan embebidas en M-3b. No requiere endpoint de detalle separado en MVP. Si emerge necesidad puntual (ej. catálogo cross-equipo), se reactiva. |
| 4.9 | `POST /mye/ot-correctivas` (crear OT) | 🚧 | Cuerpo: hallazgos, repuestos, dictamen, técnico, `responsableCosto` (decisión 2026-04-30, enum `Proyecto`/`DepartamentoEquipos`), `solicitadaPor` (ADR-007). **Idempotencia real obligatoria** sobre `Idempotency-Key=InspeccionId`: misma key → mismo `200 OK` con mismo `OTCorrectivaIdSinco`, persistente, ventana ≥30 días. Detalle del contrato en ADR-003 §13. |
| 4.9b | `PUT /equipos/{id}/dictamen-vigente` (NUEVO 2026-04-30) | 🚧 | Endpoint MYE para sincronizar dictamen vigente del equipo en cada firma. Cuerpo: `dictamen` + `inspeccionOrigenId` + `firmadaEn` + `tecnicoFirmante`. **Pendiente coordinación cross-team** — confirmar con David si campo y endpoint ya existen, o construir. Detalle en M-W-1 de `06-contrato-apis-erp.md` y pregunta 3 de `07-preguntas-destrabar-followups.md`. |
| 4.9c | `POST /mye/ot-correctivas/{id}/adjuntos` (NUEVO 2026-04-30) | 🚧 | Endpoint MYE para adjuntar PDF de inspección a OT (multipart). **Pendiente coordinación cross-team** — confirmar con David existencia, tamaño máximo, tipos admitidos, comportamiento ante replay. Detalle en M-1b de `06-contrato-apis-erp.md` y pregunta 5 de `07-preguntas-destrabar-followups.md`. |
| 4.10 | `GET /mye/ot-correctivas?inspeccionId={id}` (fallback) | 🚧 | **Condicionalmente obligatorio**: deja de ser opcional si el equipo MYE no puede entregar el contrato de idempotencia real de 4.9. Habilita el patrón "consulta-antes-de-crear" (ADR-003 §13). Si 4.9 cumple, 4.10 queda como deuda técnica diferida. |
| 4.11 | `GET /catalogos/causas-falla` | 🚧 | ADR-004 |
| 4.12 | `GET /catalogos/tipos-falla` | 🚧 | ADR-004 |
| 4.13 | `GET /catalogos/grupos` | ⏸ | Diferido (decisión 2026-04-30) — M-3b trae `grupo` denormalizado en cada equipo. |
| 4.14 | `GET /catalogos/obras` (catálogo de proyectos — el ERP usa "obras" en URL; adapter mapea a `ProyectoLocal`) | 🚧 | |
| 4.14b | `GET /catalogos/rutinas` — M-17 (NUEVO 2026-05-04, crítico MVP) | 🚧 | Sync on-app-open de definiciones de rutinas técnicas (decisión 2026-05-05 ADR-004 canonical — sin cron). Alimenta `RutinaTecnicaLocal` en IndexedDB cliente. Filtra `tipo=Tecnica` server-side (el módulo solo consume rutinas técnicas). Cierra gap detectado en revisión por flujos: el modelo asumía sync que el contrato no tenía. Detalle en M-17 de `06-contrato-apis-erp.md`. |
| 4.14c | `GET /catalogos/rutinas-monitoreo` — M-16 (NUEVO crítico MVP — decisión 2026-05-05) | 🚧 | Sync on-app-open de rutinas de monitoreo (decisión 2026-05-05 ADR-004 canonical — sin cron). Cada rutina trae `grupoMantenimientoId` (decisión 2026-05-05 — mecanismo de asignación por grupo, sin tabla intermedia equipo↔rutina en ERP). Cliente filtra el catálogo local por grupo del equipo. Detalle en M-16 de `06-contrato-apis-erp.md`. **Pendiente cross-team con David** — pregunta 6 de `07-preguntas-destrabar-followups.md`. |

### 4.C Equipo Inventario

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.15 | `GET /repuestos?parte=&q=` (búsqueda con compatibilidad) | 🚧 | Critical UX |
| 4.16 | `GET /repuestos/{id}` (detalle) | 🚧 | |
| 4.17 | `GET /catalogos/insumos` | 🚧 | Sync nocturno |

### 4.D Equipo de RRHH / Identidad — ❌ NO APLICA (decisión 2026-05-05)

> **Decisión 2026-05-05 (Jaime):** el módulo **no consume endpoints de usuarios** — toda la identidad viene del host PWA vía JWT. Pasos 4.18 y 4.19 (endpoints U-1, U-2 del contrato) eliminados. Sin sync de usuarios, sin catálogo local de usuarios, sin coordinación cross-team con el equipo de seguridad/IT Sinco. Ver Fase 2 actualizada.

---

## Fase 5 — Frontend (PWA React + MUI)

> **Ventaja**: PWA Sinco MYE existe y mismo equipo. Heredamos service worker, routing, auth, etc.

### 5.A Setup en PWA existente

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.1 | Crear módulo "Inspecciones" en PWA | ⏳ | Punto de entrada en home |
| 5.2 | Configurar router para flujo del módulo | ⏳ | |
| 5.3 | Cliente HTTP con manejo de auth + reintentos | ⏳ | |
| 5.4 | Cliente SignalR (`@microsoft/signalr`) | ⏳ | withAutomaticReconnect() |
| 5.5 | Estado global del módulo (Redux/Zustand) | ⏳ | |

### 5.B Pantallas del flujo principal

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.6 | Home con módulo "Inspecciones" destacado | ⏳ | Pantalla 1 wireframe |
| 5.7 | Bandeja "Mis inspecciones" (en curso + cerradas) | ⏳ | Pantalla 2 |
| 5.8 | Selector de equipo para iniciar inspección | ⏳ | Pantalla 2b — con badge "novedades preop" |
| 5.9 | Pantalla principal de inspección con 3 botones | ⏳ | Pantalla 3 (rev. 3) |
| 5.10 | Wizard hallazgo paso 1 (con/sin intervención) | ⏳ | Pantalla 4 |
| 5.11 | Wizard hallazgo paso 1 variante (sin paso 2) | ⏳ | Pantalla 4b |
| 5.12 | Wizard hallazgo paso 2 (análisis técnico) | ⏳ | Pantalla 5 |
| 5.13 | Pantalla de cierre/firma con validaciones V-F* | ⏳ | Pantalla 6 |
| 5.14 | Pantalla 7a: esperando OT (spinner SignalR) | ⏳ | |
| 5.15 | Pantalla 7b: OT generada (push SignalR) | ⏳ | |
| 5.16 | Pantalla 7c: error MYE | ⏳ | |

### 5.B' Pantallas del flujo monitoreo (NUEVO MVP — decisión 2026-05-05)

> Wireframes en `Inspecciones/docs/02e-wireframes-monitoreo.html` (Daniel). Validación con Sergio pendiente — paso 8.4.

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.16a | Selector de tipo de inspección al iniciar (Técnica vs Monitoreo) | ⏳ | Si el grupo del equipo no tiene rutinas-monitoreo activas, ocultar opción Monitoreo |
| 5.16b | Selector de rutina de monitoreo (cards de las rutinas activas del grupo) | ⏳ | Filtra `RutinaMonitoreoLocal` por `grupoMantenimientoId` del equipo. Cardinalidad típica 2-3 rutinas |
| 5.16c | Pantalla principal monitoreo: lista de items con captura inline | ⏳ | Items numéricos (input + unidad) y cualitativos (radios Bueno/Regular/Malo). Botón cámara por item. Botón "omitir" con motivo. Indicador visual de fuera de rango / `Malo` con badge "se abrirá hallazgo automático" |
| 5.16d | Wizard de hallazgo automático auto-generado al medir fuera de rango / Malo | ⏳ | Pre-rellena `Origen=Monitoreo`, `MedicionOrigenId=ItemId`, `ParteEquipoId` heredado, `NovedadTecnica` autogenerada (editable). Técnico solo confirma o ajusta |
| 5.16e | Pantalla de cierre/firma monitoreo | ⏳ | Reusa 5.13 con validaciones extra: items omitidos sin justificar bloquean firma. Dictamen libre (V-F4 + V-F8 aplican). Resumen muestra items dentro/fuera/omitidos |

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.17 | Pantalla "Importar" con tabs Preoperacional/Seguimiento (decisión 2026-04-30) | ⏳ | Referencia visual: image11/12 del mock de Daniel. Cada item con 2 acciones: 📥 Importar (botón principal, abre wizard) + 🗑 Descartar (icono, motivo autogenerado). Pestaña Seguimiento: solo 📥 Importar (sin descartar — escala el seguimiento via comando `EscalarSeguimiento`). |
| 5.18 | Modal Descartar con motivo obligatorio | ⏳ | |
| 5.19 | Modal Seguimiento con motivo | ⏳ | |
| 5.20 | Wizard verificar paso 1 + paso 2 | ⏳ | Referencia visual: image7 (paso 1 con radios `AccionRequerida`) e image9 (paso 2 análisis técnico) del mock de Daniel. Image13 muestra el wizard cuando viene de "Importar" (paso 1 heredado del preop, técnico arranca en paso 2). |

### 5.D Pantallas del flujo seguimientos

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.21 | Lista de seguimientos del equipo | ⏳ | `02d` pantalla 2 |
| 5.22 | Modal "Sin intervención" con motivo | ⏳ | |
| 5.23 | Toast inline "Continúa en seguimiento" | ⏳ | No-op silencioso |
| 5.24 | Wizard escalar paso 1 + paso 2 | ⏳ | Pantallas 5+6 de `02d` |

### 5.E Componentes transversales

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.25 | Componente captura de firma manuscrita (canvas) | ⏳ | PNG → blob |
| 5.26 | Componente captura GPS | ⏳ | UbicacionInicio + UbicacionFirma |
| 5.27 | Componente compresor de fotos (1920×1920, JPEG ~75%) | ⏳ | |
| 5.28 | Componente de upload directo a blob (SAS) | ⏳ | |
| 5.29 | Banners de novedades preop + seguimientos | ⏳ | Diseño consistente |
| 5.30 | Badges de antigüedad para seguimientos (azul/naranja/rojo) | ⏳ | |
| 5.31 | Tests E2E con Playwright | ⏳ | Flujos críticos |

---

## Fase 6 — Notificaciones (email + SLA seguimientos)

| # | Paso | Estado | Notas |
|---|---|---|---|
| 6.1 | Servicio de email (SMTP corporativo o SendGrid) | ⏳ | |
| 6.2 | Plantilla de notificación: OT fallida → destinatarios con capability `recibir-alertas-ot-fallida` | ⏳ |
| 6.2b | Plantilla de notificación: OT rechazada (NUEVO 2026-04-30) → destinatarios con capability `recibir-alertas-ot-rechazada`. Asunto debe incluir equipo + proyecto; cuerpo debe incluir motivo del rechazo, técnico firmante, aprobador que rechazó, link al detalle de la inspección. Quien rechaza NO se notifica a sí mismo. | ⏳ | |
| 6.3 | Plantilla de notificación: seguimiento +90 días → destinatarios con capability `recibir-alertas-sla` | ⏳ | Diario hasta cierre |
| 6.4 | Endpoint admin: cola de OT fallidas | ⏳ | |
| 6.5 | Configuración por proyecto/equipo de destinatarios de alertas (data, no código) | ⏳ | Lista de usernames + capability requerida (`recibir-alertas-sla`, `recibir-alertas-ot-fallida`). Sincronizada desde catálogo MYE o configurada localmente — decisión pendiente. |

---

## Fase 7 — Calidad y testing

| # | Paso | Estado | Notas |
|---|---|---|---|
| 7.1 | Tests unitarios de aggregate Inspeccion | ⏳ | ≥80% cobertura |
| 7.2 | Tests unitarios de aggregate Seguimiento | ⏳ | |
| 7.3 | Tests de saga de cierre | ⏳ | Casos: con OT, sin OT, falla MYE |
| 7.4 | Tests de integración con mocks de Sinco | ⏳ | |
| 7.5 | Tests E2E del frontend | ⏳ | Flujos críticos |
| 7.6 | Tests de carga (volumen de inspecciones) | ⏳ | |
| 7.7 | Tests de seguridad (OWASP top 10, JWT, SAS) | ⏳ | |
| 7.8 | Smoke tests post-deployment | ⏳ | |

---

## Fase 8 — Validación con consultor mecánico

| # | Paso | Estado | Notas |
|---|---|---|---|
| 8.1 | Consultor lee `04-brief-consultor-producto.md` | ⏳ | ~30 min |
| 8.2 | Sesión de validación 2h | ⏳ | 5 áreas críticas priorizadas |
| 8.3 | Aplicar feedback al modelo / wireframes | ⏳ | Iterativo |
| 8.4 | Validar con consultor el alcance de la rutina técnica única por grupo + flujo de monitoreo MVP | ⏳ | El modelo vigente (§12.10/§12.11) define una rutina técnica por grupo de mantenimiento. Sergio confirma si la operación real tolera no subdividir motor/hidráulica o si emerge la necesidad de "contexto de inspección" como cambio aditivo. **Crítico 2026-05-05** — al haberse incluido monitoreo al MVP, Sergio debe validar específicamente: (a) UX de captura de items numéricos vs cualitativos (`02e-wireframes-monitoreo.html`), (b) regla de hallazgo automático solo en `Malo` (no `Regular`), (c) caso de uso real para "Omitir item" con motivo, (d) si "rutinas por grupo" (decisión 2026-05-05) refleja la operación real o si emerge necesidad de overrides per-equipo. |

---

## Fase 9 — Piloto y producción

| # | Paso | Estado | Notas |
|---|---|---|---|
| 9.1 | Seleccionar 1-2 proyectos piloto | ⏳ | Con buena conectividad para mitigar restricción online-only. **Decisión 2026-04-30 (análisis volúmenes):** el piloto debe ser cliente que **use Preoperacional con datos** para ejercitar el flujo verificar/descartar/seguimiento. De los 27 clientes del ERP, solo 5 cumplen: EXPLANAN (10712 preop), FUNDACIONES Y PILOTAJES (4123), JMV (3637), SCHRADER CAMARGO (82), DEMO (test). Recomendados: **EXPLANAN** o **FUNDACIONES Y PILOTAJES** — los que más volumen tienen y validan el bulk de descarte de novedades repetidas. Detalle en `08-volumenes-clientes-erp.md` hallazgo 1. |
| 9.2 | Capacitación a técnicos del piloto | ⏳ | Material breve + sesión presencial |
| 9.3 | Despliegue a ambiente staging | ⏳ | |
| 9.4 | UAT con 3-5 técnicos | ⏳ | Feedback iterativo |
| 9.5 | Despliegue a producción | ⏳ | Bandera por proyecto |
| 9.6 | Monitoreo cercano semana 1 | ⏳ | App Insights + standup diario |
| 9.7 | Retrospectiva + plan de rollout extendido | ⏳ | |

---

## Fase 10 — Post-MVP (diferido / aditivo)

| # | Paso | Estado | Notas |
|---|---|---|---|
| 10.1 | Mediciones (`MedicionRegistrada_v1`) | ✅ | **Movido al MVP el 2026-05-05** — incluido como parte de §3.B' (extensión Monitoreo). Ver pasos 3.16b + 3.36c |
| 10.2 | Programación previa de inspección (estado `Programada`) | ⏸ | Aditivo |
| 10.3 | Modo offline (PWA + IndexedDB + Background Sync) | ⏸ | Estimaciones ya hechas |
| 10.4 | Tipo de inspección "Monitoreo" | ✅ | **Movido al MVP el 2026-05-05 (decisión Jaime).** Implementación distribuida en §3.B' (aggregate), §3.E (`RutinaMonitoreoLocal`), §3.F (endpoints `POST /inspecciones/monitoreo`, item-medicion/evaluacion/omitir), §4.B (`M-16` 🚧), §5.B' (pantallas — wireframes en `02e-wireframes-monitoreo.html`). Detalle del modelo en §12.11.5. Asignación equipo↔rutinas-monitoreo derivada por grupo (decisión 2026-05-05) — sin tabla intermedia en ERP |
| 10.5 | ~~Comando bulk para descarte de novedades duplicadas~~ | ❌ | **Descartado el 2026-04-30** tras revisión del mock del diseño. La observación de Sergio se atiende con descarte rápido individual (motivo autogenerado, sin modal) — un tap por novedad, sin selección múltiple. Modelado en §15.9 "Descarte rápido inline". El contrato del ERP P-6 preserva capacidad bulk para flexibilidad futura. |
| 10.6 | Evento `SeguimientoRevisadoSinCambio_v1` | ⏸ | Si emerge necesidad de reportería |
| 10.7 | Botón admin "refrescar catálogo ahora" | ⏸ | ADR-004 |
| 10.8 | Reportes y KPIs avanzados | ⏸ | Post-piloto |
| 10.9 | Migración a cumplimiento pleno EDA Sinco (bus corporativo) | ⏸ | Cuando aparezca segundo consumidor |
| 10.10 | Co-firma operador / supervisor en inspección | ⏸ | Si emerge requerimiento |
| 10.11 | Biometría / 2FA al firmar | ⏸ | Solo si compliance lo exige |
| 10.12 | Video como tipo de adjunto | ⏸ | Hoy solo imagen + PDF |

---

## Riesgos clave

| # | Riesgo | Mitigación |
|---|---|---|
| R1 | Coordinación cross-team Sinco para 17 endpoints activos (16 obligatorios MVP + 1 condicional M-2) en 3 módulos diferentes; 8 diferidos al post-MVP. **Refinado 2026-05-04**: M-3b consolidado (absorbe M-4) + M-17 nuevo (crítico). **Refinado 2026-05-05**: (a) M-16 promovido de Fase 2 a MVP por inclusión de monitoreo en MVP — entra a urgent cross-team con David. (b) U-1/U-2 eliminados — sin coordinación con Seguridad/IT, identidad 100% del host PWA | SOW interno + escalación a CTO si bloqueos persisten |
| R2 | Primer Azure de Sinco — landing zone "lite" como deuda técnica | ADR-001 documenta divergencias EDA, plan de migración aditivo |
| R3 | DDL preop bloqueado del lado del equipo del preop | Workstream paralelo, mock del DTO mientras tanto |
| R4 | Conectividad VPN inestable | Stale-while-revalidate en catálogos (ADR-004) + degradación graceful |
| R5 | UX inadecuada para técnico de campo (audiencia nueva) | Validación con consultor mecánico (Fase 8) antes del piloto |
| R6 | Restricción online-only en proyectos remotos | Selección cuidadosa de proyectos piloto + documentación clara |
| R7 | Catálogos cambian IDs en Sinco rompiendo audit histórico | Reglas operativas vinculantes (ADR-004): IDs inmutables, descontinuar = flag activo=false |

---

## Métricas de éxito MVP

- ≥10 inspecciones técnicas completadas en piloto
- **≥10 inspecciones de monitoreo completadas en piloto (NUEVO MVP — decisión 2026-05-05)**
- ≥80% de hallazgos con intervención generan OT correctamente en MYE
- 0 OT generadas con BOM inválido (rechazo MYE)
- Tiempo medio del flujo del técnico: ≤8 min para inspección técnica estándar; ≤6 min para inspección de monitoreo (rutina con 8-12 items)
- ≤5% de inspecciones quedan en `CierrePendienteOT` por más de 24h
- 100% de hallazgos auto-generados por monitoreo (`Origen=Monitoreo`) abren `SeguimientoHallazgo` correctamente al firmar
- Feedback de técnicos: ≥4/5 en facilidad de uso (técnica + monitoreo agregado)

---

## Resumen ejecutivo

```
Fase 0 (Diseño):              ███████████████████░  98% ✅ (2 pasos bloqueados por externos: 0.13 + 0.18)
Fase 1 (Cloud foundations):   ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 2 (Auth):                ░░░░░░░░░░░░░░░░░░░░   0% ⏳ (bloqueado por 2.1)
Fase 3 (Backend):             ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 4 (APIs Sinco):          ░░░░░░░░░░░░░░░░░░░░   0% 🚧 (cross-team)
Fase 5 (Frontend):            ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 6 (Notificaciones):      ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 7 (Calidad):             ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 8 (Validación producto): ████████████████████ 100% ✅ (cerrada 2026-04-30 con Sergio)
Fase 9 (Piloto):              ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 10 (Post-MVP):           — diferido —
```

**Próximos 3 hitos críticos:**
1. **Arrancar Fase 3 — primer slice de backend core** (TDD multi-agente — pendiente approval del usuario para iniciar). **Decisión 2026-05-05**: MVP ahora incluye monitoreo (§3.B'); coordinar orden de slices entre técnica y monitoreo
2. **Fase 1 — Arrancar fundaciones Azure en paralelo** (bloqueante de Fase 2-3)
3. **Cross-team con David: confirmar M-17 + M-3b + M-16 + idempotencia real M-1** (desbloquea Fase 4 — preguntas redactadas en `07-preguntas-destrabar-followups.md`. M-16 ahora urgente por inclusión de monitoreo en MVP — decisión 2026-05-05)

**Pasos 0.13 (ADR-002) y 0.18 (DDL preop) quedan abiertos en Fase 0** porque dependen de externos (host PWA / equipo del preop) — no se cierran solos. El 2% restante de Fase 0 los refleja.
