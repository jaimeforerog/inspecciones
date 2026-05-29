# Slice 1q — Listar novedades preop importables (con `estado` derivado)

**Autor:** domain-modeler / infra-wire
**Fecha:** 2026-05-29
**Estado:** draft
**Tipo:** endpoint de lectura (query). No emite eventos ni comandos. Añade un método de
lectura puro al aggregate (`EstadoNovedadPreop`) + un endpoint HTTP pasa-piso al ERP.
**Agregado afectado:** `Inspeccion` (solo lectura).

**Cierra (parcial):** **FU-5** — "endpoints de listado de hallazgos importables con
filtros", lado preoperacional. El lado *seguimientos* sigue abierto (depende del
aggregate `SeguimientoHallazgo`, roadmap 3.C, sin construir).

**Restricción de diseño:** **sin cambios en Maquinaria_V4** (decisión 2026-05-29 — no
se pudo tocar el ERP). Por tanto el endpoint expone solo los campos que
`GET /api/preoperacional-fallas` da hoy + el `estado` derivado. Los campos faltantes
(`parteEquipoId`, responsable) se documentan en `09-solicitud-cambio-maquinaria-preop-fallas.md`
y caen al fallback del front (selector de parte manual en Paso 1).

---

## 1. Intención

El técnico en campo necesita ver las novedades de preoperacional de su equipo para
importarlas como hallazgos o descartarlas, **dentro del contexto de su inspección**.
Hoy solo existe `GET /api/v1/admin/preop-fallas-erp`, gated en `administrar-catalogos`
(rol admin) — el rol `ejecutar-inspeccion` no puede usarlo. Este slice expone un
endpoint accesible al técnico, acotado al equipo de la inspección, que además le dice a
cada novedad si ya está **Importada**, **Descartada** o sigue **Disponible** — sin que
el front tenga que cruzar a mano contra `GET /inspecciones/{id}`.

---

## 2. Endpoint

```
GET /api/v1/inspecciones/{id:guid}/novedades-preop
    ?desde=YYYY-MM-DD        (opcional; default sin cota inferior)
    ?hasta=YYYY-MM-DD        (opcional; default sin cota superior)
    ?texto={string}          (opcional; busca en el ERP)
    ?estado=Disponible|Importada|Descartada   (opcional; filtra tras derivar)
```

- Capability: **`ejecutar-inspeccion`** (técnico en campo).
- `equipoId` NO es query param: se toma del aggregate de la inspección (acotamiento
  natural — el técnico ve las novedades del equipo que está inspeccionando).
- Sin header `X-Client-Command-Id` (es lectura, no comando).

### Flujo del handler (endpoint)

1. PRE-1 capability → 403 si falta.
2. PRE-2 cargar aggregate vía `IInspeccionReader.LeerAsync(id)` → 404 si no existe
   (provee `EquipoId` + el estado derivado de cada novedad).
3. Llamar `IMaquinariaErpClient.ListarPreoperacionalFallasAsync(desde, hasta, equipoId, texto)`.
4. Por cada `PreoperacionalFallaErpDto`, derivar `estado` con
   `aggregate.EstadoNovedadPreop(falla.Id)` y mapear a `NovedadPreopImportableDto`.
5. Filtrar por `?estado=` si vino. Devolver `ListarNovedadesPreopResponse`.

---

## 3. Estado derivado (`Inspeccion.EstadoNovedadPreop`)

Método de **lectura puro** del aggregate (la fuente de verdad de qué pasó con cada
novedad en la inspección):

- `Importada` — existe un hallazgo **no eliminado** con `Origen=PreOperacional` y
  `NovedadPreopOrigenId == novedadId`.
- `Descartada` — `novedadId ∈ _novedadesDescartadas`.
- `Disponible` — ninguno de los anteriores.

`Importada` tiene prioridad sobre `Descartada`; son mutuamente excluyentes por INV-ND1
(§15.3 — enforcada por slices 1n + 1p). Un hallazgo eliminado vuelve la novedad a
`Disponible` (coherente con I-H13 / D-1 del slice 1p: re-importar tras eliminar se
permite).

---

## 4. Forma de respuesta

```jsonc
{
  "inspeccionId": "…",
  "equipoId": 1082,
  "total": 1,
  "novedades": [
    {
      "novedadPreopOrigenId": 12345,            // -> POST de hallazgo
      "registroPreoperacionalId": 778,
      "codigoPreoperacional": "PREOP-778",      // sintetizado (no hay folio textual upstream)
      "equipoId": 1082,
      "actividadId": 987,
      "actividadDescripcion": "Revisión de fugas",
      "arbolDescripcion": "Motor > Sistema de lubricación",
      "novedadTecnica": "Fuga de aceite en empaque superior",  // = observacion del ERP
      "fechaRegistro": "2026-04-21T06:15:00-05:00",
      "estado": "Disponible"
    }
  ]
}
```

**Ausentes a propósito** (sin fuente sin cambio ERP): `parteEquipoId`, `parteNombre`,
`responsableId`, `responsableNombre`. `inspecciones-api-liaison` sincroniza
`api-contract.md §8` con este shape y la nota de los campos ausentes.

---

## 5. Decisiones

| # | Decisión | Valor | Justificación |
|---|---|---|---|
| D-1 | `estado` derivado en backend vs cruce en el front | Backend | El aggregate ya tiene el estado; evita que el front cruce contra `GET /inspecciones/{id}`. Cierra el "gap 5 derivable" del contrato. |
| D-2 | `EstadoNovedadPreop` en el aggregate vs helper en Api | En el aggregate | Es lógica de dominio ("qué pasó con esta novedad") y queda unit-testeable en `Domain.Tests` sin Postgres. |
| D-3 | Default sin `?estado=` | Devuelve **todas** (Disponible+Importada+Descartada) | El front muestra las importadas/descartadas con su etiqueta; filtra client-side o con `?estado=`. |
| D-4 | `codigoPreoperacional` | Sintetizado `PREOP-{registroPreoperacionalId}` | No hay folio textual upstream (sin cambio ERP). |
| D-5 | `equipoId` del aggregate, no query param | Aggregate | Acotamiento natural y seguro al equipo de la inspección. |
| D-6 | Campos del ERP MinValue para desde/hasta sin filtro | Igual que `GET /admin/preop-fallas-erp` | Convención ya establecida del adapter. |

---

## 6. Tests

- **Dominio (`EstadoNovedadPreopTests`, 5 casos — verde):** Importada / Descartada /
  Disponible / eliminado→Disponible / per-novedad. Sin Postgres.
- **E2E HTTP (`Api.Tests`):** gated por Postgres/Docker (FU-63). Pendiente: 200 con
  novedades + estado correcto, 403 sin capability, 404 inspección inexistente, 502 ERP
  caído. WireMock para el ERP. Documentado, no ejecutado en esta sesión.

---

## 7. Out of scope

- Tab "Seguimiento" (otro aggregate, 3.C — no construido).
- `parteEquipoId` / responsable (requieren cambio en Maquinaria_V4 — ver doc 09).
- Filtro por responsable (sin dato).
