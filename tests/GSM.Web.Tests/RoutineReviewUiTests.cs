using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Navigation.Modules;
using GSM.Panel.UI.Pages.RoutineReview;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;
using ReviewIndex = GSM.Panel.UI.Pages.RoutineReview.Index;

namespace GSM.Web.Tests;

/// <summary>R8: ajuste de semanas de revisión, lista "por revisar" y tarjeta del dashboard.</summary>
public class RoutineReviewUiTests : TestContext
{
    private static readonly DateOnly Today = GSM.UI.Shared.Formatting.GsmFormat.Today();

    private static (T Client, FakeClient Fake) Fake<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        return (proxy, (FakeClient)(object)proxy);
    }

    private static RoutineDueItem Item(string member, int reviewInDays, DateTimeOffset? lastReviewed = null)
        => new()
        {
            RoutineId = Guid.NewGuid(),
            MemberId = Guid.NewGuid(),
            MemberName = member,
            RoutineName = "Fuerza",
            StartsOn = Today.AddDays(-40),
            ReviewOn = Today.AddDays(reviewInDays),
            LastReviewedAt = lastReviewed,
        };

    [Fact]
    public void The_module_registers_the_review_menu_entry_and_the_settings_tab()
    {
        var module = new RoutinesPanelModule();

        Assert.Contains(module.MenuEntries, e => e.Href == "/routines/review");
        Assert.Contains(module.SettingsSections, s => s.Component == typeof(RoutineSettingsTab));
    }

    [Fact]
    public void The_settings_tab_shows_the_saved_weeks_and_saves_the_edited_value()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutineSettingsClient>();
        fake.Handlers["GetRoutineSettingsAsync"] = _ => new RoutineSettingsResponse { ReviewWeeks = 8 };
        fake.Handlers["UpdateRoutineSettingsAsync"] = args =>
            new RoutineSettingsResponse { ReviewWeeks = ((UpdateRoutineSettingsRequest)args[0]!).ReviewWeeks };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var tab = RenderComponent<RoutineSettingsTab>();

        tab.WaitForAssertion(() => Assert.Equal("8", tab.Find("input#routine-review-weeks").GetAttribute("value")));

        tab.Find("input#routine-review-weeks").Change("4");
        tab.Find("#routine-settings-save").Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(fake.Calls, c => c.Method == "UpdateRoutineSettingsAsync");
            Assert.Equal(4, Assert.IsType<UpdateRoutineSettingsRequest>(put.Args[0]).ReviewWeeks);
        });
    }

    [Fact]
    public void The_list_shows_overdue_routines_and_marking_one_reviewed_calls_the_api_and_removes_it()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var late = Item("Ana Pérez", -5);
        var soon = Item("Luis Gómez", 3, DateTimeOffset.UtcNow.AddDays(-40));
        var (client, fake) = Fake<IRoutinesClient>();
        fake.Handlers["GetRoutinesDueForReviewAsync"] = _ => new DueRoutinesResponse
        {
            Items = new List<RoutineDueItem> { late, soon },
            Mine = false,
            WindowDays = 7,
        };
        fake.Handlers["ReviewRoutineAsync"] = args => new RoutineReviewResponse
        {
            RoutineId = (Guid)args[0]!, ReviewOn = Today.AddDays(42), LastReviewedAt = DateTimeOffset.UtcNow,
        };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var page = RenderComponent<ReviewIndex>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ana Pérez", page.Markup);
            Assert.Contains("Luis Gómez", page.Markup);
            Assert.Contains("5 d de atraso", page.Markup);
        });

        // La primera carga no impone filtro: lo decide el servidor.
        Assert.Null(Assert.Single(fake.Calls, c => c.Method == "GetRoutinesDueForReviewAsync").Args[0]);

        page.Find($"[data-testid='review-{late.RoutineId}']").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Single(fake.Calls, c => c.Method == "ReviewRoutineAsync");
            Assert.DoesNotContain("Ana Pérez", page.Markup);
            Assert.Contains("Luis Gómez", page.Markup);
        });
    }

    [Fact]
    public void The_mine_switch_reflects_the_server_default_and_reloads_with_the_explicit_filter()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutinesClient>();
        fake.Handlers["GetRoutinesDueForReviewAsync"] = args =>
        {
            var mine = (bool?)args[0];
            return new DueRoutinesResponse
            {
                // Sin filtro explícito el servidor aplica "mis miembros" (entrenador).
                Mine = mine ?? true,
                WindowDays = 7,
                Items = (mine ?? true)
                    ? new List<RoutineDueItem> { Item("Ana Pérez", -1) }
                    : new List<RoutineDueItem> { Item("Ana Pérez", -1), Item("Luis Gómez", 2) },
            };
        };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var page = RenderComponent<ReviewIndex>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ana Pérez", page.Markup);
            Assert.DoesNotContain("Luis Gómez", page.Markup);
        });

        page.Find("[data-testid='review-mine'] input").Change(false);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Luis Gómez", page.Markup);
            Assert.Contains(fake.Calls, c => c.Method == "GetRoutinesDueForReviewAsync" && Equals(c.Args[0], false));
        });
    }

    [Fact]
    public void The_list_says_so_when_nothing_is_due()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutinesClient>();
        fake.Handlers["GetRoutinesDueForReviewAsync"] = _ => new DueRoutinesResponse
        {
            Items = new List<RoutineDueItem>(), Mine = true, WindowDays = 7,
        };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var page = RenderComponent<ReviewIndex>();

        page.WaitForAssertion(() => Assert.Contains("de tus miembros", page.Find("[data-testid='review-empty']").TextContent));
    }

    [Fact]
    public void The_dashboard_card_counts_the_routines_and_flags_the_overdue_ones()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutinesClient>();
        fake.Handlers["GetRoutinesDueForReviewAsync"] = _ => new DueRoutinesResponse
        {
            Items = new List<RoutineDueItem> { Item("Ana Pérez", -5), Item("Luis Gómez", -1), Item("Eva Ríos", 4) },
            Mine = true,
            WindowDays = 7,
        };
        Services.AddSingleton(client);

        var card = RenderComponent<RoutinesDueCard>();

        card.WaitForAssertion(() =>
        {
            var text = card.Find("[data-testid='routines-due']").TextContent;
            Assert.Contains("Rutinas por revisar", text);
            Assert.Contains("3", text);
            Assert.Contains("2 atrasadas", text);
        });
        Assert.Equal("/routines/review", card.Find("[data-testid='routines-due'] a").GetAttribute("href"));
    }

    [Fact]
    public void The_dashboard_card_stays_hidden_when_the_read_fails()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutinesClient>();
        fake.Handlers["GetRoutinesDueForReviewAsync"] = _ => throw new GsmApiException("boom", 500, "", new Dictionary<string, IEnumerable<string>>(), null);
        Services.AddSingleton(client);

        var card = RenderComponent<RoutinesDueCard>();

        Assert.Empty(card.FindAll("[data-testid='routines-due']"));
    }
}
