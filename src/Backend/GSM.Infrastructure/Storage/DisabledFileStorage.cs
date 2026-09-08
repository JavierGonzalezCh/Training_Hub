using GSM.Application.Common.Abstractions;

namespace GSM.Infrastructure.Storage;

/// <summary>Se registra cuando <c>Storage:R2</c> no está configurado. Cualquier subida falla con un mensaje claro.</summary>
public sealed class DisabledFileStorage : IFileStorage
{
    private const string Message =
        "El almacenamiento de archivos (Cloudflare R2) no está configurado. Rellena 'Storage:R2'.";

    public Task<string> SaveAsync(FileToStore file, string folder, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(Message);

    public Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
