using Bunit;
using GSM.Client.Generated;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class GymAccessPickerTests : TestContext
{
    private static GymAccessOption Access(string gym, string role, bool current = false, string? logo = null) => new()
    {
        AccessId = Guid.NewGuid(),
        GymId = Guid.NewGuid(),
        GymName = gym,
        Role = role,
        LogoUrl = logo,
        IsCurrent = current,
    };

    private IRenderedComponent<GymAccessPicker> Render(
        IReadOnlyList<GymAccessOption> accesses, Action<GymAccessOption>? onSelect = null, bool busy = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        return RenderComponent<GymAccessPicker>(p => p
            .Add(x => x.Accesses, accesses)
            .Add(x => x.Busy, busy)
            .Add(x => x.OnSelect, EventCallback.Factory.Create<GymAccessOption>(this, a => onSelect?.Invoke(a))));
    }

    [Fact]
    public void Each_access_is_listed_separately_with_its_gym_and_role_in_the_given_order()
    {
        var cut = Render([Access("Gimnasio Demo", "staff"), Access("Gimnasio Demo", "member"), Access("Otro Gym", "owner")]);

        var items = cut.FindAll("[data-testid='gym-access']");

        Assert.Equal(3, items.Count);
        Assert.Contains("Gimnasio Demo", items[0].TextContent);
        Assert.Contains("Staff", items[0].TextContent);
        Assert.Contains("Miembro", items[1].TextContent);
        Assert.Contains("Propietario", items[2].TextContent);
        Assert.Contains("Otro Gym", items[2].TextContent);
    }

    [Fact]
    public void Clicking_an_access_reports_it()
    {
        var first = Access("Gym Uno", "staff");
        var second = Access("Gym Dos", "member");
        GymAccessOption? picked = null;
        var cut = Render([first, second], a => picked = a);

        cut.FindAll("[data-testid='gym-access']")[1].Click();

        Assert.Equal(second.AccessId, picked!.AccessId);
    }

    [Fact]
    public void The_current_access_is_marked_and_cannot_be_chosen()
    {
        var cut = Render([Access("Gym Uno", "staff", current: true), Access("Gym Dos", "staff")]);

        var items = cut.FindAll("[data-testid='gym-access']");

        Assert.True(items[0].HasAttribute("disabled"));
        Assert.Contains("actual", items[0].TextContent);
        Assert.False(items[1].HasAttribute("disabled"));
    }

    [Fact]
    public void While_busy_no_access_can_be_clicked()
    {
        var cut = Render([Access("Gym Uno", "staff"), Access("Gym Dos", "member")], busy: true);

        Assert.All(cut.FindAll("[data-testid='gym-access']"), i => Assert.True(i.HasAttribute("disabled")));
    }

    [Fact]
    public void A_gym_without_logo_shows_its_initial()
    {
        var cut = Render([Access("Zona Fit", "staff")]);

        Assert.Contains("Z", cut.Find(".gsm-avatar").TextContent);
        Assert.Empty(cut.FindAll("img"));
    }

    [Theory]
    [InlineData("owner", "Propietario")]
    [InlineData("staff", "Staff")]
    [InlineData("member", "Miembro")]
    public void Role_labels(string role, string label)
        => Assert.Equal($"Gimnasio Demo · {label}", GymAccessLabels.Full("Gimnasio Demo", role));
}
