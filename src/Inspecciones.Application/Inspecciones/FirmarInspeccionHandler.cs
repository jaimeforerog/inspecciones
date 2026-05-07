using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="FirmarInspeccion"/>. Orquesta:
/// <list type="number">
///   <item>PRE-1 — capability check; lanza <see cref="CapabilityRequeridaException"/> si ausente.</item>
///   <item>PRE-4 — Diagnóstico no vacío; lanza <see cref="DiagnosticoRequeridoException"/> si vacío.</item>
///   <item>Carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>Delega PRE-2, PRE-3, PRE-5..PRE-9 al método de decisión del aggregate.</item>
///   <item>Append de los 3 eventos + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// Los tres eventos (DiagnosticoEmitido_v1, DictamenEstablecido_v1, InspeccionFirmada_v1) se
/// pasan en un único <c>Append</c> con el mismo timestamp y un solo <c>SaveChangesAsync</c>.
/// La proyección <c>InspeccionAbiertaPorEquipoView</c> (FU-13) corre inline en la misma
/// transacción y elimina la fila del equipo al recibir InspeccionFirmada_v1.
/// </summary>
public sealed class FirmarInspeccionHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    /// <summary>Ejecuta el comando y persiste los 3 eventos de firma en un único SaveChangesAsync.</summary>
    public async Task<FirmarInspeccionResult> ManejarAsync(
        FirmarInspeccion cmd,
        ClaimsTecnico claims,
        CancellationToken ct = default)
    {
        // PRE-1 — capability check (capa handler, antes de cargar el aggregate).
        if (!claims.TieneCapabilityEjecutarInspeccion)
        {
            throw new CapabilityRequeridaException(
                "El técnico no tiene la capability 'ejecutar-inspeccion' requerida para firmar.");
        }

        // PRE-4 — Diagnóstico no vacío (capa handler, antes de cargar el aggregate).
        if (string.IsNullOrWhiteSpace(cmd.Diagnostico))
        {
            throw new DiagnosticoRequeridoException(
                "El diagnóstico final es obligatorio y no puede estar vacío.");
        }

        // Cargar el aggregate desde el event store.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"Inspección {cmd.InspeccionId} no encontrada.");
        }

        // Delegar PRE-2, PRE-3, PRE-5..PRE-9 al método de decisión del aggregate.
        // Un único GetUtcNow() — los 3 eventos comparten el mismo timestamp (spec §3).
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.Firmar(cmd, ahora);

        // Append de los 3 eventos en un único SaveChangesAsync (regla CLAUDE.md atomicidad).
        // La proyección InspeccionAbiertaPorEquipoView (FU-13) corre inline.
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        var evtFirmada = (InspeccionFirmada_v1)eventos[2];
        var evtDictamen = (DictamenEstablecido_v1)eventos[1];

        return new FirmarInspeccionResult(
            InspeccionId: evtFirmada.InspeccionId,
            Estado: "Firmada",
            FirmadaEn: evtFirmada.FirmadaEn,
            Dictamen: evtDictamen.Dictamen);
    }
}

/// <summary>Resultado de ejecutar <see cref="FirmarInspeccion"/>.</summary>
public sealed record FirmarInspeccionResult(
    Guid InspeccionId,
    string Estado,
    DateTimeOffset FirmadaEn,
    DictamenOperacion Dictamen);
