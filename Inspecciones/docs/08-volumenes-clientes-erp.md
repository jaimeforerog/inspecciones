# Análisis de volúmenes — 27 clientes ERP Sinco MYE

**Fecha:** 2026-04-30
**Origen:** plantillas Excel enviadas por David desde el ERP, una por cliente. Carpeta: `Inspecciones/Plantillas Excel/Datos clientes/`.
**Propósito:** validar supuestos del modelo de dominio contra datos reales, dimensionar Azure, y orientar la selección del cliente piloto (Fase 9.1).
**Cobertura:** todos los clientes activos del ERP que David tenía visibilidad — 27 archivos. Excluye DEMO SAS de los promedios cuando sesgaría análisis (es entorno de prueba).

> **Nota de reproducibilidad**: los datos provienen del corte enviado por David el 2026-04-30. Cualquier re-procesamiento se hace con PowerShell + Excel COM sobre la misma carpeta — el script vive en el historial de la conversación de esa fecha.

---

## 1. Tabla por cliente (ordenada por equipos descendente)

| Empresa | Equipos | EquiposConPartes | TotalPartes | MinPartes | AvgPartes | P50 | P95 | MaxPartes | Rutinas | Preop | Insumos |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| REDES Y EDIFICACIONES S A R&E S A | 1660 | 1639 | 2433 | 1 | 1.5 | 1 | 7 | 11 | 144 | 0 | (ver Excel) |
| CONSTRUALMANZA S.A.S. | 1375 | 1355 | 3385 | 1 | 2.5 | 3 | 4 | 7 | 79 | 0 | — |
| SCHRADER CAMARGO S.A.S | 1227 | 1194 | 2091 | 1 | 1.8 | 1 | 7 | 13 | 187 | 82 | — |
| PAVIMENTOS COLOMBIA S.A.S | 990 | 957 | 7309 | 1 | 7.6 | 7 | 14 | 33 | **1941** | 0 | — |
| CASS CONSTRUCTORES S.A.S | 911 | 893 | 7303 | 1 | 8.2 | 7 | 16 | 31 | 237 | 0 | — |
| Constructora Rizek & Asociados SRL | 623 | 581 | 5764 | 2 | 9.9 | 10 | 17 | 20 | 366 | 0 | — |
| CONSULTA - HIDALGO E HIDALGO COLOMBIA S.A.S | 466 | 448 | 5367 | 1 | 12 | 12 | 16 | 22 | 168 | 0 | — |
| MASSEQ PROYECTOS E INGENIERÍA SAS | 333 | 333 | 1954 | 1 | 5.9 | 4 | 13 | 13 | 368 | 0 | — |
| INGENIERIA TRANSPORTE Y MAQUINARIA S.A.S. | 325 | 319 | 2799 | 2 | 8.8 | 8 | 14 | 15 | 71 | 0 | — |
| SOLETANCHE BACHY CIMAS S.A.S. | 283 | 277 | 1097 | 1 | 4 | 2 | 16 | 17 | 48 | 0 | — |
| JMV INGENIEROS S.A.S | 251 | 239 | 1181 | 1 | 4.9 | 2 | 16 | 26 | 62 | 3637 | — |
| CONSTRUCTORA RIGA SERVICES S.A | 223 | 218 | 1600 | 1 | 7.3 | 8 | 13 | 13 | 23 | 0 | — |
| CONCESIÓN ALTO MAGDALENA S.A.S | 207 | 201 | 1239 | 1 | 6.2 | 4 | 10 | 11 | 47 | 0 | — |
| INGENIERIA Y VIAS S.A.S. | 195 | 195 | 1154 | 1 | 5.9 | 8 | 9 | 11 | 149 | 0 | — |
| Gaico Ingenieros Constructores S.A. | 143 | 143 | 1588 | 1 | 11.1 | 10 | 23 | 26 | 43 | 0 | — |
| A & D ALVARADO & DURING S A S - EN REORGANIZACIÓN | 142 | 136 | 267 | 1 | 2 | 1 | 6 | 8 | 75 | 0 | — |
| CONCRETOS ASFÁLTICOS DE COLOMBIA SAS - CONCRESCOL | 112 | 97 | 1604 | 1 | 16.5 | 18 | 19 | 20 | 67 | 0 | — |
| SOLETANCHE BACHY COLOMBIA S.A.S | 104 | 92 | 610 | 3 | 6.6 | 8 | 10 | 10 | 22 | 0 | — |
| EXPLANAN S.A.S. | 92 | 91 | 1047 | 8 | 11.5 | 11 | 15 | 15 | 159 | **10712** | — |
| ESTRUCTURAS Y PAVIMENTOS S.A.S | 90 | 86 | 437 | 2 | 5.1 | 4 | 9 | 10 | 27 | 0 | — |
| OBCIPOL SAS | 83 | 82 | 832 | 5 | 10.1 | 10 | 12 | 17 | 35 | 0 | — |
| VARELA FIHOLL & CIA S.A.S | 73 | 73 | 439 | 3 | 6 | 6 | 7 | 8 | 11 | 0 | — |
| OTACC S.A.S | 70 | 70 | 529 | 1 | 7.6 | 7 | 12 | 12 | 34 | 0 | — |
| URBACOLOMBIA S.A.S | 67 | 65 | 287 | 1 | 4.4 | 2 | 13 | **54** | 38 | 0 | — |
| SOLUCIONES MODULARES ARGOS S.A.S. | 59 | 59 | 289 | 2 | 4.9 | 2 | 9 | 9 | 14 | 0 | — |
| FUNDACIONES Y PILOTAJES SAS | 38 | 38 | 390 | 2 | 10.3 | 12 | 19 | 19 | 75 | **4123** | — |
| DEMO SAS (test) | 15 | 13 | 59 | 1 | 4.5 | 5 | 7 | 7 | 17 | 37 | — |

