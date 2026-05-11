namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Evento de dominio emitido cuando el técnico descarta explícitamente una novedad
/// preoperacional durante la ejecución de una inspección técnica. Representa la
/// decisión de gobernanza del técnico (contradicción al operador). Audit-only —
/// no crea hallazgo. Ver §15.4 del modelo (evento #9 del catálogo canónico).
/// </summary>
/// <param name="InspeccionId">Stream de la inspección técnica.</param>
/// <param name="NovedadId">PK del ERP (int, convención §15.4) — novedad descartada.</param>
/// <param name="MotivoDescarte">
///   Motivo autogenerado por el handler con plantilla D-4:
///   "Cerrado por {usuario} el {fecha:yyyy-MM-dd HH:mm} UTC desde Inspecciones".
/// </param>
/// <param name="DescartadaPor">userId opaco del técnico (string, ADR-002).</param>
/// <param name="DescartadaEn">Timestamp UTC — vía TimeProvider.GetUtcNow().</param>
public sealed record NovedadPreopDescartada_v1(
    Guid           InspeccionId,
    int            NovedadId,
    string         MotivoDescarte,
    string         DescartadaPor,
    DateTimeOffset DescartadaEn);
