# Slice mt-4 — Refactor notes

**Fecha:** 2026-05-19
**Autor:** orquestador (rol `refactorer` — Agent tool no disponible en runtime; autorización pre-otorgada por Usuario para el ciclo completo mt-4).

## Candidatos evaluados

### 1. Duplicación `AmbientBearerTokenAccessor` ↔ `IncomingBearerCarrier`

Ambos son patrón **AsyncLocal estático + ScopeReverter IDisposable**. Diff:
- Storage: `private static readonly AsyncLocal<string?>` (idéntico).
- API: `Get/Set` (idéntico modulo nombre).
- ScopeReverter: clase interna nested (idéntica).

**Decisión:** **NO deduplicar.**

Razones:
1. **Semánticas distintas son explícitas.** `AmbientBearerTokenAccessor` es para "el bearer del envelope Wolverine que el listener seteó al inicio del HandleAsync" (scope listener). `IncomingBearerCarrier` es para "el bearer del request HTTP entrante que el middleware capturó" (scope HTTP request). Mezclarlos en una clase compartida ocultaría la intención y dejaría la decisión "qué scope estás usando" implícita.
2. **Distinción semántica intencional.** Si en el futuro emerge necesidad de **ambos estados simultáneos** (improbable pero posible: listener-to-listener publish donde el bearer del envelope entrante NO es el bearer del envelope saliente), tenerlos separados permite resolverlo trivialmente.
3. **FU-61 ya rastrea el patrón.** Si emerge la necesidad de migrar a instance-AsyncLocal por aislamiento más fino en paralelismo, ambos se migran juntos siguiendo el mismo plan.
4. **Costo de deduplicar > beneficio.** Crear una clase base `AsyncLocalBearerStorage` añade indirección que distrae del flujo lectura. Los ~20 líneas duplicadas son un código boilerplate trivial que no justifica abstraer.

### 2. Simetría `LogSyncFallida` ↔ `LogCierreFallido`

Diff de signature post-mt-4:

```csharp
// SincronizarDictamenVigenteListener:
LogSyncFallida(Guid inspeccionId, int equipoId, string? dictamen, string? tenantId,
               int intentosAgotados, string ultimoError, Exception ex);

// DescartarNovedadPreopErpListener:
LogCierreFallido(Guid inspeccionId, int novedadId, string? tenantId,
                 int? statusCode, string? codigoErp, bool esReintentable, Exception ex);
```

**Decisión:** **NO unificar.** Razones:
1. Los campos específicos (`dictamen` vs `novedadId`/`statusCode`/`codigoErp`) son intrínsecos al caso de fallo. Mezclarlos en un único `LogFallaErp(...)` exigiría nulables abundantes o un payload genérico — pérdida de claridad sin ganancia.
2. La consistencia que el slice introdujo (ambos incluyen `TenantId`) ya cumple MT4-INV-3.

Followup latente: si emergen 3+ listeners ERP con `LogErpFallido` similar, extraer un `LoggerMessageHelpers` con composición — pero hoy son 2 instancias, umbral típico de DRY no alcanzado.

### 3. `SessionLoggingScopeFilter` vs `BeginEmpresaScope` directo en endpoints

El spec proponía inyectar `using var _ = logger.BeginEmpresaScope(session);` en cada uno de los 15 endpoints. Green optó por filter global. **Decisión refactor:** mantener el filter (D-MT4-2'). Justificación documentada en green-notes. Si por alguna razón un endpoint quiere comportamiento custom (p. ej. enriquecer con dimensiones extra), puede hacer su propio `BeginScope` adicional dentro del scope ya abierto.

### 4. Métrica `inspecciones.erp.calls` — eficiencia del taggeo

`InspeccionesMeters.RegistrarLlamadaErp` crea KVPs en cada call. Alternativa: pre-crear `Activity`/`Counter` tags como `TagList`. **Decisión:** **no optimizar prematuramente.** El volumen de llamadas ERP del módulo es bajo (decenas/min por inspección firmada). Si emerge perf issue, refactor a `TagList` pooling es trivial.

## Refactors aplicados

**Ninguno.** Mt-4 verdea limpiamente con el código del green. Todos los candidatos evaluados se rechazaron con justificación.

## Verificación post-refactor

Identica a green (no hay cambios):
- `dotnet build` → 0 errors, 0 warnings.
- `Domain.Tests` → 248 pass / 19 skip.
- `Infrastructure.Tests` → 103 pass / 0 skip.
- `Api.Tests` → requiere Postgres (limitación heredada).

Status: **refactor completado — sin cambios al código.** Todos los candidatos evaluados se documentaron con razón explícita de no-acción.
