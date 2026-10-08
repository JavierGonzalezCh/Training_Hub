using System.Globalization;
using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Members;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>B3: el precio en COP se ve igual en toda la UI, sea cual sea la cultura del hilo (en el navegador es la del usuario).</summary>
public class MoneyFormatUiTests : TestContext
{
    public class PlansProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "MembershipPlansAllAsync")
            {
                ICollection<PlanResponse> plans =
                [
                    new PlanResponse { Id = Guid.NewGuid(), Name = "Mensual", Price = 1100000, DurationDays = 30, IsActive = true },
                ];
                return Task.FromResult(plans);
            }

            return null;
        }
    }

    public class NoopClient : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    [Fact]
    public async Task Plan_select_and_amount_field_use_the_colombian_money_format_under_any_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices();
            Services.AddSingleton(DispatchProxy.Create<IMembershipPlansClient, PlansProxy>());
            Services.AddSingleton(DispatchProxy.Create<IMemberMembershipsClient, NoopClient>());
            Services.AddSingleton(DispatchProxy.Create<IPaymentsClient, NoopClient>());

            RenderComponent<MudPopoverProvider>();
            var provider = RenderComponent<MudDialogProvider>();
            var dialogs = Services.GetRequiredService<IDialogService>();
            var parameters = new DialogParameters { ["MemberId"] = Guid.NewGuid() };
            await provider.InvokeAsync(() => dialogs.ShowAsync<AssignMembershipDialog>("Asignar", parameters));
            provider.WaitForState(() => provider.FindAll("input").Count > 0);

            var expected = GsmFormat.Money(1100000m);
            var inputs = provider.FindAll("input").Select(i => i.GetAttribute("value") ?? "").ToList();
            Assert.Contains(inputs, v => v.Contains(expected));
            Assert.Contains(inputs, v => v == "1.100.000" || v == expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
