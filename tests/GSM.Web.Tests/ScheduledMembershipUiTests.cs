using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>MS1b — membresía programada: la etiqueta del estado y el aviso del miembro en <c>/app</c>.</summary>
public class ScheduledMembershipUiTests : TestContext
{
    private sealed class NoScanner : IQrScanner
    {
        public bool IsSupported => false;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void The_scheduled_status_has_a_label_and_is_a_list_filter()
    {
        Assert.Equal("Programada", CommercialStatusLabels.Label("scheduled"));
        Assert.Contains(CommercialStatusLabels.Filters, f => f.Value == "scheduled");
    }

    [Fact]
    public void The_member_home_says_when_the_plan_starts()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<GSM.UI.Shared.Components.ViewportState>();

        var me = DispatchProxy.Create<IMeClient, FakeClient>();
        var attendance = DispatchProxy.Create<IMemberAttendanceClient, FakeClient>();
        ((FakeClient)(object)attendance).Handlers["AttendanceGET2Async"] = _ => new PagedResultOfAttendanceListItem();
        var consents = DispatchProxy.Create<IMeConsentsClient, FakeClient>();
        ((FakeClient)(object)consents).Handlers["ListMyConsentsAsync"] = _ => new List<MemberConsentStatus>();
        Services.AddSingleton(me);
        Services.AddSingleton(attendance);
        Services.AddSingleton(consents);
        Services.AddSingleton<IQrScanner>(new NoScanner());

        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowRoutines = false, CheckinQr = false } }, false);
        Services.AddSingleton(gym);

        var startsOn = new DateOnly(2026, 11, 3);
        var context = new MemberContext();
        context.Set(new MemberDetail
        {
            Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active",
            CommercialStatus = "scheduled", AccessStartsOn = startsOn,
        });
        Services.AddSingleton(context);

        var cut = RenderComponent<Home>();

        var text = cut.Find("#plan-scheduled").TextContent;
        Assert.Contains("Tu plan empieza el", text);
        Assert.Contains(GsmFormat.Day(startsOn), text);
        Assert.DoesNotContain("Sin membresía vigente", cut.Markup);
    }
}
