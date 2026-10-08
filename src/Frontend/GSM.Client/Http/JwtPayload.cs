using System.Text.Json;

namespace GSM.Client.Http;

/// <summary>
/// Lectura <b>sin validar</b> del payload del access token. El cliente solo la usa para pintar
/// la UI según el rol; la validación de verdad la hace la API en cada petición.
/// Claims de GSM: <c>sub</c>, <c>gym_id</c>, <c>role</c>, <c>name</c>, <c>impersonating</c>, <c>exp</c> y
/// <c>perm</c> (permisos especiales de staff; ver <see cref="GsmPermissions"/>) e
/// <c>impersonation_mode</c> (<c>read</c> o <c>write</c>, solo en tokens de impersonación).
/// </summary>
public sealed record JwtPayload(
    string? Subject,
    string? Role,
    string? Name,
    Guid? GymId,
    bool Impersonating,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string>? Permissions = null,
    string? ImpersonationMode = null)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } exp && exp <= now;

    public static JwtPayload? TryRead(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Decode(parts[1]));
            var root = document.RootElement;

            return new JwtPayload(
                Subject: GetString(root, "sub"),
                Role: GetString(root, "role"),
                Name: GetString(root, "name"),
                GymId: Guid.TryParse(GetString(root, "gym_id"), out var gymId) ? gymId : null,
                Impersonating: string.Equals(GetString(root, "impersonating"), "true", StringComparison.OrdinalIgnoreCase),
                ExpiresAt: root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null,
                Permissions: GetStrings(root, "perm"),
                ImpersonationMode: GetString(root, "impersonation_mode"));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Un claim que puede venir como texto suelto o como arreglo (<c>perm</c>).</summary>
    private static IReadOnlyList<string> GetStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList(),
            JsonValueKind.String => [value.GetString()!],
            _ => [],
        };
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static byte[] Decode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };
        return Convert.FromBase64String(padded);
    }
}
