namespace Inspecciones.Domain.Catalogos;

/// <summary>
/// Discriminador de tipos de rutina del catálogo. En MVP solo aplica
/// <see cref="Tecnica"/>. Escalable a futuros tipos sin migrar el catálogo
/// (§12.11.1 modelo).
/// </summary>
public enum TipoRutina
{
    Tecnica
}
