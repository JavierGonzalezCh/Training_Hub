using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>UI de MS4: el diálogo de congelar muestra las reglas del plan, pide motivo y envía el congelamiento.</summary>
public class MembershipFreezeUiTests : TestContext
{
    private readonly Guid _memberId = Guid.NewGuid();
    private readonly MembershipResponse _membership = new()
    {
        Id = Guid.NewGuid(), PlanName = "Mensual", Status = "Active",
        StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 30),
    };

    private CommercialStatusUiTests.FakeClient _fake = null!;
    private IRenderedComponent<MudDialogProvider> _provider = null!;

    private async Task OpenAsync(int maxFreezeDays, bool isOwner)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var memberships = System.Reflection.DispatchProxy.Create<IMemberMembershipsClient, CommercialStatusUiTests.FakeClient>();
        _fake = (CommercialStatusUiTests.FakeClient)(object)memberships;
        _fake.Handlers["FreezesAsync"] = _ => new MembershipFreezesResponse
        {
            MaxFreezeDays = maxFreezeDays, MinFreezeDays = 3, DaysUsed = 4, DaysRemaining = Math.Max(0, maxFreezeDays - 4),
            Items = [],
        };
        _fake.Handlers["FreezeAsync"] = _ => _membership;
        Services.AddSingleton(memberships);

        RenderComponent<MudPopoverProvider>();
        _provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<FreezeMembershipDialog>
        {
            { x => x.MemberId, _memberId },
            { x => x.Membership, _membership },
            { x => x.IsOwner, isOwner },
        };
        await _provider.InvokeAsync(() => dialogs.ShowAsync<FreezeMembershipDialog>("Congelar", parameters));
        _provider.WaitForAssertion(() => Assert.Contains("Motivo", _provider.Markup));
    }

    private void Click(string text)
        => _provider.FindAll("button").First(b => b.TextContent.Contains(text)).Click();

    [Fact]
    public async Task It_shows_the_plan_limits_with_what_is_used_and_what_remains()
    {
        await OpenAsync(maxFreezeDays: 15, isOwner: false);

        var rules = _provider.WaitForElement("[data-testid='freeze-rules']").TextContent;
        Assert.Contains("hasta 15 días", rules);
        Assert.Contains("mínimo 3", rules);
        Assert.Contains("quedan 11", rules);
    }

    [Fact]
    public async Task A_plan_that_does_not_freeze_says_so_and_only_the_owner_sees_the_override()
    {
        await OpenAsync(maxFreezeDays: 0, isOwner: false);
        Assert.Contains("no permite congelar", _provider.WaitForElement("[data-testid='freeze-rules']").TextContent);
        Assert.Empty(_provider.FindAll("[data-testid='freeze-override']"));
    }

    [Fact]
    public async Task The_owner_sees_the_override_checkbox()
    {
        await OpenAsync(maxFreezeDays: 0, isOwner: true);
        Assert.NotEmpty(_provider.FindAll("[data-testid='freeze-override']"));
        Assert.Contains("saltarte el límite", _provider.Find("[data-testid='freeze-rules']").TextContent);
    }

    [Fact]
    public async Task Without_a_reason_nothing_is_sent()
    {
        await OpenAsync(maxFreezeDays: 15, isOwner: false);

        Click("Congelar");

        _provider.WaitForAssertion(() => Assert.Contains("motivo del congelamiento", _provider.Markup));
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "FreezeAsync");
    }

    [Fact]
    public async Task An_open_freeze_is_sent_without_dates_and_with_the_reason()
    {
        await OpenAsync(maxFreezeDays: 15, isOwner: false);

        _provider.Find("[data-testid='freeze-reason']").Change("Viaje de trabajo");
        Click("Congelar");

        _provider.WaitForAssertion(() => Assert.Contains(_fake.Calls, c => c.Method == "FreezeAsync"));
        var request = (CreateFreezeRequest)_fake.Calls.Last(c => c.Method == "FreezeAsync").Args[2]!;
        Assert.Null(request.StartsOn);
        Assert.Null(request.EndsOn);
        Assert.Equal("Viaje de trabajo", request.Reason);
        Assert.False(request.OverrideLimit);
    }

    [Fact]
    public async Task Choosing_scheduled_dates_requires_both_dates()
    {
        await OpenAsync(maxFreezeDays: 15, isOwner: false);

        _provider.Find("[data-testid='freeze-scheduled']").Change(true);
        _provider.Find("[data-testid='freeze-reason']").Change("Vacaciones");
        Click("Congelar");

        _provider.WaitForAssertion(() => Assert.Contains("desde y hasta", _provider.Markup));
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "FreezeAsync");
    }
}
