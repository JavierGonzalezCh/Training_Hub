using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>AP6 — <c>/app</c> en pantalla grande: dos columnas en Inicio y la entrada se registra desde el celular.</summary>
public class MemberDesktopUiTests : TestContext
{
    private sealed class CameraScanner : IQrScanner
    {
        public bool IsSupported => true;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private IRenderedComponent<Home> RenderHome(bool narrow)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();
        Services.GetRequiredService<ViewportState>().Pin(narrow);

        var attendance = DispatchProxy.Create<IMemberAttendanceClient, FakeClient>();
        ((FakeClient)(object)attendance).Handlers["AttendanceGET2Async"] = _ => new PagedResultOfAttendanceListItem();
        var consents = DispatchProxy.Create<IMeConsentsClient, FakeClient>();
        ((FakeClient)(object)consents).Handlers["ListMyConsentsAsync"] = _ => new List<MemberConsentStatus>();
        Services.AddSingleton(DispatchProxy.Create<IMeClient, FakeClient>());
        Services.AddSingleton(attendance);
        Services.AddSingleton(consents);
        Services.AddSingleton<IQrScanner>(new CameraScanner());

        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowRoutines = false, CheckinQr = true } }, false);
        Services.AddSingleton(gym);

        var context = new MemberContext();
        context.Set(new MemberDetail { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active" });
        Services.AddSingleton(context);

        return RenderComponent<Home>();
    }

    [Fact]
    public void Desktop_home_has_two_columns_and_points_the_checkin_to_the_phone()
    {
        var cut = RenderHome(narrow: false);

        Assert.NotNull(cut.Find(".gsm-m-home .gsm-m-home__main"));
        Assert.NotNull(cut.Find(".gsm-m-home .gsm-m-home__side"));
        Assert.Contains("Registra tu entrada desde tu celular o en recepción", cut.Find("#checkin-desktop-hint").TextContent);
        Assert.Empty(cut.FindAll(".gsm-m-checkin button"));
    }

    [Fact]
    public void Phone_home_keeps_the_scanner_button()
    {
        var cut = RenderHome(narrow: true);

        Assert.Empty(cut.FindAll("#checkin-desktop-hint"));
        Assert.Contains("Escanea el QR", cut.Find(".gsm-m-checkin").TextContent);
        Assert.NotEmpty(cut.FindAll(".gsm-m-checkin button"));
    }
}
