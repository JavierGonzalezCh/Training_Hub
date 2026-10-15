using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>L1: al crear un logro el Owner ve cuántos miembros ya lo cumplen y decide si se les otorga ahora.</summary>
public class AchievementRetroactiveUiTests : TestContext
{
    private readonly FakeClient _fake;
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public AchievementRetroactiveUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAchievementsClient, FakeClient>();
        _fake = (FakeClient)(object)proxy;
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();
        _dialogs = RenderComponent<MudDialogProvider>();
    }

    private async Task OpenCreateAsync(int eligible)
    {
        _fake.Handlers["RetroactivePreviewAsync"] = _ => new RetroactivePreviewResponse { EligibleCount = eligible };
        _fake.Handlers["AchievementsPOSTAsync"] = _ => new AchievementResponse { Id = Guid.NewGuid(), Name = "Nuevo" };
        _fake.Handlers["GrantRetroactiveAsync"] = _ => new RetroactiveGrantResponse { GrantedCount = eligible };

        var service = Services.GetRequiredService<IDialogService>();
        await _dialogs.InvokeAsync(() => service.ShowAsync<AchievementFormDialog>("Nuevo logro"));
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("input")));
        _dialogs.Find("input").Change("Veterano");
        _dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();
    }

    [Fact]
    public async Task Creating_shows_how_many_members_already_qualify_and_grants_on_confirm()
    {
        await OpenCreateAsync(23);

        _dialogs.WaitForAssertion(() => Assert.Contains("23 miembro(s) ya cumplen este logro", _dialogs.Markup));
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "AchievementsPOSTAsync");

        _dialogs.FindAll("button").First(b => b.TextContent.Contains("Guardar y otorgar")).Click();

        _dialogs.WaitForAssertion(() =>
        {
            Assert.Contains(_fake.Calls, c => c.Method == "AchievementsPOSTAsync");
            Assert.Contains(_fake.Calls, c => c.Method == "GrantRetroactiveAsync");
        });
    }

    [Fact]
    public async Task Saving_without_granting_does_not_call_the_grant()
    {
        await OpenCreateAsync(5);

        _dialogs.WaitForAssertion(() => Assert.Contains("5 miembro(s) ya cumplen este logro", _dialogs.Markup));
        _dialogs.FindAll("button").First(b => b.TextContent.Contains("Guardar sin otorgar")).Click();

        _dialogs.WaitForAssertion(() => Assert.Contains(_fake.Calls, c => c.Method == "AchievementsPOSTAsync"));
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "GrantRetroactiveAsync");
    }

    [Fact]
    public async Task With_nobody_qualifying_it_saves_without_asking()
    {
        await OpenCreateAsync(0);

        _dialogs.WaitForAssertion(() => Assert.Contains(_fake.Calls, c => c.Method == "AchievementsPOSTAsync"));
        Assert.DoesNotContain("ya cumplen este logro", _dialogs.Markup);
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "GrantRetroactiveAsync");
    }
}
