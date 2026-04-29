# Roadmap — Módulo de Inspecciones Técnicas

**Última actualización:** 2026-04-28
**Estado general:** Diseño completado · Pendiente arranque de implementación
**Documentos clave:** `00-investigacion-mercado.md`, `01-modelo-dominio.md` (§15 fuente de verdad), `02*` wireframes, `03-sow-consultor.md`, `04-brief-consultor-producto.md`

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
| 0.7 | Wireframes flujo principal del técnico | ✅ | `02-wireframes-mobile.html` (rev. 3) |
| 0.8 | Wireframes flujo novedades preop (variante B) | ✅ | `02b-wireframes-novedades-preop.html` |
| 0.9 | Comparativa de variantes UX (A/B/C) | ✅ | `02c-variantes-ux-novedades.html` |
| 0.10 | Wireframes flujo seguimientos del equipo | ✅ | `02d-wireframes-seguimientos.html` |
| 0.11 | SOW / arquitectura resumida para equipo interno | ✅ | `03-sow-consultor.md` |
| 0.12 | ADR-001: REST sobre VPN (no CDC) | ✅ | `00-investigacion-mercado.md` §9.11 |
| 0.13 | ADR-002: estrategia de identidad | 🟡 | Tentativo. Recomendación original (Entra ID) revisada el 2026-04-29 — el módulo no elige IdP, hereda del host PWA. Pendiente confirmar mecanismo del host. |
| 0.14 | ADR-003: Generación de OT correctiva en MYE | ✅ | `01-modelo-dominio.md` §13 |
| 0.15 | ADR-004: Sincronización de catálogos | ✅ | `00-investigacion-mercado.md` §9.15 |
| 0.16 | ADR-005: Azure SignalR para push del cierre | ✅ | `01-modelo-dominio.md` §14 |
| 0.17 | Validar modelo con consultor mecánico | ⏳ | Sesión ~2h tras lectura del brief |
| 0.18 | Recibir DDL del preoperacional + contratar shape DTO | 🚧 | Espera por equipo del preop |
| 0.19 | Confirmar lista MVP de tipos de inspección | ⏳ | Motor, hidráulica, post-mantenimiento (propuesta) |
| 0.20 | Limpieza de referencias obsoletas en §2.1, §6, I7 del modelo | ✅ | Banners `⚠️ SECCIÓN HISTÓRICA` y notas inline `⚠️ OBSOLETO` añadidos en §2.1, §3, §6, §7, §7.4.5, §12.10.8, §12.10.9, §12.10.10 e I7 (2026-04-28). Tabla §15.11 marcada como completada. |

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
| 1.9 | Provisionar Azure Database for PostgreSQL Flexible | ⏳ | Marten event store |
| 1.10 | Provisionar Azure Blob Storage | ⏳ | Adjuntos de inspecciones |
| 1.11 | Provisionar Azure Key Vault | ⏳ | Secretos y connection strings |
| 1.12 | Provisionar Azure Application Insights | ⏳ | Observabilidad |
| 1.13 | Provisionar Azure SignalR Service (Standard tier) | ⏳ | Push del cierre de inspección |
| 1.14 | Provisionar Azure API Management | ⏳ | Gateway hacia APIs cloud |
| 1.15 | Configurar GitHub Actions / Azure DevOps pipelines | ⏳ | CI/CD |
| 1.16 | Setup de logging centralizado y alertas | ⏳ | App Insights + Log Analytics |
| 1.17 | Documentar runbook operativo inicial | ⏳ | Cómo desplegar, troubleshooting básico |

---

## Fase 2 — Autenticación y autorización

> **⚠️ Aclaración 2026-04-29:** Inspecciones es módulo dentro de la PWA Sinco MYE móvil existente, no app standalone. El módulo NO elige IdP autónomamente — hereda el contexto del usuario del host. Los pasos abajo asumen Entra ID (recomendación original del ADR-002), pero son condicionales: si el host PWA usa otra cosa, los pasos 2.2/2.3/2.4 los implementa el host (no este módulo) y este módulo solo aporta validación cloud-side del token recibido (2.6/2.7) y autorización por claim (2.5/2.8).

