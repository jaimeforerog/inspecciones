using Inspecciones.Domain.Catalogos;
using Marten;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Caso de uso administrativo: dado un <c>equipoId</c>, pulla los datos del
/// equipo + sus partes desde Maquinaria_V4 y persiste en el catálogo local
/// (<see cref="EquipoLocal"/>, <see cref="ParteEquipoLocal"/>,
/// <see cref="RutinaTecnicaLocal"/>) en Marten.
///
/// <para>
/// Gap conocido: Maquinaria_V4 NO expone hoy la rutina técnica per-equipo
/// (M-17 en el contrato). Como workaround el sync sintetiza una
/// <see cref="RutinaTecnicaLocal"/> a partir del <c>RutinaMantenimientoId</c>
/// del equipo y de su primera parte. Esto es suficiente para satisfacer las
/// pre-condiciones del slice 1b (<c>IniciarInspeccion</c>) y poder ejercitar
/// el flujo de prueba; al exponer Maquinaria_V4 un endpoint dedicado se
/// reemplaza esta lógica por fetch directo del catálogo.
/// </para>
/// </summary>
public sealed class SincronizarEquipoDesdeErpHandler
{
    private readonly IMaquinariaErpClient _erp;
    private readonly IDocumentSession _session;
    private readonly TimeProvider _time;

    public SincronizarEquipoDesdeErpHandler(
        IMaquinariaErpClient erp,
        IDocumentSession session,
        TimeProvider time)
    {
        _erp = erp;
        _session = session;
        _time = time;
    }

    public async Task<SincronizarEquipoResult> EjecutarAsync(int equipoId, CancellationToken ct = default)
    {
        if (equipoId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(equipoId), equipoId, "equipoId debe ser entero positivo");
        }

        // 1. Traer todos los equipos visibles y filtrar por el solicitado.
        //    Maquinaria_V4 no expone GET /equipos/{id}; la lista respeta la visibilidad por obras del usuario.
        var equiposResult = await _erp.ListarEquiposAsync(filtro: null, ifNoneMatch: null, ct);
        var equipos = equiposResult.Body ?? throw new MaquinariaErpException(
            "Maquinaria_V4 respondió 304 al sync inicial sin body — no es esperado en sync forzado por equipoId.");
        var equipoErp = equipos.Equipos.FirstOrDefault(e => e.EquipoId == equipoId)
            ?? throw new EquipoNoVisibleEnErpException(
                $"El equipo {equipoId} no aparece en /api/equipos. Causas posibles: " +
                "no existe, está inactivo, o el usuario del token no tiene visibilidad sobre la obra.");

        // 2. Traer las partes.
        var partesResult = await _erp.ListarPartesEquipoAsync(equipoId, ifNoneMatch: null, ct);
        var partes = partesResult.Body ?? throw new MaquinariaErpException(
            "Maquinaria_V4 respondió 304 al sync de partes sin body — no es esperado en sync forzado.");

        // 3. Mapear a catálogo local.
        var partesLocales = partes.Partes
            .Select(p => new ParteEquipoLocal(
                ParteEquipoId: p.ParteId,
                ParteCodigo: p.ParteId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ParteNombre: p.ParteDescripcion))
            .ToList();

        var proyectoId = equipoErp.ObraEquipo ?? equipoErp.ObraProyecto ?? 0;

        // Workaround M-17: sintetizar rutina técnica si Maquinaria_V4 reportó RutinaMantenimientoId
        // y al menos una parte. Si no, dejamos RutinaTecnicaId=null — IniciarInspeccion fallará con
        // EquipoSinRutinaTecnicaException, indicando claramente que falta el dato en el ERP.
        RutinaTecnicaLocal? rutina = null;
        int? rutinaIdParaEquipo = null;
        if (equipoErp.RutinaMantenimientoId is int rutinaId && partesLocales.Count > 0)
        {
            var primeraParte = partesLocales[0];
            rutina = new RutinaTecnicaLocal(
                RutinaId: rutinaId,
                Codigo: rutinaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Nombre: equipoErp.RutinaMantenimientoDescripcion ?? $"Rutina {rutinaId}",
                Tipo: TipoRutina.Tecnica,
                GrupoMantenimiento: equipoErp.GrupoMantenimientoDescripcion ?? "SIN-GRUPO",
                ParteId: primeraParte.ParteEquipoId,
                ParteCodigo: primeraParte.ParteCodigo,
                SincronizadoEn: _time.GetUtcNow());
            rutinaIdParaEquipo = rutinaId;
        }

        var equipoLocal = new EquipoLocal(
            EquipoId: equipoErp.EquipoId,
            EquipoCodigo: equipoErp.Placa,
            ProyectoId: proyectoId,
            RutinaTecnicaId: rutinaIdParaEquipo,
            GrupoMantenimientoId: equipoErp.GrupoMantenimientoId,
            Partes: partesLocales);

        // 4. Persistir (upsert) atómicamente.
        _session.Store(equipoLocal);
        if (rutina is not null)
        {
            _session.Store(rutina);
        }

        await _session.SaveChangesAsync(ct);

        return new SincronizarEquipoResult(
            EquipoId: equipoLocal.EquipoId,
            EquipoCodigo: equipoLocal.EquipoCodigo,
            ProyectoId: equipoLocal.ProyectoId,
            RutinaTecnicaId: equipoLocal.RutinaTecnicaId,
            CantidadPartes: partesLocales.Count,
            RutinaSintetizada: rutina is not null,
            SincronizadoEn: _time.GetUtcNow());
    }
}

/// <summary>Resultado del sync: resumen de lo que quedó en el catálogo local.</summary>
public sealed record SincronizarEquipoResult(
    int EquipoId,
    string EquipoCodigo,
    int ProyectoId,
    int? RutinaTecnicaId,
    int CantidadPartes,
    bool RutinaSintetizada,
    DateTimeOffset SincronizadoEn);

/// <summary>El equipo no es visible para el usuario del token en Maquinaria_V4.</summary>
public sealed class EquipoNoVisibleEnErpException : Exception
{
    public EquipoNoVisibleEnErpException(string message) : base(message) { }
}
