using Inspecciones.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace Inspecciones.Api.Tests.Auth;

/// <summary>
/// Implementación de <see cref="ISessionService"/> registrada por default en
/// env <c>Test</c> en <see cref="InspeccionesAppFactory"/>. Mantiene
/// backward-compat con los tests E2E pre-mt-1 que simulaban claims vía headers
/// HTTP:
/// <list type="bullet">
///   <item><c>X-Sin-Capability-Generar-OT</c> presente → remueve <c>generar-ot</c>.</item>
///   <item><c>X-Sin-Capability-Ejecutar</c> presente → remueve <c>ejecutar-inspeccion</c>.</item>
///   <item><c>X-Tecnico-Id: &lt;int&gt;</c> presente → override de <see cref="IdUsuario"/>
///   (los tests legacy mandan ints como "1" para contribuyente, "99" para externo
///   no contribuyente — consistente con D-MT1-6 / spec mt-1).</item>
/// </list>
///
/// Esta clase **NO es producción**. Solo existe para que el slice mt-1 pueda
/// refactorizar los 15 endpoints sin tocar la mayoría de los ~50 tests existentes
/// que ya están verde. Tests del slice mt-1 usan <see cref="FakeSessionService"/>
/// puro vía <c>InspeccionesAppFactory.WithSessionService(fake)</c>.
///
/// Cuando los slices siguientes (mt-2..mt-4) maduren la suite, esta clase puede
/// retirarse y los tests legacy migrarse a <c>WithSessionService</c>.
/// </summary>
public sealed class TestHeaderAwareSessionService(IHttpContextAccessor httpContextAccessor) : ISessionService
{
    private static readonly IReadOnlyCollection<string> CapabilitiesCompleto =
        ["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"];

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public int IdEmpresa => 1;

    public int IdUsuario
    {
        get
        {
            var headers = _httpContextAccessor.HttpContext?.Request.Headers;
            if (headers is not null
                && headers.TryGetValue("X-Tecnico-Id", out var values)
                && int.TryParse(values.ToString(),
                                System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var parsed))
            {
                return parsed;
            }
            return 1;
        }
    }

    public string NomUsuario => $"TestUser{IdUsuario}";

    public int IdSucursal => 0;
    public int IdProyecto => 0;

    public IReadOnlyCollection<string> Capabilities
    {
        get
        {
            var headers = _httpContextAccessor.HttpContext?.Request.Headers;
            if (headers is null)
            {
                return CapabilitiesCompleto;
            }

            var lista = new List<string>(CapabilitiesCompleto);
            if (headers.ContainsKey("X-Sin-Capability-Generar-OT"))
            {
                lista.Remove("generar-ot");
            }
            if (headers.ContainsKey("X-Sin-Capability-Ejecutar"))
            {
                lista.Remove("ejecutar-inspeccion");
            }
            return lista;
        }
    }
}