---

## 2. Agregados (incluye DEMO)

| Métrica | Total | Min (excl. DEMO) | Avg | Max | Cliente del max |
|---|---:|---:|---:|---:|---|
| Equipos | 10,157 | 38 | 376 | 1,660 | REDES Y EDIFICACIONES |
| Partes | 53,054 | 267 | 1,965 | 7,309 | PAVIMENTOS COLOMBIA |
| Rutinas técnicas | 4,507 | 11 | 167 | **1,941** | **PAVIMENTOS COLOMBIA (outlier)** |
| Preoperacionales | 18,591 | 0 | 689 | 10,712 | EXPLANAN S.A.S. |
| Insumos activos | 189,919 | 4 | 7,034 | 34,428 | (ver Excel por cliente) |

**Distribución de equipos:** ~80 % de los equipos están concentrados en los 7 clientes con > 250 equipos cada uno. La cola larga (los 20 restantes) suma < 20 % del total.

---

## 3. Hallazgos críticos para el modelo y el roadmap

### Hallazgo 1 — Solo 5 de 27 clientes usan Preoperacional con datos

| Cliente | Preop | Equipos | Preop por equipo |
|---|---:|---:|---:|
| EXPLANAN S.A.S. | 10,712 | 92 | **116** |
| FUNDACIONES Y PILOTAJES SAS | 4,123 | 38 | 109 |
| JMV INGENIEROS S.A.S | 3,637 | 251 | 14.5 |
| SCHRADER CAMARGO S.A.S | 82 | 1,227 | 0.07 |
| DEMO SAS (test) | 37 | 15 | 2.5 |

Los **22 clientes restantes** tienen `Preoperacionales = 0`. Es decir, la mayoría de la base instalada del ERP **no usa el módulo preoperacional**.

**Implicaciones:**
- La integración con Preop (P-1..P-6, contrato `06-contrato-apis-erp.md` §3.1) solo se ejercita en 5 clientes — pero esos 5 son los que más datos tienen y mayor valor obtienen del flujo "verificar/descartar/seguimiento".
- **El módulo ya degrada gracefully** cuando la lista está vacía (la sección no aparece) — no requiere cambio de modelo.
- Decisión derivada: la **selección del piloto (Fase 9.1)** debe ser uno de los 5. Sin preop activo, el piloto no ejercita el flujo crítico de la variante B y deja media funcionalidad sin probar antes de producción.

