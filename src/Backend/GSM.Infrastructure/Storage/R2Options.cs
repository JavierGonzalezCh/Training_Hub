namespace GSM.Infrastructure.Storage;

/// <summary>Config de Cloudflare R2 (S3-compatible). Sección <c>Storage:R2</c>.</summary>
public sealed class R2Options
{
    public const string SectionName = "Storage:R2";

    public string AccountId { get; init; } = string.Empty;

    public string AccessKeyId { get; init; } = string.Empty;

    public string SecretAccessKey { get; init; } = string.Empty;

    public string Bucket { get; init; } = string.Empty;

    /// <summary>Base URL pública para leer los objetos (dominio r2.dev o dominio propio), sin barra final.</summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId) &&
        !string.IsNullOrWhiteSpace(AccessKeyId) &&
        !string.IsNullOrWhiteSpace(SecretAccessKey) &&
        !string.IsNullOrWhiteSpace(Bucket) &&
        !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string ServiceUrl => $"https://{AccountId}.r2.cloudflarestorage.com";
}
