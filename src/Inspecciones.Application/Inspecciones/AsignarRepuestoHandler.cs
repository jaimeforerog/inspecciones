using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Inspecciones;
using Marten;

namespace Inspecciones.Application.Inspecciones;

/// <summary>
/// Handler del comando <see cref="AsignarRepuesto"/>. Orquesta:
/// <list type="number">
///   <item>PRE-F — carga el aggregate; lanza <see cref="InspeccionNoEncontradaException"/> si no existe.</item>
///   <item>PRE-H1 — valida que <c>SkuId</c> exista en <c>RepuestoLocal</c>.</item>
///   <item>Delega PRE-A, PRE-B1/B2, PRE-C, PRE-D, PRE-E, PRE-G al aggregate.</item>
///   <item>Append + commit atómico (un único <c>SaveChangesAsync</c>).</item>
/// </list>
/// </summary>
public sealed class AsignarRepuestoHandler(IDocumentSession session, TimeProvider time)
{
    private readonly IDocumentSession _session = session;
    private readonly TimeProvider _time = time;

    public async Task<AsignarRepuestoResult> ManejarAsync(
        AsignarRepuesto cmd,
        CancellationToken ct = default)
    {
        // PRE-F: el stream de la inspección debe existir.
        var inspeccion = await _session.Events.AggregateStreamAsync<Inspeccion>(cmd.InspeccionId, token: ct);
        if (inspeccion is null)
        {
            throw new InspeccionNoEncontradaException(
                $"Inspección {cmd.InspeccionId} no encontrada.");
        }

        // PRE-H1: SkuId debe existir en el catálogo local RepuestoLocal.
        var repuestoLocal = await _session.LoadAsync<RepuestoLocal>(cmd.SkuId, ct);
        if (repuestoLocal is null)
        {
            throw new RepuestoNoEncontradoEnCatalogoException(
                $"El SKU {cmd.SkuId} no existe en el catálogo local. Refresca el catálogo de inventario.");
        }

        // Delegar PRE-A, PRE-B1, PRE-B2, PRE-C, PRE-D, PRE-E, PRE-G al aggregate.
        var ahora = _time.GetUtcNow();
        var eventos = inspeccion.AsignarRepuesto(
            cmd,
            skuCodigo: repuestoLocal.CodigoSinco,
            unidad: repuestoLocal.UnidadMedida,
            ahora: ahora);

        // PRE-D: idempotencia — RepuestoId ya existe en el stream. Devuelve estado actual.
        if (eventos.Count == 0)
        {
            var repuestoExistente = inspeccion.Repuestos.First(r => r.RepuestoId == cmd.RepuestoId);
            return new AsignarRepuestoResult(
                RepuestoId: repuestoExistente.RepuestoId,
                SkuId: repuestoExistente.SkuId,
                SkuCodigo: repuestoExistente.SkuCodigo,
                Cantidad: repuestoExistente.Cantidad,
                Unidad: repuestoExistente.Unidad,
                Justificacion: repuestoExistente.Justificacion,
                AsignadoEn: ahora); // proxy — timestamp real no persiste en estado (followup análogo a #16)
        }

        // Append al stream — un único SaveChangesAsync (atomicidad CLAUDE.md).
        _session.Events.Append(cmd.InspeccionId, eventos);
        await _session.SaveChangesAsync(ct);

        var evento = (RepuestoEstimado_v1)eventos[0];
        return new AsignarRepuestoResult(
            RepuestoId: evento.RepuestoId,
            SkuId: evento.SkuId,
            SkuCodigo: evento.SkuCodigo,
            Cantidad: evento.Cantidad,
            Unidad: evento.Unidad,
            Justificacion: evento.Justificacion,
            AsignadoEn: evento.AsignadoEn);
    }
}

/// <summary>Resultado de ejecutar <see cref="AsignarRepuesto"/>.</summary>
public sealed record AsignarRepuestoResult(
    Guid RepuestoId,
    int SkuId,
    string SkuCodigo,
    decimal Cantidad,
    string Unidad,
    string? Justificacion,
    DateTimeOffset AsignadoEn);