using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>L4: "Otorgar logro" y "Retirar" en la ficha del miembro, y el tipo manual en el formulario de logros.</summary>
public class ManualAchievementUiTests : TestContext
{
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid CompetitionId = Guid.NewGuid();
    private static readonly Guid VisitsId = Guid.NewGuid();

    private readonly FakeClient _fake;

    public ManualAchievementUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAchievementsClient, FakeClient>();
        _fake = (FakeClient)(object)proxy;
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();
    }

    private static MemberAchievementItem Manual(bool unlocked, string? reason = null) => new()
    {
        AchievementId = CompetitionId,
        Name = "Campeón de abril",
        ConditionType = "Manual",
        ConditionValue = 1,
        IsManual = true,
        Unlocked = unlocked,
        UnlockedAt = unlocked ? new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero) : null,
        GrantReason = reason,
    };

    private static MemberAchievementItem Automatic() => new()
    {
        AchievementId = VisitsId,
        Name = "10 visitas",
        ConditionType = "Visits",
        ConditionValue = 10,
        IsManual = false,
        Unlocked = true,
        UnlockedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
    };

    private IRenderedComponent<MemberAchievementsPanel> Render(params MemberAchievementItem[] items)
    {
        _fake.Handlers["MemberAsync"] = _ => items.ToList();
        _fake.Handlers["GrantManualAsync"] = _ => Manual(true, "Ganó");
        return RenderComponent<MemberAchievementsPanel>(p => p.Add(x => x.MemberId, MemberId));
    }

    [Fact]
    public void Shows_manual_achievements_to_grant_and_the_reason_of_the_ones_already_granted()
    {
        var cut = Render(Manual(false), Automatic());

        cut.WaitForAssertion(() => Assert.Contains("Campeón de abril", cut.Find("#grant-achievement-select").InnerHtml));
        // El automático ganado aparece como tal y no se ofrece para otorgar ni retirar.
        Assert.Contains("Automático", cut.Markup);
        Assert.DoesNotContain("10 visitas</option>", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid^='revoke-']"));
    }

    [Fact]
    public void Granting_needs_an_achievement_and_a_reason_then_calls_the_api()
    {
        var cut = Render(Manual(false));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#grant-achievement-select")));

        Assert.True(cut.Find("[data-testid='grant-submit']").HasAttribute("disabled"));

        cut.Find("#grant-achievement-select").Change(CompetitionId.ToString());
        Assert.True(cut.Find("[data-testid='grant-submit']").HasAttribute("disabled"));

        cut.Find("input[data-testid='grant-reason']").Input("Ganó la competencia de abril");
        Assert.False(cut.Find("[data-testid='grant-submit']").HasAttribute("disabled"));

        cut.Find("[data-testid='grant-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_fake.Calls, c => c.Method == "GrantManualAsync");
            Assert.Equal(CompetitionId, call.Args[0]);
            var request = Assert.IsType<GrantManualAchievementRequest>(call.Args[1]);
            Assert.Equal(MemberId, request.MemberId);
            Assert.Equal("Ganó la competencia de abril", request.Reason);
        });
    }

    [Fact]
    public void A_granted_manual_achievement_shows_its_reason_and_can_be_revoked_with_a_reason()
    {
        _fake.Handlers["RevokeManualAsync"] = _ => null;
        var cut = Render(Manual(true, "Ganó la competencia de abril"));

        cut.WaitForAssertion(() => Assert.Contains("Ganó la competencia de abril", cut.Find("[data-testid='manual-reason']").TextContent));

        cut.Find($"[data-testid='revoke-{CompetitionId}']").Click();
        Assert.True(cut.Find("[data-testid='revoke-submit']").HasAttribute("disabled"));

        cut.Find("input[data-testid='revoke-reason']").Input("Se otorgó por error");
        cut.Find("[data-testid='revoke-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_fake.Calls, c => c.Method == "RevokeManualAsync");
            Assert.Equal(CompetitionId, call.Args[0]);
            Assert.Equal(MemberId, call.Args[1]);
            Assert.Equal("Se otorgó por error", Assert.IsType<RevokeManualAchievementRequest>(call.Args[2]).Reason);
        });
    }

    [Fact]
    public void Without_manual_achievements_it_explains_where_to_create_them()
    {
        var cut = Render(Automatic());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='no-manual-achievements']")));
        Assert.Empty(cut.FindAll("#grant-achievement-select"));
    }

    [Fact]
    public async Task Editing_a_manual_achievement_hides_the_numeric_condition()
    {
        var dialogs = RenderComponent<MudDialogProvider>();
        _fake.Handlers["AchievementsAllAsync"] = _ => new List<AchievementResponse>
        {
            new() { Id = CompetitionId, Name = "Campeón de abril", ConditionType = "Manual", ConditionValue = 1, IsActive = true },
        };

        var service = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AchievementFormDialog> { { x => x.AchievementId, CompetitionId } };
        await dialogs.InvokeAsync(() => service.ShowAsync<AchievementFormDialog>("Editar logro", parameters));

        dialogs.WaitForAssertion(() => Assert.NotNull(dialogs.Find("[data-testid='manual-hint']")));
        Assert.DoesNotContain("Valor (", dialogs.Markup);
    }

    [Fact]
    public async Task Saving_a_manual_achievement_does_not_ask_about_retroactive_grants()
    {
        var dialogs = RenderComponent<MudDialogProvider>();
        _fake.Handlers["AchievementsAllAsync"] = _ => new List<AchievementResponse>
        {
            new() { Id = CompetitionId, Name = "Campeón de abril", ConditionType = "Manual", ConditionValue = 1, IsActive = true },
        };
        _fake.Handlers["AchievementsPUTAsync"] = _ => new AchievementResponse { Id = CompetitionId, Name = "Campeón de abril" };

        var service = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AchievementFormDialog> { { x => x.AchievementId, CompetitionId } };
        await dialogs.InvokeAsync(() => service.ShowAsync<AchievementFormDialog>("Editar logro", parameters));
        dialogs.WaitForAssertion(() => Assert.NotNull(dialogs.Find("[data-testid='manual-hint']")));

        dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        dialogs.WaitForAssertion(() => Assert.Contains(_fake.Calls, c => c.Method == "AchievementsPUTAsync"));
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "RetroactivePreviewAsync");
        Assert.DoesNotContain(_fake.Calls, c => c.Method == "GrantRetroactiveAsync");
    }
}
