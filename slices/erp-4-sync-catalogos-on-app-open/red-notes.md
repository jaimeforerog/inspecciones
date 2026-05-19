# Red notes — Slice erp-4 — SincronizarCatalogos

**Autor:** red
**Fecha:** 2026-05-19
**Spec consumida:** `slices/erp-4-sync-catalogos-on-app-open/spec.md`

---

## 1. Decisión de arquitectura de tests

**Opción elegida: Opción B — puerto `ICatalogoSyncRepository` + fake en-memoria.**

**Razón:** el proyecto `Inspecciones.Infrastructure.Tests` no tiene Testcontainers ni referencia a Marten directamente — usa WireMock para emular el ERP, sin Postgres. Añadir Testcontainers habría requerido cambios no-triviales al `.csproj` y al setup de CI, lo que excede el alcance de `red`. Un repositorio fake sigue el patrón establecido por `erp-3` (IInspeccionReader / FakeInspeccionReader) y permite verificar exactamente el comportamiento observable del handler (qué llama al ERP, qué escribe al repo) sin depender de infra.

**Trade-off documentado:** si `green` decide implementar con Marten directo (sin el puerto), puede reemplazar el fake por Testcontainers en `green-notes`. Los tests ejercen el contrato del puerto, no el adapter Marten; si el adapter se elimina, los tests se adaptan trivialmente.

---

## 2. Tests escritos

| Test | Escenario spec §6.X | Archivo |
|---|---|---|
| `SincronizarCatalogos_sync_inicial_sin_etag_persiste_causas_falla_en_repo` | §6.1 | `SincronizarCatalogosHandlerTests.cs` |
| `SincronizarCatalogos_sync_inicial_sin_etag_guarda_state_causas_falla_actualizado` | §6.1 | ídem |
| `SincronizarCatalogos_sync_inicial_respuesta_incluye_causas_falla_como_actualizado` | §6.1 | ídem |
| `SincronizarCatalogos_con_etag_previo_y_304_no_toca_cache_tipos_falla` | §6.2 | ídem |
| `SincronizarCatalogos_con_etag_previo_y_304_guarda_state_no_change` | §6.2 | ídem |
| `SincronizarCatalogos_con_etag_y_304_respuesta_tipos_falla_es_no_change` | §6.2 | ídem |
| `SincronizarCatalogos_etag_previo_y_200_nuevo_reemplaza_causas_falla_wipe_and_replace` | §6.3 | ídem |
| `SincronizarCatalogos_etag_previo_y_200_nuevo_actualiza_etag_en_state` | §6.3 | ídem |
| `SincronizarCatalogos_etag_previo_y_200_nuevo_respuesta_causas_falla_es_actualizado` | §6.3 | ídem |
| `SincronizarCatalogos_causas_falla_5xx_cache_local_intacto` | §6.4 | ídem |
| `SincronizarCatalogos_causas_falla_5xx_guarda_state_error_con_mensaje` | §6.4 | ídem |
| `SincronizarCatalogos_causas_falla_5xx_tipos_falla_304_procesado_correctamente` | §6.4 | ídem |
| `SincronizarCatalogos_causas_falla_5xx_handler_no_lanza_excepcion` | §6.4 (D5) | ídem |
| `SincronizarCatalogos_causas_falla_ok_productos_error_ambos_estados_en_respuesta` | §6.5 | ídem |
| `SincronizarCatalogos_productos_error_cache_repuesto_local_intacto` | §6.5 | ídem |
| `SincronizarCatalogos_productos_error_guarda_state_error` | §6.5 | ídem |
| `SincronizarCatalogos_causas_falla_200_vacio_cache_no_se_borra_D4` | §6.6 (D4) | ídem |
| `SincronizarCatalogos_causas_falla_200_vacio_no_actualiza_etag_D4` | §6.6 (D4) | ídem |
| `SincronizarCatalogos_causas_falla_200_vacio_guarda_state_vaciado_sospechoso_D4` | §6.6 (D4) | ídem |
| `SincronizarCatalogos_causas_falla_200_vacio_respuesta_indica_error_con_mensaje_D4` | §6.6 (D4) | ídem |
| `SincronizarCatalogos_dos_ejecuciones_con_mismo_etag_y_304_idempotente_D6` | §6.7 (D6) | ídem |
| `SincronizarCatalogos_tipos_falla_200_nuevo_cuerpo_repo_contiene_exactamente_3_tipos` | §6.8 (rebuild-analogue) | ídem |
| `SincronizarCatalogos_tipos_falla_200_nuevo_cuerpo_state_coherente_con_repo` | §6.8 (rebuild-analogue) | ídem |

