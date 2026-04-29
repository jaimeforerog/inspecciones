# Investigación de mercado — App de inspecciones de equipos de construcción

**Fecha:** 2026-04-27
**Contexto:** Estudio preliminar para diseñar una app de inspecciones de maquinaria pesada que se acople a un ERP propio. Stack objetivo: backend .NET con event sourcing sobre **Marten**, frontend **React + MUI**, totalmente desacoplada del ERP (integración por eventos / API).
**Alcance:** panorama amplio (SaaS comerciales, módulos de ERPs, open-source) con foco en LATAM e internacional. Equipo objetivo: maquinaria pesada (excavadoras, retros, grúas, bulldozers, cargadores).
**Dimensiones evaluadas:** modelo de integración, arquitectura técnica, modelo comercial / precios.

> ⚠️ **Nota — código C# y nombres de eventos en este documento son del snapshot inicial de investigación.**
>
> Este archivo contiene propuestas tempranas que fueron refinadas durante el modelado posterior. **La fuente de verdad del modelo de dominio actual está en `01-modelo-dominio.md` §15** (consolidación 2026-04-28).
>
> Conceptos eliminados o renombrados que aparecen aquí: `Severidad`, `ResultadoVerificacion`, `OTCorrectivaSugerida_v1` (renombrado a `CrearOTCorrectivaRequest_v1`), `HallazgoDescubierto_v1`, `HallazgoEnRutina_v2`, `HallazgoFueraDeRutina_v2`, `OrigenHallazgo.Inspector` (renombrado a `Manual`).
>
> Los **ADRs y decisiones de integración** (§9.x) sí siguen siendo vinculantes — no aplica a esta nota.

---

## 1. Resumen ejecutivo

El mercado de software de inspección de equipo pesado se segmenta en cuatro grandes familias:

1. **Apps de inspección horizontales** (formularios + checklists), tipo SafetyCulture / iAuditor, Whip Around, Driveroo, GoAudits. Fáciles de adoptar, débiles en lógica de negocio específica de maquinaria.
2. **CMMS/EAM verticales** centrados en mantenimiento que incluyen inspecciones como sub-módulo: Fracttal One, Tractian, IBM Maximo, SAP PM, Oracle EAM, Infor EAM, HCSS Equipment360, Tenna, B2W Maintain.
3. **Plataformas de gestión de construcción** con módulos de equipo: Procore, Viewpoint Vista, CMiC.
4. **Open-source / extensibles**: openMAINT, ERPNext (módulo Asset), Snipe-IT (adaptado), Atlas CMMS.

El mercado global de fleet/equipment inspection cerró 2024 en **~USD 1.32 B con CAGR 12.6%**, empujado por nuevas regulaciones de eDVIR (FMCSA, feb-2026) y la realidad de que **~73% de los DVIRs en papel nunca llegan a la oficina**. Esto valida la necesidad pero también significa que el espacio está poblado: la diferenciación viable está en **(a) acoplamiento profundo con un ERP propio, (b) arquitectura de eventos como ciudadano de primera clase, (c) ergonomía móvil offline para operador y mecánico, y (d) trazabilidad inmutable para auditoría — terreno natural para event sourcing**.

---

## 2. Apps de inspección horizontales (SaaS)

### SafetyCulture (iAuditor)

Líder de adopción global por simplicidad. Constructor de checklists drag-and-drop, app móvil con offline, fotos, firmas y QR por activo, reportes en PDF. Multi-industria; no es nativo de maquinaria pesada.

- **Integración:** API REST pública, webhooks, integración nativa con Procore, SharePoint, Dropbox y Zapier. Existe una *Universal linked data lists 2-way API* para sincronizar catálogos.
- **Arquitectura:** SaaS multi-tenant cloud, app móvil iOS/Android offline-first.
- **Precios:** plan Premium desde **USD 24/usuario/mes** (anual). Mínimo un seat completo. Free tier limitado.
- **Implicación para nosotros:** es la "vara" en UX. Lo que NO ofrece y nosotros sí podríamos: lógica de horas-máquina, costeo por activo en obra, integración bidireccional con un ERP específico, eventos de dominio expuestos.

### Whip Around

Mobile-first para inspecciones diarias DVIR de flotas. Fuerte en cumplimiento FMCSA. Más débil para maquinaria amarilla "no rodante".

### Driveroo / GoAudits / HCSS Fleet Inspection / HVI App

Variantes anglo del mismo concepto. HCSS ofrece walk-around guiado y fuerte integración con su propio ecosistema (Equipment360, HeavyJob).

---

## 3. CMMS / EAM verticales

### Fracttal One (LATAM)

Nacido en Chile, presencia en **México, Colombia, Brasil, Chile, EE.UU., España, Portugal, Sudáfrica**. Es probablemente el competidor LATAM más relevante.

- **Funcionalidad:** mantenimiento correctivo / preventivo / predictivo / por condición, módulo de monitoreo IoT, lecturas manuales y automáticas, alertas, OTs, inspecciones como tipo de tarea.
- **Integración:** API REST, conectores IoT, webhooks.
- **Arquitectura:** SaaS cloud, app móvil con offline.
- **Precios:** **personalizados por cotización** ("paga por lo que usas"). Posicionado como buena relación precio/calidad en LATAM.
- **Riesgo competitivo:** alto en LATAM. Diferencia: nosotros estamos atados a un ERP propio, ellos son agnósticos.

### Tractian (Brasil → global)

CMMS con AI-Copilot, sensores propios (Smart Trac) para vibración, temperatura, energía. Detecta >70 modos de falla. App móvil offline con SOPs embebidas en cada OT y ejecución paso a paso.

- **Integración:** API, integración con sensores propios y de terceros.
- **Arquitectura:** Cloud + IoT. Mobile-first offline.
- **Precios:** custom; todos los planes incluyen ejecución con AI, modo offline y onboarding.
- **Diferencial fuerte:** SOPs forzadas. Tomar nota: **inspección guiada paso a paso** es UX deseable.

### IBM Maximo Application Suite

EAM enterprise. Modelo *AppPoints* (créditos transferibles entre módulos). Incluye Maximo Visual Inspection (computer vision para detección de defectos).

- **Precios:** desde **~USD 3,150/mes** (entry SaaS). Implementaciones medianas reales **>USD 100k/año**.
- **Implicación:** competencia solo en clientes muy grandes. Para PYME/contratistas medianos, sobredimensionado.

### SAP PM, Oracle EAM, Infor EAM, IFS, Hexagon

Mismo segmento enterprise. Inspección como sub-proceso del ciclo de OT. Curva de implementación dura. Si el ERP del cliente no es ya SAP/Oracle, irrelevantes.

### HCSS Equipment360 / B2W Maintain / Tenna

Ecosistema norteamericano de heavy/civil construction.

- **B2W Maintain** publicó una **API pública** que conecta su módulo de e-forms (B2W Inform) con el de mantenimiento: una inspección completada **dispara automáticamente una OT de reparación**. Ese patrón "inspección → evento → orden de trabajo" es exactamente el flujo que event sourcing modela limpiamente.
- **Tenna** combina telemática + gestión de equipo, integra con HCSS y otros, ofrece API.
- **Precios:** no publicados; cotización.

---

## 4. Plataformas de construcción (no-EAM puras)

### Procore

No es CMMS, pero su **Vista Connector** es el patrón a estudiar para acoplarse a un ERP externo.

- Conector basado en un **servicio Windows ligero (Ryvit) que habla HTTPS + REST** contra el ERP on-prem.
- Sincronización **cada hora** por defecto (batch, no event-driven).
- **Limitación documentada:** no integra con el módulo PM de Vista. Es decir, Procore demuestra que aun los grandes hacen integraciones acopladas vía REST + polling. Si nosotros entregamos integración por eventos en tiempo real, es un diferencial real.

### Viewpoint Vista, CMiC

ERPs verticales de construcción con módulos de equipo. Suele ser más fácil integrarse contra ellos vía API que reemplazarlos.

---

## 5. Open-source y componentes reutilizables

| Producto | Qué es | Aplicabilidad para nosotros |
|---|---|---|
| **openMAINT** | CMMS open-source enterprise (Tecnoteca, Italia). Activos, mantenimiento preventivo/correctivo/breakdown, GIS/BIM, energía. | Referencia de modelo de datos. Stack Java; no reutilizable directamente con .NET. |
| **ERPNext** (Frappe) | ERP open-source completo con módulo Asset (compra, depreciación, mantenimiento). | Alternativa si el ERP propio aún no existe. Stack Python/JS. |
| **Snipe-IT** | Asset management open-source orientado a IT pero adaptable. | Demasiado liviano para inspección de maquinaria. |
| **Atlas CMMS** | Open-source, posicionado para construcción. | A revisar como referencia. |
| **EventSourcing.NetCore** (Oskar Dudycz) | Tutoriales y patrones canónicos de ES en .NET con Marten. | **Recurso clave para el equipo.** |

---

## 6. Patrones de integración observados

| Patrón | Quién lo usa | Latencia | Acoplamiento |
|---|---|---|---|
| REST + polling cada N horas | Procore↔Vista (Ryvit) | Alta (≥1h) | Medio |
| REST + webhooks | SafetyCulture, Fracttal, Tractian | Baja (segundos) | Bajo |
| API "inspección → OT" | B2W Inform → B2W Maintain | Inmediata | Alto (dentro del mismo vendor) |
| File-based (CSV/EDI) | ERPs legacy | Muy alta | Alto |
| **Event-driven (bus de eventos)** | Aún poco común en este vertical | Inmediata | **Bajo** |

**Conclusión:** la opción event-driven está sub-explotada en el vertical. Es donde un diseño con Marten + outbox + bus (RabbitMQ / Azure Service Bus / Kafka) genera diferencia técnica y comercial.

---

## 7. Lecturas para nuestra arquitectura

Marten encaja particularmente bien con este dominio porque:

- Cada inspección es **una secuencia de eventos inmutables** (inspección iniciada, ítem revisado, defecto detectado, foto adjuntada, firmada, aprobada). Eso es literalmente el caso de uso textbook de event sourcing.
- La **auditoría regulatoria** (DVIR / SST / ISO 55000 / cliente final) exige trazabilidad inmutable: ES la da gratis.
- Las **proyecciones** (Marten projections) cubren las vistas que el ERP necesita: estado actual del activo, KPIs de obra, próximas inspecciones, defectos abiertos. Cada proyección es un *contrato de lectura* contra el ERP.
- El **desacople** se materializa publicando eventos al ERP por outbox/bus. El ERP nunca lee la base de datos de inspecciones; consume eventos.

Patrón de referencia: comando llega → handler carga el stream → emite evento(s) → Marten persiste → proyecciones se actualizan → outbox publica al bus → ERP (y otros consumidores) reaccionan.

---

## 8. Matriz comparativa resumida

| Producto | Foco | Móvil offline | API/Webhooks | Eventos nativos | Precio referencia | Riesgo competitivo (LATAM) |
|---|---|---|---|---|---|---|
| SafetyCulture | Inspección horizontal | Sí | Sí / Sí | No | USD 24/u/mes | Medio |
| Fracttal One | CMMS LATAM | Sí | Sí / Sí | No | Custom | **Alto** |
| Tractian | CMMS + IoT | Sí | Sí / Parcial | No | Custom | Medio-Alto |
| IBM Maximo | EAM enterprise | Sí | Sí / Sí | Parcial (Kafka opcional) | >USD 3.150/mes | Bajo (otro segmento) |
| HCSS / B2W | Heavy/civil US | Sí | Sí / Sí | No | Custom | Bajo (foco US) |
| Tenna | Telemática + equipo | Sí | Sí / Sí | No | Custom | Bajo |
| Procore (+ Vista) | Gestión de obra | Sí | Sí / Limitado | No | Custom | Bajo (no es CMMS puro) |
| openMAINT | Open-source EAM | Limitado | Sí / No | No | Gratis | — |
| ERPNext | ERP open-source | Sí (limitado) | Sí / Sí | Parcial | Gratis / Cloud | — |

---

## 9. Hallazgos accionables para nuestro diseño

1. **El "must have" funcional** que TODOS ofrecen y que no podemos quedar por debajo: checklist configurable, foto, firma, QR/RFID por activo, modo offline, reporte PDF/Excel, OT generada desde defecto, dashboard básico.
2. **Diferenciadores creíbles** dado nuestro stack:
   - Integración **event-driven en tiempo real** con el ERP propio (vs. polling/REST que usa la mayoría).
   - **Trazabilidad de auditoría inmutable** habilitada por event sourcing (vendible como cumplimiento ISO/SST).
   - **Inspección guiada paso a paso** estilo Tractian (SOP forzada), que SafetyCulture y Fracttal no enforce.
   - **Costos por activo y por obra** alimentados desde el ERP, mostrados en la app — pocos competidores cierran este loop bidireccionalmente.
