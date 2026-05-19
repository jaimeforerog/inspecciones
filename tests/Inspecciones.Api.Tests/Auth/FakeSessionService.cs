using Inspecciones.Infrastructure.Auth;

namespace Inspecciones.Api.Tests.Auth;

/// <summary>
/// Fake en memoria de <see cref="ISessionService"/> para tests E2E. Reemplaza
/// <c>SincoMiddlewareSessionService</c> en env <c>Test</c> (decisión spec slice
/// mt-1 §12.B firmada) — paridad con el patrón del proyecto Attachment.
///
/// Defaults sensibles para el happy path:
/// <list type="bullet">
///   <item><c>IdEmpresa = 1</c>, <c>IdUsuario = 1</c></item>
///   <item><c>NomUsuario = "TestUser"</c></item>
///   <item><c>IdSucursal = 0</c>, <c>IdProyecto = 0</c></item>
///   <item><c>Capabilities = ["ejecutar-inspeccion","generar-ot","administrar-catalogos"]</c></item>
/// </list>
///
/// Para tests que necesitan denegar una capability o forzar un <c>IdUsuario</c>
/// distinto, se construye con los parámetros nombrados y se inyecta vía
/// <c>InspeccionesAppFactory.WithSessionService(fake)</c>.
///
/// Para simular una claim ausente (test §6.3 PRE-AUTH-3), se pasa
/// <c>lanzarEnClaim: "IdEmpresa"</c> — el getter correspondiente lanza
/// <see cref="ClaimRequeridaException"/>.
/// </summary>
public sealed class FakeSessionService : ISessionService
{
    private static readonly IReadOnlyCollection<string> CapabilitiesPorDefault =
        ["ejecutar-inspeccion", "generar-ot", "administrar-catalogos"];

    private readonly int _idEmpresa;
    private readonly int _idUsuario;
    private readonly string _nomUsuario;
    private readonly int _idSucursal;
    private readonly int _idProyecto;
    private readonly IReadOnlyCollection<string> _capabilities;
    private readonly string? _lanzarEnClaim;

    public FakeSessionService(
        int idEmpresa = 1,
        int idUsuario = 1,
        string nomUsuario = "TestUser",
        int idSucursal = 0,
        int idProyecto = 0,
        IReadOnlyCollection<string>? capabilities = null,
        string? lanzarEnClaim = null)
    {
        _idEmpresa = idEmpresa;
        _idUsuario = idUsuario;
        _nomUsuario = nomUsuario;
        _idSucursal = idSucursal;
        _idProyecto = idProyecto;
        _capabilities = capabilities ?? CapabilitiesPorDefault;
        _lanzarEnClaim = lanzarEnClaim;
    }

    public int IdEmpresa => _lanzarEnClaim == "IdEmpresa"
        ? throw new ClaimRequeridaException("IdEmpresa")
        : _idEmpresa;

    public int IdUsuario => _lanzarEnClaim == "UsuarioId"
        ? throw new ClaimRequeridaException("UsuarioId")
        : _idUsuario;

    public string NomUsuario => _nomUsuario;

    public int IdSucursal => _idSucursal;

    public int IdProyecto => _idProyecto;

    public IReadOnlyCollection<string> Capabilities => _capabilities;
}
