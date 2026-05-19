using Inspecciones.Domain.Catalogos;
using Inspecciones.Infrastructure.Erp;
using Inspecciones.Infrastructure.Erp.Dtos;
using Marten;

namespace Inspecciones.Api.Catalogos;

/// <summary>
/// Endpoints administrativos para sincronizar / sembrar el catálogo local
/// (<c>EquipoLocal</c>, <c>RutinaTecnicaLocal</c>, <c>ParteEquipoLocal</c>)
/// desde Maquinaria_V4 o desde un payload manual.
/// </summary>
public static class CatalogosEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosEndpoints(this IEndpointRouteBuilder app)
    {
        // ── POST /api/v1/admin/sincronizar-equipo/{equipoId} ────────────────
        // Pulla equipo + partes desde Maquinaria_V4 y persiste en Marten.
        app.MapPost("/api/v1/admin/sincronizar-equipo/{equipoId:int}", async (
                int equipoId,
                SincronizarEquipoDesdeErpHandler handler,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await handler.EjecutarAsync(equipoId, ct);
                    return Results.Ok(result);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    return Results.BadRequest(new { codigoError = "EQUIPO_ID_INVALIDO", mensaje = ex.Message });
                }
                catch (EquipoNoVisibleEnErpException ex)
                {
                    return Results.NotFound(new { codigoError = "EQUIPO_NO_VISIBLE", mensaje = ex.Message });
                }
                catch (MaquinariaErpException ex)
                {
                    var status = ex.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized => StatusCodes.Status502BadGateway,
                        System.Net.HttpStatusCode.Forbidden => StatusCodes.Status502BadGateway,
                        _ => StatusCodes.Status502BadGateway,
                    };
                    return Results.Json(
                        new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: status);
                }
                catch (HttpRequestException ex)
                {
                    return Results.Json(
                        new
                        {
                            codigoError = "ERP_INACCESIBLE",
                            mensaje = $"No se pudo conectar a Maquinaria_V4: {ex.Message}",
                        },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("SincronizarEquipoDesdeErp")
            .WithTags("Admin")
            .WithSummary("Sincroniza un equipo y sus partes desde Maquinaria_V4 al catálogo local de Inspecciones.");

        // ── POST /api/v1/admin/seed-equipo ──────────────────────────────────
        // Carga manual del catálogo (útil cuando Maquinaria_V4 no está accesible).
        app.MapPost("/api/v1/admin/seed-equipo", async (
                SeedManualCatalogoCommand cmd,
                SeedManualCatalogoHandler handler,
                CancellationToken ct) =>
            {
                if (cmd.EquipoId <= 0)
                {
                    return Results.BadRequest(new { codigoError = "EQUIPO_ID_INVALIDO", mensaje = "EquipoId debe ser entero positivo" });
                }
                if (string.IsNullOrWhiteSpace(cmd.EquipoCodigo))
                {
                    return Results.BadRequest(new { codigoError = "EQUIPO_CODIGO_VACIO", mensaje = "EquipoCodigo es obligatorio" });
                }
                if (cmd.ProyectoId <= 0)
                {
                    return Results.BadRequest(new { codigoError = "PROYECTO_ID_INVALIDO", mensaje = "ProyectoId debe ser entero positivo" });
                }

                await handler.EjecutarAsync(cmd, ct);
                return Results.Ok(new
                {
                    equipoId = cmd.EquipoId,
                    equipoCodigo = cmd.EquipoCodigo,
                    rutinaTecnicaId = cmd.RutinaTecnicaId,
                    cantidadPartes = cmd.Partes.Count,
                });
            })
            .WithName("SeedManualEquipo")
            .WithTags("Admin")
            .WithSummary("Carga manual de un equipo en el catálogo local (bypass de Maquinaria_V4).");

        // ── GET /api/v1/admin/equipos-erp ──────────────────────────────────
        // Pasa-piso a Maquinaria_V4 para listar equipos visibles — útil para
        // descubrir qué equipoId pasar a sincronizar-equipo.
        app.MapGet("/api/v1/admin/equipos-erp", async (
                string? filtro,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await erp.ListarEquiposAsync(filtro, ifNoneMatch: null, ct);
                    return Results.Ok(result.Body);
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
            .WithName("ListarEquiposErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: lista equipos visibles para el token configurado.");

        // ── GET /api/v1/admin/causas-falla-erp ─────────────────────────────
        app.MapGet("/api/v1/admin/causas-falla-erp", async (
                string? texto,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await erp.ListarCausasFallaAsync(string.IsNullOrEmpty(texto) ? "-1" : texto, ifNoneMatch: null, ct);
                    return Results.Ok(result.Body);
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("ListarCausasFallaErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: lista causas de falla (texto='-1' trae todo).");

        // ── GET /api/v1/admin/tipos-falla-erp ──────────────────────────────
        app.MapGet("/api/v1/admin/tipos-falla-erp", async (
                string? texto,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await erp.ListarTiposFallaAsync(string.IsNullOrEmpty(texto) ? "-1" : texto, ifNoneMatch: null, ct);
                    return Results.Ok(result.Body);
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("ListarTiposFallaErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: lista tipos de falla (texto='-1' trae todo).");

        // ── GET /api/v1/admin/productos-erp ────────────────────────────────
        app.MapGet("/api/v1/admin/productos-erp", async (
                string? texto,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await erp.ListarProductosAsync(string.IsNullOrEmpty(texto) ? "-1" : texto, ifNoneMatch: null, ct);
                    return Results.Ok(result.Body);
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("ListarProductosErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: lista productos (texto='-1' trae todo).");

        // ── GET /api/v1/admin/preop-fallas-erp ─────────────────────────────
        app.MapGet("/api/v1/admin/preop-fallas-erp", async (
                string? desde,
                string? hasta,
                int? equipoId,
                string? texto,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                DateOnly desdeDate = DateOnly.TryParseExact(desde ?? "", "yyyy-MM-dd", out var d) ? d : DateOnly.MinValue;
                DateOnly hastaDate = DateOnly.TryParseExact(hasta ?? "", "yyyy-MM-dd", out var h) ? h : DateOnly.MinValue;
                try
                {
                    var result = await erp.ListarPreoperacionalFallasAsync(
                        desdeDate, hastaDate, equipoId ?? -1, string.IsNullOrEmpty(texto) ? "-1" : texto, ct);
                    return Results.Ok(result);
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("ListarPreopFallasErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: lista novedades preop visibles (filtros por fecha/equipo/texto).");

        // ── GET /api/v1/admin/rutinas-monitoreo-erp/{equipoId} ─────────────
        app.MapGet("/api/v1/admin/rutinas-monitoreo-erp/{equipoId:int}", async (
                int equipoId,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await erp.ListarRutinasMonitoreoPorEquipoAsync(equipoId, ifNoneMatch: null, ct);
                    return Results.Ok(result.Body);
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("ListarRutinasMonitoreoErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: lista rutinas de monitoreo asignadas a un equipo.");

        // ── GET /api/v1/admin/catalogo-equipo/{equipoId} ────────────────────
        // Muestra el estado actual del catálogo local para verificar el sync.
        app.MapGet("/api/v1/admin/catalogo-equipo/{equipoId:int}", async (
                int equipoId,
                IDocumentSession session,
                CancellationToken ct) =>
            {
                var equipo = await session.LoadAsync<EquipoLocal>(equipoId, ct);
                if (equipo is null)
                {
                    return Results.NotFound(new { codigoError = "NO_SINCRONIZADO", mensaje = $"El equipo {equipoId} no está en el catálogo local. Llamá a /admin/sincronizar-equipo/{equipoId} o /admin/seed-equipo primero." });
                }

                RutinaTecnicaLocal? rutina = null;
                if (equipo.RutinaTecnicaId is int rid)
                {
                    rutina = await session.LoadAsync<RutinaTecnicaLocal>(rid, ct);
                }

                return Results.Ok(new { equipo, rutina });
            })
            .WithName("VerCatalogoEquipo")
            .WithTags("Admin")
            .WithSummary("Devuelve el equipo + rutina técnica del catálogo local de Inspecciones.");

        // ── POST /api/v1/admin/cerrar-preop-erp ────────────────────────────
        // Pasa-piso al endpoint de Maquinaria_V4 que cierra novedades preop.
        // Útil para QA y para reintentar manualmente cierres fallidos antes
        // de que la saga outbox los re-encole.
        app.MapPost("/api/v1/admin/cerrar-preop-erp", async (
                CerrarPreoperacionalFallasRequestDto body,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                if (body is null || body.PodIds.Count == 0)
                {
                    return Results.BadRequest(new { codigoError = "POD_IDS_VACIO", mensaje = "podIds debe contener al menos un id" });
                }
                if (string.IsNullOrWhiteSpace(body.Observaciones))
                {
                    return Results.BadRequest(new { codigoError = "OBSERVACIONES_REQUERIDA", mensaje = "observaciones no puede ser vacío" });
                }
                try
                {
                    var result = await erp.CerrarPreoperacionalFallasAsync(body, ct);
                    return Results.Ok(result);
                }
                catch (MaquinariaErpException ex)
                {
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode, codigoErp = ex.CodigoErp },
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("CerrarPreopErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: cierra novedades preop bulk (PodIds + observaciones).");

        // ── POST /api/v1/catalogos/sync ─────────────────────────────────────
        // Endpoint principal del ADR-004 sync on-app-open. La PWA lo dispara al
        // abrir la app y el admin lo usa para "Refrescar ahora". Sin body; el
        // handler decide internamente qué catálogos verificar.
        // Siempre devuelve 200 OK aunque algún catálogo falle (D5 partial-failure).
        app.MapPost("/api/v1/catalogos/sync", async (
                SincronizarCatalogosHandler handler,
                CancellationToken ct) =>
            {
                var resultado = await handler.EjecutarAsync(ct);
                return Results.Ok(new
                {
                    catalogos = resultado.Catalogos.Select(c => new
                    {
                        nombre = c.Nombre,
                        status = c.Status,
                        actualizadosEn = c.ActualizadosEn,
                        error = c.Error,
                    }),
                    sincronizadoEn = resultado.SincronizadoEn,
                });
            })
            .WithName("SincronizarCatalogos")
            .WithTags("Catalogos")
            .WithSummary("Sincroniza los catálogos globales desde Maquinaria_V4 usando ETag/If-None-Match (ADR-004).");

        // ── PUT /api/v1/admin/dictamen-equipo-erp/{equipoCodigo} ───────────
        // Pasa-piso a M-W-1. Pre-saga: este endpoint NO debería invocarse en
        // producción; la fuente canónica es SincronizarDictamenVigenteSaga.
        // Sirve para QA manual y troubleshooting.
        app.MapPut("/api/v1/admin/dictamen-equipo-erp/{equipoCodigo:int}", async (
                int equipoCodigo,
                ActualizarDictamenEquipoRequestDto body,
                IMaquinariaErpClient erp,
                CancellationToken ct) =>
            {
                if (body.Estado is < 0 or > 2)
                {
                    return Results.BadRequest(new { codigoError = "ESTADO_INVALIDO", mensaje = "estado debe ser 0, 1 o 2" });
                }
                try
                {
                    var result = await erp.ActualizarDictamenEquipoAsync(equipoCodigo, body, ct);
                    return Results.Ok(result);
                }
                catch (MaquinariaErpException ex)
                {
                    var status = ex.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status502BadGateway;
                    return Results.Json(new { codigoError = "ERP_ERROR", mensaje = ex.Message, statusErp = (int?)ex.StatusCode, codigoErp = ex.CodigoErp },
                        statusCode: status);
                }
            })
            .WithName("ActualizarDictamenEquipoErp")
            .WithTags("Admin")
            .WithSummary("Pasa-piso a Maquinaria_V4: actualiza dictamen vigente del equipo (M-W-1).");

        return app;
    }
}
