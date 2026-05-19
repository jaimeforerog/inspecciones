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
- **Implicación para nosotros:** es la "vara" en UX. Lo que NO ofrece y nosotros sí podríamos: lógica de horas-máquina, costeo por activo en proyecto, integración bidireccional con un ERP específico, eventos de dominio expuestos.

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
- Las **proyecciones** (Marten projections) cubren las vistas que el ERP necesita: estado actual del activo, KPIs de proyecto, próximas inspecciones, defectos abiertos. Cada proyección es un *contrato de lectura* contra el ERP.
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
| Procore (+ Vista) | Gestión de proyecto | Sí | Sí / Limitado | No | Custom | Bajo (no es CMMS puro) |
| openMAINT | Open-source EAM | Limitado | Sí / No | No | Gratis | — |
| ERPNext | ERP open-source | Sí (limitado) | Sí / Sí | Parcial | Gratis / Cloud | — |

---

## 9. Hallazgos accionables para nuestro diseño

1. **El "must have" funcional** que TODOS ofrecen y que no podemos quedar por debajo: checklist configurable, foto, firma, QR/RFID por activo, modo offline, reporte PDF/Excel, OT generada desde defecto, dashboard básico.
2. **Diferenciadores creíbles** dado nuestro stack:
   - Integración **event-driven en tiempo real** con el ERP propio (vs. polling/REST que usa la mayoría).
   - **Trazabilidad de auditoría inmutable** habilitada por event sourcing (vendible como cumplimiento ISO/SST).
   - **Inspección guiada paso a paso** estilo Tractian (SOP forzada), que SafetyCulture y Fracttal no enforce.
   - **Costos por activo y por proyecto** alimentados desde el ERP, mostrados en la app — pocos competidores cierran este loop bidireccionalmente.
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

1. Técnico abre la app → ve **bandeja de novedades pendientes** del preoperacional para los equipos asignados a su proyecto.
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
- Definir **modelo de offline para técnicos**: probablemente más relajado que para operarios pero igual obligatorio en proyectos remotos.

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

El preoperacional vive en SQL Server on-prem (confirmado por Jaime el 2026-05-04) y no expone eventos. Patrón menos invasivo:

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
    public required int NovedadId          { get; init; } // PK estable derivada del registro origen
    public required int EquipoId           { get; init; } // FK a maestro MYE
    public required string EquipoCodigo     { get; init; } // human-readable para UX

    public required int ParteId            { get; init; }
    public required string ParteNombre      { get; init; }

    public required int ActividadId        { get; init; }
    public required string ActividadDescripcion { get; init; }

    public required string Descripcion      { get; init; } // observación del operario
    public string? Severidad                { get; init; } // si el preop la modela
    public string? ValorMedido              { get; init; } // p.ej. presión, nivel
    public string? UnidadMedida             { get; init; }

    public required DateTime ReportadaEn    { get; init; }
    public required string ReportadaPor     { get; init; } // operario
    public required int PreoperacionalId   { get; init; } // a qué reporte pertenece

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
    int NovedadId,           // referencia a la del preop
    Guid InspeccionId,        // qué inspección la verificó
    string Resultado,         // "Confirmada" | "Descartada" | "RequiereSeguimiento"
    string DiagnosticoTecnico,
    string EmitidoPor,        // técnico/ingeniero
    DateTime VerificadaEn);

// Topic: 'inspecciones.repuestos.v1'
public sealed record RepuestoEstimado_v1(
    Guid InspeccionId,
    int EquipoId,
    int SkuId,                  // catálogo de inventario Sinco
    decimal CantidadEstimada,
    string UnidadMedida,
    string Justificacion);

// Topic: 'inspecciones.ot.v1'
public sealed record OTCorrectivaSugerida_v1(
    Guid InspeccionId,
    int EquipoId,
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
- **Offline del técnico**: si el técnico va al proyecto sin señal, su app no llega al API. Mitigación: cuando se conecta, descarga las novedades pendientes de los equipos asignados a su proyecto a una caché local en el dispositivo. Esto es responsabilidad del cliente móvil, no del backend.

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
        → Lista paginada de novedades pendientes de verificar para los equipos del proyecto. (URL del ERP usa "obra"; el módulo lo conoce internamente como "proyecto" — decisión 2026-04-30.)

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
    public int EquipoId { get; private set; }
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
    int ParteId,
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
    Guid InspeccionId, Guid HallazgoId, int NovedadPreopId,
    ResultadoVerificacion Resultado, string Diagnostico,
    string TecnicoId, DateTime VerificadaEn);

// Origen inspector — NUEVO
public sealed record HallazgoDescubierto_v1(
    Guid InspeccionId, Guid HallazgoId,
    int EquipoId, int ParteId,
    string ActividadDescripcion, Severidad Severidad,
    string Descripcion, IReadOnlyList<Guid> AdjuntosIds,
    string TecnicoId, DateTime RegistradoEn);

// Comunes
public sealed record RepuestoEstimadoEnHallazgo_v1(
    Guid InspeccionId, Guid HallazgoId,
    int SkuId, decimal Cantidad, string Unidad, string Justificacion);

public sealed record DiagnosticoEmitido_v1(
    Guid InspeccionId, string Diagnostico, DictamenOperacion Dictamen,
    DateTime EmitidoEn);

public sealed record InspeccionFirmada_v1(
    Guid InspeccionId, string TecnicoId, string FirmaUri,
    DateTime FirmadaEn);

