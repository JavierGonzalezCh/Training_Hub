using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// T7 — «Ver la configuración recomendada»: compara la configuración actual con la plantilla del tipo y guarda, por el endpoint
/// normal de cada módulo, solo lo que el dueño marca. Más <c>BusinessNoun</c>.
/// </summary>
public class RecommendedConfigUiTests : TestContext
{
    // Un gym con la configuración de siempre, y la plantilla de un estudio (docs/GSM-tipos-de-negocio.md §4).
    private static GymFeatureFlagsResponse CurrentFlags() => new()
    {
        ShowRoutines = true, ShowClasses = true, ShowProgress = true, ShowGamification = true, ShowCommunity = true,
        CheckinQr = true, MembersCanCreateRoutines = false, MembersCanReportEquipment = true, ShowRanking = true, ShowFrontDesk = true,
    };

    private static RecommendedConfiguration StudioTemplate() => new()
    {
        Type = "Studio",
        Modules = new GymFeatureFlagsResponse
        {
            ShowRoutines = false, ShowClasses = true, ShowProgress = true, ShowGamification = true, ShowCommunity = true,
            CheckinQr = true, MembersCanCreateRoutines = false, MembersCanReportEquipment = false, ShowRanking = true, ShowFrontDesk = true,
        },
        Checkin = new RecommendedCheckin { Mode = "Static", AssumedStayMinutes = 60, RemoteTraining = false },
        Memberships = new RecommendedMemberships { GraceDays = 0, ExpiringDays = 7, EnrollmentFee = 0 },
        Routines = new RecommendedRoutines { ReviewWeeks = 6 },
    };

    private static CheckinSettingsResponse CurrentCheckin() => new()
    {
        Mode = "Rotating", ByDocument = false, AssumedStayMinutes = 120, RemoteTraining = false,
    };

    private static RecommendedConfigDiff Diff(RecommendedConfiguration? recommended = null)
        => new(
            CurrentFlags(), CurrentCheckin(),
            new MembershipSettingsResponse { GraceDays = 3, ExpiringDays = 7, EnrollmentFee = 0 },
            new RoutineSettingsResponse { ReviewWeeks = 6 },
            recommended ?? StudioTemplate());

    // ---- La comparación ----

    [Fact]
    public void Only_the_options_that_differ_from_the_template_are_listed()
    {
        var keys = Diff().Differences().Select(c => c.Key).ToArray();

        Assert.Equal(
            [
                "modules.ShowRoutines", "modules.MembersCanReportEquipment",
                "checkin.Mode", "checkin.AssumedStayMinutes", "memberships.GraceDays",
            ],
            keys);
    }

    [Fact]
    public void A_gym_that_already_matches_the_template_has_nothing_to_apply()
    {
        var same = new RecommendedConfigDiff(
            StudioTemplate().Modules,
            new CheckinSettingsResponse { Mode = "Static", AssumedStayMinutes = 60, RemoteTraining = false },
            new MembershipSettingsResponse { GraceDays = 0, ExpiringDays = 7, EnrollmentFee = 0 },
            new RoutineSettingsResponse { ReviewWeeks = 6 },
            StudioTemplate());

        Assert.Empty(same.Differences());
    }

    [Fact]
    public void Requests_change_only_what_is_marked_and_keep_everything_else_as_it_is_today()
    {
        var diff = Diff();
        var marked = new HashSet<string> { "modules.ShowRoutines", "memberships.GraceDays" };

        var flags = diff.FlagsRequest(marked)!;
        Assert.False(flags.ShowRoutines);
        Assert.True(flags.MembersCanReportEquipment);   // difiere de la plantilla pero no se marcó
        Assert.True(flags.ShowClasses);

        var memberships = diff.MembershipsRequest(marked)!;
        Assert.Equal(0, memberships.GraceDays);
        Assert.Equal(7, memberships.ExpiringDays);

        // Nada marcado de esos módulos: no hay petición que mandar.
        Assert.Null(diff.CheckinRequest(marked));
        Assert.Null(diff.RoutinesRequest(marked));
    }

