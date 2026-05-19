using Inspecciones.Infrastructure.Auth;

namespace Inspecciones.Infrastructure.Tests.Auth;

/// <summary>
/// Tests de la excepción <c>TenantRequeridoEnEnvelopeException</c> introducida
/// en mt-2 §4 MT2-PRE-2: cuando un listener Wolverine recibe un envelope sin
/// <c>TenantId</c>, debe lanzar esta excepción para que la política ADR-006
/// mueva el mensaje a dead-letter inmediato (escenario §6.6 del spec).
/// </summary>
public sealed class TenantRequeridoEnEnvelopeExceptionTests
{
    [Fact]
    public void Construye_la_excepcion_con_nombre_del_listener_y_messageId_visibles_en_el_mensaje()
    {
        var messageId = Guid.Parse("00000000-0000-0000-0000-000000000077");

        var ex = new TenantRequeridoEnEnvelopeException(
            nombreListener: "SincronizarDictamenVigenteListener",
            messageId: messageId);

        ex.NombreListener.Should().Be("SincronizarDictamenVigenteListener");
        ex.MessageId.Should().Be(messageId);
        ex.CodigoError.Should().Be("TENANT-ENVELOPE-AUSENTE");
        ex.Message.Should().Contain("SincronizarDictamenVigenteListener");
        ex.Message.Should().Contain(messageId.ToString());
    }

    [Fact]
    public void Es_subclase_de_InvalidOperationException_para_que_Wolverine_la_trate_como_error_permanente()
    {
        var ex = new TenantRequeridoEnEnvelopeException(
            nombreListener: "X",
            messageId: Guid.NewGuid());

        // ADR-006 §16 política: errores permanentes (4xx, ArgumentException) van a
        // dead-letter inmediato. La excepción no es retryable — debe distinguirse
        // claramente de las MaquinariaErpException 5xx. InvalidOperationException
        // es la categoría correcta (no es bug del adapter, es bug del wiring).
        ex.Should().BeAssignableTo<InvalidOperationException>();
    }
}
