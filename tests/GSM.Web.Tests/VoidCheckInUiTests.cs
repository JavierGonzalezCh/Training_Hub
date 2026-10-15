using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>A2: la acción "Anular entrada" solo aparece con el permiso ManageAttendance y pide un motivo.</summary>
public class VoidCheckInUiTests : TestContext
{
    private readonly FakeClient _fake;
    private readonly TestAuthorizationContext _auth;

    public VoidCheckInUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        _fake = (FakeClient)(object)proxy;
        Services.AddSingleton(proxy);
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("luis");
        _auth.SetRoles("staff");
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
    }

    private static AttendanceListItem Entry(bool voided = false) => new()
    {
        Id = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Ana Prueba", CheckedInAt = DateTimeOffset.UtcNow,
        AttendanceDate = DateOnly.FromDateTime(DateTime.Today), Method = "Manual", Voided = voided,
        VoidReason = voided ? "Error de recepción" : null, VoidedByName = voided ? "Luis" : null,
    };

    [Fact]
    public void Staff_without_the_permission_does_not_see_the_void_button()
    {
        var cut = RenderComponent<CheckInVoidAction>(p => p.Add(x => x.Entry, Entry()));

        Assert.Empty(cut.FindAll("[data-testid='void-checkin']"));
    }

    [Fact]
    public void Staff_with_ManageAttendance_sees_the_void_button()
    {
        _auth.SetPolicies(GsmPolicies.ForPermission(GsmPermissions.ManageAttendance));

        var cut = RenderComponent<CheckInVoidAction>(p => p.Add(x => x.Entry, Entry()));

        Assert.Single(cut.FindAll("[data-testid='void-checkin']"));
    }

    [Fact]
    public void A_voided_entry_shows_the_badge_and_no_button()
    {
        _auth.SetPolicies(GsmPolicies.ForPermission(GsmPermissions.ManageAttendance));

        var cut = RenderComponent<CheckInVoidAction>(p => p.Add(x => x.Entry, Entry(voided: true)));

        Assert.Single(cut.FindAll("[data-testid='checkin-voided']"));
        Assert.Empty(cut.FindAll("[data-testid='void-checkin']"));
    }

    [Fact]
    public void The_dialog_requires_a_reason_and_then_sends_the_void()
    {
        _auth.SetPolicies(GsmPolicies.ForPermission(GsmPermissions.ManageAttendance));
        var entry = Entry();
        var cut = RenderComponent<CheckInVoidAction>(p => p.Add(x => x.Entry, entry));

        cut.Find("[data-testid='void-checkin']").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='confirm-void-checkin']")));

        // Sin motivo no llama al servidor.
        cut.Find("[data-testid='confirm-void-checkin']").Click();
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "VoidCheckInAsync");

        cut.Find("textarea").Change("Registré al miembro equivocado");
        cut.Find("[data-testid='confirm-void-checkin']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = _fake.Calls.Last(c => c.Method == "VoidCheckInAsync");
            Assert.Equal(entry.Id, (Guid)call.Args[0]!);
            Assert.Equal("Registré al miembro equivocado", ((VoidCheckInRequest)call.Args[1]!).Reason);
        });
    }
}
