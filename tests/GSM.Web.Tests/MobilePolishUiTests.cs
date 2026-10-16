using Bunit;
using Bunit.TestDoubles;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Plans;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GSM.Web.Tests;

/// <summary>AP7: las pantallas no diarias se apilan en el celular sin cambiar la vista de escritorio.</summary>
public class MobilePolishUiTests : TestContext
{
    [Fact]
    public void Plans_table_is_stackable_with_data_labels_for_mobile()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("ana");
        auth.SetRoles("owner");

        var plans = System.Reflection.DispatchProxy.Create<IMembershipPlansClient, CommercialStatusUiTests.FakeClient>();
        ((CommercialStatusUiTests.FakeClient)(object)plans).Handlers["MembershipPlansAllAsync"] = _ => new List<PlanResponse>
        {
            new() { Id = Guid.NewGuid(), Name = "Mensual", Price = 50000, DurationUnit = "days", DurationValue = 30, IsActive = true },
        };
        Services.AddSingleton(plans);

        var cut = RenderComponent<Index>();

        var table = cut.Find("[data-testid=stack-table]");
        Assert.Contains("gsm-table--stack", table.ClassList);
        Assert.NotEmpty(cut.FindAll("td[data-label=Nombre]"));
        Assert.NotEmpty(cut.FindAll("td[data-label=Precio]"));
    }
}
