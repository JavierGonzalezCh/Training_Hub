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
    string? Permission = null,
    string? Feature = null)
{
    public string ShortTitle => BottomTitle ?? Title;

    /// <summary>
    /// <see cref="Permission"/> (un <c>GsmPermissions</c>) limita la entrada a quien lo tiene; el Owner los tiene todos.
    /// <see cref="Feature"/> (un <see cref="PanelFeatures"/>) la oculta cuando el gym apagó esa opción de interfaz; sin
    /// <paramref name="gym"/> (o sin flags cargados) se muestra.
    /// </summary>
    public bool IsVisibleTo(ClaimsPrincipal user, GSM.UI.Shared.Auth.GymContext? gym = null)
        => PanelRoles.Allows(Roles, user)
           && (Permission is null || GSM.UI.Shared.Auth.GsmClaimsPrincipalExtensions.HasPermission(user, Permission))
           && (Feature is null || PanelFeatures.IsOn(Feature, gym));
}

/// <summary>Opciones de interfaz del gym que pueden ocultar una entrada del menú.</summary>
public static class PanelFeatures
{
    /// <summary>Recepción, kiosco y aforo (<c>show_front_desk</c>, T5).</summary>
    public const string FrontDesk = "FrontDesk";

    public static bool IsOn(string feature, GSM.UI.Shared.Auth.GymContext? gym)
        => feature switch
        {
            FrontDesk => gym?.FeatureFlags?.ShowFrontDesk != false,
            _ => true,
        };
}

/// <summary>
/// Grupos del índice de <c>/settings</c>, en el orden en que se muestran. Son pocos y por tarea del dueño (no uno
/// por módulo): con once pestañas no se adivinaba dónde vivía cada ajuste. Cada módulo ubica sus secciones con
/// <see cref="PanelSettingsSection.Group"/>; lo que no declara grupo cae en <see cref="Other"/>, al final.
/// </summary>
public static class PanelSettingsGroups
{
    public const string Gym = "Tu gimnasio";
    public const string Team = "Personal";
    public const string Entry = "Entrada al gym";
    public const string Memberships = "Membresías y miembros";
    public const string Training = "Entrenamiento";
    public const string Communications = "Comunicaciones";
    public const string Other = "Otros";

    internal static readonly string[] Ordered = [Gym, Team, Entry, Memberships, Training, Communications, Other];

    internal static int Rank(string? group)
    {
        var index = Array.IndexOf(Ordered, group ?? Other);
        return index < 0 ? Ordered.Length : index;
    }
}

/// <summary>
/// Una sección de <c>/settings</c>: sale en el índice dentro de <see cref="Group"/>, con su <see cref="Description"/>,
/// y abre su propia página en <c>/settings/{Slug}</c>. <see cref="TracksUnsavedChanges"/> marca las que tienen un
/// botón "Guardar": en esas, salir con cambios sin guardar pide confirmación (el componente avisa al guardar con
/// <see cref="SettingsSectionState.MarkSaved"/>). Las que guardan al momento no lo necesitan.
/// </summary>
public sealed record PanelSettingsSection(
    string Title,
    int Order,
    Type Component,
    string? Roles = null,
    string? Group = null,
    string? Description = null,
    bool TracksUnsavedChanges = false)
{
    public string GroupName => string.IsNullOrWhiteSpace(Group) ? PanelSettingsGroups.Other : Group;

    /// <summary>"Reservas de clases" → <c>reservas-de-clases</c>: sin tildes ni espacios, para la URL.</summary>
    public string Slug => Slugify(Title);

    public bool IsVisibleTo(ClaimsPrincipal user) => PanelRoles.Allows(Roles, user);

    internal static string Slugify(string text)
    {
        var plain = new System.Text.StringBuilder();
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        return System.Text.RegularExpressions.Regex.Replace(plain.ToString(), "-{2,}", "-").Trim('-');
    }
}

/// <summary>
/// Estado de la sección de configuración abierta: si hay cambios sin guardar. La página de la sección lo marca
/// sucio con cualquier edición dentro y el componente lo limpia al guardar (llega como <c>CascadingParameter</c>;
/// fuera de <c>/settings</c>, por ejemplo en el onboarding, es nulo y no hace nada).
/// </summary>
public sealed class SettingsSectionState
{
    public bool IsDirty { get; private set; }

    public event Action? Changed;

    public void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            Changed?.Invoke();
        }
    }

    public void MarkSaved()
    {
        if (IsDirty)
        {
            IsDirty = false;
            Changed?.Invoke();
        }
    }
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
