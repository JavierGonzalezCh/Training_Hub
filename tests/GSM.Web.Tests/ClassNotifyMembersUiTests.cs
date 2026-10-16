using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Classes;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>C8: "Avisar a los miembros" solo aparece al crear una clase, nunca al editarla.</summary>
public class ClassNotifyMembersUiTests : TestContext
{
    private readonly FakeClient _fake;

    public ClassNotifyMembersUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var client = DispatchProxy.Create<IClassesClient, FakeClient>();
        _fake = (FakeClient)(object)client;
        _fake.Handlers["TrainersAsync"] = _ => new List<ClassInstructorOption>();
        Services.AddSingleton(client);
        Services.AddSingleton(DispatchProxy.Create<IEquipmentClient, FakeClient>());
        RenderComponent<MudPopoverProvider>();
    }

    [Fact]
    public async Task New_class_form_offers_the_notify_switch()
    {
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ClassFormDialog>("Nueva clase"));

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindAll("#class-notify-members")));
    }

    [Fact]
    public async Task Editing_a_class_does_not_offer_the_notify_switch()
    {
        var classId = Guid.NewGuid();
        _fake.Handlers["ClassesGETAsync"] = _ => new ClassResponse
        {
            Id = classId, Name = "Yoga", IsActive = true, IsRecurring = true,
            StartTime = new TimeOnly(8, 0), DurationMinutes = 45, DaysOfWeek = ["Tuesday"],
        };
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<ClassFormDialog>(
            "Editar", new DialogParameters<ClassFormDialog> { { x => x.ClassId, classId } }));

        provider.WaitForAssertion(() => Assert.Single(provider.FindAll("#class-instructor")));
        Assert.Empty(provider.FindAll("#class-notify-members"));
    }
}
