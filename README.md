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

**Requisitos:** .NET 9 SDK, Docker Desktop, PAT de Azure DevOps con permiso de lectura sobre los feeds NuGet corporativos (ver más abajo).

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

### Tests sin Docker (modo rápido)

`Domain.Tests` siempre corre sin Docker. `Api.Tests` y `Application.Tests` por defecto levantan Postgres con Testcontainers, pero puedes apuntarlos a un Postgres local exportando la connection string:

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5432;Database=inspecciones_test;Username=postgres;Password=postgres"
dotnet test tests/Inspecciones.Api.Tests
```

- Si la variable está seteada, `InspeccionesAppFactory` la usa y no arranca contenedor.
- Sin la variable, `Api.Tests` skipea 6 tests del header `X-Client-Command-Id` (ADR-008) con razón explícita.
- `Application.Tests` aún requiere Docker — el switch local está abierto en FU-39.

Si `docker compose up -d` falla silenciosamente, normalmente es un Postgres nativo ocupando el 5432. Workaround: cambiar el port mapping del compose a un puerto alto (p. ej. `55432:5432`) y exportar `ConnectionStrings__Postgres` con ese puerto.

### Feeds NuGet corporativos

Los paquetes `SincoSoft.MYE.Common` y `SincoSoft.MYE.Middleware` (necesarios desde slice mt-1, identidad heredada del host) viven en Azure DevOps. `NuGet.Config` del repo ya registra los feeds `NuGetSinco` y `NuGetMaquinaria`. Para autenticar localmente:

```powershell
dotnet nuget update source NuGetSinco `
  --username <tu-correo-corporativo> `
  --password <PAT-azure-devops> `
  --store-password-in-clear-text
```

Mismo paso para `NuGetMaquinaria`. El PAT necesita scope **Packaging (Read)**.

> **Nota:** CI aún no tiene auth para estos feeds — FU-53 abierto. El primer merge desde una rama que dependa de los paquetes corporativos fallará en GitHub Actions hasta que se cierre.

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
