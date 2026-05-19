using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;

namespace Inspecciones.Infrastructure.Erp.Listeners;

/// <summary>
/// Listener Wolverine que reacciona a <see cref="InspeccionFirmada_v1"/> y
/// llama a <c>PUT /api/v4/Maquinaria/api/equipos/{equipoCodigo}/dictamen-vigente</c>
/// (M-W-1) para propagar el dictamen de operación vigente al ERP.
///
/// Política de resiliencia (ADR-006 §16):
///   - PRE-L1: aggregate nulo o <see cref="Domain.Inspecciones.Inspeccion.Dictamen"/> == null
///     → <see cref="InvalidOperationException"/> → dead-letter inmediato.
///   - PRE-L3: dictamen no mapeable → <see cref="ArgumentOutOfRangeException"/> → dead-letter inmediato.
///   - 200 OK → éxito.
///   - 4xx → <see cref="MaquinariaErpException"/> → dead-letter (permanente, INV-L3).
///   - 5xx → <see cref="MaquinariaErpException"/> → Wolverine reintenta con backoff ADR-006.
/// </summary>
public sealed partial class SincronizarDictamenVigenteListener
{
    private readonly IInspeccionReader _inspeccionReader;
    private readonly IMaquinariaErpClient _erp;
    private readonly ILogger<SincronizarDictamenVigenteListener> _logger;

    public SincronizarDictamenVigenteListener(
        IInspeccionReader inspeccionReader,
        IMaquinariaErpClient erp,
        ILogger<SincronizarDictamenVigenteListener>? logger = null)
    {
        _inspeccionReader = inspeccionReader;
        _erp = erp;
        _logger = logger ?? NullLogger<SincronizarDictamenVigenteListener>.Instance;
    }

    /// <summary>
    /// Overload tenant-aware (mt-2 §6.5 + §6.6 del spec). Wolverine 3 inyecta el
    /// <see cref="Envelope"/> del mensaje entrante; el listener extrae el
    /// <c>TenantId</c> y lo propaga al <see cref="IInspeccionReader"/>. Si el
    /// envelope no trae tenant, lanza <see cref="TenantRequeridoEnEnvelopeException"/>
    /// → dead-letter inmediato (política ADR-006 §16 para errores permanentes).
    /// </summary>
    public async Task HandleAsync(InspeccionFirmada_v1 evento, Envelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var tenantId = envelope.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new TenantRequeridoEnEnvelopeException(
                nombreListener: nameof(SincronizarDictamenVigenteListener),
                messageId: envelope.Id);
        }

        var aggregate = await _inspeccionReader.LeerAsync(evento.InspeccionId, tenantId, ct).ConfigureAwait(false);
        await DespacharAsync(evento, aggregate, ct).ConfigureAwait(false);
    }

    public async Task HandleAsync(InspeccionFirmada_v1 evento, CancellationToken ct = default)
    {
        var aggregate = await _inspeccionReader.LeerAsync(evento.InspeccionId, ct).ConfigureAwait(false);
        await DespacharAsync(evento, aggregate, ct).ConfigureAwait(false);
    }

    private async Task DespacharAsync(InspeccionFirmada_v1 evento, Inspeccion? aggregate, CancellationToken ct)
    {
        if (aggregate is null)
        {
            // PRE-L1: stream no existe — indica bug en handler 1g. Dead-letter inmediato.
            throw new InvalidOperationException(
                $"Listener erp-3: stream no encontrado para InspeccionId {evento.InspeccionId}. " +
                "Posible bug en handler 1g. Dead-letter inmediato.");
        }

        if (aggregate.Dictamen is null)
        {
            // PRE-L1: DictamenEstablecido_v1 ausente del stream — estado corrupto. Dead-letter inmediato.
            throw new InvalidOperationException(
                $"Listener erp-3: Dictamen nulo en aggregate {evento.InspeccionId}. " +
                "El stream está corrupto (faltan eventos de slice 1g). Dead-letter inmediato.");
        }

        // PRE-L3: lanza ArgumentOutOfRangeException si el valor del enum no tiene mapeo definido.
        var estadoErp = MapearDictamen(aggregate.Dictamen.Value);

        var request = new ActualizarDictamenEquipoRequestDto { Estado = estadoErp };

        try
        {
            await _erp.ActualizarDictamenEquipoAsync(aggregate.EquipoId, request, ct).ConfigureAwait(false);
        }
        catch (MaquinariaErpException ex)
        {
            // INV-L2: fallo visible, no silencioso. Log estructurado antes de propagar
            // para que Wolverine aplique la política de reintentos/dead-letter.
            LogSyncFallida(
                evento.InspeccionId,
                aggregate.EquipoId,
                aggregate.Dictamen.Value.ToString(),
                intentosAgotados: 1,
                ultimoError: ex.StatusCode.HasValue
                    ? $"{(int)ex.StatusCode.Value} {ex.StatusCode.Value}"
                    : ex.Message,
                ex);
            throw;
        }
    }

    /// <summary>
    /// Mapea <see cref="DictamenOperacion"/> al entero esperado por el ERP (M-W-1).
    /// PuedeOperar=0, ConRestriccion=1, NoPuedeOperar=2.
    /// </summary>
    public static int MapearDictamen(DictamenOperacion dictamen) =>
        dictamen switch
        {
            DictamenOperacion.PuedeOperar    => 0,
            DictamenOperacion.ConRestriccion => 1,
            DictamenOperacion.NoPuedeOperar  => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dictamen),
                dictamen,
                $"DictamenOperacion '{dictamen}' no tiene mapeo definido para el ERP (PRE-L3). Dead-letter inmediato."),
        };

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "DictamenVigenteErpSyncFallida_v1 | InspeccionId={InspeccionId} EquipoId={EquipoId} " +
                  "Dictamen={Dictamen} IntentosAgotados={IntentosAgotados} UltimoError={UltimoError}")]
    private partial void LogSyncFallida(
        Guid inspeccionId,
        int equipoId,
        string? dictamen,
        int intentosAgotados,
        string ultimoError,
        Exception ex);
}

/// <summary>
/// Señal de observabilidad emitida cuando el listener agota reintentos sin éxito.
/// No es un evento de dominio — va a log estructurado y métrica (ADR-006 INV-L2).
/// </summary>
public sealed record DictamenVigenteErpSyncFallida_v1(
    Guid InspeccionId,
    int EquipoId,
    string? Dictamen,
    int IntentosAgotados,
    string UltimoError,
    bool EsReintentable = false);
