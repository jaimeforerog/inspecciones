using Inspecciones.Application.Inspecciones;
using Inspecciones.Domain.Inspecciones;
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

                // Claims mock — followup #14 (claims reales desde JWT cuando ADR-002 se resuelva).
                var claims = new ClaimsTecnico(
                    TecnicoIniciador: "rmartinez",
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
                    Capabilities: Array.Empty<string>());

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

        // ── Slice 1f — AsignarRepuesto ──────────────────────────────────────
        app.MapPost("/api/v1/inspecciones/{inspeccionId:guid}/hallazgos/{hallazgoId:guid}/repuestos", async (
                Guid inspeccionId,
                Guid hallazgoId,
                AsignarRepuestoRequest request,
                AsignarRepuestoHandler handler,
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
                        SkuIncompatibleConParteException           => "PRE-H2",
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

                var cmd = new RegistrarMedicion(
                    InspeccionId: inspeccionId,
                    HallazgoId: request.HallazgoId,
                    ItemId: itemId,
                    ValorMedido: request.ValorMedido,
                    Observacion: request.Observacion,
                    EmitidoPor: tecnicoId,
                    Capabilities: Array.Empty<string>());

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
                    Capabilities: Array.Empty<string>());

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

                var cmd = new OmitirItemMonitoreo(
                    InspeccionId: inspeccionId,
                    ItemId: itemId,
                    Motivo: request.Motivo,
                    EmitidoPor: tecnicoId,
                    Capabilities: Array.Empty<string>());

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

                // PRE-1 — capability "generar-ot" requerida (spec §4, §6.3).
                // Claims mock — ADR-002 tentativo. El host PWA inyectará claims reales vía JWT.
                // El header X-Sin-Capability-Generar-OT se usa en tests para simular claims sin capability.
                var sinCapabilityHeader = ctx.Request.Headers.ContainsKey("X-Sin-Capability-Generar-OT");
                if (sinCapabilityHeader)
                {
                    return Forbidden403("PRE-1", MensajeCapabilityGenerarOT);
                }

                // Claims mock — capability "generar-ot" hardcodeada hasta ADR-002.
                const string aprobadorId = "jefe.campo.01";
                var capabilities = (IReadOnlyCollection<string>)["generar-ot"];

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

                // PRE-1 — capability "generar-ot" requerida (spec §4, §6.3 — misma capability que GenerarOT).
                // Claims mock — ADR-002 tentativo. El host PWA inyectará claims reales vía JWT.
                // El header X-Sin-Capability-Generar-OT se usa en tests para simular claims sin capability.
                var sinCapabilityHeader = ctx.Request.Headers.ContainsKey("X-Sin-Capability-Generar-OT");
                if (sinCapabilityHeader)
                {
                    return Forbidden403("PRE-1", MensajeCapabilityGenerarOT);
                }

                // Claims mock — capability "generar-ot" hardcodeada hasta ADR-002.
                const string aprobadorId = "jefe.campo.01";
                var capabilities = (IReadOnlyCollection<string>)["generar-ot"];

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

                // PRE-1 — capability "ejecutar-inspeccion" requerida (spec §4, §6.4).
                // Claims mock — ADR-002 tentativo. El host PWA inyectará claims reales vía JWT.
                // El header X-Sin-Capability-Ejecutar se usa en tests para simular claims sin capability.
                var sinCapabilityHeader = ctx.Request.Headers.ContainsKey("X-Sin-Capability-Ejecutar");
                if (sinCapabilityHeader)
                {
                    return Forbidden403("PRE-1", MensajeCapabilityEjecutarInspeccion);
                }

                // Claims mock — capability "ejecutar-inspeccion" hardcodeada hasta ADR-002.
                // El TecnicoId se extrae del header X-Tecnico-Id en tests (simula JWT claims).
                const string tecnicoIdDefault = "carlos.ruiz";
                var tecnicoId = ctx.Request.Headers.TryGetValue("X-Tecnico-Id", out var tecnicoIdValues)
                    && !string.IsNullOrWhiteSpace(tecnicoIdValues.ToString())
                    ? tecnicoIdValues.ToString()
                    : tecnicoIdDefault;

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

                // PRE-4 — capability "ejecutar-inspeccion" requerida (spec §4).
                // El header X-Sin-Capability-Ejecutar se usa en tests para simular claims sin capability.
                var sinCapabilityHeader = ctx.Request.Headers.ContainsKey("X-Sin-Capability-Ejecutar");
                if (sinCapabilityHeader)
                {
                    return Forbidden403("PRE-4", MensajeCapabilityEjecutarInspeccion);
                }

                // Claims mock — ADR-002 tentativo.
                // El TecnicoId se extrae del header X-Tecnico-Id en tests (simula JWT claims).
                const string tecnicoIdDefault = "ana.gomez";
                var tecnicoId = ctx.Request.Headers.TryGetValue("X-Tecnico-Id", out var tecnicoIdValues)
                    && !string.IsNullOrWhiteSpace(tecnicoIdValues.ToString())
                    ? tecnicoIdValues.ToString()
                    : request.DescartadaPor ?? tecnicoIdDefault;

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
