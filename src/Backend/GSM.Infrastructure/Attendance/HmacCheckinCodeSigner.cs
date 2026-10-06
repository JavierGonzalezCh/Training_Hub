using System.Security.Cryptography;
using System.Text;
using GSM.Application.Attendance.Abstractions;
using GSM.Domain.Attendance;
using Microsoft.Extensions.Options;

namespace GSM.Infrastructure.Attendance;

/// <summary>
/// Códigos del gym firmados con HMAC-SHA256. Rotativo: <c>r{ventana}.{firma}</c>; fijo: <c>s{versión}.{firma}</c>.
/// La carga se recalcula siempre con el gym del llamante, así que un código de otro gym falla solo.
/// </summary>
public sealed class HmacCheckinCodeSigner(IOptions<CheckinOptions> options) : ICheckinCodeSigner
{
    private const int SignatureLength = 22;

    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.SigningKey);

    public string Generate(Guid gymId, CheckinMode mode, int staticVersion, DateTimeOffset now)
        => mode == CheckinMode.Rotating
            ? Build('r', CurrentWindow(now), gymId)
            : Build('s', staticVersion, gymId);

    public bool Validate(Guid gymId, string? code, CheckinMode mode, int staticVersion, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Trim();

        if (mode == CheckinMode.Static)
        {
            return Matches(code, Build('s', staticVersion, gymId));
        }

        var window = CurrentWindow(now);
        return Matches(code, Build('r', window, gymId)) || Matches(code, Build('r', window - 1, gymId));
    }

    private static long CurrentWindow(DateTimeOffset now)
        => now.ToUnixTimeSeconds() / CheckinCodeWindow.Seconds;

    private string Build(char kind, long value, Guid gymId)
    {
        var payload = $"{gymId:N}|{kind}|{value}";
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=')[..SignatureLength];
        return $"{kind}{value}.{signature}";
    }

    private static bool Matches(string provided, string expected)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
