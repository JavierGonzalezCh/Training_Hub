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
    string? Roles = null)
{
    public string ShortTitle => BottomTitle ?? Title;

    public bool IsVisibleTo(ClaimsPrincipal user) => PanelRoles.Allows(Roles, user);
}

/// <summary>Una pestaña de <c>/settings</c>: el componente se muestra dentro de la pestaña.</summary>
public sealed record PanelSettingsSection(string Title, int Order, Type Component, string? Roles = null)
{
    public bool IsVisibleTo(ClaimsPrincipal user) => PanelRoles.Allows(Roles, user);
}

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
