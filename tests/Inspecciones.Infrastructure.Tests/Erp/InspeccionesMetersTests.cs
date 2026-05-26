using System.Diagnostics.Metrics;
using FluentAssertions;
using Inspecciones.Infrastructure.Erp;

namespace Inspecciones.Infrastructure.Tests.Erp;

/// <summary>
/// Tests del counter <c>inspecciones.erp.calls</c> expuesto por
/// <see cref="InspeccionesMeters"/>. Cierra FU-64 (review post-mt-4 §4):
/// hasta este slice la métrica solo se incrementaba en runtime — un refactor
/// podía eliminarla por error y nada lo detectaba.
///
/// Patrón: <see cref="MeterListener"/> de BCL captura cada <c>Counter.Add()</c>
/// con sus tags. Evita reflexión y no depende de OpenTelemetry ni App Insights.
///
/// Importante: el <see cref="Meter"/> es estático global (<see cref="InspeccionesMeters.MeterName"/>
/// = "Inspecciones"). Los tests aíslan por instancia de listener; cada uno
/// engancha sólo cuando el meter coincide con el nombre esperado.
/// </summary>
public sealed class InspeccionesMetersTests
{
    [Fact]
    public void RegistrarLlamadaErp_con_exito_emite_counter_con_tags_id_empresa_endpoint_resultado()
    {
        var medidas = CapturarMedidas(() =>
            InspeccionesMeters.RegistrarLlamadaErp(
                idEmpresa: "7",
                endpoint: "dictamen-vigente",
                resultado: "exito"));

        medidas.Should().ContainSingle();
        var (valor, tags) = medidas[0];
        valor.Should().Be(1);
        tags["id_empresa"].Should().Be("7");
        tags["endpoint"].Should().Be("dictamen-vigente");
        tags["resultado"].Should().Be("exito");
    }

    [Fact]
    public void RegistrarLlamadaErp_con_fallo_emite_counter_resultado_fallo()
    {
        var medidas = CapturarMedidas(() =>
            InspeccionesMeters.RegistrarLlamadaErp(
                idEmpresa: "42",
                endpoint: "preoperacional-fallas-cerrar",
                resultado: "fallo"));

        medidas.Should().ContainSingle();
        medidas[0].Tags["resultado"].Should().Be("fallo");
        medidas[0].Tags["endpoint"].Should().Be("preoperacional-fallas-cerrar");
        medidas[0].Tags["id_empresa"].Should().Be("42");
    }

    [Fact]
    public void RegistrarLlamadaErp_con_idEmpresa_null_etiqueta_como_desconocido()
    {
        // MT4-INV-3 defensa: si el envelope llega sin TenantId (caso patológico
        // post-mt-2, dead-letter inmediato vía TenantRequeridoEnEnvelopeException),
        // la métrica de fallo registra "desconocido" como tag — permite que la
        // alerta operativa por spike sea visible aunque el tenant se pierda.
        var medidas = CapturarMedidas(() =>
            InspeccionesMeters.RegistrarLlamadaErp(
                idEmpresa: null,
                endpoint: "dictamen-vigente",
                resultado: "fallo"));

        medidas.Should().ContainSingle();
        medidas[0].Tags["id_empresa"].Should().Be("desconocido");
    }

    [Fact]
    public void Varias_llamadas_incrementan_el_counter_acumulando_por_emisión()
    {
        var medidas = CapturarMedidas(() =>
        {
            InspeccionesMeters.RegistrarLlamadaErp("7", "dictamen-vigente", "exito");
            InspeccionesMeters.RegistrarLlamadaErp("7", "dictamen-vigente", "exito");
            InspeccionesMeters.RegistrarLlamadaErp("8", "dictamen-vigente", "fallo");
        });

        medidas.Should().HaveCount(3);
        medidas.Should().AllSatisfy(m => m.Valor.Should().Be(1));
        medidas.Count(m => m.Tags["id_empresa"] == "7" && m.Tags["resultado"] == "exito")
            .Should().Be(2);
        medidas.Count(m => m.Tags["id_empresa"] == "8" && m.Tags["resultado"] == "fallo")
            .Should().Be(1);
    }

    [Fact]
    public void Counter_tiene_nombre_estable_inspecciones_erp_calls()
    {
        // Naming convention OpenTelemetry: lowercase con puntos. Si alguien lo
        // renombra accidentalmente, los dashboards de App Insights se rompen.
        InspeccionesMeters.ErpCalls.Name.Should().Be("inspecciones.erp.calls");
    }

    // ── Helper: captura emisiones del counter durante la ejecución de action ──

    private static List<Medida> CapturarMedidas(Action action)
    {
        var medidas = new List<Medida>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == InspeccionesMeters.MeterName &&
                    instrument.Name == "inspecciones.erp.calls")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var kv in tags)
            {
                dict[kv.Key] = kv.Value?.ToString();
            }
            medidas.Add(new Medida(value, dict));
        });

        listener.Start();

        action();

        listener.Dispose();

        return medidas;
    }

    private sealed record Medida(long Valor, IReadOnlyDictionary<string, string?> Tags);
}