3. **Lo que NO debemos construir nosotros**: motor de checklists genérico desde cero (es commodity); construirlo a la altura de SafetyCulture es un proyecto en sí. Considerar si conviene un componente reutilizable.
4. **Modelo comercial de referencia para LATAM**: Fracttal va con cotización, Tractian igual. Si vamos a comercializar fuera del cliente inicial, **USD 15-25 por usuario/mes** es la banda razonable.
5. **Riesgo principal**: Fracttal tiene presencia y dominio del español. Nuestra ventaja no es batirlo en CMMS general, sino ser **la pieza de inspección perfectamente acoplada al ERP del cliente**.

---

## 9.5 Posicionamiento dentro de Sinco MYE (actualización 2026-04-27)

**Aclaración del alcance:** esto **no es una app separada**. Es una **funcionalidad nueva dentro de la app móvil existente de Sinco MYE**. Convive con el módulo preoperacional ya en producción y con el resto del ERP de Sinco. El término "desacople" es arquitectónico (módulo aislado, contratos por evento/API) — no comercial.

### Distinción preoperacional vs. inspección técnica

| Dimensión | Preoperacional (ya existe en MYE) | Inspección técnica (este módulo) |
|---|---|---|
| Usuario | Operario / conductor de la máquina | **Técnico o ingeniero** con conocimiento del equipo |
| Conocimiento previo | Bajo / cotidiano | Alto / especializado |
| Output | Novedades reportadas | Diagnóstico, repuestos requeridos, insumos, verificación de novedades del preoperacional |
| Cuándo | Por turno, antes de operar | Programada / on-demand / disparada por novedad |
| Conectividad típica | Pobre (campo) | Mejor (taller / centro de operaciones) |

### Razón de existir

1. **Cubrir el rol técnico/ingeniero**, que el preoperacional no atiende: el operario reporta "el motor hace ruido"; el técnico determina **qué pieza es, qué repuestos pedir, si la máquina puede seguir operando**.
2. **Cerrar el ciclo del preoperacional**: cada novedad reportada en el preoperacional debe poder ser **verificada / descartada** por un técnico. Hoy ese flujo no existe digitalmente con esa claridad.
3. **Pre-estimar BOM de la OT correctiva**: la inspección técnica entrega ya la lista de repuestos e insumos requeridos para la reparación, lo que acelera el proceso de compras/almacén que ya vive en Sinco.

### Lo que cambia frente a competidores externos

- Fracttal, Tractian y SafetyCulture **no son competidores comerciales** — esto no se vende como producto independiente. Quedan como **referencias de UX y patrones de producto**, nada más.
- **Tractian sube de prioridad como referencia visual** porque su UX está pensada exactamente para el perfil de este módulo: técnico/ingeniero ejecutando OT con SOPs paso a paso, partes y herramientas adjuntas, historial del activo a un tap, mediciones. SafetyCulture/Whip Around son más relevantes para el flujo del operario (preoperacional ya existente).
- **Riesgo a vigilar**: solapamiento con preoperacional. La línea es clara conceptualmente (operario vs. técnico) pero en pantalla puede confundirse — los flujos deben sentirse distintos para no parecer "dos formas de hacer lo mismo".

### Arquitectura recomendada (módulo dentro de Sinco MYE)

```
┌──────────────────────────  Sinco MYE — App móvil  ──────────────────────────┐
│                                                                              │
│   Módulo Preoperacional (existente)         Módulo Inspecciones (nuevo)     │
│   ─────────────────────────────────         ──────────────────────────      │
│   Usuario: operario / conductor             Usuario: técnico / ingeniero    │
│                                                                              │
│   ─ Reporta novedades ──┐                   ─ Verifica novedades ◄───┐      │
│                         │                   ─ Diagnóstico técnico    │      │
│                         │                   ─ Selecciona repuestos   │      │
│                         │                   ─ Genera BOM             │      │
│                         ▼                                            │      │
│              ┌─────────────────────┐         ┌─────────────────────┐ │      │
│              │ Stream Preoperacion │         │ Stream Inspeccion   │ │      │
│              │  (Marten)           │ ──evt──▶│  (Marten)           │ │      │
│              └─────────────────────┘         └──────────┬──────────┘ │      │
│                       ▲                                 │            │      │
│                       └─── Verificación ────────────────┘            │      │
│                                                         │            │      │
│              ┌──────────────────────────────────────────▼──────┐    │      │
│              │      Outbox / Bus de eventos interno Sinco      │    │      │
│              └──────────┬─────────────┬─────────────────┬──────┘    │      │
│                         │             │                 │            │      │
│                  ┌──────▼─────┐ ┌─────▼──────┐ ┌────────▼────────┐  │      │
│                  │ MYE núcleo │ │ Inventario │ │ ADPRO / Finanz. │  │      │
│                  │ OT correct.│ │ Repuestos  │ │ Costeo proyecto │  │      │
│                  │ Medidores  │ │ Insumos    │ │                 │  │      │
│                  └────────────┘ └────────────┘ └─────────────────┘  │      │
│                                                                      │      │
└──────────────────────────────────────────────────────────────────────┘
```

**Cinco principios no-negociables:**

1. **`Inspeccion` y `Preoperacional` son aggregates distintos**, cada uno con su stream y sus eventos. Comparten referencia al `EquipoId`, no a su modelo.
2. **El módulo de inspecciones se suscribe a eventos del preoperacional** (`NovedadReportada_v1`) para presentar al técnico la lista de novedades pendientes por verificar. Emite a su vez `NovedadVerificada_v1` / `NovedadDescartada_v1` que el preoperacional consume.
3. **Catálogo de repuestos e insumos = referencia externa.** El módulo lee del módulo de inventario/almacén de Sinco vía API. Almacena sólo `SkuId + Cantidad` dentro del aggregate. Si el catálogo se reorganiza, las inspecciones históricas siguen apuntando al SKU correcto en su momento (gracias a ES).
4. **Eventos de dominio estables y versionados** (`InspeccionTecnicaIniciada_v1`, `MedicionRegistrada_v1`, `RepuestoEstimado_v1`, `DiagnosticoEmitido_v1`, `InspeccionFirmada_v1`). Otros módulos de Sinco se suscriben a ellos vía bus/outbox.
5. **Tipos de inspección configurables** (técnica de motor, hidráulica, transmisión, eléctrica, llantas técnica, post-mantenimiento, certificación). El catálogo `TipoInspeccion` define plantilla, ítems, mediciones esperadas, criterios de aprobación.

### Flujo principal en el módulo

1. Técnico abre la app → ve **bandeja de novedades pendientes** del preoperacional para los equipos asignados a su obra.
2. Selecciona una novedad → la app crea una **inspección técnica** que la referencia, o el técnico inicia una inspección programada.
3. Ejecuta la plantilla del tipo de inspección: ítems con OK / con-falla / N/A, mediciones numéricas, fotos, observaciones.
4. Por cada falla detectada: identifica **repuestos** (búsqueda en catálogo de inventario) e **insumos**, con cantidad estimada.
5. Emite **diagnóstico** y **dictamen de operación** (puede operar / no puede / con restricción).
6. Firma → se cierra el stream y se publican eventos:
   - `NovedadVerificada` (cierra el ciclo del preoperacional)
   - `RepuestoEstimado` × N (alimenta inventario / compras)
   - `OTCorrectivaSugerida` (lo recoge MYE núcleo para crear/programar la OT)

### Acciones inmediatas

- Levantar el **inventario de tipos de inspección técnica** del MVP (priorizar 2-3, no 10).
- Confirmar el **contrato de eventos del preoperacional**: qué emite hoy, qué hay que agregar, en qué bus.
- Confirmar **cómo expone el catálogo de repuestos/insumos** Sinco hoy (¿módulo inventario? ¿API REST? ¿base compartida?).
- Definir **idempotencia y outbox** para no duplicar OTs correctivas si un evento se reprocesa.
- Definir **modelo de offline para técnicos**: probablemente más relajado que para operarios pero igual obligatorio en obras remotas.

### Recomendación actualizada de referencias visuales

| Producto | Para inspirar | Prioridad |
|---|---|---|
| **Tractian** (móvil) | Ejecución guiada paso a paso, partes y herramientas adjuntas a cada paso, historial del activo, mediciones | **Alta** — es el perfil de usuario |
| **Fracttal One** (web/dashboard) | Vista de bandeja de OTs/novedades, KPIs, ficha de equipo | Alta — paleta y layout cercanos a MUI |
| **SafetyCulture / iAuditor** | Captura de fotos con anotación, firma, reporte PDF | Media — referencia general |
| **B2W Inform → Maintain** | Patrón "inspección con defecto → BOM → OT" | Media — el flujo central de este módulo |
| **App actual de Sinco MYE preoperacional** | Línea base interna, consistencia visual con lo existente | Obligatoria — el técnico ya conoce esa app |

---

> ⚠️ **Nota:** Las secciones §9.6 y §9.10 reflejan una propuesta inicial basada en CDC + Service Bus que **fue descartada en §9.11 (ADR-001)**. Se conservan como contexto histórico de la deliberación. La integración con el preoperacional es ahora **REST sobre VPN**. Leer §9.11 para el diseño vigente.

## 9.6 Arquitectura física en Azure + capa de eventos del preoperacional (2026-04-27, *superseded por §9.11*)

Hechos confirmados por el usuario:
- El módulo se despliega en **Azure**. El resto de Sinco (incluido el preoperacional) sigue **on-premise**. Escenario híbrido.
- El **preoperacional NO emite eventos** y vive en BD relacional tradicional. **Hay que construir la capa de eventos** desde cero.

### Stack Azure recomendado

| Pieza | Servicio Azure | Por qué |
|---|---|---|
| Backend API .NET | **Azure Container Apps** | Kubernetes-lite, scale-to-zero, ideal para módulo nuevo, mucho menos overhead que AKS |
| Event store (Marten) | **Azure Database for PostgreSQL — Flexible Server** | Marten requiere Postgres. Flexible Server permite HA y backups gestionados |
| Bus de eventos | **Azure Service Bus** (topics + subscriptions) | Durabilidad, ordering por sesión, dead-letter, sessions para garantizar orden por equipo |
| Almacenamiento de fotos/firmas | **Azure Blob Storage** | URLs firmadas (SAS) para subida directa desde el móvil sin pasar por el API |
| API gateway hacia móvil | **Azure API Management** | Versionado, throttling, políticas de auth |
| Identidad | **Microsoft Entra ID** o el IdP actual de Sinco vía OIDC | Reuso del SSO interno |
| Secretos | **Azure Key Vault** | Conexiones, certificados, llaves de Service Bus |
| Observabilidad | **Application Insights + Log Analytics** | Telemetría .NET nativa |
| CI/CD | **Azure DevOps** o **GitHub Actions** | Lo que ya use Sinco internamente |

### Conectividad on-prem ↔ Azure

Tres opciones, en orden de robustez:

1. **Azure ExpressRoute** — circuito privado dedicado. Robusto, baja latencia, SLA. Requiere coordinación con telco. Caro.
2. **Site-to-Site VPN** (Azure VPN Gateway) — túnel IPSec sobre internet. Mucho más barato, suficiente para volúmenes moderados de eventos. Recomendado para empezar.
3. **Azure Relay / Hybrid Connections** — túnel reverso punto a punto. Solo si la conectividad es muy puntual (un par de servicios) y no quieren tocar la red.

Recomendación inicial: **VPN sitio-a-sitio**. Sube a ExpressRoute si los volúmenes lo justifican.

### Capa de eventos del preoperacional — patrón propuesto

El preoperacional vive en SQL Server (asumido) on-prem y no expone eventos. Patrón menos invasivo:

```
On-Premise (Sinco)                                Azure
─────────────────────                             ─────────────────

┌─────────────────┐
│  SQL Server     │
│  Preoperacional │
│  (BD existente) │
└────────┬────────┘
         │ CDC habilitado en tablas
         │ (cdc.dbo_NovedadesPreop_CT, etc.)
         ▼
┌─────────────────┐                              ┌─────────────────────┐
│ Servicio        │                              │  Azure Service Bus  │
│ Publisher CDC   │ ──── VPN / ExpressRoute ────▶│  topic:             │
│ (.NET worker o  │                              │  preoperacional.    │
│  Azure Func     │                              │  novedades          │
│  on-prem-       │                              └──────────┬──────────┘
│  triggered)     │                                         │
└─────────────────┘                                         │
                                                            ▼
                                           ┌────────────────────────────────┐
                                           │  Container App: Inspecciones   │
                                           │   - Suscripción al topic       │
                                           │   - Materializa proyección     │
                                           │     "novedades pendientes"     │
                                           │     en su Postgres             │
                                           │   - Idempotencia por           │
                                           │     CDC __$start_lsn           │
                                           └────────────────────────────────┘
```

**Detalles del patrón:**

