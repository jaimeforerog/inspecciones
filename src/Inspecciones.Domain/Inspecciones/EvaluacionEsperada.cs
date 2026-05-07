namespace Inspecciones.Domain.Inspecciones;

/// <summary>
/// Tipo abstracto que representa la evaluación esperada de un item de monitoreo.
/// Subtipos: <see cref="MedicionEsperada"/> y <see cref="EvaluacionCualitativaEsperada"/>.
/// Slice 1h — IniciarInspeccionMonitoreo. Stub mínimo fase red.
/// </summary>
public abstract record EvaluacionEsperada;

/// <summary>
/// Evaluación esperada de tipo numérico (p. ej. "voltaje entre 12.3V y 12.5V").
/// Slice 1h — stub mínimo fase red.
/// </summary>
public sealed record MedicionEsperada(
    string Magnitud,
    string Unidad,
    decimal ValorMinimo,
    decimal ValorMaximo) : EvaluacionEsperada;

/// <summary>
/// Evaluación esperada de tipo cualitativo (p. ej. "estado visual correcto").
/// Slice 1h — stub mínimo fase red.
/// </summary>
public sealed record EvaluacionCualitativaEsperada : EvaluacionEsperada;
