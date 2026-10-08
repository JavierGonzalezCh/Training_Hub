using System.Text.RegularExpressions;

namespace GSM.Domain.Members;

/// <summary>
/// Normalización de teléfonos a E.164 (<c>+573001234567</c>), con <c>+57</c> (Colombia) por defecto.
/// La misma regla está en SQL en <c>Migrations/Scripts/V2_M1.sql</c>: si cambia una, cambia la otra.
/// </summary>
public static partial class PhoneNumber
{
    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex E164Pattern();

    [GeneratedRegex(@"[\s\-\(\)\.]")]
    private static partial Regex Separators();

    public static bool IsE164(string? value) => value is not null && E164Pattern().IsMatch(value);

    /// <summary>
    /// Devuelve el número en E.164, o <c>null</c> si no se puede interpretar. Acepta separadores
    /// (espacios, guiones, paréntesis, puntos), el prefijo <c>00</c>, el <c>57</c> sin <c>+</c> y los
    /// 10 dígitos nacionales (móvil <c>3…</c> o fijo con indicativo <c>6…</c>).
    /// </summary>
    public static string? TryNormalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var cleaned = Separators().Replace(input.Trim(), string.Empty);

        if (E164Pattern().IsMatch(cleaned))
        {
            return cleaned;
        }

        if (cleaned.StartsWith("00", StringComparison.Ordinal) && E164Pattern().IsMatch("+" + cleaned[2..]))
        {
            return "+" + cleaned[2..];
        }

        if (cleaned.All(char.IsAsciiDigit))
        {
            if (cleaned.Length == 10 && cleaned[0] is '3' or '6')
            {
                return "+57" + cleaned;
            }

            if (cleaned.Length == 12 && cleaned.StartsWith("57", StringComparison.Ordinal) && cleaned[2] is '3' or '6')
            {
                return "+" + cleaned;
            }
        }

        return null;
    }
}
