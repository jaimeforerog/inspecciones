# Review notes — Slice erp-2 — DescartarNovedadPreop → outbox Maquinaria_V4

**Autor:** reviewer
**Fecha:** 2026-05-19
**Slice auditado:** `slices/erp-2-descartar-novedad-preop-outbox/`
**Veredicto:** `request-changes` → devuelto a **refactorer**

---

## 1. Resumen ejecutivo

El listener `DescartarNovedadPreopErpListener` cumple correctamente con los 8 escenarios del spec, la lógica de negocio es correcta, la implementación no tiene violaciones de reglas duras y el build compila sin warnings. Sin embargo, la cobertura de ramas del método `HandleAsync` es **70 %** (7/10 condiciones), por debajo del umbral obligatorio del 85 %, y el `refactor-notes.md` no documenta ni justifica las tres ramas descubiertas. Adicionalmente, `NovedadPreopErpCierreFallido_v1` es un miembro público no ejercido por ningún test. El slice se devuelve al **refactorer** para cerrar estas dos brechas antes del commit.

---

## 2. Checklist de auditoría

### 2.1 Spec ↔ tests

- [x] Cada escenario de `spec.md §6` tiene un test correspondiente. Los 8 escenarios (§6.1 happy path, §6.1 body mapping, §6.2 idempotencia 200, §6.3 5xx transitorio, §6.4 5xx persistente, §6.5 400, §6.6 404, §6.7 409 YA_CERRADO, §6.7b 409 código desconocido, §6.8 malformado) están cubiertos por los 10 tests.
- [x] Cada precondición tiene un test que la viola. PRE-L1 (`NovedadId <= 0`) es violada en `Listener_evento_malformado_NovedadId_cero_dead_letter_inmediato_PRE_L1`. PRE-L2 no tiene test propio (verificada por DI en arranque — ausencia justificada en spec §4).
- [x] Invariantes de integración: INV-L2 (log antes de propagar), INV-L3 (no retry en 4xx), INV-L4 (200 yaCerradas = éxito) están referenciadas en los nombres de los tests.
- [x] Los nombres de tests son frases descriptivas en español que identifican el escenario y la invariante.

### 2.2 Tests como documentación

- [x] Given/When/Then visible en todos los tests mediante comentarios explícitos.
- [x] Cero mocks del dominio. El listener se instancia directamente con `MaquinariaErpClient` real apuntando a WireMock.
- [x] Los eventos usados en los fixtures tienen valores coherentes (coordenadas no aplica; `InspeccionId` Guid v7-style plausible, `DescartadaEn` fecha real 2026-05-19).

### 2.3 Implementación

