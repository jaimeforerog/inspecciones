# Brief para el consultor de producto

**Para:** ingeniero mecánico / consultor de mantenimiento que va a validar el proceso de inspección técnica
**De:** equipo de producto Sinco MYE
**Fecha:** 2026-04-27
**Tu rol:** validar que el proceso modelado refleja la realidad operativa del taller y de proyecto. Identificar vacíos, supuestos incorrectos, oportunidades de mejora.

---

## 1. Qué te pedimos

Estamos diseñando un módulo nuevo dentro de la app móvil de **Sinco MYE** llamado **Inspecciones técnicas**. Lo va a usar **el técnico o ingeniero de mantenimiento** cuando inspecciona maquinaria pesada (excavadoras, retroexcavadoras, bulldozers, grúas, volquetas, etc.).

Antes de codificarlo te pedimos que revises:

1. **Si el proceso modelado refleja lo que realmente hace un técnico** en campo o taller.
2. **Si los catálogos y la nomenclatura son operativamente correctos** (causa de falla, tipo de falla, partes, severidad).
3. **Si las reglas de negocio que aplicamos** (qué obliga a qué, qué se permite, qué no) tienen sentido mecánico.
4. **Qué le falta al proceso** para ser útil en una operación real.
5. **Recomendaciones para tipos de inspección a priorizar** en el primer release.

No necesitas saber nada de programación, Azure, ni bases de datos. Lo que ves es el proceso de negocio.

---

## 2. Cómo se inserta este módulo

La app de Sinco MYE ya tiene un módulo de **Preoperacional** que el operador o conductor de la máquina ejecuta cada turno: marca lo que ve raro y reporta novedades. Es rápido, es de superficie.

El módulo **Inspecciones técnicas** es complementario:

|   | Preoperacional (existe) | Inspección técnica (nuevo) |
|---|---|---|
| Quién la hace | Operario / conductor | **Técnico o ingeniero** que conoce el equipo |
| Frecuencia | Cada turno | Programada / a demanda |
| Profundidad | Reporte rápido de novedades | Diagnóstico técnico, mediciones, identificación de causa raíz |
| Dónde se hace | Pie de proyecto, antes de operar | Taller / centro de operaciones / pie de máquina |
| Output principal | "Aquí pasa algo raro" | "Esto es lo que pasa, esta es la causa, así se arregla, estos son los repuestos" |

El técnico tiene **dos fuentes de hallazgos** durante su inspección:

1. **Las novedades que el operario ya reportó** en el preoperacional. Las verifica una a una.
2. **Lo que el técnico descubre por su cuenta** al revisar el equipo, aunque el operario no lo haya reportado.

Ambas fuentes terminan generando hallazgos que se manejan igual.

---

## 3. Modelo conceptual del proceso

### 3.1 Conceptos centrales

**Equipo** — la máquina específica (ej. *Caterpillar D11T Custom, placa HD-9908-TX, código M-109*). Tiene un **grupo de mantenimiento** (BULLDOZER, EXCAVADORA, etc.).

**Rutina** — formato estándar de inspección que aplica a un grupo. Es una lista fija de cosas a revisar. Por ejemplo, la rutina técnica de motor para BULLDOZER puede tener: revisar nivel de aceite, verificar fugas, medir presión, etc. Hoy en Sinco existen rutinas preoperacionales y de mantenimiento; este módulo agrega rutinas técnicas que comparten la misma estructura.

**Item de rutina** — una pareja **(Parte, Actividad)** dentro de la rutina. Por ejemplo: `(MOTOR, VERIFICACION ESTADO)` o `(TRANSMISIÓN, COMPLETAR NIVEL)`. La rutina son los items.

**Hallazgo** — algo que el técnico considera que merece atención. Puede salir de:
- Verificar una novedad del preop (origen "Preoperacional")
- Descubrirlo el propio técnico (origen "Inspector")

**Repuestos / insumos** — lo que se requiere para arreglar el hallazgo, si aplica. Se eligen del catálogo de inventario.

**Diagnóstico final** — texto resumido que el técnico escribe al cerrar la inspección.

**Dictamen de operación** — decisión del técnico al cerrar. Tres niveles: **Puede operar** / **Con restricción** / **No puede operar**.

**OT correctiva** — orden de trabajo que se genera **automáticamente en Sinco MYE** cuando hay hallazgos que requieren intervención formal. Lleva la lista consolidada de repuestos.

### 3.2 Catálogos cerrados que se usan

Estos son catálogos del ERP — el técnico no inventa entradas, solo elige.

