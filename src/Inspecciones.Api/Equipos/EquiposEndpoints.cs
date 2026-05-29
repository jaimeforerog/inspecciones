using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inspecciones.Api.Equipos;

/// <summary>
/// Endpoints HTTP del recurso <c>equipos</c> orientados al rol técnico. Reemplazan
/// el workaround admin (<c>GET /api/v1/admin/catalogo-equipo/{equipoId}</c>, que
/// exige capability <c>administrar-catalogos</c>) para el caso de uso de campo:
/// el técnico consulta las partes del equipo que va a inspeccionar.
/// </summary>
public static class EquiposEndpoints
{
    private const string MensajeCapabilityEjecutarInspeccion = "Capability 'ejecutar-inspeccion' requerida.";

    /// <summary>Registra los endpoints HTTP del recurso <c>equipos</c>.</summary>
    public static IEndpointRouteBuilder MapEquiposEndpoints(this IEndpointRouteBuilder app)
    {
        // ── GET /api/v1/equipos/{equipoId}/partes ───────────────────────────
        // Fuente: EquipoLocal en Marten (mismo documento que usa el handler de
        // RegistrarHallazgo para validar INV-PartePerteneceAlEquipo). El tenant lo
        // resuelve el IQuerySession ambient vía IdEmpresa (claim ausente ⇒ 401).
        app.MapGet("/api/v1/equipos/{equipoId:int}/partes", async (
                int equipoId,
                IQuerySession query,
                ISessionService session,
                CancellationToken ct) =>
            {
                // PRE-1 — capability "ejecutar-inspeccion" requerida.
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var equipo = await query.LoadAsync<EquipoLocal>(equipoId, ct);
                if (equipo is null)
                {
                    return Results.NotFound(new
                    {
                        codigoError = "NO_SINCRONIZADO",
                        mensaje = $"El equipo {equipoId} no está en el catálogo local. Sincronízalo antes de inspeccionar."
                    });
                }

                var partes = (equipo.Partes ?? Array.Empty<ParteEquipoLocal>())
                    .Select(p => new
                    {
                        parteEquipoId = p.ParteEquipoId,
                        parteCodigo = p.ParteCodigo,
                        parteNombre = p.ParteNombre
                    })
                    .ToList();

                return Results.Ok(new { equipoId = equipo.EquipoId, partes });
            })
           .WithName("ListarPartesEquipo");

        return app;
    }

    /// <summary>
    /// Construye una respuesta HTTP 403 Forbidden con body <c>{ codigoError, mensaje }</c>.
    /// Reemplaza <c>Results.Forbid()</c> que requiere <c>IAuthenticationService</c>
    /// (no registrado — ADR-002, mismo patrón que <c>InspeccionesEndpoints</c>).
    /// </summary>
    private static IResult Forbidden403(string codigoError, string mensaje)
        => Results.Json(new { codigoError, mensaje }, statusCode: 403);
}