- [x] Código de producción mínimo respecto de los tests ejercidos, con una excepción anotada (ver hallazgo #2 — `NovedadPreopErpCierreFallido_v1` no ejercido).
- [x] Sin `DateTime.UtcNow`, `Guid.NewGuid()` ni acceso a APIs del navegador en el listener.
- [x] Listener es `sealed partial class` (no dominio, plumbing — correcto). El record `NovedadPreopErpCierreFallido_v1` es inmutable (`sealed record`).
- [x] `UbicacionGps` y demás value objects no aplican a este slice (listener de integración puro).
- [x] No aplica `Apply` puro — este slice no toca el aggregate.
- [x] No aplica rebuild test — este slice no emite eventos al stream del aggregate.
- [x] No aplica atomicidad de `SaveChangesAsync` — el listener no escribe en el event store.
- [x] Manejo correcto de idempotencia: 409 `YA_CERRADO` capturado con `when` clause antes de relanzar, evitando que Wolverine lo trate como retry.

### 2.4 Cobertura

- [ ] **Cobertura de ramas del listener ≥ 85 %. Actual: 70 %** (7/10 condiciones en `HandleAsync`).
- [ ] Ramas descubiertas **no están justificadas** en `refactor-notes.md`.

Las tres ramas descubiertas (confirmadas con `dotnet-coverage` sobre `listener-cov.xml`):

| Línea | Rama descubierta |
|---|---|
| 46 | `evento.MotivoDescarte ?? string.Empty` — rama `MotivoDescarte == null` nunca alcanzada |
| 68 | `ex.StatusCode.HasValue && ...` — rama `!HasValue` (excepción sin status code) nunca lanzada |
| 69 | `ex.StatusCode.HasValue ? (int)... : null` — rama `!HasValue` idem |

La rama de línea 46 puede alcanzarse con el test de §6.5 ajustando `MotivoDescarte = null!` en lugar de `string.Empty`. Las ramas 68-69 requieren un test donde `MaquinariaErpException` se construye sin `StatusCode` (timeout, error de red), que el spec §6.4 implícitamente cubriría si se usa `HttpRequestException` envuelta.

### 2.5 Refactor

- [x] `refactor-notes.md` presente con tabla de cambios y refactors descartados documentados.
- [x] Los tests no se modificaron entre la fase red y la fase refactor (el refactor solo tocó `DescartarNovedadPreopErpListener.cs` y `Program.cs`).
- [x] Cero warnings de compilación. Verificado: `dotnet build src/Inspecciones.Api -p:NuGetAudit=false` → 0 advertencias, 0 errores.
- [ ] Ramas descubiertas no documentadas en `refactor-notes.md` (ver hallazgo #1).

### 2.6 Invariantes cross-slice

- [x] `dotnet test tests/Inspecciones.Infrastructure.Tests` → 24/24 en verde.
- [x] `dotnet test tests/Inspecciones.Domain.Tests` → 246 passed, 19 skipped, 0 failed. Sin regresiones.
- [x] Los skips preexistentes son tests de la capa HTTP (PRE1/PRE4/PRE7 de `DescartarNovedadPreopTests`) marcados como `[Skip]` antes de este slice — no son regressions de este slice.

### 2.7 Coherencia con decisiones previas

- [x] Alineado con ADR-006 §16: política de retries 5s→30s→2m→10m declarada en `Program.cs` mediante `ScheduleRetry` + `Then.MoveToErrorQueue()`.
- [x] Alineado con ADR-001 (REST/VPN): el adapter usa `HttpClient` con `BaseAddress` configurado vía `MaquinariaErpOptions`.
- [x] ADR-002 (auth tentativo): el refactorer adoptó la opción A del spec §12 D-3 (service account via `JwtToken` en `MaquinariaErpOptions`). Coherente con el patrón establecido en el adapter.
- [x] La decisión D-1 (409 YA_CERRADO = éxito) del spec está implementada correctamente con `when` clause.
- [x] La decisión D-2 (path inconsistente) fue resuelta a favor del path en `MaquinariaErpClient` (`api/preoperacional-fallas/cerrar`), coherente con la nota DTO de 2026-05-13. Pendiente confirmación de David, marcado 🚧 en spec.
- [x] La política PRE-L1 fue reducida a solo `NovedadId <= 0` (no valida `MotivoDescarte` vacío). Decisión documentada en `green-notes.md §2` y coherente con el escenario §6.5 del spec.

### 2.8 Integración cross-team Sinco

- [x] El slice consume `POST /preoperacional-fallas/cerrar` (P-6). Los tests usan WireMock con stub del endpoint. El estado 🚧 está correctamente señalizado en `spec.md §11`. No se requiere endpoint real para este slice.
- [x] Idempotencia sin `Idempotency-Key` documentada con referencia a nota reconciliación 2026-05-13.

### 2.9 SignalR / push

- [x] No aplica. El spec §10 lo documenta correctamente.

---

## 3. Hallazgos

| # | Tipo | Descripción | Ubicación | Acción sugerida |
|---|---|---|---|---|
| 1 | **blocker** | Cobertura de ramas del `HandleAsync` es 70 % (7/10), por debajo del umbral obligatorio de 85 %. Las tres ramas descubiertas no están justificadas en `refactor-notes.md`. Ver §2.4 para el detalle. | `DescartarNovedadPreopErpListener.cs:38-76` | El refactorer debe: (a) agregar test para `MotivoDescarte = null!` que active la rama `?? string.Empty` de línea 46; (b) agregar test o justificación explícita en `refactor-notes.md` para las ramas `!HasValue` de líneas 68-69 (requiere `MaquinariaErpException` sin StatusCode — timeout/red). Si el adapter nunca puede lanzar `MaquinariaErpException` sin `StatusCode`, documentarlo como rama muerta y justificarlo en `refactor-notes.md`. |
| 2 | **followup** | `NovedadPreopErpCierreFallido_v1` es un `record` público con constructor de 5 parámetros que ningún test instancia directamente. La señal de observabilidad es producida indirectamente vía el `LoggerMessage` (que sí está testeado implícitamente), pero el record en sí no se construye ni se verifica en ningún test. Si a futuro se extiende para alimentar una proyección (spec §8), la diferencia entre el record y lo que emite el log podría divergir silenciosamente. | `DescartarNovedadPreopErpListener.cs:98-103` | Registrar en FOLLOWUPS. Cuando se cree la proyección de "novedades con cierre ERP fallido" (spec §8), el record debe alinearse con el `LoggerMessage` o reemplazarse por él. |
| 3 | **followup** | Los tests §6.5 y §6.6 verifican que el listener lanza `Exception` (tipo base) ante 4xx, pero no verifican que la excepción sea `MaquinariaErpException` específicamente. Si en el futuro el adapter cambia a lanzar un tipo diferente, los tests seguirían en verde aunque el comportamiento de retry haya cambiado. | `NovedadPreopDescartadaListenerTests.cs:277,307` | Seguimiento: cambiar `ThrowAsync<Exception>()` a `ThrowAsync<MaquinariaErpException>()` cuando se decida estabilizar el contrato del adapter. No blocker porque `MaquinariaErpClient` ya lanza `MaquinariaErpException` en todos los casos no-success. |
| 4 | **nit** | En `Program.cs` el comentario de la política de retries dice "5xx retryable — ERP no disponible temporalmente" pero el predicado verifica `StatusCode >= 500` que incluye 500, 501, 502, 503, etc. — todos correctamente retryables. El comentario es preciso; solo señalar que el predicado no incluye timeouts HTTP (`HttpRequestException`), que Wolverine por default pasa a dead-letter inmediato. El spec §6.3 no prueba timeout explícito. Documentado en FU #48 (ya abierto por refactorer). | `Program.cs:86` | Sin acción. FU #48 ya lo captura. |
| 5 | **nit** | `FU #48` (`ArgumentException → MoveToErrorQueue` global) fue anotado por el refactorer. Decisión del reviewer: mantener como followup, no escalar a blocker. Justificación: ningún handler actual en el proyecto usa `ArgumentException` para flujos recuperables; el riesgo es hipotético. Si surge la colisión, la corrección es trivial (`HandlerFor<NovedadPreopDescartada_v1>()`). | `Program.cs:107` | Sin acción adicional. FU #48 abierto. |

---

## 4. Veredicto final — Iteración 1

- [ ] **approved**
- [ ] **approved-with-followups**
- [x] **request-changes** — se devuelve a **refactorer**

**Blockers:**

**Blocker #1 (cobertura):** La cobertura de ramas del `HandleAsync` es **70 %**, por debajo del umbral obligatorio del 85 %. El `refactor-notes.md` no documenta las ramas descubiertas. El refactorer debe cerrar esta brecha antes del commit, ya sea añadiendo los tests faltantes o documentando explícitamente cada rama descubierta como "muerta por diseño" con justificación técnica.

Ramas a resolver:

1. **Línea 46** (`evento.MotivoDescarte ?? string.Empty`): el camino `null` nunca se activa. Test sugerido: `Listener_erp_200_motivo_descarte_nulo_se_normaliza_a_string_vacio` — construir un `NovedadPreopDescartada_v1` con `MotivoDescarte = null!` y verificar que el adapter recibe `observaciones: ""` y el listener completa sin excepción. Este test también acotaría el contrato frente a eventos que lleguen con `MotivoDescarte = null` (p. ej. migraciones de datos previas).

2. **Líneas 68-69** (`ex.StatusCode.HasValue`): la rama `!HasValue` no se activa porque `MaquinariaErpException` siempre recibe un `HttpStatusCode` cuando la lanza `MaquinariaErpClient`. Dos opciones:
   - (A) Agregar test donde `MaquinariaErpException` se lanza sin `StatusCode` (simulando error de red / timeout), verificando que `EsReintentable = false` y que el listener propaga la excepción. Si el adapter nunca genera este caso, ir a (B).
   - (B) Documentar en `refactor-notes.md` que las líneas 68-69 son defensivas (dead code en práctica) porque el adapter siempre pasa `StatusCode`. Justificación suficiente para que reviewer apruebe la rama como muerta.

**Followups abiertos en este review:** #49 (record `NovedadPreopErpCierreFallido_v1` no ejercido), #50 (assertions de tipo en tests 4xx). Ver FOLLOWUPS.md.

---

_Cuando el refactorer cierre el blocker #1, el reviewer puede re-auditar solo §2.4 y §3 hallazgo #1 antes de aprobar._

---

## 5. Re-auditoría — Iteración 2 (2026-05-19)

**Contexto:** el refactorer entregó la iteración 2 con: (a) test nuevo `Listener_envia_observaciones_vacias_cuando_MotivoDescarte_es_null` para cubrir la rama `?? string.Empty`; (b) justificación en `refactor-notes.md` de las ramas `!ex.StatusCode.HasValue` como dead code defensivo; (c) fortalecimiento de los tests 400 y 404 con aserciones sobre `StatusCode` exacto y tipo `MaquinariaErpException`.

### 5.1 Cobertura — blocker #1

**Rama línea 49 (`?? string.Empty`):** el test nuevo construye `EventoEstandar with { MotivoDescarte = null! }` e invoca `HandleAsync`. WireMock responde 200. El test acierta en: (1) `NotThrowAsync`, (2) body contiene `"Observaciones":""`, (3) body no contiene `"null"`, (4) body contiene `"9001"`. La aserción `body.Should().Contain("\"Observaciones\":\"\"")` con comillas literales es precisa — verifica que el serializer PascalCase emite el campo vacío como string, no como JSON null. La rama `null` de la expresión `??` queda ejercida. Correcto.

**Ramas líneas 68-69 (`!ex.StatusCode.HasValue`):** la justificación del refactorer es técnicamente sólida. `MaquinariaErpClient.EnsureSuccessOrThrowAsync` construye `MaquinariaErpException` solo a partir de `HttpResponseMessage`, que siempre tiene un `StatusCode`. No existe ningún path en el adapter que genere `MaquinariaErpException` con `StatusCode = null`. Las dos ramas defensivas actúan como guardia ante un cambio futuro del adapter — el costo de mantenerlas es nulo (cero complejidad lógica adicional, el evaluador de ramas las cuenta pero no aportan comportamiento). La documentación en `refactor-notes.md §Iteración 2` es suficientemente precisa: identifica las líneas exactas, explica el mecanismo (nullable safety ante refactor futuro del adapter) y cuantifica el impacto en la métrica (6/8 tool vs 6/6 efectiva). Aceptado.

**Cobertura efectiva:** 6/6 ramas vivas del `HandleAsync` cubiertas. Umbral 85 % superado. Blocker #1 cerrado.

### 5.2 Test nuevo — legitimidad

`Listener_envia_observaciones_vacias_cuando_MotivoDescarte_es_null` es un test legítimo:

- La rama que ejercita (`MotivoDescarte ?? string.Empty` cuando `MotivoDescarte == null`) existía en el código desde la fase green pero no estaba cubierta.
- El fixture usa `with { MotivoDescarte = null! }` sobre el record estándar — la supresión de nullable es consciente y semánticamente correcta para simular eventos legacy.
- La aserción `body.Should().Contain("\"Observaciones\":\"\"")` con comillas escapadas literales confirma serialización PascalCase y valor `""` (no `null`). Es la aserción más precisa posible sin deserializar el JSON completo.
- La aserción `body.Should().NotContain("null")` es una red de seguridad adicional que atrapa si el serializer emite `"Observaciones":null` en lugar de `""`.
- Given/When/Then estructuralmente visible con comentarios. Nombre en español con descripción del escenario. Correcto.

### 5.3 Tests 400/404 fortalecidos

Los tests `Listener_erp_400_no_reintenta_va_a_dead_letter_INV_L3` y `Listener_erp_404_no_reintenta_va_a_dead_letter_INV_L3` ahora aseveran:

```csharp
var ex = await act.Should().ThrowAsync<MaquinariaErpException>();
ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);   // o NotFound
```

Esto cierra FU #50: el contrato del adapter queda documentado por los tests. Si `MaquinariaErpClient` cambia el tipo de excepción, los tests fallan. Correcto.

### 5.4 FU #49 — `NovedadPreopErpCierreFallido_v1` record no ejercido

La justificación del refactorer es aceptable: el record existe como señal de observabilidad para la proyección futura del spec §8. El `LoggerMessage` source-generated sí está ejercido de forma indirecta en los tests 5xx (el listener lanza la excepción después de loggear). El record como tal no es más que un DTO de documentación — no tiene lógica propia que ejercitar. El riesgo real (divergencia entre el record y el log) solo materializa cuando se implemente la proyección §8. FU #49 permanece abierto con ese disparador. Aceptado.

### 5.5 Invariantes cross-slice

- `dotnet test tests/Inspecciones.Infrastructure.Tests/` → **25/25 en verde**, 0 errores. Verificado.
- `dotnet test tests/Inspecciones.Domain.Tests/` → **246 passed, 19 skipped, 0 failed**. Sin regresiones. Verificado.
- `dotnet build tests/Inspecciones.Infrastructure.Tests/ -p:NuGetAudit=false` → **0 Advertencias, 0 Errores**. Verificado.

### 5.6 Hallazgos de la iteración 2

No se identifican hallazgos nuevos. Los hallazgos #2 (FU #49 abierto), #3 (FU #50 cerrado), #4 y #5 (nits) del veredicto anterior se mantienen sin cambio.

---

## 6. Veredicto final — Iteración 2

- [ ] **approved**
- [x] **approved-with-followups**
- [ ] **request-changes**

**Followups pendientes:** #48 (política Wolverine global `ArgumentException`), #49 (record `NovedadPreopErpCierreFallido_v1` alinear con proyección §8). Ver `FOLLOWUPS.md`.

**Listo para commit:** `feat(slice-erp-2): DescartarNovedadPreop outbox Maquinaria_V4`.
