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
/// C7: "Estoy en clase" en <c>/app/clases</c>. El nombre del cliente (<c>IMemberClassAttendanceClient.AttendAsync</c>)
/// es el que NSwag debería generar. El botón solo aparece el día de la clase, dentro de la ventana de ±15 minutos.
/// </summary>
public class ClassSelfAttendanceUiTests : TestContext
{
    private static readonly Guid ClassId = Guid.NewGuid();

    private IRenderedComponent<GSM.Member.UI.Pages.Classes> RenderPage(ScheduleEntry entry, out FakeClient attendFake)
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
        Services.AddSingleton(DispatchProxy.Create<IMemberWaitlistClient, FakeClient>());

        var attend = DispatchProxy.Create<IMemberClassAttendanceClient, FakeClient>();
        attendFake = (FakeClient)(object)attend;
        attendFake.Handlers["AttendAsync"] = _ => new SelfAttendanceResponse { Status = "Attended" };
        Services.AddSingleton(attend);

        Services.AddSingleton<GSM.UI.Shared.Push.IPushNotifications>(new GrantedPushDevice());
        Services.AddSingleton(DispatchProxy.Create<IPushClient, FakeClient>());
        Services.AddScoped<GSM.UI.Shared.Push.PushRegistrationService>();
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<GSM.Member.UI.Pages.Classes>();
    }

    /// <summary>Una clase que empieza ahora mismo (hora del gym), para estar dentro de la ventana.</summary>
    private static ScheduleEntry Entry(Action<ScheduleEntry>? configure = null)
    {
        var entry = new ScheduleEntry
        {
            ClassId = ClassId, ClassName = "Spinning", SessionDate = GsmFormat.Today(),
            StartTime = TimeOnly.ParseExact(GsmFormat.Time(DateTimeOffset.UtcNow), "HH:mm"),
            DurationMinutes = 45, Capacity = 10, Status = "Scheduled", IsMaterialized = true, SessionId = Guid.NewGuid(),
            MyBookingStatus = "Booked",
        };
        configure?.Invoke(entry);
        return entry;
    }

    [Fact]
    public void A_booked_member_sees_Estoy_en_clase_when_the_class_is_starting_and_it_calls_the_api()
    {
        var page = RenderPage(Entry(), out var fake);

        page.WaitForAssertion(() => Assert.Contains("Estoy en clase", page.Markup));

        page.Find($"#attend-{ClassId}").Click();

        page.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "AttendAsync");
            Assert.Equal(ClassId, call.Args[0]);
            Assert.Equal(GsmFormat.Today(), call.Args[1]);
        });
    }

    [Fact]
    public void The_button_is_hidden_when_the_class_is_far_from_now()
    {
        var farAway = TimeOnly.ParseExact(GsmFormat.Time(DateTimeOffset.UtcNow), "HH:mm").AddHours(3);
        var page = RenderPage(Entry(e => e.StartTime = farAway), out _);

        page.WaitForAssertion(() => Assert.Contains("Spinning", page.Markup));
        Assert.DoesNotContain("Estoy en clase", page.Markup);
    }

    [Fact]
    public void A_member_without_a_booking_in_a_capacity_class_does_not_see_the_button()
    {
        var page = RenderPage(Entry(e => e.MyBookingStatus = null), out _);

        page.WaitForAssertion(() => Assert.Contains("Spinning", page.Markup));
        Assert.DoesNotContain("Estoy en clase", page.Markup);
    }

    [Fact]
    public void A_free_entry_class_offers_the_button_without_a_booking()
    {
        var page = RenderPage(Entry(e => { e.Capacity = null; e.MyBookingStatus = null; }), out _);

        page.WaitForAssertion(() => Assert.Contains("Estoy en clase", page.Markup));
    }

    [Fact]
    public void Once_attended_the_card_shows_Asististe_instead_of_the_button()
    {
        var page = RenderPage(Entry(e => e.MyBookingStatus = "Attended"), out _);

        page.WaitForAssertion(() => Assert.Contains("ASISTISTE", page.Find(".attended-chip").TextContent));
        Assert.DoesNotContain("Estoy en clase", page.Markup);
    }
}
