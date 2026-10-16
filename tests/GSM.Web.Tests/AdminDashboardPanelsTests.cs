using Bunit;
using GSM.Client.Generated;
using GSM.Web.Pages.Admin;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>D3: los bloques nuevos del panel del SuperAdmin (requiere acción, arranque, límites).</summary>
public class AdminDashboardPanelsTests : TestContext
{
    private sealed class FakeOwnersClient : IAdminGymOwnersClient
    {
        public List<(Guid GymId, Guid UserId)> Resent { get; } = [];

        public Task ResendAsync(Guid gymId, Guid userId, CancellationToken cancellationToken = default)
        {
            Resent.Add((gymId, userId));
            return Task.CompletedTask;
        }

        public Task<ICollection<GymOwnerResponse>> OwnersAllAsync(Guid gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GymOwnerResponse> OwnersPOSTAsync(Guid gymId, InviteOwnerRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountLinkResponse> LinkAsync(Guid gymId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BillingContactResponse> BillingContactGETAsync(Guid gymId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GymOwnerResponse> OwnersDELETEAsync(Guid gymId, Guid userId, bool? keepAsStaff = null, string? staffProfile = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private FakeOwnersClient Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var owners = new FakeOwnersClient();
        Services.AddSingleton<IAdminGymOwnersClient>(owners);
        return owners;
    }

    private static AttentionList List(params AttentionGymRow[] rows)
        => new() { Total = rows.Length, Items = rows.ToList() };

    private static AttentionGymRow Row(string name, int? toDisable, int? toExpiry, string status = "Overdue") => new()
    {
        GymId = Guid.NewGuid(),
        Name = name,
        EffectiveStatus = status,
        PaidThrough = new DateOnly(2026, 11, 1),
        DaysUntilDisabled = toDisable,
        DaysUntilExpiry = toExpiry,
    };

    [Fact]
    public void Attention_shows_four_lists_with_days_in_plain_words()
    {
        Setup();
        var block = new AdminAttentionBlock
        {
            TrialEnded = List(Row("Alfa", 2, null, "TrialEnded")),
            Overdue = List(Row("Beta", 0, null), Row("Gamma", 1, null)),
            AboutToDisable = List(),
            ExpiringSoon = List(Row("Delta", null, 5, "Active")),
        };

        var cut = RenderComponent<AttentionPanel>(p => p.Add(c => c.Block, block));

        Assert.Equal("1", cut.Find("[data-testid='attention-trial-ended-total']").TextContent.Trim());
        Assert.Equal("2", cut.Find("[data-testid='attention-overdue-total']").TextContent.Trim());
        Assert.Contains("Nada pendiente", cut.Find("[data-testid='attention-about-to-disable']").TextContent);
        Assert.Contains("se inhabilita hoy", cut.Find("[data-testid='attention-overdue']").TextContent);
        Assert.Contains("se inhabilita mañana", cut.Find("[data-testid='attention-overdue']").TextContent);
        Assert.Contains("vence en 5 días", cut.Find("[data-testid='attention-expiring']").TextContent);
    }

    [Fact]
    public void A_missing_attention_block_renders_nothing()
    {
        Setup();

        var cut = RenderComponent<AttentionPanel>(p => p.Add(c => c.Block, (AdminAttentionBlock?)null));

        Assert.Empty(cut.FindAll("[data-testid='attention-panel']"));
        Assert.DoesNotContain("Requiere acción", cut.Markup);
    }

    [Fact]
    public void Day_labels_distinguish_disable_from_expiry()
    {
        Assert.Equal("se inhabilita en 3 días", AttentionPanel.DaysLabel(Row("X", 3, null), byDisable: true));
        Assert.Equal("vence mañana", AttentionPanel.DaysLabel(Row("X", null, 1), byDisable: false));
        Assert.Equal("—", AttentionPanel.DaysLabel(Row("X", null, null), byDisable: true));
    }

    [Fact]
    public async Task Startup_resends_access_to_the_pending_owner()
    {
        var owners = Setup();
        var gymId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var block = new AdminStartupBlock
        {
            Total = 2,
            Items =
            [
                new StartupGymRow
                {
                    GymId = gymId, Name = "Nuevo", DaysSinceCreated = 4, OwnerNotActivated = true, OnboardingPending = true,
                    PendingOwnerUserId = ownerId, PendingOwnerEmail = "dueno@nuevo.test",
                },
                new StartupGymRow
                {
                    GymId = Guid.NewGuid(), Name = "Medio", DaysSinceCreated = 1, OwnerNotActivated = false, OnboardingPending = true,
                },
            ],
        };

        var cut = RenderComponent<StartupPanel>(p => p.Add(c => c.Block, block));

        // Solo el gym con el Owner sin activar ofrece "Reenviar acceso".
        var buttons = cut.FindAll("[data-testid='startup-resend']");
        Assert.Single(buttons);
        Assert.Contains("4 días", cut.Markup);
        Assert.Contains("1 día", cut.Markup);

        await cut.InvokeAsync(() => buttons[0].Click());

        Assert.Equal([(gymId, ownerId)], owners.Resent);
    }

    [Fact]
    public void Startup_without_gyms_says_everyone_is_set_up()
    {
        Setup();

        var cut = RenderComponent<StartupPanel>(p => p.Add(c => c.Block, new AdminStartupBlock { Total = 0, Items = [] }));

        Assert.Contains("activaron su cuenta", cut.Find("[data-testid='startup-empty']").TextContent);
    }

    [Fact]
    public void Limits_shows_usage_against_the_limit()
    {
        Setup();
        var block = new AdminLimitsBlock
        {
            Total = 1,
            Items = [new LimitGymRow { GymId = Guid.NewGuid(), Name = "Casi lleno", CountedMembers = 142, MemberLimit = 150, UsagePercent = 94 }],
        };

        var cut = RenderComponent<LimitsPanel>(p => p.Add(c => c.Block, block));

        var row = cut.Find("[data-testid='limits-table'] tbody tr");
        Assert.Contains("142 / 150", row.TextContent);
        Assert.Contains("94", row.TextContent);
    }

    [Fact]
    public void Dashboard_inactive_label_names_the_days_or_the_absence_of_entries()
    {
        Assert.Equal("Sin entradas hace 9 días", GSM.Web.Pages.Admin.Dashboard.InactiveLabel(9));
        Assert.Equal("Sin entradas", GSM.Web.Pages.Admin.Dashboard.InactiveLabel(null));
    }
}