| Catálogo | Ejemplos | Existe en Sinco |
|---|---|---|
| Grupo de mantenimiento | BULLDOZER, EXCAVADORA, GRUA, VOLQUETA, VEHICULO | Sí |
| Parte | MOTOR, TRANSMISIÓN, HIDRÁULICO, FRENOS, LLANTAS, ELÉCTRICO, CHASIS… | Sí |
| Actividad | VERIFICACION ESTADO, CAMBIO DE REPUESTO, COMPLETAR NIVEL, VERIFICAR FUGA… | Sí (usado por el preoperacional). En hallazgos manuales del módulo de inspecciones técnicas, la actividad se escribe en texto libre, no del catálogo. |
| Causa de falla | DESGASTE_NORMAL, FALLA_LUBRICACION, FATIGA_MATERIAL, OPERACIÓN_INCORRECTA… | Sí (a confirmar contigo si los códigos cubren bien) |
| Tipo de falla | MECÁNICA, HIDRÁULICA, ELÉCTRICA, NEUMÁTICA, ESTRUCTURAL… | Sí (a confirmar contigo) |
| Repuestos / insumos | Filtro de aceite, ACPM, empaquetadura, etc. | Sí |
| Ubicaciones | Patios, talleres, frentes de proyecto | Sí |
| Proyectos | Los proyectos donde Sinco gestiona maquinaria (el ERP los nombra "obras" — el módulo usa "proyecto" desde 2026-04-30, followup #4) | Sí |

Si descubres que **falta** algo en un catálogo, no lo creas tú — escala al admin del catálogo de Sinco. En el módulo, lo que no encaja queda como **observación de campo libre**.

**Reglas operativas del catálogo** (vinculantes, protegen los datos históricos):

- Los **códigos del catálogo son inmutables**. Una vez creado `FALLA_LUBRICACION`, ese código no cambia jamás.
- Si una causa o tipo necesita renombrarse: se cambia la **descripción**, el código sigue siendo el mismo.
- Si una entrada se descontinúa: se marca como **inactiva**, nunca se borra. Las inspecciones históricas siguen referenciándola.
- Agregar entradas nuevas es libre.

Esto importa para tu validación: si encuentras que el catálogo actual tiene códigos confusos o duplicados, vale la pena marcarlo ahora **antes de que entren a producción** — porque después no se pueden corregir, solo deprecar.

---

## 4. El flujo paso a paso del técnico

Imagina al ingeniero Juan que llega al taller a las 9:30 a.m. Va a inspeccionar el bulldozer Caterpillar D11T. Esto es lo que hace en la app:

### Paso 1 — Abre la app y entra al módulo

Ve la lista de módulos de Sinco MYE: Preoperacional, Estado de equipos, Aprobaciones, Agenda, Combustible, **Inspecciones**. Toca Inspecciones.

### Paso 2 — Ve sus inspecciones recientes / en curso

Una bandeja con las inspecciones del técnico: las que tiene en curso (no firmadas), las que cerró hoy, las cerradas en la semana. Cada una muestra: el equipo, el estado/dictamen, la rutina aplicada, y si aplica el código de OT generada.

En la barra inferior hay un botón prominente **"+ Iniciar inspección"**. Lo toca.

> **Decisión MVP**: en esta primera versión no hay programación previa de inspecciones. El técnico llega a planta y decide qué inspeccionar en el momento. Programación con asignación previa por supervisor se difiere a versión posterior.

### Paso 3 — Selecciona equipo

Ve los equipos disponibles en sus proyectos (filtrados automáticamente por los proyectos que tiene asignados, traídos del JWT). Los equipos con novedades preop pendientes aparecen destacados.

Selecciona el bulldozer Caterpillar D11T → tap en el equipo. **La rutina técnica se deriva automáticamente del grupo del equipo** (BULLDOZER tiene una sola rutina técnica que cubre todas las partes aplicables; no hay subdivisiones tipo "inspección de motor" vs "inspección hidráulica").

La inspección queda **en ejecución** y se le presenta el equipo con su contexto (horómetro, ubicación, novedades preop pendientes).

> **Decisión MVP**: una rutina técnica por grupo de mantenimiento. Si en el futuro aparece la necesidad de distinguir contextos (post-mantenimiento, certificación periódica, etc.), se reabre como cambio aditivo.

> **Pregunta para ti**: ¿el técnico debería poder inspeccionar cualquier equipo de sus proyectos, o solo los que tienen novedades pendientes / vencimiento de rutina próximo? El MVP es permisivo (cualquier equipo de los proyectos). Si conviene restringir, lo ajustamos.

### Paso 4 — Ejecuta la rutina y/o registra hallazgos

Aquí está el corazón. La pantalla principal de la inspección muestra **dos botones grandes**:

- **+ Agregar hallazgo** — el técnico vio algo que vale la pena registrar (origen Manual).
- **📁 Importar desde preoperacional** — abre la lista de novedades pendientes que dejó el operario; las verifica una a una.

Por debajo, va apareciendo la lista de hallazgos ya registrados con su severidad codificada por color (verde/naranja/rojo).

> **Pregunta para ti**: ¿esto te parece operativamente bien? ¿O preferirías ver primero el listado de items de la rutina técnica como SOPs paso a paso (estilo guiado), aunque la rutina sea opcional?

### Paso 5 — Para cada hallazgo: wizard de 1 o 2 pasos

Cuando el técnico abre un hallazgo (sea ad-hoc o desde una novedad preop), llena un formulario en wizard.

**Paso 1 (siempre):**
- **Parte del equipo** — dropdown del catálogo (MOTOR, TRANSMISIÓN, etc.), filtrado por la rutina técnica del alcance.
- **Actividad** — comportamiento condicional según origen del hallazgo:
  - Si el hallazgo es **manual** (origen Manual): **texto libre** que el técnico escribe (ej. "Holgura en valvulería"). NO se elige de catálogo.
  - Si el hallazgo viene de **verificar una novedad del preoperacional**: la actividad viene heredada del catálogo del preop (ej. "VERIFICACION ESTADO"). El técnico puede dejarla o cambiarla a otra del catálogo si decide reclasificar.
- **Novedad técnica** — texto libre describiendo lo que ve / diagnostica.
- **Acción requerida** — TRES opciones, esta es la decisión clave:
  - 🟢 **No requiere intervención** — "El hallazgo se registra, pero no requiere acción correctiva inmediata."
  - 🟠 **Requiere seguimiento** — "Se debe monitorear la condición. No es urgente pero requiere atención."
  - 🔴 **Sí requiere intervención** — "Se requiere acción correctiva formal. Puede derivar a orden de trabajo."
- **Observación de campo** — texto libre opcional

**Paso 2 (solo si eligió "Sí requiere intervención"):**
- **Acción correctiva** — texto describiendo qué hay que hacer
- **Causa de la falla** — dropdown del catálogo
- **Tipo de la falla** — dropdown del catálogo
- **Insumos requeridos** — uno o varios. Por cada insumo: tipo + cantidad

Si eligió "no requiere" o "seguimiento", el wizard termina en el paso 1. **No se le piden insumos ni se carga causa/tipo de falla** — son irrelevantes en esos casos. Esto es una regla dura del modelo.

> **Preguntas para ti:**
> - ¿Las tres opciones de acción requerida cubren bien la realidad? ¿O hay un cuarto caso típico que se nos escapa?
> - ¿Tiene sentido la regla de que "no requiere" y "seguimiento" no carguen causa/tipo de falla? ¿O en seguimiento sí conviene capturar al menos la causa sospechada?
> - ¿La separación causa/tipo es la correcta o las usan como sinónimos en el campo?

### Paso 6 — Cierra la inspección

Cuando ya pasó por todos los items que quería, ve un resumen: cuántos hallazgos por nivel, cuántas novedades preop verificadas, cuántos insumos en total.

Llena:
- **Diagnóstico final** — texto resumiendo el estado del equipo
- **Dictamen de operación** — uno de tres: Puede operar / Con restricción / No puede operar
- **Firma manuscrita** en el pad

Toca **Firmar y enviar a MYE**.

### Paso 7 — Confirmación de OT generada

Si hubo al menos un hallazgo "Sí requiere intervención", el sistema **automáticamente** crea una orden de trabajo correctiva en Sinco MYE con la **lista consolidada de todos los repuestos e insumos** que pidieron los hallazgos.

El técnico ve una pantalla de confirmación: "OT-123456 generada", el equipo, número de hallazgos, BOM enviado, dictamen.

Si no hubo hallazgos con intervención requerida, la inspección se cierra sin OT (queda en histórico, los seguimientos pasan a bandeja del supervisor para reinspección).

---

## 5. Las 3 acciones requeridas y sus consecuencias

Esta es la decisión más importante que el técnico toma por cada hallazgo.

| Acción requerida | Color | Cuando aplica | Qué pasa al cerrar |
|---|---|---|---|
| **No requiere intervención** | Verde | El hallazgo se registra para histórico pero el equipo opera sin acción correctiva | Nada — solo queda en histórico |
| **Requiere seguimiento** | Naranja | Hay que monitorearlo en próximas inspecciones, pero no es urgente | Pasa a bandeja del supervisor para programar reinspección |
| **Sí requiere intervención** | Rojo | Hay que arreglarlo formalmente | Aporta línea al BOM consolidado y se genera OT correctiva en MYE |

**Regla automática que aplicamos:** si hay aunque sea un hallazgo "Sí requiere intervención" en la inspección, se genera una OT correctiva. Si todos los hallazgos son seguimiento o no-requieren, no se genera OT.

> **Preguntas para ti:**
> - ¿La generación de OT debería ser automática o el técnico debería poder revisar el BOM y aprobarlo antes de mandarlo?
> - ¿Hay casos donde "Sí requiere intervención" NO debería generar OT? Por ejemplo, si el técnico mismo va a hacer la reparación in-situ sin pasar por planificación.
> - ¿La lista consolidada de insumos debería poder editarse antes de mandarse a MYE (ej. quitar duplicados, ajustar cantidades)?

---

## 6. Reglas de negocio que aplicamos hoy

Estas son las reglas internas que el sistema hace cumplir. Las llamamos "invariantes". Te las paso en lenguaje natural para que las valides:

1. **Una inspección se crea siempre ad-hoc**: el técnico elige equipo + rutina y la inicia. No hay programación previa en MVP. El sistema valida que el equipo, la rutina y los permisos del técnico encajen, y crea el registro al iniciar.
2. **Solo en estado "En ejecución"** se pueden agregar hallazgos, mediciones, repuestos, fotos.
3. **Para firmar la inspección hace falta:** diagnóstico final, dictamen establecido, y la firma del técnico. Faltando alguno no se puede firmar.
4. **Una novedad del preop solo se puede verificar UNA vez** dentro de la inspección. Si por error se verifica dos veces, el sistema rechaza la segunda.
5. **Cada repuesto debe estar asociado a un hallazgo existente** dentro de la inspección. No hay repuestos "sueltos".
6. **La inspección solo se puede cancelar antes de firmar.** Después de firmada queda inmutable.
7. **Si un hallazgo tiene "Sí requiere intervención":** acción correctiva, causa de falla y tipo de falla son **obligatorios**.
8. **Si un hallazgo tiene "No requiere intervención" o "Requiere seguimiento":** NO se pueden cargar acción correctiva, causa, tipo de falla ni insumos. (Es prohibición, no opcional.)
9. **Toda novedad técnica debe tener descripción no vacía.** No se puede registrar un hallazgo "fantasma".
10. **El que firma la inspección debe ser el técnico asignado o un supervisor**. No puede firmar otro técnico distinto.
11. **Dictamen siempre obligatorio al firmar** (selector con 3 valores: Puede operar / Con restricción / No puede operar), independiente de si hay hallazgos con intervención o no. Sin restricción sobre cuál de los 3 valores se elige cuando hay intervención — es decisión del técnico. Adicionalmente, el dictamen se sincroniza a MYE en cada firma como "dictamen vigente del equipo", no solo cuando se genera OT. *(Confirmado por Sergio 2026-04-30. Cerraba pregunta abierta de la regla original #11.)*
12. **Una sola inspección técnica abierta por equipo a la vez.** Si un técnico abre una inspección sobre el equipo X y otro técnico tap "Iniciar inspección" sobre el mismo equipo, el sistema lleva al segundo a la inspección ya activa para que **contribuya** (agregar hallazgos, evidencia) — NO se crea una segunda inspección. Una inspección queda en `EnEjecucion` desde su creación hasta firma o cancelación, y durante ese tiempo varios técnicos pueden colaborar sobre ella. *(Confirmado por Sergio 2026-04-30. Modelado como invariante I-I1 §15.7 + proyección con uniqueness §15.12.6.)*
13. **Una inspección firmada con hallazgos que requieren intervención puede ser rechazada por un aprobador**, evitando que se cree la OT en el ERP. Solo es posible mientras la OT aún NO ha sido solicitada (una vez enviada al ERP, cancelar requiere coordinación cross-team y queda fuera del alcance). El motivo del rechazo es obligatorio (mínimo 10 caracteres, texto libre). La inspección queda en estado terminal `CerradaSinOT` con discriminador `RechazadaPorAprobador`, libera el equipo para nuevas inspecciones, y dispara notificación a usuarios con capability `recibir-alertas-ot-rechazada` (típicamente técnico firmante + supervisor). El aprobador que rechaza NO se notifica a sí mismo. *(Confirmado por Sergio 2026-04-30. Modelado en ADR-007 §17 — comando `RechazarGenerarOT`, evento `GeneracionOTRechazada_v1`, invariante I-F6.)*
14. **Al firmar la inspección, el sistema genera automáticamente un PDF** (con header del equipo + proyecto, lista de hallazgos con miniaturas de fotos, repuestos estimados, diagnóstico, dictamen, firma escaneada, GPS de inicio y firma, lista de técnicos contribuyentes, hash SHA-256 al pie). El PDF se sube a Azure Blob y queda disponible para auditoría aun cuando la inspección cierre sin OT. **Cuando se genera la OT correctiva en MYE, el módulo adjunta el PDF a la OT** mediante endpoint dedicado del ERP (multipart). Si el adjunto falla, la OT queda creada igual y el PDF entra a queue manual de reintento — no se revierte la OT por falla del adjunto. *(Decisión 2026-04-30 a partir de observación Sergio. Modelado en ADR-007 §17 sub-sección "Generación de PDF de inspección y adjunto a OT".)*
15. **No se permite iniciar inspección sobre equipos sin rutina técnica configurada en su grupo.** El sistema rechaza el inicio con un mensaje accionable: el admin del catálogo de rutinas debe activar la rutina del grupo antes de inspeccionar el equipo. *(Decisión 2026-04-30 derivada del análisis de datos de los 27 clientes ERP — varios clientes tienen equipos sin partes en el catálogo, hipótesis: esos equipos están en grupos sin rutina configurada. Modelado como invariante I-I2 §15.7.)*

> **Preguntas para ti:**
> - ¿La regla #8 (prohibición de causa/tipo en seguimiento) es razonable? ¿O en seguimiento sí conviene saber al menos la causa sospechada?
> - ¿Qué reglas operativas que tú aplicas en campo NO están en esta lista y deberían estar?

---

## 7. Cómo organizamos las rutinas técnicas en el primer release

El módulo trabaja con **una rutina técnica única por grupo de equipo** (BULLDOZER, RETROEXCAVADORA, MOTONIVELADORA, etc.). La rutina lista las partes inspeccionables del grupo (motor, sistema hidráulico, transmisión, cabina, llantas, etc.) y el técnico decide qué inspeccionar en cada visita y registra los hallazgos donde corresponda. **No subdividimos en "inspección de motor" vs "inspección hidráulica"** — el técnico cubre en una sola visita lo que considera relevante.

La rutina **se deriva automáticamente** del grupo del equipo al iniciar la inspección — el técnico no la elige.

> **Preguntas para ti:**
> - ¿Tiene sentido operativo una sola rutina por grupo, o en campo esperarías subdividir por enfoque (ej. visita exclusiva al sistema hidráulico)? Si subdividirías, ¿con qué criterio?
> - ¿Las partes inspeccionables del grupo varían entre marcas/modelos? (ej. ¿la lista de partes de un D11T Caterpillar es la misma que la de un D65PX2 Komatsu, ambos del grupo BULLDOZER?)
> - ¿Cuántas partes/sub-sistemas cabría esperar en una rutina bien diseñada de bulldozer? ¿15? ¿40?
> - **Heads-up MVP (promovido 2026-05-05 — antes Fase 2):** además de la técnica, el MVP también incluye un tipo distinto de inspección llamado **monitoreo**, donde sí hay actividades pre-definidas con valores esperados (ej. *batería · medir voltaje · rango 11–15 V*). Si la medición sale del rango, el sistema crea automáticamente un hallazgo de seguimiento al equipo. La asignación equipo↔rutinas-monitoreo es **por grupo de mantenimiento** (decisión 2026-05-05) — todos los equipos del mismo grupo comparten las mismas rutinas. ¿Qué mediciones críticas esperarías ver en monitoreo de bulldozer / retroexcavadora? ¿Tiene sentido operativo asignar las rutinas-monitoreo a nivel grupo, o esperarías que dos equipos del mismo grupo (ej. bulldozer nuevo vs viejo) tengan rutinas distintas?

---

## 8. Áreas donde tu validación es más crítica

Si tienes tiempo limitado, enfócate en estos cinco puntos en orden de prioridad:

1. **Las 3 acciones requeridas** (§5). ¿Cubren la realidad? ¿Las consecuencias automáticas son operativamente correctas?
2. **Los catálogos cerrados** (§3.2). ¿La nomenclatura es la que se usa en el campo? ¿Faltan códigos críticos? ¿Sobran?
3. **Las reglas #8 y #11** (§6). Ambas tienen tensión.
4. **El alcance de la rutina técnica única por grupo** (§7). ¿La premisa "una rutina por grupo, sin subdividir" se sostiene en operación real?
5. **El flujo del técnico paso a paso** (§4). ¿Hay fricciones obvias o pasos que no tienen sentido en orden mecánico real?

---

## 9. Lo que NO te estamos pidiendo validar

Para que sepas qué dejar fuera de tu revisión:

- **Tecnología, Azure, base de datos.** No es tu trabajo, lo lleva el equipo de desarrollo.
- **Diseño visual de las pantallas.** Hay un equipo de UX que pulirá; tú valida los conceptos.
- **Integraciones técnicas con Sinco MYE.** Eso ya está resuelto a nivel arquitectónico.
- **Gestión de identidad y permisos.** Lo manejamos aparte.

---

## 10. Cómo nos das tu feedback

Sugerimos:

1. **Lectura inicial** del documento (~30 min).
2. **Sesión de revisión guiada** con el equipo de producto (~2 horas) donde discutimos los puntos de §8 con tus comentarios.
3. **Documento corto de retroalimentación** con: confirmación de lo que está bien, lista de cambios sugeridos con justificación, y áreas que crees que deberíamos investigar más.
4. **Si encuentras gaps gruesos** que requieren rediseño parcial, lo mejor es marcarlos urgentemente — preferimos parar y ajustar antes de codificar.

---

## 11. Material de apoyo

Estos archivos están disponibles si quieres profundizar en algo específico, pero no son lectura obligatoria:

- `Plantillas Excel/Equipos.xlsx` — formato real con el que se modelan equipos hoy en Sinco.
- `Plantillas Excel/Insumos.xlsx` — formato del catálogo de repuestos.
- `Plantillas Excel/preoperacional.xlsx` — ejemplo de cómo se define una rutina hoy.
- `Plantillas Excel/imagenes app.docx` — capturas de pantalla del flujo actual de Inspecciones en la app móvil.
- `Plantillas Excel/mock del diseño.docx` — mock de las pantallas del módulo nuevo (13 pantallas en 4 secciones: Etapa inicial común / Hallazgo no requiere intervención / Hallazgo sí requiere intervención / Importar hallazgo). Fuente visual vigente desde 2026-04-30.

---

## 12. Glosario operacional

| Término | Significado |
|---|---|
| **Equipo** | Máquina específica con código y placa única. |
| **Grupo de mantenimiento** | Categoría del equipo (BULLDOZER, EXCAVADORA, etc.). Determina qué rutinas aplican. |
| **Rutina** | Formato estándar de inspección. Set fijo de items por grupo. |
| **Item de rutina** | Pareja (Parte, Actividad) dentro de una rutina. |
| **Parte** | Componente o subsistema del equipo (MOTOR, TRANSMISIÓN…). Catálogo cerrado. |
| **Actividad** | Acción concreta sobre una parte. En hallazgos del preoperacional viene del catálogo cerrado del preop (ej. VERIFICACION ESTADO). En hallazgos manuales del inspector se escribe como texto libre. |
| **Hallazgo** | Cualquier cosa que el técnico decide registrar dentro de una inspección, viene con una decisión de "acción requerida". |
| **Novedad** | Hallazgo que el operario reportó previamente en el preoperacional. |
| **Acción requerida** | Decisión del técnico por cada hallazgo: No requiere / Seguimiento / Sí requiere. |
| **Causa de falla** | Por qué pasó. Catálogo cerrado del ERP. |
| **Tipo de falla** | Naturaleza de la falla (mecánica, hidráulica…). Catálogo cerrado. |
| **BOM** | Bill of materials. La lista consolidada de repuestos requeridos para una OT. |
| **OT correctiva** | Orden de trabajo que se genera en Sinco MYE para arreglar lo que el técnico marcó como "Sí requiere intervención". |
| **Diagnóstico final** | Texto del técnico al cerrar la inspección. |
| **Dictamen** | Decisión final del técnico: Puede operar / Con restricción / No puede operar. |

---

**Gracias por tomarte el tiempo de revisar este proceso. Tu input mecánico es lo que va a evitar que construyamos algo elegante en pantalla pero inútil en taller.**
