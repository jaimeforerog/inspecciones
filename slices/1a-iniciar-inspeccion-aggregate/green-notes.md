# Green notes — Slice 1a — IniciarInspeccion (aggregate puro)

**Autor:** green
**Fecha:** 2026-05-05
**Spec consumida:** `slices/1a-iniciar-inspeccion-aggregate/spec.md` (firmado 2026-05-05).
**Red consumida:** `slices/1a-iniciar-inspeccion-aggregate/red-notes.md` (14 tests rojos en `IniciarInspeccionTests.cs`).

---

## 1. Archivos modificados

Solo se tocaron **dos métodos** de un único archivo de producción:

| Archivo | Cambio |
|---|---|
| `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs` | Implementación de `Iniciar(cmd, claims, equipo, rutina, ahora)` y `Apply(InspeccionIniciada_v1 e)`. Reemplazos directos de los dos `throw new NotImplementedException()` que dejó la fase red. |

Cero cambios en:
- `tests/` (prohibido por persona green).
- `src/Inspecciones.Application/IniciarInspeccionHandler.cs` (slice 1b).
- Records/eventos/excepciones existentes (solo se consumieron los stubs ya creados en red).
- Catálogos / records de claims / DTOs.

## 2. Implementación de `Inspeccion.Iniciar(...)`

Orden de validaciones según §4 del spec (cada precondición = un guard que lanza la excepción específica):

1. **PRE-2** — `claims.ProyectosAsignados.Contains(cmd.ProyectoId)`; si no, `ProyectoNoAutorizadoException` con mensaje `"El técnico {TecnicoIniciador} no tiene asignación al proyecto {ProyectoId}."` — incluye técnico y proyecto literal para que matchee el patrón del test §6.4 (`*rmartinez*proyecto*99*`).
2. **PRE-4** — `equipo.ProyectoId == cmd.ProyectoId`; si no, `EquipoNoPerteneceAProyectoException` con mensaje que cita el `EquipoCodigo` y ambos proyectos (el del catálogo y el del comando).
3. **PRE-5 (I-I2)** — `equipo.RutinaTecnicaId is not null`; si null, `EquipoSinRutinaTecnicaException` con mensaje accionable que incluye `EquipoCodigo` (matchea `*CARGADOR-EX-201*` del test §6.6) y la indicación "Contacta al admin del catálogo en Sinco."
4. **PRE-6 (I-I2)** — `rutina.RutinaId == equipo.RutinaTecnicaId.Value && rutina.Tipo == TipoRutina.Tecnica`; si no, `RutinaTecnicaNoSincronizadaException` con mensaje que incluye la palabra `sincronizada` (matchea `*sincronizada*` del test §6.7) y la directiva "refresca catálogos".
5. **PRE-7 (I-I3)** — rango `[hoy-30, hoy]` con `hoy = DateOnly.FromDateTime(ahora.UtcDateTime)`. Comparaciones: `cmd.FechaReportada > hoy` (futura) o `cmd.FechaReportada < limiteInferior` (>30 días atrás). El mensaje incluye `"rango aceptable"` (matchea `*rango*` del test §6.8) más las fechas literales del rango para diagnóstico operativo.

Si todas las precondiciones pasan, construye y retorna `new object[] { new InspeccionIniciada_v1(...) }` con todos los campos del payload denormalizados:
- `Tipo = TipoInspeccion.Tecnica` (hard-coded; el spec dice "siempre Tecnica en MVP del slice 1a"; cuando llegue Monitoreo será otro slice/comando).
- `RutinaId = rutina.RutinaId`, `RutinaCodigo = rutina.Codigo` (denormalización del catálogo al evento — autosuficiencia del stream).
- `IniciadaEn = ahora` (parámetro `TimeProvider`-controlado del handler; jamás `DateTime.UtcNow`).
- Resto: tomado del `cmd` y `claims` tal cual.

### Decisiones deliberadas de "código más simple de lo que debería ser"

