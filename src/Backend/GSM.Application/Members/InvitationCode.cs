using System.Security.Cryptography;

namespace GSM.Application.Members;

/// <summary>Genera códigos de invitación cortos y legibles (sin caracteres ambiguos).</summary>
internal static class InvitationCode
{
    // Sin 0/O/1/I/L para que sea fácil de dictar y teclear.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    public static string New()
    {
        Span<char> buffer = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }

    public static string Normalize(string code)
        => (code ?? string.Empty).Trim().ToUpperInvariant();
}
