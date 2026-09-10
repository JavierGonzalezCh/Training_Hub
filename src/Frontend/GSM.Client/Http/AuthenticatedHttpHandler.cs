using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GSM.Client.Http;

/// <summary>
/// Pone el <c>Authorization: Bearer</c> en cada petición y, cuando llega un 401, intenta un
/// refresh y reintenta una sola vez. El refresh token rota (cada refresh revoca el anterior),
/// así que solo puede haber <b>un refresh en vuelo</b>: si varias llamadas fallan a la vez,
/// todas esperan al mismo refresh.
/// </summary>
public sealed class AuthenticatedHttpHandler(ITokenStore tokenStore, ITokenRefresher refresher)
    : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        var accessTokenUsed = tokens?.AccessToken;
        if (accessTokenUsed is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessTokenUsed);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || tokens is null)
        {
            return response;
        }

        var refreshed = await RefreshOnceAsync(accessTokenUsed, cancellationToken);
        if (refreshed is null)
        {
            return response;
        }

        response.Dispose();

        var retry = await CloneAsync(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    /// <summary>
    /// Refresca los tokens una vez, en exclusión mutua. Si otra llamada ya refrescó mientras
    /// esperábamos el lock, devuelve esos tokens nuevos sin pedir otro refresh.
    /// </summary>
    private async Task<AuthTokens?> RefreshOnceAsync(string? staleAccessToken, CancellationToken cancellationToken)
    {
        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            var current = await tokenStore.GetAsync(cancellationToken);
            if (current is not null && current.AccessToken != staleAccessToken)
            {
                return current;
            }

            if (current is null)
            {
                return null;
            }

            var refreshed = await refresher.RefreshAsync(current.RefreshToken, cancellationToken);
            if (refreshed is null)
            {
                await tokenStore.ClearAsync(cancellationToken);
                return null;
            }

            await tokenStore.SetAsync(refreshed, cancellationToken);
            return refreshed;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        if (request.Content is not null)
        {
            var buffer = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(buffer);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        return clone;
    }
}

/// <summary>Llama a <c>/api/auth/refresh</c> sin pasar por el handler autenticado (evita el bucle).</summary>
public interface ITokenRefresher
{
    Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>Implementación por defecto: un <see cref="HttpClient"/> desnudo contra la API.</summary>
public sealed class HttpTokenRefresher(HttpClient httpClient) : ITokenRefresher
{
    public async Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "api/auth/refresh", new RefreshBody(refreshToken), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken);
            return payload is null
                ? null
                : new AuthTokens(payload.AccessToken, payload.RefreshToken, payload.ExpiresAtUtc);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private sealed record RefreshBody([property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed record TokenPayload(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc);
}
