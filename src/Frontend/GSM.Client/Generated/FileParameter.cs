namespace GSM.Client.Generated;

/// <summary>
/// Parámetro de subida de fichero para el cliente generado (NSwag no lo emite en esta
/// configuración, pero los endpoints multipart lo referencian). Forma canónica de NSwag.
/// </summary>
public class FileParameter
{
    public FileParameter(Stream data)
        : this(data, null, null)
    {
    }

    public FileParameter(Stream data, string? fileName)
        : this(data, fileName, null)
    {
    }

    public FileParameter(Stream data, string? fileName, string? contentType)
    {
        Data = data;
        FileName = fileName;
        ContentType = contentType;
    }

    public Stream Data { get; }

    public string? FileName { get; }

    public string? ContentType { get; }
}