// Salida hacia el resto de Sinco (REST POST, no Service Bus por ADR-001)
public sealed record OTCorrectivaSugerida_v1(
    Guid InspeccionId, int EquipoId, Severidad PrioridadAgregada,
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
       → Detalle del equipo: serial, modelo, marca, proyecto asignado, horómetro, etc.

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
| ~~`GET /api/v1/admin/usuarios?desde={lastSync}`~~ | ~~User master Sinco~~ | ❌ Eliminado 2026-05-05 — identidad del host PWA |

Diez endpoints en **tres módulos distintos** de Sinco (decisión 2026-05-05 elimina el cuarto — User master/seguridad/IT — porque el módulo no maneja identidad).

> **Nota (2026-04-27):** Tras la revisión de plantillas Excel del ERP, este inventario fue **revisado** en `01-modelo-dominio.md §12.8`. La versión vigente cambia: el endpoint `/equipos/{id}/partes` se reemplaza por `/equipos/{cod}` + `/rutinas?grupo=&tipo=` (las partes vienen via los items de la rutina aplicable al grupo del equipo, no asociadas al equipo individual). Se agregan catálogos cerrados de partes, actividades, ubicaciones y proyectos (que el ERP nombra "obras"). Total final: **15 endpoints**. Léase `01-modelo-dominio.md §12.8` para la lista vigente.

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

## 9.14 ADR-002 — Estrategia de autenticación y autorización (2026-04-27 → cerrada 2026-05-19)

**Estado:** **ACEPTADA** (cerrada 2026-05-19 con implementación del slice `mt-1-jwt-claims-pipeline`).

> **DECISIÓN FINAL 2026-05-19:** Inspecciones consume el JWT que la PWA Sinco MYE host propaga en cada request. La validación se delega al middleware corporativo `MiddlewareAuthorizationToken` del paquete `SincoSoft.MYE.Common 1.5.1` (mismo paquete usado por el proyecto hermano Attachment — paridad 1:1). Las 5 claims del contrato (`UsuarioId`, `NomUsuario`, `IdEmpresa`, `IdSucursal`, `IdProyecto`) más una claim adicional `capabilities` (lista de strings) se exponen al resto del módulo a través del puerto `ISessionService` (`Inspecciones.Infrastructure.Auth`). El módulo NO maneja identidad propia, NO sincroniza usuarios, NO tiene app registration propio. Slice de implementación: `slices/mt-1-jwt-claims-pipeline/`. Cierra FU-14 y FU-52.
>
> **Decisión Jaime 2026-05-05 (preservada):** el módulo Inspecciones NO maneja usuarios. Endpoints `GET /api/v1/admin/usuarios` (U-1, U-2) eliminados del contrato. Sin coordinación con equipo Seguridad/IT Sinco. El análisis histórico de opciones A/B/C/D/E (abajo) describe IdPs posibles para el host PWA, no para este módulo.
>
> **Aclaración previa 2026-04-29 (preservada):** Inspecciones es módulo dentro de la PWA Sinco MYE móvil existente y hereda el contexto del usuario del host.

### Implementación cerrada (mt-1, commit `feat(slice-mt-1): ...`)

**Puerto `ISessionService`** (`src/Inspecciones.Infrastructure/Auth/ISessionService.cs`):

```csharp
public interface ISessionService
{
    int IdEmpresa { get; }       // claim canonical "IdEmpresa" (D-MT1-1)
    int IdUsuario { get; }       // claim "UsuarioId"
    string NomUsuario { get; }   // claim "NomUsuario"
    int IdSucursal { get; }      // claim "IdSucursal" (0 si no aplica)
    int IdProyecto { get; }      // claim "IdProyecto" (0 si no aplica)
    IReadOnlyCollection<string> Capabilities { get; }
}
```

**Implementaciones registradas en DI condicional por env:**

- **Producción/Development**: `SincoMiddlewareSessionService` lee `MiddlewareAuthorizationToken.SessionVariables()` del paquete corporativo. Si la claim `IdEmpresa` (PRE-AUTH-3) o `UsuarioId` (PRE-AUTH-4) está ausente, lanza `ClaimRequeridaException` que un middleware global en `Program.cs` mapea a `401 Unauthorized` con body `{ codigoError: "CLAIM-{NOMBRE}-AUSENTE", mensaje: ... }`. Las claims `IdSucursal`, `IdProyecto`, `NomUsuario` son opcionales (default a 0/empty). La claim `capabilities` es manejo especial: si el JWT no la expone, devuelve el set completo (always-allow) hasta que el host confirme el contrato (FU-54).
- **Tests (`ASPNETCORE_ENVIRONMENT=Test`)**: la fixture `InspeccionesAppFactory` registra `TestHeaderAwareSessionService` por default (lee headers HTTP para backward-compat con tests legacy) y permite override por test vía `factory.WithSessionService(new FakeSessionService(...))`. El middleware corporativo NO se monta en env Test — paridad con proyecto Attachment.

**Endpoints HTTP refactorizados (15):** cada uno lee `ISessionService session` por DI en la lambda, valida la capability con `if (!session.Capabilities.Contains("ejecutar-inspeccion")) return Forbidden403("PRE-1", ...);` y construye el `tecnicoId` desde `session.IdUsuario.ToString(CultureInfo.InvariantCulture)` (D-MT1-6 — el dominio sigue tratando el `TecnicoId` como string opaco, sin cambiar el shape de los eventos `_v1` existentes). Headers de simulación (`X-Sin-Capability-Generar-OT`, `X-Sin-Capability-Ejecutar`, `X-Tecnico-Id`) eliminados de los endpoints — vivían solo como mock de tests pre-mt-1.

**Regla dura nueva (CLAUDE.md):** todo endpoint HTTP lee identidad vía `ISessionService`. Prohibido leer `HttpContext.User` o claims directamente en endpoints o handlers. El dominio nunca conoce JWTs — los handlers reciben `ClaimsTecnico` por parámetro.

**Endpoint que gana capability check (cierre FU-52):** `POST /api/v1/catalogos/sync` ahora requiere `ejecutar-inspeccion` o `administrar-catalogos`.

### Followups vivos asociados

- **FU-44** (rola a **mt-3**): propagar el JWT entrante a `MaquinariaErpClient` y a las sagas que disparan llamadas al ERP. Hoy el cliente usa `MaquinariaErpOptions.JwtToken` (token fijo de config). Para sagas que corren fuera de scope HTTP, definir estrategia (token de servicio dedicado vs. extraer JWT del envelope Wolverine).
- **FU-53** (cross-team CI): documentar cómo configurar el credential provider para los feeds Azure DevOps en GitHub Actions (PAT en secret o caché propia del runner) antes del primer merge a `main`. Localmente, el restore funciona con la caché global caliente.
- **FU-54** (cross-team Sergio/David): confirmar si el JWT del host PWA emite la claim `capabilities` (array de strings). Cuando se confirme el contrato, apretar el default de `SincoMiddlewareSessionService.Capabilities` de "always-allow" a "vacío" — esto convierte el "default permisivo" en "default deniegues" y obliga al host a propagar la claim explícitamente.

### Análisis histórico de opciones (preservado para referencia)

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
4. **Sinco on-prem mantiene la verdad sobre quién es usuario válido y qué proyectos/equipos puede ver** — la autorización sigue siendo Sinco; sólo cambia el mecanismo de autenticación.

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
- Sinco mantiene la **verdad sobre roles y proyectos asignados** — eso entra al token vía claims enrichment al momento del login.

### Arquitectura del flujo

```
┌─────────────────────────────  On-Prem Sinco  ────────────────────────────┐
│                                                                            │
│  ┌──────────────────┐        ┌──────────────────────────┐                  │
│  │ BD usuarios      │        │ Servicio "User Sync API" │                  │
│  │ Sinco (master)   │ ─────▶ │ (expone usuarios + roles │                  │
│  │ + roles + proyec │        │  + proyectos vía REST)   │                  │
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
- Mapea proyectos asignados → custom directory extensions en el usuario Entra (atributos custom).

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

Si la autorización fina cambia (un técnico se reasigna de proyecto), el token actual sigue válido hasta vencer (típicamente 1h). El sync periódico actualiza Entra y el próximo refresh trae los claims correctos. Si se necesita revocación inmediata, agregar token revocation o reducir TTL — pero solo si hay caso de uso real.

### Lo que se acepta como trade-off

- **Latencia de cambio de roles/proyectos**: hasta el próximo refresh del token (≤ 1h). Aceptable.
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
- MFA condicional por rol/proyecto/horario sin tocar código.

Nada de eso requiere reescribir lo de arriba.

---

## 9.15 ADR-004 — Sincronización de catálogos de referencia entre Sinco on-prem y módulo Azure (2026-04-27)

**Estado:** Aceptada.

### Contexto

El módulo de inspecciones consume múltiples **catálogos de referencia** que viven en Sinco on-prem: causas de falla, tipos de falla, partes, actividades, ubicaciones, proyectos (que el ERP nombra "obras"), equipos, rutinas, repuestos. Estos catálogos:

- Se cargan **una vez al arranque del proyecto** del cliente.
- Reciben típicamente **1-2 actualizaciones por año** (agregar una causa nueva, renombrar una descripción).
- Son consumidos por la app móvil del técnico **en cada selector** (causa de falla, parte, etc.) — alta frecuencia de lectura.
- Viajan por VPN — cada lectura en vivo tiene costo de round-trip.
- Caen dentro del patrón EDA Sinco §7 ("data de referencia → query síncrona / read model").

### Decisión (canonical 2026-05-05 — confirmada por Jaime)

**Estrategia híbrida de cuatro componentes:**

1. **Sync inicial** al primer login del cliente PWA (poblado completo de los 9 catálogos en IndexedDB local).
2. **Sync delta on-app-open** — cada apertura de la app dispara `GET /api/v1/catalogos/<X>` con header `If-None-Match: "{etag-cliente}"` por catálogo, en paralelo. El ERP responde `304 Not Modified` cuando no hay cambios (cero bytes en wire); `200 OK` con body completo + nuevo `ETag` cuando sí hay cambios. **No hay cron nocturno** (decisión 2026-05-05 — antes Punto 5 refinement, ahora canonical).
3. **Stale-while-revalidate** como patrón de cache: el UI arranca inmediatamente con la cache local sin bloquearse mientras el sync corre en background. Si la app se abre **sin red**, el técnico opera con la última versión cacheada (modo degradado, sin bloqueo). El sync se completa asíncrono cuando vuelva la red.
4. **Reglas operativas de inmutabilidad** en el lado ERP: los IDs/códigos del catálogo **nunca cambian**; renombrar = cambiar solo descripción; descontinuar = `activa = false`, nunca delete.
5. **Botón admin "refrescar catálogos ahora"** (`POST /api/v1/admin/catalogos/refrescar`) — **promovido a v1.0** (decisión 2026-05-05). Útil cuando el ERP admin sabe que acaba de cambiar algo crítico y no quiere esperar a que cada técnico abra su PWA.

### Razones

- **Aprovecha el ciclo natural del técnico**: abrir la app = momento natural de sincronizar. Sin desperdicio en días sin uso.
- **Sin infraestructura de scheduler**: elimina cron Wolverine, backoff intra-noche, timezone hardcoded, alarma operativo dedicada al cron.
- **Resilencia**: stale-while-revalidate sobrevive caídas temporales de VPN sin impacto al técnico (usa último cached).
- **Resetea naturalmente el reloj ITP de iOS** (ADR-008 §6.7) en cada apertura — el heartbeat push deja de ser crítico para la persistencia de catálogos.
- **Costo en runtime mínimo**: response típico = `304 Not Modified` (cero body). Solo descarga real cuando hay cambios efectivos.
- **Alineado con guía EDA §7**: query síncrona + read model local es exactamente el patrón recomendado para catálogos.
- **IDs inmutables protegen reproducibilidad histórica** sin overhead de snapshots: una inspección de hace 2 años referencia un ID estable que sigue resolviendo aunque la descripción haya evolucionado.

### Trade-offs aceptados conscientemente

- **Ventana de staleness = tiempo entre aperturas**: si agregan una causa nueva al mediodía y un técnico no abre la app hasta el día siguiente, no la ve hasta entonces. Mitigación: botón admin "refrescar ahora" (promovido a v1.0).
- **Sin push real-time** desde ERP. Aceptable dado el bajo volumen.
- **No hay snapshot por inspección**: si una causa se renombra, las inspecciones históricas que la referencian aparecen con el nombre nuevo (no el original al momento de captura). Aceptable; si se necesita reproducibilidad estricta, se promueve a snapshot por inspección.
- **Sin alarma proactiva de "ERP caído"** (que el cron nocturno fallido generaba): se compensa con un healthcheck Application Insights independiente (ping cada 5 min al endpoint de health del ERP). Es responsabilidad de Fase 1 (paso 1.12 Application Insights) y no parte del módulo de inspecciones.

### Aplica a los siguientes catálogos

Cobertura completa del inventario §12.9.7 del modelo de dominio:

| Catálogo | Endpoint | Frecuencia esperada de cambio |
|---|---|---|
| Equipos (master) | `GET /api/v1/equipos` | Mensual (altas/bajas) |
| Rutinas técnicas (**M-17**) | `GET /api/v1/catalogos/rutinas` | Trimestral o menos. **Shape mínimo** — sin `Items[]` ni `ActividadId` (ver "Refinamientos posteriores 2026-05-05") |
| Rutinas monitoreo (**M-16**, MVP — promovido 2026-05-05) | `GET /api/v1/catalogos/rutinas-monitoreo` | Trimestral o menos. Shape completo con `Items[]` + `EvaluacionEsperada` (§12.11.5). Cada rutina trae `grupoMantenimientoId` para filtro client-side por grupo del equipo |
| Partes | `GET /api/v1/catalogos/partes` | Una vez al arranque + raras adiciones |
| Causas de falla | `GET /api/v1/catalogos/causas-falla` | Una vez al arranque + raras adiciones |
| Tipos de falla | `GET /api/v1/catalogos/tipos-falla` | Una vez al arranque + raras adiciones |
| Ubicaciones | `GET /api/v1/catalogos/ubicaciones` | Esporádico |
| Proyectos (ERP los nombra "obras") | `GET /api/v1/catalogos/obras` | Mensual |
| Repuestos / insumos | `GET /api/v1/insumos` | Variable; adiciones frecuentes |

**Nota**: Repuestos puede tener volumen de adiciones más alto. La frecuencia de sync = frecuencia de apertura de la app por cada técnico, lo que típicamente cubre cualquier ritmo de cambio. Si emerge un caso real con cambios mid-jornada que el técnico necesita ver, se usa el botón admin "refrescar ahora" (v1.0).

### Implementación técnica

**Lado ERP (workstream B):**
- Cada endpoint de catálogo soporta `ETag` (`If-None-Match`).
- Responde `304 Not Modified` cuando no hay cambios.
- Header `Cache-Control: max-age=86400, stale-while-revalidate=604800` (sirve si el navegador hace caching adicional en algún proxy intermedio; el cliente PWA respeta ETag directamente).

**Lado cliente PWA (workstream A):**
- Bootstrap en `app.tsx` dispara sync de los 9 catálogos en paralelo (uno por catálogo, cada uno con su `If-None-Match: "{etag-cliente}"`).
- Persistencia en IndexedDB (object store `catalogos` por nombre, con `{etag, lastSyncedAt, data}` por catálogo — ver ADR-008 §9.16 para storage cliente).
- UI no se bloquea por el sync — arranca con la cache local; cuando el sync devuelve `200 OK` (cambios), el UI re-renderiza el selector afectado.
- **Sin red al abrir:** UI usa último cached. Banner discreto "modo offline" en la barra superior. Sync se reintenta cuando vuelva la red (ADR-008 cola).

**Lado módulo Azure (workstream C):**
- Sin scheduler. El backend solo sirve los endpoints que el cliente PWA invoca.
- Persistencia en proyecciones Marten (read-only documents) **opcional** — si el backend también necesita resolver IDs (p. ej. para el adapter del PDF), mantiene su propia cache poblada por el mismo patrón ETag, pero esa cache se hidrata bajo demanda al primer uso, no por cron.

**Lado catálogo admin (Sinco):**
- Política de cambios: NO modificar IDs/códigos existentes. Solo descripción. Descontinuación con flag `activa`, no delete.
- Si un cambio operativo es urgente, el admin invoca el botón "refrescar ahora" v1.0 — esto bumpa el ETag server-side y al siguiente sync de cada técnico baja el cambio.

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

### Refinamientos posteriores (2026-05-05)

**Punto 1 — Cobertura explícita de M-17 (rutinas técnicas) y M-16 (rutinas monitoreo — MVP desde 2026-05-05).**

ADR-004 aplica a M-17 (`GET /api/v1/catalogos/rutinas`) con un **shape mínimo**:

```csharp
public sealed record RutinaTecnica(
    int RutinaId,
    string Codigo,                  // "INSP. BULL.MOTOR"
    string Nombre,
    TipoRutina Tipo,                // Tecnica
    string GrupoMantenimiento,      // descriptor
    int ParteId,                    // parte mayor que define el alcance
    string ParteCodigo,             // denormalizado
    DateTime SincronizadoEn);
```

**Sin `Items[]` ni `ActividadId`.** La rutina técnica MVP es **filtro del catálogo de partes** (§12.10.5 del modelo), no checklist navegable. El catálogo de actividades (`Actividad`) viaja por separado bajo este mismo ADR-004 y se consume **solo en lectura** (renderizar la descripción de la actividad heredada cuando un hallazgo viene del preop con `Origen=PreOperacional`). El módulo de inspecciones técnicas no expone selector de actividades — para hallazgos manuales el técnico escribe `ActividadDescripcion` como texto libre (§12.10.3).

Esta clarificación reduce el payload de M-17 de ~5 MB (con items expandidos hipotéticos) a ~500 KB para 4.5K rutinas técnicas — beneficioso para offline en iOS donde la cuota es más conservadora.

**M-16 (rutinas de monitoreo, MVP — promovido 2026-05-05)** cae bajo la misma estrategia ADR-004, con shape **completo**: `RutinaMonitoreo` con `IReadOnlyList<ItemRutinaMonitoreo>` y `EvaluacionEsperada` numérica/cualitativa por item + `grupoMantenimientoId` para filtro client-side por grupo del equipo (§12.11.5 del modelo). En el caso de monitoreo los items **sí** son navegables y forman el checklist que el técnico recorre.

**Cross-references:** §12.10.3-§12.10.5 (Hallazgo sin `ItemRutinaId`, rutina como filtro de partes), §12.11.5 (rutinas de monitoreo — MVP desde 2026-05-05). Followup #10 abierto para limpiar residuo de `Items[]` + `ActividadId` en §12.11.1 del modelo (donde el refinamiento de mayo 2026 reintrodujo el shape viejo por error).

**Punto 2 — Interacción con iOS ITP 7 días (cross-ref ADR-008 §6.7).**

ADR-004 asume cache local persistente entre ciclos nocturnos. En iOS Safari, el ITP (Intelligent Tracking Prevention) borra todo el storage de origen tras 7 días consecutivos sin abrir la PWA — incluyendo catálogos cacheados.

**Consecuencias para el patrón stale-while-revalidate:**

- Tras eviction, no hay nada stale para servir. El cliente debe estar online antes de operar. ADR-004 stale-while-revalidate no aplica.
- El primer sync post-eviction es full payload (200 OK, ~5-15 MB), no delta con 304s. Bandwidth pico esperado.
- Si 30 técnicos iOS regresan simultáneamente tras 7 días, full-syncs concurrentes al ERP/VPN.

**Mitigaciones (mayoría delegadas a ADR-008):**

- Heartbeat push diario para PWAs instaladas mantiene el reloj ITP reseteado (ADR-008 §6.7). Es la mitigación primaria.
- Bootstrap del cliente distingue "cache presente con ETag" vs "cache ausente post-eviction"; en el segundo caso bloquea operación hasta sync online completado.
- Métrica server-side: contar requests sin `If-None-Match` provenientes de técnicos previamente sincronizados → indica eviction iOS reciente.

**No mitigado (gap aceptado):** un técnico iOS que está offline al momento de un eviction (ej. arranca PWA sin red por primera vez tras 7 días) **no puede operar**. Es el gap recurrente de iOS y se acepta hasta que el piloto evidencie pérdida real de productividad — entonces se evalúa Capacitor wrapper (camino de evolución de ADR-008).

**Punto 3 — Política de staleness (parcialmente superseded por Punto 5 / decisión 2026-05-05).**

> ⚠️ La política de reintento del cron, métricas asociadas y manejo de fallos persistentes del cron **ya no aplican** — el cron fue eliminado (decisión canonical 2026-05-05, ver Punto 5 abajo). Lo que **sigue vigente** del Punto 3:

**Política de bloqueo por staleness extrema (vigente):**

- Cuando un catálogo crítico (equipos, rutinas técnicas, rutinas monitoreo, partes) lleva >7 días sin sync exitoso, el cliente **bloquea escritura nueva** (no permite iniciar inspecciones nuevas) hasta que un sync online se complete.
- Razón: a 7 días la cache puede tener IDs renombrados/descontinuados, riesgo de captura sobre datos inválidos. Coincide con la ventana ITP iOS.
- Inspecciones ya iniciadas pueden cerrarse normalmente (la captura usó cache vigente al momento de iniciar; la firma no requiere catálogos refrescos).

**Botón admin "refrescar ahora" — promovido a v1.0 (decisión 2026-05-05).**

Endpoint admin `POST /api/v1/admin/catalogos/refrescar` que bumpa el ETag server-side por catálogo. Útil para que el admin del ERP pueda forzar propagación de un cambio crítico al siguiente sync de cada técnico (sin esperar a que abran la app).

**Punto 4 — ETag canonical, `If-Modified-Since` queda como secundario.**

ADR-004 original menciona ambos headers `If-Modified-Since` y `ETag`. En la práctica ETag es preferible y suficiente:

**Razones:**

- **Inmune al clock skew** entre el cron Azure y el ERP on-prem. `If-Modified-Since` depende de que ambos relojes estén sincronizados al segundo, lo cual no se puede garantizar entre máquinas separadas sin NTP forzado.
- **Cubre bajas y altas** del catálogo, no solo modificaciones. Una fila borrada no tiene timestamp; un ETag (versión o hash) sí captura el estado completo.
- **Implementación más simple en el ERP**: solo expone un header por catálogo, no necesita persistir Last-Modified por fila + agregar.

**Forma del ETag (decisión MVP):**

- Versión incremental por catálogo: `ETag: "v42"`. El ERP mantiene un contador por tabla maestra de catálogo que se incrementa en cada cambio (alta, modificación, baja). Simple, debugeable, naturalmente ordenable.
- Alternativa equivalente: `ETag: "{epoch_max_updated_at}-{count}"` si el ERP no quiere agregar columna de versión. Mismo efecto.

**Contrato HTTP:**

- Cliente envía `If-None-Match: "v42"`.
- ERP responde:
  - `304 Not Modified` si la versión actual del catálogo es "v42" (sin body).
  - `200 OK` con `ETag: "v43"` y body completo si hay cambios.
- Header `Cache-Control: max-age=86400, stale-while-revalidate=604800` se mantiene como en ADR-004 original.

**`If-Modified-Since` queda como secundario:** si el ERP ya lo expone para otros consumidores, el cliente puede enviarlo además del `If-None-Match` — pero no es contrato vinculante. ETag es el único requerido. Si el ERP solo soporta uno, debe ser ETag.

**Punto 5 — Sync on-app-open canonical (confirmado 2026-05-05 por Jaime — antes refinement, ahora integrado a la decisión principal arriba).**

> ⚠️ Esta sección quedaba como refinement; **el 2026-05-05 Jaime confirmó que es la decisión vigente**. El contenido se integró directamente en la sección "Decisión" arriba. Esta sub-sección queda como audit trail histórico del proceso de decisión.

Métricas relacionadas: el cliente envía su `lastSyncedAt` en cada sync; server agrega para reporting via Application Insights. Bloqueo por staleness extrema (>7 días) sigue aplicando (ver Punto 3 vigente).

**Trade-offs aceptados:**

- Carga al ERP concentrada en 8-9 AM (técnicos arrancan jornada). Volumen real esperado: 30 técnicos × 9 catálogos × ~5 KB headers ≈ 1.4 MB total distribuidos en 30 min. Despreciable.
- Latencia al arrancar la app: ~450 ms en estado estable, mitigada por stale-while-revalidate.
- Sin alarma operativa derivada del propio sync — delegada al healthcheck Application Insights.

---

## 9.16 ADR-008 — Almacenamiento local offline cliente PWA y sincronización de comandos (2026-05-04)

**Estado:** Aceptada (firmado 2026-05-04).

### Contexto

ADR-004 cubre lectura de catálogos en cliente con stale-while-revalidate. ADR-006 cubre el **outbox del servidor** para que las llamadas al ERP sobrevivan caídas de VPN. **Gap detectado en sesión 2026-05-04:** no hay decisión documentada sobre cómo el técnico graba trabajo en campo cuando no hay conectividad cliente↔backend del módulo (zonas muertas en obra, túneles, plantas subterráneas, áreas rurales).

El gap toca tres riesgos concretos:

- **Pérdida de captura:** una inspección de 3-4 horas con 20 hallazgos y 80 fotos no puede depender de conectividad continua sin perder trabajo.
- **Bloqueo operativo del preop:** el operador no puede operar el equipo si no logra registrar la novedad — sin offline aquí, una zona muerta detiene producción.
- **Modelo de consistencia:** al ser Event Sourcing con invariantes fuertes (V-F1..V-F8, I-1..I-11, "PuedeOperar incompatible con seguimiento"), las colisiones offline **no son resolubles automáticamente** por CRDTs/last-write-wins — requieren decisión humana.

### Decisión

**El cliente almacena comandos (intenciones), no eventos (hechos).** Los eventos solo los produce el servidor tras validar invariantes en el agregado. El cliente sincroniza enviando comandos, el servidor decide qué eventos persistir en Marten.

#### Diseño en cinco componentes

**1. Storage local en cliente (PWA).**

- **IndexedDB** vía `idb` o `Dexie.js` para datos estructurados (no `localStorage`: 5 MB cap y API síncrona bloqueante).
- **OPFS (Origin Private File System)** para adjuntos cuando esté disponible, **IndexedDB blobs** como fallback. Más eficiente para fotos pesadas (5-10 MB cada una) que IndexedDB plano.
- Tres object stores:

| Store | Contenido | Se sincroniza al server |
|---|---|---|
| `comandos_pendientes` | Cola FIFO por stream, fuente única de sincronización | ✅ Sí |
| `catalogos` | Equipos, rutinas, partes, actividades, causas, tipos falla, repuestos (M-1..M-3b, M-17) con `etag` y `lastSyncedAt` | ❌ No (read-only desde ERP, ADR-004) |
| `read_models` | Vista optimista para el UI durante offline (inspección con sus hallazgos aplicados localmente) | ❌ No (derivada — se reemplaza con versión autoritativa del server cuando llega) |

**2. Forma del comando en cola.**

```
{
  clientCommandId: UUIDv7,        // idempotency key, se reusa al reintentar
  tipo: "RegistrarHallazgo" | "IniciarInspeccion" | ...,
  streamId: Guid,                 // InspeccionId / SeguimientoHallazgoId / etc.
  payload: { ... },
  capturadoEn: timestamp cliente, // reconstruye orden causal
  ubicacionGps: UbicacionGps,     // capturada al emitir, no al sincronizar
  claimsSnapshot: { tecnicoId, capabilities }, // claims al momento de captura
  status: "pendiente" | "enviando" | "confirmado" | "rechazado",
  intentos: int,
  ultimoError: string?
}
```

**3. Sincronización: comando-por-comando, secuencial por stream, paralelo entre streams.**

- Dentro de un mismo `streamId`, los comandos van en orden FIFO (`capturadoEn` ascendente). Imprescindible: cada comando depende del estado dejado por el anterior.
- Entre streams diferentes (otra inspección, otro equipo), las colas avanzan en paralelo.
- **Stop-on-error por stream:** si un comando es rechazado por el agregado (HTTP 422), la cola de **ese stream** se pausa hasta que el técnico decida (corregir y reintentar, o descartar). Las demás colas siguen.

**4. Idempotencia end-to-end.**

- `clientCommandId` (UUIDv7, generado en cliente) viaja como `MessageId` de Wolverine en el header HTTP.
- Wolverine deduplica contra su tabla de mensajes. Reintentos del cliente (tras timeout/5xx) caen en 409 con el resultado original — no doble-escritura.
- Regla CLAUDE.md ("múltiples eventos al mismo stream son atómicos por construcción") se preserva: un comando = un `SaveChangesAsync()` = N eventos atómicos.

**5. Adjuntos en dos fases (alineado con pattern SAS de ADR-005).**

- Fase A (al capturar offline): foto se almacena en OPFS/IDB con `adjuntoTempId: Guid`. Comando del hallazgo viaja con `adjuntoTempId` en payload.
- Fase B (al sincronizar): server recibe comando, devuelve **SAS de upload** + `blobUri` definitivo. Cliente sube directo a Azure Blob, después confirma a server con un comando `ConfirmarAdjunto`.
- Soft delete: si el técnico borra una foto antes de sincronizar, el `adjuntoTempId` se elimina local; si la borra después, viaja un `EliminarAdjunto` que el server reconcilia.

#### Manejo de auth offline

El JWT/session del host PWA Sinco MYE puede expirar mientras el técnico está offline. Dos opciones consideradas:

- **(a) Refresh token de TTL extendido cacheado** — operativamente simple, pero requiere que ADR-002 lo soporte (hoy tentativo).
- **(b) Validación server-side contra `claimsSnapshot` + `capturadoEn`** — el server valida que las claims **eran válidas al momento de captura**, no al momento de sync. Más correcto para offline real (jornadas de 12h+), pero requiere extender ADR-002.

**Decisión:** opción (a) para v1.0 con TTL refresh = 24h. Promover a (b) si emergen jornadas offline >24h en operación real.

#### Cuotas y monitoreo

- `navigator.storage.estimate()` al arranque y cada N comandos. Bloqueo suave al técnico cuando queda <20 % del cupo (típicamente ~60 % del disco libre).
- Una jornada larga puede generar 100 fotos × 5 MB = 500 MB; debe caber holgadamente en cuota de IndexedDB/OPFS.
- Métrica de salud cliente: `comandos_pendientes.count`, `comandos_pendientes.oldestAgeHours`. Si edad >12 h, alertar al técnico ("hay trabajo sin sincronizar de hace mucho").

#### Conflictos y rechazos

| Tipo de respuesta | Acción cliente |
|---|---|
| `200 OK` / `202 Accepted` | Marcar `confirmado`, eliminar de cola, refrescar `read_models` con versión server |
| `409 Conflict` (dedup idempotencia) | Tratar como `confirmado` — ya estaba aplicado |
| `422 Unprocessable Entity` (invariante violada) | Marcar `rechazado`, **STOP de la cola del stream**, mostrar al técnico con razón (V-F# o I-#) |
| `401 Unauthorized` | Pausar cola, intentar refresh token, reanudar |
| `5xx` o timeout | `intentos++`, backoff exponencial (1s, 2s, 4s, 8s, ..., max 60s) |

**Nunca silenciar rechazos.** Es la clase de bug más cara en offline parcial.

### Razones

- **Comandos vs eventos preserva la autoridad del stream.** En este dominio las invariantes no son resolubles automáticamente (`PuedeOperar` incompatible con seguimiento, firmar requiere diagnóstico+dictamen, etc.). El servidor debe ser el único que valida y produce eventos.
- **IndexedDB + OPFS es el stack web estándar para offline-first.** Sin librerías exóticas (PowerSync, ElectricSQL, RxDB) que asumen modelos de consistencia incompatibles con este dominio.
- **`clientCommandId` como `MessageId` aprovecha infraestructura Wolverine ya existente** — la dedup tabla del outbox del servidor (ADR-006) cubre también este caso sin código adicional.
- **Stop-on-error por stream limita el blast radius:** un hallazgo rechazado pausa solo esa inspección, no la jornada completa del técnico.
- **Adjuntos en 2 fases (comando + SAS upload) está ya alineado con ADR-005** — no introduce nuevo patrón, reutiliza el existente.

### Trade-offs aceptados conscientemente

- **iOS Safari no tiene Background Sync API.** En iPad/iPhone, la sincronización solo ocurre cuando la app está abierta. Si los técnicos usan iOS, asumir que deben volver a la app (o mantenerla en foreground) para que la cola se vacíe. Si esto es operativamente inaceptable, requerir Android/Chromium en MVP y reevaluar al haber feedback real.
- **Conflictos requieren intervención humana.** No hay merge automático. Aceptable porque el dominio lo justifica (decisiones técnicas no son merge-friendly).
- **Read models locales son optimistas.** El técnico ve la inspección "tal como él la dejó" antes de que el server valide; si el server rechaza después, hay un momento de "lo veía pero no quedó". UX debe tratar esto explícitamente (badge "pendiente sync" hasta confirmar).
- **Refresh token TTL=24h limita la ventana offline real.** Si un técnico está offline >24h, debe reautenticarse antes de poder sincronizar. v1.1 podría promover a `claimsSnapshot` server-side.
- **Cuota de IndexedDB no es ilimitada.** Si un técnico encadena 3 jornadas offline con muchas fotos, puede saturar antes del sync. La métrica de cuota mitiga; el bloqueo suave es prevención.

### Implementación técnica

**Lado cliente (frontend PWA, workstream A):**

- Wrapper `ICommandQueue` que abstrae IndexedDB (con `idb`/`Dexie`).
- Servicio `OfflineSyncWorker` (Web Worker o Service Worker) con loop: leer cola → POST → manejar respuesta → reintentar.
- Hook `useOptimisticInspeccion(streamId)` que combina `read_models` local + comandos pendientes para UI consistente.
- UI badges: `pendiente` (gris), `enviando` (azul), `confirmado` (verde — auto-desaparece), `rechazado` (rojo, requiere acción).

**Lado servidor (backend módulo, workstream C):**

- Header HTTP `X-Client-Command-Id` mapeado a `MessageId` de Wolverine.
- Dedup automática por Wolverine — handlers no necesitan código extra.
- Endpoint genérico `/comandos/{tipo}` o endpoints específicos por comando (decisión a tomar al entrar en Fase 3).
- SignalR push (ADR-005) para resultados que llegan tras outbox del ERP — el cliente puede correlacionar con `clientCommandId` original.

**Lado infra:**

- Sin cambios en Marten ni en el outbox de Wolverine. El patrón se monta encima.

### Reglas operativas (vinculantes)

1. **El cliente nunca emite eventos.** Si en code review aparece un `Append(Stream, EventoX)` invocado desde la PWA, es bug y se rechaza.
2. **`clientCommandId` es UUIDv7**, generado en cliente, inmutable en reintentos.
3. **`capturadoEn` es timestamp cliente** — el server lo persiste tal cual para reconstrucción causal. La hora del server se usa solo para auditoría (`recibidoEn`).
4. **Adjuntos siempre en 2 fases.** Nunca embebidos en payload del comando.
5. **`ubicacionGps` se captura en el momento del comando** (offline), no al sincronizar — sino la coordenada queda inútil (V-F3 requiere GPS al firmar).
6. **Stop-on-error por stream**, nunca por jornada — un rechazo no bloquea otras inspecciones.

### Aplica a los siguientes comandos

Todos los comandos de escritura del módulo. Cobertura crítica MVP:

| Comando | Stream | Crítico offline |
|---|---|---|
| `IniciarInspeccion` | `InspeccionId` | ✅ |
| `RegistrarHallazgo` | `InspeccionId` | ✅ (alta frecuencia campo) |
| `EliminarHallazgo` | `InspeccionId` | ✅ |
| `AsignarRepuesto` | `InspeccionId` | ✅ |
| `ConfirmarAdjunto` | `InspeccionId` o `SeguimientoHallazgoId` | ✅ (depende de upload SAS) |
| `FirmarInspeccion` | `InspeccionId` | ✅ (terminal del flujo offline) |
| `IniciarSeguimiento` | `SeguimientoHallazgoId` | ✅ |
| `RegistrarPreop` | `NovedadPreopId` | ✅ (bloqueo operativo del operador) |
| `GenerarOT` | `InspeccionId` | ❌ Online-only (jefe de campo en backoffice — sin necesidad offline) |
| `RechazarGenerarOT` | `InspeccionId` | ❌ Online-only |

### Camino de evolución

Si emergen requisitos no cubiertos:

1. **Jornadas offline >24h:** promover `claimsSnapshot` server-side (opción b), requiere cambio en ADR-002.
2. **iOS sin foreground sync no es viable:** evaluar Capacitor/PWA-wrapper que dé Background Sync nativo, o requerir Android.
3. **Conflictos frecuentes que el técnico no entiende:** elaborar UX de "diff preview" antes del sync (mostrar qué cambió en backend mientras estaba offline).
4. **Cuota saturada en práctica:** comprimir fotos cliente-side antes de almacenar, o forzar sync parcial cuando hay red débil intermitente.
5. **Volumen de comandos por stream alto (>1000 hallazgos en una inspección):** evaluar batching en sync (mantener orden, pero un POST con N comandos). No-op para MVP — los volúmenes esperados son <50 hallazgos por inspección.

### Anatomía de una jornada — estructura física en cliente

Para que el modelo "comandos en cola" sea concreto, esta sección muestra cómo se llena el storage local durante una jornada típica del técnico.

#### Definición Dexie de la BD (TypeScript)

```typescript
import Dexie, { Table } from 'dexie';

interface ComandoPendiente {
  clientCommandId: string;       // UUIDv7 — PK
  tipo: string;                  // "RegistrarHallazgo", "FirmarInspeccion", ...
  streamId: string;              // InspeccionId / SeguimientoHallazgoId
  payload: Record<string, unknown>;
  capturadoEn: string;           // ISO timestamp cliente
  ubicacionGps: { lat: number; lon: number; precisionMetros: number; capturadoEn: string };
  claimsSnapshot: { tecnicoId: number; capabilities: string[] };
  status: 'pendiente' | 'enviando' | 'confirmado' | 'rechazado';
  intentos: number;
  ultimoError?: string;
}

interface CatalogoCache {
  tipo: string;                  // "equipos", "rutinas", "partes", ...
  etag: string;
  lastSyncedAt: string;
  data: unknown[];
}

interface ReadModel {
  streamId: string;
  tipo: 'Inspeccion' | 'Seguimiento' | 'NovedadPreop';
  state: unknown;                // proyección local optimista
  baseVersion: number;           // versión del agregado server tomada como base
  tieneCambiosPendientes: boolean;
}

class InspeccionesDB extends Dexie {
  comandos_pendientes!: Table<ComandoPendiente, string>;
  catalogos!: Table<CatalogoCache, string>;
  read_models!: Table<ReadModel, string>;

  constructor() {
    super('inspecciones_offline');
    this.version(1).stores({
      comandos_pendientes:
        'clientCommandId, streamId, status, [streamId+capturadoEn], [status+capturadoEn]',
      catalogos: 'tipo, lastSyncedAt',
      read_models: 'streamId, tipo, tieneCambiosPendientes',
    });
  }
}
```

`[streamId+capturadoEn]` es índice compuesto que habilita lectura FIFO por stream (`db.comandos_pendientes.where('[streamId+capturadoEn]').between(...)`).

#### OPFS para fotos (no en IndexedDB)

Las fotos van al **Origin Private File System** vía `navigator.storage.getDirectory()`. Estructura:

```
/adjuntos_pendientes/
  ├── {adjuntoTempId-1}.jpg     // ~3 MB
  ├── {adjuntoTempId-2}.jpg
  └── {adjuntoTempId-3}.jpg
/adjuntos_subiendo/
  └── {adjuntoTempId-N}.jpg     // movido aquí cuando arranca el PUT a Blob
```

Cuando el upload SAS confirma (`AdjuntoConfirmado_v1` recibido), el archivo se elimina del OPFS.

#### Storyboard real — jornada del técnico

**8:00 — abre PWA por primera vez del día (online).** Catálogos del refresh nocturno ya cargados. Cola vacía.

```
catalogos: 9 registros (equipos, rutinas, partes, causas, tipos-falla, ...)
comandos_pendientes: vacío
read_models: vacío
OPFS: vacío
```

**8:30 — inicia inspección del equipo CARGADOR-EX-201 (a partir de aquí, offline en obra).**

```
comandos_pendientes:
  { clientCommandId: "0193...a4f", tipo: "IniciarInspeccion",
    streamId: "ins-9c2...", status: "pendiente", capturadoEn: "08:30:12",
    payload: { equipoId: 4521, rutinaTecnicaId: 18 }, ... }

read_models:
  { streamId: "ins-9c2...", tipo: "Inspeccion", baseVersion: 0,
    state: { estado: "Iniciada", hallazgos: [], ... },
    tieneCambiosPendientes: true }
```

**9:00 — agrega hallazgo H1 con 2 fotos.**

```
OPFS:
  /adjuntos_pendientes/foto-a1.jpg  (3.2 MB)
  /adjuntos_pendientes/foto-a2.jpg  (2.8 MB)

comandos_pendientes (+1 nuevo):
  { tipo: "RegistrarHallazgo", streamId: "ins-9c2...", capturadoEn: "09:00:43",
    payload: { hallazgoId: "h-3e1...", parteId: 88, causaFallaId: 12,
               descripcion: "Fuga aceite hidráulico cilindro derecho",
               adjuntosTempIds: ["foto-a1", "foto-a2"] }, ... }

read_models actualizado:
  state.hallazgos: [{ hallazgoId: "h-3e1...", descripcion: "...", adjuntos: [pendientes] }]
```

**11:00 — firma.** Total cola en este momento: 4 comandos. Total OPFS: 2 archivos (~6 MB).

```
comandos_pendientes (+1):
  { tipo: "FirmarInspeccion", streamId: "ins-9c2...", capturadoEn: "11:00:08",
    payload: { diagnostico: "...", dictamen: "RequiereIntervencion" }, ... }
```

**11:15 — vuelve la red.** Worker arranca y procesa la cola del stream `ins-9c2...` en orden FIFO:

1. `POST /comandos/IniciarInspeccion` → 200 OK → fila eliminada de `comandos_pendientes`.
2. `POST /comandos/RegistrarHallazgo` → 200 OK + `sasUploadUri-1`, `sasUploadUri-2`. Worker hace `PUT foto-a1.jpg` y `PUT foto-a2.jpg` directo a Azure Blob, después `POST /comandos/ConfirmarAdjunto` × 2 → fotos borradas de OPFS, fila eliminada.
3. `POST /comandos/FirmarInspeccion` → 200 OK → fila eliminada.

`read_models` actualizado vía SignalR push con la `baseVersion` del server. `tieneCambiosPendientes: false`. Jornada cerrada.

#### Persistencia entre cierres de la PWA

Tanto IndexedDB como OPFS **persisten en disco**, no son memoria de sesión. Si el técnico:

- Cierra la pestaña → al reabrir, todo está como lo dejó.
- Reinicia el celular → al reabrir, todo está como lo dejó.
- Pasa una semana sin abrir la app → al abrir, los comandos siguen en cola (con su `intentos` y `ultimoError`), las fotos en OPFS.

Lo único que **no** persiste son las variables JS en memoria (estado de React, etc.). Por eso el bootstrap al arrancar la app es:

```typescript
async function bootstrap() {
  await db.open();                                  // 1. reconectar a IndexedDB

  const inspeccionesActivas = await db.read_models  // 2. reanimar UI desde
    .where('tipo').equals('Inspeccion')             //    proyecciones locales
    .toArray();
  setUIFromReadModels(inspeccionesActivas);

  startSyncWorker();                                // 3. levantar worker; vacía cola si hay red

  refreshCatalogosIfStale();                        // 4. en paralelo, refrescar catálogos >24h
}
```

Se pierde el storage local **solo** si: (a) el usuario limpia datos del browser, (b) el browser hace eviction por presión de cuota agresiva, (c) el usuario desinstala la PWA. Caso (b) es por eso que monitoreamos `navigator.storage.estimate()` y bloqueamos suavemente al técnico al llegar a <20 % libre.

#### Tamaños esperados

| Stream típico | Comandos pendientes | OPFS adjuntos | Bytes IndexedDB |
|---|---|---|---|
| Inspección con 5 hallazgos × 2 fotos | ~10 | 10 archivos × ~3 MB = 30 MB | ~50 KB |
| Jornada de 3 inspecciones | ~30 | ~90 MB | ~150 KB |
| Cache catálogos completos | — | — | ~5-15 MB |

Cuando un comando queda `confirmado`, se elimina inmediatamente — la cola **no es un log histórico**, es trabajo pendiente. El historial autoritativo vive en Marten en el server.

#### Comportamiento por plataforma (iOS vs Android)

PWA es un estándar web, pero **el comportamiento real del storage local difiere significativamente** entre iOS Safari y Android Chrome/Edge. Esto afecta decisiones operativas y mitigaciones del módulo.

**Comparativa:**

| Aspecto | Android (Chrome/Edge/Samsung) | iOS Safari (iPhone/iPad) |
|---|---|---|
| IndexedDB | ✅ Robusto, persistencia confiable | ✅ Funciona (resuelto desde iOS 17.4, marzo 2024) |
| OPFS | ✅ Completo (Chrome 102+) | ✅ Soportado desde iOS 16+ |
| Cuota | ~60 % del disco libre (típicamente GB) | ~1 GB nominal, expandible con `persist()` pero menos garantizado |
| Background Sync API | ✅ Funciona en background | ❌ **NO existe en iOS Safari** |
| Periodic Background Sync | ✅ Chrome/Edge | ❌ NO existe |
| Web Push | ✅ Siempre | ✅ Solo si la PWA está **instalada** (Add to Home Screen), iOS 16.4+ |
| Service Worker | ✅ Robusto | ⚠️ Funciona pero con bugs históricos |
| `navigator.storage.persist()` | ✅ Suele concederse, evita eviction | ⚠️ Suele rechazarse, no garantía |
| Eviction por inactividad (ITP) | ❌ No aplica | ⚠️ **7 días de inactividad → wipe completo del storage de origen** |

**Riesgos específicos iOS:**

1. **Regla de 7 días de inactividad (ITP — Intelligent Tracking Prevention).** Si el técnico no abre la PWA por 7 días consecutivos, Safari **borra automáticamente todo el storage de origen**: IndexedDB, OPFS, localStorage, cookies. Es política deliberada de Apple, no bug. Implicaciones para el módulo: técnico de vacaciones 10 días con comandos pendientes en cola → trabajo perdido sin posibilidad de recuperación cliente-side. Mitigación parcial: PWA instalada (Add to Home Screen) tiene reloj ITP separado y algo más permisivo.
2. **Sin Background Sync.** Ya enunciado en este ADR — la cola **solo se vacía con app en foreground**. Si el técnico cierra la app antes de que vuelva la red, la cola espera al próximo abrir manual.
3. **Cuotas más conservadoras.** Una jornada de 90 MB cabe; un técnico que arrastra 3-4 jornadas sin sincronizar puede topar antes que en Android.
4. **PWA "no instalada" vs "instalada".** En Safari sin instalar (solo abierta como pestaña): storage compartido con Safari general, ITP más agresivo, sin push. **Hay que forzar instalación** como condición operativa.

**Mitigaciones de código:**

```typescript
async function ensurePersistence() {
  // 1. Pedir persistencia explícita (Android la concede, iOS a veces)
  const granted = await navigator.storage.persist();

  // 2. Detectar plataforma y mostrar warnings dirigidos
  const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
  const isStandalone = window.matchMedia('(display-mode: standalone)').matches;

  if (isIOS && !isStandalone) {
    showBanner('Para trabajar offline, instala la app: Compartir → Añadir a pantalla de inicio');
  }

  // 3. Métricas de salud de cola — alertar si se acerca al riesgo iOS
  const oldestPending = await db.comandos_pendientes
    .where('status').equals('pendiente')
    .first();
  if (oldestPending && diasSinSincronizar(oldestPending) > 4) {
    showWarning(
      `Hay trabajo sin sincronizar hace ${dias} días. ` +
      `iOS borra el storage tras 7 días sin abrir la app.`
    );
  }
}
```

**Acciones operativas (vinculantes para el rollout):**

1. **Requisito de instalación PWA** en runbook del técnico — la app debe estar agregada a pantalla de inicio antes de salir a campo.
2. **Heartbeat diario** vía push notification (iOS 16.4+ lo soporta para PWAs instaladas) — abre la app brevemente y dispara sync, mantiene el reloj ITP reseteado.
3. **Alerta de cola estancada** a partir del día 4 sin sync (banner + push si la app no se abre).
4. **Métrica server-side** "técnicos con datos no sincronizados >5 días" — backoffice los contacta proactivamente.
5. **Decisión pendiente:** ¿restringir MVP a Android, o aceptar el riesgo iOS con mitigaciones? Posición default: **aceptar iOS con mitigaciones** y reevaluar si emerge pérdida real de datos.

**Camino de evolución si iOS resulta inaceptable:**

Si en operación real se pierden datos por la regla de 7 días:

1. **Capacitor / PWA-wrapper con shell nativo iOS** — la app sigue siendo React/MUI pero compilada como app nativa, lo que da storage nativo (Core Data / SQLite) sin ITP. Costo: ~2-3 semanas + Apple Developer account ($99/año) + distribución por TestFlight o App Store. La lógica del ADR-008 (cola de comandos, sync, idempotencia) se reutiliza tal cual — Capacitor solo cambia la capa de storage subyacente.
2. **Restringir iOS a uso solo-online** y obligar Android para campo offline. Requiere alineación con cliente piloto.

#### Modos de falla y mitigaciones

Esta matriz cubre los modos de falla por capa del flujo end-to-end y las mitigaciones aplicadas. Documento de referencia operacional — consultar al diagnosticar incidentes y al revisar el diseño en cada slice de Fase 3.

##### Capa 1 — Captura en cliente offline

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| GPS no disponible (interior, túnel) | Captura en background mientras llena wizard; bloqueo solo al firmar (V-F3). UX explícita | Si firma en zona sin GPS, no puede cerrar — aceptado |
| Clock skew del celular (hora desincronizada) | `capturadoEn` cliente + `recibidoEn` server, ambos persistidos. Causalidad por orden de llegada al stream | Skew >24h: server puede rechazar como sospecha de fraude |
| Borrado accidental antes de sincronizar | Soft delete local; si comando original aún no se envió, se cancela sin viajar; si ya se envió, viaja `EliminarHallazgo` con soft delete server-side (regla CLAUDE.md) | — |

##### Capa 2 — Almacenamiento local (IndexedDB / OPFS)

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| `QuotaExceededError` al insertar | Monitor `navigator.storage.estimate()`; bloqueo suave a <20 % libre; compresión JPEG cliente-side antes de OPFS | Técnico que arrastra 4+ jornadas puede saturar |
| Browser eviction agresivo | `navigator.storage.persist()` al bootstrap; métrica server "técnicos sin sync >5 días" para outreach | iOS ITP 7 días — riesgo real, mitigado por heartbeat push y PWA instalada (sección anterior) |
| Crash de la app a mitad de write | IndexedDB es transaccional — el commit es all-or-nothing; al rearrancar la cola está coherente | — |
| Schema Dexie cambió (app actualizada) | Dexie migrations; comandos viejos validados al sincronizar contra schema vigente | Versionar `tipo` (`RegistrarHallazgo_v1` / `_v2`) si se rompen contratos |

##### Capa 3 — Cola y orden FIFO

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| Múltiples workers reordenan envíos del mismo stream | Web Locks API (`navigator.locks.request('inspecciones-sync')`) — solo un worker activo | — |
| `FirmarInspeccion` se envía sin que `RegistrarHallazgo2` se confirmara | Stop-on-error por stream; secuencial dentro del stream | — |
| Comando huérfano (referencia stream que no existe) | Cliente: borrado en cascada al cancelar `IniciarInspeccion`. Server: 404/422 si llega huérfano | — |
| Cola con comando muy viejo (10 días pendiente) | Alerta al técnico desde día 4; métrica server-side | iOS ITP día 7 puede borrar antes de poder enviar |

##### Capa 4 — Transmisión HTTP cliente→server

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| Timeout — comando no se sabe si llegó | **Idempotencia con `clientCommandId`** (UUIDv7 + Wolverine `MessageId`); retry seguro, server devuelve 409 con resultado original | — |
| 401 token expirado | Refresh token TTL=24h. Si expiró: pausa cola, pide re-login | Jornadas >24h sin red: trabajo bloqueado hasta re-login. Camino evolución: `claimsSnapshot` server-side (opción b) |
| 403 claims insuficientes (rol cambió) | Server valida claims al procesar; rechazo con UX clara | — |
| 5xx backend caído | Backoff exponencial 1, 2, 4, 8, ..., 60s | Si supera ventana de 24h con 5xx persistente, técnico ve "no podemos sincronizar — contactá soporte" |
| Body POST cortado a mitad | Idempotencia + retry — TCP/HTTP capa baja garantiza all-or-nothing del POST | — |
| Conexión cae después de procesar pero antes del ack | Server commiteó, cliente reintenta, dedup devuelve 409 con resultado original | — |

##### Capa 5 — Dedup y recepción en server

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| Cliente reenvía después de TTL del envelope expirado | Wolverine envelope TTL=30 días en MVP. **Defensa en profundidad:** handlers usan claves de negocio (`HallazgoId`, `AdjuntoId` — generados en cliente y viajan en payload) — el aggregate rechaza duplicados aunque envelope haya expirado | Casos >30 días requieren disciplina en handlers; comandos sin clave natural pueden colar duplicado |
| `MessageId` colisiona accidentalmente (UUID colisión) | UUIDv7 — probabilidad de colisión despreciable (~10⁻³⁶) | — |

##### Capa 6 — Invariantes en handler

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| V-F# / I-# violada (firma sin GPS, dictamen incompatible con seguimiento, etc.) | 422 con código de invariante; stop-on-error por stream; UX muestra al técnico qué falló | — |
| Conflicto con eventos ocurridos durante el offline (otro firmó antes) | 422; cliente hace catch-up via pull al reconectar; banner "esto cambió mientras estabas offline" | El técnico puede tener que re-decidir o descartar trabajo |
| Race condition entre dos handlers concurrentes en el mismo stream | Marten `AppendOptimistic` — concurrencia optimista. Segundo handler recibe `ConcurrencyException`, Wolverine retry automático con versión actualizada | — |

##### Capa 7 — Persistencia atómica (Marten + Wolverine)

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| Postgres rechaza commit (deadlock, constraint) | Wolverine retry; si persiste → dead letter `wolverine.dead_letter` | Operativo: alerta de DLQ en runbook |
| Proceso crashea entre `Append` y `SaveChanges` | Nada commiteado; Wolverine reentrega; handler corre de nuevo (idempotente porque envelope tampoco se guardó) | — |
| Proceso crashea después de `SaveChanges` pero antes del 200 | Cliente reintenta; dedup devuelve 409 con resultado original | — |

##### Capa 8 — Adjuntos (SAS upload a Blob)

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| SAS expira antes de upload (red lenta) | TTL SAS = 1h. Cliente solicita `RefrescarSasUpload` si expira; server valida que blob no exista aún | — |
| Upload parcial cortado | Azure Block Blob soporta resume con misma SAS si TTL vigente; si no, refresh | — |
| `ConfirmarAdjunto` enviado pero foto nunca llegó al Blob | Server hace HEAD al Blob antes de aceptar `ConfirmarAdjunto`; rechaza si no existe | — |
| Foto subida pero `ConfirmarAdjunto` nunca llega (cliente murió) | Cron diario de limpieza: blobs huérfanos sin `AdjuntoConfirmado_v1` referenciándolos en >24h se borran | Costo de storage marginal |
| Cliente perdió la foto local antes de subir (eviction iOS) | **Trabajo perdido sin recuperación** — la foto solo vivía en OPFS | Mitigación: heartbeat agresivo iOS |

##### Capa 9 — Outbox al ERP (cubierto por ADR-006)

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| ERP rechaza permanentemente (4xx) | Dead letter Wolverine; alerta backoffice; `M-1 GenerarOT` con UI de reintento manual (técnico) | — |
| ERP éxito pero sin confirmación | Idempotency-Key (ADR-003); MYE debe aceptar replay con misma key | Requiere disciplina en MYE — riesgo de duplicado si MYE no respeta idempotencia |
| VPN caída prolongada | Outbox sigue acumulando; al reconectar drena en orden | Si dura días, dashboard de outbox con alarma |

##### Capa 10 — Notificación de retorno (SignalR push)

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| Cliente desconectado cuando llega push | **SignalR no es persistente** — al reconectar hace pull explícito (`GET desde-version={baseVersion}`) | Banner "novedades desde tu última sync" |
| Hub SignalR caído | Cliente cae a polling cada 30s sobre el endpoint REST de status del comando | Latencia mayor pero correctitud preservada |

##### Capa 11 — Reconciliación cliente

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| Read_model local divergió del server (alguien editó por API directa) | Bootstrap hace pull autoritativo; cada respuesta trae versión actual; cliente compara y refresca si discrepa | — |
| Comando confirmado pero respuesta no actualiza UI | Cliente persiste status `confirmado` y refresca read_model en próximo bootstrap si push perdido | — |

##### Capa 12 — Plataforma específica

| Modo de falla | Mitigación | Gap reconocido |
|---|---|---|
| **iOS ITP 7 días — wipe completo** | Push heartbeat diario; PWA instalada obligatoria; alerta cliente día 4; métrica server "sin sync >5 días" | **No mitigable 100 % web-side** — camino evolución: Capacitor wrapper |
| iOS sin Background Sync | Sync solo con app abierta; UX explícita | Aceptado: técnico debe abrir app para drenar |
| Cuota iOS más conservadora | Compresión JPEG agresiva en iOS específicamente | — |

##### Patrones de mitigación transversales

Cinco mecanismos cubren la mayoría de los modos de falla anteriores:

1. **Idempotencia end-to-end** — `clientCommandId` UUIDv7 → Wolverine `MessageId` → claves de negocio en handlers (`HallazgoId`, `AdjuntoId`). Defiende contra retries, dedup expirado y crashes parciales.
2. **Atomicidad transaccional** — un solo `SaveChangesAsync` commitea eventos + envelope + outbox + proyecciones inline. Regla CLAUDE.md no negociable.
3. **Stop-on-error por stream** — aísla problemas a un stream sin bloquear la jornada del técnico ni otros streams.
4. **Pull al bootstrap + push en vivo** — SignalR no es source of truth, solo optimización. Catch-up vía REST garantiza eventual consistency aunque se pierdan pushes.
5. **Defensa en profundidad** — cada capa asume que la anterior puede fallar: cliente reintenta, server deduplica, handlers validan claves de negocio, blob hace HEAD antes de confirmar, cron limpia huérfanos.

##### Gaps documentados sin mitigación al 100 %

Estos gaps son aceptados conscientemente para v1.0 y tienen camino de evolución claro:

- **iOS ITP 7 días.** Mitigaciones reducen incidencia pero no eliminan riesgo de pérdida de cola. Camino: Capacitor wrapper iOS.
- **Jornadas offline >24h.** Refresh token expira; trabajo bloqueado hasta re-login online. Camino: `claimsSnapshot` server-side (opción b del manejo de auth offline).
- **Foto perdida en OPFS antes de subir.** No recuperable. Mitigación parcial: UI muestra adjunto pendiente y técnico puede volver a tomar foto.
- **MYE no respeta Idempotency-Key.** Riesgo de duplicado en ERP si MYE no implementa correctamente ADR-003. Fuera de control del módulo — requiere disciplina del equipo MYE.

### Diagrama interactivo

Ver [`09-adr-008-offline-cliente.html`](09-adr-008-offline-cliente.html) — flujo cliente offline → cola → sync → eventos en Marten + estados de un comando + flujo de adjuntos en 2 fases + anatomía de una jornada.

---

## 9.17 ADR-009 — Multi-tenancy Marten conjoined por `IdEmpresa` (2026-05-19)

**Estado:** **ACEPTADA** como decisión arquitectónica y **enforcement implementado** en mt-2. Sub-track multi-tenancy: mt-1 y mt-2 cerrados el 2026-05-19; mt-3 (JWT propagation a ERP) y mt-4 (smoke E2E + observabilidad) pendientes antes del piloto.

### Contexto

Inspecciones es un módulo embebido en la PWA Sinco MYE que sirve a múltiples empresas (clientes finales de Sinco). Cada empresa tiene sus propios proyectos, equipos, inspecciones, hallazgos y catálogos. El JWT del host PWA propaga `IdEmpresa: int` (decisión D-MT1-1 de mt-1) en cada request — el módulo lo recibe vía `ISessionService.IdEmpresa` ([ADR-002 §9.14](#914-adr-002--estrategia-de-autenticación-y-autorización)).

La pregunta: **¿cómo se persiste y consulta la data por-empresa en Marten?**

### Opciones evaluadas

| Opción | Marten setup | Pro | Con |
|---|---|---|---|
| **A. Multi-DB (database-per-tenant)** | Una DB Postgres por empresa | Aislamiento físico fuerte; backups independientes | Costo Azure × N empresas; provisioning operativo complejo; migraciones N veces; cross-tenant queries imposibles |
| **B. Multi-schema (schema-per-tenant)** | Schema Marten distinto por empresa | Aislamiento lógico fuerte; migraciones agrupables | Marten tiene soporte experimental; switching de schema por request requiere wiring custom |
| **C. Marten `Conjoined` (tenant_id discriminado en cada tabla)** | Tablas únicas con columna `tenant_id` indexada | Soporte nativo Marten; query filter automático por session; un único schema migrado | Riesgo si el filter no se aplica (bug = leak cross-tenant); índice por `tenant_id` indispensable |
| **D. Application-level filtering (sin tenancy en Marten)** | Cada query incluye `WHERE IdEmpresa = ?` manualmente | Cero overhead Marten | Frágil — un solo query sin el WHERE es un leak; revisión humana en cada slice |

### Decisión

**Opción C — Marten `Conjoined` con `tenant_id` derivado de `ISessionService.IdEmpresa`.**

Razones:

1. **Soporte nativo Marten 7**: `StoreOptions.Policies.AllDocumentsAreMultiTenanted()` y `session.ForTenant(idEmpresa)` activan el filter automático en lectura y escritura. Cada documento persiste con `tenant_id` y cada query lo agrega al WHERE implícitamente.
2. **Single migration path**: una sola `inspecciones` schema, una sola migración por cambio. El operacional es idéntico al actual.
3. **Cost-effective**: una DB Postgres compartida (Azure Database for PostgreSQL Flexible) escala con índices y particionamiento. No hay overhead de provisioning por empresa.
4. **Compatible con event store**: los streams `Inspeccion` también se persisten con `tenant_id` — el `IDocumentSession.Events.AggregateStreamAsync<T>(streamId)` filtra automáticamente.
5. **D5 firmado por el usuario (mt-1 spec §0.D5)**: todos los catálogos son por-empresa (sin excepciones single-tenant), siempre que Marten lo permita. Conjoined lo permite uniformemente.

### Arquitectura del flujo (post-mt-2)

```
HTTP Request
   ↓
ISessionService.IdEmpresa = 7    (lee del JWT del host)
   ↓
Endpoint reads session.IdEmpresa
   ↓
Handler.ManejarAsync(cmd, claims, ct)
   ↓
IDocumentSession session = store.LightweightSession(tenantId: idEmpresa)
       (factory wired en DI usando ISessionService — agregado en mt-2)
   ↓
session.Events.AggregateStreamAsync<Inspeccion>(...)
       (Marten aplica tenant_id=7 automáticamente)
session.Events.Append(streamId, evento_v1)
       (Marten persiste con tenant_id=7)
   ↓
session.SaveChangesAsync()
   ↓
HTTP Response
```

### Slices del sub-track multi-tenancy

| Slice | Estado | Foco |
|---|---|---|
| **mt-1** — JWT claims pipeline | ✅ Cerrado 2026-05-19 | `ISessionService` puerto + cableado en 15 endpoints + ADR-002 cerrado + ADR-009 creado |
| **mt-2** — Marten conjoined | ✅ Cerrado 2026-05-19 | `StoreOptions.AllDocumentsAreMultiTenanted()` + `Events.TenancyStyle = Conjoined` activos. Puerto `ITenantedDocumentSessionFactory` introducido, `IDocumentSession`/`IQuerySession` scoped delegados al factory. Listeners Wolverine reciben `Envelope` y propagan `TenantId` al puerto. Tests cross-tenant isolation cubren aggregate, catálogos y `CatalogoSyncState`. Reset schema dev confirmado en fixture, SQL backfill staging preparado (FU-58 ops). |
| **mt-3** — JWT propagation a ERP | ⏳ Pendiente | Propagar JWT entrante a `MaquinariaErpClient` (FU-44). Decidir estrategia para sagas que corren fuera de scope HTTP (token de servicio dedicado vs. capturar JWT en envelope Wolverine). Agrupable con FU-57 (`DescartarNovedadPreopErpListener` logs con tenant). |
| **mt-4** — Smoke E2E + observabilidad | ⏳ Pendiente | Tests E2E con 2 tenants concurrentes, asserts de no-leak, métricas App Insights por `IdEmpresa`. Cierra FU-56 (validar Wolverine 3 prefiere overload tenant-aware) y FU-59 (test rebuild cross-tenant defensivo). |

### Decisiones secundarias firmadas (spec mt-1)

- **D2 — `Conjoined`** (no schema-per-tenant ni DB-per-tenant).
- **D3 — ETag de catálogos en envelope** (no bump de evento `_v1`). Razón: los catálogos pre-tenancy ya están persistidos con shape actual; el `tenant_id` se agrega a la fila sin tocar el shape del evento.
- **D4 — Reset del schema en dev, backfill en staging**, sin migración cross-empresa. Razón: el módulo está en pilotaje, no hay data prod por-empresa que migrar.
- **D5 — Todos los catálogos son por-empresa** (sin excepciones single-tenant), siempre que Marten lo permita. Decisión Jaime 2026-05-19.

### Riesgos

- **Riesgo principal**: si el `IDocumentSessionFactory` deja de filtrar por `tenant_id` (bug de wiring), un endpoint puede leer/escribir cross-tenant. Mitigación:
  - Test E2E con 2 tenants concurrentes que escriben + leen distinto `InspeccionId` con mismo número de stream — Marten Conjoined garantiza que `AggregateStreamAsync` con tenant_id distinto retorne null.
  - Code review focus: cada `LightweightSession()` debe venir del factory inyectado (no directo del `IDocumentStore`).
  - Métrica de observabilidad: cardinalidad de `tenant_id` por endpoint en App Insights — pico no esperado sugiere wiring incorrecto.
- **Riesgo secundario**: PR del ADR sin tests cross-tenant ⇒ el bug se descubre en producción. Mitigación: mt-4 establece la baseline de testing antes del piloto.

### Followups

- **FU-55** (abierto desde mt-2): documentar cómo invalidar la caché Marten al cambiar `tenant_id` mid-session. No emergió en mt-2 (cada request abre sesión fresca); placeholder defensivo para sagas con context-switch.
- **FU-56** (abierto desde mt-2): validar en mt-4 con smoke E2E real que Wolverine 3 prefiere la overload tenant-aware del listener (`HandleAsync(evento, Envelope, ct)`) sobre la legacy. Si no, remover la legacy.
- **FU-57** (abierto desde mt-2): propagar `Envelope.TenantId` a logs estructurados de `DescartarNovedadPreopErpListener`. Agrupable con mt-3.
- **FU-58** (abierto desde mt-2): ejecutar SQL backfill staging post-merge. Documentado en `slices/mt-2-marten-conjoined-tenancy/green-notes.md §"SQL backfill staging"`.
- **FU-59** (abierto desde mt-2): test defensivo de rebuild cross-tenant del aggregate. Cabe en `Domain.Tests`, valida que `Apply` no introdujo lógica tenant-aware accidental.
- **FU-44** (vivo desde el sub-track ERP): propagación JWT al cliente HTTP del ERP. Rola a mt-3 según decisión D-MT1-10.

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
- ⚠️ **Pregunta superseded** (decisión 2026-04-27, §12.10/§12.11 del modelo): el MVP usa una rutina técnica única por grupo de mantenimiento, sin subdivisión por enfoque (motor / hidráulica / post-mantenimiento). Pregunta vigente: ¿el alcance de "una rutina por grupo, sin subdividir" se sostiene en operación real, o emerge necesidad de "contexto de inspección" como cambio aditivo?
- Perfil del **técnico/ingeniero**: ¿uno por proyecto? ¿flotantes entre proyectos? ¿cuántas inspecciones por día se esperan?
- ¿Se requiere **offline duro** (días sin sincronizar) o "buen offline" (horas)?

**Sobre normativa y cliente piloto:**
- Cliente piloto y tipo de proyecto (vial, minero, edificación, hidroeléctrico).
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
