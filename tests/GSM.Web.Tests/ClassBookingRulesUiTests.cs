using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Classes;
using GSM.Panel.UI.Pages.Members;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>C3: la pestaña de reglas de reserva y el diálogo de penalizados.</summary>
public class ClassBookingRulesUiTests : TestContext
{
    [Fact]
    public void The_rules_tab_shows_the_saved_values_and_saves_the_edited_ones()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IClassSettingsClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassSettingsGETAsync"] = _ => new ClassSettingsResponse
        {
            CancelDeadlineHours = 4, NoShowLimit = 2, NoShowWindowDays = 15,
            NoShowBlockDays = 10, BookingWindowDays = 14, MaxActiveBookings = 3,
        };
        fake.Handlers["ClassSettingsPUTAsync"] = args =>
        {
            var r = (UpdateClassSettingsRequest)args[0]!;
            return new ClassSettingsResponse
            {
                CancelDeadlineHours = r.CancelDeadlineHours, NoShowLimit = r.NoShowLimit, NoShowWindowDays = r.NoShowWindowDays,
                NoShowBlockDays = r.NoShowBlockDays, BookingWindowDays = r.BookingWindowDays, MaxActiveBookings = r.MaxActiveBookings,
            };
        };
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();

        var tab = RenderComponent<ClassRulesTab>();

        tab.WaitForAssertion(() =>
        {
            Assert.Equal("4", tab.Find("input#rule-cancel-deadline").GetAttribute("value"));
            Assert.Equal("14", tab.Find("input#rule-booking-window").GetAttribute("value"));
            Assert.Equal("3", tab.Find("input#rule-max-active").GetAttribute("value"));
        });

        tab.Find("input#rule-max-active").Change("6");
        tab.Find("#rules-save").Click();

        tab.WaitForAssertion(() =>
        {
            var put = Assert.Single(fake.Calls, c => c.Method == "ClassSettingsPUTAsync");
            var body = Assert.IsType<UpdateClassSettingsRequest>(put.Args[0]);
            Assert.Equal(6, body.MaxActiveBookings);
            Assert.Equal(4, body.CancelDeadlineHours);
        });
    }

    [Fact]
    public void The_blocks_dialog_lists_penalized_members_and_lifting_requires_a_reason()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var blockId = Guid.NewGuid();
        var proxy = DispatchProxy.Create<IClassBookingBlocksClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassBookingBlocksAsync"] = _ => new List<BookingBlockResponse>
        {
            new() { Id = blockId, MemberId = Guid.NewGuid(), MemberName = "Ana Pérez", BlockedUntil = new DateOnly(2026, 10, 20), StrikeCount = 3 },
        };
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        provider.InvokeAsync(() => dialogs.ShowAsync<BookingBlocksDialog>("Penalizados"));

        provider.WaitForAssertion(() => Assert.Contains("Ana Pérez", provider.Markup));
        provider.Find(".gsm-block-row button").Click();

        provider.WaitForAssertion(() =>
            Assert.True(provider.Find("#lift-confirm").HasAttribute("disabled")));
    }
    [Fact]
    public void The_member_page_shows_an_active_penalty_and_staff_can_lift_it_with_a_reason()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var memberId = Guid.NewGuid();
        var blockId = Guid.NewGuid();
        var proxy = DispatchProxy.Create<IClassBookingBlocksClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassBookingBlocksAsync"] = _ => new List<BookingBlockResponse>
        {
            new() { Id = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = "Otra Persona", BlockedUntil = new DateOnly(2026, 10, 20), StrikeCount = 3 },
            new() { Id = blockId, MemberId = memberId, MemberName = "Ana Pérez", BlockedUntil = new DateOnly(2026, 10, 20), StrikeCount = 3 },
        };
        fake.Handlers["LiftAsync"] = _ => null;
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<MemberBookingBlockNotice>(p => p.Add(x => x.MemberId, memberId));

        cut.WaitForAssertion(() => Assert.Contains("No puede reservar clases hasta el", cut.Markup));
        cut.Find("[data-testid=booking-block-lift]").Click();
        Assert.True(cut.Find("[data-testid=booking-block-confirm]").HasAttribute("disabled"));

        cut.Find("[data-testid=booking-block-reason]").Input("Estuvo hospitalizado");
        cut.Find("[data-testid=booking-block-confirm]").Click();

        cut.WaitForAssertion(() =>
        {
            var lift = Assert.Single(fake.Calls, c => c.Method == "LiftAsync");
            Assert.Equal(blockId, lift.Args[0]);
            Assert.Empty(cut.FindAll("[data-testid=booking-block-alert]"));
        });
    }

    [Fact]
    public void The_member_page_shows_nothing_without_a_penalty()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IClassBookingBlocksClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ClassBookingBlocksAsync"] = _ => new List<BookingBlockResponse>();
        Services.AddSingleton(proxy);
        RenderComponent<MudPopoverProvider>();

        var cut = RenderComponent<MemberBookingBlockNotice>(p => p.Add(x => x.MemberId, Guid.NewGuid()));

        cut.WaitForState(() => fake.Calls.Any(c => c.Method == "ClassBookingBlocksAsync"));
        Assert.Empty(cut.FindAll("[data-testid=booking-block-alert]"));
    }
}
