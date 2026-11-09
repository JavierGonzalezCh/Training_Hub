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
    public async Task Charge_dialog_offers_four_methods_as_buttons_and_the_rest_under_Otros()
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

        var buttons = provider.FindAll("[data-testid='payment-methods'] button.mud-button-root").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(["Efectivo", "Nequi", "Tarjeta", "Transferencia bancaria"], buttons);

        provider.Find("div.mud-select[id^=select] .mud-input-control").MouseDown();
        var others = popover.FindAll(".mud-list-item").Select(i => i.TextContent.Trim()).ToList();
        Assert.Equal(["Daviplata", "Bre-B", "Consignación", "Otro"], others);
    }
}
