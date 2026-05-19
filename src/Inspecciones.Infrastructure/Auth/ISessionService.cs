namespace Inspecciones.Infrastructure.Auth;

/// <summary>
/// Puerto de identidad del host PWA Sinco MYE. Expone los 5 claims canónicos
/// del JWT (contrato <c>06-contrato-apis-erp.md §0.B.5</c>) más la lista de
/// capabilities derivadas del rol ERP del usuario.
///
/// Implementaciones:
/// <list type="bullet">
///   <item><see cref="SincoMiddlewareSessionService"/> — producción. Lee de
///   <c>MiddlewareAuthorizationToken.SessionVariables()</c> del paquete
///   <c>SincoSoft.MYE.Common</c> (paridad 1:1 con proyecto Attachment).</item>
///   <item><c>FakeSessionService</c> — tests E2E. Vive en
///   <c>tests/Inspecciones.Api.Tests/Auth/</c> y se registra por default en
///   env <c>Test</c> (decisión spec slice mt-1 §12.B firmada).</item>
/// </list>
///
/// Reglas duras (regla nueva CLAUDE.md mt-1):
/// <list type="bullet">
///   <item>Todo endpoint HTTP lee identidad vía este puerto.</item>
///   <item>Prohibido leer <c>HttpContext.User</c> o claims directamente en
///   endpoints o handlers.</item>
/// </list>
///
/// Spec slice mt-1 §2 + decisiones D-MT1-1..D-MT1-10.
/// </summary>
public interface ISessionService
{
    /// <summary>Empresa activa del host (claim <c>IdEmpresa</c>). D-MT1-1: int, paridad Attachment.</summary>
    int IdEmpresa { get; }

    /// <summary>Usuario activo del host (claim <c>UsuarioId</c>). El dominio lo trata como string opaco — ver D-MT1-6.</summary>
    int IdUsuario { get; }

    /// <summary>Nombre legible del usuario (claim <c>NomUsuario</c>). Diagnóstico — no enforcement.</summary>
    string NomUsuario { get; }

    /// <summary>Sucursal activa (claim <c>IdSucursal</c>). 0 cuando no aplica.</summary>
    int IdSucursal { get; }

    /// <summary>Proyecto activo de la sesión (claim <c>IdProyecto</c>). 0 cuando no aplica. Enforcement cross-proyecto difiere a mt-2 (D-MT1-5/§12.A.3).</summary>
    int IdProyecto { get; }

    /// <summary>
    /// Capabilities derivadas del rol ERP por el host PWA. Set canónico actual:
    /// <c>ejecutar-inspeccion</c>, <c>generar-ot</c>, <c>administrar-catalogos</c>.
    ///
    /// D-MT1-4 + FU-54: si el JWT no expone la claim <c>capabilities</c>, las implementaciones
    /// reales (no-test) devuelven el set completo (always-allow) hasta que el host
    /// confirme el contrato. <c>FakeSessionService</c> permite forzar conjuntos vacíos
    /// para los tests de §6.4 / §6.5.
    /// </summary>
    IReadOnlyCollection<string> Capabilities { get; }
}
