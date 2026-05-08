using Inspecciones.Domain.Inspecciones;

namespace Inspecciones.Api.Inspecciones;

/// <summary>
/// DTO de entrada para <c>POST /api/v1/inspecciones/{id}/generar-ot</c>.
/// Spec slice 1k §2 y §9.
/// </summary>
public sealed record GenerarOTRequest(
    string  Responsable,      // "Proyecto" | "DepartamentoEquipos"
    string  Prioridad,        // "Baja" | "Normal" | "Alta" | "Urgente"
    string? Observaciones,
    string? ComentarioJefe);
