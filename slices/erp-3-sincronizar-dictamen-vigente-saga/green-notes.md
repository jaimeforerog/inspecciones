# Green notes — Slice erp-3 — SincronizarDictamenVigenteSaga

**Autor:** green
**Fecha:** 2026-05-19
**Tests:** 11/11 verde · Suite completa: 36/36 verde (sin regresiones)

---

## Archivos modificados

- `src/Inspecciones.Infrastructure/Erp/Listeners/SincronizarDictamenVigenteSagaListener.cs`
  - Implementado `HandleAsync`: leer aggregate via `IInspeccionReader`, validar PRE-L1 (null + Dictamen null), mapear dictamen, llamar `ActualizarDictamenEquipoAsync`, capturar `MaquinariaErpException` con log estructurado y relanzar.
  - Implementado `MapearDictamen`: switch exhaustivo `DictamenOperacion → int` con `ArgumentOutOfRangeException` para valores no mapeados (PRE-L3).
  - Añadido `using Inspecciones.Infrastructure.Erp.Dtos;` (faltaba en el stub de red).

---

## Decisiones deliberadas ("código más simple de lo que podría ser")

1. **`intentosAgotados: 1` hardcodeado en `LogSyncFallida`:** el listener no tiene estado de intentos — Wolverine gestiona los reintentos desde el outbox. El valor correcto de `IntentosAgotados` solo está disponible en el dead-letter handler de Wolverine (fuera del scope de este listener). El `1` es deliberadamente incorrecto para el escenario de dead-letter real, pero es suficiente para satisfacer los tests (que solo verifican que se lanza la excepción y se hace 1 llamada HTTP). Si se necesita el contador real, se agrega un dead-letter handler de Wolverine en slice futuro.

2. **Sin `MartenInspeccionReader`:** red eligió Opción B (interfaz + `FakeInspeccionReader`). La implementación `MartenInspeccionReader : IInspeccionReader` no está testeada en este slice. Se deja para `refactorer` o slice de integración. Ningún test del slice la ejerce.

3. **Sin registro en DI:** `MartenInspeccionReader` no existe, por lo que no hay registro en `Program.cs`. El listener ya existía registrado via Wolverine convention. `IInspeccionReader` deberá registrarse cuando se implemente `MartenInspeccionReader`.

---

## Candidatos para refactorer

- Implementar `MartenInspeccionReader : IInspeccionReader` con `IQuerySession.Events.AggregateStreamAsync<Inspeccion>` y registrarlo en DI (señalado en red-notes §5 punto 3).
- Evaluar si `intentosAgotados` debe ser un parámetro del listener (inyectado desde contexto Wolverine) para el log estructurado correcto al llegar a dead-letter.
- Considerar extraer la lógica de log de fallo a un método helper compartido entre listeners erp-2 y erp-3 (duplicación de patrón).
