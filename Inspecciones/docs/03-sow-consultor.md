# Arquitectura resumida — Módulo de Inspecciones Técnicas Sinco MYE

**Versión:** 1.0
**Fecha:** 2026-04-27
**Audiencia:** Equipo de desarrollo interno Sinco + arquitectura corporativa
**Propósito:** Documento técnico autocontenido que compila el alcance, decisiones y workstreams para alinear al equipo que construye el módulo. Sirve también como referencia si en el futuro se involucra un Microsoft Partner externo, aunque la construcción es responsabilidad del equipo Sinco.

> **Para consultor de producto / ingeniero mecánico que valida el proceso de inspección**, ver `04-brief-consultor-producto.md` — ese documento está en lenguaje del dominio operativo, sin código ni infraestructura.

---

## 1. Resumen ejecutivo

Sinco construye su **primer despliegue Azure** como módulo nuevo dentro de la app móvil de Sinco MYE: un módulo de **inspecciones técnicas** ejecutado por técnicos e ingenieros de mantenimiento, complementario a la inspección preoperacional que realizan los operadores.

El módulo permite al técnico recorrer una rutina estandarizada de revisión, verificar las novedades reportadas previamente por el operario, descubrir hallazgos propios, estimar repuestos requeridos, emitir diagnóstico y firmar. Cuando hay hallazgos que requieren intervención formal, se genera automáticamente una OT correctiva en Sinco MYE con el BOM consolidado.

**Stack:** backend .NET (Marten event sourcing sobre PostgreSQL), frontend React + MUI **embebido como módulo de la PWA Sinco MYE móvil existente** (no app standalone), despliegue en Azure Container Apps. Integración con Sinco on-prem vía REST sobre VPN. Identidad heredada del host PWA — el módulo no tiene IdP propio; los APIs cloud validan el token que el host emita. Mecanismo concreto del host: ADR-002 (estado tentativo, pendiente de confirmar).

**Calendario estimado:** 16-22 semanas al MVP estable, con tres workstreams paralelos (Landing Zone Azure, APIs Sinco, módulo Inspecciones).

---

## 2. Contexto

**Sinco** es una compañía colombiana fundada en 1996 que desarrolla SINCO ERP, un sistema modular para empresas de construcción, inmobiliarias y concesiones viales. **Sinco MYE** (Maquinaria y Equipos) es el módulo de gestión de flota, mantenimiento y costos operativos por proyecto.

Sinco MYE ya tiene una **app móvil** con módulos de Preoperacional, Estado de equipos, Aprobaciones, Agenda, Combustible. El módulo de **Inspecciones técnicas** es nuevo, se suma a esa app y reusa la identidad/usuarios existentes.

**Diferencias funcionales clave entre preoperacional e inspección técnica:**

| Dimensión | Preoperacional (existe) | Inspección técnica (este módulo) |
|---|---|---|
| Quién | Operario / conductor | Técnico o ingeniero |
| Profundidad | Reporte de novedades a alto nivel | Diagnóstico técnico, mediciones, identificación de causa raíz |
| Output clave | Reportar novedades | Verificar novedades + descubrir hallazgos + estimar repuestos + generar OT correctiva |
| Conectividad | Pobre (campo abierto) | Mejor (taller, oficina) |

**Contexto técnico crítico:**

- Sinco no tiene Azure landing zone hoy. El módulo es la primera carga cloud de la compañía.
- El resto del ecosistema Sinco vive on-prem (BD SQL Server, APIs REST, identidad).
- Ningún endpoint REST que el módulo necesita existe hoy en Sinco. Hay que construirlos.
- Los catálogos de equipos, partes, causas de falla, tipos de falla, repuestos, ubicaciones y obras existen como datos en el ERP — solo hay que exponerlos vía REST.

---

## 3. Alcance del compromiso del consultor

**Incluido en el alcance:**