- **`return new object[] { evento }`** en vez de un tipo más rico (`IReadOnlyList<object>` directo, o un record `EventStream`). Es lo mínimo que satisface la firma actual del método (`IReadOnlyList<object>`) y los tests. `object[]` implementa `IReadOnlyList<object>`. Candidato para `refactorer`: cuando lleguen comandos que emiten múltiples eventos (firmar inspección emite 3, ver §15.4 modelo) puede valer la pena un helper `Events(...)` o un `ImmutableArray<object>`.
- **Validaciones inline con `if + throw`** en vez de un pipeline de guards / Result monad / FluentValidation. Cero abstracción: cada precondición es un `if` literal con su mensaje. `refactorer` puede considerar extraer si emerge duplicación cuando se sumen otros comandos del mismo agregado, pero para un solo método no compensa.
- **PRE-6 combina dos checks en un solo `if`** (`RutinaId mismatch || Tipo != Tecnica`). El spec dice que PRE-6 es "rutina existe + tipo correcto"; los tests no distinguen entre los dos sub-casos (test §6.7 solo cubre RutinaId mismatch). Mantengo el `||` simple. Si emerge un test futuro que exija mensajes distintos, se separa.
- **`RutinaCodigo` y `TecnicoIniciador` con default `string.Empty`** en los properties del aggregate (ya estaba desde la fase red). No los toqué — `Apply` los sobreescribe siempre con valores no-null del evento.
- **`switch` con `default → throw InvalidOperationException`** en `AplicarEvento` (privado): mantiene el contrato "este slice solo conoce `InspeccionIniciada_v1`". Cuando lleguen los demás eventos del agregado (slice 2..N), el `refactorer` o el `green` del slice respectivo agregan el `case`. No anticipo nada.

## 3. Implementación de `Apply(InspeccionIniciada_v1 e)`

**Mutación pura, cero validaciones, cero excepciones**. Setea cada propiedad del aggregate desde el evento, incluyendo `Estado = EstadoInspeccion.EnEjecucion` (transición lógica del estado tras la creación del stream — §15.7 modelo).

Cumple la regla CLAUDE.md "Apply puro": si validara aquí, rompería el rebuild histórico contra streams persistidos. Toda regla vive en `Iniciar`.

## 4. Verificación de tests

**Bloqueo de entorno (no del slice):** la máquina del usuario no tiene acceso a los feeds NuGet corporativos (`NuGetSinco`, `NuGetMaquinaria`). Tanto `dotnet restore` como `dotnet build --no-restore` fallan con `NU1507` desde la herramienta NuGet — error de configuración de feeds, no del código del slice. Misma situación reportada en la fase red (red-notes.md §2: los tests de `Inspecciones.Application.Tests` que requieren Docker se diferían a CI).

**Verificación cruzada manual (tests vs código):**

| Test | Validación | Resultado esperado |
|---|---|---|
| §6.1 emite `InspeccionIniciada_v1` | retorno único en lista | ✅ `new object[] { evento }` con instancia correcta |
| §6.1 payload completo | todos los campos | ✅ todos asignados con valores de cmd/claims/rutina/ahora |
| §6.2 lecturas | propaga `LecturaMedidorPrimario/Secundario` | ✅ tomados del cmd |
| §6.3 retroactivo 2 días | rango acepta `[hoy-30, hoy]` | ✅ hoy-2 < hoy y > hoy-30 |
| §6.4 PRE-2 (`*rmartinez*proyecto*99*`) | mensaje incluye técnico + literal "proyecto" + id | ✅ "El técnico rmartinez no tiene asignación al proyecto 99." |
| §6.5 PRE-4 | lanza tipo correcto | ✅ orden: PRE-2 pasa (3∈{3,5}), PRE-4 falla (equipo.Proy=1 ≠ cmd.Proy=3) |
| §6.6 PRE-5 (`*CARGADOR-EX-201*`) | mensaje incluye código del equipo | ✅ "El equipo CARGADOR-EX-201 no tiene rutina técnica..." |
| §6.7 PRE-6 (`*sincronizada*`) | mensaje incluye literal "sincronizada" | ✅ "...no está sincronizada en el catálogo local..." |
| §6.8 PRE-7 futura (`*rango*`) | mensaje incluye literal "rango" | ✅ "...fuera del rango aceptable [...]" |
| §6.9 PRE-7 -35 días | lanza | ✅ -35 < -30 |
| §A boundary hoy | acepta | ✅ `cmd.FechaReportada > hoy` es false con igualdad |
| §A boundary -30 | acepta | ✅ `< limiteInferior` es false con igualdad |
| §A boundary -31 | lanza | ✅ -31 < -30 |
| §G excepción no emite eventos | `capturado == null` | ✅ `throw` antes de retornar; la lambda no asigna |
| §6.12 rebuild | `Estado=EnEjecucion`, todos los campos | ✅ `Apply` setea todos + `Estado` |

