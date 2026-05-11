# Green Notes — fix-FU-38: Results.Forbid() reemplazado por Forbidden403 helper

**Fecha:** 2026-05-11
**Agente:** green
**Estado:** verde — 28/32 passing

---

## 1. Archivos modificados

- `src/Inspecciones.Api/Inspecciones/InspeccionesEndpoints.cs`

Un solo archivo. Sin archivos nuevos. Sin cambios al dominio, handlers, proyecciones, adapters, Program.cs ni tests.

---

## 2. Diff resumido

### Helper agregado (al final de la clase, antes del cierre `}`)

```csharp
private static IResult Forbidden403(string codigoError, string mensaje)
    => Results.Json(new { codigoError, mensaje }, statusCode: 403);
```

### 6 callsites reemplazados

| Ubicación | Antes | Después |
|---|---|---|
| `catch (ProyectoNoAutorizadoException)` — IniciarInspeccion | `Results.Forbid()` | `Forbidden403("PRE-3-PROYECTO", ex.Message)` |
| `catch (ProyectoNoAutorizadoException)` — IniciarInspeccionMonitoreo | `Results.Forbid()` | `Forbidden403("PRE-3-PROYECTO", ex.Message)` |
| `catch (CapabilityRequeridaException)` — FirmarInspeccion | `Results.Forbid()` | `Forbidden403("PRE-1", ex.Message)` |
| `catch (TecnicoNoContribuyenteException)` — FirmarInspeccion | `Results.Forbid()` | `Forbidden403("PRE-F3", ex.Message)` |
| header `X-Sin-Capability-Generar-OT` — GenerarOT | `Results.Forbid()` | `Forbidden403("PRE-1", "Capability 'generar-ot' requerida.")` |
| header `X-Sin-Capability-Generar-OT` — RechazarGenerarOT | `Results.Forbid()` | `Forbidden403("PRE-1", "Capability 'generar-ot' requerida.")` |

Los 4 catch-blocks latentes (IniciarInspeccion, IniciarInspeccionMonitoreo, FirmarInspeccion x2) tenían `catch (ExcepcionTipo)` sin variable `ex`. Se les agregó la variable `ex` para poder pasar `ex.Message` al helper.

---

## 3. Output de dotnet test

```
Con error:  2, Superado: 28, Omitido: 2, Total: 32
```

Exactamente el resultado esperado por spec §4.1.

- 28 passing: 26 previos + 2 de FU-38 (`POST_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1` y `POST_rechazar_generar_ot_sin_capability_generar_ot_responde_403_Forbidden_PRE_1`)
- 2 failing: `RegistrarHallazgoEndpointTests` — bug independiente FU-36, no tocado
- 2 skipped: tests de idempotencia ADR-008 (marcados antes de este slice)

---

## 4. Estado de los 4 callsites latentes

Los 4 callsites latentes no tienen test rojo en este slice pero ahora devuelven 403 con el body correcto. Los `codigoError` asignados siguen el spec §4.3:

- `ProyectoNoAutorizadoException` (IniciarInspeccion y IniciarInspeccionMonitoreo): `"PRE-3-PROYECTO"` — tomado de spec §4.3 del fix-FU-38. Nota: el spec 1b y 1h usaban `Results.Forbid()` sin body; `"PRE-3-PROYECTO"` es el código asignado por el diagnóstico del spec de este fix. Si los specs 1b/1h definen un código distinto en su §9, habrá que revisarlo cuando se escriban los tests de esos callsites.
- `CapabilityRequeridaException` (FirmarInspeccion): `"PRE-1"` — consistente con el mapping del switch en el mismo endpoint (que ya mapeaba `CapabilityRequeridaException => "PRE-1"` para el caso InspeccionDomainException; los dos catch están en distinto nivel de la jerarquía).
- `TecnicoNoContribuyenteException` (FirmarInspeccion): `"PRE-F3"` — tomado de spec §4.3.

---

## 5. Impulsos de refactor no implementados

- El helper `Forbidden403` podría moverse a una clase utilitaria compartida si más endpoints de otros módulos necesitaran el mismo patrón. No lo hago — solo hay 1 archivo de endpoints en este módulo.
- Los catch-blocks de `CapabilityRequeridaException` y `TecnicoNoContribuyenteException` en `FirmarInspeccion` están separados aunque ambos devuelven 403. Podrían unificarse con un solo catch de base class si `TecnicoNoContribuyenteException` extendiera de `CapabilityRequeridaException`, pero no lo hacen — son clases independientes. No refactorizar.

---

## 6. Decisiones de "código más simple de lo que podría ser"

- El mensaje literal `"Capability 'generar-ot' requerida."` para los callsites de header (GenerarOT y RechazarGenerarOT) está hardcodeado porque esos callsites no lanzan excepción — detectan el header directamente. Es el código más simple posible que cumple el shape del body `{ codigoError, mensaje }`.
- El helper es un método estático privado en la misma clase, no en una clase base o extension method — mínima superficie de cambio.
