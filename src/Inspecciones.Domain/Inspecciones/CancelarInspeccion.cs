namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Comando del slice 1m — CancelarInspeccion.
/// Abandona una inspección técnica o de monitoreo que está en ejecución.
/// El técnico debe ser contribuyente del stream (PRE-3 handler).
/// Spec §2.
/// </summary>
public sealed record CancelarInspeccion(
    Guid   InspeccionId,
    string Motivo,      // texto libre obligatorio; mínimo 10 chars (trimmed); máximo 500 chars (D-1)
    string CanceladaPor // userId opaco del técnico, extraído del JWT por la capa API
);
