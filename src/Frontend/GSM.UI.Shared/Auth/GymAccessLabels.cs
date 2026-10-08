using GSM.Client;

namespace GSM.UI.Shared.Auth;

/// <summary>Textos del selector de gym: cada acceso se muestra por separado ("Gimnasio Demo · Staff").</summary>
public static class GymAccessLabels
{
    public static string Role(string? role) => role switch
    {
        GsmRoles.Owner => "Propietario",
        GsmRoles.Staff => "Staff",
        GsmRoles.Member => "Miembro",
        _ => "—",
    };

    /// <summary>"Gimnasio Demo · Staff".</summary>
    public static string Full(string gymName, string? role) => $"{gymName} · {Role(role)}";
}
