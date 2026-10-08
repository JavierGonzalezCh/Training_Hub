using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Payments;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace GSM.Web.Tests;

public class RecordPaymentDialogTests : TestContext
{
    /// <summary>Cliente sin comportamiento: renderizar el diálogo no llama a la API.</summary>
    public class NoopClient : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    [Fact]
    public async Task Charge_dialog_offers_the_eight_methods_in_spec_order()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(DispatchProxy.Create<IPaymentsClient, NoopClient>());
        Services.AddSingleton(DispatchProxy.Create<IMembersClient, NoopClient>());
        Services.AddSingleton(DispatchProxy.Create<IMemberMembershipsClient, NoopClient>());

        var popover = RenderComponent<MudPopoverProvider>();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<RecordPaymentDialog>("Cobro"));

        // El selector con id propio es el de método (el autocompletar de miembro también lleva la clase mud-select).
        provider.Find("div.mud-select[id^=select] .mud-input-control").MouseDown();

        var labels = popover.FindAll(".mud-list-item").Select(i => i.TextContent.Trim()).ToList();
        Assert.Equal(
            ["Efectivo", "Tarjeta", "Nequi", "Daviplata", "Bre-B", "Transferencia bancaria", "Consignación", "Otro"],
            labels);
    }
}
