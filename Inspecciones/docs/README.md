# Inspecciones / docs

Índice de la documentación del módulo. Para el modelo de dominio canónico ir directamente a **§15 de `01-modelo-dominio.md`** — es la fuente de verdad.

## Mapa de archivos

| Archivo | Propósito | Audiencia |
|---|---|---|
| `00-investigacion-mercado.md` | Contexto inicial, alternativas evaluadas, ADR-001..008 (en §9). | Arquitectura / nuevo dev |
| `01-modelo-dominio.md` | Modelo de dominio. **§15 = fuente de verdad** (eventos, invariantes, decisiones consolidadas). §2-14 son histórico. | Dominio / TDD |
| `03-sow-consultor.md` | SOW + ADR-003 (OT correctiva en MYE). | Arquitectura |
| `04-brief-consultor-producto.md` | Brief de producto al consultor. | Producto |
| `05-catalogo-eventos.md` | Catálogo plano de eventos del aggregate. | Dominio |
| `06-contrato-apis-erp.md` | Contrato con `Maquinaria_V4` on-prem. §0.A verificación 2026-05-16 contra swagger. §0.B reconciliación bilateral 2026-05-13. | Integración ERP |
| `07-preguntas-destrabar-followups.md` | Preguntas abiertas para cerrar followups. | Producto |
| `08-volumenes-clientes-erp.md` | Volumetría esperada por cliente. | Capacity planning |
| `roadmap.md` | Roadmap fases 0..10. | Producto / planning |

### Wireframes y mockups (HTML estático)

| Archivo | Tema |
|---|---|
| `02c-variantes-ux-novedades.html` | Variantes UX de novedades preoperacionales. |
| `02d-wireframes-seguimientos.html` | Wireframes de seguimientos de hallazgos. |
| `02e-wireframes-monitoreo.html` | Wireframes de monitoreo. |
| `02l-mock-auditoria-inspecciones.html` | Mock de auditoría. |

### Flujos — patrón triple complementario

Cada flujo tiene **dos vistas**: narrativa (`.md`, para leer) y nodal (`.md`+`.html`, para implementar).

| Flujo | Narrativa (lectura) | Nodal (implementación) |
|---|---|---|
| Inspección técnica manual | `02f-flujo-inspeccion-tecnica-manual.md` | `02i-workflow-tecnica-nodos.md` + `.html` |
| Inspección monitoreo | `02g-flujo-inspeccion-monitoreo.md` | `02j-workflow-monitoreo-nodos.md` + `.html` |
| Seguimientos | `02h-flujo-seguimientos.md` | `02k-workflow-seguimientos-nodos.md` + `.html` |

Los HTML interactivos llevan Mermaid embebido — ábrelos en navegador para zoom y panning.

### ADRs con archivo propio

ADR-001..ADR-007 viven en `00-investigacion-mercado.md §9` y `03-sow-consultor.md`. Los ADRs grandes ganan archivo propio:

| Archivo | ADR | Estado |
|---|---|---|
| `09-adr-008-offline-cliente.html` | ADR-008 (cola de comandos offline PWA) | Aceptada |
| `10-eventos-aggregate-inspeccion.html` | Catálogo visual de los 19 eventos del aggregate `Inspeccion`. | Vigente |
| `11-adr-009-multi-tenancy.md` | ADR-009 (Marten conjoined + propagación JWT). Consolida mt-1..mt-4. | Aceptada 2026-05-19 |

### Material auxiliar

- `desarrollo/` — notas técnicas internas (no doc oficial).
- `erp-swagger/` — snapshots del swagger de `Maquinaria_V4` por fecha.
- `inspeccion.xlsx` — matriz histórica de campos.

## Convención para nuevos documentos

- Prefijo numérico secuencial (`XX-nombre-en-kebab.md`).
- Si un ADR cabe en 2-3 párrafos, va a `00-investigacion-mercado.md §9.X`. Si requiere más, archivo propio `XX-adr-NNN-tema.md`.
- Los diagramas nodales reusan el patrón `02X-workflow-tema-nodos.{md,html}`.
