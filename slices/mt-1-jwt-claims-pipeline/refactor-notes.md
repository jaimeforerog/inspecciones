# mt-1 — Refactor phase notes

**Fecha:** 2026-05-19
**Rol:** refactorer (asumido por orquestador — autorización explícita)
**Green notes:** `slices/mt-1-jwt-claims-pipeline/green-notes.md`

---

## 1. Análisis

Revisé el código del slice buscando candidatos de refactor:

### 1.1 Duplicación del bloque "capability check → tecnicoId" (15×)

Cada endpoint refactorizado tiene:
```csharp
if (!session.Capabilities.Contains("ejecutar-inspeccion"))
{
    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
}
var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);
```

**Evaluado — NO refactorizar.** Razones:

1. **Uniformidad ya capturada**: los mensajes están centralizados en constantes (`MensajeCapabilityEjecutarInspeccion`, `MensajeCapabilityGenerarOT`). El helper `Forbidden403` ya existe (fix FU-38).
2. **Variabilidad real entre endpoints**: cada uno tiene un `codigoError` distinto (`"PRE-1"`, `"PRE-4"`, `"PRE-0"`) y el mensaje depende de la capability esperada. Extraer un helper genérico (`ValidarCapacidad`) introduciría indirección sin ahorrar líneas — la firma del helper tendría que aceptar 3 strings y devolver `IResult?`, equivalente al condicional inline.
3. **Punto de evolución**: cada endpoint ya tiene su propio bloque `try/catch InspeccionDomainException` con switch específico. Mover la capability a un helper deja "la mitad de la lógica fuera" — peor lectura.
4. **Acoplamiento controlado**: el patrón es deliberadamente plano. Si en mt-2 emerge una **policy de autorización compleja** (p. ej. capability scope por proyecto), un helper o middleware nuevo tendrá sentido. Hoy no.

Conclusión: la duplicación es **intencional** y refleja una arquitectura plana donde cada endpoint es legible aisladamente. Refactorizar antes de tener un segundo caso de uso lo viola YAGNI.

### 1.2 `_ = session.IdEmpresa;` solo en `POST /inspecciones`

El read de la claim crítica `IdEmpresa` solo lo agregué en el primer endpoint (donde el test §6.3 lo verifica explícitamente). Considera agregarlo a los otros 14 endpoints también para consistencia "enforcement temprano".

**Evaluado — NO agregarlo aún.** Razones:

1. mt-1 spec D-MT1-3 documenta que "el enforcement cross-proyecto se difiere a mt-2". `IdEmpresa` específicamente se usará como `tenant_id` Marten conjoined en mt-2, donde cada endpoint **necesariamente** la lee al construir el `IDocumentSession`. Agregar el read explícito ahora duplica esfuerzo.
2. Los tests cubren solo `POST /inspecciones`. Agregar el read a 14 endpoints sin test específico es escribir código sin verificación — viola TDD.

Documentado para mt-2: cuando se introduzca el `tenant_id` Marten, el read de `IdEmpresa` será obligatorio en todos los endpoints (probablemente vía middleware o un `ISessionService.RequireValidEmpresa()` helper que se llame implicitamente desde cada handler).

### 1.3 `TestHeaderAwareSessionService` — clase temporal

La clase es explícitamente "no producción" (docstring lo dice). En slices futuros (mt-2..mt-4), cuando la suite migre a `WithSessionService` puro, esta clase puede retirarse.

**Evaluado — NO retirar en mt-1.** El retiro implicaría modificar ~10 archivos de tests legacy, todo el blast radius que mt-1 trata de evitar. Queda como deuda explícita en la docstring.

### 1.4 `SincoMiddlewareSessionService` — duplicación interna en `LeerXOpcional`

Los métodos `LeerEntero`, `LeerEnteroOpcional`, `LeerStringOpcional` comparten estructura (try/dynamic/switch/catch).

**Evaluado — NO extraer.** Razones:
1. Cada método tiene **comportamiento distinto** en el `catch` (uno lanza, otro devuelve default).
2. El método central `LeerMiembro` ya centraliza el acceso a las propiedades del dynamic — la duplicación restante está en el handling de los tipos primitivos, donde un generic helper sería más oscuro que los tres métodos explícitos.
3. La clase tiene ~140 líneas — no es grande. Cada método es ~15-20 líneas. Legibilidad sobre DRY.

### 1.5 Test legacy modificaciones — strings cambiados a `"1"`

Los cambios search-and-replace de `"carlos.ruiz"` → `"1"` y `"ana.gomez"` → `"1"` en `CancelarInspeccionEndpointTests.cs` y `DescartarNovedadPreopEndpointTests.cs` pueden parecer súbitos al leer los tests.

**Evaluado — NO revertir / NO documentar más.** Los green-notes §3.3 ya documentan la decisión y razón. Los seeds quedan funcionalmente equivalentes (TecnicoIniciador = "1" es contribuyente, "99" es externo no contribuyente).

## 2. Cambios aplicados

**Ninguno.** Refactor phase concluye sin modificaciones de código.

## 3. Verificación

```
dotnet build Inspecciones.sln -p:NuGetAudit=false --no-restore
→ Compilación correcta. 0 Advertencia(s) 0 Errores

POSTGRES_TEST_CONNSTRING=... dotnet test tests/Inspecciones.Api.Tests/Inspecciones.Api.Tests.csproj -p:NuGetAudit=false --no-restore
→ Correctas! - Con error: 0, Superado: 65, Omitido: 7, Total: 72
```

Verde se mantiene 65/0/7 en `Api.Tests`. Sin cambios desde green.

## 4. Conclusión

El green deja el código en un estado donde no hay refactor obvio de bajo riesgo y alto beneficio. La metodología pide refactorizar **cuando hay duplicación clara o lectura difícil** — ninguna se detectó. La uniformidad del patrón es por diseño (15 endpoints planos, evolución independiente).

Si en review emerge un blocker de legibilidad o duplicación, se reabre esta fase con la observación específica. Para mt-1 el código queda como está.
