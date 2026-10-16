using System.Security.Claims;

namespace GSM.Panel.UI.Navigation;

/// <summary>Grupos del menú del panel, en el orden en que se muestran.</summary>
public static class PanelSections
{
    public const string Operation = "Operación";
    public const string Business = "Negocio";
    public const string Training = "Entrenamiento";
    public const string Management = "Gestión";

    internal static readonly string[] Ordered = [Operation, Business, Training, Management];
}

/// <summary>
/// Una entrada del menú del panel. <see cref="Order"/> ordena dentro de su <see cref="Section"/>.
/// Con <see cref="BottomBar"/> también aparece en la barra inferior del celular, ordenada por
/// <see cref="BottomOrder"/> y con <see cref="BottomTitle"/> si el rótulo corto difiere del título.
/// <see cref="Roles"/> (separados por coma) limita quién la ve; nulo = cualquier sesión del panel.
/// </summary>
public sealed record PanelMenuEntry(
    string Title,
    string Icon,
    string Href,
    string Section,
    int Order,
    bool MatchAll = false,
    bool BottomBar = false,
    int BottomOrder = 0,
    string? BottomTitle = null,
    string? Roles = null,
    string? Permission = null)
{
    public string ShortTitle => BottomTitle ?? Title;

    /// <summary><see cref="Permission"/> (un <c>GsmPermissions</c>) limita la entrada a quien lo tiene; el Owner los tiene todos.</summary>
    public bool IsVisibleTo(ClaimsPrincipal user)
        => PanelRoles.Allows(Roles, user)
           && (Permission is null || GSM.UI.Shared.Auth.GsmClaimsPrincipalExtensions.HasPermission(user, Permission));
}

/// <summary>
/// Secciones de <c>/settings</c> (Gyms §7), en el orden en que se muestran. Cada módulo ubica sus pestañas en una
/// de ellas con <see cref="PanelSettingsSection.Group"/>; lo que no declara grupo cae en <see cref="Other"/>, al final.
/// </summary>
public static class PanelSettingsGroups
{
    public const string Gym = "Gym";
    public const string Team = "Equipo";
    public const string Memberships = "Membresías";
    public const string Entry = "Entrada al gym";
    public const string Routines = "Rutinas";
    public const string Equipment = "Equipos";
    public const string Classes = "Clases";
    public const string Communications = "Comunicaciones";
    public const string Achievements = "Logros";
    public const string Members = "Miembros";
    public const string Modules = "Módulos";
    public const string Other = "Otros";

    internal static readonly string[] Ordered =
    [
        Gym, Team, Memberships, Entry, Routines, Equipment, Classes, Communications, Achievements, Members, Modules, Other,
    ];

    internal static int Rank(string? group)
    {
        var index = Array.IndexOf(Ordered, group ?? Other);
        return index < 0 ? Ordered.Length : index;
    }
}

/// <summary>Una pestaña de <c>/settings</c>, dentro de la sección <see cref="Group"/>: el componente se muestra dentro de ella.</summary>
public sealed record PanelSettingsSection(string Title, int Order, Type Component, string? Roles = null, string? Group = null)
{
    public string GroupName => string.IsNullOrWhiteSpace(Group) ? PanelSettingsGroups.Other : Group;

    public bool IsVisibleTo(ClaimsPrincipal user) => PanelRoles.Allows(Roles, user);
}

/// <summary>Una sección de <c>/settings</c> con las pestañas que le aportaron los módulos.</summary>
public sealed record PanelSettingsGroup(string Name, IReadOnlyList<PanelSettingsSection> Sections);

internal static class PanelRoles
{
    public static bool Allows(string? roles, ClaimsPrincipal user)
        => string.IsNullOrWhiteSpace(roles)
           || roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(user.IsInRole);
}

/// <summary>
/// Lo que un módulo aporta al panel. Cada módulo tiene su propia clase (en <c>Navigation/Modules</c>) y
/// se descubre por assembly scanning en <see cref="PanelServiceCollectionExtensions.AddGsmPanel"/>:
/// agregar una entrada de menú o una pestaña de configuración no toca <c>NavMenu</c> ni <c>Settings/Index</c>.
/// </summary>
public interface IPanelModule
{
    string Module { get; }

    IEnumerable<PanelMenuEntry> MenuEntries => [];

    IEnumerable<PanelSettingsSection> SettingsSections => [];
}
