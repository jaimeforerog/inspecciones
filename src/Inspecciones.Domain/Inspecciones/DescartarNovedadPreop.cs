namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando del slice 1n — DescartarNovedadPreop.
/// El técnico descarta una novedad preoperacional individual que considera inválida
/// (falsa alarma, ya resuelta por otro medio). Tap directo sin modal — motivo
/// autogenerado por el handler. Solo aplica a inspecciones de tipo Tecnica.
/// Spec §2 del slice 1n.
/// </summary>
public sealed record DescartarNovedadPreop(
    Guid   InspeccionId,
    int    NovedadId,       // PK del ERP (int, convención §15.4)
    string DescartadaPor    // userId opaco del técnico, extraído del JWT por la capa API
);
