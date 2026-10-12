using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Notifications.Push;

/// <summary>
/// Envío por la API HTTP v1 de Firebase Cloud Messaging. Obtiene el token de acceso OAuth2 firmando un JWT
/// RS256 con la cuenta de servicio (sin dependencias nuevas) y lo conserva hasta poco antes de vencer.
/// <para>
/// El mensaje web va solo con <c>data</c> (título, texto y enlace): el service worker de la PWA lo muestra
/// él mismo. En Android también lleva <c>notification</c> para que el sistema lo muestre con la app cerrada.
/// </para>
/// <para>
/// <b>Sin verificar contra Firebase real:</b> todavía no hay proyecto. Un token que FCM responde como
/// <c>UNREGISTERED</c> (404) o <c>INVALID_ARGUMENT</c> (400) devuelve <see cref="PushSendResult.InvalidToken"/>;
/// cualquier otro error lanza para que el dispatcher reintente.
/// </para>
/// </summary>
public sealed class FcmPushSender : IPushSender
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly HttpClient _httpClient;
    private readonly FcmOptions _options;
    private readonly ILogger<FcmPushSender> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly ServiceAccount? _account;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public FcmPushSender(HttpClient httpClient, IOptions<FcmOptions> options, ILogger<FcmPushSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _account = ServiceAccount.TryParse(_options.ServiceAccountJson);
    }

    public bool IsConfigured => _options.IsConfigured && _account is not null;

    public async Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return PushSendResult.NotConfigured;
        }

        var accessToken = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{_options.ProjectId}/messages:send")
        {
            Content = JsonContent.Create(BuildPayload(message)),
        };
        request.Headers.Authorization = new("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return PushSendResult.Sent;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound
            || (response.StatusCode == HttpStatusCode.BadRequest
                && body.Contains("INVALID_ARGUMENT", StringComparison.Ordinal)))
        {
            _logger.LogInformation("FCM rechazó el token del aviso {NotificationId}: {Status}", message.NotificationId, (int)response.StatusCode);
            return PushSendResult.InvalidToken;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _accessToken = null; // el próximo intento pide uno nuevo
        }

        throw new HttpRequestException($"FCM respondió {(int)response.StatusCode}: {Truncate(body, 300)}");
    }

    internal static object BuildPayload(PushMessage message)
    {
        var data = new Dictionary<string, string>
        {
            ["title"] = message.Title,
            ["body"] = message.Body,
            ["link"] = message.Link ?? string.Empty,
            ["notificationId"] = message.NotificationId.ToString(),
            ["gymId"] = message.GymId.ToString(),
        };

        var inner = new Dictionary<string, object?>
        {
            ["token"] = message.Token,
            ["data"] = data,
        };

        if (message.Platform == PushPlatform.Android)
        {
            inner["android"] = new
            {
                priority = "high",
                notification = new { title = message.Title, body = message.Body },
            };
        }
        else
        {
            inner["webpush"] = new { headers = new Dictionary<string, string> { ["Urgency"] = "high" } };
        }

        return new { message = inner };
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            var account = _account!;
            var now = DateTimeOffset.UtcNow;
            var assertion = SignJwt(account, now);
            using var request = new HttpRequestMessage(HttpMethod.Post, account.TokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion,
                }),
            };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Google OAuth respondió {(int)response.StatusCode}.");
            }

            var node = JsonNode.Parse(json)!;
            _accessToken = node["access_token"]!.GetValue<string>();
            var seconds = node["expires_in"]?.GetValue<int>() ?? 3600;
            _accessTokenExpiresAt = now.AddSeconds(seconds);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string SignJwt(ServiceAccount account, DateTimeOffset now)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"RS256","typ":"JWT"}"""));
        var claims = JsonSerializer.Serialize(new
        {
            iss = account.ClientEmail,
            scope = Scope,
            aud = account.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(55).ToUnixTimeSeconds(),
        });
        var unsigned = $"{header}.{Base64Url(Encoding.UTF8.GetBytes(claims))}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);
        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record ServiceAccount(string ClientEmail, string PrivateKey, string TokenUri)
    {
        public static ServiceAccount? TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var node = JsonNode.Parse(json);
                var email = node?["client_email"]?.GetValue<string>();
                var key = node?["private_key"]?.GetValue<string>();
                var uri = node?["token_uri"]?.GetValue<string>() ?? "https://oauth2.googleapis.com/token";
                return string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key)
                    ? null
                    : new ServiceAccount(email, key, uri);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return null;
            }
        }
    }
}
