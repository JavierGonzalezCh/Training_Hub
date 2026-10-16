using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI.Pages;
using GSM.Panel.UI.Pages.Classes;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// C4: la lista de espera en <c>/app/clases</c> (anotarse, ver el lugar, confirmar o rechazar la oferta), el roster del
/// staff con la lista en su orden y el ajuste del tiempo para confirmar.
/// Los nombres del cliente (<c>IMemberWaitlistClient.JoinAsync/ConfirmAsync/LeaveAsync</c>) son los que NSwag debería generar.
/// </summary>
public class ClassWaitlistUiTests : TestContext
{
    private static readonly Guid ClassId = Guid.NewGuid();

    private IRenderedComponent<GSM.Member.UI.Pages.Classes> RenderMemberPage(ScheduleEntry entry, out FakeClient waitlistFake)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<GymContext>();

        var me = DispatchProxy.Create<IMeClient, FakeClient>();
        ((FakeClient)(object)me).Handlers["ClassesGET2Async"] = _ => new ScheduleResponse
        {
            From = entry.SessionDate, To = entry.SessionDate, Entries = [entry],
        };
        Services.AddSingleton(me);
        Services.AddSingleton(DispatchProxy.Create<IMemberClassesClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMeClassBookingStatusClient, FakeClient>());

        var waitlist = DispatchProxy.Create<IMemberWaitlistClient, FakeClient>();
        waitlistFake = (FakeClient)(object)waitlist;
        waitlistFake.Handlers["JoinAsync"] = _ => new WaitlistResponse { Status = "Waiting", Position = 2 };
        waitlistFake.Handlers["ConfirmAsync"] = _ => new BookingResponse { Status = "Booked" };
        Services.AddSingleton(waitlist);

