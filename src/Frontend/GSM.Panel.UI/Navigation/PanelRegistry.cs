using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace GSM.Panel.UI.Navigation;

public sealed record PanelMenuGroup(string Label, IReadOnlyList<PanelMenuEntry> Entries);

/// <summary>Reúne lo que declara cada <see cref="IPanelModule"/> y lo entrega ordenado y filtrado por rol.</summary>
public sealed class PanelRegistry
{
    private readonly IReadOnlyList<PanelMenuEntry> _entries;
    private readonly IReadOnlyList<PanelSettingsSection> _settings;

    public PanelRegistry(IEnumerable<IPanelModule> modules)
    {
        var all = modules.ToList();
        _entries = all.SelectMany(m => m.MenuEntries).ToList();
        _settings = all.SelectMany(m => m.SettingsSections).OrderBy(s => s.Order).ToList();
    }

    /// <summary>El menú lateral de escritorio: todas las entradas visibles, por grupo.</summary>
    public IReadOnlyList<PanelMenuGroup> GetMenu(ClaimsPrincipal user) => Group(Visible(user));

    /// <summary>Las entradas de la barra inferior del celular, en su orden.</summary>
    public IReadOnlyList<PanelMenuEntry> GetBottomBar(ClaimsPrincipal user)
        => Visible(user).Where(e => e.BottomBar).OrderBy(e => e.BottomOrder).ToList();

    /// <summary>"Más": lo que no cabe en la barra inferior, por grupo.</summary>
    public IReadOnlyList<PanelMenuGroup> GetMore(ClaimsPrincipal user) => Group(Visible(user).Where(e => !e.BottomBar));

    public IReadOnlyList<PanelSettingsSection> GetSettingsSections(ClaimsPrincipal user)
        => _settings.Where(s => s.IsVisibleTo(user)).ToList();

    /// <summary>La sección de <c>/settings/{slug}</c>, si existe y el usuario puede verla.</summary>
    public PanelSettingsSection? FindSettingsSection(ClaimsPrincipal user, string? slug)
        => GetSettingsSections(user).FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Las secciones de configuración visibles para el usuario, por grupo y en orden; los grupos vacíos no salen.</summary>
    public IReadOnlyList<PanelSettingsGroup> GetSettingsGroups(ClaimsPrincipal user)
        => GetSettingsSections(user)
            .GroupBy(s => s.GroupName)
            .OrderBy(g => PanelSettingsGroups.Rank(g.Key))
            .Select(g => new PanelSettingsGroup(g.Key, g.OrderBy(s => s.Order).ToList()))
            .ToList();

    private IEnumerable<PanelMenuEntry> Visible(ClaimsPrincipal user) => _entries.Where(e => e.IsVisibleTo(user));

    private static IReadOnlyList<PanelMenuGroup> Group(IEnumerable<PanelMenuEntry> entries)
        => entries
            .GroupBy(e => e.Section)
            .OrderBy(g => SectionRank(g.Key))
            .ThenBy(g => g.Min(e => e.Order))
            .Select(g => new PanelMenuGroup(g.Key, g.OrderBy(e => e.Order).ToList()))
            .ToList();

    private static int SectionRank(string section)
    {
        var index = Array.IndexOf(PanelSections.Ordered, section);
        return index < 0 ? PanelSections.Ordered.Length : index;
    }
}

public static class PanelServiceCollectionExtensions
{
    /// <summary>Registra el registro del panel y todos los <see cref="IPanelModule"/> del assembly del panel.</summary>
    public static IServiceCollection AddGsmPanel(this IServiceCollection services)
    {
        var moduleType = typeof(IPanelModule);
        foreach (var type in typeof(PanelRegistry).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && moduleType.IsAssignableFrom(t)))
        {
            services.AddSingleton(moduleType, type);
        }

        services.AddSingleton<PanelRegistry>();
        services.AddScoped<GSM.Panel.UI.Onboarding.OnboardingGate>();
        return services;
    }
}