| # | Paso | Estado | Notas |
|---|---|---|---|
| 2.1 | Confirmar mecanismo actual de auth del host PWA Sinco MYE móvil | ⏳ | **Bloqueante real de ADR-002** |
| 2.2 | Diseño de sync usuarios Sinco → IdP del host | ⏳ | Pull-based, vía REST sobre VPN — **solo si el host no lo tiene ya** |
| 2.3 | Implementar job de sync de usuarios | ⏳ | Wolverine timer trigger — **solo si el host no lo tiene ya** |
| 2.4 | Configurar app registration en el IdP del host | ⏳ | OAuth2/OIDC scopes — **responsabilidad del host PWA** |
| 2.5 | Definir matriz comando → capability requerida (least privilege) | ⏳ | **No predefine perfiles del ERP** (técnico, supervisor, etc.). La matriz define **capabilities** (verbos como `ejecutar-inspeccion`, `auditar-inspecciones`, `recibir-alertas-sla`) que el host PWA mapea a su catálogo de perfiles ERP. El módulo nunca consulta "eres técnico" — consulta "tienes capability X". Independiente del IdP elegido (ADR-002). |
| 2.6 | Implementar JWT validation middleware en backend cloud | ⏳ | Configurable según IdP del host |
| 2.7 | Implementar JWT validation en APIs Sinco on-prem | ⏳ | Cross-team con equipos Sinco |
| 2.8 | Política de gestión de claims (obras del técnico) | ⏳ | Mapeo del claim que use el host (p. ej. `sinco_obras` si Entra ID) |
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
| 3.24 | Saga `CerrarInspeccionSaga` | ⏳ | `01-modelo-dominio.md` §6 |
| 3.25 | Saga: derivación automática Cerrada vs CerradaSinOT | ⏳ | §15.6 |
| 3.26 | Saga: apertura de seguimientos al firmar | ⏳ | Para hallazgos `RequiereSeguimiento` |
| 3.27 | Adapter MYE: POST /mye/ot-correctivas | ⏳ | Idempotency-Key=InspeccionId. Tres tests WireMock obligatorios (replay 200, 4xx sin retry, 5xx con backoff). Cuarto test si se adopta fallback `GET` (ver 4.10). Detalle en ADR-003 §13. |
| 3.28 | Adapter Preop: POST /preop/novedades/{id}/verificar | ⏳ | Por cada novedad procesada |
| 3.29 | Adapter Preop: POST /preop/novedades/{id}/descartar | ⏳ | Para `NovedadPreopDescartada_v1` |
| 3.30 | Outbox + reintento exponencial | ⏳ | Wolverine built-in |
| 3.31 | Tests de integración con mocks de Sinco | ⏳ | |

### 3.E Sincronización de catálogos

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.32 | Job de sync inicial + cron diario | ⏳ | ADR-004 |
| 3.33 | Proyección local: `EquipoLocal`, `ParteLocal`, `RepuestoLocal`, `ObraLocal`, `RutinaLocal`, `CausaFallaLocal`, `TipoFallaLocal` | ⏳ | 7 catálogos |
| 3.34 | Estrategia stale-while-revalidate | ⏳ | Si VPN cae |
| 3.35 | Health checks por catálogo | ⏳ | App Insights |

### 3.F APIs cloud (REST hacia el frontend)

| # | Paso | Estado | Notas |
|---|---|---|---|
| 3.36 | Endpoint `POST /inspecciones` (iniciar) | ⏳ | |
| 3.37 | Endpoint `POST /inspecciones/{id}/hallazgos` | ⏳ | |
| 3.38 | Endpoint `PATCH /inspecciones/{id}/hallazgos/{hid}` | ⏳ | |
| 3.39 | Endpoint `DELETE /inspecciones/{id}/hallazgos/{hid}` | ⏳ | Soft delete |
| 3.40 | Endpoints repuestos (3) | ⏳ | |
| 3.41 | Endpoints adjuntos: solicitar SAS + confirmar upload | ⏳ | Pattern blob upload |
| 3.42 | Endpoint `POST /inspecciones/{id}/firmar` | ⏳ | Comando consolidado |
| 3.43 | Endpoint `POST /inspecciones/{id}/cancelar` | ⏳ | |
| 3.44 | Endpoint `GET /inspecciones?equipo=&estado=` (bandeja) | ⏳ | |
| 3.45 | Endpoint `GET /inspecciones/{id}` (detalle) | ⏳ | Sirve `DetalleInspeccionView` (§15.12.1). **Debe** exponer: hallazgos eliminados con `MotivoEliminacion`, novedades preop descartadas con `MotivoDescarte` + `DescartadaPor` + timestamp, trazabilidad de hallazgos escalados (`SeguimientoOrigenId`). Autorización: capability `ejecutar-inspeccion` para contribuyente del stream o capability `auditar-inspecciones` para acceso amplio. |
| 3.46 | Endpoint `GET /equipos/{id}/seguimientos?estado=Abierto` | ⏳ | |
| 3.47 | Endpoint `POST /seguimientos/{id}/resolver` | ⏳ | |
| 3.48 | Endpoint `POST /seguimientos/{id}/escalar` | ⏳ | |
| 3.49 | Endpoint `POST /novedades-preop/{id}/descartar` | ⏳ | Crea evento dedicado |
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
| 3.55 | Proyección Marten `AuditoriaInspeccionesView` | ⏳ | Consume eventos de `InspeccionTecnica` y `SeguimientoHallazgo`, denormaliza por inspección. Materializa indicador `DecisionContradiceReporteOperador`. |
| 3.56 | Endpoint `GET /auditoria/inspecciones?obra=&desde=&hasta=&autor=` | ⏳ | Bandeja filtrable de inspecciones cerradas en una obra. Paginada. Autorización: capability `auditar-inspecciones` (matriz en paso 2.5). |
| 3.57 | Endpoint `GET /auditoria/inspecciones/{id}` | ⏳ | Atajo al `DetalleInspeccionView` (paso 3.45) con autorización de auditoría. Misma proyección, control de acceso por capability. |
| 3.58 | Tests de autorización (usuario sin capability `auditar-inspecciones` recibe 403) | ⏳ | Cross-cutting con paso 2.5 |