### Hallazgo 2 — EXPLANAN justifica el bulk de descarte

EXPLANAN tiene **116 preoperacionales por equipo**. En una inspección típica, el técnico se enfrenta a docenas de novedades pendientes — muchas duplicadas (operadores reportando la misma falla turno tras turno).

Esto **valida la decisión 2026-04-30** (observación Sergio) de promover el comando bulk `DescartarNovedadesPreop` (§15.9 del modelo) de 10.5 (post-MVP) a MVP. El caso operativo no es excepcional — es el modo de trabajo estándar de los clientes con uso intensivo de preop.

### Hallazgo 3 — La mayoría confirma "una rutina técnica por grupo"

22 de 27 clientes tienen ratio **rutinas/equipos < 1**:
- REDES Y EDIFICACIONES: 0.09 (1660 equipos / 144 rutinas).
- CONSTRUALMANZA: 0.06 (1375 / 79).
- SCHRADER CAMARGO: 0.15.
- CASS CONSTRUCTORES: 0.26.

**Confirma la decisión §12.10/§12.11** del modelo: una sola rutina técnica por grupo de mantenimiento, aplicada a múltiples equipos del mismo grupo. La premisa "BULLDOZER tiene una sola rutina, no se subdivide" se sostiene en operación real para la gran mayoría.

### Hallazgo 4 — Outliers que cuestionan la regla

5 clientes tienen ratio **rutinas/equipos ≥ 1**:

| Cliente | Equipos | Rutinas | Ratio |
|---|---:|---:|---:|
| FUNDACIONES Y PILOTAJES | 38 | 75 | **1.97** |
| PAVIMENTOS COLOMBIA | 990 | 1941 | **1.96** |
| EXPLANAN | 92 | 159 | **1.73** |
| DEMO SAS (test) | 15 | 17 | 1.13 |
| MASSEQ PROYECTOS | 333 | 368 | 1.11 |

PAVIMENTOS COLOMBIA es el extremo: **1,941 rutinas para 990 equipos**. Hipótesis a confirmar con David:

- (a) Rutinas por modelo/marca dentro de un grupo (un grupo BULLDOZER con rutina específica para D11T y otra para D65PX2).
- (b) Rutinas inactivas / legacy infladas en el catálogo (ERP no purga).
- (c) Mezcla de tipos de rutina (técnica + monitoreo + post-mantenimiento + certificación) que el filtro `tipo=tecnica` (§12.11) no separa correctamente.
- (d) Cliente con flota muy diversa que combina maquinaria pesada + activos pequeños + transporte, donde cada subcategoría tiene su rutina.

**Pregunta abierta para David** (ver `07-preguntas-destrabar-followups.md` pregunta 5).

### Hallazgo 5 — REDES Y EDIFICACIONES: flota grande, equipos triviales

1,660 equipos con **avg 1.5 partes/equipo**. El P95 está en 7 partes. Esto sugiere que la flota incluye **activos pequeños** (herramientas, formaletas, andamios, equipos menores) — no maquinaria pesada en sentido tradicional.

**Implicaciones:**
- El modelo está diseñado pensando en bulldozers/retroexcavadoras (10+ partes inspeccionables, mediciones, BOM, OT correctiva con repuestos).
- Para activos triviales: ¿la inspección sigue teniendo sentido o es ruido administrativo? La regla I-I2 (sin rutina no hay inspección) protege parcialmente — si esos activos triviales no tienen rutina configurada, quedan fuera del flujo. Pero si tienen rutina con 1-2 partes, la inspección se vuelve trivial pero formal.
- **No requiere cambio de modelo**, pero sí informa la **UX**: para equipos con 1-2 partes, el wizard puede simplificarse. Recomendación a Daniel: revisar si pantallas asumen `partes >= 5` o si escalan a 1.

