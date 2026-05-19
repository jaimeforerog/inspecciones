using SincoSoft.MYE.Common.Middleware;

namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Implementación de producción de <see cref="ISessionService"/>. Lee las claims
/// del JWT del host PWA Sinco MYE via <c>MiddlewareAuthorizationToken.SessionVariables()</c>
/// (paquete corporativo <c>SincoSoft.MYE.Common 1.5.1</c>).
///
/// Patrón calcado del proyecto Attachment
/// (<c>C:\Fuentes\FuentesNET3.0\AzureV4\Attachment\src\Attachment\Core\Application\BussinessLogic\SessionService.cs</c>),
/// extendido para los 5 claims del contrato de Inspecciones (Attachment solo usa
/// <c>IdUsuario</c> e <c>IdEmpresa</c>).
///
/// El método estático <c>SessionVariables()</c> devuelve un <c>dynamic</c>; los miembros
/// se acceden por nombre y se castean. Si una claim crítica está ausente, se lanza
/// <see cref="ClaimRequeridaException"/> que el handler global mapea a 401.
///
/// D-MT1-1 / D-MT1-4 / FU-54 — spec slice mt-1.
/// </summary>
public sealed class SincoMiddlewareSessionService : ISessionService
{
    private static readonly IReadOnlyCollection<string> CapabilitiesDefault =
        ["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"];

    private readonly Lazy<dynamic> _sessionVars =
        new(() => MiddlewareAuthorizationToken.SessionVariables());

    public int IdEmpresa => LeerEntero("IdEmpresa");
    public int IdUsuario => LeerEntero("UsuarioId");
    public string NomUsuario => LeerStringOpcional("NomUsuario", string.Empty);
    public int IdSucursal => LeerEnteroOpcional("IdSucursal", defaultValue: 0);
    public int IdProyecto => LeerEnteroOpcional("IdProyecto", defaultValue: 0);

    public IReadOnlyCollection<string> Capabilities
    {
        get
        {
            // D-MT1-4 + FU-54: si el host no propaga la claim, default always-allow.
            // Si llegan strings (CSV) o array, los normalizamos.
            try
            {
                dynamic raw = _sessionVars.Value.Capabilities;
                if (raw is null)
                {
                    return CapabilitiesDefault;
                }

                if (raw is string csv)
                {
                    var partes = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return partes.Length == 0 ? CapabilitiesDefault : partes;
                }

                if (raw is IEnumerable<string> stringList)
                {
                    var lista = stringList.ToArray();
                    return lista.Length == 0 ? CapabilitiesDefault : lista;
                }
            }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // La claim no existe en este JWT — fallback al default.
            }

            return CapabilitiesDefault;
        }
    }

    private int LeerEntero(string nombreClaim)
    {
        try
        {
            dynamic? value = LeerMiembro(nombreClaim);
            if (value is null)
            {
                throw new ClaimRequeridaException(nombreClaim);
            }

            return value switch
            {
                int i => i,
                long l => checked((int)l),
                string s when int.TryParse(s, System.Globalization.NumberStyles.Integer,
                                           System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => throw new ClaimRequeridaException(nombreClaim)
            };
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            throw new ClaimRequeridaException(nombreClaim);
        }
    }

    private int LeerEnteroOpcional(string nombreClaim, int defaultValue)
    {
        try
        {
            dynamic? value = LeerMiembro(nombreClaim);
            return value switch
            {
                null => defaultValue,
                int i => i,
                long l => checked((int)l),
                string s when int.TryParse(s, System.Globalization.NumberStyles.Integer,
                                           System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => defaultValue
            };
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return defaultValue;
        }
    }

    private string LeerStringOpcional(string nombreClaim, string defaultValue)
    {
        try
        {
            dynamic? value = LeerMiembro(nombreClaim);
            return value switch
            {
                null => defaultValue,
                string s => s,
                _ => value.ToString() ?? defaultValue
            };
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return defaultValue;
        }
    }

    private dynamic? LeerMiembro(string nombre)
    {
        // El _sessionVars es un objeto dynamic — accedemos por nombre.
        // Si la propiedad no existe, lanza RuntimeBinderException (capturada arriba).
        return nombre switch
        {
            "UsuarioId" => _sessionVars.Value.IdUsuario,
            "IdEmpresa" => _sessionVars.Value.IdEmpresa,
            "IdSucursal" => _sessionVars.Value.IdSucursal,
            "IdProyecto" => _sessionVars.Value.IdProyecto,
            "NomUsuario" => _sessionVars.Value.NomUsuario,
            _ => throw new ArgumentException($"Claim desconocida: {nombre}", nameof(nombre))
        };
    }
}