1. **Azure Landing Zone "lite"** según patrón Cloud Adoption Framework + acelerador ALZ Bicep.
2. **Conectividad híbrida** Sinco on-prem ↔ Azure (VPN site-to-site).
3. **Backend del módulo** — .NET 8+, Marten event sourcing, ASP.NET API, deploy en Container Apps.
4. **Frontend móvil** — React + MUI integrado dentro de la app móvil existente de Sinco MYE.
5. **Adapters** que consumen las APIs REST de Sinco (Preoperacional, MYE núcleo, Inventario, User master).
6. **Saga de cierre** que coordina la generación de OT correctiva en MYE.
7. **Sync de identidad** desde Sinco hacia el IdP que use el host PWA — **alcance condicional al ADR-002**: si el host ya tiene Entra ID con sync de Sinco, este punto desaparece; si no, el módulo no es responsable de implementarlo (es decisión del host).
8. **Pipeline CI/CD** desde el primer despliegue (Azure DevOps o GitHub Actions).
9. **Observabilidad end-to-end** — Application Insights, Log Analytics, traces distribuidos.
10. **Transferencia de conocimiento** al equipo Sinco con horas comprometidas.

**Fuera del alcance del consultor (responsabilidad de Sinco):**

- Endpoints REST a construir en los módulos existentes de Sinco (preoperacional, MYE núcleo, inventario, identidad). Sinco entrega contratos OpenAPI.
- Catálogos de datos (causas de falla, tipos de falla, partes, etc.) — ya existen.
- Diseño visual de las pantallas — los wireframes y la paleta están definidos.
- Modelo de dominio — está definido (ver `01-modelo-dominio.md`).
- Decisiones arquitectónicas vinculantes (ADR-001, ADR-002, ADR-003).

---

## 4. Arquitectura objetivo

```
┌─────────────────────────  On-Premise Sinco  ──────────────────────────┐
│                                                                        │
│  App móvil Sinco MYE actual ─────── (módulo Inspecciones embebido)     │
│                                                                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌────────────┐ │
│  │ Preoperac.   │  │ MYE núcleo   │  │ Inventario   │  │ User master│ │
│  │ (SQL Server) │  │ (SQL Server) │  │ (SQL Server) │  │ (BD/AD)    │ │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └─────┬──────┘ │
│         │                 │                 │                │        │
│  ┌──────▼─────────────────▼─────────────────▼────────────────▼──────┐ │
│  │  Capa de APIs REST a construir (responsabilidad Sinco)            │ │
│  │  /api/v1/preop/*  /api/v1/equipos /api/v1/rutinas                  │ │
│  │  /api/v1/catalogos/* /api/v1/insumos /api/v1/mye/ot-correctivas    │ │
│  │  /api/v1/admin/usuarios                                            │ │
│  └───────────────────────────────┬───────────────────────────────────┘ │
│                                  │                                     │
└──────────────────────────────────┼─────────────────────────────────────┘
                                   │
                          VPN site-to-site / ExpressRoute
                                   │
┌──────────────────────────────────┼─────────────────────────────────────┐
│                       Azure (landing zone Sinco)                       │
│                                  │                                     │
│           ┌──────────────────────▼─────────────────┐                   │
│           │ Azure API Management (gateway móvil)    │                   │
│           └──────────────────────┬─────────────────┘                   │
│                                  │                                     │
│           ┌──────────────────────▼─────────────────┐                   │
│           │ Container App: Inspecciones .NET       │                   │
│           │  - ASP.NET API (móvil + admin)         │                   │
│           │  - Marten event store + projections    │                   │
│           │  - Wolverine: handlers, sagas, outbox  │                   │
│           │  - ACLs: preop / mye / inventario      │                   │
│           └─────┬──────────────┬────────────┬──────┘                   │
│                 │              │            │                          │
│           ┌─────▼─────┐  ┌─────▼─────┐ ┌────▼──────┐                   │
│           │ PostgreSQL│  │   Blob    │ │ Key Vault │                   │
│           │ Flexible  │  │  Storage  │ │           │                   │
│           │ (Marten)  │  │ (fotos)   │ │           │                   │
│           └───────────┘  └───────────┘ └───────────┘                   │
│                                                                        │
│  ┌─────────────────────┐         ┌─────────────────────┐               │
│  │ Microsoft Entra ID  │ ◀─────  │ Azure Function      │               │
│  │ (tenant Sinco)      │  sync   │ "User Sync"         │               │
│  └─────────────────────┘         │ (timer cada N min)  │               │
│                                  └─────────────────────┘               │
│                                                                        │
│  Application Insights · Log Analytics · Defender for Cloud             │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

**Componentes clave:**

- **Container App "Inspecciones"** — backend .NET con event store en Postgres y proyecciones para UI.
- **PostgreSQL Flexible Server** — Marten requiere Postgres. Hosting gestionado, HA opcional según ambiente.
- **Blob Storage** — fotos de evidencia de hallazgos. SAS para upload directo desde móvil.
- **API Management** — gateway móvil con políticas, throttling, versionado.
- **IdP del host PWA Sinco MYE móvil** — el módulo no elige IdP; valida cloud-side el token que el host emite. Mecanismo concreto: ADR-002 (tentativo).
- **VPN site-to-site** — túnel hacia los APIs on-prem.

---

## 5. Workstreams y secuenciación

```
[ A ] Landing Zone Azure ████████░░░░░░░░░░░░░░░░░░░░░  (4-6 sem)  ◀── prerrequisito
                          │