**Tests de slice 1b (handler con Postgres real):** siguen `[Fact(Skip="Slice 1b — pendiente...")]`. Sin cambios — esperan handler implementado en 1b.

> **Decisión de orquestador requerida:** este slice no se puede marcar como "verde" verificable hasta que el bloqueo NuGet local se resuelva o se ejecute en CI. La verificación cruzada manual demuestra que el código satisface los aserts de cada test (mensajes literales, ramas de control, payload del evento), pero CI debe confirmar el verde real.

## 5. Impulsos de refactor no implementados

Notas para `refactorer` (NO se aplicaron — green es minimal):

1. **Extraer guards a métodos privados estáticos** (`PRE-2: ValidarProyectoAsignado(...)`, etc.). Mejora legibilidad si emergen duplicaciones desde otros comandos del agregado. Por ahora hay un solo método de decisión — un solo bloque inline lee bien.
2. **Patrón de retorno de eventos:** `return new object[] { evento }` es feo. Cuando emerja `FirmarInspeccion` (slice futuro) que retorna 3 eventos atómicos, conviene introducir un helper o cambiar la firma a `params object[]` / `IEnumerable<object>` con un método extension `Events(...)`. No se anticipa.
3. **Mensaje de `RutinaTecnicaNoSincronizadaException`:** combina dos sub-casos (RutinaId mismatch + Tipo incorrecto) en una sola excepción. Si en otro slice emerge la necesidad de distinguirlos para el cliente, se splittea — pero MVP del frontend no lo necesita.
4. **`AplicarEvento` con `switch`:** crecerá con un `case` por evento (RegistrarHallazgo, FirmarInspeccion, GenerarOT, etc.). En algún punto puede valer la pena un dispatcher por reflexión / Marten convención. No se anticipa.
5. **Las cadenas de mensajes de excepción tienen interpolación con formato literal de fechas** (`{:yyyy-MM-dd}`). Cuando se internacionalice el módulo, esto cambia. Por ahora MVP es solo español Colombia, no se anticipa.

Ningún impulso vale la pena ahora — todos serían código que ningún test ejerce.

## 6. Hand-off a refactorer

- ✅ Implementación lista en `src/Inspecciones.Domain/Inspecciones/Inspeccion.cs`.
- ✅ Cero cambios fuera de los dos métodos stubbeados.
- ✅ `Apply` puro (sin validaciones, sin lanzar) — cumple la regla del rebuild.
- ✅ `TimeProvider` respetado (parámetro `ahora`; jamás `DateTime.UtcNow` en dominio).
- ⚠️ **Verificación de tests bloqueada por NuGet local** — `dotnet test` no corre por NU1507 (feeds Sinco corporativos no accesibles). Verificación cruzada manual completa en §4 demuestra que el código satisface cada aserción. Confirmación verde definitiva en CI.

**Para `refactorer`:** revisar los 5 impulsos en §5. La mayoría son "esperar a que emerja la duplicación", coherentes con la regla "no anticipar". Si refactor decide aplicar alguno, debe mantener cada test pasando. El rebuild test (§6.12) es la red de seguridad principal — cualquier cambio a `Apply` debe seguir pasándolo.