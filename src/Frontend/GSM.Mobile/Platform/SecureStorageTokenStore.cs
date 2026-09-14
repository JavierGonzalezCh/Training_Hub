using System.Text.Json;
using GSM.Client.Http;

namespace GSM.Mobile.Platform;

/// <summary><see cref="ITokenStore"/> del host móvil: guarda el par de tokens en SecureStorage.</summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Key = "gsm.tokens";
    private const string BackupKey = "gsm.tokens.backup";

    public ValueTask<AuthTokens?> GetAsync(CancellationToken cancellationToken = default) => ReadAsync(Key);

    public ValueTask SetAsync(AuthTokens tokens, CancellationToken cancellationToken = default) => WriteAsync(Key, tokens);

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(Key);
        return ValueTask.CompletedTask;
    }

    public ValueTask BackupAsync(AuthTokens tokens, CancellationToken cancellationToken = default) => WriteAsync(BackupKey, tokens);

    public ValueTask<AuthTokens?> GetBackupAsync(CancellationToken cancellationToken = default) => ReadAsync(BackupKey);

    public ValueTask ClearBackupAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(BackupKey);
        return ValueTask.CompletedTask;
    }

    private static async ValueTask<AuthTokens?> ReadAsync(string key)
    {
        var raw = await SecureStorage.Default.GetAsync(key);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AuthTokens>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async ValueTask WriteAsync(string key, AuthTokens tokens)
        => await SecureStorage.Default.SetAsync(key, JsonSerializer.Serialize(tokens));
}