- **CDC** (Change Data Capture) en SQL Server es la opción menos invasiva: no toca el código del preoperacional, lee del log transaccional. Habilitar `sys.sp_cdc_enable_table` en las tablas relevantes (novedades, detalles, etc.).
- **Servicio publisher**: un worker .NET pequeño que cada N segundos lee `cdc.fn_cdc_get_all_changes_<tabla>` desde el último LSN procesado y traduce cada cambio a un **evento de dominio semántico** (`NovedadReportada_v1`), no a un "row changed". El servicio mantiene el último LSN procesado en su propio storage.
- **Donde corre el publisher**: idealmente **on-prem** (más simple para conectarse a SQL local) y publica a Service Bus en Azure por VPN. Alternativa: en Azure y se conecta por VPN al SQL on-prem (funciona pero la latencia juega en contra cuando el servicio está caliente).
- **Idempotencia downstream**: cada evento lleva el `__$start_lsn` y la operación CDC (1=delete, 2=insert, 3=update-pre, 4=update-post). El consumidor en Azure descarta duplicados por LSN.
- **Eventos de salida del módulo nuevo** (`NovedadVerificada`, `RepuestoEstimado`, `OTCorrectivaSugerida`) van por Service Bus a otro topic. Necesitan llegar a MYE on-prem → mismo patrón en reversa: un suscriptor on-prem (o el API on-prem de MYE) los consume y aplica.

**Trade-off importante a vigilar:** la traducción de "row changed en CDC" → "evento de dominio semántico" no es trivial. Una novedad puede tocar varias tablas (cabecera + detalles + adjuntos) y el evento de dominio idealmente representa el agregado completo. El publisher necesita lógica de **agrupación por transacción** (los registros CDC traen la misma `__$start_lsn` cuando vinieron en la misma transacción). Esto es trabajo real y conviene estimarlo aparte como un sub-proyecto, no como "una capa más".

**Alternativa si se decide tocar el preoperacional:** patrón **Outbox transactional** dentro del preoperacional (cada operación que reporta una novedad inserta el evento en una tabla `outbox` en la misma transacción; un publisher lee la outbox y publica). Es más limpio semánticamente y elimina la complejidad de agrupar CDC, pero requiere desarrollo dentro del preoperacional. Decisión depende de cuánto control hay sobre ese código y de la priorización.

### Diagrama físico actualizado

```
┌─────────────────────────  On-Premise Sinco  ──────────────────────────┐
│                                                                        │
│  App móvil MYE actual ──┐                                              │
│  Preoperacional         │                                              │
│  (BD relacional)        ├──▶ Publisher CDC ────┐                       │
│                         │                       │                       │
│  MYE núcleo (OT)        │                       │                       │
│  Inventario             │                       │                       │
│  ADPRO                  │                       │ VPN / ExpressRoute   │
│                         │                       │                       │
│  API REST de MYE ───────┘                       │                       │
│  (consumida por         ▲                       │                       │
│   módulo Inspec.)       │                       │                       │
│                         │                       ▼                       │
└─────────────────────────┼─────────────────  ────┼──────────────────────┘
                          │                       │
                          │                       │
┌─────────────────────────┼─────────  Azure  ─────┼──────────────────────┐
│                         │                       │                       │
│           ┌─────────────▼───────┐    ┌──────────▼───────────┐          │
│           │  API Management     │    │  Azure Service Bus   │          │
│           │  (gateway móvil)    │    │  topics:             │          │
│           └──────────┬──────────┘    │  - preoperacional.*  │          │
│                      │               │  - inspecciones.*    │          │
│                      ▼               └──────────┬───────────┘          │
│           ┌──────────────────────┐              │                       │
│           │ Container App        │◀─────────────┘                       │
│           │ Inspecciones .NET    │                                      │
│           │  - Web API           │                                      │
│           │  - Worker (consumer) │                                      │
│           └──────────┬───────────┘                                      │
│                      │                                                  │
│        ┌─────────────┼──────────────┐                                   │
│        ▼             ▼              ▼                                   │
│  ┌─────────┐  ┌────────────┐  ┌───────────┐                             │
│  │ Postgres│  │ Blob       │  │ Key Vault │                             │
│  │ Flexible│  │ Storage    │  │           │                             │
│  │ (Marten)│  │ (fotos)    │  │           │                             │
│  └─────────┘  └────────────┘  └───────────┘                             │
│                                                                         │
│  App Insights · Log Analytics · Entra ID                                │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Consecuencias para el roadmap

1. **El "MVP de inspección" tiene dos sub-proyectos**, no uno:
   - **A.** Módulo nuevo en Azure (.NET + Marten + React/MUI) — donde se concentra el dominio
   - **B.** Capa de eventos del preoperacional (CDC publisher + topics + idempotencia downstream) — infraestructura habilitante
   El B no se ve en pantalla pero sin él el flujo "técnico verifica novedades del operario" no existe.

2. **Conectividad on-prem ↔ Azure (VPN o ExpressRoute) es prerequisito** y suele tener su propio lead time con redes/seguridad de Sinco. Iniciar ese trámite ya.

3. **El catálogo de repuestos también necesita su capa de exposición desde on-prem hacia Azure** — mismo patrón: API REST detrás de VPN, o proyección sincronizada por eventos. Si el catálogo es estable y no enorme, una **proyección periódica vía Service Bus** funciona mejor que llamadas síncronas (latencia VPN + autonomía offline del módulo).

4. **Riesgo principal a vigilar:** la latencia del round-trip on-prem ↔ Azure por VPN puede afectar UX en pantallas que necesiten datos de MYE en vivo. Diseñar el módulo asumiendo que **todo lo que ve el técnico está proyectado localmente en Postgres-Azure**, y las llamadas síncronas hacia on-prem se reservan para escrituras críticas (crear OT, descargar repuesto).

---

## 9.7 Azure Landing Zone — primer despliegue cloud de Sinco (2026-04-27)

Hecho confirmado: **Sinco no tiene landing zone en Azure**. Este módulo es el primer despliegue cloud de la compañía. Antes de un solo Container App de inspecciones, hay que decidir y construir las fundaciones cloud — porque no hay dónde poner las cosas.

### Por qué importa tanto

Una landing zone **no es un detalle de infraestructura**: define quién paga, quién tiene acceso, cómo se conecta a on-prem, qué se loguea y a dónde, qué políticas de seguridad rigen. Si el consultor empieza a desplegar recursos sin esto resuelto, en seis meses Sinco tiene un cementerio de recursos sin gobierno: imposible de auditar, imposible de presupuestar, imposible de seguro corporativo.

### Dos enfoques: enterprise vs. lite

Microsoft publica el **Cloud Adoption Framework (CAF)** y el patrón **Azure Landing Zones (ALZ)**. Hay dos niveles:

| Enfoque | Cuándo aplica | Qué incluye |
|---|---|---|
| **Enterprise-Scale Landing Zone** | Empresas con plan de mover muchas cargas a Azure en 12-24 meses | Jerarquía completa de Management Groups, hub-spoke networking, Azure Firewall, Sentinel, governance avanzado, blueprints. Despliegue inicial 4-8 semanas con consultor experimentado. |
| **Application Landing Zone (lite / "ALZ Bicep")** | Una o dos cargas piloto, equipo aprendiendo Azure | Una jerarquía mínima de MG (Tenant Root → Sinco → Workloads), un hub de red simple, dos suscripciones (prod/non-prod), Entra ID, Defender for Cloud baseline, Log Analytics central. Despliegue inicial 1-2 semanas. |

**Recomendación:** **lite** para empezar, con el cuidado de no construirla de forma que impida escalar a enterprise después. El acelerador oficial **ALZ Bicep** o **Terraform AVM** te entrega plantillas que cubren ese punto medio. No improvisar la LZ desde cero — usar el acelerador.

### Mínimo viable de la landing zone para que el módulo arranque

Lo que el consultor debe entregar antes de que el equipo de inspecciones haga el primer `az containerapp create`:

1. **Identidad**: Microsoft Entra ID tenant configurado (probablemente uno nuevo para Sinco-Cloud, o el corporativo si ya existe). Grupos por rol (`sinco-inspecciones-dev`, `sinco-inspecciones-ops`).
2. **Jerarquía de suscripciones**: dos como mínimo — `sinco-inspecciones-prod` y `sinco-inspecciones-nonprod`. Idealmente un Management Group `Sinco` que las contenga, con políticas heredadas.
3. **Networking**:
   - VNet hub con un subnet de gateway para la VPN site-to-site contra Sinco on-prem
   - VNet spoke por suscripción, peering al hub
   - Subnets dedicadas: Container Apps environment, PostgreSQL, Service Bus private endpoint, App Gateway / API Management
   - Private DNS Zones para los private endpoints
4. **Conectividad híbrida**: VPN Gateway (SKU mínimo VpnGw1, escalable) + configuración del lado on-prem coordinada con redes de Sinco. Lead time real: 2-4 semanas con redes corporativas.
5. **Observabilidad central**: Log Analytics workspace compartido + Application Insights. Diagnostic settings habilitados por política.
6. **Seguridad baseline**: Defender for Cloud encendido en las suscripciones, Key Vault con private endpoint, política "no public IP" para PaaS.
7. **DevOps platform**: project en Azure DevOps o repos en GitHub (lo que use Sinco). Service connections con identidades manejadas (Workload Identity Federation, no service principals con secretos).
8. **Naming convention y tagging**: estándar publicado antes de crear el primer recurso. Tags obligatorios: `env`, `app`, `cost-center`, `owner`.

### Lo que NO necesita el MVP (resistir la sobre-ingeniería)

Para evitar que la LZ se vuelva un proyecto interminable y bloquee al módulo: en este primer paso **no** son indispensables Azure Firewall, Sentinel, Bastion, ExpressRoute, Front Door, Application Gateway WAF v2, ni una segunda región. Todo eso es agregable después sin rediseñar lo de arriba.

---

## 9.8 Workstreams y secuenciación (*actualizado por §9.11 y §9.13*)

> **Nota**: este diagrama da la silueta de tres workstreams. El detalle de B (con sub-tracks B-0..B-5) está en §9.13 tras confirmarse que ningún endpoint Sinco existe hoy.

```
Tiempo →

[ A ] Landing Zone Azure ████████░░░░░░░░░░░░░░░░░░░░░  (4-6 sem)  ◀── prerrequisito
                           │
[ B ] APIs Sinco           └─░░░██████████░░░░░░░░░░░░  (4-7 sem)  ◀── ver §9.13
       (preop + MYE núcleo                                            tres equipos
        + inventario)                                                  en paralelo
                                 │
[ C ] Módulo Inspecciones        └──────░░░░██████████  (12+ sem)
       (.NET + Marten +
        React/MUI + UX)
```

**Reglas de secuenciación:**

- **A bloquea a B y C** en su forma final, pero las **decisiones de A no bloquean** el diseño de dominio de C — el equipo puede modelar aggregates, eventos y wireframes en paralelo a que la LZ se construya. Lo único que no puede ocurrir es desplegar C sin A lista.
- **B requiere VPN entregada por A** (parte de la conectividad híbrida).
- **C consume B** para el flujo "técnico verifica novedades del operario", pero puede demostrarse **stub-eado** durante desarrollo (eventos de prueba publicados a mano en Service Bus). Esto permite que el demo de UX no espere a B.

**Plan recomendado:** A y la modelación de dominio de C arrancan **el día uno**, en paralelo. B arranca cuando A tiene VPN. La integración real C↔B ocurre cerca del final del MVP.

---

## 9.9 Brief para el consultor de Sinco

Como la construcción se hace con consultor externo, este documento debe servirle de brief. Lo que debe ir en el SOW del consultor:

### Alcance técnico esperado

1. **Azure Landing Zone "lite"** según §9.7, basada en el acelerador ALZ Bicep o equivalente.
2. **Conectividad híbrida** (VPN site-to-site) hasta SQL Server del preoperacional y, posteriormente, hacia los APIs de MYE núcleo e inventario.
3. **Capa de eventos del preoperacional**: SQL Server CDC en tablas a definir (ver §10), worker publisher .NET, topics en Azure Service Bus, contrato `NovedadReportada_v1` y derivados, idempotencia downstream por LSN.
4. **Módulo Inspecciones**: backend .NET + Marten en Container App, frontend React + MUI, integración con catálogo de repuestos vía proyección sincronizada por Service Bus.
5. **Pipeline CI/CD** desde el primer despliegue (no manual `az` ni clicks en portal).
6. **Observabilidad** end-to-end: trazas distribuidas atravesando preop on-prem → Service Bus → Container App → Postgres.

### Entregables documentales mínimos

- Diagrama de arquitectura final aprobado por Sinco
- Runbook operacional (qué hacer cuando algo falla, cómo escalar)
- Documentación de eventos (registro de schemas con versionado)
- Plan de DR/BCP básico (RPO/RTO acordado para el módulo)
- Transferencia de conocimiento al equipo interno de Sinco con horas comprometidas

### Criterios de evaluación al elegir consultor

- **Microsoft Partner** con competencias activas (mínimo Azure Specialization en Infrastructure, idealmente también DevOps).
- **Experiencia comprobable en CDC desde SQL Server hacia Service Bus** (no es trivial; pedir referencias).
- **Experiencia con Marten** (ojo: muchos consultores .NET no lo conocen y van a empujar Cosmos DB o tablas SQL — eso descarta event sourcing real).
- **Capacidad de transferencia**: que el equipo Sinco quede operando solo, no atado al consultor.

### Lo que Sinco debe definir internamente antes de contratar

- **Owner ejecutivo del programa cloud** (nadie firma facturas Azure si no está claro).
- **Modelo de gobierno**: quién aprueba creación de suscripciones, quién aprueba excepciones de policy.
- **Naming convention y tagging** corporativo (mejor ahora que renombrar después).
- **Política de datos**: qué clase de datos puede salir on-prem hacia cloud, qué requiere encriptación, retención.
- **Cliente piloto del módulo** y métricas de éxito (sin esto el SOW no tiene "definition of done").

---

## 9.10 Modelo del preoperacional y contrato de evento (2026-04-27, *transporte superseded por §9.11*)

> Los **DTOs** y la **estructura de datos** descritos aquí siguen siendo correctos y útiles — describen la forma de "una novedad" tal como debe llegar al módulo. Lo único que cambia con §9.11 es que ya **no se transmiten por Service Bus como eventos** sino que se **devuelven como respuestas JSON** del API REST del preoperacional. Léase como el shape del recurso `Novedad` que expone el endpoint `GET /api/v1/preop/novedades/{id}`.

El usuario confirmó la jerarquía del preoperacional:

```
Equipo
  └── Parte (componentes: motor, hidráulico, frenos, llantas, eléctrico…)
        └── Actividad (cada check específico dentro de la parte)
              └── Adjunto (foto / documento, ligado típicamente a actividad con novedad)
