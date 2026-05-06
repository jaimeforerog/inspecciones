using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Inspecciones;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// Mapeo del endpoint <c>POST /api/v1/inspecciones</c>. Spec slice 1b §9.
/// </summary>
public static class InspeccionesEndpoints
{
    /// <summary>Registra los endpoints HTTP del slice 1b en el <c>WebApplication</c>.</summary>
    public static IEndpointRouteBuilder MapInspeccionesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/inspecciones", async (
                IniciarInspeccionRequest request,
                IniciarInspeccionHandler handler,
                HttpContext ctx,
                CancellationToken ct) =>
            {
                // PRE-1 capa HTTP — header X-Client-Command-Id requerido (ADR-008 §9.16).
                if (!ctx.Request.Headers.TryGetValue("X-Client-Command-Id", out var clientCommandIdValues)
                    || string.IsNullOrWhiteSpace(clientCommandIdValues.ToString()))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "HEADER-REQUERIDO",
                        mensaje = "El header X-Client-Command-Id es requerido (ADR-008)."
                    });
                }

                // Claims mock — ADR-002 tentativo. El host PWA inyectará los claims reales en
                // el JWT; por ahora usamos un mock fijo compatible con los tests E2E del slice 1b.
                // Cuando el módulo se integre al host, este bloque se reemplaza por extracción del JWT.
                var claims = new ClaimsTecnico(
                    TecnicoIniciador: "rmartinez",
                    ProyectosAsignados: new HashSet<int> { request.ProyectoId },
                    TieneCapabilityEjecutarInspeccion: true);

                var cmd = new IniciarInspeccion(
                    InspeccionId: request.InspeccionId,
                    EquipoId: request.EquipoId,
                    ProyectoId: request.ProyectoId,
                    UbicacionInicio: request.UbicacionInicio,
                    FechaReportada: request.FechaReportada,
                    LecturaMedidorPrimario: request.LecturaMedidorPrimario,
                    LecturaMedidorSecundario: request.LecturaMedidorSecundario);

                try
                {
                    var resultado = await handler.ManejarAsync(cmd, claims, ct);

                    var response = new IniciarInspeccionResponse(
                        InspeccionId: resultado.InspeccionId,
                        RedirigeAExistente: resultado.RedirigeAExistente,
                        Version: resultado.Version,
                        Mensaje: resultado.Mensaje);

                    if (resultado.RedirigeAExistente)
                    {
                        return Results.Ok(response);
                    }

                    return Results.Created(
                        uri: $"/api/v1/inspecciones/{resultado.InspeccionId}",
                        value: response);
                }
                catch (EquipoNoEncontradoException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-3", mensaje = ex.Message });
                }
                catch (ProyectoNoAutorizadoException)
                {
                    return Results.Forbid();
                }
                catch (InspeccionDomainException ex)
                {
                    // Mapeo de código de error según tipo de excepción (spec §9).
                    // Preserva el contrato de status 422 para todas las InspeccionDomainException
                    // no capturadas por los catch específicos anteriores.
                    var codigoError = ex switch
                    {
                        RutinaTecnicaNoSincronizadaException => "I-I2",
                        EquipoSinRutinaTecnicaException      => "I-I2",
                        FechaReportadaFueraDeRangoException  => "I-I3",
                        EquipoNoPerteneceAProyectoException  => "PRE-4",
                        CapabilityRequeridaException         => "PRE-1",
                        _                                    => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("IniciarInspeccion");

        // ── Slice 1c — RegistrarHallazgo ────────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{id:guid}/hallazgos", async (
                Guid id,
                RegistrarHallazgoRequest request,
                RegistrarHallazgoHandler handler,
                HttpContext ctx,
                CancellationToken ct) =>
            {
                // Header X-Client-Command-Id requerido (ADR-008 §9.16).
                if (!ctx.Request.Headers.TryGetValue("X-Client-Command-Id", out var clientCommandIdValues)
                    || string.IsNullOrWhiteSpace(clientCommandIdValues.ToString()))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "HEADER-REQUERIDO",
                        mensaje = "El header X-Client-Command-Id es requerido (ADR-008)."
                    });
                }

                // Claims mock — ADR-002 tentativo. El host PWA inyectará los claims reales en el JWT.
                const string tecnicoId = "rmartinez";

                var cmd = new RegistrarHallazgo(
                    InspeccionId: id,
                    HallazgoId: request.HallazgoId,
                    Origen: request.Origen,
                    ParteEquipoId: request.ParteEquipoId,
                    NovedadPreopOrigenId: request.NovedadPreopOrigenId,
                    ActividadId: request.ActividadId,
                    ActividadDescripcion: request.ActividadDescripcion,
                    NovedadTecnica: request.NovedadTecnica,
                    AccionRequerida: request.AccionRequerida,
                    AccionCorrectiva: request.AccionCorrectiva,
                    TipoFallaId: request.TipoFallaId,
                    CausaFallaId: request.CausaFallaId,
                    ObservacionCampo: request.ObservacionCampo,
                    Ubicacion: request.Ubicacion,
                    EmitidoPor: tecnicoId);

                try
                {
                    var resultado = await handler.ManejarAsync(cmd, ct);

                    var response = new RegistrarHallazgoResponse(
                        HallazgoId: resultado.HallazgoId,
                        InspeccionId: resultado.InspeccionId,
                        AccionRequerida: resultado.AccionRequerida,
                        RegistradoEn: resultado.RegistradoEn);

                    return Results.Created(
                        uri: $"/api/v1/inspecciones/{id}/hallazgos/{resultado.HallazgoId}",
                        value: response);
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        ParteNoCorrespondeAlEquipoException    => "INV-PartePerteneceAlEquipo",
                        InspeccionNoEnEjecucionException       => "I2",
                        NovedadPreopOrigenIdRequeridoException => "I-H2",
                        NovedadPreopOrigenIdNoPermitidoException => "I-H3",
                        TipoYCausaFallaRequeridosException     => "I-H4",
                        AccionCorrectivaRequeridaException     => "PRE-8",
                        NovedadTecnicaVaciaException           => "PRE-9",
                        OrigenNoSoportadoException             => "PRE-10",
                        _                                      => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("RegistrarHallazgo");

        // ── Slice 1d — ActualizarHallazgo ───────────────────────────────────
        app.MapPut("/api/v1/inspecciones/{inspeccionId:guid}/hallazgos/{hallazgoId:guid}", async (
                Guid inspeccionId,
                Guid hallazgoId,
                ActualizarHallazgoRequest request,
                ActualizarHallazgoHandler handler,
                HttpContext ctx,
                CancellationToken ct) =>
            {
                // Header X-Client-Command-Id requerido (ADR-008 §9.16).
                if (!ctx.Request.Headers.TryGetValue("X-Client-Command-Id", out var clientCommandIdValues)
                    || string.IsNullOrWhiteSpace(clientCommandIdValues.ToString()))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "HEADER-REQUERIDO",
                        mensaje = "El header X-Client-Command-Id es requerido (ADR-008)."
                    });
                }

                // Claims mock — ADR-002 tentativo. El host PWA inyectará los claims reales en el JWT.
                const string tecnicoId = "rmartinez";

                var cmd = new ActualizarHallazgo(
                    InspeccionId: inspeccionId,
                    HallazgoId: hallazgoId,
                    NovedadTecnica: request.NovedadTecnica,
                    AccionRequerida: request.AccionRequerida,
                    AccionCorrectiva: request.AccionCorrectiva,
                    TipoFallaId: request.TipoFallaId,
                    CausaFallaId: request.CausaFallaId,
                    ObservacionCampo: request.ObservacionCampo,
                    UbicacionGps: request.UbicacionGps,
                    EmitidoPor: tecnicoId);

                try
                {
                    var resultado = await handler.ManejarAsync(cmd, ct);

                    var response = new ActualizarHallazgoResponse(
                        HallazgoId: resultado.HallazgoId,
                        InspeccionId: resultado.InspeccionId,
                        AccionRequerida: resultado.AccionRequerida,
                        ActualizadoEn: resultado.ActualizadoEn);

                    return Results.Ok(response);
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-F", mensaje = ex.Message });
                }
                catch (HallazgoNoEncontradoException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-B1", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoEnEjecucionException           => "PRE-A",
                        HallazgoEliminadoException                 => "PRE-B2",
                        NovedadTecnicaVaciaException               => "PRE-C",
                        CamposIntervencionNoPermitidosException    => "PRE-E",
                        TipoYCausaFallaRequeridosException         => "PRE-D1",
                        AccionCorrectivaRequeridaException         => "PRE-D2",
                        _                                          => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("ActualizarHallazgo");

        return app;
    }
}
