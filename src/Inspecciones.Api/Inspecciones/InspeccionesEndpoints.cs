using System.Globalization;
using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Inspecciones.Infrastructure.Erp;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1 §9.5).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                // PRE-AUTH-3 — fuerza lectura de IdEmpresa para enforcement temprano del
                // claim crítico. Si está ausente, el getter lanza ClaimRequeridaException
                // que el middleware global mapea a 401. En mt-2 este valor se usa como
                // tenant_id de Marten (conjoined).
                _ = session.IdEmpresa;

                // Claims derivadas del JWT del host (ADR-002 cerrado por spec mt-1, D-MT1-5/6).
                // TecnicoIniciador = IdUsuario.ToString() — string opaco para el dominio.
                // ProyectosAsignados: mt-1 preserva el comportamiento mock (always-allow) — el
                // enforcement cross-proyecto se difiere a mt-2 (decisión spec §12.A.3 firmada).
                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);
                var claims = new ClaimsTecnico(
                    TecnicoIniciador: tecnicoId,
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
                catch (ProyectoNoAutorizadoException ex)
                {
                    return Forbidden403("PRE-3-PROYECTO", ex.Message);
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

        // ── Slice 1h — IniciarInspeccionMonitoreo ──────────────────────────
        app.MapPost("/api/v1/inspecciones/monitoreo", async (
                IniciarInspeccionMonitoreoRequest request,
                IniciarInspeccionMonitoreoHandler handler,
                ISessionService session,
                HttpContext ctx,
                CancellationToken ct) =>
            {
                if (!ctx.Request.Headers.TryGetValue("X-Client-Command-Id", out var clientCommandIdValues)
                    || string.IsNullOrWhiteSpace(clientCommandIdValues.ToString()))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "HEADER-REQUERIDO",
                        mensaje = "El header X-Client-Command-Id es requerido (ADR-008)."
                    });
                }

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);
                var claims = new ClaimsTecnico(
                    TecnicoIniciador: tecnicoId,
                    ProyectosAsignados: new HashSet<int> { request.ProyectoId },
                    TieneCapabilityEjecutarInspeccion: true);

                var cmd = new IniciarInspeccionMonitoreo(
                    InspeccionId: request.InspeccionId,
                    EquipoId: request.EquipoId,
                    ProyectoId: request.ProyectoId,
                    RutinaMonitoreoId: request.RutinaMonitoreoId,
                    IniciadaPor: claims.TecnicoIniciador,
                    Ubicacion: request.Ubicacion,
                    FechaReportada: request.FechaReportada,
                    LecturaMedidorPrimario: request.LecturaMedidorPrimario,
                    LecturaMedidorSecundario: request.LecturaMedidorSecundario,
                    Capabilities: session.Capabilities);

                try
                {
                    var resultado = await handler.ManejarAsync(cmd, claims, ct);

                    var response = new IniciarInspeccionMonitoreoResponse(
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
                catch (ProyectoNoAutorizadoException ex)
                {
                    return Forbidden403("PRE-3-PROYECTO", ex.Message);
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        RutinaMonitoreoNoSincronizadaException => "I-I-Mon-0",
                        RutinaNoAplicableAlGrupoException      => "I-I-Mon-2",
                        EquipoSinRutinasMonitoreoException     => "I-I-Mon-1",
                        FechaReportadaFueraDeRangoException    => "I-I3",
                        EquipoNoPerteneceAProyectoException    => "PRE-4",
                        CapabilityRequeridaException           => "PRE-1",
                        _                                      => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("IniciarInspeccionMonitoreo");

        // ── Slice 1c — RegistrarHallazgo ────────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{id:guid}/hallazgos", async (
                Guid id,
                RegistrarHallazgoRequest request,
                RegistrarHallazgoHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

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
                        NovedadDescartadaNoImportableException => "INV-ND1",  // slice 1p — FU-40
                        NovedadPreopYaImportadaException       => "I-H13",    // slice 1p — Gap 6b
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
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

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

        // ── Slice 1f — AsignarRepuesto ──────────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{inspeccionId:guid}/hallazgos/{hallazgoId:guid}/repuestos", async (
                Guid inspeccionId,
                Guid hallazgoId,
                AsignarRepuestoRequest request,
                AsignarRepuestoHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                var cmd = new AsignarRepuesto(
                    InspeccionId: inspeccionId,
                    HallazgoId: hallazgoId,
                    RepuestoId: request.RepuestoId,
                    SkuId: request.SkuId,
                    Cantidad: request.Cantidad,
                    Justificacion: request.Justificacion,
                    TecnicoId: tecnicoId);

                try
                {
                    var resultado = await handler.ManejarAsync(cmd, ct);

                    return Results.Created(
                        uri: $"/api/v1/inspecciones/{inspeccionId}/hallazgos/{hallazgoId}/repuestos/{resultado.RepuestoId}",
                        value: new
                        {
                            repuestoId = resultado.RepuestoId,
                            skuId = resultado.SkuId,
                            skuCodigo = resultado.SkuCodigo,
                            cantidad = resultado.Cantidad,
                            unidad = resultado.Unidad,
                            justificacion = resultado.Justificacion,
                            asignadoEn = resultado.AsignadoEn
                        });
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
                        InspeccionNoEnEjecucionException           => "I-H7",
                        HallazgoEliminadoException                 => "PRE-B2-ELIMINADO",
                        HallazgoNoRequiereIntervencionException    => "I-H12",
                        CantidadInvalidaException                  => "PRE-E",
                        SkuDuplicadoEnHallazgoException            => "PRE-G",
                        RepuestoNoEncontradoEnCatalogoException    => "PRE-H1",
                        _                                          => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("AsignarRepuesto");

        // ── Slice 1g — FirmarInspeccion ─────────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{id:guid}/firmar", async (
                Guid id,
                FirmarInspeccionRequest request,
                FirmarInspeccionHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);
                var claims = new ClaimsTecnico(
                    TecnicoIniciador: tecnicoId,
                    ProyectosAsignados: new HashSet<int>(),
                    TieneCapabilityEjecutarInspeccion: true);

                var cmd = new FirmarInspeccion(
                    InspeccionId: id,
                    Diagnostico: request.Diagnostico,
                    Dictamen: request.Dictamen,
                    JustificacionDictamen: request.JustificacionDictamen,
                    FirmaUri: request.FirmaUri,
                    UbicacionFirma: request.UbicacionFirma,
                    TecnicoId: tecnicoId);

                try
                {
                    var resultado = await handler.ManejarAsync(cmd, claims, ct);

                    var response = new FirmarInspeccionResponse(
                        InspeccionId: resultado.InspeccionId,
                        Estado: resultado.Estado,
                        FirmadaEn: resultado.FirmadaEn,
                        Dictamen: resultado.Dictamen);

                    return Results.Ok(response);
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-F", mensaje = ex.Message });
                }
                catch (CapabilityRequeridaException ex)
                {
                    return Forbidden403("PRE-1", ex.Message);
                }
                catch (TecnicoNoContribuyenteException ex)
                {
                    return Forbidden403("PRE-F3", ex.Message);
                }
                catch (InspeccionNoEnEjecucionException ex)
                {
                    return Results.Conflict(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        SinHallazgosException                     => "V-F1",
                        DiagnosticoRequeridoException             => "PRE-4",
                        DictamenIncoherenteException              => "V-F8",
                        HallazgoIntervencionIncompletoException   => "V-F3",
                        FirmaRequeridaException                   => "V-F5",
                        GpsRequeridoException                     => "V-F6",
                        _                                         => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("FirmarInspeccion");

        // ── Slice 1e — EliminarHallazgo ─────────────────────────────────────
        app.MapDelete("/api/v1/inspecciones/{inspeccionId:guid}/hallazgos/{hallazgoId:guid}", async (
                Guid inspeccionId,
                Guid hallazgoId,
                [FromBody] EliminarHallazgoRequest request,
                EliminarHallazgoHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                var cmd = new EliminarHallazgo(
                    InspeccionId: inspeccionId,
                    HallazgoId: hallazgoId,
                    Motivo: request.Motivo,
                    TecnicoId: tecnicoId);

                try
                {
                    await handler.ManejarAsync(cmd, ct);
                    return Results.NoContent();
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
                        InspeccionNoEnEjecucionException       => "I-H7",
                        HallazgoEliminadoException             => "PRE-B2-ELIMINADO",
                        MotivoEliminacionVacioException        => "PRE-C",
                        HallazgoTieneHijosActivosException     => "I-H9",
                        _                                      => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("EliminarHallazgo");

        // ── Slice 1i — RegistrarMedicion ───────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{inspeccionId:guid}/items/{itemId:int}/medicion", async (
                Guid inspeccionId,
                int itemId,
                RegistrarMedicionRequest request,
                RegistrarMedicionHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                var cmd = new RegistrarMedicion(
                    InspeccionId: inspeccionId,
                    HallazgoId: request.HallazgoId,
                    ItemId: itemId,
                    ValorMedido: request.ValorMedido,
                    Observacion: request.Observacion,
                    EmitidoPor: tecnicoId,
                    Capabilities: session.Capabilities);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        itemId = resultado.ItemId,
                        valorMedido = resultado.ValorMedido,
                        fueraDeRango = resultado.FueraDeRango,
                        hallazgoGeneradoId = resultado.HallazgoGeneradoId,
                        registradaEn = resultado.RegistradaEn
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (ItemYaMedidoException ex)
                {
                    return Results.Conflict(new { codigoError = "I-M6", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoEsMonitoreoException           => "I-M1",
                        InspeccionNoEnEjecucionException           => "I-M2",
                        ItemNoEncontradoEnSnapshotException        => "I-M3",
                        ItemOmitidoNoPuedeMedirseException         => "I-M4",
                        ItemNoEsNumericoException                  => "I-M5",
                        ParteEquipoIdAusenteEnSnapshotException    => "I-H1",
                        _                                          => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("RegistrarMedicion");

        // ── Slice 1i' — RegistrarEvaluacionCualitativa ─────────────────────
        app.MapPost("/api/v1/inspecciones/{inspeccionId:guid}/items/{itemId:int}/evaluacion", async (
                Guid inspeccionId,
                int itemId,
                RegistrarEvaluacionCualitativaRequest request,
                RegistrarEvaluacionCualitativaHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                if (!Enum.TryParse<CalificacionCualitativa>(request.Calificacion, ignoreCase: true, out var calificacion))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "CALIFICACION-INVALIDA",
                        mensaje = $"Calificación '{request.Calificacion}' no válida. Valores aceptados: Bueno, Regular, Malo."
                    });
                }

                var cmd = new RegistrarEvaluacionCualitativa(
                    InspeccionId: inspeccionId,
                    HallazgoId: request.HallazgoId,
                    ItemId: itemId,
                    Calificacion: calificacion,
                    Observacion: request.Observacion,
                    EmitidoPor: tecnicoId,
                    Capabilities: session.Capabilities);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        itemId = resultado.ItemId,
                        calificacion = resultado.Calificacion,
                        hallazgoGeneradoId = resultado.HallazgoGeneradoId,
                        registradaEn = resultado.RegistradaEn
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (ItemYaEvaluadoException ex)
                {
                    return Results.Conflict(new { codigoError = "I-M7", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoEsMonitoreoException           => "I-M1",
                        InspeccionNoEnEjecucionException           => "I-M2",
                        ItemNoEncontradoEnSnapshotException        => "I-M3",
                        ItemOmitidoNoPuedeMedirseException         => "I-M4",
                        ItemNoEsCualitativoException               => "I-M5b",
                        ParteEquipoIdAusenteEnSnapshotException    => "PARTE-AUSENTE",
                        _                                          => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("RegistrarEvaluacionCualitativa");

        // ── Slice 1j — OmitirItemMonitoreo ─────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{inspeccionId:guid}/items/{itemId:int}/omitir", async (
                Guid inspeccionId,
                int itemId,
                OmitirItemMonitoreoRequest request,
                OmitirItemMonitoreoHandler handler,
                ISessionService session,
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

                // PRE-CAP-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                var cmd = new OmitirItemMonitoreo(
                    InspeccionId: inspeccionId,
                    ItemId: itemId,
                    Motivo: request.Motivo,
                    EmitidoPor: tecnicoId,
                    Capabilities: session.Capabilities);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        itemId = resultado.ItemId,
                        motivo = resultado.Motivo,
                        omitidoEn = resultado.OmitidoEn
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (ItemNoEncontradoEnSnapshotException ex)
                {
                    return Results.NotFound(new { codigoError = "I-M3", mensaje = ex.Message });
                }
                catch (MotivoOmisionInvalidoException ex)
                {
                    // PRE-3 vacío o PRE-4 longitud — 400 Bad Request. El código se distingue
                    // por el contenido del mensaje (que el aggregate construye en cada caso).
                    var codigoError = ex.Message.Contains("obligatorio", StringComparison.OrdinalIgnoreCase)
                        ? "MOTIVO-VACIO"
                        : "MOTIVO-LONGITUD";
                    return Results.BadRequest(new { codigoError, mensaje = ex.Message });
                }
                catch (ItemYaOmitidoException ex)
                {
                    return Results.Conflict(new { codigoError = "I-M9", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoEsMonitoreoException => "I-M1",
                        InspeccionNoEnEjecucionException => "I-M2",
                        ItemYaProcesadoException         => "I-M8",
                        _                                => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("OmitirItemMonitoreo");

        // ── Slice 1k — GenerarOT ───────────────────────────────────────────
        // TODO stub: lanza NotImplementedException hasta que el green implemente el handler.
        // El endpoint ya verifica PRE-1 (capability) y el header X-Client-Command-Id.
        app.MapPost("/api/v1/inspecciones/{id:guid}/generar-ot", async (
                Guid id,
                GenerarOTRequest request,
                GenerarOTHandler handler,
                ISessionService session,
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

                // PRE-1 — capability "generar-ot" requerida (spec mt-1 §9.5).
                if (!session.Capabilities.Contains("generar-ot"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityGenerarOT);
                }

                var aprobadorId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);
                var capabilities = session.Capabilities;

                if (!Enum.TryParse<ResponsableCosto>(request.Responsable, ignoreCase: true, out var responsable))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "RESPONSABLE-INVALIDO",
                        mensaje = $"Responsable '{request.Responsable}' no válido. Valores aceptados: Proyecto, DepartamentoEquipos."
                    });
                }

                if (!Enum.TryParse<PrioridadOT>(request.Prioridad, ignoreCase: true, out var prioridad))
                {
                    return Results.BadRequest(new
                    {
                        codigoError = "PRIORIDAD-INVALIDA",
                        mensaje = $"Prioridad '{request.Prioridad}' no válida. Valores aceptados: Baja, Normal, Alta, Urgente."
                    });
                }

                var cmd = new GenerarOT(
                    InspeccionId: id,
                    SolicitadaPor: aprobadorId,
                    Responsable: responsable,
                    Observaciones: request.Observaciones,
                    ComentarioJefe: request.ComentarioJefe,
                    Capabilities: capabilities,
                    Prioridad: prioridad);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Accepted(
                        uri: $"/api/v1/inspecciones/{id}",
                        value: new
                        {
                            inspeccionId = resultado.InspeccionId,
                            solicitadaEn = resultado.SolicitadaEn,
                            solicitadaPor = resultado.SolicitadaPor,
                            responsable = resultado.Responsable,
                            prioridad = resultado.Prioridad
                        });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (OTYaSolicitadaException ex)
                {
                    return Results.Conflict(new { codigoError = "I-F4-OT-DUPLICADA", mensaje = ex.Message });
                }
                catch (OTRechazadaException ex)
                {
                    return Results.Conflict(new { codigoError = "I-F4-OT-RECHAZADA", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoFirmadaException            => "I-F4-ESTADO",
                        SinHallazgosConIntervencionException    => "I-F4-SIN-INTERVENCION",
                        DictamenNoPermiteOTException            => "I-F4-DICTAMEN",
                        _                                       => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("GenerarOT");

        // ── Slice 1l — RechazarGenerarOT ───────────────────────────────────
        // El endpoint verifica PRE-1 (capability "generar-ot" — misma que GenerarOT) y el header
        // X-Client-Command-Id. El cierre es síncrono (D-4: 200 OK, no 202) — el handler emite
        // GeneracionOTRechazada_v1 + InspeccionCerradaSinOT_v1 atómicamente y la inspección queda
        // en estado terminal CerradaSinOT al retornar. No hay saga asíncrona ni POST al ERP.
        app.MapPost("/api/v1/inspecciones/{id:guid}/rechazar-generar-ot", async (
                Guid id,
                RechazarGenerarOTRequest request,
                RechazarGenerarOTHandler handler,
                ISessionService session,
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

                // PRE-1 — capability "generar-ot" requerida (spec mt-1 §9.5 — misma capability que GenerarOT).
                if (!session.Capabilities.Contains("generar-ot"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityGenerarOT);
                }

                var aprobadorId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);
                var capabilities = session.Capabilities;

                var cmd = new RechazarGenerarOT(
                    InspeccionId: id,
                    Motivo: request.Motivo ?? string.Empty,
                    RechazadoPor: aprobadorId,
                    Capabilities: capabilities);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    // D-4: 200 OK — el cierre es síncrono, la inspección ya está CerradaSinOT
                    // al momento de retornar. No es asíncrono como GenerarOT (202).
                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        estado = resultado.Estado,
                        rechazadaEn = resultado.RechazadaEn,
                        rechazadoPor = resultado.RechazadoPor,
                        motivo = resultado.Motivo
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (MotivoRechazoInvalidoException ex)
                {
                    return Results.UnprocessableEntity(new { codigoError = "I-F6-MOTIVO", mensaje = ex.Message });
                }
                catch (OTYaSolicitadaException ex)
                {
                    return Results.Conflict(new { codigoError = "I-F6-OT-YA-SOLICITADA", mensaje = ex.Message });
                }
                catch (OTYaRechazadaException ex)
                {
                    return Results.Conflict(new { codigoError = "I-F6-OT-YA-RECHAZADA", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoFirmadaException            => "I-F6-ESTADO",
                        SinHallazgosConIntervencionException    => "I-F6-SIN-INTERVENCION",
                        _                                       => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("RechazarGenerarOT");

        // ── Slice 1m — CancelarInspeccion ──────────────────────────────────
        // PRE-1 (capability "ejecutar-inspeccion") verificada vía header X-Sin-Capability-Ejecutar.
        // La cancelación es síncrona (D-4: 200 OK).
        app.MapPost("/api/v1/inspecciones/{id:guid}/cancelar", async (
                Guid id,
                CancelarInspeccionRequest request,
                CancelarInspeccionHandler handler,
                ISessionService session,
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

                // PRE-1 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                var cmd = new CancelarInspeccion(
                    InspeccionId: id,
                    Motivo: request.Motivo ?? string.Empty,
                    CanceladaPor: tecnicoId);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        estado = resultado.Estado,
                        canceladaEn = resultado.CanceladaEn,
                        canceladaPor = resultado.CanceladaPor,
                        motivo = resultado.Motivo
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-2", mensaje = ex.Message });
                }
                catch (TecnicoNoContribuyenteException ex)
                {
                    return Forbidden403("I6-NO-CONTRIBUYENTE", ex.Message);
                }
                catch (InspeccionNoEnEjecucionException ex)
                {
                    return Results.Conflict(new { codigoError = "I6-ESTADO", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        MotivoCancelacionInvalidoException => "I6-MOTIVO",
                        _                                  => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("CancelarInspeccion");

        // ── Slice 1n — DescartarNovedadPreop ───────────────────────────────
        // PRE-4 (capability "ejecutar-inspeccion") verificada vía header X-Sin-Capability-Ejecutar.
        // Motivo autogenerado por el handler con plantilla D-4 (spec §13 D-4).
        app.MapPost("/api/v1/inspecciones/{inspeccionId:guid}/novedades-preop/{novedadId:int}/descartar",
            async (
                Guid inspeccionId,
                int novedadId,
                DescartarNovedadPreopRequest request,
                DescartarNovedadPreopHandler handler,
                ISessionService session,
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

                // PRE-4 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-4", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                var cmd = new DescartarNovedadPreop(
                    InspeccionId: inspeccionId,
                    NovedadId: novedadId,
                    DescartadaPor: tecnicoId);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        novedadId = resultado.NovedadId,
                        motivoDescarte = resultado.MotivoDescarte,
                        descartadaPor = resultado.DescartadaPor,
                        descartadaEn = resultado.DescartadaEn
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-1", mensaje = ex.Message });
                }
                catch (InspeccionNoEnEjecucionException ex)
                {
                    return Results.UnprocessableEntity(new { codigoError = "PRE-2-ESTADO", mensaje = ex.Message });
                }
                catch (NovedadYaDescartadaException ex)
                {
                    return Results.UnprocessableEntity(new { codigoError = "PRE-5-DESCARTADA", mensaje = ex.Message });
                }
                catch (NovedadYaConvertidaEnHallazgoException ex)
                {
                    return Results.UnprocessableEntity(new { codigoError = "PRE-6-HALLAZGO", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    return Results.UnprocessableEntity(new { codigoError = "DOMINIO", mensaje = ex.Message });
                }
            })
           .WithName("DescartarNovedadPreop");

        // ── Slice 1o — ActualizarRepuesto ───────────────────────────────────
        // PRE-0 (capability "ejecutar-inspeccion") verificada vía header X-Sin-Capability-Ejecutar.
        // Verbo PATCH: los campos patcheables son opcionales independientemente (spec §9 D-6).
        // Stub — el handler lanza NotImplementedException hasta que green lo implemente.
        app.MapMethods("/api/v1/inspecciones/{inspeccionId:guid}/hallazgos/{hallazgoId:guid}/repuestos/{repuestoId:guid}",
            ["PATCH"], async (
                Guid inspeccionId,
                Guid hallazgoId,
                Guid repuestoId,
                ActualizarRepuestoRequest request,
                ActualizarRepuestoHandler handler,
                ISessionService session,
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

                // PRE-0 — capability "ejecutar-inspeccion" requerida (spec mt-1).
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-0", MensajeCapabilityEjecutarInspeccion);
                }

                var tecnicoId = session.IdUsuario.ToString(CultureInfo.InvariantCulture);

                // P-2: normalizar ObservacionNueva vacía a null en handler (spec §12 P-2 opción A).
                var observacionNormalizada = string.IsNullOrWhiteSpace(request.ObservacionNueva)
                    ? null
                    : request.ObservacionNueva;

                var cmd = new ActualizarRepuesto(
                    InspeccionId: inspeccionId,
                    HallazgoId: hallazgoId,
                    RepuestoId: repuestoId,
                    CantidadNueva: request.CantidadNueva,
                    ObservacionNueva: observacionNormalizada,
                    ActualizadoPor: tecnicoId);

                try
                {
                    var resultado = await handler.Handle(cmd, ct);

                    return Results.Ok(new
                    {
                        inspeccionId = resultado.InspeccionId,
                        hallazgoId = resultado.HallazgoId,
                        repuestoId = resultado.RepuestoId,
                        cantidad = resultado.Cantidad,
                        justificacion = resultado.Justificacion,
                        actualizadoEn = resultado.ActualizadoEn
                    });
                }
                catch (InspeccionNoEncontradaException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-1", mensaje = ex.Message });
                }
                catch (HallazgoNoEncontradoException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-3", mensaje = ex.Message });
                }
                catch (RepuestoNoEncontradoException ex)
                {
                    return Results.NotFound(new { codigoError = "PRE-5", mensaje = ex.Message });
                }
                catch (ComandoSinCambiosException ex)
                {
                    return Results.BadRequest(new { codigoError = "PRE-8", mensaje = ex.Message });
                }
                catch (InspeccionDomainException ex)
                {
                    var codigoError = ex switch
                    {
                        InspeccionNoEnEjecucionException => "I-H7",
                        HallazgoEliminadoException       => "PRE-4-ELIMINADO",
                        CantidadInvalidaException        => "PRE-7",
                        _                                => "DOMINIO"
                    };
                    return Results.UnprocessableEntity(new { codigoError, mensaje = ex.Message });
                }
            })
           .WithName("ActualizarRepuesto");

        // ── Listar inspecciones de un equipo (historia completa) ────────────
        // GET /api/v1/inspecciones?equipoId={int} — todas las inspecciones del equipo
        // (cualquier estado), más recientes primero. Fuente: proyección histórica
        // InspeccionResumenView (una fila por inspección). equipoId es obligatorio
        // (sin él el listado sería no acotado) — su ausencia da 400 automático.
        app.MapGet("/api/v1/inspecciones", async (
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

                var filas = await query.Query<InspeccionResumenView>()
                    .Where(v => v.EquipoId == equipoId)
                    .OrderByDescending(v => v.IniciadaEn)
                    .ToListAsync(ct);

                var inspecciones = filas.Select(v => new
                {
                    inspeccionId = v.InspeccionId,
                    tipo = v.Tipo,
                    estado = v.Estado,
                    rutinaId = v.RutinaId,
                    rutinaCodigo = v.RutinaCodigo,
                    tecnicoIniciador = v.TecnicoIniciador,
                    proyectoId = v.ProyectoId,
                    fechaReportada = v.FechaReportada,
                    iniciadaEn = v.IniciadaEn,
                    firmadaEn = v.FirmadaEn,
                    canceladaEn = v.CanceladaEn,
                    cerradaEn = v.CerradaEn,
                    dictamen = v.Dictamen,
                    otSolicitada = v.OTSolicitada,
                    otRechazada = v.OTRechazada,
                    motivoCancelacion = v.MotivoCancelacion,
                    hallazgos = new
                    {
                        total = v.TotalHallazgos,
                        requierenIntervencion = v.HallazgosRequierenIntervencion,
                        requierenSeguimiento = v.HallazgosRequierenSeguimiento,
                        sinIntervencion = v.HallazgosSinIntervencion
                    }
                }).ToList();

                return Results.Ok(new
                {
                    equipoId,
                    total = inspecciones.Count,
                    inspecciones
                });
            })
           .WithName("ListarInspeccionesPorEquipo");

        // ── Recuperar inspección por id ─────────────────────────────────────
        // GET /api/v1/inspecciones/{id} — reconstruye el aggregate desde su stream
        // de eventos y devuelve el estado completo. Resuelve "no queda guardada":
        // la PWA relee el estado persistido para reentrar al flujo.
        //
        // Sin header X-Client-Command-Id (ADR-008 aplica solo a comandos de escritura).
        // El tenant lo resuelve el IQuerySession ambient del reader vía IdEmpresa
        // (ITenantedDocumentSessionFactory) — claim ausente ⇒ 401 por el handler global.
        app.MapGet("/api/v1/inspecciones/{id:guid}", async (
                Guid id,
                IInspeccionReader reader,
                ISessionService session,
                CancellationToken ct) =>
            {
                // PRE-1 — capability "ejecutar-inspeccion" requerida.
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                var inspeccion = await reader.LeerAsync(id, ct);
                if (inspeccion is null)
                {
                    return Results.NotFound(new
                    {
                        codigoError = "PRE-2",
                        mensaje = $"No existe una inspección con id {id}."
                    });
                }

                return Results.Ok(RecuperarInspeccionResponse.Desde(inspeccion));
            })
           .WithName("RecuperarInspeccion");

        // ── Listar novedades preop importables de una inspección (slice 1q) ──
        // GET /api/v1/inspecciones/{id}/novedades-preop?desde=&hasta=&texto=&estado=
        // Pasa-piso a Maquinaria_V4 (GET /api/preoperacional-fallas) acotado al equipo
        // de la inspección + derivación server-side del Estado (Disponible|Importada|
        // Descartada) desde el aggregate. Capability ejecutar-inspeccion (técnico en campo).
        // Cierra FU-5.
        //
        // parteEquipoId / responsable NO están disponibles upstream sin cambio en
        // Maquinaria_V4 (ver 09-solicitud-cambio-maquinaria-preop-fallas.md) — el front
        // resuelve parteEquipoId con el selector de parte manual del Paso 1.
        app.MapGet("/api/v1/inspecciones/{id:guid}/novedades-preop", async (
                Guid id,
                string? desde,
                string? hasta,
                string? texto,
                EstadoNovedadImportacion? estado,
                IInspeccionReader reader,
                IMaquinariaErpClient erp,
                ISessionService session,
                CancellationToken ct) =>
            {
                // PRE-1 — capability "ejecutar-inspeccion" requerida.
                if (!session.Capabilities.Contains("ejecutar-inspeccion"))
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                // PRE-2 — la inspección debe existir: provee el EquipoId del filtro al
                // ERP y el estado derivado de cada novedad.
                var inspeccion = await reader.LeerAsync(id, ct);
                if (inspeccion is null)
                {
                    return Results.NotFound(new
                    {
                        codigoError = "PRE-2",
                        mensaje = $"No existe una inspección con id {id}."
                    });
                }

                var desdeDate = DateOnly.TryParseExact(desde ?? "", "yyyy-MM-dd", out var d) ? d : DateOnly.MinValue;
                var hastaDate = DateOnly.TryParseExact(hasta ?? "", "yyyy-MM-dd", out var h) ? h : DateOnly.MinValue;

                try
                {
                    var resp = await erp.ListarPreoperacionalFallasAsync(
                        desdeDate, hastaDate, inspeccion.EquipoId, string.IsNullOrEmpty(texto) ? "-1" : texto, ct);

                    // Estado derivado por el aggregate (fuente de verdad — slice 1q).
                    // Filtro opcional ?estado= aplicado tras derivar.
                    var novedades = resp.Fallas
                        .Select(f => NovedadPreopImportableDto.Desde(f, inspeccion.EstadoNovedadPreop(f.Id)))
                        .Where(n => estado is null || n.Estado == estado)
                        .ToList();

                    return Results.Ok(new ListarNovedadesPreopResponse(
                        InspeccionId: id,
                        EquipoId: inspeccion.EquipoId,
                        Novedades: novedades,
                        Total: novedades.Count));
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(
                        new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: StatusCodes.Status502BadGateway);
                }
                catch (HttpRequestException ex)
                {
                    return Results.Json(
                        new { codigoError = "ERP_INACCESIBLE", mensaje = ex.Message },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
           .WithName("ListarNovedadesPreopImportables");

        // ── Inspección activa de un equipo ──────────────────────────────────
        // GET /api/v1/inspecciones/activa?equipoId={int} — lee la proyección
        // InspeccionAbiertaPorEquipoView (misma lectura que la idempotencia I-I1).
        // Sin choque de ruteo con {id:guid} porque "activa" no es un Guid.
        app.MapGet("/api/v1/inspecciones/activa", async (
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

                var activa = await query.LoadAsync<InspeccionAbiertaPorEquipoView>(equipoId, ct);
                if (activa is null)
                {
                    return Results.NotFound(new
                    {
                        codigoError = "SIN_INSPECCION_ACTIVA",
                        mensaje = $"El equipo {equipoId} no tiene una inspección activa."
                    });
                }

                return Results.Ok(new
                {
                    equipoId = activa.EquipoId,
                    inspeccionId = activa.InspeccionId,
                    tecnicoIniciador = activa.TecnicoIniciador,
                    iniciadaEn = activa.IniciadaEn,
                    proyectoId = activa.ProyectoId,
                    tipo = activa.Tipo
                });
            })
           .WithName("RecuperarInspeccionActivaPorEquipo");

        return app;
    }

    /// <summary>
    /// Construye una respuesta HTTP 403 Forbidden con body <c>{ codigoError, mensaje }</c>.
    /// Reemplaza <c>Results.Forbid()</c> que requiere <c>IAuthenticationService</c> (no registrado — ADR-002).
    /// Fix FU-38.
    /// </summary>
    private static IResult Forbidden403(string codigoError, string mensaje)
        => Results.Json(new { codigoError, mensaje }, statusCode: 403);

    private const string MensajeCapabilityGenerarOT = "Capability 'generar-ot' requerida.";
    private const string MensajeCapabilityEjecutarInspeccion = "Capability 'ejecutar-inspeccion' requerida.";
}
