namespace GSM.UI.Shared;

/// <summary>
/// Imprimir (o compartir como PDF) lo que se ve en pantalla, por ejemplo el reporte de la caja. La web usa el
/// diálogo de impresión del navegador; la app nativa registra la suya (el visor de impresión de Android, que
/// permite «Guardar como PDF» y compartirlo). Sin implementación registrada, el consumidor cae en
/// <c>window.print</c> por JS interop (mismo patrón que <see cref="IClipboard"/>).
/// </summary>
public interface IPrintService
{
    /// <summary>Devuelve <c>false</c> si el host no pudo abrir la impresión.</summary>
    Task<bool> PrintAsync(string title, CancellationToken cancellationToken = default);
}