### Hallazgo 6 — Equipos sin partes resuelto por I-I2

Casi todos los clientes tienen **algunos equipos sin filas en la hoja "Partes por Equipo"**:
- DEMO: 13 con partes / 15 totales (2 sin partes).
- REDES: 1639 / 1660 (21 sin partes).
- CONSTRUALMANZA: 1355 / 1375 (20).
- SCHRADER: 1194 / 1227 (33).

Hipótesis confirmada en chat con Jaime el 2026-04-30: estos equipos están en grupos sin rutina técnica configurada en el ERP. La nueva invariante **I-I2 (§15.7)** los bloquea limpiamente al iniciar inspección con un mensaje accionable ("contacta al admin del catálogo de rutinas en Sinco"). No requiere código especial — la regla del handler `IniciarInspeccion` ya lo cubre.

### Hallazgo 7 — Dimensionamiento Azure (Fase 1)

**Volumen total estimado en producción** (asumiendo todos los 27 clientes operando simultáneamente, peor caso):

| Entidad | Cantidad | Notas |
|---|---:|---|
| Equipos | ~10,000 | Proyección `EquipoLocal` (sync nocturno, ADR-004) |
| Partes | ~53,000 | Proyección `ParteLocal` |
| Rutinas técnicas | ~4,500 | Proyección `RutinaLocal` (filtrada por tipo=tecnica) |
| Preoperacionales pendientes | ~20,000 (snapshot) | NO se sincroniza al módulo — lectura viva al ERP P-1 |
| Insumos / SKUs | ~190,000 | Proyección `RepuestoLocal` (sync incremental, ver hallazgo abajo) |
| **Total entidades de catálogo** | **~260,000** | Postgres Flexible — sin problema |

Hallazgos derivados para Azure:

1. **Sync de catálogos (ADR-004)**: para clientes con > 30K SKUs (CONCRESCOL, PAVIMENTOS, CASS, REDES), el full sync diario es costoso. Implementar **sync incremental basado en `updatedAt`** del lado ERP — el contrato lo permite si los endpoints aceptan `?updatedSince=`. Confirmar con David al definir M-3..M-7.
2. **PostgreSQL Flexible**: tier inicial puede ser modesto (B2s o equivalente). La carga real está en **eventos de inspecciones**, no en catálogos. Un cliente como EXPLANAN haciendo 1-3 inspecciones diarias por equipo puede generar 90-300 inspecciones/día * 92 equipos = manejable.
3. **Azure Blob (adjuntos + PDFs)**: con el PDF por inspección (decisión 2026-04-30, §17 ADR-007) y adjuntos del wizard de hallazgo, dimensionar cuota ≥ 1 TB para el primer año asumiendo retención de 7 años (reglas operativas de archivo).

---

## 4. Acciones aplicadas a partir de este análisis

| # | Acción | Documento afectado |
|---|---|---|
| 1 | Invariante **I-I2** ("sin rutina no hay inspección") | `01-modelo-dominio.md` §15.7, brief regla #15, roadmap 3.36 test (d) |
| 2 | Pregunta #6 a David — granularidad de rutinas en outliers | `07-preguntas-destrabar-followups.md` |
| 3 | Roadmap Fase 9.1 — piloto entre los 5 con preop | `roadmap.md` |
| 4 | Roadmap Fase 1 — dimensionamiento Azure | `roadmap.md` |
| 5 | Justificación operativa del bulk de descarte | Confirmado en `01-modelo-dominio.md` §15.9 |

---

## 5. Snapshot del cómputo

Generado con PowerShell + Excel COM (Office 16) en sesión 2026-04-30. Los datos por cliente son lecturas literales de las celdas (sin transformación). El cómputo de promedios, P50 y P95 se hace sobre la columna `CantidadPartes` de la hoja "Partes por Equipo" de cada archivo.

Si los datos del ERP cambian, re-ejecutar el script desde el historial de chat — no hay aún un job recurrente.
