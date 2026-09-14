using System.Text.Json;
using GSM.Client.Http;
using Microsoft.JSInterop;

namespace GSM.Web.Platform;

/// <summary>
/// <see cref="ITokenStore"/> del host web: guarda el par de tokens en <c>localStorage</c>.
/// En MAUI el equivalente será <c>SecureStorage</c>.
/// </summary>
public sealed class LocalStorageTokenStore(IJSRuntime js) : ITokenStore
{
    private const string Key = "gsm.tokens";
    private const string BackupKey = "gsm.tokens.backup";

    public ValueTask<AuthTokens?> GetAsync(CancellationToken cancellationToken = default) => ReadAsync(Key, cancellationToken);

    public ValueTask SetAsync(AuthTokens tokens, CancellationToken cancellationToken = default)
        => WriteAsync(Key, tokens, cancellationToken);

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
        => await js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, Key);

    public ValueTask BackupAsync(AuthTokens tokens, CancellationToken cancellationToken = default)
        => WriteAsync(BackupKey, tokens, cancellationToken);

    public ValueTask<AuthTokens?> GetBackupAsync(CancellationToken cancellationToken = default) => ReadAsync(BackupKey, cancellationToken);

    public async ValueTask ClearBackupAsync(CancellationToken cancellationToken = default)
        => await js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, BackupKey);

    private async ValueTask<AuthTokens?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var raw = await js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, key);
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

    private async ValueTask WriteAsync(string key, AuthTokens tokens, CancellationToken cancellationToken)
        => await js.InvokeVoidAsync("localStorage.setItem", cancellationToken, key, JsonSerializer.Serialize(tokens));
}