        // Tras anotarse, la página ofrece activar los avisos: un dispositivo que ya los tiene no muestra nada.
        Services.AddSingleton<GSM.UI.Shared.Push.IPushNotifications>(new GrantedPushDevice());
        Services.AddSingleton(DispatchProxy.Create<IPushClient, FakeClient>());
        Services.AddScoped<GSM.UI.Shared.Push.PushRegistrationService>();
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<GSM.Member.UI.Pages.Classes>();
    }

    private static ScheduleEntry Entry(Action<ScheduleEntry>? configure = null)
    {
        var entry = new ScheduleEntry
        {
            ClassId = ClassId, ClassName = "Spinning", SessionDate = GsmFormat.Today(), StartTime = new TimeOnly(18, 0),
            DurationMinutes = 45, Capacity = 1, Status = "Scheduled", IsMaterialized = true, SessionId = Guid.NewGuid(),
            IsFull = true,
        };
        configure?.Invoke(entry);
        return entry;
    }

    [Fact]
    public void A_full_session_offers_to_join_the_waitlist_and_calls_the_api()
    {
        var page = RenderMemberPage(Entry(), out var fake);

        page.WaitForAssertion(() => Assert.Contains("Anotarme en espera", page.Markup));
        Assert.DoesNotContain("Reservar", page.Markup);

        page.Find($"#join-waitlist-{ClassId}").Click();

        page.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "JoinAsync");
            Assert.Equal(ClassId, call.Args[0]);
            Assert.Equal(GsmFormat.Today(), call.Args[1]);
        });
    }

    [Fact]
    public void A_session_with_room_still_offers_to_book()
    {
        var page = RenderMemberPage(Entry(e => e.IsFull = false), out _);

        page.WaitForAssertion(() => Assert.Contains("Reservar", page.Markup));
        Assert.DoesNotContain("Anotarme en espera", page.Markup);
    }

    [Fact]
    public void Someone_waiting_sees_their_place_in_line_and_can_leave()
    {
        var page = RenderMemberPage(Entry(e => { e.MyWaitlistStatus = "Waiting"; e.MyWaitlistPosition = 2; }), out var fake);

        page.WaitForAssertion(() => Assert.Contains("Eres el 2", page.Find(".waitlist-position-chip").TextContent));

        page.Find($"#leave-waitlist-{ClassId}").Click();

        page.WaitForAssertion(() => Assert.Single(fake.Calls, c => c.Method == "LeaveAsync"));
    }

    [Fact]
    public void An_offer_shows_the_deadline_and_can_be_confirmed()
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(45);
        var page = RenderMemberPage(Entry(e => { e.MyWaitlistStatus = "Offered"; e.MyWaitlistPosition = 1; e.MyOfferExpiresAt = deadline; }), out var fake);

        page.WaitForAssertion(() =>
            Assert.Contains($"Confirma antes de las {GsmFormat.Time(deadline)}", page.Find(".waitlist-offer-text").TextContent));
        Assert.DoesNotContain("Anotarme en espera", page.Markup);

        page.Find($"#confirm-offer-{ClassId}").Click();

        page.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "ConfirmAsync");
            Assert.Equal(ClassId, call.Args[0]);
        });
    }

    [Fact]
    public void An_offer_can_be_declined_which_leaves_the_list()
    {
        var page = RenderMemberPage(
            Entry(e => { e.MyWaitlistStatus = "Offered"; e.MyWaitlistPosition = 1; e.MyOfferExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30); }),
            out var fake);

        page.WaitForAssertion(() => Assert.NotNull(page.Find($"#decline-offer-{ClassId}")));
        page.Find($"#decline-offer-{ClassId}").Click();

        page.WaitForAssertion(() => Assert.Single(fake.Calls, c => c.Method == "LeaveAsync"));
    }

    [Fact]
    public void The_staff_roster_lists_the_waitlist_in_order_and_marks_who_has_an_offer()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var classes = DispatchProxy.Create<IClassesClient, FakeClient>();
        ((FakeClient)(object)classes).Handlers["RosterAsync"] = _ => new RosterResponse
        {
            ClassId = ClassId, ClassName = "Spinning", SessionDate = GsmFormat.Today(), StartTime = new TimeOnly(18, 0),
            Capacity = 1, SessionStatus = "Scheduled", BookedCount = 1,
            Entries = [new RosterEntry { BookingId = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Ana Pérez", Status = "Booked" }],
            Waitlist =
            [
                new WaitlistRosterEntry { EntryId = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Luis Gómez", Position = 1, Status = "Offered", OfferExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) },
                new WaitlistRosterEntry { EntryId = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Sara Ruiz", Position = 2, Status = "Waiting" },
            ],
        };
        Services.AddSingleton(classes);
        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        provider.InvokeAsync(() => dialogs.ShowAsync<ClassRosterDialog>("Roster", new DialogParameters<ClassRosterDialog>
        {
            { x => x.ClassId, ClassId },
            { x => x.SessionDate, GsmFormat.Today() },
        }));

        provider.WaitForAssertion(() =>
        {
            Assert.Contains("Lista de espera (2)", provider.Find("#roster-waitlist-title").TextContent);
            var rows = provider.FindAll("#roster-waitlist tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Luis Gómez", rows[0].TextContent);
            Assert.Contains("Con oferta", rows[0].TextContent);
            Assert.Contains("Sara Ruiz", rows[1].TextContent);
            Assert.Contains("Esperando", rows[1].TextContent);
        });
    }

    [Fact]
    public void The_roster_without_a_waitlist_shows_no_waitlist_section()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var classes = DispatchProxy.Create<IClassesClient, FakeClient>();
        ((FakeClient)(object)classes).Handlers["RosterAsync"] = _ => new RosterResponse
        {
            ClassId = ClassId, ClassName = "Spinning", SessionDate = GsmFormat.Today(), StartTime = new TimeOnly(18, 0),
            Capacity = 5, SessionStatus = "Scheduled", BookedCount = 0, Entries = [], Waitlist = [],
        };
        Services.AddSingleton(classes);
        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        provider.InvokeAsync(() => dialogs.ShowAsync<ClassRosterDialog>("Roster", new DialogParameters<ClassRosterDialog>
        {
            { x => x.ClassId, ClassId },
            { x => x.SessionDate, GsmFormat.Today() },
        }));

        provider.WaitForAssertion(() => Assert.Contains("Sin reservas", provider.Markup));
        Assert.DoesNotContain("Lista de espera", provider.Markup);
    }

    [Fact]
    public void The_rules_tab_edits_the_minutes_to_confirm_an_offer()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IClassSettingsClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassSettingsGETAsync"] = _ => new ClassSettingsResponse
        {
            CancelDeadlineHours = 2, NoShowLimit = 3, NoShowWindowDays = 30, NoShowBlockDays = 7,
            BookingWindowDays = 7, MaxActiveBookings = 5, WaitlistOfferMinutes = 45,
        };
        fake.Handlers["ClassSettingsPUTAsync"] = args =>
        {
            var r = (UpdateClassSettingsRequest)args[0]!;
            return new ClassSettingsResponse
            {
                CancelDeadlineHours = r.CancelDeadlineHours, NoShowLimit = r.NoShowLimit, NoShowWindowDays = r.NoShowWindowDays,
                NoShowBlockDays = r.NoShowBlockDays, BookingWindowDays = r.BookingWindowDays, MaxActiveBookings = r.MaxActiveBookings,
                WaitlistOfferMinutes = r.WaitlistOfferMinutes ?? 0,
            };
        };
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();

        var tab = RenderComponent<ClassRulesTab>();

        tab.WaitForAssertion(() => Assert.Equal("45", tab.Find("input#rule-waitlist-minutes").GetAttribute("value")));

        tab.Find("input#rule-waitlist-minutes").Change("30");
        tab.Find("#rules-save").Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(fake.Calls, c => c.Method == "ClassSettingsPUTAsync");
            Assert.Equal(30, Assert.IsType<UpdateClassSettingsRequest>(put.Args[0]).WaitlistOfferMinutes);
        });
    }

    internal sealed class GrantedPushDevice : GSM.UI.Shared.Push.IPushNotifications
    {
        public Task<GSM.UI.Shared.Push.PushPermissionState> GetStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(GSM.UI.Shared.Push.PushPermissionState.Granted);

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string?> RequestTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> GetTokenAsync(PushConfigResponse config, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task ForgetTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsPromptDismissedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DismissPromptAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