    [Fact]
    public void A_checkin_request_keeps_the_document_entry_and_the_unmarked_values()
    {
        var current = new CheckinSettingsResponse
        {
            Mode = "Rotating", ByDocument = true, AssumedStayMinutes = 120, RemoteTraining = true,
        };
        var diff = new RecommendedConfigDiff(
            CurrentFlags(), current,
            new MembershipSettingsResponse { GraceDays = 3, ExpiringDays = 7, EnrollmentFee = 0 },
            new RoutineSettingsResponse { ReviewWeeks = 6 }, StudioTemplate());

        var request = diff.CheckinRequest(new HashSet<string> { "checkin.Mode" })!;

        Assert.Equal("Static", request.Mode);
        Assert.True(request.ByDocument);
        Assert.Equal(120, request.AssumedStayMinutes);
        Assert.True(request.RemoteTraining);
    }

    // ---- El diálogo ----

    private FakeClient _auth = null!;

    private (IRenderedComponent<MudDialogProvider> Provider, FakeClient Gym, FakeClient Attendance, FakeClient Memberships, FakeClient Routines)
        OpenDialog(RecommendedConfiguration? recommended = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var (gym, gymFake) = Fake<IGymClient>();
        gymFake.Handlers["GetRecommendedConfigurationAsync"] = _ => recommended ?? StudioTemplate();
        gymFake.Handlers["FeatureFlagsGETAsync"] = _ => CurrentFlags();
        gymFake.Handlers["FeatureFlagsPUTAsync"] = _ => CurrentFlags();
        var (attendance, attendanceFake) = Fake<IAttendanceClient>();
        attendanceFake.Handlers["SettingsGETAsync"] = _ => CurrentCheckin();
        attendanceFake.Handlers["SettingsPUTAsync"] = _ => CurrentCheckin();
        var (memberships, membershipsFake) = Fake<IMembershipSettingsClient>();
        membershipsFake.Handlers["MembershipSettingsGETAsync"] = _ => new MembershipSettingsResponse { GraceDays = 3, ExpiringDays = 7, EnrollmentFee = 0 };
        membershipsFake.Handlers["MembershipSettingsPUTAsync"] = _ => new MembershipSettingsResponse { GraceDays = 0, ExpiringDays = 7, EnrollmentFee = 0 };
        var (routines, routinesFake) = Fake<IRoutineSettingsClient>();
        routinesFake.Handlers["GetRoutineSettingsAsync"] = _ => new RoutineSettingsResponse { ReviewWeeks = 6 };
        routinesFake.Handlers["UpdateRoutineSettingsAsync"] = _ => new RoutineSettingsResponse { ReviewWeeks = 6 };

        Services.AddSingleton(gym);
        Services.AddSingleton(attendance);
        Services.AddSingleton(memberships);
        Services.AddSingleton(routines);

        // El diálogo refresca la sesión tras aplicar (los módulos cambian el menú): SessionService real, auth falso.
        var (auth, authFake) = Fake<IAuthClient>();
        authFake.Handlers["MeAsync"] = _ => new MeResponse { Role = "owner" };
        _auth = authFake;
        Services.AddSingleton(auth);
        var store = new GSM.Client.Http.InMemoryTokenStore();
        Services.AddSingleton<GSM.Client.Http.ITokenStore>(store);
        Services.AddSingleton(new GsmAuthenticationStateProvider(store));
        Services.AddSingleton<GymContext>();
        Services.AddSingleton<SessionService>();

        var provider = RenderComponent<MudDialogProvider>();
        RenderComponent<MudPopoverProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        provider.InvokeAsync(() => dialogs.ShowAsync<RecommendedConfigDialog>("Configuración recomendada"));
        provider.WaitForAssertion(() => Assert.Contains("Configuración recomendada", provider.Markup));
        return (provider, gymFake, attendanceFake, membershipsFake, routinesFake);
    }

