using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Client.Http;
using GSM.UI.Shared.Auth;
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

    private FakeClient RegisterGymAndSession(GymFeatureFlagsResponse flags)
    {
        var (gym, gymFake) = Fake<IGymClient>();
        gymFake.Handlers["FeatureFlagsGETAsync"] = _ => flags;
        gymFake.Handlers["FeatureFlagsPUTAsync"] = _ => flags;
        Services.AddSingleton(gym);
        var (auth, authFake) = Fake<IAuthClient>();
        authFake.Handlers["MeAsync"] = _ => new MeResponse();
        Services.AddSingleton(auth);
        var store = new InMemoryTokenStore();
        Services.AddSingleton<ITokenStore>(store);
        Services.AddSingleton(new GsmAuthenticationStateProvider(store));
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<SessionService>();
        return gymFake;
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
        var gymFake = RegisterGymAndSession(new GymFeatureFlagsResponse { ShowRoutines = true, MembersCanCreateRoutines = false });
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
        // Los flags no cambiaron: no se toca el PUT de módulos.
        Assert.DoesNotContain(gymFake.Calls, c => c.Method == "FeatureFlagsPUTAsync");
    }

    [Fact]
    public void The_settings_tab_owns_the_routine_flags_and_keeps_the_other_ones()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (client, fake) = Fake<IRoutineSettingsClient>();
        fake.Handlers["GetRoutineSettingsAsync"] = _ => new RoutineSettingsResponse { ReviewWeeks = 6 };
        fake.Handlers["UpdateRoutineSettingsAsync"] = _ => new RoutineSettingsResponse { ReviewWeeks = 6 };
        Services.AddSingleton(client);
        var gymFake = RegisterGymAndSession(new GymFeatureFlagsResponse
        {
            ShowRoutines = true, MembersCanCreateRoutines = false, ShowClasses = true, MembersCanReportEquipment = true,
        });
        RenderComponent<MudPopoverProvider>();

        var tab = RenderComponent<RoutineSettingsTab>();

        tab.WaitForAssertion(() => Assert.NotEmpty(tab.FindAll("[data-testid='members-can-create-routines']")));
        tab.Find("[data-testid='members-can-create-routines']").Change(true);
        tab.Find("#routine-settings-save").Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(gymFake.Calls, c => c.Method == "FeatureFlagsPUTAsync");
            var request = Assert.IsType<UpdateFeatureFlagsRequest>(put.Args[0]);
            Assert.True(request.MembersCanCreateRoutines);
            Assert.True(request.ShowRoutines);
            Assert.True(request.ShowClasses);
            Assert.True(request.MembersCanReportEquipment);
        });
    }

    [Fact]
    public void The_equipment_tab_owns_the_report_flag()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var gymFake = RegisterGymAndSession(new GymFeatureFlagsResponse { ShowRoutines = true, MembersCanReportEquipment = false });
        RenderComponent<MudPopoverProvider>();

        var tab = RenderComponent<EquipmentSettingsTab>();

        tab.WaitForAssertion(() => Assert.NotEmpty(tab.FindAll("[data-testid='members-can-report-equipment']")));
        tab.Find("[data-testid='members-can-report-equipment']").Change(true);
        tab.Find("#equipment-settings-save").Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(gymFake.Calls, c => c.Method == "FeatureFlagsPUTAsync");
            var request = Assert.IsType<UpdateFeatureFlagsRequest>(put.Args[0]);
            Assert.True(request.MembersCanReportEquipment);
            Assert.True(request.ShowRoutines);
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

        // El nombre es un enlace de verdad a la ficha (se abre en otra pestaña y el lector lo anuncia como enlace).
        Assert.Equal($"/members/{late.MemberId}", page.FindAll("a").First(a => a.TextContent.Contains("Ana Pérez")).GetAttribute("href"));

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

        page.Find("[data-testid='review-mine'] input, input[data-testid='review-mine']").Change(false);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Luis Gómez", page.Markup);
            Assert.Contains(fake.Calls, c => c.Method == "GetRoutinesDueForReviewAsync" && Equals(c.Args[0], false));
        });
    }

    [Fact]
    public void Each_row_says_why_the_routine_is_up_for_review()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var fresh = Item("Ana Pérez", 0);
        fresh.ReviewReason = "CreatedByMember";
        var edited = Item("Luis Gómez", 0);
        edited.ReviewReason = "EditedByMember";
        var periodic = Item("Eva Ríos", -2);
        periodic.ReviewReason = "Scheduled";
        var (client, fake) = Fake<IRoutinesClient>();
        fake.Handlers["GetRoutinesDueForReviewAsync"] = _ => new DueRoutinesResponse
        {
            Items = new List<RoutineDueItem> { fresh, edited, periodic }, Mine = false, WindowDays = 7,
        };
        Services.AddSingleton(client);
        RenderComponent<MudPopoverProvider>();

        var page = RenderComponent<ReviewIndex>();

        Assert.Contains("Rutina nueva del miembro", page.Find($"[data-testid='review-reason-{fresh.RoutineId}']").TextContent);
        Assert.Contains("Editada por el miembro", page.Find($"[data-testid='review-reason-{edited.RoutineId}']").TextContent);
        Assert.Contains("Revisión periódica", page.Find($"[data-testid='review-reason-{periodic.RoutineId}']").TextContent);
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
