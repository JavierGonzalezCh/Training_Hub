using GSM.Client;
using GSM.Client.Generated;

namespace GSM.UI.Shared.Auth;

/// <summary>
/// Modo staff / modo miembro (apps y vistas §4): quien tiene en un mismo gym un acceso de equipo (owner/staff)
/// y uno de miembro cambia entre los dos. Cada modo es un acceso distinto con su propio token: nunca se
/// mezclan roles en una sesión.
/// </summary>
public static class AccessModes
{
    public static bool IsTeam(string? role) => role is GsmRoles.Owner or GsmRoles.Staff;

    /// <summary>
    /// El acceso del otro modo en el mismo gym que el acceso actual, o <c>null</c> si la persona no tiene
    /// los dos. Si ningún acceso viene marcado como actual, no hay a dónde cambiar.
    /// </summary>
    public static GymAccessOption? OtherMode(IReadOnlyList<GymAccessOption> accesses)
    {
        var current = accesses.FirstOrDefault(a => a.IsCurrent == true);
        if (current is null)
        {
            return null;
        }

        var currentIsTeam = IsTeam(current.Role);
        return accesses.FirstOrDefault(a =>
            a.GymId == current.GymId
            && a.AccessId != current.AccessId
            && IsTeam(a.Role) != currentIsTeam
            && (IsTeam(a.Role) || a.Role == GsmRoles.Member));
    }

    /// <summary>"Pasar a modo miembro" / "Pasar a modo staff".</summary>
    public static string SwitchLabel(string? targetRole)
        => targetRole == GsmRoles.Member ? "Pasar a modo miembro" : "Pasar a modo staff";
}