    [Fact]
    public void The_dialog_lists_the_recommendation_as_switches_all_on()
    {
        var (provider, _, _, _, _) = OpenDialog();

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("[data-testid='recommended-list']")));
        Assert.Contains("Rutinas", provider.Markup);
        Assert.Contains("Días de gracia", provider.Markup);
        Assert.Contains("Cartel impreso", provider.Markup);
        Assert.False(provider.Find("[data-testid='recommended-apply']").HasAttribute("disabled"));
    }

    [Fact]
    public void Applying_saves_only_the_marked_options_through_their_own_module_endpoints()
    {
        var (provider, gym, attendance, memberships, routines) = OpenDialog();
        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("[data-testid='recommended-list']")));

        foreach (var other in provider.FindAll("[data-testid^='recommended-'][data-testid*='.']:not([data-testid^='recommended-tip'])")
                     .Select(e => e.GetAttribute("data-testid")!)
                     .Where(t => t is not "recommended-modules.ShowRoutines" and not "recommended-memberships.GraceDays")
                     .ToList())
        {
            provider.Find($"[data-testid='{other}']").Change(false);
        }

        provider.Find("[data-testid='recommended-apply']").Click();

        provider.WaitForAssertion(() =>
        {
            var flags = Assert.IsType<UpdateFeatureFlagsRequest>(Assert.Single(gym.Calls, c => c.Method == "FeatureFlagsPUTAsync").Args[0]);
            Assert.False(flags.ShowRoutines);
            Assert.True(flags.MembersCanReportEquipment);

            var grace = Assert.IsType<UpdateMembershipSettingsRequest>(
                Assert.Single(memberships.Calls, c => c.Method == "MembershipSettingsPUTAsync").Args[0]);
            Assert.Equal(0, grace.GraceDays);
            Assert.Equal(7, grace.ExpiringDays);
        });
        // Lo que no se marcó no se toca: ni check-in ni rutinas.
        Assert.DoesNotContain(attendance.Calls, c => c.Method == "SettingsPUTAsync");
        Assert.DoesNotContain(routines.Calls, c => c.Method == "UpdateRoutineSettingsAsync");
        Assert.Contains(_auth.Calls, c => c.Method == "MeAsync");
    }

    [Fact]
    public void Mark_all_selects_every_difference_and_apply_saves_each_module()
    {
        var (provider, gym, attendance, memberships, _) = OpenDialog();
        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("[data-testid='recommended-list']")));

        provider.Find("[data-testid='recommended-apply']").Click();

        provider.WaitForAssertion(() =>
        {
            Assert.Single(gym.Calls, c => c.Method == "FeatureFlagsPUTAsync");
            var checkin = Assert.IsType<UpdateCheckinSettingsRequest>(
                Assert.Single(attendance.Calls, c => c.Method == "SettingsPUTAsync").Args[0]);
            Assert.Equal("Static", checkin.Mode);
            Assert.Equal(60, checkin.AssumedStayMinutes);
            Assert.Single(memberships.Calls, c => c.Method == "MembershipSettingsPUTAsync");
        });
    }

    [Fact]
    public void A_gym_that_already_matches_gets_a_message_instead_of_a_table()
    {
        var aligned = StudioTemplate();
        // El gym del fake tiene la configuración de siempre; una plantilla igual a ella no tiene diferencias.
        aligned.Modules = CurrentFlags();
        aligned.Checkin = new RecommendedCheckin { Mode = "Rotating", AssumedStayMinutes = 120, RemoteTraining = false };
        aligned.Memberships = new RecommendedMemberships { GraceDays = 3, ExpiringDays = 7, EnrollmentFee = 0 };
        aligned.Type = "Gym";

        var (provider, _, _, _, _) = OpenDialog(aligned);

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("[data-testid='recommended-aligned']")));
        Assert.Empty(provider.FindAll("[data-testid='recommended-apply']"));
    }

    // ---- BusinessNoun ----

    [Theory]
    [InlineData("Gym", "gimnasio", "tu gimnasio")]
    [InlineData("Box", "box", "tu box")]
    [InlineData("Studio", "estudio", "tu estudio")]
    [InlineData("PersonalTrainer", "negocio", "tu negocio")]
    [InlineData("Academy", "academia", "tu academia")]
    [InlineData("Other", "negocio", "tu negocio")]
    [InlineData(null, "gimnasio", "tu gimnasio")]
    [InlineData("algo-desconocido", "gimnasio", "tu gimnasio")]
    public void The_business_noun_follows_the_type_and_falls_back_to_gym(string? type, string bare, string your)
    {
        Assert.Equal(bare, BusinessNoun.Bare(type));
        Assert.Equal(your, BusinessNoun.Your(type));
    }

    [Fact]
    public void The_capitalized_form_starts_a_sentence()
        => Assert.Equal("Tu estudio", BusinessNoun.YourCapitalized("Studio"));
}
