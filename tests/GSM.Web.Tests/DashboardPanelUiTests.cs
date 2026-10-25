using System.Reflection;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using GSM.Client;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages;
using GSM.UI.Shared;
using GSM.UI.Shared.Auth;
using GSM.UI.Shared.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// D1: el dashboard del panel se dibuja por bloques (lo que no viaja no se pinta), cambia de orden en celular y
/// refresca solo el aforo y la caja cada 60 s.
/// </summary>
public class DashboardPanelUiTests : TestContext
{
    private static readonly Guid MeId = Guid.NewGuid();

    private FakeClient _attendance = default!;
    private FakeClient _cashShifts = default!;
    private FakeClient _whatsApp = default!;

    private FakeClient _dash = null!;
    private Exception? _failNextLoad;

    [Fact]
    public void If_the_dashboard_fails_to_load_the_page_says_so_and_retry_loads_it()
    {
        _failNextLoad = new GsmApiException("Server error", 500, "{\"traceId\":\"00-abc\"}", new Dictionary<string, IEnumerable<string>>(), null);

        var cut = RenderHome(Full());

        // Antes: solo un snackbar y la página vacía bajo el título.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='dashboard-error']")));

        _failNextLoad = null;
        cut.Find("[data-testid='dashboard-retry']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='dash-indicators']")));
        Assert.Empty(cut.FindAll("[data-testid='dashboard-error']"));
    }

    [Fact]
    public void The_headline_counts_members_to_renew_like_the_status_cards_do()
    {
        // Full(): 4 por vencer, 2 en gracia y 6 vencidos (miembros). "Por renovar" = 2 + 6, como las tarjetas.
        Assert.Equal("Esta semana vencen 4 membresías y 8 miembros están por renovar (2 aún entran en gracia).", Home.Headline(Full()));

        var cut = RenderHome(Full());
        cut.WaitForAssertion(() => Assert.Contains("8 miembros están por renovar", cut.Markup));
        // La cifra en membresías (ExpiredCount = 6) ya no aparece como "ya vencidas" junto a las de miembros.
        Assert.DoesNotContain("ya vencidas", cut.Markup);
    }

    private static T Proxy<T>(out FakeClient fake) where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        fake = (FakeClient)(object)proxy;
        return proxy;
    }

    private IRenderedComponent<Home> RenderHome(
        DashboardResponse dashboard, bool narrow = false, string role = GsmRoles.Owner, params string[] permissions)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddGsmUiShared();

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles(role);
        auth.SetClaims(
            new[] { new Claim(ClaimTypes.NameIdentifier, MeId.ToString()) }
                .Concat(permissions.Select(p => new Claim(GsmAuthenticationStateProvider.PermissionClaim, p)))
                .ToArray());


        var dash = Proxy<IDashboardClient>(out var dashFake);
        _dash = dashFake;
        dashFake.Handlers["GetGymDashboardAsync"] = _ => _failNextLoad is { } fail ? throw fail : dashboard;
        Services.AddSingleton(dash);
        Services.AddSingleton(Proxy<IAttendanceClient>(out _attendance));
        Services.AddSingleton(Proxy<ICashShiftsClient>(out _cashShifts));
        Services.AddSingleton(Proxy<IWhatsAppClient>(out _whatsApp));
        _whatsApp.Handlers["StatesAsync"] = _ => new List<WhatsAppMemberStateResponse>();
        Services.AddSingleton(Proxy<IGymClient>(out _));
        Services.AddSingleton(Proxy<IEquipmentClient>(out _));
        var routines = Proxy<IRoutinesClient>(out var routinesFake);
        routinesFake.Handlers["GetRoutinesDueForReviewAsync"] = _ => new DueRoutinesResponse
        {
            Items = new List<RoutineDueItem>(),
            Mine = false,
            WindowDays = 7,
        };
        Services.AddSingleton(routines);

        // Pedir un servicio cierra el registro: el viewport se fija justo antes de renderizar.
        Services.GetRequiredService<ViewportState>().Pin(narrow);
        return RenderComponent<Home>();
    }

    private static DashboardResponse Full() => new()
    {
        Indicators = new IndicatorsBlock
        {
            ActiveMembers = 42,
            CheckInsToday = 9,
            Occupancy = new OccupancyResponse { PeopleInside = 5, Capacity = 20, Percent = 25, Level = "calm", AssumedStayMinutes = 120 },
            CommercialStatus = new CommercialStatusCounts { Current = 30, Expiring = 4, Grace = 2, Expired = 6 },
            ExpiringDays = 7,
        },
        Finances = new FinancesBlock { RevenueThisMonth = 120_000, RevenueLastMonth = 100_000 },
        Attention = new AttentionBlock
        {
            Equipment = new EquipmentAttentionBlock
            {
                OutOfService = 1,
                InMaintenance = 0,
                OpenIssues = 1,
                UrgentIssues = 1,
                Machines = new List<OutOfServiceMachineRow>
                {
                    new() { Id = Guid.NewGuid(), Name = "Prensa", Zone = "Zona B", HasUnsafeIssue = true },
                },
            },
            Rejected = new RejectedTodayBlock { Total = 2, Unseen = 1, Items = new List<RejectedAttemptRow>() },
            VoidRequests = new VoidRequestsBlock { Pending = 3 },
            Cash = new CashBlock
            {
                ShiftId = Guid.NewGuid(), OpenedByName = "Luis", OpenedAt = DateTimeOffset.UtcNow, ExpectedCash = 80_000, IsStale = false, IsMine = false,
            },
        },
        Today = new TodayBlock
        {
            Classes = new List<TodayClassRow>
            {
                new() { ClassId = Guid.NewGuid(), ClassName = "Spinning", StartTime = new TimeOnly(18, 0), DurationMinutes = 45, Capacity = 12, Booked = 12, Waiting = 2 },
            },
            Birthdays = new List<BirthdayTodayRow>(),
            RecentEntries = new List<RecentEntryRow>(),
        },
        PeakHours = new PeakHoursBlock { WindowDays = 30, Buckets = new List<HourBucket> { new() { Hour = 18, Count = 7 } } },
        Expiring = new ExpiringBlock
        {
            ExpiringCount = 4, ExpiredCount = 6, ExpiringDays = 7,
            Items = new List<ExpiringMemberRow>
            {
                new() { MembershipId = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Ana Gracia", PlanName = "Mensual", EndDate = new DateOnly(2026, 6, 13), DaysLeft = -2 },
                new() { MembershipId = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Beto Pronto", PlanName = "Mensual", EndDate = new DateOnly(2026, 6, 17), DaysLeft = 2 },
            },
        },
        MyMembers = new MyMembersBlock
        {
            MyClassesToday = new List<TodayClassRow>(),
            RoutinesDue = 3,
            RoutinesOverdue = 1,
            MembersWithoutRoutine = 2,
            WithoutRoutine = new List<MemberNameRow> { new() { MemberId = Guid.NewGuid(), MemberName = "Carla Sinrutina" } },
        },
    };

    private static int Position(IRenderedComponent<Home> cut, string testId)
        => cut.Markup.IndexOf($"data-testid=\"{testId}\"", StringComparison.Ordinal);

    // ---------- orden de celular ----------

    [Fact]
    public void Section_order_puts_attention_first_on_a_phone_and_the_hourly_chart_and_todo_last()
    {
        Assert.Equal(
            new[] { "attention", "indicators", "today", "my-members", "expiring", "peak-hours", "to-complete" },
            Home.SectionOrder(narrow: true));
        Assert.Equal(
            new[] { "to-complete", "indicators", "attention", "today", "peak-hours", "expiring", "my-members" },
            Home.SectionOrder(narrow: false));
    }

    [Fact]
    public void On_a_phone_the_rendered_sections_follow_the_phone_order()
    {
        var cut = RenderHome(Full(), narrow: true);

        cut.WaitForAssertion(() => Assert.True(Position(cut, "dash-attention") >= 0));
        var order = new[] { "dash-attention", "dash-indicators", "dash-today", "dash-my-members", "dash-expiring", "dash-peak-hours" };
        var positions = order.Select(id => Position(cut, id)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void On_the_desktop_the_rendered_sections_follow_the_desktop_order()
    {
        var cut = RenderHome(Full(), narrow: false);

        cut.WaitForAssertion(() => Assert.True(Position(cut, "dash-attention") >= 0));
        var order = new[] { "dash-indicators", "dash-attention", "dash-today", "dash-peak-hours", "dash-expiring", "dash-my-members" };
        var positions = order.Select(id => Position(cut, id)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    // ---------- bloques que no viajan ----------

    [Fact]
    public void A_block_that_does_not_travel_is_not_drawn()
    {
        var dashboard = Full();
        dashboard.Finances = null;
        dashboard.MyMembers = null;
        dashboard.Today = null;
        dashboard.Attention!.VoidRequests = null;
        dashboard.Attention.Cash = null;

        var cut = RenderHome(dashboard);

        cut.WaitForAssertion(() => Assert.True(Position(cut, "dash-attention") >= 0));
        Assert.Empty(cut.FindAll("[data-testid='finances-revenue']"));
        Assert.Empty(cut.FindAll("[data-testid='dash-my-members']"));
        Assert.Empty(cut.FindAll("[data-testid='dash-today']"));
        Assert.Empty(cut.FindAll("[data-testid='attention-voids']"));
        Assert.Empty(cut.FindAll("[data-testid='attention-cash']"));
    }

    [Fact]
    public void The_revenue_card_compares_against_the_same_days_of_last_month()
    {
        var cut = RenderHome(Full());

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='finances-revenue']").TextContent;
            Assert.Contains("120.000", text);
            Assert.Contains("+20 % vs mismos días del mes anterior", text);
        });
    }

    [Fact]
    public void Attention_shows_voids_cash_and_the_unsafe_machine()
    {
        var cut = RenderHome(Full());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("3", cut.Find("[data-testid='attention-voids']").TextContent);
            Assert.Contains("Caja abierta", cut.Find("[data-testid='attention-cash']").TextContent);
            Assert.Contains("Prensa", cut.Find("[data-testid='attention-machines']").TextContent);
            Assert.NotNull(cut.Find("[data-testid='machine-unsafe']"));
        });
    }

    [Fact]
    public void Attention_lists_memberships_pending_payment()
    {
        var dashboard = Full();
        dashboard.Attention!.Unpaid = new UnpaidBlock
        {
            Count = 1,
            TotalDue = 60000,
            Items = new List<UnpaidMembershipRow>
            {
                new() { MemberId = Guid.NewGuid(), MemberName = "Ana Prueba", PlanName = "Mensual", AmountDue = 60000 },
            },
        };

        var cut = RenderHome(dashboard);

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("[data-testid='attention-unpaid']").TextContent;
            Assert.Contains("Pendientes de pago", card);
            Assert.Contains("60.000", card);
            var list = cut.Find("[data-testid='attention-unpaid-list']").TextContent;
            Assert.Contains("Ana Prueba", list);
            Assert.Contains("Debe", list);
        });
    }

    [Fact]
    public void A_forgotten_register_is_flagged_as_such()
    {
        var dashboard = Full();
        dashboard.Attention!.Cash!.IsStale = true;

        var cut = RenderHome(dashboard);

        cut.WaitForAssertion(() => Assert.Contains("Caja olvidada", cut.Find("[data-testid='attention-cash']").TextContent));
    }

    [Fact]
    public void With_nothing_to_flag_attention_says_so()
    {
        var dashboard = Full();
        dashboard.Attention = new AttentionBlock
        {
            Equipment = new EquipmentAttentionBlock { Machines = new List<OutOfServiceMachineRow>() },
            Rejected = new RejectedTodayBlock { Items = new List<RejectedAttemptRow>() },
        };

        var cut = RenderHome(dashboard);

        cut.WaitForAssertion(() => Assert.Contains("Nada pide atención ahora", cut.Find("[data-testid='attention-empty']").TextContent));
    }

    [Fact]
    public void The_trainer_block_replaces_the_routines_due_card()
    {
        var withBlock = RenderHome(Full());
        withBlock.WaitForAssertion(() => Assert.True(Position(withBlock, "dash-my-members") >= 0));
        Assert.Empty(withBlock.FindAll("[data-testid='routines-due']"));
        Assert.Contains("Carla Sinrutina", withBlock.Find("[data-testid='my-without-routine-list']").TextContent);
        Assert.Contains("3", withBlock.Find("[data-testid='my-routines-due']").TextContent);
    }

    [Fact]
    public void Without_the_trainer_block_the_autonomous_routines_card_stays()
    {
        var dashboard = Full();
        dashboard.MyMembers = null;
        var cut = RenderHome(dashboard);

        cut.WaitForAssertion(() => Assert.True(Position(cut, "dash-indicators") >= 0));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='routines-due']")));
    }

    // ---------- por vencer y en gracia ----------

    [Fact]
    public void The_expiring_list_marks_the_ones_in_grace_and_loads_the_whatsapp_states_in_one_call()
    {
        var cut = RenderHome(Full());

        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[data-testid='dash-expiring']").TextContent;
            Assert.Contains("Ana Gracia", text);
            Assert.Contains("En gracia", text);
            Assert.Contains("Beto Pronto", text);
        });
        Assert.Single(_whatsApp.Calls, c => c.Method == "StatesAsync");
    }

    // ---------- refresco de 60 s ----------

    [Fact]
    public void The_live_refresh_interval_is_sixty_seconds()
        => Assert.Equal(TimeSpan.FromSeconds(60), Home.LiveRefreshInterval);

    [Fact]
    public async Task The_live_refresh_updates_the_occupancy_without_reloading_the_dashboard()
    {
        var cut = RenderHome(Full());
        cut.WaitForAssertion(() => Assert.Contains("5 personas", cut.Find("[data-testid='occupancy-card']").TextContent));
        _attendance.Handlers["GetOccupancyAsync"] = _ => new OccupancyResponse
            { PeopleInside = 9, Capacity = 20, Percent = 45, Level = "moderate", AssumedStayMinutes = 120 };

        await cut.InvokeAsync(() => cut.Instance.RefreshLiveAsync());

        Assert.Contains("9 personas", cut.Find("[data-testid='occupancy-card']").TextContent);
    }

    [Fact]
    public async Task The_live_refresh_is_skipped_while_the_page_is_hidden()
    {
        var cut = RenderHome(Full());
        cut.WaitForAssertion(() => Assert.Contains("5 personas", cut.Find("[data-testid='occupancy-card']").TextContent));
        JSInterop.Setup<bool>("gsmVisibility.isHidden").SetResult(true);
        _attendance.Handlers["GetOccupancyAsync"] = _ => new OccupancyResponse { PeopleInside = 9, AssumedStayMinutes = 120 };

        await cut.InvokeAsync(() => cut.Instance.RefreshLiveAsync());

        Assert.Contains("5 personas", cut.Find("[data-testid='occupancy-card']").TextContent);
        Assert.DoesNotContain(_attendance.Calls, c => c.Method == "GetOccupancyAsync");
    }

    [Fact]
    public async Task The_live_refresh_shows_the_register_that_just_opened_if_it_is_mine()
    {
        var dashboard = Full();
        dashboard.Attention!.Cash = null;
        var cut = RenderHome(dashboard, role: GsmRoles.Staff);
        cut.WaitForAssertion(() => Assert.True(Position(cut, "dash-attention") >= 0));
        Assert.Empty(cut.FindAll("[data-testid='attention-cash']"));
        _cashShifts.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse
        {
            Shift = new CashShiftResponse
            {
                Id = Guid.NewGuid(), OpenedBy = MeId, OpenedByName = "Ana", OpenedAt = DateTimeOffset.UtcNow,
                ExpectedCash = 70_000, IsOpen = true,
            },
        };

        await cut.InvokeAsync(() => cut.Instance.RefreshLiveAsync());

        var text = cut.Find("[data-testid='attention-cash']").TextContent;
        Assert.Contains("Caja abierta", text);
        Assert.Contains("La abriste tú", text);
    }

    [Fact]
    public async Task The_live_refresh_hides_someone_elses_register_from_staff_without_ViewFinances()
    {
        var cut = RenderHome(Full(), role: GsmRoles.Staff);
        _cashShifts.Handlers["CurrentAsync"] = _ => new CashDrawerStatusResponse
        {
            Shift = new CashShiftResponse
            {
                Id = Guid.NewGuid(), OpenedBy = Guid.NewGuid(), OpenedByName = "Luis", OpenedAt = DateTimeOffset.UtcNow,
                ExpectedCash = 70_000, IsOpen = true,
            },
        };

        await cut.InvokeAsync(() => cut.Instance.RefreshLiveAsync());

        Assert.Empty(cut.FindAll("[data-testid='attention-cash']"));
    }
}
