using Bunit.TestDoubles;
using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using GSM.Panel.UI.Pages.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>CO5: "Hoy cumple años" en recepción y los ajustes de cumpleaños en Comunicaciones.</summary>
public class BirthdayUiTests : TestContext
{
    private static readonly MemberListItem Ana = new()
    {
        Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active",
    };

    [Fact]
    public async Task The_reception_card_says_when_the_member_has_a_birthday_today()
    {
        var page = RenderReception(birthdayToday: true);
        await Pick(page);

        page.WaitForAssertion(() => Assert.Contains("Hoy cumple años", page.Find("#birthday-today").TextContent));
    }

    [Fact]
    public async Task The_reception_card_shows_nothing_special_on_other_days()
    {
        var page = RenderReception(birthdayToday: false);
        await Pick(page);

        page.WaitForAssertion(() => Assert.Contains("Ana Prueba", page.Find("#reception-card").TextContent));
        Assert.Empty(page.FindAll("#birthday-today"));
    }

    [Fact]
    public void Settings_tab_loads_the_birthday_settings_and_saves_them()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var announcements = DispatchProxy.Create<IAnnouncementsClient, FakeClient>();
        var fake = (FakeClient)(object)announcements;
        fake.Handlers["ReminderSettingsGETAsync"] = _ => new CommunicationsSettingsResponse { ReminderDaysBefore = 3 };
        fake.Handlers["BirthdaySettingsGETAsync"] = _ => new BirthdaySettingsResponse
        {
            Enabled = false, Message = "Feliz día, {nombre}", DefaultMessage = "Feliz día, {nombre}", IsDefaultMessage = true,
            Variables = ["{nombre}", "{gym}"],
        };
        fake.Handlers["BirthdaySettingsPUTAsync"] = args =>
        {
            var body = (UpdateBirthdaySettingsRequest)args[0]!;
            return new BirthdaySettingsResponse
            {
                Enabled = body.Enabled, Message = body.Message ?? "Feliz día, {nombre}", DefaultMessage = "Feliz día, {nombre}",
                IsDefaultMessage = body.Message is null, Variables = ["{nombre}", "{gym}"],
            };
        };
        Services.AddSingleton(announcements);
        var whatsApp = DispatchProxy.Create<IWhatsAppClient, FakeClient>();
        ((FakeClient)(object)whatsApp).Handlers["TemplatesGETAsync"] = _ => new WhatsAppTemplatesResponse
        {
            Variables = ["{nombre}"], Templates = [],
        };
        Services.AddSingleton(whatsApp);
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudSnackbarProvider>();

        var tab = RenderComponent<CommunicationsTab>();

        tab.WaitForAssertion(() => Assert.NotNull(tab.Find("input[data-testid=birthday-enabled]")));
        tab.Find("input[data-testid=birthday-enabled]").Change(true);
        tab.Find("textarea[data-testid=birthday-message]").Change("¡Feliz cumple, {nombre}!");
        tab.Find("#save-birthday").Click();

        tab.WaitForAssertion(() =>
        {
            var call = fake.Calls.Last(c => c.Method == "BirthdaySettingsPUTAsync");
            var body = (UpdateBirthdaySettingsRequest)call.Args[0]!;
            Assert.True(body.Enabled);
            Assert.Equal("¡Feliz cumple, {nombre}!", body.Message);
        });
    }

    private IRenderedComponent<CheckIn> RenderReception(bool birthdayToday)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        var fake = (FakeClient)(object)proxy;
        fake.Handlers["ReceptionCardAsync"] = _ => new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            DaysLeft = 12, CanRegister = true, BirthdayToday = birthdayToday,
        };
        Services.AddSingleton(proxy);
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

    private static async Task Pick(IRenderedComponent<CheckIn> page)
    {
        await page.InvokeAsync(() => (Task)typeof(CheckIn)
            .GetMethod("OnMemberPickedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page.Instance, [Ana])!);
        page.Render();
    }
}