> **Diferido a Fase 10** (post-MVP): métricas agregadas (tasa de descarte por usuario firmante / por operador que reportó), workflow de notificación al operador cuando su novedad es descartada, wireframe visual de la bandeja de auditoría (`02e-wireframes-auditoria.html`). Ver `FOLLOWUPS.md` cuando aparezca disparador.

---

## Fase 4 — APIs en Sinco (cross-team, paralelo)

> **Riesgo de programa**: 17 endpoints en 3-4 módulos diferentes de Sinco con 3-4 equipos distintos. Coordinación cross-team es bloqueante.

### 4.A Equipo del Preoperacional

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.1 | `GET /preop/novedades?equipo=&estado=pendiente` | 🚧 | Lista viva, no snapshot |
| 4.2 | `POST /preop/novedades/{id}/verificar` | 🚧 | Body con `AccionRequerida` (RequiereIntervencion o RequiereSeguimiento) + `NovedadTecnica` (diagnóstico) |
| 4.3 | `POST /preop/novedades/{id}/descartar` | 🚧 | Body con motivo |
| 4.4 | DDL del preoperacional compartido | 🚧 | Bloqueante para 0.18 |

### 4.B Equipo MYE núcleo

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.5 | `GET /equipos?obra=` (lista por obra del técnico) | 🚧 | |
| 4.6 | `GET /equipos/{id}` (detalle) | 🚧 | |
| 4.7 | `GET /equipos/{id}/partes` (árbol del equipo) | 🚧 | |
| 4.8 | `GET /partes/{id}` (detalle) | 🚧 | |
| 4.9 | `POST /mye/ot-correctivas` (crear OT) | 🚧 | Cuerpo: hallazgos, repuestos, dictamen, técnico. **Idempotencia real obligatoria** sobre `Idempotency-Key=InspeccionId`: misma key → mismo `200 OK` con mismo `OTCorrectivaIdSinco`, persistente, ventana ≥30 días. Detalle del contrato en ADR-003 §13. |
| 4.10 | `GET /mye/ot-correctivas?inspeccionId={id}` (fallback) | 🚧 | **Condicionalmente obligatorio**: deja de ser opcional si el equipo MYE no puede entregar el contrato de idempotencia real de 4.9. Habilita el patrón "consulta-antes-de-crear" (ADR-003 §13). Si 4.9 cumple, 4.10 queda como deuda técnica diferida. |
| 4.11 | `GET /catalogos/causas-falla` | 🚧 | ADR-004 |
| 4.12 | `GET /catalogos/tipos-falla` | 🚧 | ADR-004 |
| 4.13 | `GET /catalogos/grupos` | 🚧 | |
| 4.14 | `GET /catalogos/obras` | 🚧 | |

### 4.C Equipo Inventario

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.15 | `GET /repuestos?parte=&q=` (búsqueda con compatibilidad) | 🚧 | Critical UX |
| 4.16 | `GET /repuestos/{id}` (detalle) | 🚧 | |
| 4.17 | `GET /catalogos/insumos` | 🚧 | Sync nocturno |

### 4.D Equipo de RRHH / Identidad

| # | Paso | Estado | Notas |
|---|---|---|---|
| 4.18 | `GET /usuarios?role=tecnico` (sync hacia el IdP del host) | 🚧 | Condicional al ADR-002 (tentativo). Solo aplica si el host PWA no tiene ya el sync. |
| 4.19 | `GET /usuarios/{id}` (detalle con obras asignadas) | 🚧 | |

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

### 5.C Pantallas del flujo novedades preop

