using System.Text;

namespace GSM.Domain.Members;

/// <summary>
/// Texto de los enums de la ficha tal como viajan por la API y se guardan en el historial:
/// snake_case (<c>WalkIn</c> → <c>walk_in</c>), el mismo que usa la base.
/// </summary>
public static class EnumText
{
    public static string? Snake(Enum? value)
    {
        if (value is null)
        {
            return null;
        }

        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary>Lee <c>walk_in</c>, <c>WalkIn</c> o <c>walkin</c>. Vacío = nulo; desconocido = falso.</summary>
    public static bool TryParse<T>(string? text, out T? value) where T : struct, Enum
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var wanted = text.Replace("_", string.Empty).Trim();
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }
}
