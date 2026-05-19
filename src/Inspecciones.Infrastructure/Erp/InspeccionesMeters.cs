using System.Diagnostics.Metrics;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Métricas estándar del módulo Inspecciones via <see cref="Meter"/> de
/// <c>System.Diagnostics.Metrics</c>. App Insights / Azure Monitor pueden
/// scrapear este Meter sin requerir OpenTelemetry full.
///
/// MT4-INV-3 / D-MT4-4 — observabilidad pre-piloto.
///
/// Métricas expuestas:
/// - <c>inspecciones.erp.calls</c> — counter de llamadas al ERP. Tags:
///   <c>id_empresa</c>, <c>endpoint</c>, <c>resultado</c> ∈ {exito, fallo}.
///
/// Convención de naming: lowercase, puntos como separadores (estándar
/// OpenTelemetry semantic conventions).
///
/// Followup latente: histogram <c>inspecciones.command.duration</c> para los
/// 15 handlers HTTP (out of scope mt-4 — App Insights mide latencia HTTP por
/// endpoint nativamente).
/// </summary>
public static class InspeccionesMeters
{
    public const string MeterName = "Inspecciones";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Counter de llamadas al ERP Maquinaria_V4. Incrementar 1 por cada
    /// invocación HTTP exitosa o fallida (post-retry, pre-dead-letter).
    /// </summary>
    public static readonly Counter<long> ErpCalls = Meter.CreateCounter<long>(
        name: "inspecciones.erp.calls",
        unit: "{calls}",
        description: "Llamadas al ERP Maquinaria_V4 desde listeners y handlers");

    /// <summary>
    /// Registra una llamada al ERP con tags estándar. Tag <c>resultado</c>:
    /// "exito" o "fallo" (post-policy, no incluye retries intermedios).
    /// </summary>
    public static void RegistrarLlamadaErp(string? idEmpresa, string endpoint, string resultado)
    {
        ErpCalls.Add(1,
            new KeyValuePair<string, object?>("id_empresa", idEmpresa ?? "desconocido"),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("resultado", resultado));
    }
}
