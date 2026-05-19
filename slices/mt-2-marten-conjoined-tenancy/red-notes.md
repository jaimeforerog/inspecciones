# Slice mt-2 — Red notes

**Autor:** orquestador (rol `red` — Agent tool no disponible; autorización pre-otorgada)
**Fecha:** 2026-05-19
**Estado:** **Red verificado** — build falla por símbolos faltantes, los tests son rojos por la razón correcta.

---

## Resumen

Tres archivos de test rojos introducidos:

| Archivo | Propósito | Tests | Símbolo faltante (motivo del rojo) |
|---|---|---|---|
| `tests/Inspecciones.Infrastructure.Tests/Auth/TenantRequeridoEnEnvelopeExceptionTests.cs` | Spec §4 MT2-PRE-2 — excepción nueva | 2 | `TenantRequeridoEnEnvelopeException` (CS0246) |
| `tests/Inspecciones.Infrastructure.Tests/Erp/Listeners/SincronizarDictamenVigenteListenerTenantTests.cs` | Spec §6.5 + §6.6 — tenant del envelope al listener | 4 | overload `HandleAsync(evento, Envelope, ct)` (CS1501) + excepción nueva |
| `tests/Inspecciones.Api.Tests/Tenancy/MartenConjoinedTenancyTests.cs` | Spec §6.1, §6.2, §6.3, §6.4, §6.8 — cross-tenant isolation y factory | 9 | puerto `ITenantedDocumentSessionFactory` (CS0246) |

**Total:** 15 tests nuevos.

---

## Comando de reproducción del rojo

```powershell
dotnet build --no-restore -clp:NoSummary 2>&1 | Select-String "error"
```

Errores actuales (esperados):

- `CS0246` × N: `TenantRequeridoEnEnvelopeException` no existe (faltan archivos en `src/Inspecciones.Infrastructure/Auth/`).
- `CS0246` × N: `ITenantedDocumentSessionFactory` no existe (falta el puerto + registro DI).
- `CS1501` × 4: `SincronizarDictamenVigenteListener.HandleAsync` no acepta 3 argumentos (falta overload con `Wolverine.Envelope`).

Sin estos símbolos los tests no compilan — el "rojo" del slice es el build failure. Cuando green declare los símbolos, el build pasa y los tests se ejecutan; algunos de ellos pueden seguir rojos hasta que green implemente la lógica (esperado en TDD estricto).

---

## Símbolos que debe introducir green

### 1. `src/Inspecciones.Infrastructure/Auth/TenantRequeridoEnEnvelopeException.cs`

```csharp
public sealed class TenantRequeridoEnEnvelopeException : InvalidOperationException
{
    public string NombreListener { get; }
    public Guid MessageId { get; }
    public string CodigoError => "TENANT-ENVELOPE-AUSENTE";

    public TenantRequeridoEnEnvelopeException(string nombreListener, Guid messageId)
        : base($"Listener '{nombreListener}' recibió envelope sin TenantId (mensaje {messageId}). " +
               "Dead-letter inmediato (MT2-PRE-2).")
    {
        NombreListener = nombreListener;
        MessageId = messageId;
    }
}
```

### 2. `src/Inspecciones.Infrastructure/Auth/ITenantedDocumentSessionFactory.cs` + impl

Contrato del puerto (spec §2):

```csharp
public interface ITenantedDocumentSessionFactory
{
    IDocumentSession OpenSession();
    IQuerySession OpenQuerySession();
    IDocumentSession OpenSessionForTenant(string tenantId);
}
```

Impl `TenantedDocumentSessionFactory(IDocumentStore store, ISessionService session)`:
- `OpenSession()` → `store.LightweightSession(session.IdEmpresa.ToString(CultureInfo.InvariantCulture))`.
- `OpenQuerySession()` → `store.QuerySession(session.IdEmpresa.ToString(...))` (verificar API exacta Marten 7).
- `OpenSessionForTenant(tenantId)` → `store.LightweightSession(tenantId)` directo.

Registro DI en `Program.cs`:
```csharp
builder.Services.AddScoped<ITenantedDocumentSessionFactory, TenantedDocumentSessionFactory>();
```

Y el `IDocumentSession` scoped pasa a delegarse (D-MT2-1):
```csharp
builder.Services.AddScoped<IDocumentSession>(
    sp => sp.GetRequiredService<ITenantedDocumentSessionFactory>().OpenSession());
```

(Verificación pendiente en green: que Marten + Wolverine integration acepta este override sin romper outbox transaccional.)

### 3. Overload en `IInspeccionReader` + impl

`src/Inspecciones.Infrastructure/Erp/IInspeccionReader.cs`:
```csharp
public interface IInspeccionReader
{
    Task<Inspeccion?> LeerAsync(Guid inspeccionId, CancellationToken ct = default);
    // Nueva overload tenant-aware (mt-2 §6.5).
    Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default);
}
```