[ B-0 ] Contrato API estándar  ░░░██░░░░░░░░░░░░░░░░░░  (1 sem)
[ B-1 ] APIs preop             ░░░░░██████░░░░░░░░░░░░  (2-3 sem)
[ B-2 ] APIs MYE núcleo        ░░░░░██████░░░░░░░░░░░░  (2-3 sem, paralelo)
[ B-3 ] APIs inventario        ░░░░░████░░░░░░░░░░░░░░  (2 sem, paralelo)
[ B-4 ] POST OT correctiva     ░░░░░░░░░░██░░░░░░░░░░░  (1-2 sem)
[ B-5 ] Integración E2E        ░░░░░░░░░░░░░██░░░░░░░░  (2 sem)
[ B-6 ] Identity & Entra sync  ░░░░░░░██████░░░░░░░░░░  (2-3 sem, paralelo)
                                                       │
[ C ] Módulo Inspecciones                              │
       (backend + frontend)    ░░░░██████████████████  (12+ sem)
```

**Reglas de secuenciación:**

- **A bloquea a B y C** en su despliegue final, pero el modelado del dominio en C puede arrancar el día 1.
- **B-0 (contrato API estándar)** es prerrequisito secuencial de B-1..B-6 — define paginación, errores, auth, naming, OpenAPI base.
- **B-1, B-2, B-3, B-6** corren en paralelo si Sinco asigna equipos por módulo.
- **C** consume B vía stub durante desarrollo (eventos simulados, fixtures); la integración real se hace cerca del cierre del MVP.

**Total realista:** 16-22 semanas (4-5.5 meses) con todos los equipos en paralelo y coordinación funcionando. Si los equipos van secuenciales, sube a 25-30 semanas.

---

## 6. Stack técnico Azure

| Pieza | Servicio Azure | Notas |
|---|---|---|
| Compute | **Azure Container Apps** | Kubernetes-lite. Sweet spot para módulo nuevo. |
| Event store | **Azure Database for PostgreSQL Flexible Server** | Requerido por Marten. |
| API Gateway | **API Management** | Versionado, throttling, políticas. |
| Real-time push | **Azure SignalR Service** | Push del backend al cliente PWA cuando MYE responde con la OT (ADR-005). Tier Standard prod, Free dev. |
| Storage | **Blob Storage** | Fotos. SAS para upload directo. |
| Secrets | **Key Vault** | Conexiones, certificados. Private endpoint. |
| Identity | **Heredada del host PWA Sinco MYE móvil** | El módulo no tiene IdP propio; valida cloud-side el token que el host emita. ADR-002 (tentativo). |
| Observability | **Application Insights + Log Analytics** | Telemetría .NET nativa, traces distribuidos. |
| Security baseline | **Defender for Cloud** | En suscripciones prod y non-prod. |
| Networking | **Hub-spoke + VPN Gateway** | Site-to-site hacia Sinco. |
| CI/CD | **Azure DevOps** o **GitHub Actions** | Lo que el equipo Sinco prefiera. |

**Lo que NO se incluye en MVP** (agregable después): Azure Firewall, Sentinel, Bastion, ExpressRoute, Front Door, App Gateway WAF v2, segunda región.

**Stack del módulo:**

- **.NET 8+** con **Marten** (event sourcing) y **Wolverine** (mediator + outbox + saga).
- **React + MUI** para frontend, embebido dentro de la app móvil Sinco MYE.
- **OpenAPI/Swagger** para documentación de APIs.
- **xUnit + Pact** para tests (unit, integration, contract).

---

## 7. APIs a construir / exponer

Total: **17 endpoints** distribuidos en **cuatro módulos** Sinco. Lista completa en `01-modelo-dominio.md §12.9.7`.

| Endpoint | Módulo dueño | Estado |
|---|---|---|
| `GET /api/v1/preop/novedades?obra=&estado=&...` | Preoperacional | A construir |
| `GET /api/v1/preop/novedades/{id}` | Preoperacional | A construir |
| `GET /api/v1/preop/adjuntos/{id}` | Preoperacional | A construir |
| `POST /api/v1/preop/novedades/{id}/verificar` | Preoperacional | A construir |
| `GET /api/v1/equipos?codigo=&grupo=&...` | MYE núcleo | A construir |
| `GET /api/v1/equipos/{equipoCodigo}` | MYE núcleo | A construir |
| `GET /api/v1/rutinas?grupo=&tipo=` | MYE núcleo | A construir |
| `GET /api/v1/rutinas/{rutinaCodigo}` | MYE núcleo | A construir |
| `GET /api/v1/catalogos/partes?q=` | MYE núcleo | A construir |
| `GET /api/v1/catalogos/causas-falla` | MYE núcleo | A construir (data existe) |
| `GET /api/v1/catalogos/tipos-falla` | MYE núcleo | A construir (data existe) |
| `GET /api/v1/catalogos/ubicaciones` | MYE núcleo | A construir |
| `GET /api/v1/catalogos/obras` | MYE núcleo | A construir |
| `GET /api/v1/insumos?parteId=&q=` | Inventario | A construir |
| `POST /api/v1/mye/ot-correctivas` | MYE núcleo | A construir |
| `GET /api/v1/admin/usuarios?desde={lastSync}` | User master | A construir (para sync Entra) |

**Convenciones obligatorias** (definidas en B-0):

- Paginación con headers estándar.
- Error envelope unificado.
- Auth: el cliente móvil ya autenticado por el host PWA propaga el token a las APIs Sinco; éstas validan el token con el IdP que se acuerde en ADR-002.
- Versionado en URL (`/api/v1/...`).
- OpenAPI/Swagger publicado por cada endpoint.
- Idempotency-Key en POSTs no-idempotentes.

---

## 8. Decisiones arquitectónicas vinculantes

Estos ADRs ya están aceptados y **no son negociables sin reabrir la discusión con stakeholders Sinco**.

### ADR-001 — Integración con preoperacional vía REST sobre VPN

Toda integración cross-bounded-context entre el módulo Azure y los módulos on-prem se hace por **REST request/reply sobre VPN**. Se descartó la opción CDC + Service Bus por sobre-ingeniería para el primer despliegue cloud de Sinco. Cumplimiento parcial con la guía EDA Sinco — divergencia consciente y reversible. Detalle completo en `00-investigacion-mercado.md §9.11`.

### ADR-002 — Identidad: heredada del host PWA Sinco MYE móvil (tentativo, pendiente de cerrar)

El módulo Inspecciones es **componente embebido en la PWA Sinco MYE móvil existente**, no app standalone — por lo tanto no elige IdP autónomamente. El cliente móvil llega a las APIs cloud y on-prem con el token que el host PWA ya posee tras el login. Las APIs cloud y on-prem validan ese token contra el IdP que se acuerde formalmente en este ADR. La opción C originalmente recomendada (Microsoft Entra ID con sync desde Sinco) sigue siendo viable solo si el host PWA usa Entra ID o se mueve a usarlo; en caso contrario hay que adoptar lo que el host use. El análisis de las 5 opciones evaluadas y la decisión final en `00-investigacion-mercado.md §9.14`.

### ADR-003 — Generación de OT correctiva en MYE

Generación **automática** vía saga `CerrarInspeccionSaga` al recibir `InspeccionFirmada_v1`, **solo** si la inspección tiene al menos un hallazgo con `AccionRequerida = RequiereIntervencion`. Idempotency-Key = InspeccionId. Wolverine maneja reintento con backoff exponencial. Errores 4xx → notificación a supervisor; 5xx → reintento. Detalle completo en `01-modelo-dominio.md §13`.

### ADR-004 — Sincronización de catálogos de referencia

Catálogos (causas/tipos de falla, partes, ubicaciones, obras, equipos, rutinas, repuestos) sincronizados con **sync inicial + cron diario nocturno con `If-Modified-Since`/`ETag` + stale-while-revalidate** como fallback. Botón admin "refrescar ahora" diferido a v1.1. Reglas operativas vinculantes en el lado ERP: IDs/códigos inmutables, renombrar = cambiar solo descripción, descontinuar = `activa = false` no delete. Ventana de staleness aceptada: hasta 24h. Detalle completo en `00-investigacion-mercado.md §9.15`.

### ADR-005 — SignalR para notificación push del cierre

**Azure SignalR Service** para empujar al cliente la confirmación de cierre cuando MYE responde async. Hub `InspeccionesHub` con `JoinInspeccion(id)` autenticado por JWT y validado contra `TecnicosContribuyentes`. Eventos publicados: `OTGenerada` (con OTId+OTNumero), `InspeccionCerradaSinOT`, `OTGeneracionFallida`. Cliente PWA usa `@microsoft/signalr` con `withAutomaticReconnect()`. Fallback HTTP polling cada 5s si SignalR no disponible. Latencia típica: <100ms. Detalle completo en `01-modelo-dominio.md §14`.

---

## 9. Modelo de dominio (resumen)

**Bounded contexts:**

1. **Inspecciones (core)** — event-sourced. Aggregate `InspeccionTecnica`.
2. **Catálogo (supporting)** — proyecciones read-only sincronizadas desde Sinco.
3. **Integración (supporting)** — ACLs hacia Sinco vía REST.
4. **Reporting (supporting)** — proyecciones para UI.

**Aggregates principales:**
- **`InspeccionTecnica`** — aggregate central de la inspección.
- **`SeguimientoHallazgo`** — aggregate transversal a inspecciones del mismo equipo, mantiene el ciclo de vida de hallazgos en seguimiento.

**Estados de `InspeccionTecnica`:** `EnEjecucion → Firmada → Cerrada` (o `CerradaSinOT` si ningún hallazgo requiere intervención). `Cancelada` como rama paralela. La inspección es ad-hoc (sin programación previa) en MVP.

**Estados de `SeguimientoHallazgo`:** `Abierto → Resuelto | Escalado`.

**Catálogo MVP — 20 eventos** (ver `01-modelo-dominio.md` §15.4 para inventario completo):
- Lifecycle (5): `InspeccionIniciada_v1`, `InspeccionFirmada_v1`, `InspeccionCerrada_v1`, `InspeccionCerradaSinOT_v1`, `InspeccionCancelada_v1`.
- Hallazgos (3): `HallazgoRegistrado_v1`, `HallazgoActualizado_v1`, `HallazgoEliminado_v1`.
- Novedades preop (1): `NovedadPreopDescartada_v1`.
- Repuestos (3): `RepuestoEstimado_v1`, `RepuestoActualizado_v1`, `RepuestoRemovido_v1`.
- Adjuntos (2): `AdjuntoSubido_v1`, `AdjuntoEliminado_v1`.
- Firma atómicos (2): `DiagnosticoEmitido_v1`, `DictamenEstablecido_v1`.
- Seguimiento (3): `SeguimientoAbierto_v1`, `SeguimientoResuelto_v1`, `SeguimientoEscalado_v1`.
- Integración (1): `OTGeneracionFallida_v1`.

**Concepto clave: dos fuentes de hallazgos** (una sola estructura `Hallazgo`):
- Origen `PreOperacional` — verificación de novedad reportada por el operario.
- Origen `Manual` — descubrimiento del propio técnico durante la inspección.

**Concepto clave: `AccionRequerida`** (3 valores accionables, único mecanismo de clasificación; severidad cualitativa fue eliminada del modelo):
- `NoRequiereIntervencion` (verde) — solo histórico, no genera OT.
- `RequiereSeguimiento` (naranja) — abre `SeguimientoHallazgo` transversal al equipo, no genera OT.
- `RequiereIntervencion` (rojo) — aporta línea al BOM consolidado, genera OT correctiva en MYE.

Las mismas 3 opciones se usan en hallazgos manuales, novedades preop y seguimientos previos — un solo mental model en todo el sistema.

**Captura UX como wizard condicional:**
- Paso 1 siempre: parte, novedad técnica, acción requerida, adjuntos (≥1 obligatorio para intervención).
- Paso 2 solo si `AccionRequerida = RequiereIntervencion`: acción correctiva, causa de falla, tipo de falla, repuestos (≥1 obligatorio).

**Invariantes** (I-H1 a I-H9 del Hallazgo + I-F1 a I-F3 de inmutabilidad post-firma + V-F1 a V-F7 de validaciones pre-firma) — ver `01-modelo-dominio.md` §15 para detalle completo.

**Modelo completo en `01-modelo-dominio.md` §15** (fuente de verdad; las secciones §2.1 a §14 quedan como histórico de evolución).

---

## 10. Criterios de selección del consultor

El consultor debe ser **Microsoft Partner activo** con las siguientes competencias verificables:

| Requisito | Por qué |
|---|---|
| Azure Specialization en Infrastructure (mínimo) | Construye la landing zone desde cero. |
| Experiencia en CAF / ALZ Bicep o equivalente | Para no improvisar la LZ. |
| **Experiencia comprobable con Marten event sourcing** en .NET | Crítico. Muchos consultores .NET no lo conocen y empujarán Cosmos DB o tablas SQL — eso mata el event sourcing real. Pedir referencias específicas. |
| Experiencia con Container Apps en producción | Stack elegido. |
| Experiencia con escenarios híbridos on-prem ↔ Azure (VPN, Private Endpoints) | Conectividad es central. |
| Capacidad de transferencia de conocimiento | El equipo Sinco debe quedar operando solo, no atado al consultor. |

**Descalificadores:**
- Proponer reemplazar Marten por otra cosa "porque ya conocen".
- Proponer Service Bus en MVP en contra del ADR-001.
- No tener referencias verificables del cliente final que use el stack.
- No comprometer horas explícitas de transferencia de conocimiento.

---

## 11. Entregables del consultor

**Documentales:**

1. Diagrama de arquitectura final aprobado por Sinco.
2. Documentación operacional (runbook): qué hacer cuando algo falla, cómo escalar.
3. Catálogo de eventos de dominio con schema versionado.
4. Plan de DR/BCP básico con RPO/RTO acordado.
5. Onboarding del equipo Sinco (mínimo 40 horas).

**Software:**

1. Landing zone Azure desplegada vía IaC (Bicep o Terraform), idempotente.
2. Pipeline CI/CD productivo.
3. Backend del módulo en producción.
4. Frontend del módulo embebido en la app Sinco MYE.
5. Sync de identidad funcionando.
6. Adapters consumiendo APIs Sinco.
7. Saga de cierre con OT generada en MYE end-to-end.

---

## 12. Definition of Done del MVP

El módulo se considera entregado cuando:

1. ✅ Un técnico en obra puede abrir la app, ver inspecciones programadas para él.
2. ✅ Puede iniciar una inspección, recorrer la rutina técnica, marcar items con hallazgo.
3. ✅ Puede importar novedades del preoperacional y verificarlas.
4. ✅ Puede registrar hallazgos ad-hoc usando los catálogos cerrados (Parte, Causa, Tipo).
5. ✅ Para hallazgos `RequiereIntervencion`, puede estimar repuestos del catálogo de inventario.
6. ✅ Puede emitir diagnóstico, dictamen y firmar.
7. ✅ La OT correctiva aparece en MYE con BOM consolidado y código identificable.
8. ✅ El supervisor puede ver KPIs y bandeja consolidada por obra desde la web.
9. ✅ El sistema sobrevive caídas temporales de VPN sin pérdida de datos del lado backend (las inspecciones en proceso quedan persistidas; al recuperar conexión continúan).
10. ✅ Auditoría: cualquier inspección cerrada puede reconstruir su historia completa desde el event store.

> **No incluido en MVP (online-only):** trabajo offline del técnico (sin red). Se difiere a versión posterior. Si el técnico pierde conexión durante una inspección, recibe un mensaje claro y reintenta cuando recupera red. Para futuras versiones, las decisiones técnicas y volúmenes ya están analizados (PWA + IndexedDB + pre-fetch dirigido por obra del día) — esta funcionalidad es agregable sin rediseño del modelo de dominio.

---

## 13. Supuestos a validar en kickoff

Estos supuestos se asumen razonables al firmar el SOW pero deben confirmarse en la primera semana del compromiso. Si alguno no se sostiene, ajusta el plan.

| Supuesto | Riesgo si falla |
|---|---|
| Sinco tiene un Active Directory corporativo o BD propia de usuarios reusable | Bajo — alternativa: crear identidad nueva en Entra |
| El equipo de redes Sinco aprueba salida HTTPS desde on-prem hacia `*.microsoftonline.com` | Medio — alternativa: proxy interno |
| Los catálogos de causas/tipos de falla en MYE tienen los códigos suficientes para el MVP | Bajo — Sinco confirmó que existen |
| El catálogo de partes está bien estructurado para todos los grupos de equipos del piloto | Bajo — Sinco confirmó |
| MVP cubre **una rutina técnica por grupo de mantenimiento** (BULLDOZER, EXCAVADORA, VOLQUETA, etc.). No hay subdivisiones por enfoque (motor/hidráulica) ni distinción de contexto (post-mantenimiento/certificación) en v1. La rutina se deriva del grupo al iniciar inspección — el técnico no selecciona. | Bajo — agregable de forma aditiva si emerge necesidad |
| Cliente piloto a definir antes del arranque del workstream C | Medio — sin piloto el DoD pierde concreción |
| El DDL del preoperacional se entrega al consultor en kickoff | Medio — requerido para B-1 |
| **MVP es online-only** — offline diferido a versión posterior. El técnico debe tener conexión durante la inspección. | Medio — restricción operativa explícita; en obras remotas sin señal no podrá usar el módulo en v1.0 |
| **Stack PWA confirmado**: la app móvil de Sinco MYE es PWA en React, ya tiene Service Worker activo, soporta iOS, mantenida por el mismo equipo que construye este módulo | Bajo — heredamos infraestructura existente, no introducimos SW nuevo |
| La pantalla "Importar desde preoperacional" se diseña con base en wireframes existentes (`02-wireframes-mobile.html`) y se valida con UX Sinco antes de codificar | Bajo — alcance acotado |
| La pantalla de cierre + dictamen + firma (pantalla 6 de wireframes) es propuesta nueva, no existe en mockups Sinco | Medio — validar con UX Sinco |
| El concepto `Dictamen` (PuedeOperar/ConRestriccion/NoPuedeOperar) es nuevo del módulo de inspecciones, no preexistente en MYE | Bajo — se persiste en el aggregate y viaja al request a MYE |

---

## 14. Estimación de calendario

| Hito | Semanas desde kickoff |
|---|---|
| Landing zone Azure operativa, suscripciones aprovisionadas, VPN levantada | 4-6 |
| Contrato API estándar Sinco (B-0) firmado | 5-7 |
| Primer endpoint Sinco productivo (típicamente catálogos de partes) | 7-9 |
| Endpoints completos de B-1..B-6 disponibles para integración | 12-15 |
| Backend del módulo desplegado con dominio modelado y 1 flujo end-to-end stubeado | 12-14 |
| Frontend móvil con flujos principales conectados | 14-18 |
| Integración real consultando APIs Sinco productivos | 16-20 |
| MVP estable cumpliendo DoD del §12 | 16-22 |

**Banda inferior (16 sem):** equipos en paralelo, coordinación funcionando, alcance disciplinado.
**Banda superior (22 sem):** atrasos típicos en APIs Sinco, una iteración de UX adicional, depuración cross on-prem ↔ cloud.

---

## 15. Documentos de referencia (no son entregables del consultor)

- `00-investigacion-mercado.md` — investigación de mercado, ADRs, Cumplimiento EDA Sinco, brief detallado, preguntas abiertas.
- `01-modelo-dominio.md` — modelo de dominio completo: bounded contexts, aggregate, eventos, comandos, invariantes, saga, adapters, naming, estructura de proyecto, reconciliación con plantillas Excel del ERP, refinamiento del Hallazgo, ADR-003.
- `02-wireframes-mobile.html` — wireframes móviles de las pantallas clave (8 pantallas, paleta y patrones tomados de la app real Sinco MYE).
- `Plantillas Excel/` — formatos reales del ERP (Equipos.xlsx, Insumos.xlsx, preoperacional.xlsx, imagenes app.docx).

---

## 16. Glosario

| Término | Significado |
|---|---|
| **Sinco MYE** | Módulo de Maquinaria y Equipos de Sinco ERP. Contiene gestión de flota, mantenimiento, costos. |
| **Preoperacional** | Inspección rutinaria que el operario ejecuta al inicio de turno. Reporta novedades. |
| **Inspección técnica** | Este módulo. Inspección por técnico/ingeniero, más profunda que el preop. |
| **Rutina** | Plantilla estándar de inspección. Set fijo de items `(Parte, Actividad)` por grupo de mantenimiento. Tipos: Preoperacional, Mantenimiento, Técnica. |
| **Item de rutina** | Pareja `(Parte, Actividad)` dentro de una rutina. Por ej. `(MOTOR, VERIFICACION ESTADO)`. |
| **Hallazgo** | Resultado de una actividad que merece atención. Puede venir de novedad de preop verificada o ser descubrimiento ad-hoc del técnico. |
| **Novedad** | Hallazgo reportado por el operario en preoperacional. El técnico la verifica/descarta. |
| **AccionRequerida** | 3 niveles: NoRequiereIntervencion, RequiereSeguimiento, RequiereIntervencion. Solo el último genera OT. |
| **CausaFalla** | Catálogo cerrado del ERP (ej. DESGASTE_NORMAL, FALLA_LUBRICACION). |
| **TipoFalla** | Catálogo cerrado del ERP (ej. MECANICA, HIDRAULICA, ELECTRICA). |
| **BOM** | Bill of Materials. Lista de repuestos e insumos requeridos para la OT correctiva. |
| **OT correctiva** | Orden de trabajo que se genera en Sinco MYE cuando una inspección detecta hallazgos `RequiereIntervencion`. |
| **Dictamen** | Decisión del técnico al cerrar: PuedeOperar, ConRestriccion, NoPuedeOperar. |
| **Marten** | Librería .NET de event sourcing sobre PostgreSQL. |
| **Wolverine** | Librería .NET de mediator + outbox + sagas. Misma familia que Marten. |
| **ALZ** | Azure Landing Zone. Patrón de Microsoft para preparar Azure antes de cargas productivas. |
| **CAF** | Cloud Adoption Framework. Guía de Microsoft que incluye ALZ. |
| **CDC** | Change Data Capture. Mecanismo de SQL Server para leer cambios del log transaccional. Descartado en ADR-001. |
| **EDA** | Event-Driven Architecture. Guía corporativa Sinco con 4 patrones de comunicación. |
| **ACL** | Anti-Corruption Layer. Capa que aísla el dominio del módulo de los contratos externos. |

---

**Fin del documento.**