```

### Entidades y semántica esperada

| Entidad | Rol | Origen |
|---|---|---|
| **Equipo** | Maestro / activo. Identifica unívocamente la máquina. Vive en MYE núcleo. | Catálogo MYE |
| **Parte** | Componente o subsistema del equipo. Probablemente parametrizado por tipo/modelo de equipo (no toda máquina tiene los mismos sistemas). | Catálogo |
| **Actividad** | Check / pregunta concreta sobre una parte ("Nivel de aceite", "Estado de llantas"). Cada actividad tiene resultado (OK / novedad / N/A) y opcionalmente valor numérico u observación. | Plantilla + reporte |
| **Adjunto** | Foto o documento que evidencia una novedad. Adjunto al nivel de actividad. | Reporte |

**Una "novedad" emerge** cuando una actividad se reporta con resultado = "con novedad" (o equivalente) y suele acompañarse de una observación textual y uno o más adjuntos.

### Modelo del evento de salida — primer corte

Lo que el módulo de inspecciones técnicas necesita consumir es **una novedad como unidad atómica**, no las filas CDC sueltas. Propuesta de contrato:

```csharp
// Evento publicado al Service Bus topic 'preoperacional.novedades.v1'
public sealed record NovedadReportada_v1
{
    public required Guid NovedadId          { get; init; } // PK estable derivada del registro origen
    public required Guid EquipoId           { get; init; } // FK a maestro MYE
    public required string EquipoCodigo     { get; init; } // human-readable para UX

    public required Guid ParteId            { get; init; }
    public required string ParteNombre      { get; init; }

    public required Guid ActividadId        { get; init; }
    public required string ActividadDescripcion { get; init; }

    public required string Descripcion      { get; init; } // observación del operario
    public string? Severidad                { get; init; } // si el preop la modela
    public string? ValorMedido              { get; init; } // p.ej. presión, nivel
    public string? UnidadMedida             { get; init; }

    public required DateTime ReportadaEn    { get; init; }
    public required string ReportadaPor     { get; init; } // operario
    public required Guid PreoperacionalId   { get; init; } // a qué reporte pertenece

    public required IReadOnlyList<AdjuntoRef> Adjuntos { get; init; }

    // Metadatos para idempotencia/trazabilidad
    public required long CdcLsn             { get; init; } // last LSN procesado
    public required string SourceSystem     { get; init; } = "preoperacional";
    public required int SchemaVersion       { get; init; } = 1;
}

public sealed record AdjuntoRef
{
    public required Guid AdjuntoId          { get; init; }
    public required string NombreOriginal   { get; init; }
    public required string MimeType         { get; init; }
    public required string BlobUri          { get; init; } // ya en Azure Blob Storage
    public required int TamanoBytes         { get; init; }
    public required string Sha256           { get; init; } // verificación e idempotencia
}
```

### Decisiones que esto fuerza

**1. Adjuntos: replicar a Azure Blob vs. streaming on-demand**

Tres opciones, con trade-offs:

| Opción | Pro | Contra |
|---|---|---|
| **Replicar al publicar** (recomendada para MVP) | Módulo en Azure es autosuficiente. UX rápida. Funciona si VPN cae. | Doble almacenamiento. Latencia adicional al publicar. |
| **Streaming on-demand** vía VPN | Cero duplicación. Foto vive en una sola fuente de verdad. | Cada foto cuesta VPN round-trip. UX golpeada. Si VPN cae, se rompe la pantalla. |
| **Híbrido**: thumbnails replicados, full-res on-demand | Mejor UX que streaming puro, menos almacenamiento que replicación total. | Más código. Dos rutas de fallo. |

**Recomendado: replicar al publicar.** El worker publisher, al detectar una novedad, sube los adjuntos a un container `preoperacional-adjuntos/{año}/{mes}/{novedadId}/` en Blob Storage de Azure y solo entonces publica el evento con el `BlobUri` ya válido. Esta es la única forma sensata si la operación del módulo no puede degradarse cuando la VPN tiene problemas.

**2. Granularidad: una `NovedadReportada` por actividad-con-novedad**

Una actividad sin novedad NO genera evento. Eso evita inundar el bus con miles de "OK" diarios y el consumidor solo procesa lo que importa.

**3. Agrupación CDC**

Un cierre de preoperacional toca: `Preoperacional` (cabecera) + N `Actividades` + M `Adjuntos`. Todo en una transacción → todos los registros CDC traen el mismo `__$start_lsn`. El publisher debe:
- Leer todos los registros CDC con un mismo LSN
- Reconstruir cada novedad con sus adjuntos antes de publicar
- Confirmar (commit del último LSN) sólo después de que el evento se publicó exitosamente al bus

Esto es el corazón de la complejidad del workstream B.

**4. Idempotencia downstream**

`NovedadId` es la clave de deduplicación canónica. El consumidor en Azure mantiene una tabla `procesados_preop(NovedadId, CdcLsn)`. Si llega un evento cuyo `NovedadId` ya existe pero con `CdcLsn` mayor, es un **update** del preop (raro pero posible: el operario corrigió). Si es menor o igual, descartar.

### Eventos derivados que el módulo de inspecciones publica

```csharp
// Topic: 'inspecciones.novedades.v1'
public sealed record NovedadVerificada_v1(
    Guid NovedadId,           // referencia a la del preop
    Guid InspeccionId,        // qué inspección la verificó
    string Resultado,         // "Confirmada" | "Descartada" | "RequiereSeguimiento"
    string DiagnosticoTecnico,
    string EmitidoPor,        // técnico/ingeniero
    DateTime VerificadaEn);

// Topic: 'inspecciones.repuestos.v1'
public sealed record RepuestoEstimado_v1(
    Guid InspeccionId,
    Guid EquipoId,
    Guid SkuId,                  // catálogo de inventario Sinco
    decimal CantidadEstimada,
    string UnidadMedida,
    string Justificacion);

// Topic: 'inspecciones.ot.v1'
public sealed record OTCorrectivaSugerida_v1(
    Guid InspeccionId,
    Guid EquipoId,
    string Prioridad,
    string DescripcionTrabajo,
    IReadOnlyList<Guid> NovedadesRelacionadas,
    IReadOnlyList<RepuestoSugerido> Bom,
    string DictamenOperacion);   // "PuedeOperar" | "NoPuedeOperar" | "ConRestriccion"
```

### Lo que falta para cerrar el contrato (preguntas concretas para el usuario)

Para terminar de aterrizar el evento `NovedadReportada_v1` con tipos y nombres correctos:

1. **DDL completo de las cuatro tablas**: nombres reales, columnas, tipos, FKs. Ya lo tienes a mano — pegándolo se cierra.
2. **¿Existe una tabla `Preoperacional` cabecera** que agrupa al equipo + operario + turno + fecha? ¿O el equipo es la raíz del reporte y la cabecera se infiere?
3. **¿Cómo se distingue una actividad con novedad** de una en estado "OK"? ¿Hay un campo `Resultado` / `Estado` / `TieneNovedad`?
4. **¿Los adjuntos viven en disco o en BLOB de SQL Server?** Esto define cómo el publisher accede a ellos.
5. **¿Hay un campo `Severidad` o el técnico la define en su inspección?**
6. **Volumen estimado**: novedades/día por cliente, adjuntos/novedad promedio, MB por adjunto. Para dimensionar Service Bus tier, Blob Storage y VPN bandwidth.

Con el DDL en mano, el contrato del evento queda escrito con tipos exactos y se puede pasar al consultor como entregable cerrado del análisis funcional, no como "diseño en proceso".

---

## 9.11 ADR-001 — Integración con preoperacional vía REST sobre VPN, no CDC + Service Bus

**Fecha:** 2026-04-27
**Estado:** Aceptada
**Reemplaza:** la propuesta CDC + Azure Service Bus de §9.6 y §9.10

### Contexto

El preoperacional vive on-prem en BD relacional sin eventos. El módulo de inspecciones vive en Azure. Hay que conectar ambos para que el técnico/ingeniero pueda ver y verificar las novedades reportadas por el operario. Sinco no tiene aún Azure landing zone y el módulo es la primera carga cloud de la compañía.

Se evaluaron dos enfoques:

**A. CDC + Azure Service Bus** (propuesta inicial en §9.6, §9.10): SQL Server CDC en las tablas del preoperacional, worker publisher que reconstruye novedades por LSN y publica eventos a un topic, módulo en Azure suscrito al topic mantiene proyección local de novedades pendientes. Push real-time, autonomía offline en Azure, audit trail completo del cambio.

**B. REST sobre VPN** (propuesta del usuario, ahora aceptada): el equipo del preoperacional expone endpoints REST (`GET /novedades?...`, `POST /novedades/{id}/verificar`). El módulo de inspecciones llama esos endpoints cuando los necesita. Pull-based, sin infraestructura de eventos en el lado on-prem.

### Decisión

**Se elige B (REST sobre VPN).**

### Razones

1. **Complejidad-vs-valor desfavorable para el contexto.** CDC + bus + publisher + agrupación por LSN + idempotencia + Service Bus + replicación de adjuntos a Blob es mucha infraestructura nueva para una organización sin experiencia previa en Azure. La probabilidad de bugs operacionales ocultos es alta, y diagnosticarlos cruza on-prem ↔ cloud.
2. **Real-time push no es requisito.** El técnico no necesita notificación al milisegundo; revisa la bandeja de novedades cuando se sienta a trabajar. Para ese caso de uso, polling/refresh es suficiente.
3. **Volumen moderado.** Decenas a cientos de novedades/día por cliente en MVP. No justifica un bus.
4. **Menos invasivo en el preoperacional.** Exponer endpoints REST es menos riesgoso que habilitar CDC en producción + escribir publisher con state.
5. **Más rápido al primer demo.** Workstream B baja de 4-8 semanas a 2-3.
6. **Alineado con cómo Sinco probablemente ya integra módulos** internamente (REST inter-módulo es práctica común en ERPs maduros).
7. **Reversible.** Si en el futuro se necesita push real-time o múltiples consumidores, agregar Service Bus encima de los mismos endpoints REST es un cambio aditivo, no una reescritura.

### Trade-offs aceptados conscientemente

- **Latencia**: cada pantalla de bandeja de novedades cuesta un round-trip VPN. Mitigación: caché HTTP corto (30-60s) en el lado del módulo + paginación.
- **Sin notificación push** al técnico cuando aparece una novedad nueva. Aceptable para el caso de uso.
- **Adjuntos**: el módulo en Azure los lee bajo demanda vía VPN (la API devuelve URL firmada que apunta a un endpoint del preop o a un servicio proxy). Si VPN cae, no se ven fotos. Mitigación opcional: cachear en Blob los adjuntos consultados (lazy replication).
- **Acoplamiento al contrato API del preoperacional**: si cambia, el módulo se rompe. Mitigación: versionado de URL (`/api/v1/...`), capa ACL aislada en el módulo (`Sinco.Inspecciones.Adapters.Preoperacional`), tests de contrato (Pact o equivalente).
- **Offline del técnico**: si el técnico va a obra sin señal, su app no llega al API. Mitigación: cuando se conecta, descarga las novedades pendientes de los equipos asignados a su obra a una caché local en el dispositivo. Esto es responsabilidad del cliente móvil, no del backend.

### Lo que NO cambia con esta decisión

- El módulo de inspecciones **sigue siendo event-sourced internamente con Marten**. Cada acción del técnico (`InspeccionIniciada_v1`, `NovedadPreopVerificada_v1`, `HallazgoEnRutina_v2`, `HallazgoFueraDeRutina_v2`, `MedicionRegistrada_v1`, `DiagnosticoEmitido_v1`, `InspeccionFirmada_v1`, `InspeccionCerrada_v1`, etc.) es un evento de dominio en el event store. La OT correctiva NO es un evento de dominio: es un efecto cross-BC implementado como POST con DTO `CrearOTCorrectivaRequest_v1` (ver `01-modelo-dominio.md §8` y §13 sobre la convención evento-de-dominio vs DTO-de-request). El audit trail del dominio se preserva intacto.
- Los **bounded contexts** se mantienen igual: el módulo no comparte BD con preoperacional ni con MYE núcleo. La separación lógica sigue.
- La decisión de **stack Azure** (Container Apps + Postgres Flexible + Blob + Key Vault) no cambia. Lo que se elimina es Service Bus + CDC + worker publisher.
- La **ALZ lite** y el **VPN site-to-site** siguen siendo prerrequisito.

### Re-arquitectura física

```
On-Prem (Sinco)                                Azure (módulo Inspecciones)

