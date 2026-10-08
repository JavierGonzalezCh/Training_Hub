using System.Reflection;
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

    private static (T Client, FakeClient Fake) Fake<T>() where T : class
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

        var monthly = new PlanResponse { Id = Guid.NewGuid(), Name = "Mensual", Price = 50000, DurationDays = 30, IsActive = true };
        var quarterly = new PlanResponse { Id = Guid.NewGuid(), Name = "Trimestral", Price = 130000, DurationDays = 90, IsActive = true };
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
        membershipsFake.Handlers["AccessAsync"] = _ => new AccessStatusResponse
        {
            Commercial = "expired", Kind = "blocked", BlockReason = "expired", CanEnter = false,
            BlockMessage = "Membresía vencida el 7 oct, fuera de la gracia.",
        };

        await Pick(page, member);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Membresía vencida el 7 oct, fuera de la gracia.", page.Markup);
            Assert.Contains("Renovar y cobrar", page.Markup);
            Assert.Contains("Dejar pasar", page.Markup);
        });
        // No se registró nada todavía: recepción decide.
        Assert.DoesNotContain(attendanceFake.Calls, c => c.Method == "CheckinAsync");

        // "Dejar pasar" pide el motivo y lo manda en la petición.
        attendanceFake.Handlers["CheckinAsync"] = args => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = member.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, Authorized = true, MembershipExpired = true,
        };
        page.FindAll("button").First(b => b.TextContent.Contains("Dejar pasar")).Click();
        page.Find("#blocked-card input").Change("Paga mañana");
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
        membershipsFake.Handlers["AccessAsync"] = _ => new AccessStatusResponse { Commercial = "expiring", Kind = "expiring", CanEnter = true };
        attendanceFake.Handlers["CheckinAsync"] = _ => new CheckInResponse
        {
            AttendanceId = Guid.NewGuid(), MemberId = member.Id, MemberName = "Ana Prueba", Method = "Manual",
            CheckedInAt = DateTimeOffset.UtcNow, Notice = "Tu plan vence en 3 días (el 13 oct). Renueva en recepción para no perder tu fecha.",
        };

        await Pick(page, member);

        page.WaitForAssertion(() => Assert.Contains("Tu plan vence en 3 días", page.Find("#access-notice").TextContent));
        Assert.DoesNotContain("Dejar pasar", page.Markup);
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
        attendanceFake = a;
        membershipsFake = m;

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
