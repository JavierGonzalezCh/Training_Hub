using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Member.UI;
using GSM.Member.UI.Auth;
using GSM.Member.UI.Pages;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// La entrada con el QR vive en el encabezado de toda la app (<see cref="CheckinButton"/>), no en Hoy; Hoy tiene dos
/// columnas en escritorio.
/// </summary>
public class MemberDesktopUiTests : TestContext
{
    private sealed class CameraScanner(string? code) : IQrScanner
    {
        public bool IsSupported => true;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult(code);
    }

    private sealed class NoScanner : IQrScanner
    {
        public bool IsSupported => false;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private void Register(IQrScanner scanner, bool checkinQr = true, bool narrow = true)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddScoped<ViewportState>();

        var consents = DispatchProxy.Create<IMeConsentsClient, FakeClient>();
        ((FakeClient)(object)consents).Handlers["ListMyConsentsAsync"] = _ => new List<MemberConsentStatus>();
        Services.AddSingleton(DispatchProxy.Create<IMeClient, FakeClient>());
        Services.AddSingleton(consents);
        Services.AddSingleton(scanner);

        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowRoutines = false, CheckinQr = checkinQr } }, false);
        Services.AddSingleton(gym);

        var context = new MemberContext();
        context.Set(new MemberDetail { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", Status = "Active" });
        Services.AddSingleton(context);

        Services.GetRequiredService<ViewportState>().Pin(narrow);
    }

    [Fact]
    public void Desktop_home_has_two_columns_and_no_scan_card()
    {
        Register(new NoScanner(), narrow: false);

        var cut = RenderComponent<Home>();

        Assert.NotNull(cut.Find(".gsm-m-home .gsm-m-home__main"));
        Assert.NotNull(cut.Find(".gsm-m-home .gsm-m-home__side"));
        Assert.Empty(cut.FindAll(".gsm-m-checkin"));
    }

    [Fact]
    public void The_header_button_scans_and_goes_straight_to_the_checkin()
    {
        Register(new CameraScanner("https://app.traininghub.co/app/checkin?t=ABC123"));

        var cut = RenderComponent<CheckinButton>();
        cut.Find("[data-testid='header-checkin']").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.Contains("/app/checkin?t=ABC123", nav.Uri));
    }

    [Fact]
    public void Without_a_camera_the_header_button_explains_how_to_scan()
    {
        Register(new NoScanner());
        var provider = RenderComponent<MudDialogProvider>();

        var cut = RenderComponent<CheckinButton>();
        cut.Find("[data-testid='header-checkin']").Click();

        provider.WaitForAssertion(() => Assert.Contains("apunta al QR de la entrada", provider.Markup));
    }

    [Fact]
    public void The_header_button_is_not_there_when_the_gym_does_not_use_the_qr()
    {
        Register(new NoScanner(), checkinQr: false);

        var cut = RenderComponent<CheckinButton>();

        Assert.Empty(cut.FindAll("[data-testid='header-checkin']"));
    }
}
