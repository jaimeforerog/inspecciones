# Inspecciones — Módulo Sinco MYE

Módulo de inspecciones técnicas para la PWA Sinco MYE móvil. Backend .NET con event sourcing sobre Marten + Wolverine; frontend PWA React/MUI heredada del host.

Ver `CLAUDE.md` para reglas del repo y `METHODOLOGY.md` para el ciclo TDD multi-agente. La fuente de verdad del modelo de dominio es `Inspecciones/docs/01-modelo-dominio.md §15`.

## Stack

| Capa | Tecnología |
|---|---|
| Runtime | .NET 9 |
| Event store / CQRS | Marten 7 sobre PostgreSQL 16 |
| Mediator + outbox | Wolverine 3 |
| Tests | xUnit v3 + FluentAssertions + Testcontainers + WireMock |
| Cloud | Azure Container Apps + Azure DB for PostgreSQL Flexible + Azure Blob + Azure SignalR |

## Cómo correr local

**Requisitos:** .NET 9 SDK, Docker Desktop.

```bash
# 1. Levantar Postgres local
docker compose up -d

# 2. Restaurar dependencias
dotnet restore

# 3. Compilar
dotnet build

# 4. Correr tests (Domain unit + Api E2E con Testcontainers)
dotnet test

# 5. Correr la API (desarrollo)
dotnet run --project src/Inspecciones.Api
# → http://localhost:5000/health
# → http://localhost:5000/openapi/v1.json (en Development)
```

## Estructura de proyectos

```
src/
  Inspecciones.Domain/          Aggregates, eventos, value objects, invariantes (PURO)
  Inspecciones.Application/     Comandos, handlers, sagas
  Inspecciones.Infrastructure/  Adapters Sinco ERP, Azure Blob, SignalR, proyecciones
  Inspecciones.Api/             Endpoints HTTP, Program.cs, hub SignalR

tests/
  Inspecciones.Domain.Tests/         Unitarios del dominio (puros, sin Postgres)
  Inspecciones.Application.Tests/    Integration con Testcontainers Postgres
  Inspecciones.Infrastructure.Tests/ Adapters con WireMock
  Inspecciones.Api.Tests/            E2E con WebApplicationFactory + Testcontainers
```

## Reglas duras (de CLAUDE.md, no negociables)

- `nullable enable` + `TreatWarningsAsErrors=true` en todos los proyectos.
- Naming: español para dominio (`Inspeccion`, `Hallazgo`), inglés para plumbing (`Program`, `Handler`).
- Records para eventos y comandos; classes para aggregates.
- `TimeProvider` inyectado — prohibido `DateTime.UtcNow` en dominio.
- `Guid.NewGuid()` solo en handlers; el dominio recibe el id desde fuera.
- `int` para PKs del ERP, `Guid` solo para IDs internos del módulo.
- `UbicacionGps(...)` para coordenadas — prohibido `double` pelado.
- Cobertura de ramas del agregado afectado **≥ 85 %** por slice.
- Eventos versionados con sufijo `_v1`, `_v2`.
- Soft delete: nunca borra del stream.
- `Apply(Evt)` puro, sin validaciones — invariantes en métodos de decisión.
- Atomicidad: un comando = un `SaveChangesAsync()`.

## Arranque del trabajo (slice nuevo)

Ver `METHODOLOGY.md`. Resumen:

1. Usuario dice "vamos con `XComando`".
2. `domain-modeler` produce `slices/{N}-{slug}/spec.md`.
3. Usuario firma `spec.md`.
4. `red` → `green` → `refactorer` → `reviewer` en orden.
5. Orquestador (`infra-wire`): handler en Wolverine, proyección en Marten, endpoint HTTP, hub SignalR.
6. Commit único `feat(slice-{N}): {comando}`.
