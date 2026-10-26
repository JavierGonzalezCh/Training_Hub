using Microsoft.AspNetCore.Components;
﻿using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Pages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>A6 — el check-in del miembro pide la ubicación solo cuando el servidor lo indica y avisa si no suma.</summary>
public class GeofenceCheckinUiTests : TestContext
{
    private sealed class FakeLocation(DeviceLocation? location, bool supported = true) : IDeviceLocation
    {
        public int Calls { get; private set; }

        public bool IsSupported => supported;

        public Task<DeviceLocation?> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(location);
        }
    }

    private (FakeClient Fake, FakeLocation Location) Setup(bool locationRequired, DeviceLocation? location, CheckInResponse response)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles(GsmRoles.Member);

        var attendance = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        var fake = (FakeClient)(object)attendance;
        fake.Handlers["GetCheckinLocationPolicyAsync"] = _ => new CheckinLocationPolicyResponse { LocationRequired = locationRequired };
        fake.Handlers["CheckinAsync"] = _ => response;
        Services.AddSingleton(attendance);

        var device = new FakeLocation(location);
        Services.AddSingleton<IDeviceLocation>(device);
        return (fake, device);
    }

    private static CheckInResponse Response(string? notice = null, bool counts = true) => new()
    {
        MemberName = "Ana Prueba",
        CheckedInAt = DateTimeOffset.UtcNow,
        UnlockedAchievements = [],
        LocationNotice = notice,
        CountsForProgress = counts,
    };

    private static CheckInRequest SentRequest(FakeClient fake)
        => (CheckInRequest)fake.Calls.Single(c => c.Method == "CheckinAsync").Args[0]!;

    [Fact]
    public void With_a_static_code_and_a_geofence_the_page_sends_the_phone_location()
    {
        var (fake, device) = Setup(true, new DeviceLocation(4.5981, -74.0760), Response());

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("t", "s1.abc"));
        var cut = RenderComponent<CheckIn>();

        cut.WaitForAssertion(() => Assert.Equal(1, device.Calls));
        var request = SentRequest(fake);
        Assert.Equal(4.5981, request.Latitude);
        Assert.Equal(-74.0760, request.Longitude);
        Assert.Empty(cut.FindAll("#checkin-location-notice"));
    }

    [Fact]
    public void A_gym_with_qr_check_in_turned_off_sends_the_member_to_reception()
    {
        var (fake, _) = Setup(false, null, Response());
        fake.Handlers["CheckinAsync"] = _ => throw new GsmApiException(
            "Forbidden", 403, "{\"detail\":\"Tu gimnasio no tiene activado el check-in con QR.\"}",
            new Dictionary<string, IEnumerable<string>>(), null);

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("t", "s1.abc"));
        var cut = RenderComponent<CheckIn>();

        cut.WaitForAssertion(() => Assert.Contains("Check-in con QR desactivado", cut.Find("h1").TextContent));
        Assert.Contains("recepción", cut.Find(".gsm-checkin-result__detail").TextContent);
    }

    [Fact]
    public void Without_a_geofence_the_page_never_asks_for_the_location()
    {
        var (fake, device) = Setup(false, new DeviceLocation(1, 1), Response());

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("t", "r1.abc"));
        var cut = RenderComponent<CheckIn>();

        cut.WaitForAssertion(() => Assert.Contains(fake.Calls, c => c.Method == "CheckinAsync"));
        Assert.Equal(0, device.Calls);
        var request = SentRequest(fake);
        Assert.Null(request.Latitude);
        Assert.Null(request.Longitude);
    }

    [Fact]
    public void If_the_member_denies_the_location_the_entry_is_sent_without_it_and_the_notice_is_shown()
    {
        const string notice = "Registramos tu entrada, pero sin tu ubicación no suma a tu racha ni a tus logros.";
        var (fake, _) = Setup(true, null, Response(notice, counts: false));

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("t", "s1.abc"));
        var cut = RenderComponent<CheckIn>();

        cut.WaitForAssertion(() => Assert.Contains(notice, cut.Find("#checkin-location-notice").TextContent));
        var request = SentRequest(fake);
        Assert.Null(request.Latitude);
        Assert.Null(request.Longitude);
    }
}