| # | Paso | Estado | Notas |
|---|---|---|---|
| 5.17 | Lista de novedades con 3 botones inline (variante B) | ⏳ | `02b` pantalla 2 |
| 5.18 | Modal Descartar con motivo obligatorio | ⏳ | |
| 5.19 | Modal Seguimiento con motivo | ⏳ | |
| 5.20 | Wizard verificar paso 1 + paso 2 | ⏳ | Pantallas 5+6 de `02b` |

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
| 6.2 | Plantilla de notificación: OT fallida → destinatarios con capability `recibir-alertas-ot-fallida` | ⏳ | |
| 6.3 | Plantilla de notificación: seguimiento +90 días → destinatarios con capability `recibir-alertas-sla` | ⏳ | Diario hasta cierre |
| 6.4 | Endpoint admin: cola de OT fallidas | ⏳ | |
| 6.5 | Configuración por obra/equipo de destinatarios de alertas (data, no código) | ⏳ | Lista de usernames + capability requerida (`recibir-alertas-sla`, `recibir-alertas-ot-fallida`). Sincronizada desde catálogo MYE o configurada localmente — decisión pendiente. |

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
| 8.4 | Cerrar lista MVP de tipos de inspección | ⏳ | Motor / hidráulica / post-mantenimiento |

---

## Fase 9 — Piloto y producción

| # | Paso | Estado | Notas |
|---|---|---|---|
| 9.1 | Seleccionar 1-2 obras piloto | ⏳ | Con buena conectividad para mitigar restricción online-only |
| 9.2 | Capacitación a técnicos del piloto | ⏳ | Material breve + sesión presencial |
| 9.3 | Despliegue a ambiente staging | ⏳ | |
| 9.4 | UAT con 3-5 técnicos | ⏳ | Feedback iterativo |
| 9.5 | Despliegue a producción | ⏳ | Bandera por obra |
| 9.6 | Monitoreo cercano semana 1 | ⏳ | App Insights + standup diario |
| 9.7 | Retrospectiva + plan de rollout extendido | ⏳ | |

---

## Fase 10 — Post-MVP (diferido / aditivo)

| # | Paso | Estado | Notas |
|---|---|---|---|
| 10.1 | Mediciones (`MedicionRegistrada_v1`) | ⏸ | Más relevante en inspección de monitoreo |
| 10.2 | Programación previa de inspección (estado `Programada`) | ⏸ | Aditivo |
| 10.3 | Modo offline (PWA + IndexedDB + Background Sync) | ⏸ | Estimaciones ya hechas |
| 10.4 | Tipo de inspección "Monitoreo" | ⏸ | Aditivo al enum `TipoInspeccion` |
| 10.5 | Comando bulk para descarte de novedades duplicadas | ⏸ | Si emerge volumen alto |
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
| R1 | Coordinación cross-team Sinco para 17 endpoints en 3-4 módulos | SOW interno + escalación a CTO si bloqueos persisten |
| R2 | Primer Azure de Sinco — landing zone "lite" como deuda técnica | ADR-001 documenta divergencias EDA, plan de migración aditivo |
| R3 | DDL preop bloqueado del lado del equipo del preop | Workstream paralelo, mock del DTO mientras tanto |
| R4 | Conectividad VPN inestable | Stale-while-revalidate en catálogos (ADR-004) + degradación graceful |
| R5 | UX inadecuada para técnico de campo (audiencia nueva) | Validación con consultor mecánico (Fase 8) antes del piloto |
| R6 | Restricción online-only en obras remotas | Selección cuidadosa de obras piloto + documentación clara |
| R7 | Catálogos cambian IDs en Sinco rompiendo audit histórico | Reglas operativas vinculantes (ADR-004): IDs inmutables, descontinuar = flag activo=false |

---

## Métricas de éxito MVP

- ≥10 inspecciones técnicas completadas en piloto
- ≥80% de hallazgos con intervención generan OT correctamente en MYE
- 0 OT generadas con BOM inválido (rechazo MYE)
- Tiempo medio del flujo del técnico: ≤8 min para inspección estándar
- ≤5% de inspecciones quedan en `CierrePendienteOT` por más de 24h
- Feedback de técnicos: ≥4/5 en facilidad de uso

---

## Resumen ejecutivo

```
Fase 0 (Diseño):              ████████████████████  95% ✅ (3 de 20 pasos pendientes)
Fase 1 (Cloud foundations):   ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 2 (Auth):                ░░░░░░░░░░░░░░░░░░░░   0% ⏳ (bloqueado por 2.1)
Fase 3 (Backend):             ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 4 (APIs Sinco):          ░░░░░░░░░░░░░░░░░░░░   0% 🚧 (cross-team)
Fase 5 (Frontend):            ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 6 (Notificaciones):      ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 7 (Calidad):             ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 8 (Validación producto): ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 9 (Piloto):              ░░░░░░░░░░░░░░░░░░░░   0% ⏳
Fase 10 (Post-MVP):           — diferido —
```

**Próximos 3 hitos críticos:**
1. **Fase 8 — Validación con consultor mecánico** (sesión 2h, posible esta semana)
2. **Fase 0.18 — Recibir DDL del preoperacional** (desbloquea Fase 4)
3. **Fase 1 — Arrancar fundaciones Azure en paralelo** (bloqueante de Fase 2-3)