`src/Inspecciones.Infrastructure/Erp/MartenInspeccionReader.cs` — impl tenant-aware. El reader debe usar la factory para abrir una `IQuerySession` con el tenant pasado:

```csharp
public Task<Inspeccion?> LeerAsync(Guid inspeccionId, string tenantId, CancellationToken ct = default)
{
    using var session = _store.QuerySession(tenantId);
    return session.Events.AggregateStreamAsync<Inspeccion>(inspeccionId, token: ct);
}
```

(Requiere inyectar `IDocumentStore` en lugar de `IQuerySession` — refactor del ctor del reader. Compat: la overload original `LeerAsync(Guid, CancellationToken)` puede leer del `IQuerySession` scoped si Wolverine ya provee uno tenanted, o se deprecará/lanzará si no.)

### 4. Overload en `SincronizarDictamenVigenteListener.HandleAsync`

`src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteListener.cs`:

```csharp
// Nueva overload — recibe el Envelope de Wolverine para leer el tenant.
public async Task HandleAsync(InspeccionFirmada_v1 evento, Envelope envelope, CancellationToken ct = default)
{
    var tenantId = envelope.TenantId;
    if (string.IsNullOrEmpty(tenantId))
    {
        throw new TenantRequeridoEnEnvelopeException(
            nombreListener: nameof(SincronizarDictamenVigenteListener),
            messageId: envelope.Id);
    }

    var aggregate = await _inspeccionReader.LeerAsync(evento.InspeccionId, tenantId, ct);
    // ... resto idéntico al método actual.
}
```

La overload original `HandleAsync(InspeccionFirmada_v1, CancellationToken)` se conserva por backwards-compat con tests existentes; ambos coexisten. **Wolverine 3 prefiere la overload con más parámetros cuando ambos están presentes** (verificable en green con un test directo si emerge ambigüedad).

---

## Lo que green NO debe hacer

- **No tocar dominio.** Cero cambios en `src/Inspecciones.Domain/*` ni eventos `_v1`. D8 firmada.
- **No tocar `SincoMiddlewareSessionService` ni `FakeSessionService`.** Solo extender el factory.
- **No agregar la propagación del JWT al `MaquinariaErpClient`.** Eso es mt-3.
- **No mockear el dominio en tests.** El listener usa un fake **del puerto** `IInspeccionReader`, nunca del aggregate.

---

## Lo que green debe verificar (D-MT2-10 — riesgo crítico)

Al activar `StoreOptions.Policies.AllDocumentsAreMultiTenanted()` + `Events.TenancyStyle = TenancyStyle.Conjoined` y delegar el `IDocumentSession` scoped al factory, el outbox de Wolverine **debe seguir funcionando** end-to-end. Verificable corriendo:

```powershell
$env:POSTGRES_TEST_CONNSTRING = "Host=localhost;Port=5433;Username=postgres;Password=postgres;Database=inspecciones_mt2_test"
dotnet test tests/Inspecciones.Api.Tests/ --filter "MartenConjoinedTenancy"
```

Si el outbox falla (`wolverine_outgoing_envelopes` no acepta el `tenant_id`, o el listener no recibe el envelope con tenant), green **debe parar y reportar** — posible bloqueo que requiere ADR adicional. Consigna del usuario al iniciar el slice.

---

## Riesgo conocido: ambigüedad de overload `HandleAsync`

Si Wolverine 3 detecta ambas overloads (`HandleAsync(evento, ct)` y `HandleAsync(evento, Envelope, ct)`) y no resuelve cuál usar, el listener falla en runtime con `AmbiguousMatchException`. Mitigación en green:

- **Opción A:** marcar la overload sin envelope con `[Obsolete]` o privatizar (no descubrible).
- **Opción B:** eliminar la overload sin envelope; ajustar los 11 tests del slice erp-3 que la consumen para que pasen `new Envelope { TenantId = "1" }` explícito. **Más invasivo pero más limpio.**

Recomendación red: **Opción B post-green** si Wolverine se confunde. Si no, **Opción A** preserva los 11 tests intactos.

---

## Conteo esperado post-green

| Suite | Antes | Después (expectativa) | Delta |
|---|---|---|---|
| `Domain.Tests` | 246 pass + 19 skip | 246 pass + 19 skip | 0 (mt-2 no toca dominio) |
| `Application.Tests` | (Docker required, skip si no) | igual | 0 |
| `Infrastructure.Tests` | 59 pass | 59 + 2 (excepción) + 4 (listener tenant) = **65 pass** | +6 |
| `Api.Tests` | 65 pass + 7 skip | 65 + 9 (tenancy E2E) = **74 pass + 7 skip** | +9 |

**Total nuevo: 15 tests + sin regresión.**

Cobertura `Domain.Tests`: 94.44% del aggregate `Inspeccion` (sin cambios — mt-2 no toca dominio).
