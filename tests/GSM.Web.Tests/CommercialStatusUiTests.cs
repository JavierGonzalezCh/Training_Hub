using System.Reflection;
using Bunit.TestDoubles;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Attendance;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>UI de MS1: asignar muestra inicio y fin y propone el plan anterior; recepción muestra el letrero y los botones.</summary>
public class CommercialStatusUiTests : TestContext
{
    /// <summary>Cliente de pruebas: cada método devuelve lo que diga el diccionario (por nombre) y, si no está, null/vacío.</summary>
    public class FakeClient : DispatchProxy
    {
        public Dictionary<string, Func<object?[], object?>> Handlers { get; } = [];

        public List<(string Method, object?[] Args)> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            Calls.Add((name, args ?? []));
            var result = Handlers.TryGetValue(name, out var handler) ? handler(args ?? []) : null;

            var returnType = targetMethod.ReturnType;
            if (!returnType.IsGenericType)
            {
                return Task.CompletedTask;
            }

            var inner = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
        }
    }

    internal static (T Client, FakeClient Fake) Fake<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, FakeClient>();
        return (proxy, (FakeClient)(object)proxy);
    }

    private static readonly DateOnly Start = new(2026, 10, 8);
    private static readonly DateOnly End = new(2026, 11, 6);

    [Fact]
    public async Task Assign_dialog_shows_start_and_end_and_proposes_the_previous_plan()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var monthly = new PlanResponse { Id = Guid.NewGuid(), Name = "Mensual", Price = 50000, DurationUnit = "days", DurationValue = 30, IsActive = true };
        var quarterly = new PlanResponse { Id = Guid.NewGuid(), Name = "Trimestral", Price = 130000, DurationUnit = "days", DurationValue = 90, IsActive = true };
        var memberId = Guid.NewGuid();

        var (plans, plansFake) = Fake<IMembershipPlansClient>();
        plansFake.Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse> { monthly, quarterly };
        var (memberships, membershipsFake) = Fake<IMemberMembershipsClient>();
        membershipsFake.Handlers["MembershipsAllAsync"] = _ => new List<MembershipResponse>
        {
            new() { Id = Guid.NewGuid(), PlanId = quarterly.Id, PlanName = "Trimestral", Status = "Active", StartDate = Start.AddDays(-90), EndDate = Start.AddDays(-1) },
        };
        membershipsFake.Handlers["PreviewAsync"] = _ => new RenewalPreviewResponse
        {
            Kind = "in_grace", SystemStartDate = Start, StartDate = Start, EndDate = End, PreviousEndDate = Start.AddDays(-1), StartDateChanged = false,
        };
        Services.AddSingleton(plans);
        Services.AddSingleton(memberships);
        Services.AddSingleton(Fake<IPaymentsClient>().Client);

        RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<AssignMembershipDialog> { { x => x.MemberId, memberId } };
        await provider.InvokeAsync(() => dialogs.ShowAsync<AssignMembershipDialog>("Asignar", parameters));

        provider.WaitForAssertion(() => Assert.Contains(
            $"Empieza el {GsmFormat.DayShort(Start)} · vence el {GsmFormat.DayShort(End)}", provider.Markup));

        // Propone el plan que ya tenía (el trimestral, no el primero de la lista) y la vista previa se pidió con él.
        var preview = membershipsFake.Calls.Last(c => c.Method == "PreviewAsync");
        Assert.Equal(quarterly.Id, preview.Args[1]);
        Assert.Contains("conserva su fecha de ciclo", provider.Markup);
    }

    [Fact]
    public async Task Reception_card_shows_the_block_message_with_renew_and_let_through_buttons()
    {
        var member = new MemberListItem { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active" };
        var page = Render(out var attendanceFake, out var membershipsFake);
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => new ReceptionCardResponse
        {
            MemberId = member.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "expired", Kind = "blocked",
            BlockMessage = "Membresía vencida el 7 oct, fuera de la gracia.", CanRegister = false, CanLetThrough = true,
            RejectionReason = "membership_expired",
        };

        await Pick(page, member);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Membresía vencida el 7 oct, fuera de la gracia.", page.Markup);
            Assert.Equal("No puede entrar", page.Find("#reception-verdict").TextContent.Trim());
            Assert.Contains("gsm-reception--danger", page.Find("#reception-card").ClassName);
            Assert.Contains("Renovar y cobrar", page.Markup);
            Assert.Contains("Dejar pasar", page.Markup);
        });

        // Lo que decide va arriba: el motivo y los botones antes de lo secundario, que queda plegado con su resumen.
        var markup = page.Find("#reception-card").InnerHtml;
        Assert.True(markup.IndexOf("block-message", StringComparison.Ordinal) < markup.IndexOf("reception-extras", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("Renovar y cobrar", StringComparison.Ordinal) < markup.IndexOf("reception-extras", StringComparison.Ordinal));
        Assert.Contains("Además: avisar por WhatsApp", page.Find("[data-testid='reception-extras'] summary").TextContent);
        // No se registró nada todavía: recepción decide.
        Assert.DoesNotContain(attendanceFake.Calls, c => c.Method == "CheckinAsync");

        // "Dejar pasar" pide el motivo y lo manda en la petición.
        attendanceFake.Handlers["CheckinAsync"] = args => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = member.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, Authorized = true, MembershipExpired = true,
        };
        page.FindAll("button").First(b => b.TextContent.Contains("Dejar pasar")).Click();
        page.Find("#reception-card input").Change("Paga mañana");
        page.FindAll("button").First(b => b.TextContent.Contains("Confirmar entrada")).Click();

        page.WaitForAssertion(() => Assert.Contains("Entrada autorizada por recepción", page.Markup));
        var request = (CheckInRequest)attendanceFake.Calls.Last(c => c.Method == "CheckinAsync").Args[0]!;
        Assert.Equal("Paga mañana", request.AuthorizationReason);
        Assert.Equal(member.Id, request.MemberId);
    }

    [Fact]
    public async Task Reception_card_shows_the_renewal_notice_when_the_plan_is_about_to_expire()
    {
        var member = new MemberListItem { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active" };
        var page = Render(out var attendanceFake, out var membershipsFake);
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => new ReceptionCardResponse
        {
            MemberId = member.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "expiring", Kind = "expiring",
            DaysLeft = 3, Notice = "Tu plan vence en 3 días (el 13 oct). Renueva en recepción para no perder tu fecha.",
            CanRegister = true,
        };
        attendanceFake.Handlers["CheckinAsync"] = _ => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = member.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, Notice = "Tu plan vence en 3 días (el 13 oct). Renueva en recepción para no perder tu fecha.",
        };

        await Pick(page, member);

        // Paso 1: la tarjeta con el letrero, redactado para recepción (no el "Tu plan…" del kiosco).
        page.WaitForAssertion(() => Assert.Equal(
            "Su plan vence en 3 días. Ofrécele renovar.", page.Find("#access-notice").TextContent.Trim()));
        Assert.Equal("Puede entrar", page.Find("#reception-verdict").TextContent.Trim());
        Assert.Contains("Ana Prueba: Puede entrar.", page.Find("#reception-announcer").TextContent);
        Assert.DoesNotContain("Dejar pasar", page.Markup);
        Assert.DoesNotContain(attendanceFake.Calls, c => c.Method == "CheckinAsync");

        // Paso 2: el botón registra; el resultado es de éxito y se anuncia.
        page.Find("#register-button").Click();
        page.WaitForAssertion(() => Assert.Contains("Entrada registrada", page.Find("#checkin-result").TextContent));
        Assert.Contains("gsm-reception--ok", page.Find("#checkin-result-card").ClassName);
        Assert.StartsWith("Entrada registrada: Ana Prueba", page.Find("#reception-announcer").TextContent.Trim());
    }

    [Fact]
    public async Task A_check_in_in_grace_is_a_success_with_a_note_for_reception()
    {
        var member = new MemberListItem { Id = Guid.NewGuid(), FirstName = "Ana", LastName = "Prueba", DocumentNumber = "1", DocumentType = "cc", Status = "Active" };
        var page = Render(out var attendanceFake, out _);
        attendanceFake.Handlers["ReceptionCardAsync"] = _ => new ReceptionCardResponse
        {
            MemberId = member.Id, MemberName = "Ana Prueba", MemberActive = true, Commercial = "grace", Kind = "grace",
            EndsOn = new DateOnly(2026, 10, 21), GraceDaysLeft = 2, CanRegister = true,
            Notice = "Tu plan venció el 21 oct. Tienes hasta el 24 oct para renovar sin perder tu fecha; después no podrás entrar.",
        };
        attendanceFake.Handlers["CheckinAsync"] = _ => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = member.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, MembershipExpired = true, MembershipEndsOn = new DateOnly(2026, 10, 21), GraceDaysLeft = 2,
        };

        await Pick(page, member);

        page.WaitForAssertion(() => Assert.Equal("Puede entrar · en gracia", page.Find("#reception-verdict").TextContent.Trim()));
        Assert.Equal(
            "Venció el 21 oct. Está en gracia: puede entrar 2 días más sin renovar. Ofrécele renovar.",
            page.Find("#access-notice").TextContent.Trim());

        page.Find("#register-button").Click();

        // Antes salía con borde rojo y triángulo de alerta aunque la entrada se había registrado.
        page.WaitForAssertion(() => Assert.Contains("gsm-reception--ok", page.Find("#checkin-result-card").ClassName));
        Assert.Contains("Está en gracia", page.Find("#access-notice").TextContent);
        Assert.DoesNotContain("Tu plan", page.Markup);
    }

    private IRenderedComponent<CheckIn> Render(out FakeClient attendanceFake, out FakeClient membershipsFake)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var (attendance, a) = Fake<IAttendanceClient>();
        var (memberships, m) = Fake<IMemberMembershipsClient>();
        Services.AddSingleton(attendance);
        Services.AddSingleton(memberships);
        Services.AddSingleton(Fake<IMembersClient>().Client);
        Services.AddSingleton(Fake<IPaymentsClient>().Client);
        Services.AddSingleton(Fake<IMembershipPlansClient>().Client);
        var (whatsApp, whatsAppFake) = Fake<IWhatsAppClient>();
        whatsAppFake.Handlers["StatesAsync"] = _ => new List<WhatsAppMemberStateResponse>();
        Services.AddSingleton(whatsApp);
        Services.AddSingleton(CommercialStatusUiTests.Fake<GSM.Client.Generated.ICashShiftsClient>().Client); // P3-4: recepción muestra la barra de caja
        attendanceFake = a;
        membershipsFake = m;

        this.AddTestAuthorization().SetAuthorized("staff");
        RenderComponent<MudPopoverProvider>();
        RenderComponent<MudDialogProvider>();
        return RenderComponent<CheckIn>();
    }

    // Invocado por reflexión (en la app lo dispara el autocompletar, que sí refresca el componente).
    private static async Task Pick(IRenderedComponent<CheckIn> page, MemberListItem member)
    {
        await page.InvokeAsync(() => (Task)typeof(CheckIn)
            .GetMethod("OnMemberPickedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page.Instance, [member])!);
        page.Render();
    }
}
