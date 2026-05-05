namespace Inspecciones.Domain.Comun;

/// <summary>
/// Lectura puntual de un medidor del equipo (horómetro, odómetro u otro).
/// Tipo y unidad vienen denormalizados desde el equipo (ver §12.7 modelo).
/// </summary>
public sealed record LecturaMedidor(
    string Tipo,
    decimal Valor,
    DateTimeOffset CapturadoEn);
