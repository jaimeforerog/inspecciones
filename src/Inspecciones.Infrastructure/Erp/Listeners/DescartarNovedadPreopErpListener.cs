using System.Net;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Erp.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inspecciones.Infrastructure.Erp.Listeners;

/// <summary>
/// Listener Wolverine que reacciona a <see cref="NovedadPreopDescartada_v1"/> y
/// llama a <c>POST /preoperacional-fallas/cerrar</c> (P-6) en Maquinaria_V4.
///
/// Política de resiliencia (ADR-006 §16):
///   - PRE-L1: <see cref="NovedadId"/> == 0 → <see cref="ArgumentException"/> → dead-letter inmediato.
///   - 200 OK (cerradasAhora >= 1 o yaCerradas >= 1) → éxito.
///   - 409 YA_CERRADO → éxito (idempotencia natural, D-1).
///   - 409 otro código → <see cref="MaquinariaErpException"/> → dead-letter (permanente, INV-L3).
///   - 4xx → <see cref="MaquinariaErpException"/> → dead-letter (permanente, INV-L3).
///   - 5xx → <see cref="MaquinariaErpException"/> → Wolverine reintenta con backoff ADR-006.
/// </summary>
public sealed partial class DescartarNovedadPreopErpListener
{
    private readonly IMaquinariaErpClient _erp;
    private readonly ILogger<DescartarNovedadPreopErpListener> _logger;

    public DescartarNovedadPreopErpListener(
        IMaquinariaErpClient erp,
        ILogger<DescartarNovedadPreopErpListener>? logger = null)
    {
        _erp = erp;
        _logger = logger ?? NullLogger<DescartarNovedadPreopErpListener>.Instance;
    }

    public async Task HandleAsync(NovedadPreopDescartada_v1 evento, CancellationToken ct = default)
    {
        // PRE-L1: NovedadId debe ser > 0. MotivoDescarte vacío se permite pasar al ERP
        // (defensa en profundidad: el ERP retorna 400 si lo rechaza — ver spec §6.5).
        if (evento.NovedadId <= 0)
        {
            throw new ArgumentException(
                $"Evento malformado (PRE-L1): NovedadId={evento.NovedadId} no es válido. " +
                $"InspeccionId={evento.InspeccionId}. Dead-letter inmediato.",
                nameof(evento));
        }

        var request = new CerrarPreoperacionalFallasRequestDto
        {
            PodIds = [evento.NovedadId],
            Observaciones = evento.MotivoDescarte ?? string.Empty,
        };

        try
        {
            await _erp.CerrarPreoperacionalFallasAsync(request, ct).ConfigureAwait(false);
        }
        catch (MaquinariaErpException ex)
            when (ex.StatusCode == HttpStatusCode.Conflict
                  && ex.CodigoErp == "YA_CERRADO")
        {
            // D-1: 409 YA_CERRADO es idempotencia natural del ERP — tratado como éxito silencioso.
            // No relanzar; el outbox se marcará como completado.
            return;
        }
        catch (MaquinariaErpException ex)
        {
            // INV-L2: fallo visible, no silencioso. Emite señal de observabilidad antes de
            // propagar para que Wolverine aplique la política de reintentos/dead-letter.
            var esReintentable = ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500;
            LogCierreFallido(
                evento.InspeccionId,
                evento.NovedadId,
                ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null,
                ex.CodigoErp,
                esReintentable,
                ex);
            throw;
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "NovedadPreopErpCierreFallido_v1 | InspeccionId={InspeccionId} NovedadId={NovedadId} " +
                  "StatusCode={StatusCode} CodigoErp={CodigoErp} EsReintentable={EsReintentable}")]
    private partial void LogCierreFallido(
        Guid inspeccionId,
        int novedadId,
        int? statusCode,
        string? codigoErp,
        bool esReintentable,
        Exception ex);
}

/// <summary>
/// Señal de observabilidad emitida cuando el listener agota reintentos sin éxito.
/// No es un evento de dominio — va a log estructurado y métrica (ADR-006 INV-L2).
/// </summary>
public sealed record NovedadPreopErpCierreFallido_v1(
    Guid InspeccionId,
    int NovedadId,
    int IntentosAgotados,
    string UltimoError,
    bool EsReintentable = false);
