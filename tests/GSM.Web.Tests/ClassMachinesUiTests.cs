using System.Reflection;
using Bunit;
using GSM.Client.Generated;
using GSM.Panel.UI.Pages.Classes;
using GSM.UI.Shared.Formatting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using static GSM.Web.Tests.CommercialStatusUiTests;

namespace GSM.Web.Tests;

/// <summary>
/// C6: cupo ligado a máquinas. El editor de clase ofrece las máquinas del inventario (solo con cupo) y manda la lista;
/// el roster dice cuántas máquinas hay disponibles frente a las reservas, sin cancelar a nadie.
/// Nombres que NSwag debería generar: <c>SaveClassRequest/ClassResponse.EquipmentItemIds</c>,
/// <c>RosterResponse.EffectiveCapacity/AvailableMachines/LinkedMachines</c>, <c>IEquipmentClient.ListEquipmentAsync</c>.
/// </summary>
public class ClassMachinesUiTests : TestContext
{
    private static readonly Guid ClassId = Guid.NewGuid();
    private static readonly Guid BikeA = Guid.NewGuid();
    private static readonly Guid BikeB = Guid.NewGuid();

    private readonly FakeClient _classes;

    public ClassMachinesUiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var classes = DispatchProxy.Create<IClassesClient, FakeClient>();
        _classes = (FakeClient)(object)classes;
        _classes.Handlers["TrainersAsync"] = _ => new List<ClassInstructorOption>();
        Services.AddSingleton(classes);

        var equipment = DispatchProxy.Create<IEquipmentClient, FakeClient>();
        ((FakeClient)(object)equipment).Handlers["ListEquipmentAsync"] = _ => new List<EquipmentItemResponse>
        {
            new() { Id = BikeA, Name = "Bicicleta 1", Status = "Available", Quantity = 1, IsActive = true },
            new() { Id = BikeB, Name = "Bicicleta 2", Status = "Maintenance", Quantity = 1, IsActive = true },
        };
        Services.AddSingleton(equipment);
        RenderComponent<MudPopoverProvider>();
    }

    private IRenderedComponent<MudDialogProvider> OpenForm(int? capacity, List<Guid>? linked)
    {
        _classes.Handlers["ClassesGETAsync"] = _ => new ClassResponse
        {
            Id = ClassId, Name = "Spinning", IsActive = true, IsRecurring = true, Capacity = capacity,
            StartTime = new TimeOnly(7, 0), DurationMinutes = 45, DaysOfWeek = ["Monday"],
            EquipmentItemIds = linked ?? [],
        };
        _classes.Handlers["ClassesPUTAsync"] = _ => new ClassResponse();
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        provider.InvokeAsync(() => dialogs.ShowAsync<ClassFormDialog>(
            "Editar", new DialogParameters<ClassFormDialog> { { x => x.ClassId, ClassId } }));
        return provider;
    }

    [Fact]
    public void A_class_with_capacity_offers_the_inventory_machines()
    {
        var provider = OpenForm(capacity: 2, linked: [BikeA]);

        provider.WaitForAssertion(() => Assert.Single(provider.FindAll("#class-machines")));
        Assert.Contains(_classes.Calls, c => c.Method == "ClassesGETAsync");
    }

    [Fact]
    public void A_free_entry_class_does_not_offer_machines()
    {
        var provider = OpenForm(capacity: null, linked: null);

        provider.WaitForAssertion(() => Assert.Contains("Cupo", provider.Markup));
        Assert.Empty(provider.FindAll("#class-machines"));
    }

    [Fact]
    public void Saving_sends_the_linked_machines()
    {
        var provider = OpenForm(capacity: 2, linked: [BikeA]);
        provider.WaitForAssertion(() => Assert.Single(provider.FindAll("#class-machines")));

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        provider.WaitForAssertion(() =>
        {
            var call = Assert.Single(_classes.Calls, c => c.Method == "ClassesPUTAsync");
            var request = Assert.IsType<SaveClassRequest>(call.Args[1]);
            Assert.Equal(new[] { BikeA }, request.EquipmentItemIds);
        });
    }

    [Fact]
    public void Saving_a_free_entry_class_sends_an_empty_list_to_clear_the_links()
    {
        var provider = OpenForm(capacity: null, linked: [BikeA]);
        provider.WaitForAssertion(() => Assert.Contains("Cupo", provider.Markup));

        provider.FindAll("button").First(b => b.TextContent.Contains("Guardar")).Click();

        provider.WaitForAssertion(() =>
        {
            var call = Assert.Single(_classes.Calls, c => c.Method == "ClassesPUTAsync");
            var request = Assert.IsType<SaveClassRequest>(call.Args[1]);
            Assert.Empty(request.EquipmentItemIds!);
        });
    }

    [Fact]
    public void The_roster_says_how_many_machines_are_available_and_warns_when_reservations_exceed_them()
    {
        var provider = OpenRoster(booked: 2, effective: 1, available: 1, linked: 2);

        provider.WaitForAssertion(() =>
        {
            var text = provider.Find("#roster-machines").TextContent;
            Assert.Contains("2 reservas", text);
            Assert.Contains("1 máquina disponible", text);
            Assert.Contains("de 2 ligadas", text);
            Assert.Single(provider.FindAll(".roster-machines-warning"));
        });
    }

    [Fact]
    public void The_roster_has_no_warning_when_the_machines_cover_the_reservations()
    {
        var provider = OpenRoster(booked: 1, effective: 2, available: 2, linked: 2);

        provider.WaitForAssertion(() => Assert.Contains("2 máquinas disponibles", provider.Find("#roster-machines").TextContent));
        Assert.Empty(provider.FindAll(".roster-machines-warning"));
    }

    [Fact]
    public void A_roster_of_a_class_without_machines_shows_no_machine_summary()
    {
        var provider = OpenRoster(booked: 1, effective: null, available: null, linked: null);

        provider.WaitForAssertion(() => Assert.Contains("Miembro 0", provider.Markup));
        Assert.Empty(provider.FindAll("#roster-machines"));
    }

    private IRenderedComponent<MudDialogProvider> OpenRoster(int booked, int? effective, int? available, int? linked)
    {
        _classes.Handlers["RosterAsync"] = _ => new RosterResponse
        {
            ClassId = ClassId, ClassName = "Spinning", SessionDate = GsmFormat.Today(), StartTime = new TimeOnly(18, 0),
            Capacity = 2, SessionStatus = "Scheduled", BookedCount = booked,
            Entries = Enumerable.Range(0, booked).Select(i => new RosterEntry
            {
                BookingId = Guid.NewGuid(), MemberId = Guid.NewGuid(), MemberName = $"Miembro {i}", Status = "Booked",
            }).ToList(),
            EffectiveCapacity = effective, AvailableMachines = available, LinkedMachines = linked,
        };
        var provider = RenderComponent<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        provider.InvokeAsync(() => dialogs.ShowAsync<ClassRosterDialog>("Roster", new DialogParameters<ClassRosterDialog>
        {
            { x => x.ClassId, ClassId },
            { x => x.SessionDate, GsmFormat.Today() },
        }));
        return provider;
    }
}