┌────────────────────────────┐
│ SQL Server                 │
│ Preoperacional (existente) │
└────────────┬───────────────┘
             │
             │ (lectura/escritura interna del módulo preop)
             │
┌────────────▼─────────────────┐                   ┌────────────────────────────┐
│ API REST Preoperacional      │                   │ Container App              │
│  (a construir o ampliar)     │                   │ Inspecciones .NET          │
│                              │ ◀── HTTPS/VPN ───▶│  - Web API móvil/web       │
│  GET  /novedades             │   (pull síncrono) │  - ACL Preoperacional      │
│  GET  /novedades/{id}        │                   │  - Marten event store      │
│  GET  /adjuntos/{id}         │                   │  - Aggregates inspección   │
│  POST /novedades/{id}/       │                   └────────────┬───────────────┘
│        verificar             │                                │
└──────────────────────────────┘                                │
                                                          ┌─────▼──────────┐
┌────────────────────────────┐                            │ Postgres       │
│ API REST MYE núcleo        │                            │ Flexible       │
│ + Inventario               │ ◀── HTTPS/VPN ────────────▶│ (event store)  │
│                            │  (POST OT, lookup SKU)     └────────────────┘
└────────────────────────────┘                                  │
                                                          ┌─────▼──────────┐
                                                          │ Blob Storage   │
                                                          │ (cache foto    │
                                                          │  + propios)    │
                                                          └────────────────┘
```

**Lo que se construye (vs. lo que ya estaba):**

- **API REST del preoperacional**: nuevos endpoints (o ampliación de los que ya existan) para exponer las novedades como recurso consumible. Es responsabilidad del equipo del preoperacional, no del consultor cloud.
- **API REST de MYE núcleo / Inventario**: para que el módulo cree OT correctivas y haga lookup de SKUs. Probablemente ya existen endpoints — confirmar y documentar.
- **ACL Preoperacional dentro del módulo**: clase/módulo `.NET` que encapsula las llamadas REST y traduce a tipos del dominio. Si mañana se cambia a CDC, sólo se reemplaza este ACL.
- **Caché HTTP** corto en el módulo para no martillar el API por cada refresh.
- **Replicación lazy de adjuntos** (opcional, agregable después): cuando el técnico abre una foto por primera vez, el módulo la cachea en Blob para futuras visualizaciones.

### Endpoints REST que el preoperacional debe exponer (primer borrador)

```
GET  /api/v1/preop/novedades?obra={obraId}&estado=pendiente&page=1&size=50
        → Lista paginada de novedades pendientes de verificar para los equipos de la obra.

GET  /api/v1/preop/novedades/{novedadId}
        → Detalle completo: equipo, parte, actividad, descripción,
          severidad, reporte, lista de URLs de adjuntos.

GET  /api/v1/preop/adjuntos/{adjuntoId}
        → Stream del archivo (foto/documento). Auth obligatoria.
          Headers de cache.

POST /api/v1/preop/novedades/{novedadId}/verificar
     Body: { resultado: "Confirmada"|"Descartada"|"RequiereSeguimiento",
             diagnostico: string,
             inspeccionId: guid,
             verificadaPor: string,
             verificadaEn: datetime }
        → Marca la novedad como verificada en el preoperacional.
          Cierra el ciclo. Idempotente por (novedadId, inspeccionId).
```

DTOs `Novedad` y `Adjunto` con los mismos campos del record que se había modelado en §9.10 — el contenido es el mismo, lo que cambia es el **transporte** (HTTP request/response en vez de Service Bus message).

### Re-estimación de workstreams

```
[ A ] Landing Zone Azure ████████░░░░░░░░░░░░░  (4-6 sem)  ◀── prerrequisito
                          │
[ B ] API preoperacional  └─░░░██████░░░░░░░░░░  (2-3 sem)  ◀── era 4-8 con CDC
       (ampliación REST                │
        + auth + paginación)           │
                                       │
[ C ] Módulo Inspecciones              └──░░░░██████████  (12+ sem)  (sin cambios)
```

Workstream B baja sustancialmente. El equipo del preoperacional gana la responsabilidad pero no necesita aprender Azure ni eventos — sólo expone endpoints, algo que ya saben hacer.

### Cumplimiento con la guía EDA Sinco

La guía corporativa **"Arquitectura dirigida por eventos (EDA)"** describe cuatro patrones de comunicación que un sub-dominio típico debería combinar (Request/Reply síncrono, Comando asíncrono, Evento de dominio, Evento de integración). Esta sección reconoce explícitamente cómo cumplimos cada uno y qué se diverge conscientemente con esta decisión.

| Sección de la guía EDA | Cumplimiento del módulo Inspecciones | Comentario |
|---|---|---|
| §1 Eventos como ciudadanos de primera clase | ✅ Total | Marten event-sourced. El aggregate `InspeccionTecnica` se reconstruye desde el stream. Todo cambio de estado es un evento inmutable. |
| §2 Tipos de evento (dominio vs integración) | ⚠️ Parcial | Tenemos eventos de dominio internos claros, pero al no publicar a un broker no llegamos a tener "eventos de integración" en el sentido estricto. La distinción se preserva a nivel de naming (DTO de request HTTP ≠ evento de dominio). |
| §3.1 Request/Reply síncrono | ✅ Total | Toda integración cross-BC va por REST sobre VPN. Justificado: las consultas de catálogo (parte, causa, tipo de falla, repuestos) y las verificaciones de novedades necesitan respuesta inmediata para que el flujo del técnico avance. |
| §3.2 Comando asíncrono | ❌ Ausente — divergencia consciente | No usamos broker. Por las razones del ADR. La generación de OT a MYE, que técnicamente cabría aquí, se modela como Request/Reply con la saga absorbiendo la latencia. |
| §3.3 Evento de dominio | ✅ Total | Marten + proyecciones (`BandejaTecnico`, `DetalleInspeccion`, `KPIsPorObra`, `HistoricoEquipo`) + saga `CerrarInspeccionSaga`. |
| §3.4 Evento de integración (vía broker) | ❌ Ausente — divergencia consciente | Misma razón que §3.2. Los hechos cross-BC se materializan vía POST REST en lugar de publicarse a un bus. |
| §4 Patrón mixto | ⚠️ Parcial | Solo combinamos 2 de los 4 patrones (Request/Reply + Eventos de dominio internos). |
| §5 Idempotencia | ✅ Total | `Idempotency-Key = InspeccionId` en POST a MYE. `{inspeccionId}-{novedadId}` en POST de verificación. Optimistic concurrency en streams Marten. |
| §6 Consistencia eventual | ✅ Total | Fuerte solo dentro del aggregate (invariantes I1-I11). Eventual entre BCs. ADR-003 modela explícitamente la ventana con estado intermedio "Firmada pendiente OT". |
| §7 Cuándo NO usar eventos | ✅ Total | Catálogos cerrados por GET síncrono, validaciones por query, operaciones intra-aggregate como lógica interna. Coincide al 100% con la recomendación. |

#### Justificación de las divergencias §3.2 y §3.4

La guía EDA estándar de Sinco asume infraestructura de mensajería disponible y madura. El módulo de inspecciones es **el primer despliegue Azure de la compañía** y enfrenta tres restricciones específicas que no aplican a sub-dominios on-prem maduros:

1. **No hay landing zone Azure aún** (workstream A en construcción). Service Bus suma infraestructura nueva sobre infraestructura nueva.
2. **No hay bus corporativo Sinco hoy** que extienda hacia cloud. Crear uno solo para este módulo es over-engineering.
3. **El equipo está aprendiendo Azure por primera vez**, asistido por consultor externo. Reducir piezas operacionales nuevas baja el riesgo de bugs ocultos cruzando on-prem ↔ cloud.

Las consecuencias funcionales se aceptan conscientemente:

- **Sin push real-time** del operario al técnico cuando aparece una novedad. Aceptable porque el técnico revisa la bandeja al iniciar su jornada, no necesita notificación al milisegundo.
- **Sin múltiples consumidores** suscribiéndose al mismo hecho. Hoy solo MYE consume "OT correctiva sugerida". Si en el futuro otros sub-dominios lo necesitan (BI, mantenimiento predictivo, CRM de clientes), agregar publicación a un bus es aditivo.
- **Sin pub/sub para auditoría externa**. El audit trail interno está completo en Marten; lo que no hay es difusión asíncrona de hechos hacia otros BCs por fuera del POST.

#### Camino de migración a cumplimiento EDA pleno

Esta divergencia es **reversible** y prevista como temporal. Cuando alguna de estas tres condiciones se cumpla, conviene migrar a comando asíncrono / evento de integración:

- Sinco corporativo adopta un bus de eventos (Service Bus, RabbitMQ, Kafka) que extienda on-prem ↔ cloud.
- Aparece un segundo consumidor real para los hechos del módulo (BI, predictivo, otro BC).
- Los volúmenes superan lo que polling/REST puede sostener cómodamente.

La migración es **aditiva**: se agrega publicación al bus encima de los mismos endpoints REST existentes. El contrato de los eventos `OTCorrectivaSolicitada`, `NovedadVerificada` queda definido desde ya en el modelo de dominio (§13 de `01-modelo-dominio.md`); lo único que cambia el día de migración es el transporte.

#### Decisión documentada de no aplicar el ADR-004 propuesto en su momento

Se evaluó agregar una **outbox local "para historial"** (registro de eventos de integración en una tabla Postgres aunque el efecto cross-BC siga siendo por REST). Se descartó del MVP por simplicidad — el costo de mantener una tabla más + decorator en cada adapter no se justifica si los eventos no tienen consumidor real. Si en el futuro se materializa el escenario de "segundo consumidor", esa outbox vuelve al backlog como ADR.

---

## 9.12 Dos fuentes de hallazgos en la inspección técnica (2026-04-27)

Aclaración del usuario: el técnico/ingeniero **no sólo verifica novedades del preoperacional**. También **descubre hallazgos propios** durante la inspección, aunque no exista una novedad previa que los origine. Para registrarlos usa **servicios (REST) de equipo y parte** que ya existen en Sinco.

Esto duplica el alcance funcional del módulo y hay que modelarlo explícitamente.

### Las dos fuentes

| Origen | Cómo entra al módulo | Quién la inicia |
|---|---|---|
| **Novedad del preoperacional** | API REST del preoperacional → bandeja "novedades pendientes" → técnico verifica | El operario, en su turno previo |
| **Hallazgo del inspector** | API REST de equipo/parte → técnico crea hallazgo desde cero | El propio técnico durante la inspección |

Ambas son **`Hallazgo`** dentro del aggregate `InspeccionTecnica` — solo cambia la propiedad `Origen`.

### Modelo de dominio actualizado

```csharp
public sealed class InspeccionTecnica  // aggregate root
{
    public Guid InspeccionId { get; private set; }
    public Guid EquipoId { get; private set; }
    public Guid TipoInspeccionId { get; private set; }
    public string TecnicoId { get; private set; }
    public DateTime IniciadaEn { get; private set; }
    public InspeccionEstado Estado { get; private set; }

    private readonly List<Hallazgo> _hallazgos = new();
    public IReadOnlyList<Hallazgo> Hallazgos => _hallazgos;

    private readonly List<RepuestoEstimado> _repuestos = new();
    public IReadOnlyList<RepuestoEstimado> Repuestos => _repuestos;

    public string? DiagnosticoFinal { get; private set; }
    public DictamenOperacion? Dictamen { get; private set; }

    // Comandos (cada uno emite uno o más eventos)
    public void VerificarNovedadPreoperacional(Guid novedadPreopId,
        ResultadoVerificacion resultado, string diagnosticoEspecifico) { ... }

    public void DescubrirHallazgo(Guid parteId, string actividadDescubierta,
        Severidad severidad, string descripcion, IReadOnlyList<Guid> adjuntos) { ... }

