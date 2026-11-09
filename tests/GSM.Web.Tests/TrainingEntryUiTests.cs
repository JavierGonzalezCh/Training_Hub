using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Attendance;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// A8/C4 — en un gym sin registro de entrada en la puerta, el inicio del miembro dice que el día cuenta al registrar
/// el primer ejercicio, sin botón.
/// </summary>
public class TrainingEntryUiTests : TestContext
{
    private sealed class NoScanner : IQrScanner
    {
        public bool IsSupported => false;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private FakeClient _me = null!;
    private FakeClient _attendance = null!;

    private IRenderedComponent<Home> RenderHome(
        bool routineEntry, DateTimeOffset? enteredAt = null, bool firstLog = false, bool showFrontDesk = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();

        var me = DispatchProxy.Create<IMeClient, FakeClient>();
        _me = (FakeClient)(object)me;
        _me.Handlers["GetMyHomeAsync"] = _ => new MemberHomeResponse
        {
            Entry = new HomeEntryBlock { CheckedInToday = enteredAt is not null, CheckedInAt = enteredAt },
            Gym = new HomeGymBlock { OccupancyLevel = "calm" },
        };
        var consents = DispatchProxy.Create<IMeConsentsClient, FakeClient>();
        ((FakeClient)(object)consents).Handlers["ListMyConsentsAsync"] = _ => new List<MemberConsentStatus>();
        var attendance = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        _attendance = (FakeClient)(object)attendance;
        Services.AddSingleton(me);
        Services.AddSingleton(consents);
        Services.AddSingleton(attendance);
        Services.AddSingleton<IQrScanner>(new NoScanner());

        var gym = new GymContext();
        gym.Set(new MeResponse
        {
            FeatureFlags = new MeFeatureFlags { ShowGamification = true, CheckinQr = !routineEntry, RoutineEntry = routineEntry, RoutineEntryFirstLog = firstLog, ShowFrontDesk = showFrontDesk },
        }, false);
        Services.AddSingleton(gym);

        var context = new MemberContext();
        context.Set(new MemberDetail { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active" });
        Services.AddSingleton(context);

        Services.GetRequiredService<ViewportState>().Pin(true);
        return RenderComponent<Home>();
    }

    [Fact]
    public void Without_door_registration_the_home_says_the_day_counts_with_the_first_exercise()
    {
        var cut = RenderHome(routineEntry: true, firstLog: true);

        Assert.Empty(cut.FindAll("[data-testid='training-entry-start']"));
        Assert.Contains("primer ejercicio", cut.Find("[data-testid='training-entry-first-log']").TextContent);
    }

    [Fact]
    public void A_day_that_already_counts_shows_it_without_the_button()
    {
        var cut = RenderHome(routineEntry: true, enteredAt: DateTimeOffset.UtcNow);

        Assert.NotEmpty(cut.FindAll("[data-testid='training-entry-done']"));
        Assert.Empty(cut.FindAll("[data-testid='training-entry-start']"));
    }

    [Fact]
    public void With_first_log_a_day_that_already_counts_still_shows_it_on_the_home()
    {
        var cut = RenderHome(routineEntry: true, enteredAt: DateTimeOffset.UtcNow, firstLog: true);

        Assert.NotEmpty(cut.FindAll("[data-testid='training-entry-done']"));
        Assert.Empty(cut.FindAll("[data-testid='training-entry-start']"));
    }

    [Fact]
    public void The_routine_card_explains_that_the_first_exercise_counts_the_day()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(DispatchProxy.Create<IAttendanceClient, FakeClient>());

        var cut = RenderComponent<TrainingEntryCard>();

        Assert.Contains("Tu día cuenta cuando registres tu primer ejercicio", cut.Find("[data-testid='training-entry-first-log']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='training-entry-start']"));
    }

    [Fact]
    public void With_door_registration_the_home_never_offers_it()
    {
        var cut = RenderHome(routineEntry: false);

        Assert.Empty(cut.FindAll("[data-testid='training-entry']"));
        Assert.Empty(cut.FindAll("[data-testid='training-entry-done']"));
    }

    [Theory]
    [InlineData("Qr", "QR")]
    [InlineData("Document", "Documento")]
    [InlineData("Routine", "Rutina")]
    [InlineData("Manual", "Manual")]
    public void Reception_reads_how_each_entry_was_registered(string method, string label)
        => Assert.Equal(label, AttendanceMethodLabels.Of(method));

    [Fact]
    public void The_home_shows_how_full_the_gym_is_unless_the_front_desk_option_is_off()
    {
        var on = RenderHome(routineEntry: false);
        Assert.NotEmpty(on.FindAll("[data-testid='member-occupancy']"));
    }

    [Fact]
    public void With_front_desk_off_the_home_does_not_show_how_full_the_gym_is()
    {
        var off = RenderHome(routineEntry: false, showFrontDesk: false);
        Assert.Empty(off.FindAll("[data-testid='member-occupancy']"));
    }
}
