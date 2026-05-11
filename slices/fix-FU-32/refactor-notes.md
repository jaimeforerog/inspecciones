# refactor-notes — fix FU-32

## Veredicto

**Sin refactor estructural.** El fix es plumbing de tests con responsabilidades bien separadas
desde la primera versión. Los cinco amplificadores (Postgres switch, config precedence,
EventLog noise, invariant globalization, paralelismo) tienen razones distintas y viven en
archivos distintos — no comparten estado ni código.

## Decisiones de calidad revisadas

### 1. `InspeccionesAppFactory` — un solo archivo, dos modos

Considerado: extraer `LocalPostgresInitializer` / `TestcontainersInitializer` a clases
separadas detrás de una interfaz.

**Decisión: no extraer.** La fixture tiene 2 modos triviales, 4 métodos privados de helpers,
y los modos son ortogonales (el campo `_postgres` es null en modo local, no-null en CI).
La indirección agregaría 2 archivos sin mejorar testabilidad — el fixture ya es el punto
de testabilidad. Si emerge un tercer modo (ej. Postgres remoto en Azure), reconsiderar.

### 2. `DropMartenSchemasAsync` con `DO $$ ... $$` PL/pgSQL inline

Considerado: usar `pg_namespace` SELECT + N statements en lugar de PL/pgSQL.

**Decisión: dejar PL/pgSQL.** Una sola roundtrip al servidor; código declarativo y atómico.
La alternativa N+1 statement es más Postgres-idiomatic pero menos legible y más roundtrips.

### 3. Supresión `EventLog` por reflection sobre `ImplementationType.FullName`

Considerado: tipar `EventLogLoggerProvider` directamente y hacer
`services.RemoveAll<EventLogLoggerProvider>()`.

**Decisión: dejar la búsqueda por nombre.** El `EventLogLoggerProvider` puede registrarse
con tipos internal o decorados con factories; el match por `FullName.Contains("EventLog")`
es robusto al cómo el host genérico lo registra y no agrega referencia al paquete
`Microsoft.Extensions.Logging.EventLog`.

## Cobertura de invariantes

No aplica — es plumbing, no domain logic. La cobertura relevante es que los 24 tests E2E
que pasan ejercitan los handlers reales contra Marten real (Postgres real). Los 6 tests
que fallan son hallazgos válidos del fix (bugs preexistentes en handlers, no de la fixture).

## Warnings

`dotnet build` sobre `Inspecciones.Api.Tests.csproj`: **0 warnings, 0 errores**.

## `dotnet test` post-refactor

Sin cambios respecto al green: 24/32 passing, 6 fail (bugs preexistentes), 2 skip (Wolverine
envelope dedup, FU separado).