    public void EstimarRepuesto(Guid hallazgoId, Guid skuId,
        decimal cantidad, string unidad, string justificacion) { ... }

    public void EmitirDiagnostico(string diagnostico, DictamenOperacion dictamen) { ... }

    public void Firmar(string firmaTecnico) { ... }
}

// NOTA: Esta es la estructura inicial de investigación.
// Ver `01-modelo-dominio.md` §15.2 para la estructura final del Hallazgo.
// Cambios consolidados (2026-04-28):
//   - Severidad eliminada del modelo
//   - ResultadoVerificacion eliminado (descartar emite evento dedicado)
//   - OrigenHallazgo.Inspector renombrado a Manual
//   - ParteEquipoId siempre obligatorio
//   - TipoFallaId / CausaFallaId obligatorios si AccionRequerida ≠ NoRequiereIntervencion

public sealed record Hallazgo(
    Guid HallazgoId,
    OrigenHallazgo Origen,                // PreOperacional | Manual
    Guid? NovedadPreopReferenciada,       // null si Origen = Manual
    Guid ParteId,
    string ActividadDescripcion,
    AccionRequerida AccionRequerida,
    string Descripcion,
    IReadOnlyList<Guid> AdjuntosIds,
    DateTime RegistradoEn);

public enum OrigenHallazgo { PreOperacional, Manual }
public enum AccionRequerida { NoRequiereIntervencion, RequiereSeguimiento, RequiereIntervencion }
public enum DictamenOperacion { Apto, AptoConRestricciones, NoApto }
```

### Eventos de dominio (Marten)

```csharp
// Origen preoperacional
public sealed record NovedadPreopVerificada_v1(
    Guid InspeccionId, Guid HallazgoId, Guid NovedadPreopId,
    ResultadoVerificacion Resultado, string Diagnostico,
    string TecnicoId, DateTime VerificadaEn);

// Origen inspector — NUEVO
public sealed record HallazgoDescubierto_v1(
    Guid InspeccionId, Guid HallazgoId,
    Guid EquipoId, Guid ParteId,
    string ActividadDescripcion, Severidad Severidad,
    string Descripcion, IReadOnlyList<Guid> AdjuntosIds,
    string TecnicoId, DateTime RegistradoEn);

// Comunes
public sealed record RepuestoEstimadoEnHallazgo_v1(
    Guid InspeccionId, Guid HallazgoId,
    Guid SkuId, decimal Cantidad, string Unidad, string Justificacion);

public sealed record DiagnosticoEmitido_v1(
    Guid InspeccionId, string Diagnostico, DictamenOperacion Dictamen,
    DateTime EmitidoEn);

public sealed record InspeccionFirmada_v1(
    Guid InspeccionId, string TecnicoId, string FirmaUri,
    DateTime FirmadaEn);

// Salida hacia el resto de Sinco (REST POST, no Service Bus por ADR-001)
public sealed record OTCorrectivaSugerida_v1(
    Guid InspeccionId, Guid EquipoId, Severidad PrioridadAgregada,
    string DescripcionTrabajo,
    IReadOnlyList<Guid> NovedadesPreopRelacionadas,
    IReadOnlyList<Guid> HallazgosInspectorRelacionados,
    IReadOnlyList<RepuestoBom> Bom,
    DictamenOperacion Dictamen);
```

Notar que `OTCorrectivaSugerida_v1` lleva **dos listas** de origen: las novedades del preop que se confirmaron y los hallazgos descubiertos. MYE núcleo recibe el BOM consolidado sin tener que diferenciar — para él es una sola OT con sus repuestos.

### APIs REST adicionales que el módulo consume

A las del preoperacional (§9.11) se suman las de equipo y parte. Estas probablemente ya existen en MYE núcleo o catálogos de Sinco — confirmar nombres reales:

```
GET /api/v1/equipos/{equipoId}
       → Detalle del equipo: serial, modelo, marca, obra asignada, horómetro, etc.

GET /api/v1/equipos/{equipoId}/partes
       → Lista de partes/componentes aplicables a este equipo
         (probablemente derivadas del modelo). Para llenar el selector
         cuando el técnico crea un hallazgo.

GET /api/v1/partes/{parteId}
       → Detalle de la parte. Opcional: actividades típicas de inspección
         para esa parte (sirve como sugerencias auto-completadas).

GET /api/v1/repuestos?parteId={id}&q={busqueda}
       → Búsqueda en catálogo de inventario, filtrada por parte (si la
         relación parte↔repuestos existe) y por término libre. Para llenar
         el selector cuando el técnico estima repuestos.

POST /api/v1/mye/ot-correctivas
       → Crear OT correctiva sugerida desde la inspección. Idempotente
         por InspeccionId. Body = mapping del evento OTCorrectivaSugerida_v1.
```

### Flujo del técnico, ahora con dos fuentes

1. Técnico abre la app → la app carga la inspección asignada (equipo X, tipo Y).
2. **Tab "Novedades del preop"** → llama `GET /preop/novedades?equipo=X&estado=pendiente`. El técnico verifica una a una (confirmar / descartar / requiere seguimiento).
3. **Tab "Hallazgos del inspector"** → vacío al inicio. Botón "+ Agregar hallazgo":
   - Selector de parte: `GET /equipos/{X}/partes`
   - Descripción de la actividad/hallazgo (texto libre + sugerencias de `partes/{id}/actividades-tipicas`)
   - Severidad, descripción detallada, fotos
4. **Para cada hallazgo (de cualquier fuente)**: el técnico opcionalmente estima repuestos buscando en `GET /repuestos?parteId=...&q=...`.
5. **Diagnóstico final + dictamen** (puede operar / con restricción / no puede operar).
6. **Firmar y cerrar**: emite `InspeccionFirmada` y dispara `POST /mye/ot-correctivas` con el BOM consolidado.

### UX implication

La pantalla de inspección **ya no es lineal** ("recorrer plantilla y marcar OK/novedad"); es una **bandeja con dos tabs y un acumulador de hallazgos**. Esto se parece más al patrón de Tractian (técnico ejecutando OT con ítems acumulables) que al de SafetyCulture (checklist secuencial). Refuerza la decisión de §9.5 de tomar Tractian como referencia visual prioritaria.

### Consecuencias para el plan

- El workstream B (§9.8) cubre el preoperacional. **Hay que sumar dependencias del workstream "APIs de catálogo"**: equipo, parte, repuestos. Si esos endpoints ya existen en Sinco actuales, es trabajo de descubrimiento y documentación, no de construcción. Si no existen, se vuelve un sub-proyecto adicional. Aclarar con el equipo de MYE núcleo / inventario.
- El **MVP no se completa sin la fuente proactiva**: si solo se construye la verificación de preoperacional, la propuesta de valor del módulo es la mitad. La fuente proactiva es lo que justifica el rol del técnico.
- El **catálogo de partes por equipo** debe estar en buena forma del lado de Sinco; si es desordenado o incompleto, eso afecta la UX del módulo aunque no sea responsabilidad de éste. Vale la pena validar antes del MVP.

---

## 9.13 Inventario de APIs Sinco — qué existe, qué hay que construir (2026-04-27)

Tras confirmación del usuario el 2026-04-27:

- **Ninguno** de los endpoints REST que el módulo necesita existe hoy en Sinco. Todos son a construir.
- **El catálogo de partes está fuertemente estructurado**: siempre existen partes para cada equipo. La data está bien, lo que falta es exposición.

Esto reescribe el alcance del workstream B y obliga a coordinación cross-team.

### Mapa de endpoints, módulo dueño y equipo responsable

| Endpoint | Módulo Sinco dueño | Equipo responsable (a confirmar) | Estado |
|---|---|---|---|
| `GET /api/v1/preop/novedades?obra=&estado=&page=` | Preoperacional | Equipo del preop | A construir |
| `GET /api/v1/preop/novedades/{id}` | Preoperacional | Equipo del preop | A construir |
| `GET /api/v1/preop/adjuntos/{id}` | Preoperacional | Equipo del preop | A construir |
| `POST /api/v1/preop/novedades/{id}/verificar` | Preoperacional | Equipo del preop | A construir |
| `GET /api/v1/equipos/{id}` | MYE núcleo | Equipo de MYE | A construir |
| `GET /api/v1/equipos/{id}/partes` | MYE núcleo | Equipo de MYE | A construir |
| `GET /api/v1/partes/{id}` | MYE núcleo | Equipo de MYE | A construir |
| `GET /api/v1/partes/{id}/actividades-tipicas` (opcional) | MYE núcleo | Equipo de MYE | A construir / nice-to-have |
| `GET /api/v1/repuestos?parteId=&q=` | Inventario / Almacén | Equipo de inventario | A construir |
| `POST /api/v1/mye/ot-correctivas` | MYE núcleo | Equipo de MYE | A construir |
| `GET /api/v1/admin/usuarios?desde={lastSync}` | User master Sinco | Equipo seguridad/IT Sinco | A construir (para sync Entra, ver §9.14) |

Diez a once endpoints en **cuatro módulos distintos** de Sinco, posiblemente **cuatro equipos distintos** (sumando el de identidad/usuarios por el ADR-002).

> **Nota (2026-04-27):** Tras la revisión de plantillas Excel del ERP, este inventario fue **revisado** en `01-modelo-dominio.md §12.8`. La versión vigente cambia: el endpoint `/equipos/{id}/partes` se reemplaza por `/equipos/{cod}` + `/rutinas?grupo=&tipo=` (las partes vienen via los items de la rutina aplicable al grupo del equipo, no asociadas al equipo individual). Se agregan catálogos cerrados de partes, actividades, ubicaciones y obras. Total final: **15 endpoints**. Léase `01-modelo-dominio.md §12.8` para la lista vigente.

### Lo bueno

- **El catálogo de partes está bien**, así que la calidad de la data no es un riesgo. El selector de parte en el móvil va a funcionar bien desde el día uno (asumiendo que el endpoint la entrega íntegra).
- Como no hay API existente, **se puede diseñar de cero un contrato consistente** (mismo estilo de paginación, errores, auth, versionado en URL) en vez de heredar inconsistencias.
- Lo que **sí** existe es la **data subyacente** y los **procedimientos almacenados / ABMs internos**, así que cada equipo construye una capa de exposición sobre su propia BD, no lógica de negocio nueva. Eso es más rápido.

### Lo que esto obliga

1. **Definir un contrato API estándar Sinco** antes de que cada equipo se ponga a construir. Si no, cada uno va a inventar su propio estilo de paginación, errores, auth, naming. Recomendado: documento corto con OpenAPI base + reglas (HTTP status codes, naming, error envelope, pagination headers, versionado, auth shared). Una página por sección de la doc, no un libro.
2. **Coordinación cross-team** ahora es la dependencia crítica del programa. Hay que tener un **dueño/program manager** que sincronice los tres equipos (preop, MYE, inventario) con el equipo de la app móvil y el consultor cloud.
3. **Auth común**: si cada API usa un esquema de auth distinto, el cliente Azure se vuelve un Frankenstein. Definir un esquema único — probablemente OAuth2 client credentials contra Entra ID o un IdP corporativo Sinco — y aplicarlo a todos.
4. **Versionado obligatorio en URL** (`/api/v1/...`) desde el primer día. Sale gratis y previene dolor futuro.
5. **Documentación viva**: cada equipo entrega contrato OpenAPI/Swagger publicado. El consultor cloud y el equipo del módulo construyen contra esos contratos, no contra implementaciones empíricas.

### Re-estimación realista del workstream B

```
Antes (solo preop):              2-3 semanas
Ahora (preop + MYE + inventario): 4-7 semanas, calendario, no esfuerzo
```

La pesadez no está en el esfuerzo agregado de cada equipo (cada uno aporta lo suyo en paralelo), está en la **coordinación**: alinear tres equipos, definir contrato común, integrar y testear punta a punta. La banda alta del estimado asume que los equipos no han trabajado antes con esta cadencia.

### Workstream B desglosado en sub-tracks

```
B-0: Definir contrato API estándar Sinco          (1 sem)        ◀ secuencial, prerequisito
       (paginación, errores, auth, versionado,
        OpenAPI baseline)
       
B-1: Endpoints del preoperacional                 (2-3 sem)      ─┐
       (equipo preop)                                              │
                                                                   │ paralelo
B-2: Endpoints de equipos/partes                  (2-3 sem)      ─┤
       (equipo MYE)                                                │
                                                                   │
B-3: Endpoints de repuestos + lookup              (2 sem)        ─┘
       (equipo inventario)
       
B-4: Endpoint POST OT correctiva                  (1-2 sem)     ◀ depende de B-2
       (equipo MYE)
       
B-5: Integración punta a punta + ajustes          (2 sem)       ◀ todos
       contractuales
```

### Implicaciones para el roadmap consolidado

```
[ A ] Landing Zone Azure         ████████░░░░░░░░░░░░░░░░░░░░░  (4-6 sem)
                                  │
[ B-0 ] Contrato API estándar     ░░░██░░░░░░░░░░░░░░░░░░░░░░░  (1 sem)
                                     │
