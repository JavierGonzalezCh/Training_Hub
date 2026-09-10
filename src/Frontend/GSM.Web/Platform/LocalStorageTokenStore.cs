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

    public async ValueTask<AuthTokens?> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, Key);
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
        => await js.InvokeVoidAsync("localStorage.setItem", cancellationToken, Key, JsonSerializer.Serialize(tokens));

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
        => await js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, Key);
}
