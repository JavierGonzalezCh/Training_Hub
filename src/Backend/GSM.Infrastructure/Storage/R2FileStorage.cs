using Amazon.Runtime;
using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using GSM.Application.Common.Abstractions;
using GSM.Application.Common.Logs;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Storage;

/// <summary>
/// Almacenamiento sobre Cloudflare R2 vía la API S3. Los objetos se renombran con un GUID. Un fallo se propaga
/// igual que antes; además queda en <c>logs.external_failures</c> (LG3).
/// </summary>
public sealed class R2FileStorage : IFileStorage, IDisposable
{
    private readonly R2Options _options;
    private readonly IAmazonS3 _client;
    private readonly ILogStore _logStore;

    public R2FileStorage(IOptions<R2Options> options, ILogStore logStore)
    {
        _options = options.Value;
        _logStore = logStore;
        _client = new AmazonS3Client(
            _options.AccessKeyId,
            _options.SecretAccessKey,
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            });
    }

    public async Task<string> SaveAsync(
        FileToStore file, string folder, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(file.FileName);
        var key = $"{folder.Trim('/')}/{Guid.CreateVersion7():n}{extension}";

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = _options.Bucket,
                    Key = key,
                    InputStream = file.Content,
                    ContentType = file.ContentType,
                    DisablePayloadSigning = true,
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            RecordFailure(ExternalOperations.Upload, ex, started);
            throw;
        }

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";
    }

    public async Task DeleteAsync(string publicUrl, CancellationToken cancellationToken = default)
    {
        var prefix = _options.PublicBaseUrl.TrimEnd('/') + "/";
        if (!publicUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = publicUrl[prefix.Length..];

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = _options.Bucket, Key = key }, cancellationToken);
        }
        catch (Exception ex)
        {
            RecordFailure(ExternalOperations.Delete, ex, started);
            throw;
        }
    }

    private void RecordFailure(string operation, Exception ex, long started)
    {
        // Una cancelación del llamador no es una falla del proveedor.
        if (ex is OperationCanceledException)
        {
            return;
        }

        var status = ex is AmazonServiceException service ? (int)service.StatusCode : (int?)null;
        var body = ex is AmazonServiceException s3
            ? $"{s3.ErrorCode}: {s3.Message}"
            : $"{ex.GetType().Name}: {ex.Message}";
        _logStore.RecordExternalFailure(new ExternalFailureLogEntry(
            ExternalProviders.R2, operation, status, body,
            (int)Math.Min(Stopwatch.GetElapsedTime(started).TotalMilliseconds, int.MaxValue)));
    }

    public void Dispose() => _client.Dispose();
}