[B-1..3] APIs por módulo Sinco       ░░░░██████░░░░░░░░░░░░░░░  (2-3 sem en paralelo)
                                            │
[ B-4 ] POST OT correctivas                 ░░░░░░░██░░░░░░░░░  (1-2 sem)
                                                    │
[ B-5 ] Integración punta a punta           ░░░░░░░░░░██░░░░░░  (2 sem)
                                                       │
[ C ] Módulo Inspecciones (Azure) ░░░░░░░░░██████████████████  (12+ sem)
        - Modelado dominio puede arrancar el día 1
        - Pantallas con datos reales requieren B-1 listo
        - Integración real requiere B-1..5 listos
```

**Total realista de calendario al MVP estable**: 16-22 semanas (4-5.5 meses) si los equipos trabajan en paralelo y la coordinación funciona. Si todo es secuencial, suma a 25-30 semanas.

### Riesgos de programa que vale la pena nombrar

- **El equipo de MYE on-prem es probable cuello de botella**: aporta tres endpoints (equipos, partes, OT correctivas) que son los más complejos. Si están saturados con su roadmap actual, esto se desliza.
- **Inconsistencia de contrato**: si no se hace B-0 primero, cada equipo construye distinto y la integración duele en B-5.
- **Auth fragmentada**: si el OAuth/IdP corporativo Sinco no está alineado, cada API termina con su propio scheme y el módulo Azure se vuelve frágil.
- **Catálogo de partes "está bien" pero…**: validar que la API entregue todos los campos que el técnico necesita en pantalla (tipo, posición, criticidad, etc.), no sólo `id + nombre`. Si la BD los tiene pero la API los omite, se pierde valor.

---

## 9.14 ADR-002 — Estrategia de autenticación y autorización (2026-04-27)

**Estado:** Tentativamente aceptada, dependiente de confirmar el mecanismo de auth actual de Sinco.

### Contexto

- Los técnicos/ingenieros que usarán el módulo nuevo **son los mismos usuarios que ya existen en Sinco**. No es población nueva.
- El módulo vive en **Azure**; el ERP y la auth actual viven **on-prem en redes distintas**. La auth no se puede consumir tal cual desde cloud sin cruzar el VPN.
- **No hay restricción fuerte** de costo o licencia respecto a Microsoft Entra ID.
- El **mecanismo actual** de auth en Sinco es desconocido / probablemente mixto (BD propia, AD, y/o algo más por módulo).
- El módulo necesita autenticar tanto **desde el móvil hacia APIs en Azure** como **desde el móvil hacia APIs on-prem** (preop, MYE, inventario), idealmente con un solo token.

### Restricciones que esto impone

1. **No reusar la auth tal cual** (sesiones server-side de Sinco no funcionan en cloud).
2. **No duplicar credenciales**: el técnico no debería tener "una clave Sinco + una clave Azure". Una sola identidad operativa.
3. **Token único** que sirva contra cloud y on-prem, para no fragmentar el cliente móvil.
4. **Sinco on-prem mantiene la verdad sobre quién es usuario válido y qué obras/equipos puede ver** — la autorización sigue siendo Sinco; sólo cambia el mecanismo de autenticación.

### Opciones evaluadas

| Opción | Descripción | Pro | Con |
|---|---|---|---|
| **A. Reusar auth Sinco directamente** | El módulo cloud llama a un endpoint de Sinco que valida cookie/sesión | Sin duplicación | No funciona con OAuth2/JWT/móvil moderno; requiere VPN siempre arriba; no escalable |
| **B. Construir IdentityServer/OpenIddict propio** | Token issuer .NET sobre la base de datos de usuarios Sinco | Stack .NET familiar, sin licencias | Custom code que mantener; security review duro; revocación, MFA, audit hechos a mano |
| **C. Microsoft Entra ID + sync de usuarios** | Entra ID como IdP cloud, usuarios sincronizados desde Sinco vía VPN | Estándar OAuth2/OIDC, MFA, conditional access, audit, libs maduras .NET y móvil | Sincronización a mantener; costo Entra (aceptable según user) |
| **D. Microsoft Entra External ID con custom auth** | External ID con custom policy que llama a Sinco para validar credenciales | Sinco mantiene la verdad de credenciales | Cost por MAU; custom policies complejas; user store distribuido |
| **E. Federación SAML/OIDC** | Sinco como IdP federado a Entra | Sinco fuente de verdad, sin duplicación | Solo viable si Sinco ya soporta federación moderna (probablemente no) |

### Decisión recomendada (tentativa)

**Opción C: Microsoft Entra ID como IdP del módulo cloud, con sync pull-based desde Sinco vía VPN.**

Es el balance correcto entre:
- Estándar industry (OAuth2/OIDC con tokens JWT estándar — todas las librerías .NET y móviles los entienden).
- Bajo riesgo de ingeniería personalizada (no se construye token issuer; se usa lo de Microsoft).
- Compatibilidad con que **on-prem también valide los mismos tokens**: cualquier API .NET puede validar JWTs Entra usando `Microsoft.AspNetCore.Authentication.JwtBearer` + JWKS público.
- Camino limpio para sumar MFA, conditional access, B2C de clientes futuros sin reescribir.
- Sinco mantiene la **verdad sobre roles y obras asignadas** — eso entra al token vía claims enrichment al momento del login.

### Arquitectura del flujo

```
┌─────────────────────────────  On-Prem Sinco  ────────────────────────────┐
│                                                                            │
│  ┌──────────────────┐        ┌──────────────────────────┐                  │
│  │ BD usuarios      │        │ Servicio "User Sync API" │                  │
│  │ Sinco (master)   │ ─────▶ │ (expone usuarios + roles │                  │
│  │ + roles + obras  │        │  + obras vía REST)       │                  │
│  └──────────────────┘        └────────────┬─────────────┘                  │
│                                            │                                │
│  ┌──────────────────┐                      │                                │
│  │ APIs on-prem     │                      │                                │
│  │ (preop, MYE,     │                      │                                │
│  │  inventario)     │                      │                                │
│  │                  │                      │                                │
│  │ Validan JWT      │                      │                                │
│  │ Entra usando     │                      │                                │
│  │ JWKS público     │                      │                                │
│  └────────▲─────────┘                      │                                │
│           │                                │                                │
└───────────┼────────────────────────────────┼────────────────────────────────┘
            │ HTTPS / VPN                    │ VPN (pull periódico)
            │                                │
┌───────────┼────────────────────────────────┼────────────────────────────────┐
│           │                       ┌────────▼────────────┐                   │
│           │                       │ Azure Function /    │                   │
│           │                       │ Logic App "Sync     │                   │
│           │                       │ Sinco→Entra"        │                   │
│           │                       │ (cada N min: SCIM   │                   │
│           │                       │  o Microsoft Graph) │                   │
│           │                       └────────┬────────────┘                   │
│           │                                │                                │
│           │                       ┌────────▼────────────┐                   │
│           │                       │ Microsoft Entra ID  │                   │
│           │                       │ (tenant Sinco)      │                   │
│           │                       │  - Users sincronizad│                   │
│           │                       │  - Groups por rol   │                   │
│           │                       │  - Custom claims    │                   │
│           │                       │  - JWKS publicado   │                   │
│           │                       └────────┬────────────┘                   │
│           │                                │                                │
│           │                       ┌────────▼────────────┐                   │
│           │                       │ App registrations:  │                   │
│           │                       │  - Mobile (public)  │                   │
│           │                       │  - API Inspecciones │                   │
│           │                       │  - APIs on-prem     │                   │
│           │                       │    (como resources) │                   │
│           │                       └────────┬────────────┘                   │
│           │                                │                                │
│           │   ┌────────────────────────────▼─────────────────────────┐      │
│           │   │              App móvil del técnico                    │      │
│           │   │   1. Login interactivo OAuth2 PKCE contra Entra      │      │
│           │   │   2. Recibe JWT con claims (uid, sinco_roles,         │      │
│           │   │      sinco_obras_asignadas)                           │      │
│           └─◀ │   3. Usa el token en header Authorization para TODAS │      │
│               │      las llamadas (cloud y on-prem)                   │      │
│               └──────────────────┬────────────────────────────────────┘      │
│                                  │                                           │
│                       ┌──────────▼─────────────┐                             │
│                       │ API Inspecciones       │                             │
│                       │ (Container App .NET)   │                             │
│                       │ Valida JWT Entra       │                             │
│                       │ + autoriza por scope   │                             │
│                       └────────────────────────┘                             │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Componentes específicos

**1. Sincronización Sinco → Entra ID**

Pull-based, vía VPN, con un job en Azure (Function timer-triggered o Logic App) que cada N minutos:

- Llama `GET /api/v1/admin/usuarios?desde={lastSync}` en Sinco (endpoint a construir, expone delta).
- Por cada usuario: crea/actualiza/desactiva en Entra usando **Microsoft Graph** o **SCIM**.
- Mapea roles Sinco → grupos Entra (`sinco-tecnicos`, `sinco-ingenieros`, `sinco-supervisores`).
- Mapea obras asignadas → custom directory extensions en el usuario Entra (atributos custom).

**Ventaja del pull**: Sinco no expone nada al internet — todo va por VPN. Solo el endpoint `/admin/usuarios` necesita estar disponible para el sync, y solo desde la VNet Azure.

**2. Claims enrichment en el token**

Cuando el técnico se autentica, el token JWT que Entra emite incluye:

```json
{
  "sub": "user-guid-azure",
  "sinco_user_id": "12345",                    // ID de Sinco
  "name": "Juan Pérez",
  "preferred_username": "juan.perez@cliente.com",
  "sinco_roles": ["tecnico"],
  "sinco_obras": ["obra-id-1", "obra-id-2"],
  "groups": ["sinco-tecnicos"],
  "iss": "https://login.microsoftonline.com/{tenant}/v2.0",
  "aud": "api://sinco-inspecciones",
  "exp": 1234567890
}
```

`sinco_roles` y `sinco_obras` se llenan desde el directorio (sincronizado de Sinco). Esto evita que cada API tenga que ir a buscar autorización a Sinco en cada request — el token la lleva.

**3. Validación en APIs cloud y on-prem**

Tanto el API cloud como los APIs on-prem usan el mismo middleware en .NET:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/{tenantId}/v2.0";
        options.Audience  = "api://sinco-inspecciones";
        options.TokenValidationParameters.ValidateIssuer = true;
        // JWKS se descarga automáticamente de Entra y se cachea
    });
