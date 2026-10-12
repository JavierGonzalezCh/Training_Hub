using System.Security.Claims;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Navigation;
using GSM.Panel.UI.Navigation.Modules;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>I7: pestaña "Kioscos" de la configuración (cuentas de dispositivo).</summary>
public class KiosksTabTests : TestContext
{
    private sealed class FakeKiosksClient : IKiosksClient
    {
        public List<KioskDeviceResponse> Kiosks { get; } = [];
        public List<Guid> Revoked { get; } = [];
        public List<string> Created { get; } = [];

        public Task<ICollection<KioskDeviceResponse>> KiosksAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ICollection<KioskDeviceResponse>>(Kiosks.ToList());

        public Task<KioskCreatedResponse> KiosksPOSTAsync(CreateKioskRequest body, CancellationToken cancellationToken = default)
        {
            Created.Add(body.Name);
            var device = new KioskDeviceResponse
            {
                Id = Guid.NewGuid(), Name = body.Name, Status = "pending", CreatedAt = DateTimeOffset.UtcNow,
                PairingCodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            };
            Kiosks.Add(device);
            return Task.FromResult(new KioskCreatedResponse
            {
                Device = device, PairingCode = "K7M2QX9P", ExpiresAt = device.PairingCodeExpiresAt!.Value,
            });
        }

        public Task<KioskDeviceResponse> KiosksDELETEAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Revoked.Add(id);
            var index = Kiosks.FindIndex(k => k.Id == id);
            Kiosks[index].Status = "revoked";
            return Task.FromResult(Kiosks[index]);
        }
    }

    private FakeKiosksClient Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var api = new FakeKiosksClient();
        Services.AddSingleton<IKiosksClient>(api);
        return api;
    }

    private static KioskDeviceResponse Paired(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Status = "paired", CreatedAt = DateTimeOffset.UtcNow,
        PairedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Lists_the_kiosks_with_their_status()
    {
        var api = Setup();
        api.Kiosks.Add(Paired("Entrada principal"));
        api.Kiosks.Add(new KioskDeviceResponse { Id = Guid.NewGuid(), Name = "Piscina", Status = "revoked", CreatedAt = DateTimeOffset.UtcNow });

        var cut = RenderComponent<KiosksTab>();

        Assert.Contains("Entrada principal", cut.Markup);
        Assert.Contains("EMPAREJADO", cut.Markup);
        Assert.Contains("REVOCADO", cut.Markup);
        // Solo el que sigue activo ofrece "Revocar".
        Assert.Single(cut.FindAll("button.mud-button-text").Where(b => b.TextContent.Trim() == "Revocar"));
    }

    [Fact]
    public void Adding_a_kiosk_shows_the_pairing_code_once()
    {
        var api = Setup();
        var cut = RenderComponent<KiosksTab>();
        Assert.Contains("Todavía no hay kioscos", cut.Markup);
        Assert.Empty(cut.FindAll("#pairing-code"));

        cut.Find("input").Change("Entrada principal");
        cut.FindAll("button").First(b => b.TextContent.Contains("Agregar kiosco")).Click();

        cut.WaitForAssertion(() => Assert.Equal("K7M2QX9P", cut.Find("#pairing-code").TextContent));
        Assert.Equal(["Entrada principal"], api.Created);
        Assert.Contains("ESPERANDO CÓDIGO", cut.Markup);
    }

    [Fact]
    public void A_blank_name_is_not_sent()
    {
        var api = Setup();
        var cut = RenderComponent<KiosksTab>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Agregar kiosco")).Click();

        Assert.Empty(api.Created);
    }

    [Fact]
    public void The_tab_is_declared_by_Identity_for_the_owner_only()
    {
        var section = new IdentityPanelModule().SettingsSections.Single(s => s.Title == "Kioscos");
        var owner = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "owner")], "test"));
        var staff = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "staff")], "test"));

        Assert.Equal(typeof(KiosksTab), section.Component);
        Assert.True(section.IsVisibleTo(owner));
        Assert.False(section.IsVisibleTo(staff));
    }
}
