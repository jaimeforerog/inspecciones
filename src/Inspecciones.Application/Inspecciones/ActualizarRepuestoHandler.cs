using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="ActualizarRepuesto"/>. Spec slice 1o §4.
/// </summary>
public sealed class ActualizarRepuestoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    public async Task<ActualizarRepuestoResult> Handle(
        ActualizarRepuesto cmd,
        CancellationToken ct = default)
    {
        // PRE-1: cargar el aggregate; lanza si no existe.
        var aggregate = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (aggregate is null)
        {
            throw new InspeccionNoEncontradaException(
                $"No existe una inspección con Id {cmd.InspeccionId}.");
        }

        // P-2 (opción A): normalizar ObservacionNueva vacía/whitespace a null en el handler.
        var observacionNormalizada = string.IsNullOrWhiteSpace(cmd.ObservacionNueva)
            ? null
            : cmd.ObservacionNueva;

        // Reconstruir el comando normalizado si es necesario.
        var cmdNormalizado = cmd with { ObservacionNueva = observacionNormalizada };

        // Delegar PRE-2..PRE-8 al aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = aggregate.ActualizarRepuesto(cmdNormalizado, ahora);

        // Append atómico — un único SaveChangesAsync (spec §7).
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        // Estado post-update: leer el repuesto del aggregate pre-update y aplicar el delta del evento emitido.
        var repuestoAntes = aggregate.Repuestos.Single(r => r.RepuestoId == cmd.RepuestoId);
        var eventoEmitido = (RepuestoActualizado_v1)eventos[0];
        var repuesto = repuestoAntes with
        {
            Cantidad      = eventoEmitido.Cantidad      ?? repuestoAntes.Cantidad,
            Justificacion = eventoEmitido.Justificacion ?? repuestoAntes.Justificacion,
        };

        return new ActualizarRepuestoResult(
            InspeccionId: cmd.InspeccionId,
            HallazgoId: cmd.HallazgoId,
            RepuestoId: cmd.RepuestoId,
            Cantidad: repuesto.Cantidad,
            Justificacion: repuesto.Justificacion,
            ActualizadoEn: ahora);
    }
}

