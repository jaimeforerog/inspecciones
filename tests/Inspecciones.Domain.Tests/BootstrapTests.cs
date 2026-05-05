namespace Inspecciones.Domain.Tests;

/// <summary>
/// Test trivial de bootstrap del dominio. Confirma que el proyecto compila,
/// xUnit corre y FluentAssertions está cargado correctamente. Será reemplazado
/// por tests reales del dominio en el slice 1 (`IniciarInspeccion`).
/// </summary>
public class BootstrapTests
{
    [Fact]
    public void El_proyecto_de_dominio_compila_y_los_tests_corren()
    {
        // Arrange + Act
        var resultado = 1 + 1;

        // Assert
        resultado.Should().Be(2, "porque la matemática básica todavía funciona");
    }
}
