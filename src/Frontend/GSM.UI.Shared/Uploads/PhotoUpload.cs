using GSM.Client.Generated;
using Microsoft.AspNetCore.Components.Forms;

namespace GSM.UI.Shared.Uploads;

/// <summary>
/// Prepara una foto (de persona o de equipo) para subirla. El navegador la convierte a JPEG de hasta
/// <see cref="MaxSide"/> px, así que llegan bien las fotos HEIC del iPhone y las de 8–12 MB de la cámara. Además, se
/// lee entera en memoria antes de enviarla: en Safari de iOS, leerla por partes durante el envío puede cortarse a la
/// mitad, y la API recibe un multipart incompleto (400 sin detalle).
/// No sirve para logos ni íconos: el JPEG pierde la transparencia.
/// </summary>
public static class PhotoUpload
{
    public const int MaxSide = 1600;

    // Tope de lo que se lee del navegador; la API rechaza más de 5 MB (ImageUploadRules).
    private const long MaxBytes = 5 * 1024 * 1024;

    public static async Task<FileParameter> PrepareAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        IBrowserFile source = file;
        var contentType = file.ContentType;
        var name = file.Name;
        try
        {
            source = await file.RequestImageFileAsync("image/jpeg", MaxSide, MaxSide);
            contentType = "image/jpeg";
            name = Path.ChangeExtension(string.IsNullOrWhiteSpace(file.Name) ? "foto" : file.Name, ".jpg");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // El navegador no supo decodificarla (p. ej., HEIC fuera de Safari): se manda tal cual y la API decide.
        }

        var buffer = new MemoryStream();
        await using (var stream = source.OpenReadStream(MaxBytes, cancellationToken))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        // Sin tipo conocido no se inventa uno: la API responde con su mensaje de formatos admitidos.
        return new FileParameter(buffer, name, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
    }
}
