using Bunit.TestDoubles;
using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using GSM.Panel.UI.Pages.Members;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>A1b: entradas múltiples en la ficha del miembro y en la tarjeta de recepción.</summary>
public class EntryRuleUiTests : TestContext
{
    private static readonly Guid MemberId = Guid.NewGuid();

    private static readonly MemberListItem Ana = new()
    {
        Id = MemberId, FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active",
    };

    private FakeClient RegisterAttendance()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        Services.AddSingleton(proxy);
        return fake;
    }

    private static EntryRuleResponse Enabled(int max = 2, DateOnly? until = null) => new()
    {
        MemberId = MemberId, Enabled = true, Expired = false, MaxDailyEntries = max, Reason = "Entrena mañana y tarde",
        ValidUntil = until, GrantedByName = "Olga Dueña",
    };

    [Fact]
    public void Without_a_rule_the_panel_says_one_entry_per_day_and_offers_to_enable()
    {
        var fake = RegisterAttendance();
        fake.Handlers["GetEntryRuleAsync"] = _ => new EntryRuleResponse { MemberId = MemberId, Enabled = false, Expired = false };

        var cut = RenderComponent<MemberEntryRulePanel>(p => p.Add(x => x.MemberId, MemberId));

        cut.WaitForAssertion(() => Assert.Contains("Una entrada por día", cut.Find("#entry-rule-summary").TextContent));
        Assert.Contains("Habilitar entradas múltiples", cut.Find("#entry-rule-edit").TextContent);
        Assert.Empty(cut.FindAll("#entry-rule-remove"));
    }

    [Fact]
    public void With_a_rule_the_panel_shows_max_reason_and_who_granted_it()
    {
        var fake = RegisterAttendance();
        fake.Handlers["GetEntryRuleAsync"] = _ => Enabled(3);

        var cut = RenderComponent<MemberEntryRulePanel>(p => p.Add(x => x.MemberId, MemberId));

        cut.WaitForAssertion(() => Assert.Contains("3 por día", cut.Find("#entry-rule-summary").TextContent));
        Assert.Contains("Entrena mañana y tarde", cut.Find("#entry-rule-detail").TextContent);
        Assert.Contains("Olga Dueña", cut.Find("#entry-rule-detail").TextContent);
        Assert.NotNull(cut.Find("#entry-rule-remove"));
    }

    [Fact]
    public void Saving_sends_max_reason_and_expiry_and_requires_a_reason()
    {
        var fake = RegisterAttendance();
        fake.Handlers["GetEntryRuleAsync"] = _ => new EntryRuleResponse { MemberId = MemberId, Enabled = false, Expired = false };
        fake.Handlers["SetEntryRuleAsync"] = _ => Enabled(4, new DateOnly(2026, 12, 31));

        var cut = RenderComponent<MemberEntryRulePanel>(p => p.Add(x => x.MemberId, MemberId));
        cut.WaitForAssertion(() => cut.Find("#entry-rule-edit").Click());

        // Sin motivo no llama a la API.
        cut.Find("#entry-rule-save").Click();
        Assert.DoesNotContain(fake.Calls, c => c.Method == "SetEntryRuleAsync");

        cut.Find("#entry-rule-max").Change("4");
        cut.Find("#entry-rule-until").Change("2026-12-31");
        cut.Find("textarea, input[type=text]").Input("Entrena mañana y tarde");
        cut.Find("#entry-rule-save").Click();

        cut.WaitForAssertion(() => Assert.Contains("4 por día", cut.Find("#entry-rule-summary").TextContent));
        var call = fake.Calls.Last(c => c.Method == "SetEntryRuleAsync");
        Assert.Equal(MemberId, (Guid)call.Args[0]!);
        var request = (SetEntryRuleRequest)call.Args[1]!;
        Assert.Equal(4, request.MaxDailyEntries);
        Assert.Equal("Entrena mañana y tarde", request.Reason);
        Assert.Equal(new DateOnly(2026, 12, 31), request.ValidUntil);
    }

    [Fact]
    public void Removing_calls_the_api_and_goes_back_to_one_entry_per_day()
    {
        var fake = RegisterAttendance();
        var removed = false;
        fake.Handlers["GetEntryRuleAsync"] = _ => removed
            ? new EntryRuleResponse { MemberId = MemberId, Enabled = false, Expired = false }
            : Enabled();
        fake.Handlers["RemoveEntryRuleAsync"] = _ =>
        {
            removed = true;
            return null;
        };

        var cut = RenderComponent<MemberEntryRulePanel>(p => p.Add(x => x.MemberId, MemberId));
        cut.WaitForAssertion(() => cut.Find("#entry-rule-remove").Click());

        cut.WaitForAssertion(() => Assert.Contains("Una entrada por día", cut.Find("#entry-rule-summary").TextContent));
        Assert.Contains(fake.Calls, c => c.Method == "RemoveEntryRuleAsync");
    }

    [Fact]
    public async Task Reception_card_shows_the_multiple_entries_line_and_still_offers_to_register()
    {
        var page = RenderReception(out _, new ReceptionCardResponse
        {
            MemberId = MemberId, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            AlreadyCheckedInToday = true, CheckedInAt = DateTimeOffset.UtcNow.AddHours(-3), CanRegister = true,
            MultipleEntries = true, MaxDailyEntries = 2, EntriesToday = 1,
        });

        await Pick(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Entradas múltiples: 2 por día · lleva 1 hoy", page.Find("#multiple-entries").TextContent);
            Assert.Contains("Ya entró hoy a las", page.Find("#already-in").TextContent);
            Assert.NotNull(page.Find("#register-button"));
        });
    }

    [Fact]
    public async Task Reception_card_inside_the_two_hour_gap_says_when_the_member_can_come_back_and_hides_register()
    {
        var page = RenderReception(out _, new ReceptionCardResponse
        {
            MemberId = MemberId, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            AlreadyCheckedInToday = true, CheckedInAt = DateTimeOffset.UtcNow.AddMinutes(-30), CanRegister = false,
            RejectionReason = "already_checked_in", MultipleEntries = true, MaxDailyEntries = 2, EntriesToday = 1,
            NextEntryAt = DateTimeOffset.UtcNow.AddMinutes(90),
        });

        await Pick(page);

        page.WaitForAssertion(() => Assert.Contains("Puede volver a entrar a partir de las", page.Find("#next-entry").TextContent));
        Assert.Empty(page.FindAll("#register-button"));
    }

    [Fact]
    public async Task Reception_card_without_a_rule_has_no_multiple_entries_line()
    {
        var page = RenderReception(out _, new ReceptionCardResponse
        {
            MemberId = MemberId, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            CanRegister = true,
        });

        await Pick(page);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("#register-button")));
        Assert.Empty(page.FindAll("#multiple-entries"));
    }

    private IRenderedComponent<CheckIn> RenderReception(out FakeClient attendanceFake, ReceptionCardResponse card)
    {
        attendanceFake = RegisterAttendance();
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => card;
        Services.AddSingleton(DispatchProxy.Create<IMembersClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMemberMembershipsClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IPaymentsClient, FakeClient>());
        Services.AddSingleton(DispatchProxy.Create<IMembershipPlansClient, FakeClient>());
        var whatsApp = DispatchProxy.Create<IWhatsAppClient, FakeClient>();
        ((FakeClient)(object)whatsApp).Handlers["StatesAsync"] = _ => new List<WhatsAppMemberStateResponse>();
        Services.AddSingleton(whatsApp);
        Services.AddSingleton(CommercialStatusUiTests.Fake<GSM.Client.Generated.ICashShiftsClient>().Client); // P3-4: recepción muestra la barra de caja

        this.AddTestAuthorization().SetAuthorized("staff");
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<CheckIn>();
    }

    private static async Task Pick(IRenderedComponent<CheckIn> page)
    {
        await page.InvokeAsync(() => (Task)typeof(CheckIn)
            .GetMethod("OnMemberPickedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page.Instance, [Ana])!);
        page.Render();
    }
}
