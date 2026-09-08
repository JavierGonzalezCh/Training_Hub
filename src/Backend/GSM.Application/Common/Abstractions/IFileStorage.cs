namespace GSM.Application.Common.Abstractions;

/// <summary>Almacenamiento de archivos públicos (logos, fotos, íconos). Implementado sobre Cloudflare R2.</summary>
public interface IFileStorage
{
    /// <summary>Guarda el archivo bajo <paramref name="folder"/> con un nombre nuevo (GUID) y devuelve su URL pública.</summary>
    Task<string> SaveAsync(FileToStore file, string folder, CancellationToken cancellationToken = default);

    /// <summary>Borra el archivo por su URL pública. Idempotente.</summary>
    Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default);
}

public sealed record FileToStore(Stream Content, string FileName, string ContentType, long Length);
