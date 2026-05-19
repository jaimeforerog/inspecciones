using Inspecciones.Domain.Catalogos;
using Marten;

namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Caso de uso administrativo paralelo al sync HTTP: permite cargar un equipo
/// + rutina + partes en el catálogo local con un JSON manual. Útil cuando
/// Maquinaria_V4 no está accesible (token JWT inválido, servicio caído, etc.)
/// pero queremos ejercitar Inspecciones de punta a punta.
/// </summary>
public sealed class SeedManualCatalogoHandler
{
    private readonly IDocumentSession _session;
    private readonly TimeProvider _time;

    public SeedManualCatalogoHandler(IDocumentSession session, TimeProvider time)
    {
        _session = session;
        _time = time;
    }

    public async Task EjecutarAsync(SeedManualCatalogoCommand cmd, CancellationToken ct = default)
    {
        var partes = cmd.Partes
            .Select(p => new ParteEquipoLocal(
                ParteEquipoId: p.ParteId,
                ParteCodigo: string.IsNullOrWhiteSpace(p.ParteCodigo)
                    ? p.ParteId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : p.ParteCodigo,
                ParteNombre: p.ParteNombre))
            .ToList();

        var equipo = new EquipoLocal(
            EquipoId: cmd.EquipoId,
            EquipoCodigo: cmd.EquipoCodigo,
            ProyectoId: cmd.ProyectoId,
            RutinaTecnicaId: cmd.RutinaTecnicaId,
            GrupoMantenimientoId: cmd.GrupoMantenimientoId,
            Partes: partes);

        _session.Store(equipo);

        if (cmd.RutinaTecnicaId is int rutinaId)
        {
            var primeraParte = partes.FirstOrDefault();
            var rutina = new RutinaTecnicaLocal(
                RutinaId: rutinaId,
                Codigo: string.IsNullOrWhiteSpace(cmd.RutinaTecnicaCodigo)
                    ? rutinaId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : cmd.RutinaTecnicaCodigo,
                Nombre: string.IsNullOrWhiteSpace(cmd.RutinaTecnicaNombre)
                    ? $"Rutina {rutinaId}"
                    : cmd.RutinaTecnicaNombre,
                Tipo: TipoRutina.Tecnica,
                GrupoMantenimiento: string.IsNullOrWhiteSpace(cmd.RutinaGrupoMantenimiento)
                    ? "SIN-GRUPO"
                    : cmd.RutinaGrupoMantenimiento,
                ParteId: primeraParte?.ParteEquipoId ?? 0,
                ParteCodigo: primeraParte?.ParteCodigo ?? "0",
                SincronizadoEn: _time.GetUtcNow());

            _session.Store(rutina);
        }

        await _session.SaveChangesAsync(ct);
    }
}

public sealed record SeedManualCatalogoCommand(
    int EquipoId,
    string EquipoCodigo,
    int ProyectoId,
    int? RutinaTecnicaId,
    int? GrupoMantenimientoId,
    string? RutinaTecnicaCodigo,
    string? RutinaTecnicaNombre,
    string? RutinaGrupoMantenimiento,
    IReadOnlyList<SeedManualParte> Partes);

public sealed record SeedManualParte(int ParteId, string? ParteCodigo, string ParteNombre);
