# Review notes — Slice {N} — {NombreComando}

**Autor:** reviewer
**Fecha:** YYYY-MM-DD
**Slice auditado:** `slices/{N}-{slug}/`.
**Veredicto:** `approved` | `approved-with-followups` | `request-changes`

---

## 1. Resumen ejecutivo

Dos o tres frases con el estado del slice y el veredicto. Si es `request-changes`, decir explícitamente a quién se devuelve (red / green / refactorer) y por qué.

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [ ] Cada escenario de `spec.md §6` tiene un test correspondiente.
- [ ] Cada precondición tiene un test que la viola.
- [ ] Cada invariante tocada tiene un test que la viola (referenciando el código del modelo: I-H*, I-F*, V-F*).
- [ ] Los nombres de los tests son frases completas que describen el comportamiento (no `Test1`, no `ShouldWork`).

### 2.2 Tests como documentación

- [ ] Un lector que no conoce el código puede entender el comportamiento leyendo solo los tests.
- [ ] Given/When/Then está claro visualmente en cada test (comentarios o estructura).
- [ ] Sin mocks del dominio.

### 2.3 Implementación

- [ ] El código de producción añadido es mínimo (no hay métodos/propiedades no ejercidos por los tests).
- [ ] No hay `DateTime.UtcNow`, `Guid.NewGuid()`, ni acceso directo a APIs del navegador (GPS, firma) dentro del dominio.
- [ ] Los eventos son `record` inmutables.
- [ ] `UbicacionGps`, `Hallazgo`, `Repuesto` y demás value objects del dominio se usan en sus campos respectivos; nunca primitivos pelados (`double` para coords, `string` para causa de falla, etc.).

### 2.4 Cobertura

- [ ] Cobertura de ramas del agregado ≥ **85 %**. Actual: **XX %**.
- [ ] Ramas descubiertas están justificadas en `refactor-notes.md` o anotadas como deuda.

### 2.5 Refactor

- [ ] `refactor-notes.md` presente y claro, aunque sea "sin cambios".
- [ ] Los tests no se tocaron en la fase refactor (salvo renombrar).
- [ ] Sin warnings de compilación.

### 2.6 Invariantes cross-slice

- [ ] El slice no rompe invariantes de slices previos (verificación: `dotnet test` completo en verde, no solo el filtro del slice).

### 2.7 Coherencia con decisiones previas

- [ ] Alineado con `01-modelo-dominio.md` §15 (fuente de verdad).
- [ ] Alineado con ADRs aplicables: ADR-001 (REST/VPN), ADR-002 (Entra ID), ADR-003 (OT correctiva), ADR-004 (catálogos), ADR-005 (SignalR).
- [ ] Alineado con memoria del proyecto y notas del consultor mecánico.

### 2.8 Integración cross-team Sinco (si aplica)

- [ ] Si el slice consume un endpoint Sinco on-prem, el contrato del mock está acordado o el endpoint real está disponible.
- [ ] Si el slice publica hacia Sinco (p. ej. `POST /mye/ot-correctivas`), `Idempotency-Key` está presente y verificado en test.

### 2.9 SignalR / push (si aplica)

- [ ] Hub registrado, autenticación JWT validada contra los técnicos contribuyentes de la inspección.
- [ ] Fallback HTTP polling documentado.

## 3. Hallazgos

Numerar cada hallazgo, clasificar en:

- **blocker**: obliga a `request-changes`.
- **followup**: permite `approved-with-followups`, se mueve a `FOLLOWUPS.md`.
- **nit**: comentario menor, no bloquea ni genera followup.

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | blocker / followup / nit | … | archivo:línea | … |

## 4. Veredicto final

- [ ] **approved** — sin hallazgos, o solo nits asumidos.
- [ ] **approved-with-followups** — followups registrados en `FOLLOWUPS.md`.
- [ ] **request-changes** — se devuelve a **{red | green | refactorer}** con los blockers detallados.

---

_Cuando el veredicto es `approved` o `approved-with-followups`, el orquestador puede proceder al commit del slice y a la fase de infra-wire._