**Total: 23 tests nuevos.**

---

## 3. Verificación de estado rojo

```
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj \
  --filter "FullyQualifiedName~SincronizarCatalogos" --no-build

# Salida observada:
# Con error! — Con error: 23, Superado: 0, Omitido: 0, Total: 23
# Razón de fallo: System.NotImplementedException en SincronizarCatalogosHandler.EjecutarAsync()
```

```
dotnet test tests/Inspecciones.Infrastructure.Tests/Inspecciones.Infrastructure.Tests.csproj --no-build

# Salida observada:
# Con error! — Con error: 23, Superado: 36, Omitido: 0, Total: 59
# Los 36 tests previos (erp-1+2+3) siguen verdes.
```

**Razón de fallo de cada test:** `System.NotImplementedException` — el método `SincronizarCatalogosHandler.EjecutarAsync()` lanza `throw new NotImplementedException()`. Es el fallo correcto: no compila el método de negocio, no el test.

**Excepción especial — `SincronizarCatalogos_causas_falla_5xx_handler_no_lanza_excepcion`:** este test usa `act.Should().NotThrowAsync()`. FluentAssertions captura la `NotImplementedException` y reporta "Did not expect any exception, but found NotImplementedException". Es un rojo válido: el método no está implementado; cuando green lo implemente correctamente capturando el 5xx internamente, el test pasará.

---

## 4. Código de producción añadido (stubs mínimos)

| Archivo | Tipo | Notas |
|---|---|---|
| `src/Inspecciones.Domain/Catalogos/CausaFallaCatalogo.cs` | record nuevo | documento Marten — ID int (clave ERP) |
| `src/Inspecciones.Domain/Catalogos/TipoFallaCatalogo.cs` | record nuevo | documento Marten — ID int (clave ERP) |
| `src/Inspecciones.Infrastructure/Erp/CatalogoSyncState.cs` | clase nueva | documento Marten con Id natural string |
| `src/Inspecciones.Infrastructure/Erp/ICatalogoSyncRepository.cs` | interface nuevo | puerto Opción B — abstrae IDocumentSession |
| `src/Inspecciones.Infrastructure/Erp/SincronizarCatalogosHandler.cs` | clase stub | `EjecutarAsync` lanza `NotImplementedException` |
| `tests/.../Erp/FakeCatalogoSyncRepository.cs` | fake test | implementación en-memoria del puerto |

`RepuestoLocal` ya existía en `Inspecciones.Domain.Catalogos` — no se modificó.

---

## 5. Desviaciones respecto a la spec

- **§6.7 (concurrencia real):** el spec marca este escenario como opcional (baja prioridad). El test implementado simula dos ejecuciones secuenciales en lugar de verdaderamente concurrentes — suficiente para verificar idempotencia last-write-wins (D6) sin race conditions de test flaky. La concurrencia real requiere `Task.WhenAll` con dos handlers compartiendo el mismo repo, lo cual introduciría no-determinismo. Decisión conservadora aceptada.

- **§6.4 — spec dice "MaquinariaErpException (5xx)":** el test `SincronizarCatalogos_causas_falla_5xx_cache_local_intacto` configura WireMock con `500 Internal Server Error`, que el adapter convierte en `MaquinariaErpException`. El test `SincronizarCatalogos_causas_falla_ok_productos_error_ambos_estados_en_respuesta` usa `503 Service Unavailable` (también `MaquinariaErpException`) para distinguirlo del escenario §6.5 que usa `HttpRequestException` (timeout VPN). La distinción es fidelidad al spec.

---

## 6. Hand-off a green

- Spec firmada: sí (estado `draft` en el archivo — confirmar con el usuario si ya está firmado).
- Todos los tests rojos: **sí** — 23/23 rojos por `NotImplementedException`.
- Tests previos intactos: **sí** — 36 tests erp-1+2+3 siguen verdes.
- Sin cambios de comportamiento accidentales: sí.

**Para green:** implementar `SincronizarCatalogosHandler.EjecutarAsync()` + `MartenCatalogoSyncRepository` (implementación Marten del puerto). El endpoint HTTP `POST /api/v1/catalogos/sync` va en `CatalogosEndpoints.cs`. Si green decide eliminar el puerto `ICatalogoSyncRepository` y usar `IDocumentSession` directamente, debe reemplazar `FakeCatalogoSyncRepository` por Testcontainers en `SincronizarCatalogosHandlerTests`.
