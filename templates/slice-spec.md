# Slice {N} — {NombreComando}

**Autor:** domain-modeler
**Fecha:** YYYY-MM-DD
**Estado:** draft | firmado
**Agregado afectado:** {InspeccionTecnica | SeguimientoHallazgo | …}
**Decisiones previas relevantes:** links a `01-modelo-dominio.md §15.X`, ADRs aplicables, hallazgos de la sesión con el consultor mecánico.

---

## 1. Intención

Una o dos frases que describan qué necesita lograr el usuario (técnico/ingeniero de mantenimiento, supervisor, admin) con este comando.

> _Ejemplo: "El técnico necesita registrar un hallazgo durante la inspección, indicando si requiere intervención inmediata (genera OT) o solo seguimiento (sin OT, abre ticket de monitoreo)."_

## 2. Comando

```csharp
public sealed record {NombreComando}(
    // payload tipado; usar UbicacionGps para coordenadas, DateOnly para fechas calendario,
    // DateTimeOffset para timestamps, Guid para identificadores.
);
```

## 3. Evento(s) emitido(s)

| Evento | Payload | Cuándo |
|---|---|---|
| `{NombreEvento}` | {campos} | Al {condición}. |

> Convención: eventos en pasado (`HallazgoRegistrado`, no `RegistrarHallazgo`). Ver §15 del modelo de dominio para el catálogo canónico.

## 4. Precondiciones

Condiciones que deben cumplirse **antes** de ejecutar el comando. Si no se cumplen, el comando falla con excepción de dominio específica.

- `{PRE-1}`: {condición} — excepción: `{Tipo}`.
- `{PRE-2}`: …

## 5. Invariantes tocadas

Invariantes del agregado que este comando debe preservar. Referenciar por código del modelo:

- `I-H1` a `I-H9` — invariantes de hallazgos.
- `I-F1` a `I-F3` — invariantes de firma.
- `V-F1` a `V-F7` — validaciones pre-firma.
- `INV-{custom}` si emergió un nuevo invariante en este slice (debe quedar registrado en §15 del modelo en el mismo PR).

## 6. Escenarios Given / When / Then

Cada escenario se convierte en **un test** en la fase red. Todos los escenarios de esta lista deben terminar con un test correspondiente.

### 6.1 Happy path

**Given**
- {estado inicial, expresado como lista de eventos previos}

**When**
- {comando}

**Then**
- emite `{Evento}` con `{campos esperados}`.

### 6.2 Violación de precondición `{PRE-1}`

**Given** …
**When** …
**Then** lanza `{Tipo}` con mensaje "…".

### 6.3 Violación de invariante `{INV-X}`

**Given** …
**When** …
**Then** lanza `{Tipo}`.

_(Añadir tantos escenarios como precondiciones + invariantes el comando pueda violar.)_

## 7. Idempotencia / retries

¿Qué pasa si el comando se reintenta? ¿Es naturalmente idempotente? ¿Requiere `IdempotencyKey`? Para comandos que cruzan a Sinco on-prem (saga MYE, adapter Preop), `Idempotency-Key=InspeccionId` por defecto (ADR-003).

## 8. Impacto en proyecciones / read models

- `{ReadModelX}`: añadir/actualizar campos `…`.
- Si proyecta a un catálogo local (ADR-004 — `EquipoLocal`, `ParteLocal`, etc.), explicitar la estrategia stale-while-revalidate.
- Si no impacta ninguna proyección: anotarlo explícitamente.

## 9. Impacto en endpoints HTTP

- Método + ruta propuesta: `{POST /…}`.
- DTO de request / response.
- Código HTTP esperado en happy path y en cada error de dominio.
- Rol/permiso requerido. La auth la inyecta el host PWA Sinco MYE; el slice declara qué claim/rol espera (p. ej. `tecnico` con obra asignada). Mecanismo concreto: ADR-002 (tentativo).

## 10. Impacto en SignalR / push (si aplica)

Si el slice emite eventos al frontend en tiempo real (ADR-005):

- Hub que emite: `{InspeccionesHub}`.
- Evento push: `{NombreEvento}`.
- Audiencia: `{User=tecnicoId | Group=obraId}`.
- Fallback: HTTP polling cada 5s si SignalR no disponible.

## 11. Impacto en adapters Sinco on-prem (si aplica)

Si el slice consume o publica hacia APIs Sinco (Fase 4 del roadmap — REST sobre VPN, ADR-001):

- Endpoint Sinco consumido: `{GET /equipos/{id}}` (módulo: `MYE núcleo | Preop | Inventario | RRHH`).
- Endpoint Sinco publicado: `{POST /mye/ot-correctivas}` (con `Idempotency-Key`).
- Estado de disponibilidad: `🟢 disponible | 🟡 mock-only | 🚧 bloqueado por equipo Sinco`.

## 12. Preguntas abiertas

Lista de dudas que el domain-modeler no resolvió y requieren decisión del usuario antes de pasar a red.

- [ ] ¿…?

## 13. Checklist pre-firma

- [ ] Todas las precondiciones mapean a un escenario Then.
- [ ] Todas las invariantes tocadas mapean a un escenario Then.
- [ ] El happy path está presente.
- [ ] Preguntas abiertas están todas respondidas o marcadas como asunción con justificación.
- [ ] Si el slice toca un endpoint Sinco no disponible aún, está marcado `🟡 mock-only` y se acordó el contrato del mock.
