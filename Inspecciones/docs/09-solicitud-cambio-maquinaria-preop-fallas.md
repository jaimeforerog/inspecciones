# 09 — Solicitud de cambio a Maquinaria_V4: `PreoperacionalFallaDto`

> **Para:** equipo Maquinaria_V4 (módulo SincoMyE del ERP).
> **De:** equipo Inspecciones (módulo Azure).
> **Fecha:** 2026-05-29.
> **Disparador:** flujo "Importar hallazgo desde preoperacional" de la PWA (13 frames)
> requiere datos que hoy **no existen upstream** en `GET /api/preoperacional-fallas`
> (slice 7 de Maquinaria_V4). Verificado contra `Inspecciones/docs/erp-swagger/maquinaria_swagger.json`
> (`PreoperacionalFallaDto`) y contra el adapter espejo `Inspecciones.Infrastructure.Erp.Dtos.PreoperacionalFallaErpDto`.
> Complementa la reconciliación bilateral de `06-contrato-apis-erp.md §0.B`.

---

## 0. Resumen ejecutivo (qué pedimos)

Inspecciones consume `GET /api/preoperacional-fallas` como **pasa-piso puro**: no
guarda novedades de preop localmente. Por lo tanto, **cualquier campo que falte en
el DTO de Maquinaria_V4 es invisible para la PWA** — no podemos derivarlo en nuestro
lado.

Pedimos **un cambio aditivo** (sin romper consumidores actuales) al
`PreoperacionalFallaDto` del endpoint de listado:

| Campo nuevo | Tipo | Prioridad | Desbloquea |
|---|---|---|---|
| `parteEquipoId` | `int` | **🔴 ALTA — bloqueante** | El POST de hallazgo de Inspecciones lo **exige** (`RegistrarHallazgo.ParteEquipoId`). Sin él, el técnico debe re-elegir la parte a mano (rompe el diseño). |
| `responsableId` | `int` | 🟡 MEDIA | Filtro "por responsable" + etiqueta "Registrado: {nombre}". |
| `responsableNombre` | `string` | 🟡 MEDIA | Etiqueta "Registrado: {nombre}" en la card. |
| `codigoPreoperacional` | `string` | 🟢 BAJA | Chip de folio "PREOP-2026-0255". Si no existe folio textual, Inspecciones puede formatear `registroPreoperacionalId` (ya presente). |

No pedimos `estado` (Disponible/Importada/Descartada): eso lo deriva Inspecciones
en su propio endpoint (lo sabemos por el aggregate). Ver §3.

---

## 1. Estado actual del DTO (lo que hay)

`Maquinaria.Core.Application.PreoperacionalFallas.Listar.PreoperacionalFallaDto`
(swagger `:867`) expone exactamente:

```jsonc
{
  "id": 0,                       // = PODId (EQV4.PreoperacionalFallas) -> NovedadPreopOrigenId
  "registroPreoperacionalId": 0,
  "equipoId": 0,
  "actividadId": 0,
  "arbolDescripcion": "Motor > Sistema de lubricación",  // texto del árbol — NO es la parte
  "actividadDescripcion": "Revisión de fugas",
  "observacion": "Fuga de aceite en empaque superior",
  "fecha": "2026-04-21T06:15:00-05:00"
}
```

Query params actuales: `desde`, `hasta`, `equipoId`, `texto`. Sin ETag (slice 7 no
emite caché HTTP). Sin filtro por responsable.

---

## 2. Cambio solicitado (lo que falta)

### 2.1 `parteEquipoId: int` — 🔴 bloqueante

