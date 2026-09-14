using System.Text.Json;
using GSM.Client.Http;

namespace GSM.Mobile.Platform;

/// <summary><see cref="ITokenStore"/> del host móvil: guarda el par de tokens en SecureStorage.</summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Key = "gsm.tokens";

    public async ValueTask<AuthTokens?> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await SecureStorage.Default.GetAsync(Key);
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

    public async ValueTask SetAsync(AuthTokens tokens, CancellationToken cancellationToken = default)
        => await SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(tokens));

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(Key);
        return ValueTask.CompletedTask;
    }
}
