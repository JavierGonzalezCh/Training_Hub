using Bunit.TestDoubles;
using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>A1: la recepción tiene dos pasos (tarjeta y luego botón) y la tarjeta no ofrece lo que el servidor rechazaría.</summary>
public class ReceptionCardUiTests : TestContext
{
    private static readonly MemberListItem Ana = new()
    {
        Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active",
    };

    [Fact]
    public async Task Picking_a_member_shows_the_card_and_registers_nothing()
    {
        var page = Render(out var fake, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            DaysLeft = 12, MissingWaiver = true, CanRegister = true,
        });

        await Pick(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ana Prueba", page.Find("#reception-card").TextContent);
            Assert.Contains("Le quedan 12 días", page.Find("#days-left").TextContent);
            Assert.Contains("exoneración", page.Find("#missing-waiver").TextContent);
            Assert.NotNull(page.Find("#register-button"));
        });
        Assert.DoesNotContain(fake.Calls, c => c.Method == "CheckinAsync");
    }

    [Fact]
    public async Task Registering_sends_the_check_in_and_shows_the_time()
    {
        var page = Render(out var fake, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered", CanRegister = true,
        });
        fake.Handlers["CheckinAsync"] = _ => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = Ana.Id, MemberName = "Ana Prueba", Method = "Manual", CheckedInAt = DateTimeOffset.UtcNow,
            UnlockedAchievements = [new UnlockedAchievement { Name = "Primera visita" }],
        };

        await Pick(page);
        page.WaitForAssertion(() => page.Find("#register-button").Click());

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Entrada registrada", page.Find("#checkin-result").TextContent);
            Assert.Contains("Primera visita", page.Markup);
        });
        var request = (CheckInRequest)fake.Calls.Last(c => c.Method == "CheckinAsync").Args[0]!;
        Assert.Equal(Ana.Id, request.MemberId);
        Assert.Null(request.AuthorizationReason);
    }

    [Fact]
    public async Task A_member_who_already_entered_today_shows_the_time_and_no_buttons()
    {
        var page = Render(out _, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "current", Kind = "covered",
            AlreadyCheckedInToday = true, CheckedInAt = DateTimeOffset.UtcNow, CanRegister = false, CanLetThrough = false,
            RejectionReason = "already_checked_in",
        });

        await Pick(page);

        page.WaitForAssertion(() => Assert.Contains("Ya entró hoy a las", page.Find("#already-in").TextContent));
        Assert.DoesNotContain("Registrar entrada", page.Markup);
        Assert.DoesNotContain("Dejar pasar", page.Markup);
    }

    [Fact]
    public async Task A_closed_gym_offers_neither_register_nor_let_through()
    {
        var page = Render(out _, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "expired", Kind = "blocked",
            GymClosed = true, GymClosedMessage = "El gimnasio está cerrado.", CanRegister = false, CanLetThrough = false,
            RejectionReason = "gym_closed",
        });

        await Pick(page);

        page.WaitForAssertion(() => Assert.Contains("cerrado", page.Find("#gym-closed").TextContent));
        Assert.DoesNotContain("Registrar entrada", page.Markup);
        Assert.DoesNotContain("Dejar pasar", page.Markup);
    }

    [Fact]
    public async Task An_inactive_member_offers_no_let_through()
    {
        var page = Render(out _, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = false, Commercial = "current", Kind = "covered",
            CanRegister = false, CanLetThrough = false, RejectionReason = "member_inactive",
        });

        await Pick(page);

        page.WaitForAssertion(() => Assert.Contains("dado de baja", page.Find("#member-inactive").TextContent));
        Assert.DoesNotContain("Dejar pasar", page.Markup);
    }

    [Fact]
    public async Task A_frozen_membership_offers_renew_and_let_through_with_a_reason()
    {
        var page = Render(out var fake, new ReceptionCardResponse
        {
            MemberId = Ana.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "frozen", Kind = "blocked",
            BlockMessage = "Su membresía está congelada.", CanRegister = false, CanLetThrough = true, RejectionReason = "membership_frozen",
        });
        fake.Handlers["CheckinAsync"] = _ => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = Ana.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, Authorized = true, MembershipExpired = true,
        };

        await Pick(page);
        page.WaitForAssertion(() => Assert.Contains("congelada", page.Find("#block-message").TextContent));
        Assert.Contains("Renovar y cobrar", page.Markup);

        page.FindAll("button").First(b => b.TextContent.Contains("Dejar pasar")).Click();
        page.Find("#reception-card input").Change("Autorizado por el dueño");
        page.FindAll("button").First(b => b.TextContent.Contains("Confirmar entrada")).Click();

        page.WaitForAssertion(() => Assert.Contains("Entrada autorizada por recepción", page.Markup));
        var request = (CheckInRequest)fake.Calls.Last(c => c.Method == "CheckinAsync").Args[0]!;
        Assert.Equal("Autorizado por el dueño", request.AuthorizationReason);
    }

    [Fact]
    public void Reception_shows_the_cash_drawer_bar()
    {
        var page = Render(out _, new ReceptionCardResponse());

        Assert.NotEmpty(page.FindAll("[data-testid='cash-drawer-bar']"));
    }

    private IRenderedComponent<CheckIn> Render(out FakeClient attendanceFake, ReceptionCardResponse card)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var proxy = DispatchProxy.Create<IAttendanceClient, FakeClient>();
        attendanceFake = (FakeClient)(object)proxy;
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => card;
        Services.AddSingleton(proxy);
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
