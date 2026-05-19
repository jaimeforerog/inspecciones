namespace Inspecciones.Infrastructure.Erp;

/// <summary>
/// Documento Marten que registra el estado de la última sincronización de cada
/// catálogo. Id natural = nombre del catálogo ("causas-falla", "tipos-falla",
/// "productos"). Escrito por <see cref="SincronizarCatalogosHandler"/> al terminar
/// cada ciclo de sync (D7 del spec erp-4 — ETag incluye las comillas tal como
/// lo devuelve HTTP).
/// </summary>
public sealed class CatalogoSyncState
{
    /// <summary>Nombre del catálogo: "causas-falla" | "tipos-falla" | "productos".</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Valor crudo del header ETag incluyendo comillas (p. ej. <c>"\"v42\""</c>).</summary>
    public string? EtagActual { get; set; }

    /// <summary>Timestamp de la última sync con resultado "actualizado" o "no-change".</summary>
    public DateTimeOffset? UltimaSyncExitosa { get; set; }

    /// <summary>Timestamp del último intento de sync (exitoso o fallido).</summary>
    public DateTimeOffset UltimaSyncIntento { get; set; }

    /// <summary>Estado resultante: "no-change" | "actualizado" | "error" | "vaciado-sospechoso".</summary>
    public string UltimoEstado { get; set; } = string.Empty;

    /// <summary>Mensaje de error. Solo poblado cuando <c>UltimoEstado = "error"</c>.</summary>
    public string? UltimoErrorMensaje { get; set; }
}
