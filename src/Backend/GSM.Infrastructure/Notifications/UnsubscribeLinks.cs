using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using GSM.Application.Notifications.Abstractions;
using GSM.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Notifications;

/// <summary>
/// Claves del enlace de baja. Se leen de la sección <c>Communications</c> (donde ya están configuradas en
/// cada ambiente desde la etapa 13): mover las claves al módulo nuevo obligaría a reconfigurar producción.
/// </summary>
public sealed class NotificationLinkOptions
{
    public const string SectionName = "Communications";

    /// <summary>Clave HMAC del enlace de baja. Mínimo 32 caracteres; distinta de las demás.</summary>
    [Required]
    [MinLength(32)]
    public string UnsubscribeSigningKey { get; init; } = string.Empty;

    /// <summary>URL pública de la API, base del enlace de baja (p. ej. <c>https://api.dominio</c>).</summary>
    [Required]
    [Url]
    public string PublicApiBaseUrl { get; init; } = string.Empty;
}

/// <summary>
/// Token de baja: <c>{memberId:N}.{gymId:N}.{categoría}.{firma}</c>, firmado con HMAC-SHA256. No caduca: un
/// correo de hace meses debe poder seguir dando de baja. La baja es idempotente. Los tokens anteriores a N4
/// (<c>{memberId:N}.{gymId:N}.{firma}</c>, sin categoría) siguen siendo válidos y apagan el correo de todas.
/// </summary>
public sealed class UnsubscribeLinks(IOptions<NotificationLinkOptions> options) : IUnsubscribeLinks
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.UnsubscribeSigningKey);
    private readonly string _baseUrl = options.Value.PublicApiBaseUrl.TrimEnd('/');

    public string BuildUrl(Guid memberId, Guid gymId, NotificationCategory category)
        => $"{_baseUrl}/api/public/unsubscribe?t={Uri.EscapeDataString(Token($"{memberId:N}.{gymId:N}.{CategoryKey(category)}"))}";

    public bool TryRead(string? token, out Guid memberId, out Guid gymId, out NotificationCategory? category)
    {
        memberId = Guid.Empty;
        gymId = Guid.Empty;
        category = null;

        var parts = token?.Split('.');
        if (parts is not { Length: 3 or 4 }
            || !Guid.TryParseExact(parts[0], "N", out var member)
            || !Guid.TryParseExact(parts[1], "N", out var gym))
        {
            return false;
        }

        NotificationCategory? parsed = null;
        if (parts.Length == 4)
        {
            if (!TryParseCategoryKey(parts[2], out var value))
            {
                return false;
            }

            parsed = value;
        }

        var payload = parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : $"{parts[0]}.{parts[1]}";
        var expected = Token(payload);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token!), Encoding.UTF8.GetBytes(expected)))
        {
            return false;
        }

        memberId = member;
        gymId = gym;
        category = parsed;
        return true;
    }

    private static string CategoryKey(NotificationCategory category) => category.ToString().ToLowerInvariant();

    private static bool TryParseCategoryKey(string key, out NotificationCategory category)
        => Enum.TryParse(key, ignoreCase: true, out category)
            && Enum.IsDefined(category)
            && CategoryKey(category) == key;

    private string Token(string payload)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"unsubscribe|{payload}"));
        var signature = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{payload}.{signature}";
    }
}