```

Las APIs on-prem necesitan **acceso saliente HTTPS a `login.microsoftonline.com`** (para descargar JWKS la primera vez y refrescar). Esto puede ser un punto de fricción con el equipo de redes Sinco — vale la pena confirmar pronto.

**4. Autorización (más allá de la autenticación)**

Cada endpoint cloud y on-prem valida en el token:

- `sinco_roles` contiene "tecnico" o "ingeniero" para acceder a inspecciones.
- `sinco_obras` contiene el `obraId` del recurso solicitado, o el usuario tiene rol "supervisor" que ve todo.

Si la autorización fina cambia (un técnico se reasigna de obra), el token actual sigue válido hasta vencer (típicamente 1h). El sync periódico actualiza Entra y el próximo refresh trae los claims correctos. Si se necesita revocación inmediata, agregar token revocation o reducir TTL — pero solo si hay caso de uso real.

### Lo que se acepta como trade-off

- **Latencia de cambio de roles/obras**: hasta el próximo refresh del token (≤ 1h). Aceptable.
- **Mantenimiento del sync**: una pieza nueva que monitorear. Mitigación: alertas en Application Insights si el sync falla N veces seguidas.
- **Doble fuente de verdad** (Sinco para la data operativa, Entra para identity en cloud) — bien explícito en el sync, no es problema si está bien gobernado.
- **Salida HTTPS desde on-prem a Microsoft endpoints**: requiere coordinación con redes Sinco. No es excepcional, pero hay que pedirla.

### Lo que falta para cerrar definitivamente el ADR

1. **Confirmar el mecanismo actual de auth en Sinco** — si por casualidad ya hay AD corporativo, **AD Connect / Entra Connect** elimina la necesidad de construir el sync custom: AD se sincroniza nativo a Entra. Esto cambiaría la implementación de sync pero no la decisión.
2. **Confirmar que redes Sinco aprueba salida HTTPS desde on-prem hacia `*.microsoftonline.com` y `*.microsoft.com`** (necesario para validar JWTs en APIs on-prem). Sin esto, las APIs on-prem necesitarían un proxy interno.
3. **Decidir tenant Entra**: ¿se usa el corporativo de Sinco si existe, o se crea uno nuevo dedicado al producto? Implicación: si Sinco vende este módulo a clientes finales en el futuro, conviene tenant separado o multi-tenant Entra.
4. **MFA obligatoria sí/no** en MVP. Recomendación: **sí**, con app authenticator. El módulo trae acceso al taller y a OTs correctivas — vale la pena protegerlo.

### Implicación para el plan

- Agregar **workstream B-6: identity & sync** dentro del workstream B (~2-3 semanas paralelas a los otros).
- Sumar al SOW del consultor: tenant Entra, configuración de App registrations, sync job, configuración de JWT Bearer en todos los APIs.
- El **endpoint `/admin/usuarios`** de Sinco (para el sync) hay que sumarlo al inventario de §9.13. Es responsabilidad del equipo que mantiene el master de usuarios Sinco.

### Si en el futuro Sinco se vuelve más cloud-mature

Esta arquitectura permite evolucionar a:
- B2C/External ID si se vende a clientes finales con marcas distintas.
- Federación inversa si Sinco lanza su propio IdP corporativo y quieren federar.
- MFA condicional por rol/obra/horario sin tocar código.

Nada de eso requiere reescribir lo de arriba.

---

## 9.15 ADR-004 — Sincronización de catálogos de referencia entre Sinco on-prem y módulo Azure (2026-04-27)

**Estado:** Aceptada.

### Contexto

El módulo de inspecciones consume múltiples **catálogos de referencia** que viven en Sinco on-prem: causas de falla, tipos de falla, partes, actividades, ubicaciones, obras, equipos, rutinas, repuestos. Estos catálogos:

- Se cargan **una vez al arranque del proyecto** del cliente.
- Reciben típicamente **1-2 actualizaciones por año** (agregar una causa nueva, renombrar una descripción).
- Son consumidos por la app móvil del técnico **en cada selector** (causa de falla, parte, etc.) — alta frecuencia de lectura.
- Viajan por VPN — cada lectura en vivo tiene costo de round-trip.
- Caen dentro del patrón EDA Sinco §7 ("data de referencia → query síncrona / read model").

### Decisión

**Estrategia híbrida de cinco componentes:**

1. **Sync inicial** al desplegar el módulo y al provisionar nuevos ambientes (dev/staging/prod).
2. **Cron diario nocturno** (~3 AM hora Bogotá) que invoca `GET /api/v1/catalogos/<X>` con headers `If-Modified-Since` o `ETag`. El ERP responde **`304 Not Modified`** si nada cambió desde la última sync — sin transferir body.
3. **Stale-while-revalidate** como patrón de cache: si el cron del día falla o la VPN cae, el técnico sigue viendo la cache anterior; el refresh se completa asíncrono cuando vuelva la red.
4. **Reglas operativas de inmutabilidad** en el lado ERP: los IDs/códigos del catálogo **nunca cambian**; renombrar = cambiar solo descripción; descontinuar = `activa = false`, nunca delete.
5. **Botón admin "refrescar catálogos ahora"** (`POST /api/v1/admin/catalogos/refrescar`) — **diferido a v1.1**. En v1.0 se acepta ventana de hasta 24h entre cambio y propagación.

### Razones

- **Complejidad-vs-beneficio favorable**: para 1-2 cambios/año, sync periódico simple bate al webhook push, y bate también al sync continuo por demanda.
- **Resilencia**: stale-while-revalidate sobrevive caídas temporales de VPN sin impacto al técnico.
- **Costo en runtime mínimo**: cron diario con `If-Modified-Since` retornando `304` es prácticamente gratis — no transfer de body, solo headers.
- **Alineado con guía EDA §7**: query síncrona + read model local es exactamente el patrón recomendado para catálogos.
- **IDs inmutables protegen reproducibilidad histórica** sin overhead de snapshots: una inspección de hace 2 años referencia un ID estable que sigue resolviendo aunque la descripción haya evolucionado.

### Trade-offs aceptados conscientemente

- **Ventana de staleness de hasta 24h**: si agregan una causa nueva al mediodía, el técnico no la ve hasta la próxima noche. Si en la práctica resulta operativamente doloroso, se promueve el botón admin de v1.1 a v1.0 (costo: media tarde de implementación).
- **Sin push real-time** desde ERP. Aceptable dado el bajo volumen.
- **No hay snapshot por inspección**: si una causa se renombra, las inspecciones históricas que la referencian aparecen con el nombre nuevo (no el original al momento de captura). Aceptable; si se necesita reproducibilidad estricta, se promueve a snapshot por inspección.

### Aplica a los siguientes catálogos

Cobertura completa del inventario §12.9.7 del modelo de dominio:

| Catálogo | Endpoint | Frecuencia esperada de cambio |
|---|---|---|
| Equipos (master) | `GET /api/v1/equipos` | Mensual (altas/bajas) |
| Rutinas | `GET /api/v1/rutinas` | Trimestral o menos |
| Partes | `GET /api/v1/catalogos/partes` | Una vez al arranque + raras adiciones |
| Causas de falla | `GET /api/v1/catalogos/causas-falla` | Una vez al arranque + raras adiciones |
| Tipos de falla | `GET /api/v1/catalogos/tipos-falla` | Una vez al arranque + raras adiciones |
| Ubicaciones | `GET /api/v1/catalogos/ubicaciones` | Esporádico |
| Obras | `GET /api/v1/catalogos/obras` | Mensual |
| Repuestos / insumos | `GET /api/v1/insumos` | Variable; adiciones frecuentes |

**Nota**: Repuestos puede tener volumen de adiciones más alto. Si en operación real resulta que el cron diario es insuficiente para repuestos, se reduce su intervalo a 4-6 horas sin tocar los demás. La estrategia es por catálogo.

### Implementación técnica

**Lado ERP (workstream B):**
- Cada endpoint de catálogo soporta `If-Modified-Since` y `ETag`.
- Responde `304 Not Modified` cuando no hay cambios.
- Header `Cache-Control: max-age=86400, stale-while-revalidate=604800`.

**Lado módulo Azure (workstream C):**
- Cliente HTTP con cache estándar (built-in en .NET) que respeta los headers.
- Persistencia en proyecciones Marten (read-only documents) para sobrevivir reinicios del Container App.
- Cron job en Wolverine (timer trigger) que llama cada catálogo a las 3 AM Bogotá.
- Wrapper `IReferenceDataService` que abstrae la lectura: el dominio nunca llama HTTP directo, siempre va por la cache local.

**Lado catálogo admin (Sinco):**
- Documentar en runbook operativo: "después de agregar/modificar un código en el catálogo X, los cambios estarán disponibles en cloud al siguiente refresh nocturno (~3 AM). En v1.0 no hay botón de refresh inmediato."
- Política de cambios: NO modificar IDs/códigos existentes. Solo descripción. Descontinuación con flag `activa`, no delete.

### Reglas operativas para el admin del catálogo (vinculantes)

Estas reglas no son negociables — protegen la integridad de los datos históricos:

1. **Los IDs/códigos del catálogo son inmutables.** Una vez creado `FALLA_LUBRICACION`, ese código no cambia jamás.
2. **Renombrar = cambiar descripción**, mantener código. Si el nombre operativo evoluciona ("Falla de lubricación" → "Lubricación deficiente"), se actualiza el campo descripción y el código sigue.
3. **Descontinuar = marcar inactivo**, no borrar. Los registros históricos siguen referenciando el ID; lo que cambia es que ya no aparece en selectores nuevos.
4. **Adiciones son libres**, sin restricción.

### Camino de evolución

Si la operación demanda mejor responsividad (ej. en operaciones grandes con cambios diarios al catálogo de repuestos):

1. Promover el botón admin de v1.1 a v1.0.
2. Reducir intervalo del cron a 4-6h en catálogos críticos.
3. Si aparece un caso real de "necesito real-time", evaluar webhook push desde ERP — pero solo entonces, no preventivamente.

---

## 10. Preguntas abiertas para la siguiente conversación

**Sobre el preoperacional (acceso confirmado):**
- Compartir el **DDL real de las tablas** que componen una novedad: cabecera, detalles, adjuntos, FKs, claves naturales. Con esto se cierra el contrato del evento `NovedadReportada_v1`.
- ¿Hay restricción para habilitar **CDC** en esas tablas, o se prefiere patrón **Outbox** dentro del preoperacional?
- ¿Qué versión de SQL Server corre el preoperacional? (CDC requiere Standard o superior, y la sintaxis varía).

**Sobre Azure Landing Zone (Sinco aún no la tiene):**
- ¿Quién es el **owner ejecutivo** del programa cloud en Sinco? ¿Existe ya un comité o se va a crear?
- ¿Hay **tenant Entra ID corporativo** existente o hay que crear uno?
- ¿El consultor ya está identificado o el primer paso es selección de Microsoft Partner?
- Política corporativa de datos en cloud: ¿hay alguna ya escrita, o hay que redactarla?

**Sobre el ecosistema Sinco on-prem:**
- ¿Qué **API exponen hoy** MYE núcleo, inventario y ADPRO? ¿REST, SOAP, otro?
- ¿Existe algún patrón estándar de autenticación inter-módulos en Sinco?

**Sobre el catálogo de repuestos e insumos:**
- ¿En qué módulo de Sinco vive (inventario, almacén, MYE)?
- ¿Maneja jerarquía por equipo (repuestos compatibles con cada modelo) o es lista plana?
- Volumen aproximado: ¿miles de SKUs, decenas de miles?

**Sobre el alcance funcional del MVP:**
- ¿Qué **2-3 tipos de inspección técnica** entran primero (motor, hidráulica, post-mantenimiento, certificación)?
- Perfil del **técnico/ingeniero**: ¿uno por obra? ¿flotantes entre obras? ¿cuántas inspecciones por día se esperan?
- ¿Se requiere **offline duro** (días sin sincronizar) o "buen offline" (horas)?

**Sobre normativa y cliente piloto:**
- Cliente piloto y tipo de obra (vial, minera, edificación, hidroeléctrica).
- Normativas aplicables (ISO 45001, normativa minera colombiana, ANI/INVIAS).

---

## Fuentes

- [Top 10 Construction Equipment Inspection Software in 2026 — getclue.com](https://www.getclue.com/blog/top-construction-equipment-inspection-software)
- [Top 5 Equipment Inspection Software in 2026 — GoAudits](https://goaudits.com/blog/equipment-inspection-software/)
- [Construction Equipment Inspection Software for 2026 — HVI](https://heavyvehicleinspection.com/article/best-2026-construction-equipment-inspection-software)
- [Construction Equipment Software Integration Guide — HVI](https://heavyvehicleinspection.com/blog/post/construction-equipment-software-integration-guide)
- [Heavy Equipment Inspection Apps — TrueContext](https://truecontext.com/blog/heavy-equipment-inspection-apps-101/)
- [SafetyCulture Pricing](https://safetyculture.com/pricing)
- [SafetyCulture Software Reviews — Capterra](https://www.capterra.com/p/141080/iAuditor/)
- [Fracttal One Precios y Funciones — Capterra](https://www.capterra.com/p/159911/Fracttal/)
- [Fracttal One Precios — Comparasoftware](https://www.comparasoftware.co/fracttal)
- [Fracttal — Sitio oficial](https://www.fracttal.com/)
- [Tractian — Heavy Equipment CMMS](https://tractian.com/en/industry/heavy-equipment-maintenance-software)
- [Tractian Mobile CMMS App](https://tractian.com/en/solutions/cmms/mobile-app)
- [TRACTIAN Comparasoftware](https://www.comparasoftware.com/tractian-es)
- [IBM Maximo Application Suite Pricing](https://www.ibm.com/products/maximo/pricing)
- [IBM Maximo vs SAP EAM — Facilio](https://facilio.com/blog/ibm-maximo-vs-sap-eam/)
- [Top 5 EAM Software — MaintainNow](https://www.maintainnow.app/learn/guides/top-5-eam-software-compare-ibm-maximo-ifs-sap-oracle-hexagon)
- [B2W Adds API Capabilities — IRONPROS](https://www.ironpros.com/bidding-estimating-tools/article/22908771/b2w-adds-api-capabilities-to-help-heavy-and-civil-contractors-bid-to-win)
- [B2W Software API streamlines Inspections — Highways Today](https://highways.today/2021/09/25/b2w-software-api-inspection-repairs/)
- [Tenna — Equipment Management Software](https://www.tenna.com/)
- [HCSS Fleet Inspection Software](https://www.hcss.com/products/fleet-inspection-software/)
- [Procore — Vista Connector](https://support.procore.com/products/online/user-guide/company-level/erp-integrations/vista/about-viewpoint-vista)
- [Snipe-IT — GitHub](https://github.com/grokability/snipe-it)
- [openMAINT — SourceForge](https://sourceforge.net/projects/openmaint/)
- [Best Open Source CMMS — SoftwareSuggest](https://www.softwaresuggest.com/cmms-software/open-source)
- [Atlas CMMS — Open Source Construction Maintenance](https://atlas-cmms.com/industries/open-source-construction-maintenance-software)
- [Marten — Understanding Event Sourcing](https://martendb.io/events/learning.html)
- [Event Sourcing and CQRS with Marten — CODE Magazine](https://www.codemag.com/Article/2209071/Event-Sourcing-and-CQRS-with-Marten)
- [EventSourcing.NetCore — Oskar Dudycz](https://github.com/oskardudycz/EventSourcing.NetCore)
