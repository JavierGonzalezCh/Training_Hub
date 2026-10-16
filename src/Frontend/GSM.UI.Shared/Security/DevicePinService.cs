using System.Security.Cryptography;

namespace GSM.UI.Shared.Security;

public enum PinCheckResult
{
    Ok,
    Wrong,

    /// <summary>Demasiados intentos fallidos: el bloqueo cierra la sesión y la persona entra de nuevo con su contraseña.</summary>
    TooManyAttempts,
}

/// <summary>
/// PIN propio de 4 a 6 dígitos del modo staff (apps y vistas §7), para teléfonos sin bloqueo configurado.
/// Nunca se guarda en claro: PBKDF2-SHA256 con sal aleatoria en el almacén seguro. Nunca se loguea.
/// </summary>
public sealed class DevicePinService(ISecureValueStore store)
{
    public const int MinLength = 4;
    public const int MaxLength = 6;
    public const int MaxAttempts = 5;
    private const int Iterations = 100_000;
    private const string HashKey = "gsm.lock.pin";

    private int _failures;

    public static bool IsValidFormat(string? pin)
        => pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    public async Task<bool> HasPinAsync() => !string.IsNullOrEmpty(await store.GetAsync(HashKey));

    public async Task SetPinAsync(string pin)
    {
        if (!IsValidFormat(pin))
        {
            throw new ArgumentException("El PIN debe tener de 4 a 6 dígitos.", nameof(pin));
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        await store.SetAsync(HashKey, Encode(salt, Hash(pin, salt)));
        _failures = 0;
    }

    public async Task<PinCheckResult> VerifyAsync(string pin)
    {
        var stored = await store.GetAsync(HashKey);
        if (!string.IsNullOrEmpty(stored) && TryDecode(stored, out var salt, out var hash)
            && CryptographicOperations.FixedTimeEquals(Hash(pin ?? string.Empty, salt), hash))
        {
            _failures = 0;
            return PinCheckResult.Ok;
        }

        _failures++;
        return _failures >= MaxAttempts ? PinCheckResult.TooManyAttempts : PinCheckResult.Wrong;
    }

    public void Clear()
    {
        store.Remove(HashKey);
        _failures = 0;
    }

    private static byte[] Hash(string pin, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static string Encode(byte[] salt, byte[] hash) => Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);

    private static bool TryDecode(string stored, out byte[] salt, out byte[] hash)
    {
        salt = hash = [];
        var parts = stored.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[0]);
            hash = Convert.FromBase64String(parts[1]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
