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

            // La sesión de un kiosco (A3) se renueva por su propia ruta: el token del dispositivo no rota.
            var isKiosk = JwtPayload.TryRead(current.AccessToken)?.Role == GsmRoles.Kiosk;
            var refreshed = isKiosk
                ? await refresher.RefreshKioskAsync(current.RefreshToken, cancellationToken)
                : await refresher.RefreshAsync(current.RefreshToken, cancellationToken);
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

    /// <summary>
    /// Renueva la sesión de un kiosco con <c>/api/auth/kiosk/refresh</c> (el token del dispositivo no rota ni vence;
    /// 401 si el Owner lo revocó). Por defecto cae en <see cref="RefreshAsync"/>.
    /// </summary>
    Task<AuthTokens?> RefreshKioskAsync(string refreshToken, CancellationToken cancellationToken = default)
        => RefreshAsync(refreshToken, cancellationToken);
}

/// <summary>Implementación por defecto: un <see cref="HttpClient"/> desnudo contra la API.</summary>
public sealed class HttpTokenRefresher(HttpClient httpClient) : ITokenRefresher
{
    public Task<AuthTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        => PostRefreshAsync("api/auth/refresh", new RefreshBody(refreshToken), kiosk: false, cancellationToken);

    public Task<AuthTokens?> RefreshKioskAsync(string refreshToken, CancellationToken cancellationToken = default)
        => PostRefreshAsync("api/auth/kiosk/refresh", new RefreshBody(refreshToken), kiosk: true, cancellationToken);

    private async Task<AuthTokens?> PostRefreshAsync(string path, RefreshBody body, bool kiosk, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(path, body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Un kiosco sin supervisión solo pierde la sesión si el Owner la revocó (401/403); un 5xx o un corte
                // de red no deben desemparejarlo: el error sube y la pantalla reintenta.
                if (kiosk && (int)response.StatusCode is not (401 or 403))
                {
                    response.EnsureSuccessStatusCode();
                }

                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken);
            return payload is null
                ? null
                : new AuthTokens(payload.AccessToken, payload.RefreshToken, payload.ExpiresAtUtc);
        }
        catch (HttpRequestException) when (!kiosk)
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
