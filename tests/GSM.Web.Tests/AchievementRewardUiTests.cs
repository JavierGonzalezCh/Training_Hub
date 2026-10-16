using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Settings;
using GSM.UI.Shared.Auth;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// L5: premios de los logros. Campos del premio en el formulario, entrega desde la ficha y desde la tarjeta de
/// recepción, vista del Owner y lo que ve el miembro en "Logros".
/// </summary>
public class AchievementRewardUiTests : TestContext
{
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid ShirtId = Guid.NewGuid();
    private static readonly Guid CapId = Guid.NewGuid();

    private static readonly DateTimeOffset Expires = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    public AchievementRewardUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private FakeClient Register<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        Services.AddSingleton(proxy);
        return (FakeClient)(object)proxy;
    }

    private static MemberAchievementItem Earned(Guid id, string name, string reward, string status) => new()
    {
        AchievementId = id,
        Name = name,
        ConditionType = "Visits",
        ConditionValue = 10,
        Unlocked = true,
        UnlockedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
        RewardDescription = reward,
        RewardStatus = status,
        RewardExpiresAt = status == "Pending" ? Expires : null,
        RewardDeliveredAt = status == "Delivered" ? new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero) : null,
    };

    // ---------- formulario del logro ----------

    [Fact]
    public async Task The_achievement_form_sends_the_reward_text_and_deadline()
    {
        var fake = Register<IAchievementsClient>();
        fake.Handlers["RetroactivePreviewAsync"] = _ => new RetroactivePreviewResponse { EligibleCount = 0 };
        fake.Handlers["AchievementsPOSTAsync"] = _ => new AchievementResponse { Id = ShirtId, Name = "Constante" };
        RenderComponent<MudPopoverProvider>();
        var dialogs = RenderComponent<MudDialogProvider>();

        var service = Services.GetRequiredService<IDialogService>();
        await dialogs.InvokeAsync(() => service.ShowAsync<AchievementFormDialog>("Nuevo logro"));

        // Sin texto de premio no se ofrece el plazo.
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll("input[data-testid='reward-description']")));
        Assert.Empty(dialogs.FindAll("input[data-testid='reward-valid-days']"));

        dialogs.Find("input[data-testid='reward-description']").Change("Camiseta del gym");
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll("input[data-testid='reward-valid-days']")));
        dialogs.Find("input[data-testid='reward-valid-days']").Change("10");

        var inputs = dialogs.FindAll("input").Where(i => i.GetAttribute("data-testid") is null).ToList();
        inputs[0].Change("Constante");
        dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Guardar").Click();

        dialogs.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "AchievementsPOSTAsync");
            var request = Assert.IsType<CreateAchievementRequest>(call.Args[0]);
            Assert.Equal("Camiseta del gym", request.RewardDescription);
            Assert.Equal(10, request.RewardValidDays);
        });
    }

    [Fact]
    public async Task Editing_an_achievement_loads_its_reward()
    {
        var fake = Register<IAchievementsClient>();
        fake.Handlers["AchievementsAllAsync"] = _ => new List<AchievementResponse>
        {
            new()
            {
                Id = ShirtId, Name = "Constante", ConditionType = "Visits", ConditionValue = 10, IsActive = true,
                RewardDescription = "Camiseta del gym", RewardValidDays = 30,
            },
        };
        RenderComponent<MudPopoverProvider>();
        var dialogs = RenderComponent<MudDialogProvider>();

        var service = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AchievementFormDialog> { { x => x.AchievementId, ShirtId } };
        await dialogs.InvokeAsync(() => service.ShowAsync<AchievementFormDialog>("Editar logro", parameters));

        dialogs.WaitForAssertion(() =>
        {
            Assert.Equal("Camiseta del gym", dialogs.Find("input[data-testid='reward-description']").GetAttribute("value"));
            Assert.Equal("30", dialogs.Find("input[data-testid='reward-valid-days']").GetAttribute("value"));
        });
    }

    [Fact]
    public void The_settings_table_shows_each_achievements_reward()
    {
        var fake = Register<IAchievementsClient>();
        fake.Handlers["AchievementsAllAsync"] = _ => new List<AchievementResponse>
        {
            new() { Id = ShirtId, Name = "Constante", ConditionType = "Visits", ConditionValue = 10, IsActive = true, RewardDescription = "Camiseta del gym", RewardValidDays = 30 },
            new() { Id = CapId, Name = "Honor", ConditionType = "Visits", ConditionValue = 50, IsActive = true },
        };

        var cut = RenderComponent<AchievementsTab>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Camiseta del gym · 30 días", cut.Find($"[data-testid='reward-text-{ShirtId}']").TextContent);
            Assert.Empty(cut.FindAll($"[data-testid='reward-text-{CapId}']"));
        });
    }

    // ---------- vista del Owner ----------

    [Fact]
    public void The_owner_loads_the_rewards_summary_and_filters_by_status()
    {
        var fake = Register<IAchievementsClient>();
        fake.Handlers["AchievementsAllAsync"] = _ => new List<AchievementResponse>();
        fake.Handlers["ListRewardsAsync"] = args => new RewardsOverviewResponse
        {
            Pending = 2,
            Delivered = 5,
            Expired = 1,
            Items = new List<RewardListItem>
            {
                new()
                {
                    AchievementId = ShirtId, AchievementName = "Constante", MemberId = MemberId, MemberName = "Ana Prueba",
                    RewardDescription = "Camiseta del gym", Status = "Pending", UnlockedAt = Expires.AddDays(-10), ExpiresAt = Expires,
                },
            },
        };

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<RewardsOverviewPanel>();
        Assert.Empty(cut.FindAll("[data-testid='rewards-summary']"));

        cut.Find("[data-testid='rewards-load']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2", cut.Find("[data-testid='rewards-pending']").TextContent);
            Assert.Equal("5", cut.Find("[data-testid='rewards-delivered']").TextContent);
            Assert.Equal("1", cut.Find("[data-testid='rewards-expired']").TextContent);
            Assert.Contains("Ana Prueba", cut.Markup);
            Assert.Contains("Camiseta del gym", cut.Markup);
        });

        cut.Find("[data-testid='rewards-filter-expired']").Click();

        cut.WaitForAssertion(() =>
        {
            var last = fake.Calls.Last(c => c.Method == "ListRewardsAsync");
            Assert.Equal("expired", last.Args[0]);
        });
    }

    // ---------- ficha del miembro ----------

    [Fact]
    public void The_member_file_shows_each_reward_and_delivers_the_pending_one()
    {
        var fake = Register<IAchievementsClient>();
        fake.Handlers["MemberAsync"] = _ => new List<MemberAchievementItem>
        {
            Earned(ShirtId, "Constante", "Camiseta del gym", "Pending"),
            Earned(CapId, "Honor", "Gorra", "Delivered"),
        };
        fake.Handlers["DeliverRewardAsync"] = _ => new RewardDeliveredResponse { AchievementId = ShirtId, MemberId = MemberId };

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<MemberAchievementsPanel>(p => p.Add(x => x.MemberId, MemberId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Pendiente", cut.Find($"[data-testid='reward-{ShirtId}']").TextContent);
            Assert.Contains("vence el", cut.Find($"[data-testid='reward-{ShirtId}']").TextContent);
            Assert.Contains("Entregado", cut.Find($"[data-testid='reward-{CapId}']").TextContent);
        });

        // Solo el pendiente se puede entregar.
        Assert.Empty(cut.FindAll($"[data-testid='deliver-{CapId}']"));
        cut.Find($"[data-testid='deliver-{ShirtId}']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(fake.Calls, c => c.Method == "DeliverRewardAsync");
            Assert.Equal(ShirtId, call.Args[0]);
            Assert.Equal(MemberId, call.Args[1]);
        });
    }

    [Fact]
    public void An_expired_reward_is_shown_as_expired_with_no_deliver_button()
    {
        var fake = Register<IAchievementsClient>();
        fake.Handlers["MemberAsync"] = _ => new List<MemberAchievementItem>
        {
            Earned(ShirtId, "Constante", "Camiseta del gym", "Expired"),
        };

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<MemberAchievementsPanel>(p => p.Add(x => x.MemberId, MemberId));

        cut.WaitForAssertion(() => Assert.Contains("Vencido", cut.Find($"[data-testid='reward-{ShirtId}']").TextContent));
        Assert.Empty(cut.FindAll($"[data-testid='deliver-{ShirtId}']"));
    }

    // ---------- tarjeta de recepción ----------

    [Fact]
    public void The_reception_card_lists_pending_rewards_and_marks_them_delivered()
    {
        var card = new ReceptionCardResponse
        {
            MemberId = MemberId, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered", CanRegister = true,
            PendingRewards = new List<ReceptionRewardResponse>
            {
                new() { AchievementId = ShirtId, AchievementName = "Constante", RewardDescription = "Camiseta del gym", ExpiresAt = Expires },
            },
        };
        var achievements = Register<IAchievementsClient>();
        achievements.Handlers["DeliverRewardAsync"] = _ => new RewardDeliveredResponse { AchievementId = ShirtId, MemberId = MemberId };
        var page = RenderCheckIn(card, out var attendance);

        PickAna(page);

        page.WaitForAssertion(() =>
        {
            var alert = page.Find("#pending-rewards").TextContent;
            Assert.Contains("un premio por reclamar", alert);
            Assert.Contains("Camiseta del gym", alert);
            Assert.Contains("vence el", alert);
        });

        page.Find($"[data-testid='deliver-{ShirtId}']").Click();

        page.WaitForAssertion(() =>
        {
            var call = Assert.Single(achievements.Calls, c => c.Method == "DeliverRewardAsync");
            Assert.Equal(ShirtId, call.Args[0]);
            Assert.Equal(MemberId, call.Args[1]);
            // La tarjeta se recarga tras entregar.
            Assert.Equal(2, attendance.Calls.Count(c => c.Method == "ReceptionCardAsync"));
        });
    }

    [Fact]
    public void A_card_without_rewards_shows_no_rewards_block()
    {
        Register<IAchievementsClient>();
        var page = RenderCheckIn(
            new ReceptionCardResponse
            {
                MemberId = MemberId, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
                CanRegister = true, PendingRewards = new List<ReceptionRewardResponse>(),
            },
            out _);

        PickAna(page);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("#reception-card")));
        Assert.Empty(page.FindAll("#pending-rewards"));
    }

    // ---------- app del miembro ----------

    [Fact]
    public void The_member_sees_pending_rewards_on_top_and_each_reward_state()
    {
        var me = Register<IMeClient>();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>
        {
            Earned(ShirtId, "Constante", "Camiseta del gym", "Pending"),
            Earned(CapId, "Honor", "Gorra", "Delivered"),
            new()
            {
                AchievementId = Guid.NewGuid(), Name = "Mil visitas", ConditionType = "Visits", ConditionValue = 1000,
                Unlocked = false, RewardDescription = "Chaqueta", RewardStatus = "None",
            },
        };
        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowGamification = true } }, impersonating: false);
        Services.AddSingleton(gym);

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() =>
        {
            var top = cut.Find("[data-testid='pending-rewards']").TextContent;
            Assert.Contains("Premio pendiente", top);
            Assert.Contains("Camiseta del gym", top);
            Assert.Contains("reclámalo en recepción", top);
            Assert.DoesNotContain("Gorra", top);
            Assert.Contains("Premio entregado: Gorra", cut.Find($"[data-testid='reward-line-{CapId}']").TextContent);
        });
        // Un logro bloqueado muestra el premio que ofrece, sin llamarlo pendiente.
        Assert.Contains("Premio: Chaqueta", cut.Markup);
    }

    [Fact]
    public void Without_pending_rewards_the_member_page_shows_no_reward_banner()
    {
        var me = Register<IMeClient>();
        me.Handlers["AchievementsAll2Async"] = _ => new List<MemberAchievementItem>
        {
            Earned(CapId, "Honor", "Gorra", "Delivered"),
        };
        var gym = new GymContext();
        gym.Set(new MeResponse { FeatureFlags = new MeFeatureFlags { ShowGamification = true } }, impersonating: false);
        Services.AddSingleton(gym);

        RenderComponent<MudPopoverProvider>();
        var cut = RenderComponent<GSM.Member.UI.Pages.Achievements>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"[data-testid='reward-line-{CapId}']")));
        Assert.Empty(cut.FindAll("[data-testid='pending-rewards']"));
    }

    // ---------- helpers ----------

    private IRenderedComponent<CheckIn> RenderCheckIn(ReceptionCardResponse card, out FakeClient attendanceFake)
    {
        var attendance = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        attendanceFake = (FakeClient)(object)attendance;
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => card;
        Services.AddSingleton(attendance);
        Services.AddSingleton(DispatchProxy.Create<IMembersClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMemberMembershipsClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IPaymentsClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMembershipPlansClient, FakeClient>());
        var whatsApp = DispatchProxy.Create<IWhatsAppClient, FakeClient>();
        ((FakeClient)(object)whatsApp).Handlers["StatesAsync"] = _ => new List<WhatsAppMemberStateResponse>();
        Services.AddSingleton(whatsApp);

        this.AddTestAuthorization().SetAuthorized("staff");
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<CheckIn>();
    }

    private static void PickAna(IRenderedComponent<CheckIn> page)
    {
        var ana = new MemberListItem
        {
            Id = MemberId, FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active",
        };
        page.InvokeAsync(() => (Task)typeof(CheckIn)
            .GetMethod("OnMemberPickedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page.Instance, [ana])!).GetAwaiter().GetResult();
        page.Render();
    }
}
