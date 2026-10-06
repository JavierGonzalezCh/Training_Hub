using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using GSM.Application.Communications.Abstractions;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Communications;

public sealed class CommunicationsOptions
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
/// Token de baja: <c>{memberId:N}.{gymId:N}.{firma}</c>, firmado con HMAC-SHA256. No caduca: un
/// correo de hace meses debe poder seguir dando de baja. La baja es idempotente.
/// </summary>
public sealed class UnsubscribeLinks(IOptions<CommunicationsOptions> options) : IUnsubscribeLinks
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.UnsubscribeSigningKey);
    private readonly string _baseUrl = options.Value.PublicApiBaseUrl.TrimEnd('/');

    public string BuildUrl(Guid memberId, Guid gymId)
        => $"{_baseUrl}/api/public/unsubscribe?t={Uri.EscapeDataString(Token(memberId, gymId))}";

    public bool TryRead(string? token, out Guid memberId, out Guid gymId)
    {
        memberId = Guid.Empty;
        gymId = Guid.Empty;

        var parts = token?.Split('.');
        if (parts is not { Length: 3 }
            || !Guid.TryParseExact(parts[0], "N", out var member)
            || !Guid.TryParseExact(parts[1], "N", out var gym))
        {
            return false;
        }

        var expected = Token(member, gym);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token!), Encoding.UTF8.GetBytes(expected)))
        {
            return false;
        }

        memberId = member;
        gymId = gym;
        return true;
    }

    private string Token(Guid memberId, Guid gymId)
    {
        var payload = $"{memberId:N}.{gymId:N}";
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"unsubscribe|{payload}"));
        var signature = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{payload}.{signature}";
    }
}
