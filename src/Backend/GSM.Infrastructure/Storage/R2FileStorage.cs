using Amazon.S3;
using Amazon.S3.Model;
using GSM.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Storage;

/// <summary>Almacenamiento sobre Cloudflare R2 vía la API S3. Los objetos se renombran con un GUID.</summary>
public sealed class R2FileStorage : IFileStorage, IDisposable
{
    private readonly R2Options _options;
    private readonly IAmazonS3 _client;

    public R2FileStorage(IOptions<R2Options> options)
    {
        _options = options.Value;
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

        await _client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _options.Bucket, Key = key }, cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