**Problema:** el árbol (`arbolDescripcion`) es texto ("Motor > Sistema de
lubricación"); no podemos resolver de forma fiable el `ParteEquipoId` desde ese
string contra `GET /api/partes-equipos` (`ParteEquipoDto.ParteId`). Es matching
textual frágil y propenso a colisiones.

**Por qué nos bloquea:** el comando de escritura de Inspecciones
`RegistrarHallazgo` tiene `ParteEquipoId: int` **obligatorio** (no nullable). La
novedad de preop tiene que llegar ya anclada a su parte para poder importarla sin
fricción.

**Lo que pedimos:** exponer la FK a la parte que el ERP **ya usa internamente** para
construir `arbolDescripcion`. Tres campos, idealmente:

```jsonc
{
  "parteEquipoId": 1280,                 // 🔴 el crítico — FK a la parte
  "parteNombre": "Motor",                // display (opcional, deriva de la parte)
  "sistemaNombre": "Sistema de lubricación"  // display (opcional)
}
```

Si solo pueden dar uno, que sea `parteEquipoId`. Los nombres de display ya están
implícitos en `arbolDescripcion`.

**Fallback acordado del lado Inspecciones (si NO es viable):** la PWA vuelve a
mostrar el selector "Parte del equipo" en el Paso 1 del wizard de importación. Es un
peor diseño (el Paso 1 de import no debería pedir la parte), pero ship-able.
Preferimos resolverlo upstream.

### 2.2 `responsableId: int` + `responsableNombre: string` — 🟡 media

Para el filtro "por responsable" del drawer y la etiqueta "Registrado: {nombre}" de
la card. Es el usuario que registró la novedad en el preop (operador). El ERP lo
tiene (toda novedad de preop tiene autor).

### 2.3 `codigoPreoperacional: string` — 🟢 baja

Chip de folio legible ("PREOP-2026-0255"). Si no existe un folio textual,
Inspecciones formatea `registroPreoperacionalId` (ya presente) — no es bloqueante.

### 2.4 (opcional) filtro `responsableId` en query params

Si exponen `responsableId`, agregar `?responsableId={int}` al listado evita que
Inspecciones filtre en memoria. No bloqueante.

---

## 3. Lo que NO pedimos (lo resuelve Inspecciones)

- **`estado` (Disponible / Importada / Descartada):** Inspecciones lo deriva en su
  propio endpoint `GET /api/v1/inspecciones/{id}/novedades-preop` (ver FU-5) cruzando
  la lista del ERP contra el aggregate `Inspeccion` (`NovedadesDescartadas[]` +
  `Hallazgos[].NovedadPreopOrigenId`). El ERP no necesita saber qué pasó dentro de una
  inspección.
- **Dedupe de importación:** Inspecciones lo enforcea en su dominio (slice 1p —
  `RegistrarHallazgo` rechaza re-importar una novedad ya importada o ya descartada).
- **Tab "Seguimiento":** depende del aggregate `SeguimientoHallazgo` (roadmap 3.C),
  aún no construido del lado Inspecciones. No es una solicitud a Maquinaria_V4.

---

## 4. Forma del DTO objetivo (propuesta concreta)

```jsonc
"PreoperacionalFallaDto": {
  "id": 12345,
  "registroPreoperacionalId": 778,
  "equipoId": 1082,
  "actividadId": 987,
  "arbolDescripcion": "Motor > Sistema de lubricación",
  "actividadDescripcion": "Revisión de fugas",
  "observacion": "Fuga de aceite en empaque superior",
  "fecha": "2026-04-21T06:15:00-05:00",

  // ── nuevos (aditivos, no rompen consumidores actuales) ──
  "parteEquipoId": 1280,                 // 🔴 ALTA
  "parteNombre": "Motor",                // 🟢 opcional
  "sistemaNombre": "Sistema de lubricación", // 🟢 opcional
  "responsableId": 55,                   // 🟡 MEDIA
  "responsableNombre": "Karol Daniela Madrigal", // 🟡 MEDIA
  "codigoPreoperacional": "PREOP-2026-0255"  // 🟢 BAJA (o formateamos registroPreoperacionalId)
}
```

Es un cambio **aditivo**: campos nuevos opcionales. No cambia los tipos ni la
semántica de los existentes; el adapter espejo de Inspecciones
(`PreoperacionalFallaErpDto`) los mapea sin romper deserialización (los campos
ausentes quedan en su default).

---

## 5. Impacto y trazabilidad

- **Endpoint afectado:** `GET /api/preoperacional-fallas` (Maquinaria_V4 slice 7).
- **Origen de datos:** `EQV4.PreoperacionalFallas` (la FK a parte y el autor ya
  deberían existir como columnas; confirmar con DBA).
- **Espejo en Inspecciones:** al aterrizar el cambio, actualizar
  `PreoperacionalFallaErpDto` (vía `inspecciones-api-liaison`) + sumar los campos al
  endpoint de listado de Inspecciones (FU-5) + `06-contrato-apis-erp.md §0.B`.
- **Relación con M-1:** independiente. M-1 (POST de OT al MYE on-prem) sigue bloqueado
  por DDL del DBA del slice 8 de Maquinaria_V4; esta solicitud es sobre el slice 7
  (lectura) y no comparte ese bloqueo.

---

## 6. Pregunta abierta para el equipo Maquinaria_V4

1. ¿`EQV4.PreoperacionalFallas` tiene la FK a la parte del equipo materializada
   (la que alimenta `arbolDescripcion`)? Si sí, exponer `parteEquipoId` es trivial.
2. ¿El autor de la novedad (operador) está disponible en esa tabla / join directo?
3. ¿Existe un folio textual de preoperacional, o solo el `registroPreoperacionalId`
   entero?
